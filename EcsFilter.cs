using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ECS
{
    public struct EcsFilter
    {
        private EcsWorld _world;

        public BitMask Includes;
        public BitMask Excludes;
        //TODO: implement sortable groups
        //TODO: lock filters on iteration
        private HashSet<int> _filteredEntities;
        //TODO: maybe it is better to use simple arrays for delayed ops
        private HashSet<int> _addSet;
        private HashSet<int> _removeSet;

        public delegate void IteartionDelegate(EcsWorld world, int id);
        private int _lockCount;

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
            _world = null;
            Includes = Excludes = default;
            _filteredEntities = _addSet = _removeSet = null;
            _cachedHash = hash;
            _lockCount = 0;
        }

        public EcsFilter(EcsWorld world, in BitMask includes, in BitMask excludes)
        {
            _world = world;
            Includes = default;
            Includes.Copy(includes);
            Excludes = default;
            Excludes.Copy(excludes);
            _filteredEntities = new HashSet<int>(EcsCacheSettings.FilteredEntitiesSize);
            _addSet = new HashSet<int>();
            _removeSet = new HashSet<int>();
            _cachedHash = GetHashFromMasks(Includes, Excludes);
            _lockCount = 0;
        }

        public void Iterate(IteartionDelegate iteartionDelegate)
        {
            _lockCount++;
            foreach (var id in _filteredEntities)
                iteartionDelegate(_world, id);//TODO: maybe should pass entity = _world.GetById(id) instead of id
            _lockCount--;
#if DEBUG
            if (_lockCount < 0)
                throw new EcsException("_lockCount shouldn't be negative");
            else
#endif
            if (_lockCount == 0)
            {
                foreach (var id in _addSet)
                    _filteredEntities.Add(id);
                foreach (var id in _removeSet)
                    _filteredEntities.Remove(id);
            }
        }

        public void Add(int id)
        {
            if (_lockCount > 0)
                _addSet.Add(id);
            else
                _filteredEntities.Add(id);
        }

        public void Remove(int id)
        {
            if (_lockCount > 0)
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
        }
    }
}
