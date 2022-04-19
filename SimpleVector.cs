﻿using System;
using System.Runtime.CompilerServices;

namespace ECS
{
    //TODO: rename to simple list to match c# style
    class SimpleVector<T>
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

        public SimpleVector(int reserved = 0)
        {
            _elements = new T[reserved];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Copy(in SimpleVector<T> other)
        {
            _end = other._end;
            if (_elements.Length < _end)
                Array.Resize(ref _elements, other._elements.Length);
            Array.Copy(other._elements, _elements, _end);//TODO: try to use Buffer.BlockCopy instead
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Remove(int idx)
        {
            _end--;
            if (idx < _end)
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
                var newLength = _elements.Length > 0 ? _elements.Length * 2 : 2;
                while (_end >= newLength)
                    newLength *= 2;
                Array.Resize(ref _elements, newLength);
            }
            _elements[_end] = element;
            _end++;
        }
    }
}
