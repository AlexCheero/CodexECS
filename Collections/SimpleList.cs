using System;
using CodexECS.Utility;
using System.Runtime.CompilerServices;

namespace CodexECS
{
    public class SimpleList<T>
    {
        public T[] _elements;
        public int _end = 0;

        public int Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _end;
        }

        public int Reserved
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _elements.Length;
        }

        public ref T this[int i]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref _elements[i];
        }

        public SimpleList(int reserved = 2)
        {
            _elements = new T[reserved];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Copy(in SimpleList<T> other)
        {
            _end = other._end;
            if (_elements.Length < _end)
                Array.Resize(ref _elements, other._elements.Length);
            Array.Copy(other._elements, _elements, _end);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SwapRemoveAt(int idx)
        {
#if DEBUG && !ECS_PERF_TEST
            if (idx >= _end)
                throw new EcsException("idx should be smaller than _end");
#endif
            //needed to cleanup reference types even if this is the last element in collection
            _elements[idx] = default;
            _end--;
            _elements[idx] = _elements[_end];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear(bool full = false)
        {
            _end = 0;
            if (full)
            {
                for (int i = 0; i < _elements.Length; i++)
                    _elements[i] = default;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(T element)
        {
            if (_end >= _elements.Length)
            {
                const int maxResizeDelta = 256;
                Utils.ResizeArray(_end, ref _elements, maxResizeDelta);
            }
            _elements[_end] = element;
            _end++;
        }

        public struct Enumerator
        {
            private readonly SimpleList<T> _list;
            private int _index;
            public Enumerator(SimpleList<T> list)
            {
                _list = list;
                _index = -1;
            }
            public bool MoveNext()
            {
                _index++;
                return _index < _list._end;
            }
            public void Reset() => _index = -1;
            public ref T Current => ref _list._elements[_index];
        }

        public Enumerator GetEnumerator() => new Enumerator(this);
    }
}