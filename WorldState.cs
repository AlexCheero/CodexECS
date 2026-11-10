using System;

namespace CodexECS
{
    /// <summary>
    /// A copy of an <see cref="EcsWorld"/>'s slot bookkeeping and of a chosen set of component
    /// storages, written by <see cref="EcsWorld.CaptureState"/> and read back by
    /// <see cref="EcsWorld.RestoreState"/>. Buffers grow on capture and never shrink, so capturing into
    /// a reused instance allocates nothing once warm. Only storages of unmanaged components can be
    /// captured: payloads are copied as plain values.
    ///
    /// The read-only views let callers hash, compare and serialize a state without touching the
    /// world. Payload entries of slots whose membership bit is clear are stale and must be ignored.
    /// </summary>
    public sealed partial class WorldState
    {
        internal struct PageRecord
        {
            public ulong Reserved;
            public int Group;
            public int Next;
            public int Previous;
        }

        internal ulong[] AliveWordsArray = Array.Empty<ulong>();
        internal int AliveCount;
        internal uint[] VersionsArray = Array.Empty<uint>();
        internal PageRecord[] Pages = Array.Empty<PageRecord>();
        internal ulong[][] PageTypes = Array.Empty<ulong[]>();
        internal int[] PageTypesLength = Array.Empty<int>();
        internal int[] GroupHeads = Array.Empty<int>();
        internal int GroupHeadCount;
        internal int EmptyPages;
        internal int Live;
        internal int PagesUsed;
        internal int[] ComponentIdArray = Array.Empty<int>();
        internal StorageState[] StorageStates = Array.Empty<StorageState>();
        internal int ComponentCount;
        internal bool Captured;

        public bool IsCaptured => Captured;

        /// <summary>Pages the captured world had handed out; slots are PageCount × 64.</summary>
        public int PageCount => PagesUsed;

        public int SlotCount => PagesUsed << Layout.PageShift;

        public int EntitiesCount => Live;

        /// <summary>Component ids in the order they were passed to CaptureState.</summary>
        public ReadOnlySpan<int> ComponentIds => new ReadOnlySpan<int>(ComponentIdArray, 0, ComponentCount);

        /// <summary>One word per page; bit i set means slot (page × 64 + i) is alive.</summary>
        public ReadOnlySpan<ulong> AliveWords => new ReadOnlySpan<ulong>(AliveWordsArray, 0, PagesUsed);

        /// <summary>Version of every slot below SlotCount, alive or not.</summary>
        public ReadOnlySpan<uint> Versions => new ReadOnlySpan<uint>(VersionsArray, 0, SlotCount);

        public int IndexOf(int componentId)
        {
            for (int i = 0; i < ComponentCount; i++)
            {
                if (ComponentIdArray[i] == componentId)
                    return i;
            }
            return -1;
        }

        /// <summary>Membership words of a captured component, one per page.</summary>
        public ReadOnlySpan<ulong> MembershipWords(int componentId)
        {
            return new ReadOnlySpan<ulong>(StateFor(componentId).Words, 0, PagesUsed);
        }

        /// <summary>Number of slots owning the component at capture time.</summary>
        public int ComponentCountOf(int componentId) => StateFor(componentId).Count;

        /// <summary>
        /// The 64 payload values of one page of a captured data component. Entries whose membership
        /// bit is clear are stale. Throws when T is not the component's type or the component is a tag.
        /// </summary>
        public ReadOnlySpan<T> Payload<T>(int componentId, int page)
        {
            if (!(StateFor(componentId) is StorageState<T> typed))
                throw new EcsException($"component {componentId} is not a data component of type {typeof(T).Name}");
            return new ReadOnlySpan<T>(typed.Payload, page << Layout.PageShift, 1 << Layout.PageShift);
        }

        private StorageState StateFor(int componentId)
        {
            int index = IndexOf(componentId);
            if (index < 0)
                throw new EcsException($"component {componentId} is not part of this state");
            return StorageStates[index];
        }

        // Sizes every buffer for a capture of `pages` pages and the given component set.
        internal void Begin(int pages, ReadOnlySpan<int> componentIds, EcsWorld world)
        {
            Captured = false;
            PagesUsed = pages;
            StateArrays.EnsureLength(ref AliveWordsArray, pages);
            StateArrays.EnsureLength(ref VersionsArray, pages << Layout.PageShift);
            StateArrays.EnsureLength(ref Pages, pages);
            StateArrays.EnsureLength(ref PageTypes, pages);
            StateArrays.EnsureLength(ref PageTypesLength, pages);

            bool sameSet = componentIds.Length == ComponentCount;
            for (int i = 0; sameSet && i < componentIds.Length; i++)
                sameSet = componentIds[i] == ComponentIdArray[i];
            if (!sameSet)
            {
                ComponentCount = componentIds.Length;
                StateArrays.EnsureLength(ref ComponentIdArray, ComponentCount);
                StorageStates = new StorageState[ComponentCount];
                for (int i = 0; i < ComponentCount; i++)
                {
                    ComponentIdArray[i] = componentIds[i];
                    StorageStates[i] = world.GetStorage(componentIds[i]).CreateState();
                }
            }
            for (int i = 0; i < ComponentCount; i++)
                StorageStates[i].EnsurePages(pages);
        }
    }

    internal static class StateArrays
    {
        internal static void EnsureLength<T>(ref T[] array, int length)
        {
            if (array.Length < length)
                Array.Resize(ref array, Math.Max(length, array.Length * 2));
        }
    }

    /// <summary>Captured membership of one storage; data components add a payload (see the generic subclass).</summary>
    internal class StorageState
    {
        internal ulong[] Words = Array.Empty<ulong>();
        internal int Count;

        internal virtual void EnsurePages(int pages) => StateArrays.EnsureLength(ref Words, pages);

        /// <summary>Bytes per payload element; 0 for tags.</summary>
        internal virtual int ElementSize => 0;

        /// <summary>Raw bytes of one page of payload (64 elements). Empty for tags.</summary>
        internal virtual void CopyPageOut(int page, Span<byte> destination) { }

        internal virtual void CopyPageIn(int page, ReadOnlySpan<byte> source) { }
    }

    internal sealed class StorageState<T> : StorageState
    {
        private static readonly int Stride = ComputeStride();

        internal T[] Payload = Array.Empty<T>();

        internal override void EnsurePages(int pages)
        {
            base.EnsurePages(pages);
            StateArrays.EnsureLength(ref Payload, pages << Layout.PageShift);
        }

        internal override int ElementSize => Stride;

        internal override void CopyPageOut(int page, Span<byte> destination)
        {
            int bytes = Stride << Layout.PageShift;
            var handle = System.Runtime.InteropServices.GCHandle.Alloc(Payload, System.Runtime.InteropServices.GCHandleType.Pinned);
            try
            {
                IntPtr start = System.Runtime.InteropServices.Marshal.UnsafeAddrOfPinnedArrayElement(Payload, page << Layout.PageShift);
                for (int i = 0; i < bytes; i++)
                    destination[i] = System.Runtime.InteropServices.Marshal.ReadByte(start, i);
            }
            finally
            {
                handle.Free();
            }
        }

        internal override void CopyPageIn(int page, ReadOnlySpan<byte> source)
        {
            int bytes = Stride << Layout.PageShift;
            var handle = System.Runtime.InteropServices.GCHandle.Alloc(Payload, System.Runtime.InteropServices.GCHandleType.Pinned);
            try
            {
                IntPtr start = System.Runtime.InteropServices.Marshal.UnsafeAddrOfPinnedArrayElement(Payload, page << Layout.PageShift);
                for (int i = 0; i < bytes; i++)
                    System.Runtime.InteropServices.Marshal.WriteByte(start, i, source[i]);
            }
            finally
            {
                handle.Free();
            }
        }

        // Managed stride of T without a generic constraint: the distance between two pinned elements.
        private static int ComputeStride()
        {
            var probe = new T[2];
            var handle = System.Runtime.InteropServices.GCHandle.Alloc(probe, System.Runtime.InteropServices.GCHandleType.Pinned);
            try
            {
                IntPtr first = System.Runtime.InteropServices.Marshal.UnsafeAddrOfPinnedArrayElement(probe, 0);
                IntPtr second = System.Runtime.InteropServices.Marshal.UnsafeAddrOfPinnedArrayElement(probe, 1);
                return (int)((long)second - (long)first);
            }
            catch (ArgumentException)
            {
                return 0; // holds references: cannot be pinned, and cannot be captured either
            }
            finally
            {
                if (handle.IsAllocated)
                    handle.Free();
            }
        }
    }
}
