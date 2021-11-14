//TODO: cover with tests
using System;
using System.Collections.Generic;

namespace ECS
{
    struct EcsFilter
    {
        class TypeComparer : IComparer<Type>
        {
            public int Compare(Type x, Type y)
            {
                if (x == null)
                    return -1;
                if (y == null)
                    return 1;
                return x.GetHashCode().CompareTo(y.GetHashCode());
            }
        }

        public Type[] Comps;
        public Type[] Excludes;
        //TODO: implement sortable groups
        public HashSet<int> FilteredEntities;

        private static TypeComparer _typeComparer;

        static EcsFilter()
        {
            _typeComparer = new TypeComparer();
        }

        private int? _cachedHash;
        public int HashCode
        {
            get
            {
                if (!_cachedHash.HasValue)
                    _cachedHash = GetHashFromComponents(Comps, Excludes);
                return _cachedHash.Value;
            }
        }

        public static int GetHashFromComponents(Type[] comps, Type[] excludes)
        {
            Array.Sort(comps, _typeComparer);
            int hash = 17;
            hash = hash * 23 + comps[0].GetHashCode();
            for (int i = 1; i < comps.Length; i++)
            {
#if DEBUG
                var curr = comps[i];
                var prev = comps[i - 1];
                if (curr == prev)
                    throw new EcsException("duplicated type in filter");
#endif

                hash = hash * 23 + comps[i].GetHashCode();
            }

            if (excludes != null)
            {
                Array.Sort(excludes, _typeComparer);
                hash = hash * 23 + excludes[0].GetHashCode();
                for (int i = 1; i < excludes.Length; i++)
                {
#if DEBUG
                    var curr = excludes[i];
                    var prev = excludes[i - 1];
                    if (curr == prev)
                        throw new EcsException("duplicated type in filter");
#endif

                    hash = hash * 23 + excludes[i].GetHashCode();
                }
            }

            return hash;
        }

        public EcsFilter(int hash)//dummy ctor
        {
            Comps = null;
            Excludes = null;
            FilteredEntities = null;
            _cachedHash = hash;
        }

        public EcsFilter(Type[] comps, Type[] excludes, HashSet<int> filter)
        {
            Comps = comps;
            Excludes = excludes;
            FilteredEntities = filter;
            _cachedHash = GetHashFromComponents(Comps, Excludes);
        }
    }
}
