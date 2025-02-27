using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CodexECS.Utility;
using EntityType = System.Int32;//duplicated in EntityExtension

namespace CodexECS
{
    public class ComponentManager
    {
        private IComponentsPool[] _pools;
        private int _poolsEnd;
        private struct DummyStruct { }
        private static readonly IComponentsPool DummyPool = new ComponentsPool<DummyStruct>();

        public ComponentManager()
        {
            _pools = new[] { DummyPool, DummyPool };
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsTypeRegistered<T>() => IsTypeRegistered(ComponentMeta<T>.Id);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsTypeRegistered(int componentId) =>
            componentId < _poolsEnd && _pools[componentId] != DummyPool;

        [Obsolete("slow, use Archetypes.Have instead")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Have<T>(EntityType eid) => Have(ComponentMeta<T>.Id, eid);

        [Obsolete("slow, use Archetypes.Have instead")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Have(int componentId, EntityType eid) => componentId < _poolsEnd && _pools[componentId] != DummyPool && _pools[componentId].Contains(eid);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add<T>(EntityType eid, T component = default)
        {
            var pool = GetPool<T>();

#if DEBUG && !ECS_PERF_TEST
            if (eid < 0)
                throw new EcsException("negative id");
            if (pool == null)
                throw new EcsException("pool is null");
#endif

            if (ComponentMeta<T>.IsTag)
                ((TagsPool<T>)pool).Add(eid);
            else
                ((ComponentsPool<T>)pool).Add(eid, component);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddReference(Type type, int id, object component)
        {
#if DEBUG && !ECS_PERF_TEST
            if (component is ValueType)
                throw new EcsException("trying to add object of value type as reference");
            if (id < 0)
                throw new EcsException("negative id");
#endif
            var componentId = ComponentMapping.TypeToId[type];

            var pool = GetPool(componentId);
#if DEBUG && !ECS_PERF_TEST
            if (pool == null)
                throw new EcsException("invalid pool");
#endif
            pool.AddReference(id, component);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T GetComponent<T>(EntityType eid)
        {
#if DEBUG && !ECS_PERF_TEST
            if (ComponentMeta<T>.IsTag)
                throw new EcsException(typeof(T) + " is tag component");
            if (!Have<T>(eid))
                throw new EcsException("entity have no " + typeof(T));
#endif
            //var pool = (ComponentsPool<T>)_componentsPools[ComponentMeta<T>.Id];
            var pool = (ComponentsPool<T>)_pools[ComponentMeta<T>.Id];
            return ref pool._values[pool._sparse[eid]];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Remove<T>(EntityType eid) => _pools[ComponentMeta<T>.Id].Remove(eid);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Remove(int componentId, EntityType eid) => _pools[componentId].Remove(eid);
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveAll<T>() => _pools[ComponentMeta<T>.Id].Clear();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveAll(int componentId) => _pools[componentId].Clear();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private IComponentsPool GetPool<T>()
        {
            var componentId = ComponentMeta<T>.Id;
            //if (_componentsPools.ContainsIdx(componentId))
            if (componentId < _poolsEnd && _pools[componentId] != DummyPool)
                return _pools[componentId];
            if (ComponentMeta<T>.IsTag)
                AddPool(componentId, new TagsPool<T>());
            else
                AddPool(componentId, new ComponentsPool<T>());
            return _pools[componentId];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IComponentsPool GetPool(int componentId)
        {
            if (!(componentId < _poolsEnd && _pools[componentId] != DummyPool))
                AddPool(componentId, PoolFactory.FactoryMethods[componentId](/*EcsCacheSettings.PoolSize*/32));
            return _pools[componentId];
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddPool(int idx, IComponentsPool pool)
        {
            if (idx >= _pools.Length)
            {
                var oldLength = _pools.Length;

                const int maxResizeDelta = 256;
                Utils.ResizeArray(idx, ref _pools, maxResizeDelta);
                for (int i = oldLength; i < _pools.Length; i++)
                    _pools[i] = DummyPool;
            }

#if DEBUG && !ECS_PERF_TEST
            if (_pools[idx] != DummyPool)
                throw new EcsException("already have pool at this index");
#endif

            _pools[idx] = pool;
            if (_poolsEnd <= idx)
                _poolsEnd = idx + 1;
        }

        public void GetTypesByMask(in BitMask mask, HashSet<Type> buffer)
        {
            buffer.Clear();
            foreach (var bit in mask)
                buffer.Add(_pools[bit].GetComponentType());
        }
    }
}