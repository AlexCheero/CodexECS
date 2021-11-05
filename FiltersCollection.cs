//TODO: cover with tests
using System;
using System.Collections.Generic;

namespace ECS
{
    struct Filter
    {
        public HashSet<Type> Comps;
        public HashSet<Type> Excludes;
        public HashSet<int> FilteredEntities;
    }

    class FilterComparer : IEqualityComparer<Filter>
    {
        public bool Equals(Filter x, Filter y)
        {
            return x.Comps.SetEquals(y.Comps) && x.Excludes.SetEquals(y.Excludes);
        }

        public int GetHashCode(Filter filter)
        {
            int hash = 17;
            foreach (var comp in filter.Comps)
                hash = hash * 23 + comp.GetHashCode();
            foreach (var comp in filter.Excludes)
                hash = hash * 23 + comp.GetHashCode();
            return hash;
        }
    }

    class FiltersCollection
    {
        private HashSet<Filter> _collection;

        public FiltersCollection()
        {
            _collection = new HashSet<Filter>(new FilterComparer());
        }
    }
}
