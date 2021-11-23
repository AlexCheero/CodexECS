//TODO: cover with tests
using System;

namespace ECS
{
    abstract class EcsSystem
    {
        //TODO: add check that Comps and Excludes doesn't intersects
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
                Iterate(world, id);//TODO: maybe should pass entity = world.GetById(id) instead of id
        }
    }
}
