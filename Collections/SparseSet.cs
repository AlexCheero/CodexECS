using System;
using CodexECS.Utility;
using System.Runtime.CompilerServices;

namespace CodexECS
{
    public class SparseSet<T>
    {
        private int[] _sparse;
        private T[] _values;
        private int[] _dense;
        private int _valuesEnd;

        public int Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _valuesEnd;
        }

        public ref T this[int i]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref _values[_sparse[i]];
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T GetNthValue(int n) => ref _values[n];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetIdx(int num) => _sparse[_dense[num]];

        public SparseSet(int initialCapacity = 2)
        {
            _sparse = new int[initialCapacity];
            for (int i = 0; i < initialCapacity; i++)
                _sparse[i] = -1;
            _values = new T[initialCapacity];
            _dense = new int[initialCapacity];
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
            if (_values.Length != _dense.Length)
                throw new EcsException("_values _dense desynch");
#endif

            _sparse[outerIdx] = _valuesEnd;
            // _values.Add(value);
            // _dense.Add(outerIdx);
            if (_valuesEnd >= _values.Length)
            {
                const int maxResizeDelta = 256;
                Utils.ResizeArray(_valuesEnd, ref _values, maxResizeDelta);
                Utils.ResizeArray(_valuesEnd, ref _dense, maxResizeDelta);
            }
            _values[_valuesEnd] = value;
            _dense[_valuesEnd] = outerIdx;
            _valuesEnd++;

#if DEBUG && !ECS_PERF_TEST
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
            if (innerIndex >= _valuesEnd)
                throw new EcsException("innerIndex should be smaller than _valuesEnd");
            if (_values.Length != _dense.Length)
                throw new EcsException("_values _dense desynch");
#endif

            //backswap using _dense
            _valuesEnd--;
            if (innerIndex < _valuesEnd)
            {
                _sparse[_dense[_valuesEnd]] = innerIndex;
                _values[innerIndex] = _values[_valuesEnd];
                _dense[innerIndex] = _dense[_valuesEnd];
            }

            _values[_valuesEnd] = default;
            //_dense[_valuesEnd] = -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            for (int i = 0; i < _valuesEnd; i++)
            {
                ref int outerIdx = ref _dense[i];
                if (outerIdx >= 0 && outerIdx < _sparse.Length)
                    _sparse[outerIdx] = -1;
                _values[i] = default;
                outerIdx = -1;
            }

            _valuesEnd = 0;
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

            // _values.Copy(other._values);
            // _dense.Copy(other._dense);
#if DEBUG && !ECS_PERF_TEST
            if (_values.Length != _dense.Length)
                throw new EcsException("_values _dense desynch");
#endif
            _valuesEnd = other._valuesEnd;
            if (_values.Length < _valuesEnd)
            {
                Array.Resize(ref _values, other._values.Length);
                Array.Resize(ref _dense, other._dense.Length);
            }
            Array.Copy(other._values, _values, _valuesEnd);
            Array.Copy(other._dense, _dense, _valuesEnd);
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
