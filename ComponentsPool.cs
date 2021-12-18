using System.Runtime.CompilerServices;
using EntityType = System.UInt32;

namespace ECS
{
    interface IComponentsPool
    {
        public int Length { get; }
        public bool Contains(int i);
        public bool Contains(EntityType entity);
        public void Remove(EntityType entity);
        public void Clear();
        public void Copy(in IComponentsPool other);
        public IComponentsPool Duplicate();
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
        public bool Contains(int i) => _components.Contains(i);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(EntityType entity) => _components.Contains(entity.ToId());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Remove(EntityType entity) => _components.Remove(entity.ToId());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear() => _components.Clear();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Copy(in IComponentsPool other)
        {
            var otherPool = other as ComponentsPool<T>;
#if DEBUG
            if (otherPool == null)
                throw new EcsException("trying to copy from pool of different type");
#endif
            _components.Copy(otherPool._components);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IComponentsPool Duplicate()
        {
            var newPool = new ComponentsPool<T>(Length);
            newPool.Copy(this);
            return newPool;
        }
#endregion

        public ref T this[EntityType entity]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return ref _components[entity.ToId()]; }
        }

        public ComponentsPool(int initialCapacity = 0)
        {
            _components = new SparseSet<T>(initialCapacity);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T Add(EntityType entity, T value) => ref _components.Add(entity.ToId(), value);
    }

    class TagsPool<T> : IComponentsPool
    {
        private BitMask _tags;

#region Interface implementation
        public int Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _tags.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(int i) => _tags.Check(i);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(EntityType entity) => Contains(entity.ToId());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Remove(EntityType entity) => _tags.Unset(entity.ToId());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear() => _tags.Clear();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Copy(in IComponentsPool other)
        {
            var otherPool = other as TagsPool<T>;
#if DEBUG
            if (otherPool == null)
                throw new EcsException("trying to copy from pool of different type");
#endif
            _tags.Copy(otherPool._tags);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IComponentsPool Duplicate()
        {
            var newPool = new TagsPool<T>(Length);
            newPool.Copy(this);
            return newPool;
        }
#endregion

        public TagsPool(int initialCapacity = 0)
        {
            _tags = new BitMask();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(EntityType entity) => _tags.Set(entity.ToId());
    }
}
