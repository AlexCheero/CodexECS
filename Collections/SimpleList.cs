using System;
using CodexECS.Utility;
using System.Runtime.CompilerServices;

namespace CodexECS
{
    //CODEX_TODO: rename to simple list to match c# style
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
        public void Clear()
        {
            _end = 0;
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
    }
}