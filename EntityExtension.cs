using System.Runtime.CompilerServices;
using EntityType = System.UInt32;

//TODO: cover with tests
namespace ECS
{
    static class EntityExtension
    {
        public const int BitSizeHalved = sizeof(EntityType) * 4;
        public const EntityType NullEntity = (1 << BitSizeHalved) - 1;
        
        static EntityExtension()
        {
            if (NullEntity.GetVersion() > 0)
                EcsExceptionThrower.ThrowException("NullEntity should always have 0 version");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static EntityType GetId(this EntityType entity)
        {
            var id = entity << BitSizeHalved;
            id >>= BitSizeHalved;
            return id;
        }

        public static void SetId(this ref EntityType entity, EntityType id)
        {
            if (id.GetId() >= NullEntity)
                EcsExceptionThrower.ThrowException("set overflow id");
            entity = id.GetId() | entity.GetVersion();
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static EntityType GetVersion(this EntityType entity)
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
        public static int ToId(this EntityType entity) => (int)entity.GetId();

        //TODO: check versions of all entities that uses these methods
#region World methods forwarded
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref T AddComponent<T>(this EntityType entity, EcsWorld world, T component = default)
            => ref world.AddComponent<T>(entity);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void AddTag<T>(this EntityType entity, EcsWorld world) => world.AddTag<T>(entity);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsDead(this EntityType entity, EcsWorld world) => world.IsDead(entity);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Have<T>(this EntityType entity, EcsWorld world)
            => world.Have<T>(entity);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref T GetComponent<T>(this EntityType entity, EcsWorld world)
            => ref world.GetComponent<T>(entity);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void RemoveComponent<T>(this EntityType entity, EcsWorld world)
            => world.RemoveComponent<T>(entity);
#endregion
    }
}
