using System;
using System.Collections.Generic;
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

        //previously Entities was indexable hash set
        //used instead of dictionary
        //key was EntityType, now it is assumed to be outer index
        private readonly SparseSet<int> _entitiesMaping;
        //public SimpleList<EntityType> EntitiesArr;
        public EntityType[] EntitiesArr;
        public int EntitiesArrEnd;

        public Archetype(BitMask mask)
        {
            _mask = mask;
            _entitiesMaping = new (2);
            EntitiesArr = new EntityType[2];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddEntity(EntityType eid)
        {
#if DEBUG && !ECS_PERF_TEST
            if (_entitiesMaping.ContainsIdx(eid))
                throw new EcsException("entity was already in archetype");
#endif
            _entitiesMaping.Add(eid, EntitiesArrEnd);
            //EntitiesArr.Add(eid);
            if (EntitiesArrEnd >= EntitiesArr.Length)
            {
                const int maxResizeDelta = 256;
                Utils.ResizeArray(EntitiesArrEnd, ref EntitiesArr, maxResizeDelta);
            }
            EntitiesArr[EntitiesArrEnd] = eid;
            EntitiesArrEnd++;
            
            OnEntityAdded?.Invoke(eid);

        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveEntity(EntityType eid)
        {
#if DEBUG && !ECS_PERF_TEST
            if (!_entitiesMaping.ContainsIdx(eid))
                throw new EcsException("entity was not in archetype");
#endif
            var index = _entitiesMaping[eid];
            var removeIdx = EntitiesArrEnd - 1;
            var lastEntity = EntitiesArr[removeIdx];
            EntitiesArr[index] = lastEntity;
            if (!_entitiesMaping.ContainsIdx(lastEntity))
                _entitiesMaping.Add(lastEntity, index);
            else
                _entitiesMaping[lastEntity] = index;
            
            _entitiesMaping.RemoveAt(eid);
            // EntitiesArr.RemoveAt(EntitiesArrEnd - 1);
            EntitiesArr[removeIdx] = default;
            EntitiesArrEnd--;
            if (removeIdx < EntitiesArrEnd)
                EntitiesArr[removeIdx] = EntitiesArr[EntitiesArrEnd];
            
            OnEntityRemoved?.Invoke(eid);
        }
    }
}