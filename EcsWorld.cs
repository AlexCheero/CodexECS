using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using EntityType = System.UInt32;
using UpdateSets =
    System.Collections.Generic.Dictionary<System.Type, System.Collections.Generic.HashSet<int>>;

//TODO: cover with tests
namespace ECS
{
    class EcsException : Exception
    {
        public EcsException(string msg) : base(msg) { }
    }

    public class EcsWorld
    {
        public EcsWorld(int entitiesReserved = 32)
        {
            //TODO: ensure that _entities and masks are always have same length
            _entites = new SimpleVector<EntityType>(entitiesReserved);
            _masks = new SimpleVector<BitArray>(entitiesReserved);
            _componentsPools = new Dictionary<Type, IComponentsPool>();
            
            _compsUpdateSets = new UpdateSets();
            _excludesUpdateSets = new UpdateSets();
            _filtersCollection = new FiltersCollection();
            _registeredComponents = new Dictionary<Type, int>();
        }

        //prealloc ctor
        public EcsWorld(EcsWorld other)
        {
            _entites = new SimpleVector<EntityType>(other._entites.Reserved);
            _masks = new SimpleVector<BitArray>(other._masks.Reserved);
            _componentsPools = new Dictionary<Type, IComponentsPool>();

            _compsUpdateSets = other._compsUpdateSets;
            _excludesUpdateSets = other._excludesUpdateSets;
            _filtersCollection = new FiltersCollection(other._filtersCollection.Length);
            _registeredComponents = other._registeredComponents;
        }

        public void Copy(in EcsWorld other)
        {
            _entites.Copy(other._entites);
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

            //update sets should be same for every copy of the world

            _filtersCollection.Copy(other._filtersCollection);
        }

#region Entities methods
        private SimpleVector<EntityType> _entites;
        private EntityType _recycleListHead = EntityExtension.NullEntity;

        private SimpleVector<BitArray> _masks;
        private Dictionary<Type, int> _registeredComponents;

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
            _masks.Add(new BitArray(_registeredComponents.Count));
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
#if DEBUG
            if (_componentsPools[key] as ComponentsPool<T> == null
                && _componentsPools[key] as TagsPool<T> == null)
            {
                throw new EcsException("invalid pool");
            }
#endif
            return _componentsPools[key].Contains(entity);
        }

        //TODO: implement custom bit array, to check masks without iterations
        private bool PassIncludeMask(BitArray includeMask, BitArray entityMask)
        {
            for (int i = 0; i < includeMask.Length; i++)
            {
                if (includeMask[i])
                {
                    if (i >= entityMask.Length || !entityMask[i])
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private bool PassExcludeMask(BitArray excludeMask, BitArray entityMask)
        {
            if (excludeMask == null)
                return true;

            for (int i = 0; i < excludeMask.Length; i++)
            {
                if (i >= entityMask.Length)
                    break;

                if (excludeMask[i])
                {
                    if (entityMask[i])
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private void AddIdToFlters(int id, HashSet<int> filterIds)
        {
            foreach (var filterId in filterIds)
            {
                var filter = _filtersCollection[filterId];
                //TODO: pass code duplicated in RemoveIdFromFilters. move to method
                var pass = PassIncludeMask(filter.CompsMask, _masks[id]);
                pass |= PassExcludeMask(filter.ExcludesMask, _masks[id]);
                if (!pass)
                    continue;
#if DEBUG
                if (filter.FilteredEntities.Contains(id))
                    throw new EcsException("filter should not contain this entity!");
#endif
                filter.FilteredEntities.Add(id);
            }
        }

        private void RemoveIdFromFilters(int id, HashSet<int> filterIds)
        {
            foreach (var filterId in filterIds)
            {
                var filter = _filtersCollection[filterId];

                var pass = PassIncludeMask(filter.CompsMask, _masks[id]);
                pass |= PassExcludeMask(filter.ExcludesMask, _masks[id]);
                if (!pass)
                    continue;
#if DEBUG
                if (!filter.FilteredEntities.Contains(id))
                    throw new EcsException("filter should contain this entity!");
#endif
                filter.FilteredEntities.Remove(id);
            }
        }

        private void UpdateFiltersOnAdd(Type key, int id)
        {
#if DEBUG
            if (!_registeredComponents.ContainsKey(key))
                throw new EcsException("component should be registered before adding it");
#endif
            if (_excludesUpdateSets.ContainsKey(key))
                RemoveIdFromFilters(id, _excludesUpdateSets[key]);

            var componentId = _registeredComponents[key];
            if (_masks[id].Length <= componentId)
                _masks[id].Length = componentId + 1;
            _masks[id].Set(componentId, true);

            AddIdToFlters(id, _compsUpdateSets[key]);
        }

        private void UpdateFiltersOnRemove(Type key, int id)
        {
#if DEBUG
            if (!_registeredComponents.ContainsKey(key))
                throw new EcsException("component should be registered before removing it");
#endif
            RemoveIdFromFilters(id, _compsUpdateSets[key]);

            var componentId = _registeredComponents[key];
#if DEBUG
            if (_masks[id].Length <= componentId)
                throw new EcsException("there was no component ever");
#endif
            _masks[id].Set(componentId, false);

            AddIdToFlters(id, _excludesUpdateSets[key]);
        }

        public ref T AddComponent<T>(EntityType entity, T component = default)
        {
            var key = typeof(T);

            int id = entity.ToId();

            UpdateFiltersOnAdd(key, id);

            if (!_componentsPools.ContainsKey(key))
                _componentsPools.Add(key, new ComponentsPool<T>());
            var pool = _componentsPools[key] as ComponentsPool<T>;
#if DEBUG
            if (pool == null)
                throw new EcsException("invalid pool");
#endif
            return ref pool.Add(entity, component);
        }

        public void AddTag<T>(EntityType entity)
        {
            var key = typeof(T);

            int id = entity.ToId();

            UpdateFiltersOnAdd(key, id);

            if (!_componentsPools.ContainsKey(key))
                _componentsPools.Add(key, new TagsPool<T>());
            var pool = _componentsPools[key] as TagsPool<T>;
#if DEBUG
            if (pool == null)
                throw new EcsException("invalid pool");
#endif
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

            int id = entity.ToId();

            UpdateFiltersOnRemove(key, id);

            _componentsPools[key].Remove(entity);
        }
        #endregion
        #region Filters methods
        //update sets holds indices of filters by types
        private UpdateSets _compsUpdateSets;
        private UpdateSets _excludesUpdateSets;
        private FiltersCollection _filtersCollection;

        private void AddFilterToUpdateSets(Type[] comps, int filterIdx
            , UpdateSets sets)
        {
            foreach (var comp in comps)
            {
                if (!sets.ContainsKey(comp))
                    sets.Add(comp, new HashSet<int>());

#if DEBUG
                if (sets[comp].Contains(filterIdx))
                    throw new EcsException("set already contains this filter!");
#endif

                sets[comp].Add(filterIdx);
            }
        }

        private void RegisterComponentsAndInitMask(out BitArray mask, Type[] comps)
        {
            mask = new BitArray(comps.Length);
            foreach (var comp in comps)
            {
                if (!_registeredComponents.ContainsKey(comp))
                    _registeredComponents.Add(comp, _registeredComponents.Count);
                var componentId = _registeredComponents[comp];
                if (mask.Length <= componentId)
                    mask.Length = componentId + 1;
                mask.Set(componentId, true);
            }
        }

        public int RegisterFilter(ref Type[] comps, ref Type[] excludes,
            out BitArray compsMask, out BitArray excludesMask)
        {
            int filterId;
            if (_filtersCollection.TryAdd(ref comps, ref excludes, out filterId))
            {
                var filter = _filtersCollection[filterId];
                RegisterComponentsAndInitMask(out filter.CompsMask, comps);
                if (excludes != null)
                    RegisterComponentsAndInitMask(out filter.ExcludesMask, excludes);
                compsMask = filter.CompsMask;
                excludesMask = filter.ExcludesMask;
                AddFilterToUpdateSets(filter.Comps, filterId, _compsUpdateSets);
                if (filter.Excludes != null)
                    AddFilterToUpdateSets(filter.Excludes, filterId, _excludesUpdateSets);
            }
            else
            {
                //TODO: seems like copypaste from above
                var filter = _filtersCollection[filterId];
                compsMask = filter.CompsMask;
                excludesMask = filter.ExcludesMask;
            }
            

            return filterId;
        }

        public HashSet<int> GetFilteredEntitiesById(int id) => _filtersCollection[id].FilteredEntities;
#endregion
    }
}
