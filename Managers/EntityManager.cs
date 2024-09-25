using CodexECS.Utility;
using System.Runtime.CompilerServices;
using EntityType = System.Int32;//duplicated in EntityExtension

namespace CodexECS
{
    public class EntityManager
    {
        private SimpleList<Entity> _entities;
        private Entity _recycleListHead = EntityExtension.NullEntity;
        private const int RECYCLE_LIST_HEAD_IDX = -1;
        private int _recycleListEndIdx = RECYCLE_LIST_HEAD_IDX;

        public int EntitiesCount
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _entities.Length;
        }
        
        public ref Entity GetEntity(int entityIdx) => ref _entities[entityIdx];

        public EntityManager()
        {
            _recycleListEndIdx = RECYCLE_LIST_HEAD_IDX;

            _entities = new SimpleList<Entity>();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsEntityInRange(EntityType eid) => eid < _entities.Length;

        //CODEX_TODO: looks like it is redundant
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref Entity GetRefById(EntityType eid)
        {
#if DEBUG && !ECS_PERF_TEST
            if (eid == EntityExtension.NullEntity.GetId())
                throw new EcsException("null entity id");
            if (!IsEntityInRange(eid))
                throw new EcsException("wrong entity id: " + eid);
#endif

            return ref _entities[eid];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsDead(EntityType eid) => GetRefById(eid).GetId() != eid;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNull(EntityType eid) => eid == EntityExtension.NullEntity.GetId();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(EntityType eid1, EntityType eid2) =>
            eid1 == eid2 && GetRefById(eid1).GetVersion() == GetRefById(eid2).GetVersion();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsEntityValid(Entity entity)
        {
            if (entity.IsNull())
                return false;
            var eid = entity.GetId();
            return !IsDead(eid) && entity.GetVersion() == GetRefById(eid).GetVersion();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsIdValid(EntityType eid) => eid >= 0 && eid != EntityExtension.NullEntity.GetId() && !IsDead(eid);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private EntityType GetRecycledId()
        {
#if DEBUG && !ECS_PERF_TEST
            if (_recycleListHead.IsNull())
                throw new EcsException("recycle list head is null");
#endif

            var headId = _recycleListHead.GetId();
            if (headId == _recycleListEndIdx)
            {
                _entities[_recycleListEndIdx].SetId(headId);
                _entities[_recycleListEndIdx].IncrementVersion();

                _recycleListHead = EntityExtension.NullEntity;
                _recycleListEndIdx = -1;
            }
            else
            {
                ref Entity nextToHead = ref GetRefById(headId);
                _recycleListHead.SetId(nextToHead.GetId());
                nextToHead.SetId(headId);
                nextToHead.IncrementVersion();
            }

            return headId;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Delete(EntityType eid)
        {
#if DEBUG && !ECS_PERF_TEST
            if (GetRefById(eid).IsNull())
                throw new EcsException("trying to delete null entity");
            if (IsDead(eid))
                throw new EcsException("trying to delete already dead entity");
            if (!IsEntityInRange(eid))
                throw new EcsException("trying to delete wrong entity");
#endif

            if (_recycleListEndIdx < 0)
                _recycleListHead.SetId(eid);
            else
                _entities[_recycleListEndIdx].SetId(eid);
            ref Entity deletedEntity = ref GetRefById(eid);
            deletedEntity.SetNullId();
            _recycleListEndIdx = eid;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public EntityType Create()
        {
            if (!_recycleListHead.IsNull())
                return GetRecycledId();

            Entity lastEntity = new Entity(_entities.Length);
#if DEBUG && !ECS_PERF_TEST
            if (lastEntity.Val == EntityExtension.NullEntity.Val)
                throw new EcsException("entity limit reached");
            if (_entities.Length < 0)
                throw new EcsException("entities vector length overflow");
            if (lastEntity.GetVersion() > 0)
                throw new EcsException("lastEntity version should always be 0");
#endif

            _entities.Add(lastEntity);
            return _entities.Length - 1;
        }
    }
}