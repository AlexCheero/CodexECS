//TODO: cover with tests
using System;

namespace ECS
{
    abstract class EcsSystem
    {
        protected static Type GetType<T>() => default(T).GetType();

        protected Type[] _comps;
        protected Type[] _excludes;

        public void RegisterInWorld(EcsWorld world)
        {
            world.RegisterFilter(_comps, _excludes);
        }

        protected abstract void Iterate(EcsWorld world, int id);

        public virtual void Tick(EcsWorld world)
        {
            var filter = world.GetView(_comps, _excludes);
            foreach (var id in filter)
                Iterate(world, id);
        }
    }
}
