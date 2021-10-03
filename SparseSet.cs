using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ECS
{
    class SparseSet<T>
    {
        private int[] _sparse;
        private SimpleVector<T> _values;
        //TODO: implement custom hash set, to check existence, but also to have
        //      availability to use indexer
        //      and make it private again
        public HashSet<int> _entitiesSet;

        public ref T this[int i] { get { return ref _values[_sparse[i]]; } }

        public SparseSet()
        {
            _sparse = new int[0];
            _values = new SimpleVector<T>();
            _entitiesSet = new HashSet<int>();
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
            if (_entitiesSet.Contains(outerIdx))
                throw new EcsException("cant be here! _entitiesSet can't contain entity, while _sparse array have -1");

            _entitiesSet.Add(outerIdx);
            _sparse[outerIdx] = _values.Length;
            _values.Add(value);
            return ref _values[_sparse[outerIdx]];
        }

        //TODO: maybe should shrink set after removing?
        public void Remove(int outerIdx)
        {
            _sparse[outerIdx] = -1;
        }
    }
}
