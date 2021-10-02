
using System;
using System.Collections;
using System.Collections.Generic;

namespace ECS
{
    class EcsVector<T> : IEnumerable<T>
    {
        private T[] _elements;
        private int _end = 0;

        public int Length => _end;

        public ref T this[int i] { get { return ref _elements[i]; } }

        public EcsVector(int reserved = 0)
        {
            _elements = new T[reserved];
        }

        public void Add(T element)
        {
            if (_end >= _elements.Length)
                Array.Resize(ref _elements, _elements.Length > 0 ? _elements.Length * 2 : 2);
            _elements[_end] = element;
            _end++;
        }

        public IEnumerator<T> GetEnumerator()
        {
            for (int i = 0; i < _end; i++)
                yield return _elements[i];
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            yield return GetEnumerator();
        }
    }
}
