#if DEBUG
using System;
#endif
using System.Runtime.CompilerServices;
using EntityType = System.Int32;

namespace CodexECS
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

        public static string ToString(this in Entity entity)
        {
            return Convert.ToString(entity.Val, 2).PadLeft(sizeof(EntityType) * 8, '0');
        }
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static EntityType GetId(this in Entity entity)
        {
            return entity.Val & NullEntity.Val;
        }

        public static void SetId(this ref Entity entity, in EntityType id)
        {
#if DEBUG
            if (id >= NullEntity.Val)
                throw new EcsException("set overflow id");
#endif
            entity.Val = id | (entity.GetVersion() << BitSizeHalved);
        }

        public static void SetNullId(this ref Entity entity)
        {
            entity.Val = NullEntity.GetId() | (entity.GetVersion() << BitSizeHalved);
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
            entity.Val = entity.GetId() | (version << BitSizeHalved);
        }
    }
}
