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
        private Dictionary<EntityType, int> _entitiesMap;
        private EntityType[] _entitiesArr;
        private int _entitiesLength;
#endregion

        private List<View> _views;
        private EcsWorld _world;

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
        public EntityType GetNthEntitySafe(int idx) => _entitiesArr.Length > idx ? _entitiesArr[idx] : -1;

        public EcsFilter(EcsWorld world)
        {
#if DEBUG
            _archetypes = new();
#endif
            _entitiesMap = new();
            _entitiesArr = new EntityType[2];
            _views = new() { new View(world, this) };
            _world = world;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddArchetype(Archetype archetype)
        {
            archetype.OnEntityAdded += AddEntity;
            archetype.OnEntityRemoved += RemoveEntity;

            //CODEX_TODO: optimize
            for (int i = 0; i < archetype.Entities.Count; i++)
                AddEntity(archetype.Entities[i]);

#if DEBUG
            if (!_archetypes.Add(archetype))
                throw new EcsException("filter already have this archetype");
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddEntity(EntityType entityType)
        {
#if DEBUG
            if (_entitiesMap.ContainsKey(entityType))
            {
                //same entity could be added from different archetypes
                return;
                //throw new EcsException("entity was already in filter");
            }
#endif
            _entitiesMap[entityType] = _entitiesLength;

#region Unrolled from SimpleList (Add)
            if (_entitiesLength >= _entitiesArr.Length)
            {
                const int maxResizeDelta = 256;
                Utils.ResizeArray(_entitiesLength, ref _entitiesArr, maxResizeDelta);
            }
            _entitiesArr[_entitiesLength] = entityType;
            _entitiesLength++;
#endregion
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RemoveEntity(EntityType entityType)
        {
#if DEBUG
            if (!_entitiesMap.TryGetValue(entityType, out var index))
            {
                //same entity could be removed from different archetypes
                return;
                //throw new EcsException("entity was not in filter");
            }
#endif

            _entitiesMap.Remove(_entitiesArr[index]);
            if (_entitiesLength > 1)//swap only if this is not the last element
            {
                _entitiesArr[index] = _entitiesArr[^1];
                _entitiesMap[_entitiesArr[index]] = index;
            }

#region Unrolled from SimpleList (Remove)
            var removeIdx = _entitiesLength - 1;
            _entitiesArr[removeIdx] = default;
            _entitiesLength--;
            if (removeIdx < _entitiesLength)
                _entitiesArr[removeIdx] = _entitiesArr[_entitiesLength];
#endregion
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public View GetEnumerator()
        {
            _world.Lock();
            //CODEX_TODO: swap views in order to have first or last view always free
            foreach (View view in _views)
            {
                if (!view.IsInUse)
                {
                    view.Use();
                    return view;
                }
            }
            _views.Add(new View(_world, this));
            _views[^1].Use();
            return _views[^1];
        }

        public class View : IDisposable
        {
            private EcsWorld _world;
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

            public View(EcsWorld world, EcsFilter filter)
            {
                _world = world;
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
                _world.Unlock();
                Reset();
            }
        }
    }
}
