using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using EntityType = System.UInt32;

//TODO: cover with tests
namespace ECS
{
    class EcsException : Exception
    {
        public EcsException(string msg) : base(msg) { }
    }

    static class EcsExceptionThrower
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ThrowException(string message)
        {
            throw new EcsException(message);
        }
    }

    class EcsWorld
    {
        public EcsWorld(int entitiesReserved = 32)
        {
            _entites = new SimpleVector<EntityType>(entitiesReserved);
            _componentsPools = new Dictionary<Type, IComponentsPool>();
            
            //TODO: don't forget to copy
            _compsUpdateSets = new Dictionary<Type, HashSet<HashSet<int>>>();
            _excludesUpdateSets = new Dictionary<Type, HashSet<HashSet<int>>>();
            _filtersCollection = new FiltersCollection();
        }

        public void Copy(in EcsWorld other)
        {
            _entites.Copy(other._entites);
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
        }

#region Entities methods
        private SimpleVector<EntityType> _entites;
        private EntityType _recycleListHead = EntityExtension.NullEntity;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsEnitityInRange(int id) => id < _entites.Length;
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsEnitityInRange(EntityType entity) => IsEnitityInRange(entity.GetId());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ref EntityType GetRefById(int id)
        {
            if (id == EntityExtension.NullEntity.GetId() || !IsEnitityInRange(id))
                EcsExceptionThrower.ThrowException("wrong entity id");
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
            if (IsDead(entity))
                EcsExceptionThrower.ThrowException("trying to delete already dead entity");
            if (!IsEnitityInRange(entity))
                EcsExceptionThrower.ThrowException("trying to delete wrong entity");
            if (entity.IsNull())
                EcsExceptionThrower.ThrowException("trying to delete null entity");
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
            if (lastEntity == EntityExtension.NullEntity)
                EcsExceptionThrower.ThrowException("entity limit reached");
            if (_entites.Length < 0)
                EcsExceptionThrower.ThrowException("entities vector length overflow");
            if (lastEntity.GetVersion() > 0)
                EcsExceptionThrower.ThrowException("lastEntity version should always be 0");
            
            _entites.Add(lastEntity);
            return _entites[_entites.Length - 1];
        }
#endregion

#region Components methods
        private Dictionary<Type, IComponentsPool> _componentsPools;

        public bool Have<T>(EntityType entity)
        {
            var key = typeof(T);
            if (!_componentsPools.ContainsKey(key))
                return false;
            if (_componentsPools[key] as ComponentsPool<T> == null
                && _componentsPools[key] as TagsPool<T> == null)
            {
                EcsExceptionThrower.ThrowException("invalid pool");
            }
            return _componentsPools[key].Contains(entity);
        }

        public ref T AddComponent<T>(EntityType entity, T component = default)
        {
            var key = typeof(T);

            var updateFilters = _compsUpdateSets[key];
            foreach (var filter in updateFilters)
            {
                int id = entity.ToId();
                if (filter.Contains(id))//TODO: probably this check is redundant
                    filter.Remove(id);//TODO: add to add lists and remove from remove lists and vice versa for remove component
            }

            if (!_componentsPools.ContainsKey(key))
                _componentsPools.Add(key, new ComponentsPool<T>());
            var pool = _componentsPools[key] as ComponentsPool<T>;
            if (pool == null)
                EcsExceptionThrower.ThrowException("invalid pool");
            return ref pool.Add(entity, component);
        }

        public void AddTag<T>(EntityType entity)
        {
            //same as for AddComponent

            var key = typeof(T);

            var updateFilters = _compsUpdateSets[key];
            foreach (var filter in updateFilters)
            {
                int id = entity.ToId();
                if (filter.Contains(id))//TODO: probably this check is redundant
                    filter.Remove(id);
            }

            if (!_componentsPools.ContainsKey(key))
                _componentsPools.Add(key, new TagsPool<T>());
            var pool = _componentsPools[key] as TagsPool<T>;
            if (pool == null)
                EcsExceptionThrower.ThrowException("invalid pool");
            pool.Add(entity);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T GetComponent<T>(EntityType entity)
        {
            var key = typeof(T);
            var pool = _componentsPools[key] as ComponentsPool<T>;
            return ref pool[entity];
        }

        public void RemoveComponent<T>(EntityType entity)
        {
            var key = typeof(T);

            var updateFilters = _excludesUpdateSets[key];
            foreach (var filter in updateFilters)
            {
                int id = entity.ToId();
                if (filter.Contains(id))//TODO: probably this check is redundant
                    filter.Remove(id);
            }

            _componentsPools[key].Remove(entity);
        }
        #endregion
        #region Filters methods
        private Dictionary<Type, HashSet<HashSet<int>>> _compsUpdateSets;
        private Dictionary<Type, HashSet<HashSet<int>>> _excludesUpdateSets;
        /* TODO:
         * on RegisterFilter filter should be checked if it already in this collection (like in FiltersCollection)
         * and added if not, RegisterFilter's filter parameter should be ref, to replace it with already existing one
         * so that systems with same filters share common filter
         * update lists should contain only indices for filters in this internal collection and, therefore it's type
         * should be Dictionary<Type, List<int>> or use HashSet (maybe only in debug) to make sure it has no duplicates
         * than, on entitiy deletion, it should be quite fast to iterate over all internal collection, to remove entity
         * from every filter it belongs to
         */
        private FiltersCollection _filtersCollection;

        private void AddFilterToUpdateSets(Type[] comps, HashSet<int> filter, Dictionary<Type, HashSet<HashSet<int>>> sets)
        {
            foreach (var comp in comps)
            {
                if (!sets.ContainsKey(comp))
                    sets.Add(comp, new HashSet<HashSet<int>>());

                if (sets[comp].Contains(filter))
                    EcsExceptionThrower.ThrowException("set already contains this filter!");

                sets[comp].Add(filter);
            }
        }

        public void RegisterFilter(Type[] comps, Type[] excludes, ref HashSet<int> filter)
        {
            bool addded = _filtersCollection.GetOrAdd(comps, excludes, ref filter);
            if (addded)
            {
                AddFilterToUpdateSets(comps, filter, _compsUpdateSets);
                if (excludes != null)
                    AddFilterToUpdateSets(excludes, filter, _excludesUpdateSets);
            }
        }
#endregion
    }
}
