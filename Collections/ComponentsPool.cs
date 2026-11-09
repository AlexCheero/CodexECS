using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
#if ENABLE_IL2CPP
using Unity.IL2CPP.CompilerServices;
#endif

namespace CodexECS
{
    // Which slots own one component type in one world: one word per 64-slot page and one summary word per
    // chunk of 64 pages (bit = page whose word is nonzero). Tags are this alone; data components add payload.
    // Element access is unchecked under IL2CPP: every id and page passed in is below the world's capacity,
    // which all storages of a world share.
#if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
#endif
    public abstract class ComponentStorage
    {
        internal readonly EcsWorld World;
        internal readonly int TypeId;
        internal ulong[] Words = Array.Empty<ulong>();
        internal ulong[] NonEmpty = Array.Empty<ulong>();
        private int _count;

        internal ComponentStorage(EcsWorld world, int typeId)
        {
            World = world;
            TypeId = typeId;
        }

        // Number of entities that own the component.
        public int Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _count;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Has(int id) => (Words[id >> Layout.PageShift] & (1UL << id)) != 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(int id) => Has(id);

        /// <summary>
        /// Borrows the current membership directory without copying entity data.
        /// Capture on the world owner thread, and finish all readers before any
        /// structural mutation. Recapture after the world may have grown.
        /// </summary>
        public ComponentMembershipPages CaptureMembershipPages() => new ComponentMembershipPages(Words, NonEmpty);

        // The bit must be clear.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Set(int id)
        {
            int page = id >> Layout.PageShift;
            Words[page] |= 1UL << id;
            NonEmpty[page >> 6] |= 1UL << page;
            _count++;
        }

        // The bit must be set.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Unset(int id)
        {
            int page = id >> Layout.PageShift;
            ref ulong word = ref Words[page];
            word &= ~(1UL << id);
            if (word == 0)
                NonEmpty[page >> 6] &= ~(1UL << page);
            _count--;
        }

        internal virtual void Grow(int pages)
        {
            Array.Resize(ref Words, pages);
            Array.Resize(ref NonEmpty, (pages + 63) >> 6);
        }

        // First owner in slot order, or -1.
        internal int First()
        {
            var nonEmpty = NonEmpty;
            for (int chunk = 0; chunk < nonEmpty.Length; chunk++)
            {
                ulong pages = nonEmpty[chunk];
                if (pages == 0)
                    continue;
                int page = (chunk << 6) | Bits.TrailingZeros(pages);
                return (page << Layout.PageShift) | Bits.TrailingZeros(Words[page]);
            }
            return -1;
        }

        // ---- state capture and restore (EcsWorld.CaptureState / RestoreState) ----

        internal virtual StorageState CreateState() => new StorageState();

        internal void CaptureMembership(StorageState state, int pages)
        {
            state.EnsurePages(pages);
            Array.Copy(Words, state.Words, pages);
            state.Count = _count;
        }

        // Writes the first `pages` words, clears the rest, and rebuilds the chunk summaries.
        internal void RestoreMembership(ulong[] words, int pages, int count)
        {
            Array.Copy(words, Words, pages);
            if (Words.Length > pages)
                Array.Clear(Words, pages, Words.Length - pages);
            RecomputeNonEmpty();
            _count = count;
        }

        internal virtual void CapturePayload(StorageState state, int pages) { }

        internal virtual void RestorePayload(StorageState state, int pages) { }

        // Removes (with Cleanup, without reactions) every owner whose bit is clear in keep[page];
        // pages at or beyond keepPages lose all owners.
        internal void Prune(ulong[] keep, int keepPages, int totalPages)
        {
            int limit = Math.Min(totalPages, Words.Length);
            for (int p = 0; p < limit; p++)
            {
                ulong remove = Words[p] & ~(p < keepPages ? keep[p] : 0UL);
                while (remove != 0)
                {
                    int id = (p << Layout.PageShift) | Bits.TrailingZeros(remove);
                    remove &= remove - 1;
                    Remove(id);
                }
            }
        }

        private void RecomputeNonEmpty()
        {
            Array.Clear(NonEmpty, 0, NonEmpty.Length);
            for (int p = 0; p < Words.Length; p++)
            {
                if (Words[p] != 0)
                    NonEmpty[p >> 6] |= 1UL << (p & 63);
            }
        }

        public abstract bool IsTag { get; }
        public abstract Type GetComponentType();
        public abstract string DebugString(int id, bool printFields);

        // Type-erased lifecycle. The bit must be clear for AddDefault and set for Remove.
        // AddDefault stores the metadata default (static Default + Init); Remove runs Cleanup.
        internal abstract void AddDefault(int id);
        internal abstract void Remove(int id);
    }

    // Membership with a value per owner. Values live in 256-slot arrays that are allocated on first use and
    // never move or grow, so refs to them stay valid across any number of adds; only the directory grows.
#if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
#endif
    public sealed class ComponentsPool<T> : ComponentStorage
    {
        internal T[][] Segments = Array.Empty<T[]>();

        internal ComponentsPool(EcsWorld world, int typeId) : base(world, typeId) { }

        public override bool IsTag => false;
        public override Type GetComponentType() => typeof(T);

        internal override void Grow(int pages)
        {
            base.Grow(pages);
            Array.Resize(ref Segments, (pages << Layout.PageShift) >> Layout.SegmentShift);
        }

        // The entity must own the component. Unchecked in release, checked in DEBUG.
        public ref T this[int id]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
#if DEBUG && !ECS_PERF_TEST
                if (!Has(id))
                    throw new EcsException($"entity {id} has no {typeof(T).Name}");
#endif
                return ref Segments[id >> Layout.SegmentShift][id & Layout.SegmentMask];
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T Get(int id) => ref this[id];

        /// <summary>
        /// Borrows membership and payload directories for a synchronous phase.
        /// Workers may write disjoint owned slots only. The caller must complete
        /// every worker before resuming world mutation or publishing commands.
        /// </summary>
        public ComponentPayloadPages<T> CapturePayloadPages() => new ComponentPayloadPages<T>(Words, NonEmpty, Segments);

        // The bit must be clear.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void AddValue(int id, in T value)
        {
            ref var segment = ref Segments[id >> Layout.SegmentShift];
            if (segment == null)
                segment = new T[Layout.SegmentSize];
            segment[id & Layout.SegmentMask] = value;
            Set(id);
        }

        internal override void AddDefault(int id) => AddValue(id, ComponentMeta<T>.GetDefault());

        internal override void Remove(int id)
        {
            ComponentMeta<T>.Cleanup(ref Segments[id >> Layout.SegmentShift][id & Layout.SegmentMask]);
            Unset(id);
        }

        internal override StorageState CreateState() => new StorageState<T>();

        // Only pages with owners are copied; payload entries of slots without the component are stale.
        internal override void CapturePayload(StorageState state, int pages)
        {
#if DEBUG && !ECS_PERF_TEST
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
                throw new EcsException($"{typeof(T).Name} holds references and cannot be captured into a WorldState");
#endif
            var typed = (StorageState<T>)state;
            for (int p = 0; p < pages; p++)
            {
                if (Words[p] == 0)
                    continue;
                T[] segment = Segments[p >> (Layout.SegmentShift - Layout.PageShift)];
                int offset = (p << Layout.PageShift) & Layout.SegmentMask;
                Array.Copy(segment, offset, typed.Payload, p << Layout.PageShift, 1 << Layout.PageShift);
            }
        }

        // Membership must already be restored. Segments missing in this world are allocated.
        internal override void RestorePayload(StorageState state, int pages)
        {
            var typed = (StorageState<T>)state;
            for (int p = 0; p < pages; p++)
            {
                if (typed.Words[p] == 0)
                    continue;
                ref T[] segment = ref Segments[p >> (Layout.SegmentShift - Layout.PageShift)];
                if (segment == null)
                    segment = new T[Layout.SegmentSize];
                int offset = (p << Layout.PageShift) & Layout.SegmentMask;
                Array.Copy(typed.Payload, p << Layout.PageShift, segment, offset, 1 << Layout.PageShift);
            }
        }

        private StringBuilder _debugStringBuilder;
        public override string DebugString(int id, bool printFields)
        {
            _debugStringBuilder ??= new StringBuilder();
            _debugStringBuilder.Append(typeof(T).Name);
            if (printFields)
            {
                var value = this[id];
                var fields = value?.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ??
                             Array.Empty<FieldInfo>();
                _debugStringBuilder.Append(fields.Length > 0 ? ":" : " {}");
                foreach (var field in fields)
                {
                    var fieldValue = field.GetValue(value);
                    _debugStringBuilder.Append("\n\t").Append(field.Name).Append(": ")
                        .Append(fieldValue != null ? fieldValue.ToString() : "null").Append(", ");
                }
                if (fields.Length > 0)
                    _debugStringBuilder.Remove(_debugStringBuilder.Length - 2, 2);
            }

            var result = _debugStringBuilder.ToString();
            _debugStringBuilder.Clear();
            return result;
        }
    }

    // A component without fields: membership only.
    public sealed class TagsPool<T> : ComponentStorage
    {
        internal TagsPool(EcsWorld world, int typeId) : base(world, typeId) { }

        public override bool IsTag => true;
        public override Type GetComponentType() => typeof(T);
        public override string DebugString(int id, bool printFields) => typeof(T).Name;

        internal override void AddDefault(int id) => Set(id);
        internal override void Remove(int id) => Unset(id);
    }

    // The world's alive slots, the base set of filters without includes.
    internal sealed class SlotSet : ComponentStorage
    {
        internal SlotSet(EcsWorld world) : base(world, -1) { }

        public override bool IsTag => true;
        public override Type GetComponentType() => null;
        public override string DebugString(int id, bool printFields) => "alive";
        internal override void AddDefault(int id) => Set(id);
        internal override void Remove(int id) => Unset(id);
    }
}
