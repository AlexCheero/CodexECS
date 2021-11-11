using System;

namespace ECS
{
    //TODO: rename to simple list to match c# style
    class SimpleVector<T>
    {
        private T[] _elements;
        private int _end = 0;

        public int Length => _end;

        public ref T this[int i] { get { return ref _elements[i]; } }

        public SimpleVector(int reserved = 0)
        {
            _elements = new T[reserved];
        }

        public void Copy(in SimpleVector<T> other)
        {
            _end = other._end;
            if (_elements.Length < _end)
                Array.Resize(ref _elements, other._elements.Length);
            Array.Copy(other._elements, _elements, _end);
        }

        public void Clear()
        {
            _end = 0;
        }

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
