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
            //_filtersGraph = new Node<Type, SimpleVector<int>>();
            _filters = new FiltersCollection();
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

        //TODO: not sure about the way to store pools and get keys for them
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Type TypeKey<T>() => default(T).GetType();

        public bool Have<T>(EntityType entity)
        {
            var key = TypeKey<T>();
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
            var key = TypeKey<T>();

            var updateFilters = _addUpdateLists[key];
            for (int i = 0; i < updateFilters.Count; i++)
            {
                var filter = updateFilters[i];
                int id = entity.ToId();
                if (filter.Contains(id))//TODO: probably this check is redundant
                    filter.Remove(id);
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

            var key = TypeKey<T>();

            var updateFilters = _addUpdateLists[key];
            for (int i = 0; i < updateFilters.Count; i++)
            {
                var filter = updateFilters[i];
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
            var key = TypeKey<T>();
            var pool = _componentsPools[key] as ComponentsPool<T>;
            return ref pool[entity];
        }

        public void RemoveComponent<T>(EntityType entity)
        {
            var key = TypeKey<T>();

            var updateFilters = _removeUpdateLists[key];
            for (int i = 0; i < updateFilters.Count; i++)
            {
                var filter = updateFilters[i];
                int id = entity.ToId();
                if (filter.Contains(id))//TODO: probably this check is redundant
                    filter.Remove(id);
            }

            _componentsPools[key].Remove(entity);
        }
        #endregion
        #region Filters methods
        //private GraphNode<Type, SimpleVector<int>> _filtersGraph;

        private FiltersCollection _filters;

        //TODO: init in ctor
        //TODO: don't forget to copy update lists
        Dictionary<Type, List<HashSet<int>>> _addUpdateLists = new Dictionary<Type, List<HashSet<int>>>();
        Dictionary<Type, List<HashSet<int>>> _removeUpdateLists = new Dictionary<Type, List<HashSet<int>>>();

        public void RegisterFilter(Type[] comps, Type[] excludes, HashSet<int> filter)
        {
            for (int i = 0; i < comps.Length; i++)
            {
                if (!_addUpdateLists.ContainsKey(comps[i]))
                    _addUpdateLists.Add(comps[i], new List<HashSet<int>>());
                _addUpdateLists[comps[i]].Add(filter);//TODO: remove duplicates
            }

            if (excludes == null || excludes.Length < 1)
                return;

            for (int i = 0; i < excludes.Length; i++)
            {
                if (!_removeUpdateLists.ContainsKey(excludes[i]))
                    _removeUpdateLists.Add(excludes[i], new List<HashSet<int>>());
                _removeUpdateLists[excludes[i]].Add(filter);//TODO: remove duplicates
            }
        }

        //TODO: maybe shouldn't use HashSet and use sorted array with uniqueness check
        public HashSet<int> GetView(IEnumerable<Type> comps, IEnumerable<Type> excludes)
        {
            return _filters.Get(comps, excludes);
        }
        //TODO: implement sortable groups
        //TODO: probably it is better to register all needed views at start, and update them on adding/removing
        //      components, using some kind of filter graph (somewhat similar to flecs)
        public void GetView(ref SimpleVector<int> filter, in Type[] types, in Type[] excludes = null)
        {
            filter.Clear();
            if (types.Length == 0)
                return;

            var firstPool = _componentsPools[types[0]];
            for (int i = 0; i < firstPool.Length; i++)
            {
                bool belongs = true;
                var id = firstPool.IthEntityId(i);
                if (IsDead(id))
                    continue;

                for (int j = 1; j < types.Length && belongs; j++)
                {
                    var pool = _componentsPools[types[j]];
                    belongs &= pool.Contains(id);
                }
                for (int j = 0; excludes != null && j < excludes.Length && belongs; j++)
                {
                    var pool = _componentsPools[excludes[j]];
                    belongs &= !pool.Contains(id);
                }
                if (belongs)
                    filter.Add(id);
            }
        }
#endregion
    }
}
