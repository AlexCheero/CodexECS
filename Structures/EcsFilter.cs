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
        public string Name;

        private Archetype[] _archetypes;
        private int _archetypesEnd;

        public readonly EcsWorld World;
        
        private readonly SimpleList<View> _views;
        private int _viewsStartIdx;
        
        public int EntitiesCount
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                var count = 0;
                for (int i = 0; i < _archetypesEnd; i++)
                    count += _archetypes[i].EntitiesEnd;
                return count;
            }
        }

        public EntityType this[int idx]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                var archetypeIdx = 0;
                while (idx >= _archetypes[archetypeIdx].EntitiesEnd)
                {
                    idx -= _archetypes[archetypeIdx].EntitiesEnd;
                    archetypeIdx++;
                }
                return _archetypes[archetypeIdx].EntitiesArr[idx];
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public EntityType GetNthEntitySafe(int idx) => EntitiesCount > idx ? this[idx] : -1;

        public EcsFilter(EcsWorld world)
        {
            _archetypes = Array.Empty<Archetype>();
            _views = new();
            _views.Add(new View(this, 0));
            World = world;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddArchetype(Archetype archetype)
        {
#if DEBUG && !ECS_PERF_TEST
            for (int i = 0; i < _archetypesEnd; i++)
            {
                if (_archetypes[i] == archetype)
                    throw new EcsException("filter already have this archetype");
            }
#endif

            if (_archetypesEnd >= _archetypes.Length)
            {
                Utils.ResizeArray(_archetypesEnd, ref _archetypes, 32);
            }
            _archetypes[_archetypesEnd] = archetype;
            _archetypesEnd++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public View GetEnumerator()
        {
            var isNotEmpty = false;
            int firstArchetypeIndex = 0;
            for (int i = 0; i < _archetypesEnd; i++)
            {
                if (_archetypes[i].EntitiesEnd == 0 && !isNotEmpty)
                    continue;
                if (!isNotEmpty)
                {
                    firstArchetypeIndex = i;
                    isNotEmpty = true;
                }
                _archetypes[i].Lock();
            }

            if (!isNotEmpty)
                return EmptyView;
            
            World.Lock();

            View view = null; 
            if (_views.Length == 1)
            {
                if (!_views[0].IsInUse)
                {
                    view = _views[0];
                    goto UseView;
                }

                goto AddNewView;
            }

            var lastIdx = (_viewsStartIdx + _views.Length - 1) % _views.Length;
            if (!_views[lastIdx].IsInUse)
            {
                view = _views[lastIdx];
                _viewsStartIdx--;
                if (_viewsStartIdx < 0)
                    _viewsStartIdx += _views.Length;
            }
            
            AddNewView:
            if (view == null)
            {
                view = new View(this, _views.Length);
                _views.Add(view);
                ReturnView(view.Idx);
            }
            
            UseView:
            
#if DEBUG && !ECS_PERF_TEST
            if (view.IsInUse)
                throw new EcsException("view is already in use");
#endif

            view.Use(firstArchetypeIndex);
            return view;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ReturnView(int idx)
        {
#if DEBUG && !ECS_PERF_TEST
            if (idx < 0 || idx >= _views.Length)
                throw new EcsException("View index is out of range");
#endif
            if (_views.Length < 2)
                return;
            (_views[idx], _views[_viewsStartIdx]) = (_views[_viewsStartIdx], _views[idx]);
            _views[idx].Idx = idx;
            _views[_viewsStartIdx].Idx = _viewsStartIdx;
            
            _viewsStartIdx = (_viewsStartIdx + 1) % _views.Length;
        }

        private static readonly View EmptyView = new(null, -1);

        public class View : IDisposable
        {
            private readonly EcsFilter _filter;
            internal int Idx;

            private int _archetypeIndex;
            private int _entityIndex;

            //copied from filter to reduce indirection
            //and to preserve old _entitiesEnd to emulate delayed add
            private Archetype[] _archetypes;
            private EntityType[] _entitiesArr;
            private int _entitiesEnd;
            private int _archetypesEnd;

            //CODEX_TODO: dunno how to properly reset it
            public bool IsInUse
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get;
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                private set;
            }

            public void Use(int archetypeIndex)
            {
                IsInUse = true;

                _archetypeIndex = archetypeIndex;
                _archetypes = _filter._archetypes;
                _archetypesEnd = _filter._archetypesEnd;

                if (_archetypesEnd > 0)
                {
                    _entitiesArr = _archetypes[_archetypeIndex].EntitiesArr;
                    _entitiesEnd = _archetypes[_archetypeIndex].EntitiesEnd;
                }
                else
                {
                    _entitiesArr = null;
                    _entitiesEnd = 0;
                }
            }

            public View(EcsFilter filter, int idx)
            {
                _filter = filter;
                _entityIndex = -1;
                _archetypeIndex = 0;
                Idx = idx;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext()
            {
                if (_archetypesEnd == 0)
                    return false;
                
                _entityIndex++;
                if (_entityIndex < _entitiesEnd)
                    return true;

                _entityIndex = 0;
                do
                {
                    _archetypes[_archetypeIndex].Unlock();
                    _archetypeIndex++;
                    
                    if (_archetypeIndex >= _archetypesEnd)
                        break;

                    _entitiesArr = _archetypes[_archetypeIndex].EntitiesArr;
                    _entitiesEnd = _archetypes[_archetypeIndex].EntitiesEnd;
                    if (_archetypes[_archetypeIndex].EntitiesEnd < _entitiesEnd)
                        _entitiesEnd = _archetypes[_archetypeIndex].EntitiesEnd;
                }
                while (_entitiesEnd < 1);

                return _archetypeIndex < _archetypesEnd;
            }

            public EntityType Current
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => _entitiesArr[_entityIndex];
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Dispose()
            {
                _entityIndex = -1;

                for (int i = _archetypeIndex; i < _archetypesEnd; i++)
                    _archetypes[i].Unlock();

                _archetypeIndex = 0;
                IsInUse = false;
                if (_filter != null)
                {
                    _filter.World.Unlock();
                    _filter.ReturnView(Idx);
                }
            }
        }
    }
}
