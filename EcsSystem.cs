
namespace ECS
{
    //TODO: add check that Includes and Excludes doesn't intersects
    public abstract class EcsSystem
    {
        protected int Id<T>() => ComponentMeta<T>.Id;

        //TODO: add check that system is registered before Ticking
        public abstract void Tick(EcsWorld world);
    }
}
