namespace CodexECS
{
    //TODO: add check that Includes and Excludes doesn't intersects
    public abstract class EcsSystem
    {
        protected static int Id<T>() => ComponentMeta<T>.Id;

        public virtual bool IsPausable => true;

        public virtual void Init(EcsWorld world) { }
        public abstract void Tick(EcsWorld world);
    }
}
