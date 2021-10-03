using System;
using System.Runtime.CompilerServices;

namespace ECS
{
    class SparseSet<T>
    {
        private int[] _sparse;
        private SimpleVector<int> _dense;
        private SimpleVector<T> _values;

        public int Length => _values.Length;
        public int IthEntity(int i) => _dense[i];
        public ref T this[int i] { get { return ref _values[_sparse[i]]; } }

        public SparseSet()
        {
            _sparse = new int[0];
            _dense = new SimpleVector<int>();
            _values = new SimpleVector<T>();
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

            if (_sparse[outerIdx] > -1)
                throw new EcsException("sparse set already have element at this index");
            
            _sparse[outerIdx] = _values.Length;
            _values.Add(value);
            _dense.Add(outerIdx);

            if (_values.Length != _dense.Length)
                throw new EcsException("_values.Length != _dense.Length");
            if (_dense[_sparse[outerIdx]] != outerIdx)
                throw new EcsException("wrong sparse set idices");

            return ref _values[_sparse[outerIdx]];
        }

        //TODO: maybe should shrink set after removing?
        public void Remove(int outerIdx)
        {
            _sparse[outerIdx] = -1;
        }
    }

    //SparseSet implementation without values, for implementing tags
    class LightSparseSet<T>
    {
        private int[] _sparse;
        private SimpleVector<int> _dense;

        public int Length => _dense.Length;
        public int IthEntity(int i) => _dense[i];

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

            if (_sparse[outerIdx] > -1)
                throw new EcsException("sparse set already have element at this index");

            _sparse[outerIdx] = _dense.Length;
            _dense.Add(outerIdx);

            if (_dense[_sparse[outerIdx]] != outerIdx)
                throw new EcsException("wrong sparse set idices");
        }

        //TODO: maybe should shrink set after removing?
        public void Remove(int outerIdx)
        {
            _sparse[outerIdx] = -1;
        }
    }
}
