using System;
using System.Runtime.CompilerServices;

namespace CodexECS
{
    class SparseSet<T>
    {
        private int[] _sparse;
        private SimpleVector<T> _values;

        //don't chage outside of SparseSet. made public only for manual unrolling
        public SimpleVector<int> _dense;

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
            _sparse = new int[initialCapacity];
            for (int i = 0; i < initialCapacity; i++)
                _sparse[i] = -1;
            _values = new SimpleVector<T>(initialCapacity);
            _dense = new SimpleVector<int>(initialCapacity);
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
                var newLength = oldLength > 0 ? oldLength << 1 : 2;
                while (outerIdx >= newLength)//TODO: probably can get rid of loop here
                    newLength <<= 1;
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
            _dense.Add(outerIdx);

#if DEBUG
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
            _dense.Remove(innerIndex);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            //TODO: make proper define
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP2_0_OR_GREATER || NET5_0_OR_GREATER
            Array.Fill(_sparse, -1);
#else
            for (int i = 0; i < _sparse.Length; i++)
                _sparse[i] = -1;
#endif

            _values.Clear();
            _dense.Clear();
        }

        public void Copy(in SparseSet<T> other)
        {
            if (_sparse.Length < other._sparse.Length)
                Array.Resize(ref _sparse, other._sparse.Length);
            else if (_sparse.Length > other._sparse.Length)
            {
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP2_0_OR_GREATER || NET5_0_OR_GREATER
                Array.Fill(_sparse, -1, other._sparse.Length, _sparse.Length - other._sparse.Length);
#else
                for (int i = other._sparse.Length; i < _sparse.Length; i++)
                    _sparse[i] = -1;
#endif
            }
            Array.Copy(other._sparse, _sparse, other._sparse.Length);

            _values.Copy(other._values);
            _dense.Copy(other._dense);
        }
    }
}
