using System;
using CodexECS.Utility;
using System.Runtime.CompilerServices;

namespace CodexECS
{
    public class SparseSet<T>
    {
        private int[] _sparse;
        private SimpleList<T> _values;
        private SimpleList<int> _dense;

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

        public SparseSet(int initialCapacity = 2)
        {
            _sparse = new int[initialCapacity];
            for (int i = 0; i < initialCapacity; i++)
                _sparse[i] = -1;
            _values = new SimpleList<T>(initialCapacity);
            _dense = new SimpleList<int>(initialCapacity);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ContainsIdx(int outerIdx) => outerIdx < _sparse.Length && _sparse[outerIdx] > -1;

        public ref T Add(int outerIdx, T value)
        {
            if (outerIdx >= _sparse.Length)
            {
                var oldLength = _sparse.Length;

                const int maxResizeDelta = 256;
                Utils.ResizeArray(outerIdx, ref _sparse, maxResizeDelta);
                for (int i = oldLength; i < _sparse.Length; i++)
                    _sparse[i] = -1;
            }

#if DEBUG && !ECS_PERF_TEST
            if (_sparse[outerIdx] > -1)
                throw new EcsException("sparse set already have element at this index");
#endif

            _sparse[outerIdx] = _values.Length;
            _values.Add(value);
            _dense.Add(outerIdx);

#if DEBUG && !ECS_PERF_TEST
            if (_values.Length != _dense.Length)
                throw new EcsException("_values.Length != _dense.Length");
            if (_dense[_sparse[outerIdx]] != outerIdx)
                throw new EcsException("wrong sparse set idices");
#endif

            return ref _values[_sparse[outerIdx]];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveAt(int outerIdx)
        {
            var innerIndex = _sparse[outerIdx];
            _sparse[outerIdx] = -1;
            
#if DEBUG && !ECS_PERF_TEST
            if (innerIndex >= _dense.Length)
                throw new EcsException("innerIndex should be smaller than _dense.Length");
#endif
            
            //backswap using _dense
            if (innerIndex < _dense.Length - 1)
                _sparse[_dense[_dense.Length - 1]] = innerIndex;
            _values.SwapRemoveAt(innerIndex);
            _dense.SwapRemoveAt(innerIndex);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            //CODEX_TODO: make proper define
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

#if HEAVY_ECS_DEBUG
        public bool CheckInvariant()
        {
            for (int i = 0; i < _sparse.Length; i++)
            {
                if (_sparse[i] == -1)
                    continue;
                for (int j = 0; j < _sparse.Length; j++)
                {
                    if (i == j)
                        continue;

                    if (_sparse[i] == _sparse[j])
                        return false;
                }
            }

            return true;
        }
#endif
    }
}
