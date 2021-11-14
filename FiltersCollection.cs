﻿//TODO: cover with tests
using System;
using System.Collections.Generic;

namespace ECS
{
    class FiltersCollection
    {
        class FilterEqComparer : IEqualityComparer<EcsFilter>
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

            public bool Equals(EcsFilter x, EcsFilter y)
            {
                bool compsEq = ComponentsEquals(x.Comps, y.Comps);
                bool excludesEq = ComponentsEquals(x.Excludes, y.Excludes);
                return compsEq && excludesEq;
            }

            public int GetHashCode(EcsFilter filter) => filter.HashCode;
        }

        private HashSet<EcsFilter> _collection;
        
        public FiltersCollection()
        {
            _collection = new HashSet<EcsFilter>(new FilterEqComparer());
        }

        public bool GetOrAdd(ref EcsFilter filter)
        {
            var dummy = new EcsFilter(filter.HashCode);
            var addNew = !_collection.TryGetValue(dummy, out dummy);
            if (addNew)
                _collection.Add(filter);
            else
                //TODO: probably should force GC after adding all filters to remove duplicates
                filter = dummy;
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
