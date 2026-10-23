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
    // Semantics kept from the sparse-set CodexECS:
    // - entity ids are ints, 0 is null; Entity adds a version to an id;
    // - Add/Remove take effect immediately, also inside filter loops;
    // - Delete is deferred while the world is locked (every pipeline system tick and every filter loop
    //   lock it) and applied at the outermost Unlock, followed by reactive callbacks;
    // - removing an entity's last component deletes it; Create() makes an empty entity that stays alive.
    // A running filter loop reads the current page from the word captured at its start; later pages are
    // read live, so an entity that gains the filter's components further on is visited and one that loses
    // them is skipped.
    public partial class EcsWorld
    {
        private readonly SparseSet<Action<EcsWorld>> _onAddCallbacks;
        private readonly SparseSet<Action<EcsWorld>> _onRemoveCallbacks;
        internal event Action<EcsFilter, FilterMasks> FilterResolved;
        private BitMask _dirtyAddMask;
        private BitMask _dirtyRemoveMask;

        private BitMask _addReactGuard;
        private BitMask _removeReactGuard;
        private readonly SimpleList<EntityType> _removeAllEntities;
        private readonly SimpleList<EntityType> _emptiedEntities;

        private readonly Dictionary<FilterMasks, EcsFilter> _filters;

        public EcsWorld()
        {
            Alive = new SlotSet(this);
            _delayedDeleteList = new();

            _onAddCallbacks = new();
            _onRemoveCallbacks = new();
            _dirtyAddMask = new();
            _dirtyRemoveMask = new();
            _removeAllEntities = new();
            _emptiedEntities = new();
            _filters = new Dictionary<FilterMasks, EcsFilter>(FilterMasks.MasksComparer);

            _componentsSetMatchGraph = new();
            _matchCollectionBuffer = new();
            _dirtyMatchNodes = new();
            _pendingAddEntities = new();
            _pendingRemoveEntities = new();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Entity GetById(int id) => EntityExtension.Make(id, Versions[id]);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsDead(int id) => !IsAliveId(id);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsDead(Entity entity) => IsDead(entity.GetId());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsNull(int id) => id == EntityExtension.NullId;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsEntityValid(Entity entity)
        {
            var id = entity.GetId();
            return id != EntityExtension.NullId && IsAliveId(id) && Versions[id] == entity.GetVersion();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsIdValid(int id) => id > 0 && IsAliveId(id);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public EntityType Create() => CreateWithComponents(default);

        // Adds every component of the mask with its metadata default and places the entity with the other
        // entities created from the same mask (allocation group by exact mask).
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public EntityType CreateWithComponents(in BitMask componentsMask) =>
            CreateWithComponents(in componentsMask, in componentsMask);

        // Places the entity in the group of an explicit mask, which must be a subset of the components, so
        // entities created with different component sets can share pages. Later Add/Remove never move it.
        public EntityType CreateWithComponents(in BitMask componentsMask, in BitMask groupMask)
        {
#if USE_DEBUG_TRACE_COMPONENT && DEBUG
            var destinationMask = componentsMask.Duplicate();
            destinationMask.Set(ComponentMeta<DebugTraceData>.Id);
#else
            ref readonly var destinationMask = ref componentsMask;
#endif

#if DEBUG && !ECS_PERF_TEST
            foreach (var componentId in destinationMask)
            {
                if (!ComponentMapping.HaveId(componentId))
                    throw new EcsException($"component id {componentId} is not registered");
                if (IsReactWrapperType(componentId))
                    throw new EcsException("Cannot create entities with reactive wrapper components");
            }
            if (!componentsMask.InclusivePass(in groupMask))
            {
                var missing = new System.Text.StringBuilder();
                foreach (var componentId in groupMask)
                {
                    if (!componentsMask.Check(componentId))
                        missing.Append(' ').Append(ComponentMapping.GetTypeForId(componentId).Name);
                }
                throw new EcsException("the group mask must be a subset of the created components; missing:" + missing);
            }
#endif

            var entity = Allocate(GroupFor(in groupMask));
            if (destinationMask.SetBitsCount == 0)
                return entity;

            foreach (var componentId in destinationMask)
            {
                GetStorage(componentId).AddDefault(entity);
                OnComponentAdded(entity, componentId);
            }

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
            var storage = FindStorage(ComponentMeta<T>.Id);
            return storage != null && storage.Has(eid);
        }

        // All components of the mask.
        public bool Have(in BitMask mask, EntityType eid)
        {
            foreach (var componentId in mask)
            {
                var storage = FindStorage(componentId);
                if (storage == null || !storage.Has(eid))
                    return false;
            }
            return true;
        }

        // At least one component of the mask.
        public bool HaveAny(in BitMask mask, EntityType eid)
        {
            foreach (var componentId in mask)
            {
                var storage = FindStorage(componentId);
                if (storage != null && storage.Has(eid))
                    return true;
            }
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Have(int componentId, EntityType eid)
        {
            var storage = FindStorage(componentId);
            return storage != null && storage.Has(eid);
        }

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
        // Every component that appears in a components-set subscription; adds of other components skip the graph.
        private BitMask _matchComponents;

        public void SubscribeOnComponentsSetMatch(BitMask mask, Action<EcsWorld> callback)
        {
            var requiredMask = mask.Duplicate();
            requiredMask.Unset(ComponentMeta<MatchReact>.Id);
            if (requiredMask.SetBitsCount == 0)
                throw new ArgumentException("components-set match requires at least one non-reactive component", nameof(mask));
            _componentsSetMatchGraph.Subscribe(requiredMask, callback);
            _matchComponents.Set(requiredMask);
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
            Add(eid, ComponentMeta<T>.IsTag ? default : ComponentMeta<T>.GetDefault());

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

#if USE_DEBUG_TRACE_COMPONENT && DEBUG
            if (!IsCalledFromWorld(filePath))
                SaveTraceData(eid, typeof(T), DebugTraceData.EMethodType.Add, memberName, filePath, lineNumber);
#endif
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

            if (checkComponentsSetMatch && _matchComponents.Check(componentId))
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
            _componentsSetMatchGraph.CollectNewMatchesAfterAdd(this, eid, componentId, _matchCollectionBuffer);
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
        public void Add_Dynamic(Type type, int id)
        {
            ComponentMapping.EnsureTypeRegistered(type);
            ComponentMapping.CallDispatchers[type].AddDefault(this, id);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddMultiple_Dynamic(Type type, int id, object component) =>
            GetDynamicDispatcher(type).AddMultiple(this, id, component);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add_Dynamic(Type type, int id, object component) =>
            GetDynamicDispatcher(type).Add(this, id, component);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Set_Dynamic(Type type, int id, object component) =>
            GetDynamicDispatcher(type).Set(this, id, component);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Remove_Dynamic(Type type, int id) =>
            GetDynamicDispatcher(type).Remove(this, id);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ComponentMapping.IWorldCallDispatcher GetDynamicDispatcher(Type type)
        {
            ComponentMapping.EnsureTypeRegistered(type);
            return ComponentMapping.CallDispatchers[type];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Replace<T>(EntityType eid, T component)
        {
            ref var current = ref Get<T>(eid);
            ComponentMeta<T>.Cleanup(ref current);
            current = component;
        }

        // Cache it for repeated reads: pool[id] skips the world's type lookup and cast.
        public ComponentsPool<T> GetComponentsPool<T>()
        {
#if DEBUG && !ECS_PERF_TEST
            if (ComponentMeta<T>.IsTag)
                throw new EcsException("can't get components pool for tags");
#endif
            return Pool<T>();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T Get<T>(EntityType eid)
        {
#if DEBUG && !ECS_PERF_TEST
            if (ComponentMeta<T>.IsTag)
                throw new EcsException("can't get specific component from tags pool");
#endif
            var pool = (ComponentsPool<T>)Storages[ComponentMeta<T>.Id];
            return ref pool[eid];
        }

        // The component of the lowest owning id.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T GetFirst<T>()
        {
#if DEBUG && !ECS_PERF_TEST
            if (ComponentMeta<T>.IsTag)
                throw new EcsException("can't get specific component from tags pool");
            if (!HasAny<T>())
                throw new EcsException($"no components of type {typeof(T).Name} in the pool");
#endif
            var pool = (ComponentsPool<T>)Storages[ComponentMeta<T>.Id];
            return ref pool[pool.First()];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasAny<T>()
        {
            var storage = FindStorage(ComponentMeta<T>.Id);
            return storage != null && storage.Length > 0;
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
            if (HaveMultipleStorage<T>(eid))
                throw new EcsException(
                    $"entity has multiple components of type {typeof(T).Name}; use {nameof(RemoveMultiple)} or {nameof(RemoveAllMultiple)} instead");
#endif

            var storage = FindStorage(ComponentMeta<T>.Id);
            if (storage == null || !storage.Has(eid))
            {
#if DEBUG && !ECS_PERF_TEST
                throw new EcsException($"entity {eid} has no {typeof(T).Name}");
#else
                return;
#endif
            }

            ReactOnRemove<T>(eid);

            storage.Remove(eid);
            _componentCounts[eid]--;
            if (HasNoComponents(eid))
                Delete(eid);

#if USE_DEBUG_TRACE_COMPONENT && DEBUG
            if (!IsCalledFromWorld(filePath))
                SaveTraceData(eid, typeof(T), DebugTraceData.EMethodType.Remove, memberName, filePath, lineNumber);
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveAll<T>() => RemoveAll(ComponentMeta<T>.Id);

        public void RemoveAll(int componentId)
        {
#if DEBUG && !ECS_PERF_TEST
            if (IsReactWrapperType(componentId))
                throw new EcsException("Cannot remove reactive wrappers manually");
#endif

            var storage = FindStorage(componentId);
            if (storage == null)
                return;

            if (ComponentMapping.TryGetMultipleStorageId(componentId, out var storageId))
                RemoveAllInternal(storageId);

            if (_removeReactGuard.Check(componentId) && storage.Length > 0)
            {
                CollectOwners(storage, _removeAllEntities);
                var componentType = ComponentMapping.GetTypeForId(componentId);
                var dispatcher = ComponentMapping.CallDispatchers[componentType];
                for (var i = 0; i < _removeAllEntities.Length; i++)
                    dispatcher.ReactOnRemove(this, _removeAllEntities[i]);
                _removeAllEntities.Clear();
            }

            RemoveAllInternal(componentId);
        }

        // Owners in slot order.
        private static void CollectOwners(ComponentStorage storage, SimpleList<EntityType> buffer)
        {
            buffer.Clear();
            var words = storage.Words;
            var nonEmpty = storage.NonEmpty;
            for (int chunk = 0; chunk < nonEmpty.Length; chunk++)
            {
                ulong pages = nonEmpty[chunk];
                while (pages != 0)
                {
                    int page = (chunk << 6) | Bits.TrailingZeros(pages);
                    pages &= pages - 1;
                    ulong word = words[page];
                    while (word != 0)
                    {
                        buffer.Add((page << Layout.PageShift) | Bits.TrailingZeros(word));
                        word &= word - 1;
                    }
                }
            }
        }

        private void RemoveAllInternal(int componentId)
        {
            var storage = FindStorage(componentId);
            if (storage == null || storage.Length == 0)
                return;
            CollectOwners(storage, _emptiedEntities);
            for (int i = 0; i < _emptiedEntities.Length; i++)
            {
                var eid = _emptiedEntities[i];
                storage.Remove(eid);
                _componentCounts[eid]--;
            }
            for (int i = 0; i < _emptiedEntities.Length; i++)
            {
                var eid = _emptiedEntities[i];
                if (HasNoComponents(eid))
                    Delete(eid);
            }
            _emptiedEntities.Clear();
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

        // Filters are shared per pair of masks and never unregistered.
        public EcsFilter RegisterFilter(FilterMasks masks, bool gateSystem = true)
        {
            if (!_filters.TryGetValue(masks, out var filter))
            {
                var key = new FilterMasks
                {
                    Includes = masks.Includes.Duplicate(),
                    Excludes = masks.Excludes.Duplicate(),
                };
                filter = new EcsFilter(this, in key.Includes, in key.Excludes);
                _filters[key] = filter;
            }

            // Gating belongs to this registration, not the shared filter. Opted-in
            // resolutions are captured during system construction even for existing filters.
            if (gateSystem)
                FilterResolved?.Invoke(filter, masks);
            return filter;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public EcsFilter RegisterFilter(in BitMask includes, in BitMask excludes = default,
            bool gateSystem = true)
        {
            return RegisterFilter(new FilterMasks
            {
                Includes = includes,
                Excludes = excludes
            }, gateSystem);
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

            public EcsFilter Build(bool gateSystem = true) =>
                _world.RegisterFilter(_includes, _excludes, gateSystem);
        }

        public FilterBuilder Filter() => new() { _world = this };

        private int _lockCounter;
        private bool _isFlushingReactives;
        internal bool IsLocked => _lockCounter != 0;

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
            if (_lockCounter != 0 || _isFlushingReactives)
                return;

            _isFlushingReactives = true;
            try
            {
                while (_delayedDeleteList.Length > 0 ||
                       _dirtyAddMask.Length > 0 ||
                       _dirtyRemoveMask.Length > 0 ||
                       _dirtyMatchNodes.Count > 0)
                {
                    DeleteDelayedEntities();

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
                // A callback may throw after scheduling a hard delete. Keep the world
                // structurally valid without dispatching any more reactive callbacks.
                if (_lockCounter == 0)
                    DeleteDelayedEntities();
            }
        }

        private void DeleteDelayedEntities()
        {
            if (_delayedDeleteList.Length == 0)
                return;
            foreach (var eid in _delayedDeleteList)
            {
                if (IsIdValid(eid))
                    DestroyNow(eid);
            }
            _delayedDeleteList.Clear();
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

                var haveLiveReaction = false;
                foreach (var eid in processingEntities)
                {
                    if (_delayedDeleteList.Check(eid) || !IsIdValid(eid) || !Have(reactWrapperId, eid))
                        continue;
                    haveLiveReaction = true;
                    break;
                }
                if (!haveLiveReaction)
                    continue;

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
                        if (_delayedDeleteList.Check(eid) ||
                            pendingEntities[reactWrapperId].Check(eid) ||
                            !IsIdValid(eid) ||
                            !Have(reactWrapperId, eid))
                            continue;

                        RemoveInternal(reactWrapperId, eid);
                        if (HasNoComponents(eid))
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
                        if (_delayedDeleteList.Check(eid) ||
                            !IsIdValid(eid) ||
                            !Have(node.RequiredMask, eid))
                            continue;
                        EnsureMatchReact(eid);
                        markedEntities.Set(eid);
                    }

                    if (markedEntities.Length > 0)
                        node.Invoke(this);
                }
                finally
                {
                    foreach (var eid in markedEntities)
                    {
                        if (_delayedDeleteList.Check(eid) || !IsIdValid(eid) || !Have<MatchReact>(eid))
                            continue;
                        RemoveInternal<MatchReact>(eid);
                        if (HasNoComponents(eid))
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
        /// reactive teardown callbacks are required. While the world is locked the entity stays
        /// alive, with its components, until the outermost Unlock; deleting it twice is harmless.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Delete(EntityType eid)
        {
            if (_lockCounter > 0)
            {
                _delayedDeleteList.Set(eid);
                return;
            }

#if DEBUG && !ECS_PERF_TEST
            if (_delayedDeleteList.Length > 0)
                throw new EcsException("_delayedDeleteList is not empty here");
            if (!IsIdValid(eid))
                throw new EcsException($"trying to delete dead or null entity {eid}");
#else
            if (!IsIdValid(eid))
                return;
#endif
            DestroyNow(eid);
        }

#region Debug methods
        public void GetTypesForId(int id, HashSet<Type> buffer)
        {
            buffer.Clear();
            if (!IsAliveId(id))
                return;
            foreach (var storage in Storages)
            {
                if (storage != null && storage.Has(id))
                    buffer.Add(storage.GetComponentType());
            }
        }

        private StringBuilder _debugEntityStringBuilder;
        public string DebugEntity(int id, bool printFields = false)
        {
            if (id == EntityExtension.NullId)
                return "null entity";
            if (id < 0)
                return "negative entity";
            if (IsDead(id))
                return "dead entity";
            _debugEntityStringBuilder ??= new StringBuilder();
            foreach (var storage in Storages)
            {
                if (storage != null && storage.Has(id))
                    _debugEntityStringBuilder.Append(storage.DebugString(id, printFields)).Append("\n");
            }
            var result = _debugEntityStringBuilder.ToString();
            _debugEntityStringBuilder.Clear();
            return result;
        }

        public void DebugAll(StringBuilder sb, bool printFields)
        {
            for (int id = 1; id < SlotCapacity; id++)
            {
                if (!IsAliveId(id))
                    continue;
                sb.Append(id + ": " + DebugEntity(id, printFields));
                sb.Append('\n');
            }
        }
#endregion
    }
}
