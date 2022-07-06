using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

#if DEBUG
using System.Text;
#endif

//TODO: cover with tests
namespace ECS
{
    //TODO: think about implementing dynamically counted initial size
    public static class EcsCacheSettings
    {
        public static int UpdateSetSize = 4;
        public static int PoolSize = 512;
        public static int FilteredEntitiesSize = 128;
        public static int PoolsCount = 16;
    }

    class EcsException : Exception
    {
        public EcsException(string msg) : base(msg) { }
    }

    public class EcsWorld
    {
        private SimpleVector<Entity> _entites;
        private Entity _recycleListHead = EntityExtension.NullEntity;

        private SimpleVector<BitMask> _masks;

        private SparseSet<IComponentsPool> _componentsPools;

        //update sets holds indices of filters by types
        private Dictionary<int, HashSet<int>> _includeUpdateSets;
        private Dictionary<int, HashSet<int>> _excludeUpdateSets;
        private FiltersCollection _filtersCollection;

        private List<int> _delayedDeleteList;
        private readonly Dictionary<int, Enumerable> _enumerators;

        public delegate void OnAddRemoveHandler(EcsWorld world, int id);
        private Dictionary<int, OnAddRemoveHandler> _onAddEvents;
        private Dictionary<int, OnAddRemoveHandler> _onRemoveEvents;

        public EcsWorld(int entitiesReserved = 32)
        {
            //TODO: ensure that _entities and masks are always have same length
            _entites = new SimpleVector<Entity>(entitiesReserved);
            _masks = new SimpleVector<BitMask>(entitiesReserved);
            _componentsPools = new SparseSet<IComponentsPool>(EcsCacheSettings.PoolsCount);
            
            _includeUpdateSets = new Dictionary<int, HashSet<int>>();
            _excludeUpdateSets = new Dictionary<int, HashSet<int>>();
            _filtersCollection = new FiltersCollection();

            _delayedDeleteList = new List<int>();
            _enumerators = new Dictionary<int, Enumerable>();

            _onAddEvents = new Dictionary<int, OnAddRemoveHandler>();
            _onRemoveEvents = new Dictionary<int, OnAddRemoveHandler>();
        }

        //prealloc ctor
        public EcsWorld(EcsWorld other)
        {
            _entites = new SimpleVector<Entity>(other._entites.Reserved);
            _masks = new SimpleVector<BitMask>(other._masks.Reserved);
            _componentsPools = new SparseSet<IComponentsPool>(other._componentsPools.Length);

            //update sets should be same for every copy of the world
            _includeUpdateSets = other._includeUpdateSets;
            _excludeUpdateSets = other._excludeUpdateSets;
            _filtersCollection = new FiltersCollection(other._filtersCollection.Length);

            _delayedDeleteList = new List<int>();
            _enumerators = new Dictionary<int, Enumerable>();

            _onAddEvents = other._onAddEvents;
            _onRemoveEvents = other._onRemoveEvents;
        }

        public void Copy(in EcsWorld other)
        {
            _entites.Copy(other._entites);
            _recycleListHead = other._recycleListHead;
            _masks.Copy(other._masks);

            var length = other._componentsPools.Length;
            var dense = other._componentsPools._dense;
            for (int i = 0; i < length; i++)
            {
                var compId = dense[i];
                var otherPool = other._componentsPools[compId];
                if (_componentsPools.Contains(compId))
                    _componentsPools[compId].Copy(otherPool);
                else
                    _componentsPools.Add(compId, otherPool.Duplicate());
            }

            if (length < _componentsPools.Length)
            {
                for (int i = 0; i < _componentsPools.Length; i++)
                {
                    var compId = _componentsPools._dense[i];
                    if (!other._componentsPools.Contains(compId))
                        _componentsPools[compId].Clear();
                }
            }

            _filtersCollection.Copy(other._filtersCollection);
        }

#region Entities methods

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsEnitityInRange(int id) => id < _entites.Length;
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ref Entity GetRefById(int id)
        {
#if DEBUG
            if (id == EntityExtension.NullEntity.GetId() || !IsEnitityInRange(id))
                throw new EcsException("wrong entity id");
#endif
            return ref _entites[id];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ref Entity GetRefById(Entity other) => ref GetRefById(other.GetId());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Entity GetById(int id) => GetRefById(id);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsDead(int id) => GetRefById(id).GetId() != id;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsNull(int id) => id == EntityExtension.NullEntity.GetId();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(int entity1, int entity2)
        {
            if (entity1 == entity2)
                return GetById(entity1).GetVersion() == GetById(entity2).GetVersion();
            else
                return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsEntityValid(Entity entity)
        {
            var id = entity.GetId();
            return !entity.IsNull() && !IsDead(id) && entity.GetVersion() == GetById(id).GetVersion();
        }

        private int GetRecycledId()
        {
            ref var curr = ref _recycleListHead;
            ref var next = ref GetRefById(curr);
            while (!next.IsNull())
            {
                curr = ref next;
                next = ref GetRefById(next);
            }

            next.SetId(curr.GetId());
            next.IncrementVersion();
            curr.SetNullId();
            return next.GetId();
        }

        #region Enumerable
        private int _lockCounter;
        private bool IsLocked { get => _lockCounter > 0; }
        private void Lock()
        {
#if DEBUG
            //TODO: could cause problems even for single thread in nested loops
            //if (_lockCounter > 0)
            //    throw new EcsException("_lockCounter is positive. error only for single thread");
#endif

            _lockCounter++;
        }
        private void Unlock()
        {
            _lockCounter--;

#if DEBUG
            if (_lockCounter < 0)
                throw new EcsException("world's lock counter negative");
            else
#endif

            if (_lockCounter == 0)
            {
                foreach (var id in _delayedDeleteList)
                    Delete(id);
                _delayedDeleteList.Clear();
            }
        }

        public class Enumerable : IDisposable
        {
            private EcsWorld _world;
            private EcsFilter _filter;
            private bool _enumerationFinished;

            public Enumerable(EcsWorld world, int filterId)
            {
                _world = world;
                _filter = _world._filtersCollection[filterId];
                _enumerationFinished = false;
            }

            public Enumerable GetEnumerator() => this;

            public int Current
            {
                get => _filter.Current;
            }

            public bool MoveNext()
            {
                bool wasMoved = _filter.MoveNext();
                if (!wasMoved)
                {
                    _world.Unlock();
                    _enumerationFinished = true;
                }
                return wasMoved;
            }

            public void Dispose()
            {
                if (_world.IsLocked)
                    _world.Unlock();
                if (!_enumerationFinished)
                    _filter.Cleanup();
            }
        }

        public Enumerable Enumerate(int filterId)
        {
            Lock();
            if (!_enumerators.ContainsKey(filterId))
                _enumerators.Add(filterId, new Enumerable(this, filterId));
            return _enumerators[filterId];
        }
        #endregion

        public void Delete(int id)
        {
            if (IsLocked)
            {
                _delayedDeleteList.Add(id);
                return;
            }

            ref Entity entity = ref GetRefById(id);
#if DEBUG
            if (IsDead(id))
                throw new EcsException("trying to delete already dead entity");
            if (!IsEnitityInRange(id))
                throw new EcsException("trying to delete wrong entity");
            if (entity.IsNull())
                throw new EcsException("trying to delete null entity");
#endif
            var mask = _masks[id];
            foreach (var bit in mask)
                RemoveComponent(bit, id);

            //TODO: check if this method is really needed here (it seems that while loop above does all the work)
            //_filtersCollection.RemoveId(id);

            ref var recycleListEnd = ref _recycleListHead;
            while (!recycleListEnd.IsNull())
                recycleListEnd = ref GetRefById(recycleListEnd);
            recycleListEnd.SetId(id);
            entity.SetNullId();
        }

        public int Create()
        {
            if (!_recycleListHead.IsNull())
                return GetRecycledId();

            var lastEntity = new Entity(_entites.Length);
#if DEBUG
            if (lastEntity.Val == EntityExtension.NullEntity.Val)
                throw new EcsException("entity limit reached");
            if (_entites.Length < 0)
                throw new EcsException("entities vector length overflow");
            if (lastEntity.GetVersion() > 0)
                throw new EcsException("lastEntity version should always be 0");
#endif

            _entites.Add(lastEntity);
            _masks.Add(new BitMask());//TODO: precache with _registeredComponents.Count
            return _entites.Length - 1;
        }

        //TODO: implement copying without component override
        public void CopyComponents(Entity from, Entity to/*, bool overrideIfExists*/)
        {
#if DEBUG
            if (!IsEntityValid(from))
                throw new EcsException("trying to move components from invalid entity");
            if (!IsEntityValid(to))
                throw new EcsException("trying to move components to invalid entity");
#endif

            var fromId = from.GetId();
            var toId = to.GetId();
            var fromMask = _masks[fromId];
            ref var toMask = ref _masks[toId];
            foreach (var bit in fromMask)
            {
                _componentsPools[bit].CopyItem(fromId, toId);
                toMask.Set(bit);
                UpdateFiltersOnAdd(bit, toId);
                CallAddEvent(bit, toId);
            }
        }
#endregion

#region Components methods
        //TODO: add reactive callbacks

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Have<T>(int id) => _masks[id].Check(ComponentMeta<T>.Id);

        private void AddIdToFlters(int id, HashSet<int> filterIds)
        {
            foreach (var filterId in filterIds)
            {
                var filter = _filtersCollection[filterId];

                var pass = _masks[id].InclusivePass(filter.Includes);
                pass &= _masks[id].ExclusivePass(filter.Excludes);
                if (!pass)
                    continue;
                //could try to add same id several times due to delayed set modification operations
                filter.Add(id);
            }
        }

        private void RemoveIdFromFilters(int id, HashSet<int> filterIds)
        {
            foreach (var filterId in filterIds)
            {
                var filter = _filtersCollection[filterId];

                var pass = _masks[id].InclusivePass(filter.Includes);
                pass &= _masks[id].ExclusivePass(filter.Excludes);

                if (!pass)
                    continue;
                //could try to remove same id several times due to delayed set modification operations
                filter.Remove(id);
            }
        }

        public bool CheckAgainstMasks(int id, BitMask includes = default, BitMask excludes = default) =>
            _masks[id].InclusivePass(includes) && _masks[id].ExclusivePass(excludes);

        public void SubscribeOnAdd<T>(OnAddRemoveHandler handler)
        {
            var componentId = ComponentMeta<T>.Id;
            if (_onAddEvents.ContainsKey(componentId))
                _onAddEvents[componentId] += handler;
            else
                _onAddEvents[componentId] = handler;
        }

        public void SubscribeOnRemove<T>(OnAddRemoveHandler handler)
        {
            var componentId = ComponentMeta<T>.Id;
            if (_onRemoveEvents.ContainsKey(componentId))
                _onRemoveEvents[componentId] += handler;
            else
                _onRemoveEvents[componentId] = handler;
        }

        private void CallAddEvent(int componentId, int id)
        {
            if (_onAddEvents.ContainsKey(componentId))
                _onAddEvents[componentId].Invoke(this, id);
        }

        private void CallRemoveEvent(int componentId, int id)
        {
            if (_onRemoveEvents.ContainsKey(componentId))
                _onRemoveEvents[componentId].Invoke(this, id);
        }

        private void UpdateFiltersOnAdd(int componentId, int id)
        {
            if (_excludeUpdateSets.ContainsKey(componentId))
                RemoveIdFromFilters(id, _excludeUpdateSets[componentId]);

            _masks[id].Set(componentId);

            if (!_includeUpdateSets.ContainsKey(componentId))
            {
#if UNITY
                _includeUpdateSets.Add(componentId, new HashSet<int>());
#else
                _includeUpdateSets.Add(componentId, new HashSet<int>(EcsCacheSettings.UpdateSetSize));
#endif
            }
            AddIdToFlters(id, _includeUpdateSets[componentId]);
        }

        private void UpdateFiltersOnRemove(int componentId, int id)
        {
            RemoveIdFromFilters(id, _includeUpdateSets[componentId]);
#if DEBUG
            if (_masks[id].Length <= componentId)
                throw new EcsException("there was no component ever");
#endif
            _masks[id].Unset(componentId);

            if (!_excludeUpdateSets.ContainsKey(componentId))
            {
#if UNITY
                _excludeUpdateSets.Add(componentId, new HashSet<int>());
#else
                _excludeUpdateSets.Add(componentId, new HashSet<int>(EcsCacheSettings.UpdateSetSize));
#endif
            }
            AddIdToFlters(id, _excludeUpdateSets[componentId]);
        }

        public ref T AddComponent<T>(int id, T component = default)
        {
            var componentId = ComponentMeta<T>.Id;
            UpdateFiltersOnAdd(componentId, id);

            if (!_componentsPools.Contains(componentId))
                _componentsPools.Add(componentId, new ComponentsPool<T>(EcsCacheSettings.PoolSize));
            var pool = (ComponentsPool<T>)_componentsPools[componentId];
#if DEBUG
            if (pool == null)
                throw new EcsException("invalid pool");
#endif
            ref var addedComponent = ref pool.Add(id, component);

            CallAddEvent(componentId, id);

            return ref addedComponent;
        }

        public void AddComponentNoReturn<T>(int id, T component = default) => AddComponent<T>(id, component);

        public void AddTag<T>(int id)
        {
            var componentId = ComponentMeta<T>.Id;
            UpdateFiltersOnAdd(componentId, id);

            if (!_componentsPools.Contains(componentId))
                _componentsPools.Add(componentId, new TagsPool<T>(EcsCacheSettings.PoolSize));
            var pool = (TagsPool<T>)_componentsPools[componentId];
#if DEBUG
            if (pool == null)
                throw new EcsException("invalid pool");
#endif
            pool.Add(id);

            CallAddEvent(componentId, id);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T GetComponent<T>(int id)
        {
#if DEBUG
            if (!Have<T>(id))
                throw new EcsException("entity have no " + typeof(T));
#endif
            var pool = (ComponentsPool<T>)_componentsPools[ComponentMeta<T>.Id];
            return ref pool._values[pool._sparse[id]];
        }

#if DEBUG
        private string DebugString(int id, int componentId) => _componentsPools[componentId].DebugString(id);

        public void DebugEntity(int id, StringBuilder sb)
        {
            var mask = _masks[id];
            foreach (var bit in mask)
                sb.Append("\n\t" + DebugString(id, bit));
        }

        public void DebugAll(StringBuilder sb)
        {
            for (int i = 0; i < _entites.Length; i++)
            {
                var entity = _entites[i];
                var id = entity.GetId();
                if (!IsDead(id))
                {
                    sb.Append(id + ":");
                    DebugEntity(id, sb);
                    sb.Append('\n');
                }
            }    
        }
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T GetOrAddComponent<T>(int id)
        {
            if (Have<T>(id))
                return ref GetComponent<T>(id);
            else
                return ref AddComponent<T>(id);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveComponent<T>(int id) => RemoveComponent(ComponentMeta<T>.Id, id);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RemoveComponent(int componentId, int id)
        {
            UpdateFiltersOnRemove(componentId, id);
            _componentsPools[componentId].Remove(id);
            CallRemoveEvent(componentId, id);
        }
#endregion
#region Filters methods

        private void AddFilterToUpdateSets(in BitMask components, int filterIdx
            , Dictionary<int, HashSet<int>> sets)
        {
            foreach (var bit in components)
            {
                if (!sets.ContainsKey(bit))
                {
#if UNITY
                    sets.Add(bit, new HashSet<int>());
#else
                    sets.Add(nextSetBit, new HashSet<int>(EcsCacheSettings.UpdateSetSize));
#endif
                }

#if DEBUG
                if (sets[bit].Contains(filterIdx))
                    throw new EcsException("set already contains this filter!");
#endif

                sets[bit].Add(filterIdx);
            }
        }

        public int RegisterFilter(in BitMask includes)
        {
            BitMask defaultExcludes = default;
            return RegisterFilter(in includes, in defaultExcludes);
        }

        public int RegisterFilter(in BitMask includes, in BitMask excludes)
        {
            int filterId;
            if (_filtersCollection.TryAdd(in includes, in excludes, out filterId))
            {
                var filter = _filtersCollection[filterId];
                AddFilterToUpdateSets(in filter.Includes, filterId, _includeUpdateSets);
                AddFilterToUpdateSets(in filter.Excludes, filterId, _excludeUpdateSets);
            }

            return filterId;
        }
        #endregion
    }
}
