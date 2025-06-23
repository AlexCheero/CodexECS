using System.Runtime.CompilerServices;
using CodexECS.Utility;
using EntityType = System.Int32;//duplicated in EntityExtension

namespace CodexECS
{
    public class Archetype
    {
        public BitMask Mask;
        private readonly SparseSet<int> _entitiesMapping;
        public EntityType[] EntitiesArr;
        public int EntitiesEnd;

        private BitMask _pendingRemove;
        private BitMask _pendingAdd;
        private int _lockCounter;

        public Archetype(BitMask mask)
        {
            Mask = mask;
            _entitiesMapping = new(2);
            EntitiesArr = new EntityType[2];
            _pendingRemove = new();
            _pendingAdd = new();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddEntity(EntityType eid)
        {
            if (_lockCounter > 0)
            {
                if (_pendingRemove.Check(eid))
                    _pendingRemove.Unset(eid);
                else
                    _pendingAdd.Set(eid);
                return;
            }

#if DEBUG && !ECS_PERF_TEST
            if (_entitiesMapping.ContainsIdx(eid))
                throw new EcsException($"entity id {eid} was already in archetype");
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
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Lock() => _lockCounter++;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Unlock()
        {
            _lockCounter--;
#if DEBUG && !ECS_PERF_TEST
            if (_lockCounter < 0)
                throw new EcsException("negative lock counter");
#endif
            if (_lockCounter > 0)
                return;

            foreach (var eid in _pendingRemove)
                RemoveEntity(eid);
            _pendingRemove.Clear();

            foreach (var eid in _pendingAdd)
                AddEntity(eid);
            _pendingAdd.Clear();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveEntity(EntityType eid)
        {
            if (_lockCounter > 0)
            {
                if (_pendingAdd.Check(eid))
                    _pendingAdd.Unset(eid);
                else
                    _pendingRemove.Set(eid);
                return;
            }

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
            EntitiesEnd = lastEntityIdx;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            EntitiesEnd = 0;
            _entitiesMapping.Clear();
        }
        
#if DEBUG
        public override string ToString() => ComponentMapping.DebugByMask(Mask);
#endif
    }
}