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

#region Unrolled from IndexableHashSet
        private readonly Dictionary<EntityType, int> _entitiesMap;
        private EntityType[] _entitiesArr;
        private int _entitiesLength;
#endregion

        private readonly List<View> _views;
        private readonly EcsWorld _world;

        public int EntitiesCount
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _entitiesLength;
        }

        public EntityType this[int idx]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _entitiesArr[idx];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public EntityType GetNthEntitySafe(int idx) => _entitiesLength > idx ? _entitiesArr[idx] : -1;

        public EcsFilter(EcsWorld world)
        {
#if DEBUG && !ECS_PERF_TEST
            _archetypes = new();
#endif
            _entitiesMap = new();
            _entitiesArr = new EntityType[2];
            _views = new() { new View(this) };
            _world = world;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddArchetype(Archetype archetype)
        {
            archetype.OnEntityAdded += AddEntity;
            archetype.OnEntityRemoved += RemoveEntity;

            for (int i = 0; i < archetype.EntitiesEnd; i++)
                AddEntity(archetype.EntitiesArr[i]);

#if DEBUG && !ECS_PERF_TEST
            if (!_archetypes.Add(archetype))
                throw new EcsException("filter already have this archetype");
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddEntity(EntityType eid)
        {
#if DEBUG && !ECS_PERF_TEST
            if (_entitiesMap.ContainsKey(eid))
                throw new EcsException("filter already have this entity");
#endif
            
            if (_lockCounter > 0)
            {
                _entitiesMap[eid] = default;
                _dirty = true;
                return;
            }
            
            _entitiesMap[eid] = _entitiesLength;

#region Unrolled from SimpleList (Add)
            if (_entitiesLength >= _entitiesArr.Length)
            {
                const int maxResizeDelta = 256;
                Utils.ResizeArray(_entitiesLength, ref _entitiesArr, maxResizeDelta);
            }
            _entitiesArr[_entitiesLength] = eid;
            _entitiesLength++;
#endregion

#if HEAVY_ECS_DEBUG
            if (!CheckEntitiesSynch())
                throw new EcsException("Entities desynched!");
            if (!CheckUniqueness())
                throw new EcsException("Entities not unique!");
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RemoveEntity(EntityType eid)
        {
#if DEBUG && !ECS_PERF_TEST
            if (!_entitiesMap.ContainsKey(eid))
                throw new EcsException("filter have no this entity");
#endif
            
            if (_lockCounter > 0)
            {
                _entitiesMap.Remove(eid);
                _dirty = true;
                return;
            }

            _entitiesLength--;
            var index = _entitiesMap[eid];
            _entitiesArr[index] = _entitiesArr[_entitiesLength];
            _entitiesMap[_entitiesArr[index]] = index;
            _entitiesMap.Remove(eid);
            
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
            if (_entitiesLength != _entitiesMap.Count)
                return false;
            for (int i = 0; i < _entitiesLength; i++)
            {
                var eid = _entitiesArr[i];
                if (!_entitiesMap.ContainsKey(eid) || _entitiesMap[eid] != i)
                    return false;
            }

            return true;
        }

        private bool CheckUniqueness()
        {
            for (int i = 0; i < _entitiesLength; i++)
            {
                for (int j = 0; j < _entitiesLength; j++)
                {
                    if (i == j)
                        continue;
                    if (_entitiesArr[i] == _entitiesArr[j])
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
            _world.Lock();
            _lockCounter++;
        }

        public void Unlock()
        {
            _world.Unlock();
            _lockCounter--;
#if DEBUG && !ECS_PERF_TEST
            if (_lockCounter < 0)
                throw new EcsException("negative lock counter");
#endif
            if (_lockCounter != 0 || !_dirty)
                return;

            _entitiesLength = _entitiesMap.Count;
            if (_entitiesLength > _entitiesArr.Length)
            {
                const int maxResizeDelta = 256;
                Utils.ResizeArray(_entitiesLength, ref _entitiesArr, maxResizeDelta);
            }
            
            //CODEX_TODO: two loops. try to do it in a single loop
            var arrIdx = 0;
            foreach (var (eid, idx) in _entitiesMap)
            {
                _entitiesArr[arrIdx] = eid;
                arrIdx++;
            }
            for (int i = 0; i < _entitiesLength; i++)
                _entitiesMap[_entitiesArr[i]] = i;

            _dirty = false;
            
#if HEAVY_ECS_DEBUG
            if (!CheckEntitiesSynch())
                throw new EcsException("Entities desynched!");
            if (!CheckUniqueness())
                throw new EcsException("Entities not unique!");
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public View GetEnumerator()
        {
            Lock();
            //CODEX_TODO: swap views in order to have first or last view always free
            foreach (View view in _views)
            {
                if (view.IsInUse)
                    continue;
                view.Use();
                return view;
            }
            _views.Add(new View(this));
            _views[^1].Use();
            return _views[^1];
        }

        public class View : IDisposable
        {
            private EcsFilter _filter;

            private int _entityIndex;

            //CODEX_TODO: dunno how to properly reset it
            public bool IsInUse
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get;
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                private set;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Use() => IsInUse = true;

            public View(EcsFilter filter)
            {
                _filter = filter;
                _entityIndex = -1;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext()
            {
                _entityIndex++;
                return _entityIndex < _filter._entitiesLength;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Reset()
            {
                _entityIndex = -1;
                IsInUse = false;
            }

            public EntityType Current
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => _filter._entitiesArr[_entityIndex];
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Dispose()
            {
                _filter.Unlock();
                Reset();
            }
        }
    }
}
