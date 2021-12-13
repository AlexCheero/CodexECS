using System;
using System.Runtime.CompilerServices;

namespace ECS
{
    class SparseSet<T>
    {
        private int[] _sparse;
        private SimpleVector<T> _values;

#if DEBUG
        private SimpleVector<int> _dense;
#endif

        public int Length => _values.Length;
        public ref T this[int i] { get => ref _values[_sparse[i]]; }

        public SparseSet(int initialCapacity = 0)
        {
            _sparse = new int[0];
            _values = new SimpleVector<T>(initialCapacity);
#if DEBUG
            _dense = new SimpleVector<int>(initialCapacity);
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(int outerIdx)
        {
            return outerIdx < _sparse.Length && _sparse[outerIdx] > -1;
        }

        public ref T Add(int outerIdx, T value)
        {
            if (outerIdx >= _sparse.Length)
            {
                var oldLength = _sparse.Length;
                var newLength = oldLength > 0 ? oldLength * 2 : 2;
                while (outerIdx >= newLength)
                    newLength *= 2;
                Array.Resize(ref _sparse, newLength);
                for (int i = oldLength; i < _sparse.Length; i++)
                    _sparse[i] = -1;
            }

#if DEBUG
            if (_sparse[outerIdx] > -1)
                throw new EcsException("sparse set already have element at this index");
#endif

            _sparse[outerIdx] = _values.Length;
            _values.Add(value);

#if DEBUG
            _dense.Add(outerIdx);

            if (_values.Length != _dense.Length)
                throw new EcsException("_values.Length != _dense.Length");
            if (_dense[_sparse[outerIdx]] != outerIdx)
                throw new EcsException("wrong sparse set idices");
#endif

            return ref _values[_sparse[outerIdx]];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Remove(int outerIdx)
        {
            var innerIndex = _sparse[outerIdx];
            _sparse[outerIdx] = -1;
            _values.Remove(innerIndex);
#if DEBUG
            _dense.Remove(innerIndex);
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            //TODO: make proper define
#if UNITY
            for (int i = 0; i < _sparse.Length; i++)
                _sparse[i] = -1;
#else
            Array.Fill(_sparse, -1);
#endif

            _values.Clear();
#if DEBUG
            _dense.Clear();
#endif
        }

        public void Copy(in SparseSet<T> other)
        {
            if (_sparse.Length < other._sparse.Length)
                Array.Resize(ref _sparse, other._sparse.Length);
            else if (_sparse.Length > other._sparse.Length)
            {
#if UNITY
                for (int i = other._sparse.Length; i < _sparse.Length; i++)
                    _sparse[i] = -1;
#else
                Array.Fill(_sparse, -1, other._sparse.Length, _sparse.Length - other._sparse.Length);
#endif
            }
            Array.Copy(other._sparse, _sparse, other._sparse.Length);

            _values.Copy(other._values);

#if DEBUG
            _dense.Copy(other._dense);
#endif
        }
    }
}
