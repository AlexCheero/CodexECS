﻿//TODO: cover with tests
using System;
using System.Collections.Generic;

namespace ECS
{
    class FilteresCollection
    {
        struct Filter
        {
            public HashSet<Type> Comps;
            public HashSet<Type> Excludes;
            public SimpleVector<int> FilteredEntities;
        }

        private SimpleVector<Filter> _filteres;

        private bool IsFilteresEqual(Filter filter, IEnumerable<Type> comps, IEnumerable<Type> excludes)
        {
            return filter.Comps.SetEquals(comps) && filter.Excludes.SetEquals(excludes);
        }

        public SimpleVector<int> Get(IEnumerable<Type> comps, IEnumerable<Type> excludes)
        {
            for (int i = 0; i < _filteres.Length; i++)
            {
                var filter = _filteres[i];
                if (IsFilteresEqual(filter, comps, excludes))
                    return filter.FilteredEntities;
            }
            return null;
        }

        public bool TryAdd(IEnumerable<Type> comps, IEnumerable<Type> excludes)
        {
            if (Get(comps, excludes) != null)
                return false;

            _filteres.Add(
                new Filter
                {
                    Comps = new HashSet<Type>(comps),
                    Excludes = new HashSet<Type>(excludes),
                    FilteredEntities = new SimpleVector<int>()
                });

            return true;
        }
    }

    abstract class EcsSystem
    {
        protected static Type GetType<T>() => default(T).GetType();

        protected Type[] _comps;
        protected Type[] _excludes;

        public EcsSystem()
        {
        }

        public void RegisterInWorld(EcsWorld world)
        {
            world.RegisterFilter(_comps, _excludes);
        }

        protected abstract void Iterate(EcsWorld world, int id);

        public virtual void Tick(EcsWorld world)
        {
            var filter = world.GetView(_comps, _excludes);
            for (int i = 0; i < filter.Length; i++)
                Iterate(world, filter[i]);
        }
    }
}