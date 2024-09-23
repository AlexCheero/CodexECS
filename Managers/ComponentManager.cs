using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using EntityType = System.Int32;//duplicated in EntityExtension

namespace CodexECS
{
    public class ComponentManager
    {
        private Dictionary<int, IComponentsPool> _componentsPools;

        public ComponentManager()
        {
            _componentsPools = new();
        }

        [Obsolete("slow, use Archetypes.Have instead")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Have<T>(EntityType eid) => Have(ComponentMeta<T>.Id, eid);

        [Obsolete("slow, use Archetypes.Have instead")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Have(int componentId, EntityType eid) => _componentsPools.ContainsKey(componentId) && _componentsPools[componentId].Contains(eid);

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
            var pool = (ComponentsPool<T>)_componentsPools[ComponentMeta<T>.Id];
            return ref pool._values[pool._sparse[eid]];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Remove<T>(EntityType eid) => _componentsPools[ComponentMeta<T>.Id].Remove(eid);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Remove(int componentId, EntityType eid) => _componentsPools[componentId].Remove(eid);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private IComponentsPool GetPool<T>()
        {
            var componentId = ComponentMeta<T>.Id;
            if (_componentsPools.TryGetValue(componentId, out var pool))
                return pool;
            if (ComponentMeta<T>.IsTag)
                _componentsPools[componentId] = new TagsPool<T>();
            else
                _componentsPools[componentId] = new ComponentsPool<T>();
            return _componentsPools[componentId];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IComponentsPool GetPool(int componentId)
        {
            if (!_componentsPools.ContainsKey(componentId))
                _componentsPools.Add(componentId, PoolFactory.FactoryMethods[componentId](/*EcsCacheSettings.PoolSize*/32));
            return _componentsPools[componentId];
        }

#if DEBUG
        public void GetTypesByMask(in BitMask mask, HashSet<Type> buffer)
        {
            buffer.Clear();
            foreach (var bit in mask)
                buffer.Add(_componentsPools[bit].GetComponentType());
        }
#endif
    }
}