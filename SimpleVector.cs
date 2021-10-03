using System;

namespace ECS
{
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
