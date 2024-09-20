using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using EntityType = System.Int32;//duplicated in EntityExtension

#if DEBUG
using System.Text;
#endif

namespace CodexECS
{
    public class EcsWorld
    {
        private EntityManager _entityManager;
        private ComponentManager _componentManager;
        private ArchetypesManager _archetypes;

        public EcsWorld()
        {
            _entityManager = new EntityManager();
            _componentManager = new ComponentManager();
            _archetypes = new ArchetypesManager();
            _delayedDeleteList = new HashSet<EntityType>();

            _delayedAddComponentBuffer = new();
            _delayedRemoveComponentBuffer = new();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref Entity GetRefById(int id) => ref _entityManager.GetRefById(id);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref Entity GetRefById(Entity other) => ref _entityManager.GetRefById(other.GetId());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsEntityInRange(int id) => _entityManager.IsEntityInRange(id);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Entity GetById(int id) => GetRefById(id);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsDead(int id) => GetRefById(id).GetId() != id;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsNull(int id) => id == EntityExtension.NullEntity.GetId();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(int entity1, int entity2)
        {
            if (entity1 == entity2)
                return GetById(entity1).GetVersion() == GetById(entity2).GetVersion();
            else
                return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsEntityValid(Entity entity)
        {
            if (entity.IsNull())
                return false;
            var id = entity.GetId();
            return !IsDead(id) && entity.GetVersion() == GetById(id).GetVersion();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsIdValid(int id) => id >= 0 && id != EntityExtension.NullEntity.GetId() && !IsDead(id);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public EntityType Create()
        {
            var entity = _entityManager.Create();
            _archetypes.AddToArchetype(entity, new BitMask());
            return entity;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Have<T>(EntityType eid) => _archetypes.Have<T>(eid);

        private Dictionary<EntityType, HashSet<int>> _delayedAddComponentBuffer;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add<T>(EntityType eid, T component = default)
        {
            if (_lockCounter > 0)
            {
                if (!_delayedAddComponentBuffer.ContainsKey(eid))
                    _delayedAddComponentBuffer[eid] = new();
                bool added = _delayedAddComponentBuffer[eid].Add(ComponentMeta<T>.Id);
#if DEBUG
                if (!added)
                    throw new EcsException("component already added");
#endif

            }
            else
            {
#if DEBUG
                if (_delayedAddComponentBuffer.Count > 0)
                    throw new EcsException("_delayedAddComponentBuffer is not empty here");
#endif
                _archetypes.AddComponent<T>(eid);
            }
            AddInternal<T>(eid, component);
        }

        //CODEX_TODO: is it really needed?
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddReference(Type type, int id, object component) => _componentManager.AddReference(type, id, component);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddInternal<T>(EntityType eid, T component = default) => _componentManager.Add<T>(eid, component);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T GetComponent<T>(EntityType eid) => ref _componentManager.GetComponent<T>(eid);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryAdd<T>(EntityType eid)
        {
            if (Have<T>(eid))
                return false;
            Add<T>(eid);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T GetOrAddComponent<T>(EntityType eid)
        {
            if (!Have<T>(eid))
                Add<T>(eid);
            return ref GetComponent<T>(eid);
        }

        private Dictionary<EntityType, HashSet<int>> _delayedRemoveComponentBuffer;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Remove<T>(EntityType eid)
        {
            if (_lockCounter > 0)
            {
                if (!_delayedRemoveComponentBuffer.ContainsKey(eid))
                    _delayedRemoveComponentBuffer[eid] = new();
                bool removed = _delayedRemoveComponentBuffer[eid].Add(ComponentMeta<T>.Id);
#if DEBUG
                if (!removed)
                    throw new EcsException("component already removed");
#endif
            }
            else
            {
#if DEBUG
                if (_delayedRemoveComponentBuffer.Count > 0)
                    throw new EcsException("_delayedRemoveComponentBuffer is not empty here");
#endif
                _archetypes.RemoveComponent<T>(eid);
            }

            _componentManager.Remove<T>(eid);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryRemove<T>(int eid)
        {
            if (!Have<T>(eid))
                return false;
            Remove<T>(eid);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public EcsFilter RegisterFilter(FilterMasks masks)
        {
            _archetypes.RegisterFilter(this, masks, out EcsFilter filter);
            return filter;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public EcsFilter RegisterFilter(in BitMask includes, in BitMask excludes = default)
        {
            return RegisterFilter(new FilterMasks
            {
                Includes = includes,
                Excludes = excludes
            });
        }

        private uint _lockCounter;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Lock() { _lockCounter++; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Unlock()
        {
            _lockCounter--;
#if DEBUG
            if (_lockCounter < 0)
                throw new EcsException("negative lock counter");
#endif
            if (_lockCounter == 0)
            {
                //CODEX_TODO: do the same for adding/deleting components
                foreach (var eid in _delayedDeleteList)
                    Delete_Impl(eid);
                foreach (var (eid, addedSet) in _delayedAddComponentBuffer)
                {
                    foreach (var compId in addedSet)
                        _archetypes.AddComponent(eid, compId);
                    addedSet.Clear();
                }
                foreach (var (eid, removedSet) in _delayedRemoveComponentBuffer)
                {
                    foreach (var compId in removedSet)
                        _archetypes.RemoveComponent(eid, compId);
                    removedSet.Clear();
                }

                _delayedDeleteList.Clear();
                _delayedAddComponentBuffer.Clear();
                _delayedRemoveComponentBuffer.Clear();
            }
        }

        private HashSet<EntityType> _delayedDeleteList;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Delete(EntityType eid)
        {
            if (_lockCounter > 0)
            {
                _delayedDeleteList.Add(eid);
            }
            else
            {
#if DEBUG
                if (_delayedDeleteList.Count > 0)
                    throw new EcsException("_delayedDeleteList is not empty here");
#endif
                Delete_Impl(eid);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Delete_Impl(EntityType eid)
        {
            //CODEX_TODO: check if manual mask iteration is really faster and if so, use it instead
            //ref var mask = ref _archetypes.GetMask(eid);
            //for (int i = mask.GetNextSetBit(0); i >= 0; i = mask.GetNextSetBit(i + 1))
            //    _componentManager.Remove(i, eid);
            foreach (var componentId in _archetypes.GetMask(eid))
                _componentManager.Remove(componentId, eid);

            _archetypes.Delete(eid);
            _entityManager.Delete(eid);
        }

#if DEBUG
        public void GetTypesForId(int id, HashSet<Type> buffer) =>
            _componentManager.GetTypesByMask(_archetypes.GetMask(id), buffer);
        
        private string DebugString(int id, int componentId) =>
            _componentManager.GetPool(componentId).DebugString(id);

        public string DebugEntity(int id)
        {
            if (id < 0)
                return "negative entity";
            var mask = _archetypes.GetMask(id);
            StringBuilder sb = new StringBuilder();
            foreach (var bit in mask)
                sb.Append("\n\t" + DebugString(id, bit));
            return sb.ToString();
        }

        public void DebugAll(StringBuilder sb)
        {
            for (int i = 0; i < _entityManager.EntitiesCount; i++)
            {
                var entity = _entityManager.GetEntity(i);
                if (IsEntityValid(entity))
                {
                    var id = entity.GetId();
                    sb.Append(id + ": " + DebugEntity(id));
                    sb.Append('\n');
                }
            }
        }
#endif
    }
}
