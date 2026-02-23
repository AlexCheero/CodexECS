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
        public const int BitSize = sizeof(EntityType) * 8;
        public const int BitSizeIdSize = (int)(BitSize * 3f / 4);
        public const EntityType VersionMask = -1 << BitSizeIdSize;
        public const EntityType IdMask = ~VersionMask;
        public const EntityType NullId = IdMask;
        public const EntityType VersionIncrement = IdMask + 1;

        public static readonly Entity NullEntity = new(NullId);

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
        public static EntityType GetId(this in Entity entity) => entity.Val & IdMask;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetId(this ref Entity entity, in EntityType id)
        {
#if DEBUG && !ECS_PERF_TEST
            if (id >= NullEntity.Val)
                throw new EcsException("set overflow id");
#endif
            entity.Val = id | (entity.Val & VersionMask);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetNullId(this ref Entity entity) => entity.Val = NullId | (entity.Val & VersionMask);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNull(this in Entity entity) => entity.GetId() == NullId;

        //CODEX_TODO: use smaller part for version
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static EntityType GetVersion(this in Entity entity) => entity.Val >> BitSizeIdSize;

        //theoretically could overflow, but there is enough room for version
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void IncrementVersion(this ref Entity entity) => entity.Val += VersionIncrement;
    }
}
