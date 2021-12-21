using System.Runtime.CompilerServices;

namespace ECS
{
    interface IComponentsPool
    {
        public int Length { get; }
        public bool Contains(int id);
        public void Remove(int id);
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
        public bool Contains(int id) => _components.Contains(id);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Remove(int id) => _components.Remove(id);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear() => _components.Clear();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Copy(in IComponentsPool other) => _components.Copy(((ComponentsPool<T>)other)._components);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IComponentsPool Duplicate()
        {
            var newPool = new ComponentsPool<T>(Length);
            newPool.Copy(this);
            return newPool;
        }
#endregion

        public ref T this[int id]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return ref _components[id]; }
        }

        public ComponentsPool(int initialCapacity = 0)
        {
            _components = new SparseSet<T>(initialCapacity);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T Add(int id, T value) => ref _components.Add(id, value);
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
        public void Remove(int id) => _tags.Unset(id);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear() => _tags.Clear();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Copy(in IComponentsPool other) => _tags.Copy(((TagsPool<T>)other)._tags);

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
        public void Add(int id) => _tags.Set(id);
    }
}
