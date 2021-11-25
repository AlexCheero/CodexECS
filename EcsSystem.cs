//TODO: cover with tests
using System;
using System.Collections;

namespace ECS
{
    public abstract class EcsSystem
    {
        //TODO: add check that Comps and Excludes doesn't intersects
        protected Type[] Comps;
        protected Type[] Excludes;
        protected BitArray CompsMask;
        protected BitArray ExcludesMask;
        protected int FilteredSetId;

        public void RegisterInWorld(EcsWorld world)
        {
            FilteredSetId = world.RegisterFilter(ref Comps, ref Excludes, out CompsMask, out ExcludesMask);
        }

        protected abstract void Iterate(EcsWorld world, int id);

        //TODO: add check that system is registered before Ticking
        public virtual void Tick(EcsWorld world)
        {
            var filteredEntities = world.GetFilteredEntitiesById(FilteredSetId);
            foreach (var id in filteredEntities)
                Iterate(world, id);//TODO: maybe should pass entity = world.GetById(id) instead of id
        }
    }
}
