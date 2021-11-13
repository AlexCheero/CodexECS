//TODO: cover with tests
using System;
using System.Collections.Generic;

namespace ECS
{
    abstract class EcsSystem
    {
        protected static Type GetType<T>() => default(T).GetType();

        protected EcsFilter Filter;

        //TODO: maybe should register itself in ctor?
        public void RegisterInWorld(EcsWorld world)
        {
            world.RegisterFilter(ref Filter);
        }

        protected abstract void Iterate(EcsWorld world, int id);

        public virtual void Tick(EcsWorld world)
        {
            foreach (var id in Filter.FilteredEntities)
                Iterate(world, id);
        }
    }
}
