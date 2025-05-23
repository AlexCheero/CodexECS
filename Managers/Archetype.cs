﻿using System;
using System.Runtime.CompilerServices;
using CodexECS.Utility;
using EntityType = System.Int32;//duplicated in EntityExtension

namespace CodexECS
{
    public class Archetype
    {
        public event Action<EntityType> OnEntityAdded;
        public event Action<EntityType> OnEntityRemoved;

        public BitMask Mask;

        //public IndexableHashSet<EntityType> Entities;
        private readonly SparseSet<int> _entitiesMapping;
        // public readonly SimpleList<EntityType> EntitiesArr;
        public EntityType[] EntitiesArr;
        public int EntitiesEnd;

        public Archetype(BitMask mask)
        {
            Mask = mask;
            _entitiesMapping = new(2);
            EntitiesArr = new EntityType[2];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddEntity(EntityType eid)
        {
#if DEBUG && !ECS_PERF_TEST
            if (_entitiesMapping.ContainsIdx(eid))
                throw new EcsException("entity was already in archetype");
#endif
            _entitiesMapping.Add(eid, EntitiesEnd);

            // EntitiesArr.Add(eid);
            if (EntitiesEnd >= EntitiesArr.Length)
            {
                const int maxResizeDelta = 256;
                Utils.ResizeArray(EntitiesEnd, ref EntitiesArr, maxResizeDelta);
            }
            EntitiesArr[EntitiesEnd] = eid;
            EntitiesEnd++;

            if (OnEntityAdded != null)
                OnEntityAdded(eid);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveEntity(EntityType eid)
        {            
#if DEBUG && !ECS_PERF_TEST
            if (!_entitiesMapping.ContainsIdx(eid))
                throw new EcsException("entity was not in archetype");
#endif
            var lastEntityIdx = EntitiesEnd - 1;
            var index = _entitiesMapping[eid];
            var lastEntity = EntitiesArr[lastEntityIdx];
            EntitiesArr[index] = lastEntity;
            
            _entitiesMapping[lastEntity] = index;
            _entitiesMapping.RemoveAt(eid);
            
            // EntitiesArr.SwapRemoveAt(lastEntityIdx);
#if DEBUG && !ECS_PERF_TEST
            if (lastEntityIdx >= EntitiesEnd)
                throw new EcsException("lastEntityIdx should be smaller than EntitiesEnd");
#endif
            EntitiesEnd--;
            EntitiesArr[lastEntityIdx] = EntitiesArr[EntitiesEnd];

            if (OnEntityRemoved != null)
                OnEntityRemoved(eid);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            if (OnEntityRemoved != null)
            {
                for (int i = 0; i < EntitiesEnd; i++)
                    OnEntityRemoved(EntitiesArr[i]);
            }

            EntitiesEnd = 0;
            _entitiesMapping.Clear();
        }
        
#if DEBUG
        public override string ToString() => ComponentMapping.DebugByMask(Mask);
#endif
    }
}