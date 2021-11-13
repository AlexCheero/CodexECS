//TODO: cover with tests
using System;
using System.Collections.Generic;

namespace ECS
{
    class FiltersCollection
    {
        class FilterEqComparer : IEqualityComparer<Filter>
        {
            //comps in this method should always be sorted
            //and, if equals, their length should alway be equal, because it will throw in Filter's
            //ctor, where the cache is calculated, if array contains duplicated elements
            private bool ComponentsEquals(Type[] compsA, Type[] compsB)
            {
                if (compsA == null)
                    return compsB == null;
                if (compsB == null)
                    return false;
                if (compsA.Length != compsB.Length)
                    return false;

                for (int i = 0; i < compsA.Length; i++)
                {
                    if (compsA[i] != compsB[i])
                        return false;
                }

                return true;
            }

            public bool Equals(Filter x, Filter y)
            {
                bool compsEq = ComponentsEquals(x.Comps, y.Comps);
                bool excludesEq = ComponentsEquals(x.Excludes, y.Excludes);
                return compsEq && excludesEq;
            }

            public int GetHashCode(Filter filter) => filter.HashCode;
        }

        private HashSet<Filter> _collection;
        
        public FiltersCollection()
        {
            _collection = new HashSet<Filter>(new FilterEqComparer());
        }

        public bool GetOrAdd(Type[] comps, Type[] excludes, ref HashSet<int> filter)
        {
            var hash = Filter.GetHashFromComponents(comps, excludes);
            var dummy = new Filter(hash);
            var addNew = !_collection.TryGetValue(dummy, out dummy);
            if (addNew)
                _collection.Add(new Filter(comps, excludes, filter));
            else
                //TODO: probably should force GC after adding all filters to remove duplicates
                filter = dummy.FilteredEntities;
            return addNew;
        }

        public void RemoveId(int id)
        {
            foreach (var filter in _collection)
            {
                if (filter.FilteredEntities.Contains(id))
                    filter.FilteredEntities.Remove(id);
            }
        }
    }
}
