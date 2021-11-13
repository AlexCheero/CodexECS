//TODO: cover with tests
using System;
using System.Collections.Generic;

namespace ECS
{
    abstract class EcsSystem
    {
        protected static Type GetType<T>() => default(T).GetType();

        //TODO: duplicated from Filter. maybe its better to hold just filter instance
        //      and update sets in EcsWorld almost duplicate the type of filters collection
        protected Type[] _comps;
        protected Type[] _excludes;
        protected HashSet<int> _filter;

        //TODO: maybe should register itself in ctor?
        public void RegisterInWorld(EcsWorld world)
        {
            world.RegisterFilter(_comps, _excludes, ref _filter);
        }

        protected abstract void Iterate(EcsWorld world, int id);

        public virtual void Tick(EcsWorld world)
        {
            foreach (var id in _filter)
                Iterate(world, id);
        }
    }
}
