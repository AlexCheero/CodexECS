
namespace CodexECS
{
    //TODO: add check that Includes and Excludes doesn't intersects
    public abstract class EcsSystem
    {
        protected static int Id<T>() => ComponentMeta<T>.Id;

        public abstract bool IsEnabled { get; }

        public virtual void Init(EcsWorld world) { }
        public virtual void Tick(EcsWorld world) { }
    }
}
