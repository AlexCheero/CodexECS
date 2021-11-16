//TODO: cover with tests
using System;
using System.Collections.Generic;

namespace ECS
{
    abstract class EcsSystem
    {
        protected static Type GetType<T>() => default(T).GetType();

        protected Type[] Comps;
        protected Type[] Excludes;
        protected int FilteredSetId;

        public void RegisterInWorld(EcsWorld world)
        {
            FilteredSetId = world.RegisterFilter(ref Comps, ref Excludes);
        }

        protected abstract void Iterate(EcsWorld world, int id);

        public virtual void Tick(EcsWorld world)
        {
            var filteredEntities = world.GetFilteredEntitiesById(FilteredSetId);
            foreach (var id in filteredEntities)
                Iterate(world, id);
        }
    }
}
