
namespace ECS
{
    #region Components
    public struct C1 { public int i; }

    public struct C2 { public float f; }

    public struct T1 { }
    public struct T2 { }
    #endregion

    public static class EntityCreator
    {
        public static void CreateEntities<T1>(EcsWorld world, int count)
        {
            for (int i = 0; i < count; i++)
            {
                var entity = world.Create();
                entity.AddComponent<T1>(world);
            }
        }

        public static void CreateEntities<T1, T2>(EcsWorld world, int count)
        {
            for (int i = 0; i < count; i++)
            {
                var entity = world.Create();
                entity.AddComponent<T1>(world);
                entity.AddComponent<T2>(world);
            }
        }

        public static void CreateEntities<T1, T2, T3>(EcsWorld world, int count)
        {
            for (int i = 0; i < count; i++)
            {
                var entity = world.Create();
                entity.AddComponent<T1>(world);
                entity.AddComponent<T2>(world);
                entity.AddComponent<T3>(world);
            }
        }

        public static void CreateEntities<T1, T2, T3, T4>(EcsWorld world, int count)
        {
            for (int i = 0; i < count; i++)
            {
                var entity = world.Create();
                entity.AddComponent<T1>(world);
                entity.AddComponent<T2>(world);
                entity.AddComponent<T3>(world);
                entity.AddComponent<T4>(world);
            }
        }

        public static void CreateEntities<T1, T2, T3, T4, T5>(EcsWorld world, int count)
        {
            for (int i = 0; i < count; i++)
            {
                var entity = world.Create();
                entity.AddComponent<T1>(world);
                entity.AddComponent<T2>(world);
                entity.AddComponent<T3>(world);
                entity.AddComponent<T4>(world);
                entity.AddComponent<T5>(world);
            }
        }

        public static void CreateEntities<T1, T2, T3, T4, T5, T6>(EcsWorld world, int count)
        {
            for (int i = 0; i < count; i++)
            {
                var entity = world.Create();
                entity.AddComponent<T1>(world);
                entity.AddComponent<T2>(world);
                entity.AddComponent<T3>(world);
                entity.AddComponent<T4>(world);
                entity.AddComponent<T5>(world);
                entity.AddComponent<T6>(world);
            }
        }

        public static void CreateEntities<T1, T2, T3, T4, T5, T6, T7>(EcsWorld world, int count)
        {
            for (int i = 0; i < count; i++)
            {
                var entity = world.Create();
                entity.AddComponent<T1>(world);
                entity.AddComponent<T2>(world);
                entity.AddComponent<T3>(world);
                entity.AddComponent<T4>(world);
                entity.AddComponent<T5>(world);
                entity.AddComponent<T6>(world);
                entity.AddComponent<T7>(world);
            }
        }

        public static void CreateEntities<T1, T2, T3, T4, T5, T6, T7, T8>(EcsWorld world, int count)
        {
            for (int i = 0; i < count; i++)
            {
                var entity = world.Create();
                entity.AddComponent<T1>(world);
                entity.AddComponent<T2>(world);
                entity.AddComponent<T3>(world);
                entity.AddComponent<T4>(world);
                entity.AddComponent<T5>(world);
                entity.AddComponent<T6>(world);
                entity.AddComponent<T7>(world);
                entity.AddComponent<T8>(world);
            }
        }

        public static void CreateEntities<T1, T2, T3, T4, T5, T6, T7, T8, T9>(EcsWorld world, int count)
        {
            for (int i = 0; i < count; i++)
            {
                var entity = world.Create();
                entity.AddComponent<T1>(world);
                entity.AddComponent<T2>(world);
                entity.AddComponent<T3>(world);
                entity.AddComponent<T4>(world);
                entity.AddComponent<T5>(world);
                entity.AddComponent<T6>(world);
                entity.AddComponent<T7>(world);
                entity.AddComponent<T8>(world);
                entity.AddComponent<T9>(world);
            }
        }

        public static void CreateEntities<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(EcsWorld world, int count)
        {
            for (int i = 0; i < count; i++)
            {
                var entity = world.Create();
                entity.AddComponent<T1>(world);
                entity.AddComponent<T2>(world);
                entity.AddComponent<T3>(world);
                entity.AddComponent<T4>(world);
                entity.AddComponent<T5>(world);
                entity.AddComponent<T6>(world);
                entity.AddComponent<T7>(world);
                entity.AddComponent<T8>(world);
                entity.AddComponent<T9>(world);
                entity.AddComponent<T10>(world);
            }
        }
    }

    public class System1 : EcsSystem
    {
        public System1()
        {
            Includes.Set(Id<C1>(), Id<C2>());
        }

        protected override void Iteration(EcsWorld world, int id)
        {
            var entity = world.GetById(id);
            entity.GetComponent<C1>(world).i++;
        }
    }

    public class System2 : EcsSystem
    {
        public System2()
        {
            Includes.Set(Id<C1>(), Id<C2>());
            Excludes.Set(Id<T2>());
        }

        protected override void Iteration(EcsWorld world, int id)
        {
            var entity = world.GetById(id);
            entity.GetComponent<C2>(world).f *= 0.9999f;
        }
    }

    public class Startup
    {
        public bool _shouldRun = false;
        public bool _shouldCopy = false;
        public int _copyIterationsNum = 1;

        EcsWorld _world;
        EcsWorld _worldCopy;

        EcsSystem[] _systems;

        private void CreateEntites()
        {
            var e1 = _world.Create();
            e1.AddComponent<C1>(_world);
            var e2 = _world.Create();
            e2.AddComponent<C1>(_world);
            e2.AddComponent<C2>(_world);
            var e3 = _world.Create();
            e3.AddComponent<C1>(_world);
            e3.AddComponent<C2>(_world);
            e3.AddTag<T1>(_world);
            var e4 = _world.Create();
            e4.AddComponent<C1>(_world);
            e4.AddComponent<C2>(_world);
            e4.AddTag<T1>(_world);
            e4.AddTag<T2>(_world);
        }

        public Startup(bool run, bool copy, int iterNum)
        {
            _shouldRun = run;
            _shouldCopy = copy;
            _copyIterationsNum = iterNum;

            _world = new EcsWorld();
            _worldCopy = new EcsWorld();

            _systems = new EcsSystem[] { new System1(), new System2() };

            for (int i = 0; i < _systems.Length; i++)
                _systems[i].RegisterInWorld(_world);

            CreateEntites();
        }

        ulong ctr;
        public void FixedUpdate()
        {
            if (_shouldRun)
            {
                for (int i = 0; i < _systems.Length; i++)
                    _systems[i].Tick(_world);
            }

            if (_shouldCopy)
            {
                //TODO: probably should copy to different varible every iteration
                for (int i = 0; i < _copyIterationsNum; i++)
                    _worldCopy.Copy(_world);
            }

            ctr++;
        }
    }


    //usage example:
    class Program
    {
        static void Main(string[] args)
        {
            var startup = new Startup(true, true, 1);
            startup.FixedUpdate();
        }
    }
}
