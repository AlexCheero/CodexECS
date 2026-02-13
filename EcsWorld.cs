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

        private FilterBuilder _filterBuilder;

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

            _dirtyMatchMasksSet = new(BitMask.MaskComparer);
            _dirtyMatchMasksList = new();
            _onMatchCallbacks = new(BitMask.MaskComparer);
            _componentToMatchMaskMapping = new();
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
        public EntityType Create()
        {
            var entity = _entityManager.Create();
            _archetypes.AddToEmptyArchetype(entity);

#if USE_DEBUG_TRACE_COMPONENT && DEBUG
            Add<DebugTraceData>(entity);
#endif

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

        private HashSet<BitMask> _dirtyMatchMasksSet;
        private List<BitMask> _dirtyMatchMasksList;
        private Dictionary<BitMask, Action<EcsWorld>> _onMatchCallbacks;
        private SparseSet<HashSet<BitMask>> _componentToMatchMaskMapping;
        public void SubscribeOnComponentsSetMatch(BitMask mask, Action<EcsWorld> callback)
        {
            if (!_onMatchCallbacks.ContainsKey(mask))
                _onMatchCallbacks.Add(mask, callback);
            else
                _onMatchCallbacks[mask] += callback;

            foreach (var componentId in mask)
            {
                if (!_componentToMatchMaskMapping.ContainsIdx(componentId))
                    _componentToMatchMaskMapping.Add(componentId, new(BitMask.MaskComparer));
                _componentToMatchMaskMapping[componentId].Add(mask);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T AddMultiple<T>(EntityType eid)
        {
#if DEBUG && !ECS_PERF_TEST
            if (ComponentMeta<T>.IsTag)
                throw new EcsException("Tags are not assumed to be added multiple times, use component with counter instead");
#endif
            return ref AddMultiple(eid, _componentManager.GetNextFree<T>());
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
            if (!Have<MultipleComponents<T>>(eid))
            {
                Add<MultipleComponents<T>>(eid);
                components = Get<MultipleComponents<T>>(eid).components;
                components.Add(Get<T>(eid));
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
            if (!Have<MultipleComponents<T>>(eid))
            {
                Remove<T>(eid);
                return;
            }
            
            ref var firstComponent = ref Get<T>(eid);
            ComponentMeta<T>.Cleanup(ref firstComponent);
            var components = Get<MultipleComponents<T>>(eid).components;
            components.SwapRemoveAt(removeAt);
            if (components.Length == 0)
                Remove<MultipleComponents<T>>(eid);
            else
                firstComponent = Get<MultipleComponents<T>>(eid).components[0];
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveMultipleAll<T>(EntityType eid)
        {
            Remove<T>(eid);
            if (Have<MultipleComponents<T>>(eid))
                Remove<MultipleComponents<T>>(eid);
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
            
            _archetypes.AddComponent<T>(eid);
            _componentManager.Add<T>(eid, component);

            var componentId = ComponentMeta<T>.Id;
            if (_addReactGuard.Check(componentId))
            {
                var reactWrapperId = ComponentMeta<AddReact<T>>.Id;
                //excess check- it already checked in react type guard
                //if (_onAddCallbacks.ContainsIdx(reactWrapperId))
                {
                    //unrolled Add<AddReact<T>>(eid); without wrapper check
                    _archetypes.AddComponent<AddReact<T>>(eid);
                    _componentManager.Add<AddReact<T>>(eid);

                    _dirtyAddMask.Set(reactWrapperId);
                }
            }

            if (_componentToMatchMaskMapping.ContainsIdx(componentId))
            {
                ref readonly var mask = ref _archetypes.GetMask(eid);
                foreach (var key in _componentToMatchMaskMapping[componentId])
                {
                    if (mask.InclusivePass(key))
                        _dirtyMatchMasksSet.Add(key);
                }

                if (!Have<MatchReact>(eid))
                    Add<MatchReact>(eid);
            }

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
            if (!Have<T>(to))
                Add<T>(to);
            Get<T>(to) = Get<T>(from);
        }

#if DEBUG
        private bool IsReactWrapperType<T>() => IsReactWrapperType(ComponentMeta<T>.Id);

        private bool IsReactWrapperType(int componentId)
        {
            var gtd = Utils.GetGenericTypeDefinition(ComponentMapping.GetTypeForId(componentId));
            return gtd == typeof(AddReact<>) || gtd == typeof(RemoveReact<>);
        }
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddMultiple_Dynamic(Type type, int id, object component) =>
            ComponentMapping.CallDispatchers[type].AddMultiple(this, id, component);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add_Dynamic(Type type, int id, object component) =>
            ComponentMapping.CallDispatchers[type].Add(this, id, component);

        public IComponentsPool GetPool<T>() => _componentManager.GetPool(ComponentMeta<T>.Id);
        
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
            if (Have<MultipleComponents<T>>(eid))
                throw new EcsException($"entity have multiple components of type {typeof(T).Name}, use {nameof(RemoveMultiple)} instead");
#endif

            if (_removeReactGuard.Check(ComponentMeta<T>.Id))
            {
                var reactWrapperId = ComponentMeta<RemoveReact<T>>.Id;
                //excess check- it already checked in react type guard
                //if (_onRemoveCallbacks.ContainsIdx(reactWrapperId))
                {
                    _archetypes.AddComponent<RemoveReact<T>>(eid);
                    _componentManager.Add(eid, new RemoveReact<T>
                    {
                        removingComponent = Get<T>(eid)
                    });

                    _dirtyRemoveMask.Set(reactWrapperId);
                }
            }
            
            _archetypes.RemoveComponent<T>(eid);
            _componentManager.Remove<T>(eid);

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

            //TODO: RemoveAll for reactive types not implemented
            //TODO: _onRemoveCallbacks contains Ids for wrappers, not for components itself 
            // if (_onRemoveCallbacks.ContainsIdx(componentId))
            // {
            //     throw new NotImplementedException("RemoveAll for reactive components not implemented");
            // }
            
            _archetypes.RemoveAll(componentId);
            _componentManager.RemoveAll(componentId);
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

        public FilterBuilder Filter() => new FilterBuilder { _world = this };

        private int _lockCounter;
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
            foreach (var eid in _delayedDeleteList)
                Delete_Impl(eid);
            _delayedDeleteList.Clear();

            if (_dirtyAddMask.Length > 0)
                ReactOnAddRemove(ref _dirtyAddMask, _onAddCallbacks);
            if (_dirtyRemoveMask.Length > 0)
                ReactOnAddRemove(ref _dirtyRemoveMask, _onRemoveCallbacks);

            if (_dirtyMatchMasksSet.Count > 0)
            {
                _dirtyMatchMasksList.Clear();
                foreach (var mask in _dirtyMatchMasksSet)
                    _dirtyMatchMasksList.Add(mask);
                _dirtyMatchMasksList.Sort((m1, m2) =>
                {
                    //branchless sign function
                    int diff = m1.SetBitsCount - m2.SetBitsCount;
                    return (diff >> 31) | ((-diff) >> 31 & 1);
                });
                _dirtyMatchMasksSet.Clear();
                for (int i = 0; i < _dirtyMatchMasksList.Count; i++)
                    _onMatchCallbacks[_dirtyMatchMasksList[i]](this);

                RemoveAll<MatchReact>();
            }
        }

        private void ReactOnAddRemove(ref BitMask dirtyMask, SparseSet<Action<EcsWorld>> callbacks)
        {
            Lock();
                
            foreach (var reactWrapperId in dirtyMask)
            {
                var callback = callbacks[reactWrapperId];
#if DEBUG && !ECS_PERF_TEST
                if (callback == null)
                    throw new EcsException("no registered on add callback for type " + ComponentMapping.GetTypeForId(reactWrapperId));
#endif
                callback(this);
                //unrolled RemoveAll(reactWrapperId); without wrapper check
                //MUST already be registered!
                // if (!_componentManager.IsTypeRegistered(reactWrapperId)) continue;
                _archetypes.RemoveAll(reactWrapperId);
                _componentManager.RemoveAll(reactWrapperId);
            }
            dirtyMask.Clear();
                
            Unlock();
        }

        private BitMask _delayedDeleteList;
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
        public string DebugEntity(int id, bool printFields)
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
