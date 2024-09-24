using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CodexECS.Utility;
using EntityType = System.Int32;//duplicated in EntityExtension

namespace CodexECS
{
    public class ComponentManager
    {
        //private SparseSet<IComponentsPool> _componentsPools;
        private int[] _poolsIndices;
        private readonly SimpleList<IComponentsPool> _pools;

        public ComponentManager()
        {
            _poolsIndices = new[] { -1, -1 };
            _pools = new(2);
        }

        [Obsolete("slow, use Archetypes.Have instead")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Have<T>(EntityType eid) => Have(ComponentMeta<T>.Id, eid);

        [Obsolete("slow, use Archetypes.Have instead")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Have(int componentId, EntityType eid)
        {
            //return _componentsPools.ContainsIdx(componentId) && _componentsPools[componentId].Contains(eid);
            return (componentId < _poolsIndices.Length && _poolsIndices[componentId] > -1) &&
                   (_pools[_poolsIndices[componentId]]).Contains(eid);
        }

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
            var pool = (ComponentsPool<T>)_pools[_poolsIndices[ComponentMeta<T>.Id]];
            return ref pool._values[pool._sparse[eid]];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Remove<T>(EntityType eid) => _pools[_poolsIndices[ComponentMeta<T>.Id]].Remove(eid);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Remove(int componentId, EntityType eid) => _pools[_poolsIndices[componentId]].Remove(eid);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private IComponentsPool GetPool<T>()
        {
            var componentId = ComponentMeta<T>.Id;
            //if (_componentsPools.ContainsIdx(componentId))
            if (componentId < _poolsIndices.Length && _poolsIndices[componentId] > -1)
                return _pools[_poolsIndices[componentId]];
            if (ComponentMeta<T>.IsTag)
                AddPool(componentId, new TagsPool<T>());
            else
                AddPool(componentId, new ComponentsPool<T>());
            return _pools[_poolsIndices[componentId]];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IComponentsPool GetPool(int componentId)
        {
            if (!(componentId < _poolsIndices.Length && _poolsIndices[componentId] > -1))
                AddPool(componentId, PoolFactory.FactoryMethods[componentId](/*EcsCacheSettings.PoolSize*/32));
            return _pools[_poolsIndices[componentId]];
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddPool(int outerIdx, IComponentsPool pool)
        {
            if (outerIdx >= _poolsIndices.Length)
            {
                var oldLength = _poolsIndices.Length;

                const int maxResizeDelta = 256;
                Utils.ResizeArray(outerIdx, ref _poolsIndices, maxResizeDelta);
                for (int i = oldLength; i < _poolsIndices.Length; i++)
                    _poolsIndices[i] = -1;
            }

#if DEBUG && !ECS_PERF_TEST
            if (_poolsIndices[outerIdx] > -1)
                throw new EcsException("already have pool at this index");
#endif

            _poolsIndices[outerIdx] = _pools.Length;
            _pools.Add(pool);
        }

#if DEBUG
        public void GetTypesByMask(in BitMask mask, HashSet<Type> buffer)
        {
            buffer.Clear();
            foreach (var bit in mask)
                buffer.Add(_pools[_poolsIndices[bit]].GetComponentType());
        }
#endif
    }
}