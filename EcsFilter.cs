using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ECS
{
    public struct EcsFilter
    {
        public BitMask Includes;
        public BitMask Excludes;
        //TODO: implement sortable groups
        private HashSet<int> _filteredEntities;
        private int[] _entitiesArray;//TODO: hack for iteration speedup, rewrite
        //TODO: maybe it is better to use simple arrays for delayed ops
        private HashSet<int> _addSet;
        private HashSet<int> _removeSet;

        public delegate void IteartionDelegate(int[] entities, int count);

        private class BoxedInt { public int Value = 0; }
        private BoxedInt _lockCount;

        private int? _cachedHash;
        public int HashCode
        {
            get
            {
                if (!_cachedHash.HasValue)
                    _cachedHash = GetHashFromMasks(Includes, Excludes);
                return _cachedHash.Value;
            }
        }

        public static int GetHashFromMasks(in BitMask includes, in BitMask excludes)
        {
            int hash = 17;
            var nextSetBit = includes.GetNextSetBit(0);
            while (nextSetBit != -1)
            {
                hash = hash * 23 + nextSetBit.GetHashCode();
                nextSetBit = includes.GetNextSetBit(nextSetBit + 1);
            }

            nextSetBit = excludes.GetNextSetBit(0);
            while (nextSetBit != -1)
            {
                hash = hash * 23 + nextSetBit.GetHashCode();
                nextSetBit = excludes.GetNextSetBit(nextSetBit + 1);
            }

            return hash;
        }

        public EcsFilter(int hash)//dummy ctor
        {
            Includes = Excludes = default;
            _filteredEntities = _addSet = _removeSet = null;
            _cachedHash = hash;
            _lockCount = null;
            _entitiesArray = null;
        }

        public EcsFilter(in BitMask includes, in BitMask excludes)
        {
            Includes = default;
            Includes.Copy(includes);
            Excludes = default;
            Excludes.Copy(excludes);
            _filteredEntities = new HashSet<int>(EcsCacheSettings.FilteredEntitiesSize);
            _addSet = new HashSet<int>();
            _removeSet = new HashSet<int>();
            _cachedHash = GetHashFromMasks(Includes, Excludes);
            _lockCount = new BoxedInt();
            _entitiesArray = new int[EcsCacheSettings.FilteredEntitiesSize];
        }

        public void Iterate(IteartionDelegate iteartionDelegate)
        {
            _lockCount.Value++;
            if (_entitiesArray.Length < _filteredEntities.Count)
            {
                var newCapacity = 2;
                while (newCapacity < _filteredEntities.Count)
                    newCapacity <<= 1;
                Array.Resize(ref _entitiesArray, newCapacity);
            }
            _filteredEntities.CopyTo(_entitiesArray);
            iteartionDelegate(_entitiesArray, _filteredEntities.Count);
            _lockCount.Value--;
#if DEBUG
            if (_lockCount.Value < 0)
                throw new EcsException("_lockCount shouldn't be negative");
            else
#endif
            if (_lockCount.Value == 0)
            {
                foreach (var id in _addSet)
                    _filteredEntities.Add(id);
                foreach (var id in _removeSet)
                    _filteredEntities.Remove(id);
            }
        }

        public void Add(int id)
        {
            if (_lockCount.Value > 0)
                _addSet.Add(id);
            else
                _filteredEntities.Add(id);
        }

        public void Remove(int id)
        {
            if (_lockCount.Value > 0)
                _removeSet.Add(id);
            else
                _filteredEntities.Remove(id);
        }

        public bool Contains(int id)
        {
            return _filteredEntities.Contains(id) && !_removeSet.Contains(id);
        }

        public void Copy(in EcsFilter other)
        {
            Includes.Copy(other.Includes);
            Excludes.Copy(other.Excludes);

            if (_filteredEntities != null)
                _filteredEntities.Clear();
            else
                _filteredEntities = new HashSet<int>(EcsCacheSettings.FilteredEntitiesSize);
            foreach (var entity in other._filteredEntities)
                _filteredEntities.Add(entity);

            _lockCount = other._lockCount;
        }
    }
}
