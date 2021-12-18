
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
            //TODO: implement lazy system registration. (do smth with the fact that entities should be created after registration)
            FilteredSetId = world.RegisterFilter(in Includes, in Excludes);
        }

        //TODO: add check that system is registered before Ticking
        public abstract void Tick(EcsWorld world);
    }
}
