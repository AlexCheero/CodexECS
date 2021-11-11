//TODO: cover with tests
using System;
using System.Collections.Generic;

namespace ECS
{
    struct Filter
    {
        //using sorted set because types in same sets should be in same orders
        //to calculate cache properly
        public SortedSet<Type> Comps;//TODO: probably should make some (or all) fields private
        public SortedSet<Type> Excludes;
        public HashSet<int> FilteredEntities;
        
        private int? _cachedHash;
        public int HashCode
        {
            get
            {
                if (_cachedHash.HasValue)
                    return _cachedHash.Value;

                int hash = 17;
                foreach (var comp in Comps)
                    hash = hash * 23 + comp.GetHashCode();
                foreach (var comp in Excludes)
                    hash = hash * 23 + comp.GetHashCode();
                _cachedHash = hash;
                return hash;
            }
        }

        public Filter(int hash)//dummy ctor
        {
            Comps = null;
            Excludes = null;
            FilteredEntities = null;
            _cachedHash = hash;
        }

        public Filter(IEnumerable<Type> comps, IEnumerable<Type> excludes, HashSet<int> filter)
        {
            //TODO: use only one TypeComparer, don't create it every time it is needed
            Comps = new SortedSet<Type>(comps, new TypeComparer());
            Excludes = new SortedSet<Type>(excludes, new TypeComparer());
            FilteredEntities = filter;
            _cachedHash = null;//TODO: probably should calculate hash immediately
        }
    }

    class FilterEqComparer : IEqualityComparer<Filter>
    {
        public bool Equals(Filter x, Filter y)
        {
            return x.Comps.SetEquals(y.Comps) && x.Excludes.SetEquals(y.Excludes);
        }

        public int GetHashCode(Filter filter) => filter.HashCode;
    }

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

    class FiltersCollection
    {
        private HashSet<Filter> _collection;
        private TypeComparer _typeComparer;

        public FiltersCollection()
        {
            _collection = new HashSet<Filter>(new FilterEqComparer());
            _typeComparer = new TypeComparer();
        }

        //TODO: check excludes for null everywhere
        private int GetHashFromComponents(Type[] comps, Type[] excludes)
        {
            Array.Sort(comps, _typeComparer);
            Array.Sort(excludes, _typeComparer);

            int hash = 17;
            hash = hash * 23 + comps[0].GetHashCode();
            for (int i = 1; i < comps.Length; i++)
            {
                var curr = comps[i];
                var prev = comps[i - 1];
                if (curr == prev)
                    continue;

                hash = hash * 23 + comps[i].GetHashCode();
            }

            hash = hash * 23 + excludes[0].GetHashCode();
            for (int i = 1; i < excludes.Length; i++)
            {
                var curr = excludes[i];
                var prev = excludes[i - 1];
                if (curr == prev)
                    continue;

                hash = hash * 23 + excludes[i].GetHashCode();
            }

            return hash;
        }

        public bool GetOrAdd(Type[] comps, Type[] excludes, ref HashSet<int> filter)
        {
            var hash = GetHashFromComponents(comps, excludes);
            var dummy = new Filter(hash);
            var haveFilter = _collection.TryGetValue(dummy, out dummy);
            if (haveFilter)
                //TODO: probably should force GC after adding all filters to remove duplicates
                filter = dummy.FilteredEntities;
            else
                _collection.Add(new Filter(comps, excludes, filter));
            return haveFilter;
        }
    }
}
