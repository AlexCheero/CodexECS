using System;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using CodexECS.Utility;
using EntityType = System.Int32;//duplicated in EntityExtension

namespace CodexECS
{
    public class Archetype
    {
        public readonly SimpleList<EcsFilter> RelatedFilters;

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
            RelatedFilters = new();
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

            for (int i = 0; i < RelatedFilters._end; i++)
                RelatedFilters._elements[i].AddEntity(eid);
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

            for (int i = 0; i < RelatedFilters._end; i++)
                RelatedFilters._elements[i].RemoveEntity(eid);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            if (RelatedFilters._end > 0)
            {
                for (int i = 0; i < EntitiesEnd; i++)
                {
                    for (int j = 0; j < RelatedFilters._end; j++)
                        RelatedFilters._elements[j].RemoveEntity(EntitiesArr[i]);
                }
            }

            EntitiesEnd = 0;
            _entitiesMapping.Clear();
        }
        
#if DEBUG
        public override string ToString() => ComponentMapping.DebugByMask(Mask);
#endif
    }
}