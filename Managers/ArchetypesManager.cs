using System.Collections.Generic;
using CodexECS.Utility;
using System.Runtime.CompilerServices;
using EntityType = System.Int32;//duplicated in EntityExtension

#if HEAVY_ECS_DEBUG
using System.Linq;
#endif

namespace CodexECS
{
    class ArchetypesManager
    {
        private Archetype[] _eToA; //entity to archetype mapping
        private SparseSet<List<Archetype>> _cToA; //component id to archetype mapping
        private EntityType _eToACount;
        private Dictionary<BitMask, Archetype> _mToA; //mask to archetype mapping
        private Dictionary<FilterMasks, EcsFilter> _filters;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HaveEntity(EntityType eid) => eid < _eToACount && _eToA[eid] != null;

        public ArchetypesManager()
        {
            _eToA = new Archetype[2];
            _cToA = new ();
            _eToACount = 0;
            _mToA = new Dictionary<BitMask, Archetype>(BitMask.MaskComparer);
            _filters = new Dictionary<FilterMasks, EcsFilter>(FilterMasks.MasksComparer);
            
            //create empty archetype
            var emptyMask = new BitMask();
            _mToA[emptyMask] = new Archetype(emptyMask);
        }

#if HEAVY_ECS_DEBUG
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
                if (archetype.EntitiesEnd > 0 && !_eToA.Contains(archetype))
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
        private void AddArchetype(BitMask mask)
        {
#if DEBUG && !ECS_PERF_TEST
            if (_mToA.ContainsKey(mask))
                throw new EcsException("Archetype already added");
#endif
            var newArchetype = new Archetype(mask);
            _mToA[mask] = newArchetype;
            foreach (var maskFilterPair in _filters)
                TryAddArchetypeToFilter(newArchetype, maskFilterPair.Key, maskFilterPair.Value);
            foreach (var componentId in mask)
            {
                if (!_cToA.ContainsIdx(componentId))
                    _cToA.Add(componentId, new List<Archetype>());
#if DEBUG && !ECS_PERF_TEST
                for (var i = 0; i < _cToA[componentId].Count; i++)
                {
                    if (_cToA[componentId][i].Mask.MasksEquals(mask))
                        throw new EcsException("component id to archetype mapping already contains such archetype");
                }
#endif
                _cToA[componentId].Add(newArchetype);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UpdateArchetype(EntityType eid, BitMask mask)
        {
            if (_eToACount <= eid)
            {
                if (_eToA.Length <= eid)
                {
                    const int maxResizeDelta = 64;
                    Utils.ResizeArray(eid, ref _eToA, maxResizeDelta);
                }
                _eToACount = eid + 1;
            }
            
            _eToA[eid] = _mToA[mask];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddToEmptyArchetype(EntityType eid)
        {
            var emptyMask = new BitMask();
            _mToA[emptyMask].AddEntity(eid);
            UpdateArchetype(eid, emptyMask);
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
        public void RemoveAll<T>() { RemoveAll(ComponentMeta<T>.Id); }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveAll(int componentId)
        {
            for (var i = 0; i < _cToA[componentId].Count; i++)
            {
                var archetype = _cToA[componentId][i];

                var nextMask = archetype.Mask.AndNot(componentId);
                if (!_mToA.ContainsKey(nextMask))
                    AddArchetype(nextMask);
                var nextArchetype = _mToA[nextMask];
                //hack. we store EntitiesEnd and then clear archetype, before iterating its entities in order for the filters to update
                //in fact the array of entities is not cleared and it is just EntitiesEnd which is reset to 0
                var entitiesEnd = archetype.EntitiesEnd;
                archetype.Clear();
                for (int j = 0; j < entitiesEnd; j++)
                {
                    var eid = archetype.EntitiesArr[j];
                    UpdateArchetype(eid, nextMask);
                    nextArchetype.AddEntity(eid);
                }
            }

#if HEAVY_ECS_DEBUG
            if (!CheckMappingSynch())
                throw new EcsException("mappings desynch");
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void MoveBetweenArchetypes(EntityType eid, int componentId, bool isAdd)
        {
#if DEBUG && !ECS_PERF_TEST
            if (!HaveEntity(eid))
                throw new EcsException("archetypes have no such eid");
            if (!_mToA.ContainsKey(_eToA[eid].Mask))
                throw new EcsException("mappings desynch");
#endif

            Archetype archetype = _eToA[eid];
            archetype.RemoveEntity(eid);
            BitMask nextMask = archetype.Mask.Duplicate();
            if (isAdd)
                nextMask.Set(componentId);
            else
                nextMask.Unset(componentId);

            if (!_mToA.ContainsKey(nextMask))
                AddArchetype(nextMask);
            UpdateArchetype(eid, nextMask);
            _eToA[eid].AddEntity(eid);
            
#if HEAVY_ECS_DEBUG
            if (!CheckMappingSynch())
                throw new EcsException("mappings desynch");
#endif
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