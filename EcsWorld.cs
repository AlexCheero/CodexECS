﻿using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
//TODO: maybe should move aliases to classes
using EntityType = System.UInt32;
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
        private SimpleVector<EntityType> _entites;
        private EntityType _recycleListHead = EntityExtension.NullEntity;

        private SimpleVector<BitMask> _masks;

        private Dictionary<int, IComponentsPool> _componentsPools;

        //update sets holds indices of filters by types
        private UpdateSets _compsUpdateSets;//TODO: rename all "comps" to includes for more consistency
        private UpdateSets _excludesUpdateSets;
        private FiltersCollection _filtersCollection;

        public EcsWorld(int entitiesReserved = 32)
        {
            //TODO: ensure that _entities and masks are always have same length
            _entites = new SimpleVector<EntityType>(entitiesReserved);
            _masks = new SimpleVector<BitMask>(entitiesReserved);
            _componentsPools = new Dictionary<int, IComponentsPool>();
            
            _compsUpdateSets = new UpdateSets();
            _excludesUpdateSets = new UpdateSets();
            _filtersCollection = new FiltersCollection();
        }

        //prealloc ctor
        public EcsWorld(EcsWorld other)
        {
            _entites = new SimpleVector<EntityType>(other._entites.Reserved);
            _masks = new SimpleVector<BitMask>(other._masks.Reserved);
            _componentsPools = new Dictionary<int, IComponentsPool>();

            //update sets should be same for every copy of the world
            _compsUpdateSets = other._compsUpdateSets;
            _excludesUpdateSets = other._excludesUpdateSets;
            _filtersCollection = new FiltersCollection(other._filtersCollection.Length);
        }

        public void Copy(in EcsWorld other)
        {
            _entites.Copy(other._entites);
            _recycleListHead = other._recycleListHead;
            _masks.Copy(other._masks);//TODO: BitArrays will not copy properly here
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
                    _componentsPools.Add(key, otherPool.Dulicate());
            }

            _filtersCollection.Copy(other._filtersCollection);
        }

#region Entities methods

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsEnitityInRange(int id) => id < _entites.Length;
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsEnitityInRange(EntityType entity) => IsEnitityInRange(entity.GetId());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ref EntityType GetRefById(int id)
        {
#if DEBUG
            if (id == EntityExtension.NullEntity.GetId() || !IsEnitityInRange(id))
                throw new EcsException("wrong entity id");
#endif
            return ref _entites[id];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ref EntityType GetRefById(EntityType other) => ref GetRefById(other.ToId());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public EntityType GetById(int id) => GetRefById(id);

        public bool IsDead(int id) => GetRefById(id).GetId() != id;

        public bool IsDead(EntityType entity) => IsDead(entity.ToId());

        private ref EntityType GetRecycled()
        {
            ref var curr = ref _recycleListHead;
            ref var next = ref GetRefById(curr);
            while (!next.IsNull())
            {
                curr = ref next;
                next = ref GetRefById(next);
            }

            next.SetId(curr);
            next.IncrementVersion();
            curr.SetNullId();
            return ref next;
        }

        public void Delete(EntityType entity)
        {
#if DEBUG
            if (IsDead(entity))
                throw new EcsException("trying to delete already dead entity");
            if (!IsEnitityInRange(entity))
                throw new EcsException("trying to delete wrong entity");
            if (entity.IsNull())
                throw new EcsException("trying to delete null entity");
#endif

            _filtersCollection.RemoveId(entity.ToId());

            //TODO: recycle all components here, because when recycled, entity will have all its previous components
            ref var recycleListEnd = ref _recycleListHead;
            while (!recycleListEnd.IsNull())
                recycleListEnd = ref GetRefById(recycleListEnd);
            recycleListEnd.SetId(entity);
            GetRefById(entity).SetNullId();
        }

        public EntityType Create()
        {
            if (!_recycleListHead.IsNull())
                return GetRecycled();

            var lastEntity = (EntityType)_entites.Length;
#if DEBUG
            if (lastEntity == EntityExtension.NullEntity)
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

        //TODO: probably its better to use mask to check
        public bool Have<T>(EntityType entity)
        {
            var componentId = ComponentMeta<T>.Id;
            if (!_componentsPools.ContainsKey(componentId))
                return false;
#if DEBUG
            if (_componentsPools[componentId] as ComponentsPool<T> == null
                && _componentsPools[componentId] as TagsPool<T> == null)
            {
                throw new EcsException("invalid pool");
            }
#endif
            return _componentsPools[componentId].Contains(entity);
        }

        private void AddIdToFlters(int id, HashSet<int> filterIds)
        {
            foreach (var filterId in filterIds)
            {
                var filter = _filtersCollection[filterId];

                var pass = _masks[id].InclusivePass(filter.Includes);
                pass &= _masks[id].ExclusivePass(filter.Excludes);
                if (!pass)
                    continue;
                filter.FilteredEntities.Add(id);
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
#if DEBUG
                if (!filter.FilteredEntities.Contains(id))
                    throw new EcsException("filter should contain this entity!");
#endif
                filter.FilteredEntities.Remove(id);
            }
        }

        private void UpdateFiltersOnAdd<T>(int id)
        {
            var componentId = ComponentMeta<T>.Id;
            if (_excludesUpdateSets.ContainsKey(componentId))
                RemoveIdFromFilters(id, _excludesUpdateSets[componentId]);

            _masks[id].Set(componentId);

            if (!_compsUpdateSets.ContainsKey(componentId))
                _compsUpdateSets.Add(componentId, new HashSet<int>(EcsCacheSettings.UpdateSetSize));
            AddIdToFlters(id, _compsUpdateSets[componentId]);
        }

        private void UpdateFiltersOnRemove<T>(int id)
        {
            var componentId = ComponentMeta<T>.Id;
            RemoveIdFromFilters(id, _compsUpdateSets[componentId]);
#if DEBUG
            if (_masks[id].Length <= componentId)
                throw new EcsException("there was no component ever");
#endif
            _masks[id].Unset(componentId);

            if (!_excludesUpdateSets.ContainsKey(componentId))
                _excludesUpdateSets.Add(componentId, new HashSet<int>(EcsCacheSettings.UpdateSetSize));
            AddIdToFlters(id, _excludesUpdateSets[componentId]);
        }

        public ref T AddComponent<T>(EntityType entity, T component = default)
        {
            int id = entity.ToId();

            UpdateFiltersOnAdd<T>(id);

            var componentId = ComponentMeta<T>.Id;
            if (!_componentsPools.ContainsKey(componentId))
                _componentsPools.Add(componentId, new ComponentsPool<T>(EcsCacheSettings.PoolSize));
            var pool = _componentsPools[componentId] as ComponentsPool<T>;
#if DEBUG
            if (pool == null)
                throw new EcsException("invalid pool");
#endif
            return ref pool.Add(entity, component);
        }

        public void AddTag<T>(EntityType entity)
        {
            int id = entity.ToId();

            UpdateFiltersOnAdd<T>(id);

            var componentId = ComponentMeta<T>.Id;
            if (!_componentsPools.ContainsKey(componentId))
                _componentsPools.Add(componentId, new TagsPool<T>(EcsCacheSettings.PoolSize));
            var pool = _componentsPools[componentId] as TagsPool<T>;
#if DEBUG
            if (pool == null)
                throw new EcsException("invalid pool");
#endif
            pool.Add(entity);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T GetComponent<T>(EntityType entity)
        {
            var pool = _componentsPools[ComponentMeta<T>.Id] as ComponentsPool<T>;
            return ref pool[entity];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveComponent<T>(EntityType entity)
        {
            UpdateFiltersOnRemove<T>(entity.ToId());
            _componentsPools[ComponentMeta<T>.Id].Remove(entity);
        }
        #endregion
        #region Filters methods

        private void AddFilterToUpdateSets(BitMask components, int filterIdx
            , UpdateSets sets)
        {
            var nextSetBit = components.GetNextSetBit(0);
            while (nextSetBit != -1)
            {
                if (!sets.ContainsKey(nextSetBit))
                    sets.Add(nextSetBit, new HashSet<int>(EcsCacheSettings.UpdateSetSize));

#if DEBUG
                if (sets[nextSetBit].Contains(filterIdx))
                    throw new EcsException("set already contains this filter!");
#endif

                sets[nextSetBit].Add(filterIdx);

                nextSetBit = components.GetNextSetBit(nextSetBit + 1);
            }
        }

        public int RegisterFilter(BitMask includes, BitMask excludes)
        {
            int filterId;
            if (_filtersCollection.TryAdd(includes, excludes, out filterId))
            {
                var filter = _filtersCollection[filterId];
                AddFilterToUpdateSets(filter.Includes, filterId, _compsUpdateSets);
                AddFilterToUpdateSets(filter.Excludes, filterId, _excludesUpdateSets);
            }

            return filterId;
        }

        public HashSet<int> GetFilteredEntitiesById(int id) => _filtersCollection[id].FilteredEntities;
#endregion
    }
}
