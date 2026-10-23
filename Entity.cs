#if DEBUG
using System;
#endif
using System.Runtime.CompilerServices;
using EntityType = System.Int32;

namespace CodexECS
{
    // A versioned handle: [version:32][id:32]. Plain ids are slots; id 0 is never used, so default(Entity) is null.
    // The version advances on every delete of the slot: a stale handle matches again only after 2^32 reuses of
    // one slot. Loops carry plain ids and build handles only when asked.
    public struct Entity : System.IEquatable<Entity>
    {
        public long Val;

        public Entity(long entity) => Val = entity;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(Entity other) => Val == other.Val;
        public override bool Equals(object obj) => obj is Entity other && Val == other.Val;
        public override int GetHashCode() => Val.GetHashCode();
    }

    static class EntityExtension
    {
        public const int BitSizeIdSize = 32;
        public const long VersionMask = unchecked((long)0xFFFF_FFFF_0000_0000UL);
        public const long IdMask = 0xFFFF_FFFFL;
        public const EntityType NullId = 0;
        public const long VersionIncrement = 1L << BitSizeIdSize;

        public static readonly Entity NullEntity = default;

#if DEBUG
        public static string ToString(this in Entity entity) => $"Entity({entity.GetId()} v{entity.GetVersion()})";
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static EntityType GetId(this in Entity entity) => (EntityType)(uint)entity.Val;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetId(this ref Entity entity, in EntityType id)
        {
#if DEBUG && !ECS_PERF_TEST
            if (id <= NullId)
                throw new EcsException("set overflow id");
#endif
            entity.Val = (uint)id | (entity.Val & VersionMask);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetNullId(this ref Entity entity) => entity.Val &= VersionMask;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNull(this in Entity entity) => entity.GetId() == NullId;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint GetVersion(this in Entity entity) => (uint)((ulong)entity.Val >> BitSizeIdSize);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void IncrementVersion(this ref Entity entity) => entity.Val = unchecked(entity.Val + VersionIncrement);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Entity Make(int id, uint version) => new Entity((long)(((ulong)version << BitSizeIdSize) | (uint)id));
    }
}
