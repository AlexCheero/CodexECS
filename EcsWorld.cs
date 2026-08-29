using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using EntityType = System.Int32;//duplicated in EntityExtension

#if DEBUG
using CodexECS.Utility;
#endif

namespace CodexECS
{
    public class EcsWorld
    {
        private readonly EntityManager _entityManager;
        private readonly ComponentManager _componentManager;
        //this is the direct reference from component manager to speed thing up a bit
        private IComponentsPool[] _pools;
        private readonly ArchetypesManager _archetypes;
        
        private readonly SparseSet<Action<EcsWorld>> _onAddCallbacks;
        private readonly SparseSet<Action<EcsWorld>> _onRemoveCallbacks;
        private BitMask _dirtyAddMask;
        private BitMask _dirtyRemoveMask;

        private BitMask _addReactGuard;
        private BitMask _removeReactGuard;
        private readonly SimpleList<EntityType> _entitiesMovedToEmpty;
        private readonly SimpleList<EntityType> _removeAllEntities;

        private void SetPools(IComponentsPool[] pools) => _pools = pools;

        public EcsWorld()
        {
            _entityManager = new EntityManager();
            _componentManager = new ComponentManager();
            _pools = _componentManager._pools;
            _componentManager.OnPoolsResized = SetPools;
            _archetypes = new ArchetypesManager();
            _delayedDeleteList = new();
            
            _onAddCallbacks = new();
            _onRemoveCallbacks = new();
            _dirtyAddMask = new();
            _dirtyRemoveMask = new();
            _entitiesMovedToEmpty = new();
            _removeAllEntities = new();

            _componentsSetMatchGraph = new();
            _matchCollectionBuffer = new();
            _dirtyMatchNodes = new();
            _pendingAddEntities = new();
            _pendingRemoveEntities = new();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref Entity GetById(int id) => ref _entityManager.GetById(id);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsDead(int id) => _entityManager.IsDead(id);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsDead(Entity entity) => IsDead(entity.GetId());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsNull(int id) => id == EntityExtension.NullId;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsEntityValid(Entity entity)
        {
            if (entity.IsNull())
                return false;
            var id = entity.GetId();
            return !IsDead(id) && entity.GetVersion() == GetById(id).GetVersion();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsIdValid(int id) => id >= 0 && id != EntityExtension.NullId && !IsDead(id);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public EntityType Create() => CreateWithComponents(default);

        public EntityType CreateWithComponents(in BitMask componentsMask)
        {
            var destinationMask = componentsMask.Duplicate();

#if USE_DEBUG_TRACE_COMPONENT && DEBUG
            destinationMask.Set(ComponentMeta<DebugTraceData>.Id);
#endif

#if DEBUG && !ECS_PERF_TEST
            foreach (var componentId in destinationMask)
            {
                if (!ComponentMapping.HaveId(componentId))
                    throw new EcsException($"component id {componentId} is not registered");
                if (IsReactWrapperType(componentId))
                    throw new EcsException("Cannot create entities with reactive wrapper components");
            }
#endif

            var entity = _entityManager.Create();
            _archetypes.AddToArchetype(entity, destinationMask);

            foreach (var componentId in destinationMask)
                _componentManager.AddDefault(entity, componentId);

            foreach (var componentId in destinationMask)
            {
                var componentType = ComponentMapping.GetTypeForId(componentId);
                ComponentMapping.CallDispatchers[componentType].ReactOnAdd(this, entity, false);
            }

            ReactOnComponentsSetCreated(entity, destinationMask);

            return entity;
        }

#if USE_DEBUG_TRACE_COMPONENT && DEBUG
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SaveTraceData(EntityType eid, Type type, DebugTraceData.EMethodType method, string memberName, string filePath, int lineNumber)
        {
            if (string.IsNullOrEmpty(memberName))
                return;

            ref var component = ref Get<DebugTraceData>(eid);
            var traceData = new DebugTraceData.Data
            {
                memberName = memberName,
                filePath = filePath,
                lineNumber = lineNumber
            };

            switch (method)
            {
                case DebugTraceData.EMethodType.Add:
                    component.added[type] = traceData;
                    break;
                case DebugTraceData.EMethodType.Remove:
                    component.removed[type] = traceData;
                    break;
                default:
                    throw new EcsException($"wrong value of {nameof(DebugTraceData.EMethodType)}");
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsCalledFromWorld(string sourceFilePath)
        {
            var classNameStartIdx = sourceFilePath.Length - ".cs".Length - nameof(EcsWorld).Length;
            if (classNameStartIdx < 0)
                return false;
            for (int i = 0; i < 8; i++)
            {
                if (nameof(EcsWorld)[i] != sourceFilePath[classNameStartIdx + i])
                    return false;
            }

            return true;
        }
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Have<T>(EntityType eid)
        {
#if DEBUG && !ECS_PERF_TEST
            if (_archetypes.Have<T>(eid) != _componentManager.Have<T>(eid))
                throw new EcsException("Components and archetypes desynch");
#endif
            return _archetypes.Have<T>(eid);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Have(in BitMask mask, EntityType eid)
        {
#if DEBUG && !ECS_PERF_TEST
            if (_archetypes.Have(mask, eid) != _componentManager.Have(mask, eid))
                throw new EcsException("Components and archetypes desynch");
#endif
            return _archetypes.Have(mask, eid);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HaveAny(in BitMask mask, EntityType eid)
        {
#if DEBUG && !ECS_PERF_TEST
            //TODO: implement
#endif
            return _archetypes.HaveAny(mask, eid);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Have(int componentId, EntityType eid)
        {
#if DEBUG && !ECS_PERF_TEST
            if (_archetypes.Have(componentId, eid) != _componentManager.Have(componentId, eid))
                throw new EcsException("Components and archetypes desynch");
#endif
            return _archetypes.Have(componentId, eid);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref readonly BitMask GetMask(int eid) => ref _archetypes.GetMask(eid);

        public void SubscribeOnAdd<T>(Action<EcsWorld> callback)
        {
#if DEBUG && !ECS_PERF_TEST
            if (IsReactWrapperType<T>())
                throw new EcsException("Cannot subscribe on reactive wrappers manually");
#endif
            _addReactGuard.Set(ComponentMeta<T>.Id);
            SubscribeOnExistenceChange<AddReact<T>>(_onAddCallbacks, callback);
        }
        
        public void SubscribeOnRemove<T>(Action<EcsWorld> callback)
        {
#if DEBUG && !ECS_PERF_TEST
            if (IsReactWrapperType<T>())
                throw new EcsException("Cannot subscribe on reactive wrappers manually");
#endif
            _removeReactGuard.Set(ComponentMeta<T>.Id);
            SubscribeOnExistenceChange<RemoveReact<T>>(_onRemoveCallbacks, callback);
        }
        
        private void SubscribeOnExistenceChange<T>(SparseSet<Action<EcsWorld>> callbacks, Action<EcsWorld> callback)
        {
#if DEBUG && !ECS_PERF_TEST
            if (!IsReactWrapperType<T>())
                throw new EcsException("Subscription on the direct type instead of reactive wrapper");
#endif
            
            var reactWrapperId = ComponentMeta<T>.Id;
            if (!callbacks.ContainsIdx(reactWrapperId))
                callbacks.Add(reactWrapperId, callback);
            else
                callbacks[reactWrapperId] += callback;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void QueueExistenceReaction(
            SparseSet<BitMask> pendingEntities,
            int reactWrapperId,
            EntityType eid)
        {
            if (!pendingEntities.ContainsIdx(reactWrapperId))
                pendingEntities.Add(reactWrapperId, new BitMask());
            pendingEntities[reactWrapperId].Set(eid);
        }

        private readonly ComponentsSetMatchGraph _componentsSetMatchGraph;
        private readonly List<ComponentsSetMatchGraph.Node> _matchCollectionBuffer;
        private readonly Queue<ComponentsSetMatchGraph.Node> _dirtyMatchNodes;
        private readonly SparseSet<BitMask> _pendingAddEntities;
        private readonly SparseSet<BitMask> _pendingRemoveEntities;
        private BitMask _previousComponentsMaskBuffer;

        public void SubscribeOnComponentsSetMatch(BitMask mask, Action<EcsWorld> callback)
        {
            var requiredMask = mask.Duplicate();
            requiredMask.Unset(ComponentMeta<MatchReact>.Id);
            if (requiredMask.SetBitsCount == 0)
                throw new ArgumentException("components-set match requires at least one non-reactive component", nameof(mask));
            _componentsSetMatchGraph.Subscribe(requiredMask, callback);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T AddMultiple<T>(EntityType eid)
        {
#if DEBUG && !ECS_PERF_TEST
            if (ComponentMeta<T>.IsTag)
                throw new EcsException("Tags are not assumed to be added multiple times, use component with counter instead");
#endif
            return ref AddMultiple(eid, ComponentMeta<T>.GetDefault());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T AddMultiple<T>(EntityType eid, T component)
        {
#if DEBUG && !ECS_PERF_TEST
            if (ComponentMeta<T>.IsTag)
                throw new EcsException("Tags are not assumed to be added multiple times, use component with counter instead");
#endif

            if (!Have<T>(eid))
            {
                Add(eid, component);
                return ref Get<T>(eid);
            }

            SimpleList<T> components;
            if (!HaveMultipleStorage<T>(eid))
            {
                AddInternal<MultipleComponents<T>>(eid, ComponentMeta<MultipleComponents<T>>.GetDefault());
                components = Get<MultipleComponents<T>>(eid).components;
            }
            else
            {
                components = Get<MultipleComponents<T>>(eid).components;
            }
            components.Add(component);
            return ref components[^1];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveMultiple<T>(EntityType eid, int removeAt = 0)
        {
#if DEBUG && !ECS_PERF_TEST
            if (!Have<T>(eid))
                throw new EcsException($"entity has no components of type {typeof(T).Name}");
#endif

            if (!HaveMultipleStorage<T>(eid))
            {
#if DEBUG && !ECS_PERF_TEST
                if (removeAt != 0)
                    throw new EcsException("single component can only be removed at index 0");
#endif
                Remove<T>(eid);
                return;
            }

            var components = Get<MultipleComponents<T>>(eid).components;

#if DEBUG && !ECS_PERF_TEST
            if (removeAt < 0 || removeAt > components.Length)
                throw new EcsException("multiple component index is out of range");
#endif

            if (removeAt == 0)
            {
                ref var firstComponent = ref Get<T>(eid);
                ComponentMeta<T>.Cleanup(ref firstComponent);
                var promotedComponent = components[0];
                components.SwapRemoveAt(0);
                firstComponent = promotedComponent;
            }
            else
            {
                var additionalIndex = removeAt - 1;
                ComponentMeta<T>.Cleanup(ref components[additionalIndex]);
                components.SwapRemoveAt(additionalIndex);
            }

            if (components.Length == 0)
                RemoveInternal<MultipleComponents<T>>(eid);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveAllMultiple<T>(EntityType eid)
        {
            if (HaveMultipleStorage<T>(eid))
                RemoveInternal<MultipleComponents<T>>(eid);
            Remove<T>(eid);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool HaveMultipleStorage<T>(EntityType eid)
        {
            return ComponentMapping.TryGetMultipleStorageId(ComponentMeta<T>.Id, out var storageId) &&
                   Have(storageId, eid);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HaveMultiple<T>(EntityType eid) =>
            Have<T>(eid) && HaveMultipleStorage<T>(eid) &&
            Get<MultipleComponents<T>>(eid).components.Length > 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public MultipleComponentCollection<T> GetMultiple<T>(EntityType eid)
        {
#if DEBUG && !ECS_PERF_TEST
            if (!Have<T>(eid))
                throw new EcsException($"entity has no components of type {typeof(T).Name}");
#endif
            return new MultipleComponentCollection<T>(this, eid);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add<T>(EntityType eid
#if USE_DEBUG_TRACE_COMPONENT && DEBUG
          , [CallerMemberName] string memberName = "",
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0
#endif
            )
        {
            var defaultValue = ComponentMeta<T>.IsTag ? default : _componentManager.GetNextFree<T>();
            Add(eid, defaultValue);

#if USE_DEBUG_TRACE_COMPONENT && DEBUG
            if (!IsCalledFromWorld(filePath))
                SaveTraceData(eid, typeof(T), DebugTraceData.EMethodType.Add, memberName, filePath, lineNumber);
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add<T>(EntityType eid, T component
#if USE_DEBUG_TRACE_COMPONENT && DEBUG
          , [CallerMemberName] string memberName = "",
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0
#endif
            )
        {
#if DEBUG && !ECS_PERF_TEST
            if (IsReactWrapperType<T>())
                throw new EcsException("Cannot add reactive wrappers manually");
#endif
            
            AddInternal(eid, component);
            ReactOnAdd<T>(eid, true);

#if HEAVY_ECS_DEBUG
            if (!ExistenceSynched<T>(eid))
                throw new EcsException("Components and archetypes not synched");
#endif

#if USE_DEBUG_TRACE_COMPONENT && DEBUG
            if (!IsCalledFromWorld(filePath))
                SaveTraceData(eid, typeof(T), DebugTraceData.EMethodType.Add, memberName, filePath, lineNumber);
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddInternal<T>(EntityType eid, T component)
        {
            _archetypes.AddComponent<T>(eid);
            _componentManager.Add(eid, component);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RemoveInternal<T>(EntityType eid)
        {
            _archetypes.RemoveComponent<T>(eid);
            _componentManager.Remove<T>(eid);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void ReactOnAdd<T>(EntityType eid, bool checkComponentsSetMatch)
        {
            var componentId = ComponentMeta<T>.Id;
            if (_addReactGuard.Check(componentId))
            {
                var reactWrapperId = ComponentMeta<AddReact<T>>.Id;
                if (!Have<AddReact<T>>(eid))
                    AddInternal(eid, default(AddReact<T>));
                QueueExistenceReaction(_pendingAddEntities, reactWrapperId, eid);
                _dirtyAddMask.Set(reactWrapperId);
            }

            if (checkComponentsSetMatch)
                ReactOnComponentSetAdded(eid, componentId);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void ReactOnRemove<T>(EntityType eid)
        {
            var componentId = ComponentMeta<T>.Id;
            if (!_removeReactGuard.Check(componentId))
                return;

            var reactWrapperId = ComponentMeta<RemoveReact<T>>.Id;
            var removedComponent = ComponentMeta<T>.IsTag ? default : Get<T>(eid);
            if (Have<RemoveReact<T>>(eid))
            {
                Get<RemoveReact<T>>(eid).removedComponent = removedComponent;
            }
            else
            {
                AddInternal(eid, new RemoveReact<T> { removedComponent = removedComponent });
            }

            QueueExistenceReaction(_pendingRemoveEntities, reactWrapperId, eid);
            _dirtyRemoveMask.Set(reactWrapperId);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureMatchReact(EntityType eid)
        {
            if (!Have<MatchReact>(eid))
                AddInternal(eid, default(MatchReact));
        }

        private void ReactOnComponentsSetCreated(EntityType eid, in BitMask componentsMask)
        {
            _componentsSetMatchGraph.CollectNewMatches(default, componentsMask, _matchCollectionBuffer);
            QueueComponentsSetMatches(eid);
        }

        private void ReactOnComponentSetAdded(EntityType eid, int componentId)
        {
            ref readonly var componentsMask = ref _archetypes.GetMask(eid);
            _previousComponentsMaskBuffer.Copy(componentsMask);
            _previousComponentsMaskBuffer.Unset(componentId);
            _componentsSetMatchGraph.CollectNewMatches(
                _previousComponentsMaskBuffer,
                componentsMask,
                _matchCollectionBuffer);
            QueueComponentsSetMatches(eid);
        }

        private void QueueComponentsSetMatches(EntityType eid)
        {
            for (int i = 0; i < _matchCollectionBuffer.Count; i++)
            {
                var node = _matchCollectionBuffer[i];
                node.PendingEntities.Set(eid);
                if (node.IsQueued)
                    continue;
                node.IsQueued = true;
                _dirtyMatchNodes.Enqueue(node);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CopyComponents(in BitMask mask, EntityType from, EntityType to
#if USE_DEBUG_TRACE_COMPONENT && DEBUG
            , [CallerMemberName] string memberName = "",
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0
#endif
        )
        {
            foreach (var typeId in mask)
            {
                var componentType = ComponentMapping.GetTypeForId(typeId);
#if USE_DEBUG_TRACE_COMPONENT && DEBUG
                if (!IsCalledFromWorld(filePath))
                    SaveTraceData(to, componentType, DebugTraceData.EMethodType.Add, memberName, filePath, lineNumber);
#endif
                ComponentMapping.CallDispatchers[componentType].Copy(this, from, to);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CopyComponent(int typeId, EntityType from, EntityType to
#if USE_DEBUG_TRACE_COMPONENT && DEBUG
            , [CallerMemberName] string memberName = "",
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0
#endif
        )
        {
            var componentType = ComponentMapping.GetTypeForId(typeId);
#if USE_DEBUG_TRACE_COMPONENT && DEBUG
            if (!IsCalledFromWorld(filePath))
                SaveTraceData(to, componentType, DebugTraceData.EMethodType.Add, memberName, filePath, lineNumber);
#endif
            
            ComponentMapping.CallDispatchers[componentType].Copy(this, from, to);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CopyComponent<T>(EntityType from, EntityType to
#if USE_DEBUG_TRACE_COMPONENT && DEBUG
            , [CallerMemberName] string memberName = "",
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0
#endif
        )
        {
#if USE_DEBUG_TRACE_COMPONENT && DEBUG
            if (!IsCalledFromWorld(filePath))
                SaveTraceData(to, typeof(T), DebugTraceData.EMethodType.Add, memberName, filePath, lineNumber);
#endif
            if (!Have<T>(from))
                return;
            if (from == to)
                return;

            if (ComponentMeta<T>.IsTag)
            {
                if (!Have<T>(to))
                    Add<T>(to);
                return;
            }

            ref var source = ref Get<T>(from);
            if (!Have<T>(to))
            {
                var copiedComponent = default(T);
                ComponentMeta<T>.Copy(in source, ref copiedComponent);
                Add(to, copiedComponent);
                return;
            }

            ref var destination = ref Get<T>(to);
            ComponentMeta<T>.Cleanup(ref destination);
            ComponentMeta<T>.Copy(in source, ref destination);
        }

#if DEBUG
        private bool IsReactWrapperType<T>() => IsReactWrapperType(ComponentMeta<T>.Id);

        private bool IsReactWrapperType(int componentId)
        {
            var type = ComponentMapping.GetTypeForId(componentId);
            var gtd = Utils.GetGenericTypeDefinition(type);
            return type == typeof(MatchReact) || gtd == typeof(AddReact<>) || gtd == typeof(RemoveReact<>);
        }
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddMultiple_Dynamic(Type type, int id, object component) =>
            ComponentMapping.CallDispatchers[type].AddMultiple(this, id, component);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add_Dynamic(Type type, int id, object component) =>
            ComponentMapping.CallDispatchers[type].Add(this, id, component);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Set_Dynamic(Type type, int id, object component) =>
            ComponentMapping.CallDispatchers[type].Set(this, id, component);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Replace<T>(EntityType eid, T component)
        {
            ref var current = ref Get<T>(eid);
            ComponentMeta<T>.Cleanup(ref current);
            current = component;
        }

        public ComponentsPool<T> GetComponentsPool<T>() => (ComponentsPool<T>)_componentManager.GetPool(ComponentMeta<T>.Id);
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T Get<T>(EntityType eid)
        {
#if DEBUG && !ECS_PERF_TEST
            if (ComponentMeta<T>.IsTag)
                throw new EcsException("can't get specific component from tags pool");
#endif
            var pool = (ComponentsPool<T>)_pools[ComponentMeta<T>.Id];

            //return ref pool.Get(eid);
            return ref pool.Values[pool.Sparse[eid]];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T GetFirst<T>()
        {
#if DEBUG && !ECS_PERF_TEST
            if (ComponentMeta<T>.IsTag)
                throw new EcsException("can't get specific component from tags pool");
            if (!HasAny<T>())
                throw new EcsException($"no components of type {typeof(T).Name} in the pool");
#endif
            var pool = (ComponentsPool<T>)_pools[ComponentMeta<T>.Id];
            return ref pool.Values[0];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasAny<T>()
        {
            var componentId = ComponentMeta<T>.Id;
            return componentId < _pools.Length && _pools[componentId] != null && _pools[componentId].Length > 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryAdd<T>(EntityType eid
#if USE_DEBUG_TRACE_COMPONENT && DEBUG
          , [CallerMemberName] string memberName = "",
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0
#endif
            )
        {
            if (Have<T>(eid))
                return false;

            Add<T>(eid);

#if USE_DEBUG_TRACE_COMPONENT && DEBUG
            if (!IsCalledFromWorld(filePath))
                SaveTraceData(eid, typeof(T), DebugTraceData.EMethodType.Add, memberName, filePath, lineNumber);
#endif

            return true;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T GetOrAddComponent<T>(EntityType eid
#if USE_DEBUG_TRACE_COMPONENT && DEBUG
          , [CallerMemberName] string memberName = "",
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0
#endif
            )
        {
            if (!Have<T>(eid))
            {
                Add<T>(eid);

#if USE_DEBUG_TRACE_COMPONENT && DEBUG
                if (!IsCalledFromWorld(filePath))
                    SaveTraceData(eid, typeof(T), DebugTraceData.EMethodType.Add, memberName, filePath, lineNumber);
#endif
            }
            return ref Get<T>(eid);
        }

        //CODEX_TODO: possibly if filter is double looped and in outer loop the component is removed, than it won't be there in the inner loop
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Remove<T>(EntityType eid
#if USE_DEBUG_TRACE_COMPONENT && DEBUG
          , [CallerMemberName] string memberName = "",
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0
#endif
            )
        {
#if DEBUG && !ECS_PERF_TEST
            if (IsReactWrapperType<T>())
                throw new EcsException("Cannot remove reactive wrappers manually");
#endif
            if (HaveMultipleStorage<T>(eid))
                throw new EcsException(
                    $"entity has multiple components of type {typeof(T).Name}; use {nameof(RemoveMultiple)} or {nameof(RemoveAllMultiple)} instead");

            ReactOnRemove<T>(eid);

            RemoveInternal<T>(eid);

            if (_archetypes.GetMask(eid).Length == 0)
                Delete(eid);

#if HEAVY_ECS_DEBUG
            if (!ExistenceSynched<T>(eid))
                throw new EcsException("Components and archetypes not synched");
#endif

#if USE_DEBUG_TRACE_COMPONENT && DEBUG
            if (!IsCalledFromWorld(filePath))
                SaveTraceData(eid, typeof(T), DebugTraceData.EMethodType.Remove, memberName, filePath, lineNumber);
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveAll<T>() => RemoveAll(ComponentMeta<T>.Id);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveAll(int componentId)
        {
#if DEBUG && !ECS_PERF_TEST
            if (IsReactWrapperType(componentId))
                throw new EcsException("Cannot remove reactive wrappers manually");
#endif
            
            if (!_componentManager.IsTypeRegistered(componentId))
                return;

            RemoveAllMultipleStorage(componentId);

            if (_removeReactGuard.Check(componentId))
            {
                _archetypes.CollectEntitiesWithComponent(componentId, _removeAllEntities);
                var componentType = ComponentMapping.GetTypeForId(componentId);
                var dispatcher = ComponentMapping.CallDispatchers[componentType];
                for (var i = 0; i < _removeAllEntities.Length; i++)
                    dispatcher.ReactOnRemove(this, _removeAllEntities[i]);
                _removeAllEntities.Clear();
            }
            
            RemoveAllInternal(componentId);
        }

        private void RemoveAllMultipleStorage(int componentId)
        {
            if (ComponentMapping.TryGetMultipleStorageId(componentId, out var storageId) &&
                _componentManager.IsTypeRegistered(storageId))
                RemoveAllInternal(storageId);
        }

        private void RemoveAllInternal(int componentId)
        {
            _entitiesMovedToEmpty.Clear();
            _archetypes.RemoveAll(componentId, _entitiesMovedToEmpty);
            _componentManager.RemoveAll(componentId);
            DeleteEntitiesMovedToEmpty();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DeleteEntitiesMovedToEmpty()
        {
            for (int i = 0; i < _entitiesMovedToEmpty.Length; i++)
                Delete(_entitiesMovedToEmpty[i]);
            _entitiesMovedToEmpty.Clear();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryRemove<T>(int eid
#if USE_DEBUG_TRACE_COMPONENT && DEBUG
          , [CallerMemberName] string memberName = "",
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0
#endif
            )
        {
            if (!Have<T>(eid))
                return false;

            Remove<T>(eid);

#if USE_DEBUG_TRACE_COMPONENT && DEBUG
            if (!IsCalledFromWorld(filePath))
                SaveTraceData(eid, typeof(T), DebugTraceData.EMethodType.Remove, memberName, filePath, lineNumber);
#endif

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
        
        public struct FilterBuilder
        {
            public EcsWorld _world;
            private BitMask _includes;
            private BitMask _excludes;

            public FilterBuilder With<T>()
            {
                _includes.Set(ComponentMeta<T>.Id);
                return this;
            }

            public FilterBuilder Without<T>()
            {
                _excludes.Set(ComponentMeta<T>.Id);
                return this;
            }

            public EcsFilter Build() => _world.RegisterFilter(_includes, _excludes);
        }

        public FilterBuilder Filter() => new() { _world = this };

        private int _lockCounter;
        private bool _isFlushingReactives;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Lock() { _lockCounter++; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Unlock()
        {
            _lockCounter--;
#if DEBUG && !ECS_PERF_TEST
            if (_lockCounter < 0)
                throw new EcsException("negative lock counter");
#endif
            if (_lockCounter != 0)
                return;

            FlushReactives();
        }

        public void FlushReactives()
        {
            if (_lockCounter != 0)
                return;

            foreach (var eid in _delayedDeleteList)
                Delete_Impl(eid);
            _delayedDeleteList.Clear();

            if (_isFlushingReactives)
                return;

            _isFlushingReactives = true;
            try
            {
                while (_dirtyAddMask.Length > 0 ||
                       _dirtyRemoveMask.Length > 0 ||
                       _dirtyMatchNodes.Count > 0)
                {
                    ReactOnAddRemove(
                        ref _dirtyAddMask,
                        _onAddCallbacks,
                        _pendingAddEntities,
                        "add");
                    ReactOnAddRemove(
                        ref _dirtyRemoveMask,
                        _onRemoveCallbacks,
                        _pendingRemoveEntities,
                        "remove");
                    ReactOnComponentsSetMatches();
                }
            }
            finally
            {
                _isFlushingReactives = false;
            }
        }

        private void ReactOnAddRemove(
            ref BitMask dirtyMask,
            SparseSet<Action<EcsWorld>> callbacks,
            SparseSet<BitMask> pendingEntities,
            string reactionName)
        {
            while (dirtyMask.Length > 0)
            {
                var reactWrapperId = dirtyMask.GetNextSetBit(0);
                dirtyMask.Unset(reactWrapperId);

                var callback = callbacks[reactWrapperId];
#if DEBUG && !ECS_PERF_TEST
                if (callback == null)
                    throw new EcsException($"no registered on {reactionName} callback for type " +
                                           ComponentMapping.GetTypeForId(reactWrapperId));
#endif

                var processingEntities = pendingEntities[reactWrapperId].Duplicate();
                pendingEntities[reactWrapperId].Clear();

                Lock();
                try
                {
                    callback(this);
                }
                finally
                {
                    foreach (var eid in processingEntities)
                    {
                        // A same-type event raised by the callback owns the wrapper now and
                        // will be handled by the next queue generation.
                        if (pendingEntities[reactWrapperId].Check(eid) || !IsIdValid(eid) || !Have(reactWrapperId, eid))
                            continue;

                        _archetypes.RemoveComponent(eid, reactWrapperId);
                        _componentManager.Remove(reactWrapperId, eid);
                        if (_archetypes.GetMask(eid).Length == 0)
                            Delete(eid);
                    }

                    Unlock();
                }
            }
        }

        private void ReactOnComponentsSetMatches()
        {
            while (_dirtyMatchNodes.Count > 0)
            {
                var node = _dirtyMatchNodes.Dequeue();
                node.IsQueued = false;

                var processingEntities = node.PendingEntities.Duplicate();
                node.PendingEntities.Clear();
                var markedEntities = new BitMask();

                Lock();
                try
                {
                    foreach (var eid in processingEntities)
                    {
                        if (!IsIdValid(eid) || !_archetypes.GetMask(eid).InclusivePass(node.RequiredMask))
                            continue;
                        EnsureMatchReact(eid);
                        markedEntities.Set(eid);
                    }

                    node.Invoke(this);
                }
                finally
                {
                    foreach (var eid in markedEntities)
                    {
                        if (!IsIdValid(eid) || !Have<MatchReact>(eid))
                            continue;
                        RemoveInternal<MatchReact>(eid);
                        if (_archetypes.GetMask(eid).Length == 0)
                            Delete(eid);
                    }

                    Unlock();
                }
            }
        }

        private BitMask _delayedDeleteList;
        /// <summary>
        /// Destroys an entity as a hard lifecycle operation. This intentionally bypasses
        /// component-remove subscriptions; remove components explicitly first when their
        /// reactive teardown callbacks are required.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Delete(EntityType eid)
        {
            if (_lockCounter > 0)
            {
                _delayedDeleteList.Set(eid);
            }
            else
            {
#if DEBUG && !ECS_PERF_TEST
                if (_delayedDeleteList.Length > 0)
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
        
#region Debug methods
        public void GetTypesForId(int id, HashSet<Type> buffer) =>
            _componentManager.GetTypesByMask(_archetypes.GetMask(id), buffer);
        
        private string DebugString(int id, int componentId, bool printFields) =>
            _componentManager.GetPool(componentId).DebugString(id, printFields);

        private StringBuilder _debugEntityStringBuilder;
        public string DebugEntity(int id, bool printFields = false)
        {
            if (id == EntityExtension.NullId)
                return "null entity";
            if (IsDead(id))
                return "dead entity";
            if (id < 0)
                return "negative entity";
            var mask = _archetypes.GetMask(id);
            _debugEntityStringBuilder ??= new StringBuilder();
            foreach (var bit in mask)
                _debugEntityStringBuilder.Append(DebugString(id, bit, printFields)).Append("\n");
            var result = _debugEntityStringBuilder.ToString();
            _debugEntityStringBuilder.Clear();
            return result;
        }

        public void DebugAll(StringBuilder sb, bool printFields)
        {
            for (int i = 0; i < _entityManager.EntitiesCount; i++)
            {
                var entity = _entityManager.GetEntity(i);
                if (IsEntityValid(entity))
                {
                    var id = entity.GetId();
                    sb.Append(id + ": " + DebugEntity(id, printFields));
                    sb.Append('\n');
                }
            }
        }
#endregion
        
#if HEAVY_ECS_DEBUG
        private bool ExistenceSynched<T>(int eid) => _archetypes.Have<T>(eid) == _componentManager.Have<T>(eid);
        private bool ExistenceSynched(int componentId, int eid) =>
            _archetypes.Have(componentId, eid) == _componentManager.Have(componentId, eid);
#endif
    }
}
