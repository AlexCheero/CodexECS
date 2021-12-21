
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
                world.AddComponent<T1>(entity);
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
                    ref var c1 = ref world.GetComponent<C1>(entities[i]);
                    c1.i += 3;
                    c1.i -= 3;
                    c1.i /= 3;
                    c1.i *= 3;
                    var rem = c1.i % 5;

                    ref var c2 = ref world.GetComponent<C2>(entities[i]);
                    c2.f += 3;
                    c2.f -= 3;
                    c2.f /= 3;
                    c2.f *= 3;
                    var rem2 = c2.f % 5;

                    world.AddTag<T3>(entities[i]);
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
                    ref var c1 = ref world.GetComponent<C1>(entities[i]);
                    c1.i += 3;
                    c1.i -= 3;
                    c1.i /= 3;
                    c1.i *= 3;
                    var rem = c1.i % 5;

                    ref var c2 = ref world.GetComponent<C2>(entities[i]);
                    c2.f += 3;
                    c2.f -= 3;
                    c2.f /= 3;
                    c2.f *= 3;
                    var rem2 = c2.f % 5;

                    world.RemoveComponent<T3>(entities[i]);
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
            _world.AddComponent<C1>(e);
            _world.AddComponent<C2>(e);
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
