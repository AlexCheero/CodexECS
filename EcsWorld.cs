using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
//TODO: maybe should move aliases to classes
//TODO: probably it is better to use sparse sets for update sets,
//      maybe even every Dictionary, that uses int as keys
using UpdateSets = System.Collections.Generic.Dictionary<int, System.Collections.Generic.HashSet<int>>;

//TODO: cover with tests
namespace ECS
{
    //TODO: think about implementing dynamically counted initial size
    public static class EcsCacheSettings
    {
        public static int UpdateSetSize = 4;
        public static int PoolSize = 512;
        public static int FilteredEntitiesSize = 128;
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

        private Dictionary<int, IComponentsPool> _componentsPools;

        //update sets holds indices of filters by types
        //TODO: fill this sets on registration, not on add/remove
        private UpdateSets _includeUpdateSets;
        private UpdateSets _excludeUpdateSets;
        private FiltersCollection _filtersCollection;

        public EcsWorld(int entitiesReserved = 32)
        {
            //TODO: ensure that _entities and masks are always have same length
            _entites = new SimpleVector<Entity>(entitiesReserved);
            _masks = new SimpleVector<BitMask>(entitiesReserved);
            _componentsPools = new Dictionary<int, IComponentsPool>();
            
            _includeUpdateSets = new UpdateSets();
            _excludeUpdateSets = new UpdateSets();
            _filtersCollection = new FiltersCollection();
        }

        //prealloc ctor
        public EcsWorld(EcsWorld other)
        {
            _entites = new SimpleVector<Entity>(other._entites.Reserved);
            _masks = new SimpleVector<BitMask>(other._masks.Reserved);
            _componentsPools = new Dictionary<int, IComponentsPool>();

            //update sets should be same for every copy of the world
            _includeUpdateSets = other._includeUpdateSets;
            _excludeUpdateSets = other._excludeUpdateSets;
            _filtersCollection = new FiltersCollection(other._filtersCollection.Length);
        }

        public void Copy(in EcsWorld other)
        {
            _entites.Copy(other._entites);
            _recycleListHead = other._recycleListHead;
            _masks.Copy(other._masks);
            foreach (var key in _componentsPools.Keys)
            {
                if (!other._componentsPools.ContainsKey(key))
                {
                    _componentsPools[key].Clear();
                }
            }

            foreach (var key in other._componentsPools.Keys)
            {
                var otherPool = other._componentsPools[key];
                if (_componentsPools.ContainsKey(key))
                    _componentsPools[key].Copy(otherPool);
                else
                    _componentsPools.Add(key, otherPool.Duplicate());
            }

            _filtersCollection.Copy(other._filtersCollection);
        }

#region Entities methods

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsEnitityInRange(int id) => id < _entites.Length;
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsEnitityInRange(Entity entity) => IsEnitityInRange(entity.GetId());

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
        private ref Entity GetRefById(Entity other) => ref GetRefById(other.ToId());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Entity GetById(int id) => GetRefById(id);

        public bool IsDead(int id) => GetRefById(id).GetId() != id;

        public bool IsDead(Entity entity) => IsDead(entity.ToId());

        private ref Entity GetRecycled()
        {
            ref var curr = ref _recycleListHead;
            ref var next = ref GetRefById(curr);
            while (!next.IsNull())
            {
                curr = ref next;
                next = ref GetRefById(next);
            }

            next.SetId(curr.ToId());
            next.IncrementVersion();
            curr.SetNullId();
            return ref next;
        }

        public void Delete(Entity entity)
        {
#if DEBUG
            if (IsDead(entity))
                throw new EcsException("trying to delete already dead entity");
            if (!IsEnitityInRange(entity))
                throw new EcsException("trying to delete wrong entity");
            if (entity.IsNull())
                throw new EcsException("trying to delete null entity");
#endif
            var mask = _masks[entity.ToId()];
            var nextSetBit = mask.GetNextSetBit(0);
            while (nextSetBit != -1)
            {
                RemoveComponent(entity.ToId(), nextSetBit);
                nextSetBit = mask.GetNextSetBit(nextSetBit + 1);
            }

            _filtersCollection.RemoveId(entity.ToId());

            ref var recycleListEnd = ref _recycleListHead;
            while (!recycleListEnd.IsNull())
                recycleListEnd = ref GetRefById(recycleListEnd);
            recycleListEnd.SetId(entity.ToId());
            GetRefById(entity).SetNullId();
        }

        public Entity Create()
        {
            if (!_recycleListHead.IsNull())
                return GetRecycled();

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
            return _entites[_entites.Length - 1];
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

        private void UpdateFiltersOnAdd<T>(int id)
        {
            var componentId = ComponentMeta<T>.Id;
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

        public ref T AddComponent<T>(Entity entity, T component = default)
        {
            int id = entity.ToId();

            UpdateFiltersOnAdd<T>(id);

            var componentId = ComponentMeta<T>.Id;
            if (!_componentsPools.ContainsKey(componentId))
                _componentsPools.Add(componentId, new ComponentsPool<T>(EcsCacheSettings.PoolSize));
            var pool = (ComponentsPool<T>)_componentsPools[componentId];
#if DEBUG
            if (pool == null)
                throw new EcsException("invalid pool");
#endif
            return ref pool.Add(entity, component);
        }

        public void AddTag<T>(Entity entity)
        {
            int id = entity.ToId();

            UpdateFiltersOnAdd<T>(id);

            var componentId = ComponentMeta<T>.Id;
            if (!_componentsPools.ContainsKey(componentId))
                _componentsPools.Add(componentId, new TagsPool<T>(EcsCacheSettings.PoolSize));
            var pool = (TagsPool<T>)_componentsPools[componentId];
#if DEBUG
            if (pool == null)
                throw new EcsException("invalid pool");
#endif
            pool.Add(entity);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T GetComponent<T>(int id)
        {
            var pool = (ComponentsPool<T>)_componentsPools[ComponentMeta<T>.Id];
            return ref pool[id];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveComponent<T>(int id) => RemoveComponent(id, ComponentMeta<T>.Id);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RemoveComponent(int id, int componentId)
        {
            UpdateFiltersOnRemove(componentId, id);
            _componentsPools[componentId].Remove(id);
        }
#endregion
#region Filters methods

        private void AddFilterToUpdateSets(in BitMask components, int filterIdx
            , UpdateSets sets)
        {
            var nextSetBit = components.GetNextSetBit(0);
            while (nextSetBit != -1)
            {
                if (!sets.ContainsKey(nextSetBit))
                {
#if UNITY
                    sets.Add(nextSetBit, new HashSet<int>());
#else
                    sets.Add(nextSetBit, new HashSet<int>(EcsCacheSettings.UpdateSetSize));
#endif
                }

#if DEBUG
                if (sets[nextSetBit].Contains(filterIdx))
                    throw new EcsException("set already contains this filter!");
#endif

                sets[nextSetBit].Add(filterIdx);

                nextSetBit = components.GetNextSetBit(nextSetBit + 1);
            }
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

        public EcsFilter GetFilter(int id) => _filtersCollection[id];
#endregion
    }
}
