using System;
using CodexECS.Utility;
using System.Runtime.CompilerServices;
using EntityType = System.Int32;//duplicated in EntityExtension

namespace CodexECS
{
    public class Archetype
    {
        public event Action<EntityType> OnEntityAdded;
        public event Action<EntityType> OnEntityRemoved;

        private BitMask _mask;

        public ref BitMask Mask
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref _mask;
        }

        //CODEX_TODO: public field smells
        public IndexableHashSet<EntityType> Entities;

        public Archetype(BitMask mask)
        {
            _mask = mask;
            Entities = new();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddEntity(EntityType eid)
        {
            bool added = Entities.Add(eid);
#if DEBUG
            if (!added)
                throw new EcsException("entity was already in archetype");
#endif
            OnEntityAdded?.Invoke(eid);

        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveEntity(EntityType eid)
        {
            bool removed = Entities.Remove(eid);
#if DEBUG
            if (!removed)
                throw new EcsException("entity was not in archetype");
#endif
            OnEntityRemoved?.Invoke(eid);
        }
    }
}