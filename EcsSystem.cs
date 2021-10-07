//TODO: cover with tests
using System;

namespace ECS
{
    abstract class EcsSystem
    {
        protected static Type GetType<T>() => default(T).GetType();

        protected Type[] _comps;
        protected Type[] _excludes;
        protected SimpleVector<int> _filter;

        public EcsSystem()
        {
            _filter = new SimpleVector<int>();
        }

        public void RegisterInWorld(EcsWorld world)
        {
            world.RegisterFilter(_filter);
        }

        protected abstract void Iterate(EcsWorld world, int id);

        public virtual void Tick(EcsWorld world)
        {
            for (int i = 0; i < _filter.Length; i++)
                Iterate(world, _filter[i]);
        }
    }
}
