//TODO: cover with tests
using System;
using System.Collections.Generic;

namespace ECS
{
    class FiltersCollection
    {
        public struct Filter
        {
            public HashSet<Type> Comps;
            public HashSet<Type> Excludes;
            public HashSet<int> FilteredEntities;
        }

        //TODO: make set/get methods and/or iterator
        public SimpleVector<Filter> _filters;

        private bool IsFilteresEqual(Filter filter, IEnumerable<Type> comps, IEnumerable<Type> excludes)
        {
            return filter.Comps.SetEquals(comps) && filter.Excludes.SetEquals(excludes);
        }

        public HashSet<int> Get(IEnumerable<Type> comps, IEnumerable<Type> excludes)
        {
            for (int i = 0; i < _filters.Length; i++)
            {
                var filter = _filters[i];
                if (IsFilteresEqual(filter, comps, excludes))
                    return filter.FilteredEntities;
            }
            return null;
        }

        public bool TryAdd(IEnumerable<Type> comps, IEnumerable<Type> excludes)
        {
            if (Get(comps, excludes) != null)
                return false;

            _filters.Add(
                new Filter
                {
                    Comps = new HashSet<Type>(comps),
                    Excludes = new HashSet<Type>(excludes),
                    FilteredEntities = new HashSet<int>()
                });

            return true;
        }
    }
}
