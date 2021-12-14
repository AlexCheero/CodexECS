using System.Collections.Generic;

namespace ECS
{
    struct EcsFilter
    {
        public BitMask Includes;
        public BitMask Excludes;
        //TODO: implement sortable groups
        //TODO: lock filters on iteration
        public HashSet<int> FilteredEntities;

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
            FilteredEntities = null;
            _cachedHash = hash;
        }

        public EcsFilter(in BitMask includes, in BitMask excludes, HashSet<int> filter)
        {
            Includes = default;
            Includes.Copy(includes);
            Excludes = default;
            Excludes.Copy(excludes);
            FilteredEntities = filter;
            _cachedHash = GetHashFromMasks(Includes, Excludes);
        }
    }
}
