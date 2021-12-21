using System.Runtime.CompilerServices;
using EntityType = System.Int32;

namespace ECS
{
    public struct Entity
    {
        public EntityType Val;

        public Entity(EntityType entity) => Val = entity;
    }

    static class EntityExtension
    {
        public const int BitSizeHalved = sizeof(EntityType) * 4;
        public static readonly Entity NullEntity = new Entity((1 << BitSizeHalved) - 1);

#if DEBUG
        static EntityExtension()
        {
            if (NullEntity.GetVersion() > 0)
                throw new EcsException("NullEntity should always have 0 version");
        }
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static EntityType GetId(this in Entity entity)
        {
            var id = entity.Val << BitSizeHalved;
            id >>= BitSizeHalved;
            return id;
        }

        public static void SetId(this ref Entity entity, in EntityType id)
        {
#if DEBUG
            if (id >= NullEntity.Val)
                throw new EcsException("set overflow id");
#endif
            entity.Val = id | entity.GetVersion();
        }

        public static void SetNullId(this ref Entity entity)
        {
            entity.Val = NullEntity.GetId() | entity.GetVersion();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNull(this in Entity entity)
        {
            return entity.GetId() == NullEntity.GetId();
        }

        //TODO: use smaller part for version
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static EntityType GetVersion(this in Entity entity)
        {
            return entity.Val >> BitSizeHalved;
        }

        public static void IncrementVersion(this ref Entity entity)
        {
            EntityType version = entity.GetVersion();
            version++;
            version <<= BitSizeHalved;
            entity.Val = entity.GetId() | version;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ToId(this Entity entity) => entity.GetId();
    }
}
