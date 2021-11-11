//TODO: cover with tests
using System;
using System.Collections.Generic;

namespace ECS
{
    abstract class EcsSystem
    {
        protected static Type GetType<T>() => default(T).GetType();

        protected HashSet<Type> _comps;
        protected HashSet<Type> _excludes;
        protected HashSet<int> _filter;

        public void RegisterInWorld(EcsWorld world)
        {
            world.RegisterFilter(_comps, _excludes, _filter);
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
