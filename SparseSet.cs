using System;
using System.Runtime.CompilerServices;

namespace ECS
{
    class SparseSet<T>
    {
        private int[] _sparse;
        private T[] _dense;

        public ref T this[int i] { get { return ref _dense[_sparse[i]]; } }

        public SparseSet()
        {
            _sparse = new int[0];
            _dense = new T[0];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(int outerIdx)
        {
            return outerIdx < _sparse.Length && _sparse[outerIdx] > -1;
        }

        public ref T Assign(int outerIdx, T value)
        {
            if (outerIdx >= _sparse.Length)
            {
                var oldLength = _sparse.Length;
                Array.Resize(ref _sparse, oldLength > 0 ? oldLength * 2 : 2);
                for (int i = oldLength; i < _sparse.Length; i++)
                    _sparse[i] = -1;
            }

            var innderIdx = _sparse[outerIdx];
            if (innderIdx >= _dense.Length)
            {
                var oldLength = _dense.Length;
                Array.Resize(ref _dense, oldLength > 0 ? oldLength * 2 : 2);
            }
            _dense[innderIdx] = value;
            return ref _dense[innderIdx];
        }
    }
}
