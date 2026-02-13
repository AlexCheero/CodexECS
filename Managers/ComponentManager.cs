using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CodexECS.Utility;
using EntityType = System.Int32;//duplicated in EntityExtension

namespace CodexECS
{
    public class ComponentManager
    {
        public IComponentsPool[] _pools;
        private int _poolsEnd;
        private struct DummyStruct { }
        private static readonly IComponentsPool DummyPool = new ComponentsPool<DummyStruct>(0);

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
        
        [Obsolete("slow, use Archetypes.Have instead")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Have(in BitMask mask, EntityType eid)
        {
            var have = true;
            foreach (var componentId in mask)
                have &= componentId < _poolsEnd && _pools[componentId] != DummyPool && _pools[componentId].Contains(eid);
            return have;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add<T>(EntityType eid, T component = default)
        {
            var componentId = ComponentMeta<T>.Id;
            var shouldAddNewPool = componentId >= _poolsEnd || _pools[componentId] == DummyPool;
            if (ComponentMeta<T>.IsTag)
            {
                if (shouldAddNewPool)
                    AddPool(componentId, new TagsPool<T>());
                ((TagsPool<T>)_pools[componentId]).Add(eid);
            }
            else
            {
                if (shouldAddNewPool)
                    AddPool(componentId, new ComponentsPool<T>());
                ((ComponentsPool<T>)_pools[componentId]).Add(eid, component);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T Get<T>(EntityType eid)
        {
#if DEBUG && !ECS_PERF_TEST
            if (ComponentMeta<T>.IsTag)
                throw new EcsException("can't get specific component from tags pool");
#endif
            var pool = (ComponentsPool<T>)_pools[ComponentMeta<T>.Id];

            //return ref pool.Get(eid);
            return ref pool.Values[pool.Sparse[eid]];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T GetNextFree<T>()
        {
#if DEBUG && !ECS_PERF_TEST
            if (ComponentMeta<T>.IsTag)
                throw new EcsException("can only be called for non empty components");
#endif

            var componentId = ComponentMeta<T>.Id;
            var shouldAddPool = componentId >= _poolsEnd || _pools[componentId] == DummyPool;
            if (shouldAddPool)
                AddPool(componentId, new ComponentsPool<T>());
            return ref ((ComponentsPool<T>)_pools[componentId]).GetNextFree();
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
        public IComponentsPool GetPool(int componentId)
        {
            if (!(componentId < _poolsEnd && _pools[componentId] != DummyPool))
                AddPool(componentId, PoolFactory.FactoryMethods[componentId]());
            return _pools[componentId];
        }

        public Action<IComponentsPool[]> OnPoolsResized;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddPool(int idx, IComponentsPool pool)
        {
            if (idx >= _pools.Length)
            {
                var oldLength = _pools.Length;

                const int maxResizeDelta = 256;
                Utils.ResizeArray(idx, ref _pools, maxResizeDelta);
                OnPoolsResized(_pools);
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