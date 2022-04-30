using System.Runtime.CompilerServices;
using EntityType = System.Int32;

namespace ECS
{
    static class EntityExtension
    {
        public const int BitSizeHalved = sizeof(EntityType) * 4;
        public static readonly EntityType NullEntity = (1 << BitSizeHalved) - 1;

#if DEBUG
        static EntityExtension()
        {
            if (NullEntity.GetVersion() > 0)
                throw new EcsException("NullEntity should always have 0 version");
        }
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static EntityType GetId(this in EntityType entity)
        {
            var id = entity << BitSizeHalved;
            id >>= BitSizeHalved;
            return id;
        }

        public static void SetId(this ref EntityType entity, in EntityType id)
        {
#if DEBUG
            if (id >= NullEntity)
                throw new EcsException("set overflow id");
#endif
            entity = id | entity.GetVersion();
        }

        public static void SetNullId(this ref EntityType entity)
        {
            entity = NullEntity.GetId() | entity.GetVersion();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNull(this in EntityType entity)
        {
            return entity.GetId() == NullEntity.GetId();
        }

        //TODO: use smaller part for version
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static EntityType GetVersion(this in EntityType entity)
        {
            return entity >> BitSizeHalved;
        }

        public static void IncrementVersion(this ref EntityType entity)
        {
            EntityType version = entity.GetVersion();
            version++;
            version <<= BitSizeHalved;
            entity = entity.GetId() | version;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ToId(this EntityType entity) => entity.GetId();
    }
}
