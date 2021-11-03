using System;

//TODO: cover with tests
namespace ECS
{
    //usage example:
    class Program
    {
        struct Comp1 { public int i; }
        struct Comp2 { public float f; }
        struct Comp3 { public uint ui; }
        struct Tag1 { }
        struct Tag2 { }
        struct Tag3 { }

        static void Main(string[] args)
        {
            var world = new EcsWorld();

            var entity1 = world.Create();
            entity1.AddComponent<Comp1>(world).i = 10;
            entity1.AddComponent<Comp2>(world).f = 0.5f;
            entity1.AddTag<Tag1>(world);
            entity1.AddTag<Tag2>(world);

            var entity2 = world.Create();
            entity2.AddComponent<Comp1>(world).i = 10;
            entity2.AddComponent<Comp2>(world).f = 0.5f;
            entity2.AddTag<Tag2>(world);
            entity2.AddTag<Tag3>(world);

            var entity3 = world.Create();
            entity3.AddComponent<Comp1>(world).i = 10;
            entity3.AddComponent<Comp2>(world).f = 0.5f;
            entity3.AddTag<Tag1>(world);

            var filter = new SimpleVector<int>();

            Type GetType<T>() => default(T).GetType();

            var comps = new Type[] { GetType<Comp1>(), GetType<Comp2>(), GetType<Tag1>(), GetType<Tag2>() };
            var excludes = new Type[] { };
            world.GetView(ref filter, in comps, in excludes);//should be only 0

            comps = new Type[] { GetType<Comp2>() };
            excludes = new Type[] { };
            world.GetView(ref filter, in comps, in excludes);//should be all

            comps = new Type[] { GetType<Comp1>(), GetType<Comp2>(), GetType<Tag2>() };
            excludes = new Type[] { GetType<Tag1>() };
            world.GetView(ref filter, in comps, in excludes);//should be only 1

            for(int i = 0; i < filter.Length; i++)
            {
                var id = filter[i];
                var entity = world.GetById(id);
                var comp1 = entity.GetComponent<Comp1>(world);
                ref var comp2 = ref entity.GetComponent<Comp2>(world);
                if (!entity.Have<Comp3>(world))
                {
                    //do smth
                    int a = 0;
                }
                if (entity.Have<Tag3>(world))
                {
                    //do smth
                    int a = 0;
                }
            }

            var world2 = new EcsWorld();
            world2.Copy(world);
        }
    }
}
