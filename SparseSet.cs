using System;
using System.Collections;
//TODO: measure if standart containers really affects performanse and git rid of them or use them everywhere
using System.Collections.Generic;
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

        public SparseSet()
        {
            _sparse = new int[0];
            _values = new SimpleVector<T>();
#if DEBUG
            _dense = new SimpleVector<int>();
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
            _sparse[outerIdx] = -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            Array.Fill(_sparse, -1);
        }

        public void Copy(in SparseSet<T> other)
        {
            if (_sparse.Length < other._sparse.Length)
                Array.Resize(ref _sparse, other._sparse.Length);
            else if (_sparse.Length > other._sparse.Length)
                Array.Fill(_sparse, -1, other._sparse.Length, _sparse.Length - other._sparse.Length);
            Array.Copy(other._sparse, _sparse, other._sparse.Length);

            _values.Copy(other._values);

#if DEBUG
            _dense.Copy(other._dense);
#endif
        }
    }

    //TODO: probably should use some kind of bitsets inside of LightSparseSet
    //SparseSet implementation without values, for implementing tags
    class LightSparseSet<T>
    {
        private int[] _sparse;
        private SimpleVector<int> _dense;

        public int Length => _dense.Length;

        public LightSparseSet()
        {
            _sparse = new int[0];
            _dense = new SimpleVector<int>();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(int outerIdx)
        {
            return outerIdx < _sparse.Length && _sparse[outerIdx] > -1;
        }

        public void Add(int outerIdx)
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

            _sparse[outerIdx] = _dense.Length;
            _dense.Add(outerIdx);

#if DEBUG
            if (_dense[_sparse[outerIdx]] != outerIdx)
                throw new EcsException("wrong sparse set idices");
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Remove(int outerIdx)
        {
            _sparse[outerIdx] = -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            Array.Fill(_sparse, -1);
        }

        public void Copy(in LightSparseSet<T> other)
        {
            if (_sparse.Length < other._sparse.Length)
                Array.Resize(ref _sparse, other._sparse.Length);
            else if (_sparse.Length > other._sparse.Length)
                Array.Fill(_sparse, -1, other._sparse.Length, _sparse.Length - other._sparse.Length);
            Array.Copy(other._sparse, _sparse, other._sparse.Length);

            _dense.Copy(other._dense);
        }
    }
}
