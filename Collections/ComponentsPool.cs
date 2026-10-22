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
