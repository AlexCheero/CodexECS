
using System.Collections.Generic;

namespace CodexECS
{
    //TODO: add check that Includes and Excludes doesn't intersects
    public abstract class EcsSystem
    {
        protected static int Id<T>() => ComponentMeta<T>.Id;

        public virtual bool IsPausable => true;

        public virtual void Init(EcsWorld world) { }
        public abstract void Tick(EcsWorld world);


        protected List<int> JustAddedIds;

        public virtual void ReactiveTick(EcsWorld world) { }
        protected void SubscribeOnAddToFilter(EcsWorld world, int filterId)
        {
            JustAddedIds = new();
            world.SubscribeReactiveSystem(filterId,
                (id) => JustAddedIds.Add(id),
                ReactiveTick,
                () => JustAddedIds.Clear());
        }
    }
}
