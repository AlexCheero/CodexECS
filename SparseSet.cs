using System;
using System.Runtime.CompilerServices;

namespace ECS
{
    class SparseSet<T>
    {
        private int[] _sparse;
        private SimpleVector<T> _values;
        
        public SimpleVector<int> Dense;

        public int Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _values.Length;
        }

        public ref T this[int i]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref _values[_sparse[i]];
        }

        public SparseSet(int initialCapacity = 0)
        {
            _sparse = new int[0];
            _values = new SimpleVector<T>(initialCapacity);
            Dense = new SimpleVector<int>(initialCapacity);
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
            Dense.Add(outerIdx);

#if DEBUG
            if (_values.Length != Dense.Length)
                throw new EcsException("_values.Length != _dense.Length");
            if (Dense[_sparse[outerIdx]] != outerIdx)
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
            Dense.Remove(innerIndex);
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
            Dense.Clear();
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
            Dense.Copy(other.Dense);
        }
    }
}
