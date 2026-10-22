#if DEBUG
using System;
#endif
using System.Runtime.CompilerServices;
using EntityType = System.Int32;

namespace CodexECS
{
    // A versioned handle: [version:10][id:22]. Plain ids are slots; id 0 is never used, so default(Entity) is null.
    // The version advances on every delete of the slot and wraps after 1,024 reuses.
    public struct Entity : System.IEquatable<Entity>
    {
        public EntityType Val;

        public Entity(EntityType entity) => Val = entity;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(Entity other) => Val == other.Val;
        public override bool Equals(object obj) => obj is Entity other && Val == other.Val;
        public override int GetHashCode() => Val;
    }

    static class EntityExtension
    {
        public const int BitSize = sizeof(EntityType) * 8;
        public const int BitSizeIdSize = 22;
        public const EntityType VersionMask = -1 << BitSizeIdSize;
        public const EntityType IdMask = ~VersionMask;
        public const EntityType NullId = 0;
        public const EntityType VersionIncrement = IdMask + 1;
        // Versions are stored per slot in 10 bits.
        internal const int VersionLimit = 1 << (BitSize - BitSizeIdSize);

        public static readonly Entity NullEntity = default;

#if DEBUG
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
            if (id == NullId || id > IdMask)
                throw new EcsException("set overflow id");
#endif
            entity.Val = id | (entity.Val & VersionMask);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetNullId(this ref Entity entity) => entity.Val = NullId | (entity.Val & VersionMask);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNull(this in Entity entity) => entity.GetId() == NullId;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static EntityType GetVersion(this in Entity entity) => (EntityType)((uint)entity.Val >> BitSizeIdSize);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void IncrementVersion(this ref Entity entity) => entity.Val += VersionIncrement;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Entity Make(int id, int version) => new Entity((version << BitSizeIdSize) | id);
    }
}
