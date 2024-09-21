using System.Collections.Generic;
using CodexECS.Utility;
using System.Runtime.CompilerServices;
using EntityType = System.Int32;//duplicated in EntityExtension

#if DEBUG
using System.Linq;
#endif

namespace CodexECS
{
    class ArchetypesManager
    {
        private Archetype[] _eToA; //entity to archerype mapping
        private EntityType _eToACount;
        private Dictionary<BitMask, Archetype> _mToA; //mask to archetype mapping
        private Dictionary<FilterMasks, EcsFilter> _filters;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HaveEntity(EntityType eid) => eid < _eToACount && _eToA[eid] != null;

        public ArchetypesManager()
        {
            _eToA = new Archetype[2];
            _eToACount = 0;
            _mToA = new Dictionary<BitMask, Archetype>(BitMask.MaskComparer);
            _filters = new Dictionary<FilterMasks, EcsFilter>(FilterMasks.MasksComparer);
        }

#if DEBUG
        private bool CheckMappingSynch()
        {
            for (int i = 0; i < _eToACount; i++)
            {
                var archetype = _eToA[i];
                if (archetype == null)
                    continue;
                if (!_mToA.ContainsKey(archetype.Mask))
                    return false;
                if (!_mToA[archetype.Mask].Mask.MasksEquals(archetype.Mask))
                    return false;
            }

            foreach (var (_, archetype) in _mToA)
            {
                if (archetype == null)
                    return false;
                if (archetype.Entities.Count > 0 && !_eToA.Contains(archetype))
                    return false;
            }

            return true;
        }
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryAddArchetypeToFilter(Archetype archetype, FilterMasks masks, EcsFilter filter)
        {
            bool add = archetype.Mask.InclusivePass(masks.Includes) &&
                       archetype.Mask.ExclusivePass(masks.Excludes);
            if (add)
                filter.AddArchetype(archetype);

            return add;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Archetype GetOrAddArchetype(EntityType eid, BitMask mask)
        {
            if (!_mToA.ContainsKey(mask))
            {
                _mToA[mask] = new Archetype(mask);
                foreach (var maskFilterPair in _filters)
                    TryAddArchetypeToFilter(_mToA[mask], maskFilterPair.Key, maskFilterPair.Value);
            }

            if (_eToA.Length <= eid)
            {
                //CODEX_TODO: use this in every place where resize is needed
                const int maxResizeDelta = 64;
                Utils.ResizeArray(eid, ref _eToA, maxResizeDelta);
            }
            if (_eToACount <= eid)
                _eToACount = eid + 1;
            //CODEX_TODO: changing each call even if it already has right archetype
            //looks smelly but for now I don't know how to rewrite it better
            _eToA[eid] = _mToA[mask];

#if DEBUG
            if (!CheckMappingSynch())
                throw new EcsException("mappings desynch");
#endif

            return _mToA[mask];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddToArchetype(EntityType eid, BitMask mask)
        {
            GetOrAddArchetype(eid, mask).AddEntity(eid);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddComponent<T>(EntityType eid) { AddComponent(eid, ComponentMeta<T>.Id); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddComponent(EntityType eid, int componentId)
        {
            MoveBetweenArchetypes(eid, componentId, true);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveComponent<T>(EntityType eid) { RemoveComponent(eid, ComponentMeta<T>.Id); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveComponent(EntityType eid, int componentId)
        {
            MoveBetweenArchetypes(eid, componentId, false);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void MoveBetweenArchetypes(EntityType eid, int componentId, bool isAdd)
        {
#if DEBUG
            if (!HaveEntity(eid))
                throw new EcsException("archetypes have no such eid");
            if (!_mToA.ContainsKey(_eToA[eid].Mask))
                throw new EcsException("mappings desynch");

            if (!CheckMappingSynch())
                throw new EcsException("mappings desynch");
#endif

            Archetype archetype = _eToA[eid];
            archetype.RemoveEntity(eid);
            BitMask nextMask = archetype.Mask.Duplicate();
            if (isAdd)
                nextMask.Set(componentId);
            else
                nextMask.Unset(componentId);
            Archetype newArchetype = GetOrAddArchetype(eid, nextMask);
            newArchetype.AddEntity(eid);
            _eToA[eid] = newArchetype;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool RegisterFilter(EcsWorld world, FilterMasks masks, out EcsFilter filter)
        {
            if (_filters.ContainsKey(masks))
            {
                filter = _filters[masks];
                return false;
            }

            filter = new EcsFilter(world);
            _filters[masks] = filter;
            foreach (var (_, archetype) in _mToA)
                TryAddArchetypeToFilter(archetype, masks, filter);

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Have<T>(EntityType eid) => _eToA[eid].Mask.Check(ComponentMeta<T>.Id);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Have(int componentId, EntityType eid) => _eToA[eid].Mask.Check(componentId);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref BitMask GetMask(EntityType eid) => ref _eToA[eid].Mask;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Delete(EntityType eid)
        {
            _eToA[eid].RemoveEntity(eid);
            _eToA[eid] = null;
        }
    }
}