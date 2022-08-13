using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using EntityType = System.Int32;

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
        private SimpleVector<EntityType> _entites;
        private EntityType _recycleListHead = EntityExtension.NullEntity;

        private SimpleVector<BitMask> _masks;

        private SparseSet<IComponentsPool> _componentsPools;

        //update sets holds indices of filters by types
        private Dictionary<int, HashSet<int>> _includeUpdateSets;
        private Dictionary<int, HashSet<int>> _excludeUpdateSets;
        private FiltersCollection _filtersCollection;

        public EcsWorld(int entitiesReserved = 32)
        {
            //TODO: ensure that _entities and masks are always have same length
            _entites = new SimpleVector<EntityType>(entitiesReserved);
            _masks = new SimpleVector<BitMask>(entitiesReserved);
            _componentsPools = new SparseSet<IComponentsPool>(EcsCacheSettings.PoolsCount);
            
            _includeUpdateSets = new Dictionary<int, HashSet<int>>();
            _excludeUpdateSets = new Dictionary<int, HashSet<int>>();
            _filtersCollection = new FiltersCollection();
        }

        //prealloc ctor
        public EcsWorld(EcsWorld other)
        {
            _entites = new SimpleVector<EntityType>(other._entites.Reserved);
            _masks = new SimpleVector<BitMask>(other._masks.Reserved);
            _componentsPools = new SparseSet<IComponentsPool>(other._componentsPools.Length);

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

        private int ByteLength()
        {
            #region entites and recycle list
            int size = 2 * sizeof(int);/*_entites.Length and _entites._elements.Length*/
            size += sizeof(EntityType) * _entites.Length;
            size += sizeof(EntityType);/*_recycleListHead*/
            #endregion

            #region masks
            size += 2 * sizeof(int);/*_masks.Length and _masks._elements.Length*/
            for (int i = 0; i < _masks.Length; i++)
                size += _masks[i].ByteLength;
            #endregion

            #region components pools
            size += sizeof(int) + _componentsPools._sparse.Length * sizeof(int);
            size += 2 * sizeof(int);/*_componentsPools._values.Length and _componentsPools._values._elements.Length*/
            for (int i = 0; i < _componentsPools._values.Length; i++)
                size += _componentsPools._values[i].ByteLength();
            size += 2 * sizeof(int);/*_componentsPools._dense.Length and _componentsPools._dense._elements.Length*/
            size += sizeof(int) * _componentsPools._dense.Length;
            #endregion

            #region update sets
            size += sizeof(int) + sizeof(int) * _includeUpdateSets.Count;
            foreach (var pair in _includeUpdateSets)
                size += sizeof(int) + sizeof(int) * pair.Value.Count; /*pair.Value.Count + values in set*/

            size += sizeof(int) + sizeof(int) * _excludeUpdateSets.Count;
            foreach (var pair in _excludeUpdateSets)
                size += sizeof(int) + sizeof(int) * pair.Value.Count; /*pair.Value.Count + values in set*/
            #endregion

            size += _filtersCollection.ByteLength();

            return size;
        }

        public byte[] Serialize()
        {
            var bytes = new byte[ByteLength()];

            int startIndex = 0;

            #region entites and recycle list
            BinarySerializer.SerializeInt(_entites.Length, bytes, ref startIndex);
            BinarySerializer.SerializeInt(_entites._elements.Length, bytes, ref startIndex);
            for (int i = 0; i < _entites.Length; i++)
                BinarySerializer.SerializeInt(_entites[i], bytes, ref startIndex);
            BinarySerializer.SerializeInt(_recycleListHead, bytes, ref startIndex);
            #endregion

            #region masks
            BinarySerializer.SerializeInt(_masks.Length, bytes, ref startIndex);
            BinarySerializer.SerializeInt(_masks._elements.Length, bytes, ref startIndex);
            for (int i = 0; i < _masks.Length; i++)
                _masks[i].Serialize(bytes, ref startIndex);
            #endregion

            #region components pools
            BinarySerializer.SerializeInt(_componentsPools._sparse.Length, bytes, ref startIndex);
            BinarySerializer.SerializeIntegerArray(_componentsPools._sparse, bytes, ref startIndex);

            BinarySerializer.SerializeInt(_componentsPools._values.Length, bytes, ref startIndex);
            BinarySerializer.SerializeInt(_componentsPools._values._elements.Length, bytes, ref startIndex);

            for (int i = 0; i < _componentsPools._values.Length; i++)
            {
                BinarySerializer.SerializeInt(_componentsPools._dense[i], bytes, ref startIndex);
                _componentsPools._values[i].Serialize(bytes, ref startIndex);
            }
            #endregion

            #region update sets
            BinarySerializer.SerializeInt(_includeUpdateSets.Count, bytes, ref startIndex);
            foreach (var key in _includeUpdateSets.Keys)
                BinarySerializer.SerializeInt(key, bytes, ref startIndex);
            foreach (var set in _includeUpdateSets.Values)
            {
                BinarySerializer.SerializeInt(set.Count, bytes, ref startIndex);
                foreach (var idx in set)
                    BinarySerializer.SerializeInt(idx, bytes, ref startIndex);
            }

            BinarySerializer.SerializeInt(_excludeUpdateSets.Count, bytes, ref startIndex);
            foreach (var key in _excludeUpdateSets.Keys)
                BinarySerializer.SerializeInt(key, bytes, ref startIndex);
            foreach (var set in _excludeUpdateSets.Values)
            {
                BinarySerializer.SerializeInt(set.Count, bytes, ref startIndex);
                foreach (var idx in set)
                    BinarySerializer.SerializeInt(idx, bytes, ref startIndex);
            }
            #endregion

            _filtersCollection.Serialize(bytes, ref startIndex);

            return bytes;
        }

        public void Deserialize(byte[] bytes)
        {
            int startIndex = 0;

            #region entites and recycle list
            _entites._end = BinarySerializer.DeserializeInt(bytes, ref startIndex);
            var entitesElementsLength = BinarySerializer.DeserializeInt(bytes, ref startIndex);
            _entites._elements = new EntityType[entitesElementsLength];
            for (int i = 0; i < _entites.Length; i++)
                _entites[i] = BinarySerializer.DeserializeInt(bytes, ref startIndex);
            _recycleListHead = BinarySerializer.DeserializeInt(bytes, ref startIndex);
            #endregion

            #region masks
            _masks._end = BinarySerializer.DeserializeInt(bytes, ref startIndex);
            var masksElementsLength = BinarySerializer.DeserializeInt(bytes, ref startIndex);
            _masks._elements = new BitMask[masksElementsLength];
            for (int i = 0; i < _masks.Length; i++)
                _masks[i].Deserialize(bytes, ref startIndex);
            #endregion

            #region components pools
            var poolsSparseLength = BinarySerializer.DeserializeInt(bytes, ref startIndex);
            _componentsPools._sparse = BinarySerializer.DeserializeIntegerArray(bytes, ref startIndex, poolsSparseLength);
            
            _componentsPools._values._end = BinarySerializer.DeserializeInt(bytes, ref startIndex);
            var poolsValuesElementsLength = BinarySerializer.DeserializeInt(bytes, ref startIndex);
            _componentsPools._values._elements = new IComponentsPool[poolsValuesElementsLength];

            _componentsPools._dense._end = _componentsPools._values._end;
            _componentsPools._dense._elements = new int[poolsValuesElementsLength];

            for (int i = 0; i < _componentsPools._values.Length; i++)
            {
                int typeIdx = BinarySerializer.DeserializeInt(bytes, ref startIndex);
                _componentsPools._dense[i] = typeIdx;

                IComponentsPool pool = ComponentRegistartor.CreatePool(typeIdx);
                pool.Deserialize(bytes, ref startIndex);
                _componentsPools._values[i] = pool;
            }

            #endregion

            #region update sets
            _includeUpdateSets.Clear();

            var includeUpdateSetsCount = BinarySerializer.DeserializeInt(bytes, ref startIndex);
            for (int i = includeUpdateSetsCount; i > 0; i--)
            {
                var key = BinarySerializer.DeserializeInt(bytes, ref startIndex);
                _includeUpdateSets[key] = new HashSet<int>();
            }
            foreach (var set in _includeUpdateSets.Values)
            {
                var count = BinarySerializer.DeserializeInt(bytes, ref startIndex);
                for (int i = 0; i < count; i++)
                    set.Add(BinarySerializer.DeserializeInt(bytes, ref startIndex));
            }

            _excludeUpdateSets.Clear();

            var excludeUpdateSetsCount = BinarySerializer.DeserializeInt(bytes, ref startIndex);
            for (int i = excludeUpdateSetsCount; i > 0; i--)
            {
                var key = BinarySerializer.DeserializeInt(bytes, ref startIndex);
                _excludeUpdateSets[key] = new HashSet<int>();
            }
            foreach (var set in _excludeUpdateSets.Values)
            {
                var count = BinarySerializer.DeserializeInt(bytes, ref startIndex);
                for (int i = 0; i < count; i++)
                    set.Add(BinarySerializer.DeserializeInt(bytes, ref startIndex));
            }
            #endregion

            _filtersCollection.Deserialize(bytes, ref startIndex);
        }

#region Entities methods

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsEnitityInRange(int id) => id < _entites.Length;
        
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
        public EntityType GetById(int id) => GetRefById(id);

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
        public bool IsEntityValid(EntityType entity)
        {
            var id = entity.GetId();
            return !IsDead(id) && entity.GetVersion() == GetById(id).GetVersion();
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

            next.SetId(curr.ToId());
            next.IncrementVersion();
            curr.SetNullId();
            return next.ToId();
        }

        public void Delete(int id)
        {
            ref EntityType entity = ref GetRefById(id);
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
                RemoveComponent(id, bit);

            _filtersCollection.RemoveId(id);

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

            var lastEntity = _entites.Length;
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
            return _entites.Length - 1;
        }
        #endregion

        #region Components methods
        //TODO: add reactive callbacks

#if DEBUG
        private void CheckRegistration<T>()
        {
            if (!ComponentRegistartor.IsRegistered<T>())
                throw new EcsException("component not registered: " + typeof(T));
        }
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Have<T>(int id)
        {
#if DEBUG
            CheckRegistration<T>();
#endif

            return _masks[id].Check(ComponentMeta<T>.Id);
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
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP2_0_OR_GREATER || NET5_0_OR_GREATER || NET472_OR_GREATER
                _includeUpdateSets.Add(componentId, new HashSet<int>(EcsCacheSettings.UpdateSetSize));
#else
                _includeUpdateSets.Add(componentId, new HashSet<int>());
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
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP2_0_OR_GREATER || NET5_0_OR_GREATER || NET472_OR_GREATER
                _excludeUpdateSets.Add(componentId, new HashSet<int>(EcsCacheSettings.UpdateSetSize));
#else
                _excludeUpdateSets.Add(componentId, new HashSet<int>());               
#endif
            }
            AddIdToFlters(id, _excludeUpdateSets[componentId]);
        }

        public ref T AddComponent<T>(int id, T component = default)
        {
#if DEBUG
            CheckRegistration<T>();
#endif

            UpdateFiltersOnAdd<T>(id);

            var componentId = ComponentMeta<T>.Id;
            if (!_componentsPools.Contains(componentId))
                _componentsPools.Add(componentId, new ComponentsPool<T>(EcsCacheSettings.PoolSize));
            var pool = (ComponentsPool<T>)_componentsPools[componentId];
#if DEBUG
            if (pool == null)
                throw new EcsException("invalid pool");
#endif
            return ref pool.Add(id, component);
        }

        public void AddTag<T>(int id)
        {
#if DEBUG
            CheckRegistration<T>();
#endif

            UpdateFiltersOnAdd<T>(id);

            var componentId = ComponentMeta<T>.Id;
            if (!_componentsPools.Contains(componentId))
                _componentsPools.Add(componentId, new TagsPool<T>(EcsCacheSettings.PoolSize));
            var pool = (TagsPool<T>)_componentsPools[componentId];
#if DEBUG
            if (pool == null)
                throw new EcsException("invalid pool");
#endif
            pool.Add(id);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T GetComponent<T>(int id)
        {
#if DEBUG
            CheckRegistration<T>();

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
                if (IsEntityValid(entity))
                {
                    var id = entity.GetId();
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
        public void RemoveComponent<T>(int id)
        {
#if DEBUG
            CheckRegistration<T>();
#endif

            RemoveComponent(id, ComponentMeta<T>.Id);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RemoveComponent(int id, int componentId)
        {
            UpdateFiltersOnRemove(componentId, id);
            _componentsPools[componentId].Remove(id);
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
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP2_0_OR_GREATER || NET5_0_OR_GREATER || NET472_OR_GREATER
                    sets.Add(bit, new HashSet<int>(EcsCacheSettings.UpdateSetSize));
#else
                    sets.Add(bit, new HashSet<int>());                   
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

        public EcsFilter GetFilter(int id) => _filtersCollection[id];
#endregion
    }
}
