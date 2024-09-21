
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace CodexECS.Utility
{
    public class IndexableHashSet<T>
    {
        private Dictionary<T, int> _map;
        private SimpleList<T> _arr;

        public int Count
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _arr.Length;
        }

        public ref T this[int i]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref _arr[i];
        }

        public IndexableHashSet()
        {
            _map = new();
            _arr = new();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(T value) => _map.ContainsKey(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Add(T value)
        {
            if (_map.ContainsKey(value))
                return false;
            _map[value] = _arr.Length;
            _arr.Add(value);
            return true;
            
#if DEBUG
            if (!CheckSynch())
                throw new EcsException("IdexableHashSet desynch.");
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Remove(T value)
        {
            if (!_map.TryGetValue(value, out var index))
                return false;
            
            _arr[index] = _arr[^1];
            _map[_arr[index]] = index;
            
            _map.Remove(value);
            _arr.RemoveAt(_arr.Length - 1);
            
#if DEBUG
            if (!CheckSynch())
                throw new EcsException("IdexableHashSet desynch.");
#endif
            
            return true;
        }
        
#if DEBUG
        private bool CheckSynch()
        {
            if (_arr.Length != _map.Count)
                return false;
            for (int i = 0; i < _arr.Length; i++)
            {
                if (_map[_arr[i]] != i)
                    return false;
            }

            return true;
        }
#endif
    }
}