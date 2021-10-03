using System.Runtime.CompilerServices;
using EntityType = System.UInt32;

namespace ECS
{
    interface IComponentsPool
    {
        public int Length { get; }
        public int IthEntity(int i);
        public bool Contains(int i);
        public bool Contains(EntityType entity);
        void Remove(EntityType entity);
    }

    class ComponentsPool<T> : IComponentsPool
    {
        private SparseSet<T> _components;

#region Interface implementation
        public int Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _components.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int IthEntity(int i) => _components.IthEntity(i);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(int i) => _components.Contains(i);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(EntityType entity) => _components.Contains(entity.ToIdx());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Remove(EntityType entity) => _components.Remove(entity.ToIdx());
        #endregion

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
        public ref T Add(EntityType entity, T value) => ref _components.Add(entity.ToIdx(), value);
    }

    class TagsPool<T> : IComponentsPool
    {
        private LightSparseSet<T> _tags;

#region Interface implementation
        public int Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _tags.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int IthEntity(int i) => _tags.IthEntity(i);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(int i) => _tags.Contains(i);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(EntityType entity) => _tags.Contains(entity.ToIdx());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Remove(EntityType entity) => _tags.Remove(entity.ToIdx());
        #endregion

        public TagsPool()
        {
            _tags = new LightSparseSet<T>();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(EntityType entity) => _tags.Add(entity.ToIdx());
    }
}
