
namespace CodexECS
{
    //TODO: add check that Includes and Excludes doesn't intersects
    public abstract class EcsSystem
    {
        public const string CreationPredicateName = "CreationPredicate";
        
        protected static int Id<T>() => ComponentMeta<T>.Id;

        public virtual void Init(EcsWorld world) { }
        public virtual void Tick(EcsWorld world) { }
    }
}
