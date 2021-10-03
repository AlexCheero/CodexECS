using System;
using System.Runtime.CompilerServices;
using EntityType = System.UInt32;

//TODO: cover with tests
namespace ECS
{
    interface IComponentsPool
    {
        public int Length { get; }
        public int IthEntity(int i);
        public bool Contains(int i);
    }

    class ComponentsPool<T> : IComponentsPool
    {
        private SparseSet<T> _components;

        int IComponentsPool.Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _components.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        int IComponentsPool.IthEntity(int i) => _components.IthEntity(i);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool IComponentsPool.Contains(int i) => _components.Contains(i);

        public ref T this[EntityType entity]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return ref _components[entity.ToIdx()]; }
        }

        public ComponentsPool()
        {
            _components = new SparseSet<T>();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(EntityType entity) => _components.Contains(entity.ToIdx());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T Add(EntityType entity, T value) => ref _components.Add(entity.ToIdx(), value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Remove(EntityType entity) => _components.Remove(entity.ToIdx());
    }
}
