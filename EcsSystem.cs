
namespace ECS
{
    //TODO: add check that Includes and Excludes doesn't intersects
    public abstract class EcsSystem
    {
        protected static int Id<T>() => ComponentMeta<T>.Id;

        public abstract void Tick(EcsWorld world);
    }
}
