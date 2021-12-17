﻿
namespace ECS
{
    public abstract class EcsSystem
    {
        //TODO: add check that Includes and Excludes doesn't intersects
        protected BitMask Includes;
        protected BitMask Excludes;

        protected int Id<T>() => ComponentMeta<T>.Id;

        //TODO: implement ability to use several filters in one system
        //      maybe have to implement getting of entities dynamically, without caching
        protected int FilteredSetId;

        public void RegisterInWorld(EcsWorld world)
        {
            FilteredSetId = world.RegisterFilter(in Includes, in Excludes);
        }

        protected abstract void Iteration(EcsWorld world, int id);

        //TODO: add check that system is registered before Ticking
        public virtual void Tick(EcsWorld world) => world.GetFilter(FilteredSetId).Iterate(Iteration);
    }
}
