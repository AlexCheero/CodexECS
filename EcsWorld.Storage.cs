using System;
using System.Runtime.CompilerServices;
#if ENABLE_IL2CPP
using Unity.IL2CPP.CompilerServices;
#endif

namespace CodexECS
{
    // Inverted storage: entities are slots in 64-slot pages, and every component type keeps one membership
    // word per page (ComponentStorage). Filters intersect those words; nothing is stored per entity except
    // its version and component count, so adding or removing a component never moves anything.
#if ENABLE_IL2CPP
    [Il2CppSetOption(Option.NullChecks, false)]
    [Il2CppSetOption(Option.ArrayBoundsChecks, false)]
#endif
    public partial class EcsWorld
    {
        // Indexed by ComponentMeta<T>.Id; null until the component is used in this world.
        internal ComponentStorage[] Storages = Array.Empty<ComponentStorage>();
        // Alive slots: the base set of filters without includes, and the page occupancy.
        internal readonly SlotSet Alive;

        private int _pages;
        private int _pagesUsed;
        // Pages ever handed out; filters scan below this.
        internal int PageCount => _pagesUsed;

        // Per slot: the version of its current or next occupant (32 bits) and its number of components.
        internal uint[] Versions = Array.Empty<uint>();
        private ushort[] _componentCounts = Array.Empty<ushort>();

        // Pages belong to allocation groups chosen by an exact component mask (GroupGraph). A page is only
        // slot bookkeeping: component data lives in each component's own storage at the same slot.
        private struct PageInfo
        {
            // Slot 0 of page 0 is never handed out, so id 0 stays the null id.
            public ulong Reserved;
            public int Group;
            // Links in the owning group's list of pages with free slots, or in the stack of empty pages.
            public int Next, Previous;
            // Every component type any entity of the page had since the page was last empty:
            // Delete cleans only these types instead of every registered one.
            public ulong[] Types;
        }

        private PageInfo[] _pageInfo = Array.Empty<PageInfo>();
        // Per group: first page with a free slot (-1 = none).
        private int[] _groupHeads = { -1 };
        // Pages without live entities, reusable by any group.
        private int _emptyPages = -1;
        private readonly GroupGraph _groups = new GroupGraph();
        private int _live;

        public int EntitiesCount => _live;
        internal int GroupCount => _groups.Count;

        // Slots ever handed out cover [0, SlotCapacity).
        internal int SlotCapacity => _pagesUsed << Layout.PageShift;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool IsAliveId(int id) => (uint)id < (uint)Versions.Length && Alive.Has(id);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ComponentStorage GetStorage(int typeId)
        {
            var storages = Storages;
            if ((uint)typeId < (uint)storages.Length)
            {
                var storage = storages[typeId];
                if (storage != null)
                    return storage;
            }
            return CreateStorage(typeId);
        }

        // Null when the component was never used in this world.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ComponentStorage FindStorage(int typeId)
        {
            var storages = Storages;
            return (uint)typeId < (uint)storages.Length ? storages[typeId] : null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ComponentsPool<T> Pool<T>() => (ComponentsPool<T>)GetStorage(ComponentMeta<T>.Id);

        private ComponentStorage CreateStorage(int typeId)
        {
#if DEBUG && !ECS_PERF_TEST
            if (!PoolFactory.FactoryMethods.ContainsIdx(typeId))
                throw new EcsException($"component id {typeId} is not registered");
#endif
            if (typeId >= Storages.Length)
                Array.Resize(ref Storages, Math.Max(typeId + 1, Math.Max(16, Storages.Length * 2)));
            var storage = PoolFactory.FactoryMethods[typeId](this, typeId);
            storage.Grow(_pages);
            Storages[typeId] = storage;
            return storage;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void OnComponentAdded(int id, int typeId)
        {
            _componentCounts[id]++;
            ref var types = ref _pageInfo[id >> Layout.PageShift].Types;
            int word = typeId >> 6;
            if (types == null || word >= types.Length)
                Array.Resize(ref types, Math.Max(word + 1, 8));
            types[word] |= 1UL << typeId;
        }

        // Adds a value, or replaces the value of an owned component (an error in DEBUG).
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddInternal<T>(int id, T component)
        {
            var typeId = ComponentMeta<T>.Id;
            var storage = GetStorage(typeId);
            if (storage.Has(id))
            {
#if DEBUG && !ECS_PERF_TEST
                throw new EcsException($"entity {id} already has {typeof(T).Name}");
#else
                if (!ComponentMeta<T>.IsTag)
                    ((ComponentsPool<T>)storage)[id] = component;
                return;
#endif
            }

            if (ComponentMeta<T>.IsTag)
                storage.Set(id);
            else
                ((ComponentsPool<T>)storage).AddValue(id, component);
            OnComponentAdded(id, typeId);
        }

        // Runs Cleanup. False when the entity does not own the component (an error in DEBUG).
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool RemoveInternal(int typeId, int id)
        {
            var storage = FindStorage(typeId);
            if (storage == null || !storage.Has(id))
            {
#if DEBUG && !ECS_PERF_TEST
                throw new EcsException($"entity {id} has no component with id {typeId}");
#else
                return false;
#endif
            }
            storage.Remove(id);
            _componentCounts[id]--;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RemoveInternal<T>(int id) => RemoveInternal(ComponentMeta<T>.Id, id);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool HasNoComponents(int id) => _componentCounts[id] == 0;

        // A free slot of the group: the lowest free slot of the group's first page with free slots, else an
        // empty page, else a new page. Freed slots are reused before never-used ones and live entities stay
        // packed at the start of each page.
        private int Allocate(int group)
        {
            int p = _groupHeads[group];
            if (p < 0)
                p = AcquirePage(group);
            ref var page = ref _pageInfo[p];
            ulong used = Alive.Words[p] | page.Reserved;
            int local = Bits.TrailingZeros(~used);
            int id = (p << Layout.PageShift) | local;
            Alive.Set(id);
            if ((used | (1UL << local)) == ulong.MaxValue)
                Detach(p);
            _live++;
            return id;
        }

        private int AcquirePage(int group)
        {
            int p;
            if (_emptyPages >= 0)
            {
                p = _emptyPages;
                _emptyPages = _pageInfo[p].Next;
            }
            else
            {
                if (_pagesUsed == _pages)
                    Grow();
                p = _pagesUsed++;
                if (p == 0)
                    _pageInfo[0].Reserved = 1;
            }
            _pageInfo[p].Group = group;
            Attach(p);
            return p;
        }

        // The slot's components must already be removed.
        private void FreeSlot(int id)
        {
            int p = id >> Layout.PageShift;
            ref var page = ref _pageInfo[p];
            // A full page is in no list; it rejoins its group once it has a free slot again.
            bool wasFull = (Alive.Words[p] | page.Reserved) == ulong.MaxValue;
            Alive.Unset(id);
            _live--;
            _componentCounts[id] = 0;
            Versions[id] = unchecked(Versions[id] + 1);
            if (Alive.Words[p] == 0)
            {
                // A page without live entities leaves its group and can serve any group; versions stay.
                if (!wasFull)
                    Detach(p);
                page.Group = -1;
                page.Previous = -1;
                page.Next = _emptyPages;
                _emptyPages = p;
                if (page.Types != null)
                    Array.Clear(page.Types, 0, page.Types.Length);
            }
            else if (wasFull)
            {
                Attach(p);
            }
        }

        private void Attach(int p)
        {
            ref var page = ref _pageInfo[p];
            page.Previous = -1;
            page.Next = _groupHeads[page.Group];
            if (page.Next >= 0)
                _pageInfo[page.Next].Previous = p;
            _groupHeads[page.Group] = p;
        }

        private void Detach(int p)
        {
            ref var page = ref _pageInfo[p];
            if (page.Previous < 0)
                _groupHeads[page.Group] = page.Next;
            else
                _pageInfo[page.Previous].Next = page.Next;
            if (page.Next >= 0)
                _pageInfo[page.Next].Previous = page.Previous;
            page.Next = page.Previous = -1;
        }

        private int GroupFor(in BitMask mask)
        {
            var node = _groups.Get(in mask);
            if (node.Id >= _groupHeads.Length)
            {
                int old = _groupHeads.Length;
                Array.Resize(ref _groupHeads, Math.Max(node.Id + 1, old * 2));
                for (int i = old; i < _groupHeads.Length; i++)
                    _groupHeads[i] = -1;
            }
            return node.Id;
        }

        // Removes every component (Cleanup runs, no reactions) and frees the slot.
        private void DestroyNow(int id)
        {
            var types = _pageInfo[id >> Layout.PageShift].Types;
            if (types != null && _componentCounts[id] != 0)
            {
                var storages = Storages;
                for (int w = 0; w < types.Length; w++)
                {
                    ulong bits = types[w];
                    while (bits != 0)
                    {
                        int typeId = (w << 6) | Bits.TrailingZeros(bits);
                        bits &= bits - 1;
                        var storage = storages[typeId];
                        if (storage != null && storage.Has(id))
                            storage.Remove(id);
                    }
                }
            }
            FreeSlot(id);
        }

        private void Grow()
        {
            // Whole chunks, so chunk summaries never need a length check. Payload arrays never move;
            // replaced word and directory arrays are read afresh by running loops on each page.
            _pages = Math.Max(64, _pages * 2);
            int slots = _pages << Layout.PageShift;
            Array.Resize(ref Versions, slots);
            Array.Resize(ref _componentCounts, slots);
            Array.Resize(ref _pageInfo, _pages);
            Alive.Grow(_pages);
            foreach (var storage in Storages)
                storage?.Grow(_pages);
        }

        // Every component of one entity, by testing each registered type: meant for inspectors and debugging.
        public void CollectMask(int id, ref BitMask mask)
        {
            mask.Clear();
            if (!IsAliveId(id))
                return;
            var storages = Storages;
            for (int typeId = 0; typeId < storages.Length; typeId++)
            {
                var storage = storages[typeId];
                if (storage != null && storage.Has(id))
                    mask.Set(typeId);
            }
        }
    }
}
