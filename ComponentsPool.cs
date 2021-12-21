using System;
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
        private int[] _sparse;
        private SimpleVector<T> _values;

        #region Interface implementation
        public int Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _values.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(int id) => id < _sparse.Length && _sparse[id] > -1;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Remove(int id)
        {
            var innerIndex = _sparse[id];
            _sparse[id] = -1;
            _values.Remove(innerIndex);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
#if UNITY
            for (int i = 0; i < _sparse.Length; i++)
                _sparse[i] = -1;
#else
            Array.Fill(_sparse, -1);
#endif

            _values.Clear();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Copy(in IComponentsPool other)
        {
            var otherPool = (ComponentsPool<T>)other;
            if (_sparse.Length < otherPool._sparse.Length)
                Array.Resize(ref _sparse, otherPool._sparse.Length);
            else if (_sparse.Length > otherPool._sparse.Length)
            {
#if UNITY
                for (int i = otherPool._sparse.Length; i < _sparse.Length; i++)
                    _sparse[i] = -1;
#else
                Array.Fill(_sparse, -1, otherPool._sparse.Length, _sparse.Length - otherPool._sparse.Length);
#endif
            }
            Array.Copy(otherPool._sparse, _sparse, otherPool._sparse.Length);

            _values.Copy(otherPool._values);
        }

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
            get => ref _values[_sparse[id]];
        }

        public ComponentsPool(int initialCapacity = 0)
        {
            _sparse = new int[initialCapacity];
            for (int i = 0; i < initialCapacity; i++)
                _sparse[i] = -1;
            _values = new SimpleVector<T>(initialCapacity);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T Add(int id, T value)
        {
            if (id >= _sparse.Length)
            {
                var oldLength = _sparse.Length;
                var newLength = oldLength > 0 ? oldLength * 2 : 2;
                while (id >= newLength)
                    newLength *= 2;
                Array.Resize(ref _sparse, newLength);
                for (int i = oldLength; i < _sparse.Length; i++)
                    _sparse[i] = -1;
            }

#if DEBUG
            if (_sparse[id] > -1)
                throw new EcsException("sparse set already have element at this index");
#endif

            _sparse[id] = _values.Length;
            _values.Add(value);

            return ref _values[_sparse[id]];
        }
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
