using System;
using System.Collections.Generic;

namespace ECS
{
    public struct EcsFilter
    {
        public BitMask Includes;
        public BitMask Excludes;
        //TODO: implement sortable groups
        private Dictionary<int, int> _filteredEntities;
        private SimpleVector<int> _entitiesVector;
        private HashSet<int> _addSet;
        private HashSet<int> _removeSet;

        public delegate void IteartionDelegate(int[] entities, int count);

        public int Length => _entitiesVector.Length;
        public int this[int i] => _entitiesVector[i];

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

        //init method for blank filter for serialization
        public void Init()
        {
            _filteredEntities = new Dictionary<int, int>();
            _entitiesVector = new SimpleVector<int>();
            _addSet = new HashSet<int>();
            _removeSet = new HashSet<int>();
        }

        public EcsFilter(in BitMask includes, in BitMask excludes, bool dummy = false)
        {
            Includes = default;
            Includes.Copy(includes);
            Excludes = default;
            Excludes.Copy(excludes);
            _filteredEntities = dummy ? null : new Dictionary<int, int>(EcsCacheSettings.FilteredEntitiesSize);
            _addSet = dummy ? null : new HashSet<int>();
            _removeSet = dummy ? null : new HashSet<int>();
            _cachedHash = GetHashFromMasks(Includes, Excludes);
            _lockCount = 0;
            _entitiesVector = dummy ? null : new SimpleVector<int>(EcsCacheSettings.FilteredEntitiesSize);
        }

        public void Iterate(IteartionDelegate iteartionDelegate)
        {
            _lockCount++;
            iteartionDelegate(_entitiesVector._elements, _entitiesVector._end);
            _lockCount--;
#if DEBUG
            if (_lockCount < 0)
                throw new EcsException("_lockCount shouldn't be negative");
            else
#endif
            if (_lockCount == 0)
            {
                foreach (var id in _addSet)
                {
                    if (!_filteredEntities.ContainsKey(id))
                    {
                        _entitiesVector.Add(id);
                        _filteredEntities.Add(id, _entitiesVector._end - 1);
                    }
                }
                _addSet.Clear();
                foreach (var id in _removeSet)
                {
                    if (_filteredEntities.TryGetValue(id, out int idx))
                    {
                        var lastEntity = _entitiesVector._elements[_entitiesVector._end - 1];
                        _entitiesVector.Remove(idx);
                        if (idx < _entitiesVector._end)
                        {
                            _filteredEntities[lastEntity] = _filteredEntities[id];
                            _filteredEntities.Remove(id);
                        }
                        else
                            _filteredEntities.Remove(id);
                    }
                }
                _removeSet.Clear();
            }
        }

        public void Add(int id)
        {
            if (_lockCount > 0)
                _addSet.Add(id);
            else if (!_filteredEntities.ContainsKey(id))
            {
                _entitiesVector.Add(id);
                _filteredEntities.Add(id, _entitiesVector._end - 1);
            }
        }

        public void Remove(int id)
        {
            if (_lockCount > 0)
                _removeSet.Add(id);
            else if (_filteredEntities.TryGetValue(id, out int idx))
            {
                var lastEntity = _entitiesVector._elements[_entitiesVector._end - 1];
                _entitiesVector.Remove(idx);
                if (idx < _entitiesVector._end)
                {
                    _filteredEntities[lastEntity] = _filteredEntities[id];
                    _filteredEntities.Remove(id);
                }
                else
                    _filteredEntities.Remove(id);
            }
        }

        public void Copy(in EcsFilter other)
        {
            //TODO: can copy only once
            Includes.Copy(other.Includes);
            Excludes.Copy(other.Excludes);

            if (_filteredEntities != null)
                _filteredEntities.Clear();
            else
                _filteredEntities = new Dictionary<int, int>(EcsCacheSettings.FilteredEntitiesSize);
            
            if (other._filteredEntities != null)
            {
                foreach (var entity in other._filteredEntities)
                    _filteredEntities.Add(entity.Key, entity.Value);
            }
            
            if (_entitiesVector == null)
                _entitiesVector = new SimpleVector<int>(other._entitiesVector._elements.Length);
            _entitiesVector.Copy(other._entitiesVector);

            _lockCount = other._lockCount;
        }

        public int ByteLength()
        {
            int length = Includes.ByteLength;
            length += Excludes.ByteLength;
            length += sizeof(int); //_filteredEntities.Count
            length += 2 * sizeof(int) * _filteredEntities.Count;
            length += 2 * sizeof(int);//_entitiesVector.Length and _entitiesVector._elements.Length
            length += sizeof(int) * _entitiesVector.Length;
            length += sizeof(int); //_addSet.Count
            length += sizeof(int) * _addSet.Count;
            length += sizeof(int); //_removeSet.Count
            length += sizeof(int) * _removeSet.Count;

            return length;
        }

        public void Serialize(byte[] outBytes, ref int startIndex)
        {
            Includes.Serialize(outBytes, ref startIndex);
            Excludes.Serialize(outBytes, ref startIndex);
            
            BinarySerializer.SerializeInt(_filteredEntities.Count, outBytes, ref startIndex);
            foreach (var pair in _filteredEntities)
            {
                BinarySerializer.SerializeInt(pair.Key, outBytes, ref startIndex);
                BinarySerializer.SerializeInt(pair.Value, outBytes, ref startIndex);
            }
            
            BinarySerializer.SerializeInt(_entitiesVector.Length, outBytes, ref startIndex);
            BinarySerializer.SerializeInt(_entitiesVector._elements.Length, outBytes, ref startIndex);
            for (int i = 0; i < _entitiesVector.Length; i++)
                BinarySerializer.SerializeInt(_entitiesVector[i], outBytes, ref startIndex);

            BinarySerializer.SerializeInt(_addSet.Count, outBytes, ref startIndex);
            foreach (int idx in _addSet)
                BinarySerializer.SerializeInt(idx, outBytes, ref startIndex);
            BinarySerializer.SerializeInt(_removeSet.Count, outBytes, ref startIndex);
            foreach (int idx in _removeSet)
                BinarySerializer.SerializeInt(idx, outBytes, ref startIndex);
        }

        public void Deserialize(byte[] bytes, ref int startIndex)
        {
            Includes.Deserialize(bytes, ref startIndex);
            Excludes.Deserialize(bytes, ref startIndex);

            int filteredEntitiesCount = BinarySerializer.DeserializeInt(bytes, ref startIndex);
            if (filteredEntitiesCount > 0 && _filteredEntities == null)
                _filteredEntities = new Dictionary<int, int>();
            for (int i = 0; i < filteredEntitiesCount; i++)
            {
                int key = BinarySerializer.DeserializeInt(bytes, ref startIndex);
                int value = BinarySerializer.DeserializeInt(bytes, ref startIndex);
                _filteredEntities.Add(key, value);
            }

            if (_entitiesVector == null)
                _entitiesVector = new SimpleVector<int>();
            _entitiesVector._end = BinarySerializer.DeserializeInt(bytes, ref startIndex);
            var entitesElementsLength = BinarySerializer.DeserializeInt(bytes, ref startIndex);
            _entitiesVector._elements = new int[entitesElementsLength];
            for (int i = 0; i < _entitiesVector.Length; i++)
                _entitiesVector[i] = BinarySerializer.DeserializeInt(bytes, ref startIndex);

            if (_addSet == null)
                _addSet = new HashSet<int>();
            int addSetCount = BinarySerializer.DeserializeInt(bytes, ref startIndex);
            for (int i = 0; i < addSetCount; i++)
                _addSet.Add(BinarySerializer.DeserializeInt(bytes, ref startIndex));

            if (_removeSet == null)
                _removeSet = new HashSet<int>();
            int removeSetCount = BinarySerializer.DeserializeInt(bytes, ref startIndex);
            for (int i = 0; i < removeSetCount; i++)
                _removeSet.Add(BinarySerializer.DeserializeInt(bytes, ref startIndex));
        }
    }
}
