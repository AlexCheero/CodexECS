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
            get
            {
                var index = _sparse[i];
#if UNITY_EDITOR || !ECS_OPTIMIZATIONS
                if (index < 0 || index >= _valuesEnd)
                    throw new IndexOutOfRangeException();
#endif
                return ref _values[index];
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T GetNthValue(int n)
        {
#if UNITY_EDITOR || !ECS_OPTIMIZATIONS
            if (n < 0 || n >= _valuesEnd)
                throw new IndexOutOfRangeException();
#endif
            return ref _values[n];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetIdx(int num)
        {
#if UNITY_EDITOR || !ECS_OPTIMIZATIONS
            if (num < 0 || num >= _valuesEnd)
                throw new IndexOutOfRangeException();
#endif
            return _sparse[_dense[num]];
        }

        public SparseSet(int initialCapacity = 2) : this(initialCapacity, initialCapacity)
        { }
        
        public SparseSet(int initialSparseCapacity, int initialDenseCapacity)
        {
            _sparse = new int[initialSparseCapacity];
            for (int i = 0; i < initialSparseCapacity; i++)
                _sparse[i] = -1;
            _values = new T[initialDenseCapacity];
            _dense = new int[initialDenseCapacity];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ContainsIdx(int outerIdx)
        {
            var innerIdx = outerIdx > -1 && outerIdx < _sparse.Length ? _sparse[outerIdx] : -1;
            return innerIdx > -1 && innerIdx < _valuesEnd;
        }

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
            _dense[_valuesEnd] = -1;
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
        
        /// <summary>
        /// if this is the set of reference types, then they will still be in memory
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ClearFast() => _valuesEnd = 0;

        /// <summary>
        /// just "opens" already existing element. use only for preallocated sets!
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T GetOrAddFast(int outerIdx)
        {
            ref var innerIdx= ref _sparse[outerIdx];
            if (innerIdx > -1 && innerIdx < _valuesEnd)
                return ref _values[innerIdx];
            
            innerIdx = _valuesEnd;
            _dense[_valuesEnd] = outerIdx;
            _valuesEnd++;
            return ref _values[innerIdx];
        }
        
        /// <summary>
        /// just "opens" already existing element. use only for preallocated sets!
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T AddFast(int outerIdx)
        {
#if UNITY_EDITOR || !ECS_OPTIMIZATIONS
            if (ContainsIdx(outerIdx))
                throw new Exception("Already contains element at this index");
#endif
            
            ref var innerIdx= ref _sparse[outerIdx];
            innerIdx = _valuesEnd;
            _dense[_valuesEnd] = outerIdx;
            _valuesEnd++;
            return ref _values[innerIdx];
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

        public Enumerator GetEnumerator() => new(this);

        public struct Enumerator
        {
            private readonly SparseSet<T> _set;
            private int _currentIdx;
            public Enumerator(SparseSet<T> set)
            {
                _set = set;
                _currentIdx = 0;
            }

            public readonly (int, T) Current
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => (_set._dense[_currentIdx], _set._values[_currentIdx]);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext()
            {
                _currentIdx++;
                return _currentIdx < _set._valuesEnd;
            }
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
