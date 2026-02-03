using System;
using System.Collections.Generic;
using CodexECS.Utility;
using System.Runtime.CompilerServices;
using EntityType = System.Int32;//duplicated in EntityExtension

namespace CodexECS
{
    //masks moved out of the filter to be used as a key
    public struct FilterMasks
    {
        public class EqualityComparer : IEqualityComparer<FilterMasks>
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool Equals(FilterMasks x, FilterMasks y) => x.FilterMasksEquals(y);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public int GetHashCode(FilterMasks obj) => obj.GetMasksHash();
        }

        public static readonly EqualityComparer MasksComparer;
        static FilterMasks() => MasksComparer = new();

        public BitMask Includes;
        public BitMask Excludes;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool FilterMasksEquals(FilterMasks other) =>
            Includes.MasksEquals(other.Includes) && Excludes.MasksEquals(other.Excludes);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetMasksHash() => Includes.GetMaskHash() * 23 + Excludes.GetMaskHash();
    }

    public class EcsFilter
    {
#if DEBUG
        private HashSet<Archetype> _archetypes;
#endif

        public readonly EcsWorld World;
        
        // private SparseSet<EntityType> _entitiesSet;
        private int[] _sparse;
        private EntityType[] _dense;
        private int _valuesEnd;
        
        private BitMask _pendingAdd;
        private BitMask _pendingDelete;

        public int EntitiesCount
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _valuesEnd;
        }

        public EntityType this[int idx]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            // get => _entitiesSet._values[idx];
            get => _dense[idx];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        // public EntityType GetNthEntitySafe(int idx) => _entitiesSet.Length > idx ? _entitiesSet._values[idx] : -1;
        public EntityType GetNthEntitySafe(int idx) => _valuesEnd > idx ? _dense[idx] : -1;

        public EcsFilter(EcsWorld world)
        {
#if DEBUG && !ECS_PERF_TEST
            _archetypes = new();
#endif
            // _entitiesSet = new();
            const int initialCapacity = 2;
            _sparse = new int[initialCapacity];
            for (int i = 0; i < initialCapacity; i++)
                _sparse[i] = -1;
            _dense = new int[initialCapacity];
            
            _pendingAdd = new();
            _pendingDelete = new();
            World = world;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddArchetype(Archetype archetype)
        {
            archetype.RelatedFilters.Add(this);

            for (int i = 0; i < archetype.EntitiesEnd; i++)
                AddEntity(archetype.EntitiesArr[i]);

#if DEBUG && !ECS_PERF_TEST
            if (!_archetypes.Add(archetype))
                throw new EcsException("filter already have this archetype");
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddEntity(EntityType eid)
        {
#if DEBUG && !ECS_PERF_TEST
            // if (!_pendingDelete.Contains(eid) && _entitiesSet.ContainsIdx(eid))
            var containsEntity = eid < _sparse.Length && _sparse[eid] > -1;
            if (!_pendingDelete.Check(eid) && containsEntity)
                throw new EcsException("filter already have this entity");
#endif
            
            if (_lockCounter > 0)
            {
                if (_pendingDelete.Check(eid))
                {
                    _pendingDelete.Unset(eid);
                }
                else
                {
                    _pendingAdd.Set(eid);
                    _dirty = true;
                }
                
                return;
            }
            
            // _entitiesSet.Add(eid, eid);
#region SparseSet.Add unrolled

            if (eid >= _sparse.Length)
            {
                var oldLength = _sparse.Length;

                const int maxResizeDelta = 256;
                Utils.ResizeArray(eid, ref _sparse, maxResizeDelta);
                for (int i = oldLength; i < _sparse.Length; i++)
                    _sparse[i] = -1;
            }

#if DEBUG && !ECS_PERF_TEST
            if (_sparse[eid] > -1)
                throw new EcsException("sparse set already have element at this index");
#endif

            _sparse[eid] = _valuesEnd;
            if (_valuesEnd >= _dense.Length)
            {
                const int maxResizeDelta = 256;
                Utils.ResizeArray(_valuesEnd, ref _dense, maxResizeDelta);
            }
            _dense[_valuesEnd] = eid;
            _valuesEnd++;

#if DEBUG && !ECS_PERF_TEST
            if (_dense[_sparse[eid]] != eid)
                throw new EcsException("wrong sparse set entities");
#endif

#endregion
            

#if HEAVY_ECS_DEBUG
            if (!CheckEntitiesSynch())
                throw new EcsException("Entities desynched!");
            if (!CheckUniqueness())
                throw new EcsException("Entities not unique!");
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveEntity(EntityType eid)
        {
#if DEBUG && !ECS_PERF_TEST
            // if (!_pendingAdd.Contains(eid) && !_entitiesSet.ContainsIdx(eid))
            var containsEntity = eid < _sparse.Length && _sparse[eid] > -1;
            if (!_pendingAdd.Check(eid) && !containsEntity)
                throw new EcsException("filter have no this entity");
#endif
            
            if (_lockCounter > 0)
            {
                if (_pendingAdd.Check(eid))
                {
                    _pendingAdd.Unset(eid);
                }
                else
                {
                    _pendingDelete.Set(eid);
                    _dirty = true;
                }
                
                return;
            }

            // _entitiesSet.RemoveAt(eid);
#region SparseSet.RemoveAt unrolled

            var innerIndex = _sparse[eid];
            _sparse[eid] = -1;
            
#if DEBUG && !ECS_PERF_TEST
            if (innerIndex >= _valuesEnd)
                throw new EcsException("innerIndex should be smaller than _valuesEnd");
#endif
            
            //backswap using _dense
            var lastIdx = _valuesEnd - 1;
            if (innerIndex < lastIdx)
                _sparse[_dense[lastIdx]] = innerIndex;
            _dense[innerIndex] = -1;
            _valuesEnd--;
            _dense[innerIndex] = _dense[_valuesEnd];

#endregion
            
#if HEAVY_ECS_DEBUG
            if (!CheckEntitiesSynch())
                throw new EcsException("Entities desynched!");
            if (!CheckUniqueness())
                throw new EcsException("Entities not unique!");
#endif
        }

#if HEAVY_ECS_DEBUG
        private bool CheckEntitiesSynch()
        {
            for (int i = 0; i < _valuesEnd; i++)
            {
                var outerIdx = _dense[i];
                if (outerIdx != _dense[_sparse[outerIdx]])
                    return false;
            }

            return true;
        }
        
        private bool CheckUniqueness()
        {
            for (int i = 0; i < _valuesEnd; i++)
            {
                for (int j = 0; j < _valuesEnd; j++)
                {
                    if (i == j)
                        continue;
                    if (_dense[i] == _dense[j])
                        return false;
                }
            }

            return true;
        }
#endif

        private bool _dirty;
        private int _lockCounter;
        public void Lock()
        {
            World.Lock();
            _lockCounter++;
        }

        public void Unlock()
        {
            World.Unlock();
            _lockCounter--;
#if DEBUG && !ECS_PERF_TEST
            if (_lockCounter < 0)
                throw new EcsException("negative lock counter");
#endif
            if (_lockCounter != 0 || !_dirty)
                return;

            foreach (var eid in _pendingAdd)
            {
                // _entitiesSet.Add(eid, eid);
                if (eid >= _sparse.Length)
                {
                    var oldLength = _sparse.Length;

                    const int maxResizeDelta = 256;
                    Utils.ResizeArray(eid, ref _sparse, maxResizeDelta);
                    for (int i = oldLength; i < _sparse.Length; i++)
                        _sparse[i] = -1;
                }

#if DEBUG && !ECS_PERF_TEST
                if (_sparse[eid] > -1)
                    throw new EcsException("sparse set already have element at this index");
#endif

                _sparse[eid] = _valuesEnd;
                if (_valuesEnd >= _dense.Length)
                {
                    const int maxResizeDelta = 256;
                    Utils.ResizeArray(_valuesEnd, ref _dense, maxResizeDelta);
                }
                _dense[_valuesEnd] = eid;
                _valuesEnd++;

#if DEBUG && !ECS_PERF_TEST
                if (_dense[_sparse[eid]] != eid)
                    throw new EcsException("wrong sparse set idices");
#endif
            }
            _pendingAdd.Clear();
            foreach (var eid in _pendingDelete)
            {
                // _entitiesSet.RemoveAt(eid);
                var innerIndex = _sparse[eid];
                _sparse[eid] = -1;
            
#if DEBUG && !ECS_PERF_TEST
                if (innerIndex >= _valuesEnd)
                    throw new EcsException("innerIndex should be smaller than _valuesEnd");
#endif
            
                //backswap using _dense
                var lastIdx = _valuesEnd - 1;
                if (innerIndex < lastIdx)
                    _sparse[_dense[lastIdx]] = innerIndex;
                _dense[innerIndex] = -1;
                _valuesEnd--;
                _dense[innerIndex] = _dense[_valuesEnd];
            }
            _pendingDelete.Clear();

            _dirty = false;
            
#if HEAVY_ECS_DEBUG
            if (!CheckEntitiesSynch())
                throw new EcsException("Entities desynched!");
            if (!CheckUniqueness())
                throw new EcsException("Entities not unique!");
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Enumerator GetEnumerator()
        {
            Lock();
            return new (this);
        }

        public struct Enumerator : IDisposable
        {
            private readonly EcsFilter _filter;
            
            //moved out form filter to reduce indirection
            private readonly EntityType[] _dense;
            private readonly int _valuesEnd;
            //===========================================

            private int _entityIndex;

            public Enumerator(EcsFilter filter)
            {
                _filter = filter;
                _dense = _filter._dense;
                _valuesEnd = _filter._valuesEnd;
                _entityIndex = -1;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext()
            {
                _entityIndex++;
                return _entityIndex < _valuesEnd;
            }

            public EntityType Current
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => _dense[_entityIndex];
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Dispose()
            {
                _entityIndex = -1;
                _filter.Unlock();
            }
        }
    }
}
