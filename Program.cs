
namespace ECS
{
    #region Components
    public struct C1 { public int i; }
    public struct C2 { public float f; }
    public struct T3 { }
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
            Excludes.Set(Id<T3>());
        }

        public override void Tick(EcsWorld world)
        {
            world.GetFilter(FilteredSetId).Iterate((entities, count) =>
            {
                for (int i = 0; i < count; i++)
                {
                    var entity = world.GetById(entities[i]);

                    ref var c1 = ref entity.GetComponent<C1>(world);
                    c1.i += 3;
                    c1.i -= 3;
                    c1.i /= 3;
                    c1.i *= 3;
                    var rem = c1.i % 5;

                    ref var c2 = ref entity.GetComponent<C2>(world);
                    c2.f += 3;
                    c2.f -= 3;
                    c2.f /= 3;
                    c2.f *= 3;
                    var rem2 = c2.f % 5;

                    entity.AddTag<T3>(world);
                }
            });
        }
    }

    public class System2 : EcsSystem
    {
        public System2()
        {
            Includes.Set(Id<C1>(), Id<C2>(), Id<T3>());
        }

        public override void Tick(EcsWorld world)
        {
            world.GetFilter(FilteredSetId).Iterate((entities, count) =>
            {
                for (int i = 0; i < count; i++)
                {
                    var entity = world.GetById(entities[i]);

                    ref var c1 = ref entity.GetComponent<C1>(world);
                    c1.i += 3;
                    c1.i -= 3;
                    c1.i /= 3;
                    c1.i *= 3;
                    var rem = c1.i % 5;

                    ref var c2 = ref entity.GetComponent<C2>(world);
                    c2.f += 3;
                    c2.f -= 3;
                    c2.f /= 3;
                    c2.f *= 3;
                    var rem2 = c2.f % 5;

                    entity.RemoveComponent<T3>(world);
                }
            });
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
            var e = _world.Create();
            e.AddComponent<C1>(_world);
            e.AddComponent<C2>(_world);
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
            var numIters = 100;
            for (int i = 0; i < numIters; i++)
                startup.FixedUpdate();
        }
    }
}
