using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace CodexECS
{
    /// <summary>
    /// Owns and executes ECS systems independently from any engine-specific update loop.
    /// Filters resolved while a system is being constructed are used to avoid ticking that
    /// system when none of the filters can produce work.
    /// </summary>
    public sealed class EcsPipeline<TGroup> : IDisposable where TGroup : struct, Enum
    {
        public sealed class Registration
        {
            private readonly EcsFilter[] _filters;

            public EcsSystem System { get; }
            public bool Active { get; set; }
            public bool NonPausable { get; set; }
            public IReadOnlyList<EcsFilter> Filters => _filters;

            internal Registration(EcsSystem system, bool active, bool nonPausable, EcsFilter[] filters)
            {
                System = system;
                Active = active;
                NonPausable = nonPausable;
                _filters = filters;
            }
        }

        private readonly struct CapturedFilter
        {
            public readonly EcsFilter Filter;
            public readonly FilterMasks Masks;

            public CapturedFilter(EcsFilter filter, in FilterMasks masks)
            {
                Filter = filter;
                Masks = new FilterMasks
                {
                    Includes = masks.Includes.Duplicate(),
                    Excludes = masks.Excludes.Duplicate()
                };
            }
        }

        private sealed class GroupState : IDisposable
        {
            public readonly List<Registration> Registrations = new();
            public readonly Dictionary<Type, Registration> RegistrationsByType = new();
            public readonly SystemGraph Graph = new();

            public void Dispose() => Graph.Dispose();
        }

        /// <summary>
        /// A subset tree of filters. A child is always at least as restrictive as its
        /// parent, so an empty parent lets the whole branch be pruned safely.
        /// </summary>
        private sealed class SystemGraph : IDisposable
        {
            private sealed class Node
            {
                public readonly EcsFilter Filter;
                public readonly FilterMasks Masks;
                public readonly List<Node> Children = new();
                public BitMask Systems;
                public Node Parent;

                public Node(EcsFilter filter, in FilterMasks masks)
                {
                    Filter = filter;
                    Masks = masks;
                }
            }

            private readonly Node _root = new(null, default);
            private readonly Dictionary<EcsFilter, Node> _nodes = new();
            private BitMask _systemsWithoutFilters;
            private BitMask _runnableSystems;
            private bool _dirty = true;
            private bool _disposed;

            public void AddSystem(int systemIndex, List<CapturedFilter> filters)
            {
                if (filters.Count == 0)
                {
                    _systemsWithoutFilters.Set(systemIndex);
                    _dirty = true;
                    return;
                }

                for (int i = 0; i < filters.Count; i++)
                {
                    var node = GetOrAddNode(filters[i]);
                    node.Systems.Set(systemIndex);
                }

                _dirty = true;
            }

            public bool ShouldRun(int systemIndex)
            {
                RebuildRunnableSystemsIfNeeded();
                return _runnableSystems.Check(systemIndex);
            }

            private Node GetOrAddNode(in CapturedFilter captured)
            {
                if (_nodes.TryGetValue(captured.Filter, out var existing))
                    return existing;

                var parent = _root;
                var parentSpecificity = -1;
                foreach (var candidate in _nodes.Values)
                {
                    if (!IsDerivative(captured.Masks, candidate.Masks))
                        continue;

                    var specificity = GetSpecificity(candidate.Masks);
                    if (specificity <= parentSpecificity)
                        continue;

                    parent = candidate;
                    parentSpecificity = specificity;
                }

                var node = new Node(captured.Filter, captured.Masks)
                {
                    Parent = parent
                };

                // Keep the tree as compact as possible when a less restrictive filter is
                // registered after filters that derive from it.
                for (int i = parent.Children.Count - 1; i >= 0; i--)
                {
                    var child = parent.Children[i];
                    if (!IsDerivative(child.Masks, node.Masks))
                        continue;

                    parent.Children.RemoveAt(i);
                    child.Parent = node;
                    node.Children.Add(child);
                }

                parent.Children.Add(node);
                _nodes.Add(captured.Filter, node);
                captured.Filter.EmptinessChanged += MarkDirty;
                return node;
            }

            private static bool IsDerivative(in FilterMasks child, in FilterMasks parent)
            {
                return child.Includes.InclusivePass(parent.Includes) &&
                       child.Excludes.InclusivePass(parent.Excludes);
            }

            private static int GetSpecificity(in FilterMasks masks)
            {
                return masks.Includes.SetBitsCount + masks.Excludes.SetBitsCount;
            }

            private void RebuildRunnableSystemsIfNeeded()
            {
                if (!_dirty)
                    return;

                _runnableSystems.Clear();
                _runnableSystems.Set(_systemsWithoutFilters);
                for (int i = 0; i < _root.Children.Count; i++)
                    AddRunnableBranch(_root.Children[i]);

                _dirty = false;
            }

            private void AddRunnableBranch(Node node)
            {
                if (node.Filter.EntitiesCount == 0)
                    return;

                _runnableSystems.Set(node.Systems);
                for (int i = 0; i < node.Children.Count; i++)
                    AddRunnableBranch(node.Children[i]);
            }

            private void MarkDirty() => _dirty = true;

            public void Dispose()
            {
                if (_disposed)
                    return;

                _disposed = true;
                foreach (var pair in _nodes)
                    pair.Key.EmptinessChanged -= MarkDirty;

                _nodes.Clear();
                _root.Children.Clear();
            }
        }

        private sealed class ReferenceComparer<T> : IEqualityComparer<T> where T : class
        {
            public static readonly ReferenceComparer<T> Instance = new();

            public bool Equals(T x, T y) => ReferenceEquals(x, y);
            public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
        }

        private readonly Dictionary<TGroup, GroupState> _groups;
        private readonly List<Registration> _registrations = new();
        private bool _disposed;

        public EcsWorld World { get; }
        public bool IsPaused { get; private set; }

        public EcsPipeline(EcsWorld world, params TGroup[] groups)
        {
            World = world ?? throw new ArgumentNullException(nameof(world));
            if (groups == null)
                throw new ArgumentNullException(nameof(groups));

            _groups = new Dictionary<TGroup, GroupState>(groups.Length);
            for (int i = 0; i < groups.Length; i++)
            {
                if (_groups.ContainsKey(groups[i]))
                    throw new ArgumentException($"Group '{groups[i]}' was supplied more than once.", nameof(groups));

                _groups.Add(groups[i], new GroupState());
            }
        }

        public Registration Register(
            TGroup group,
            Func<EcsWorld, EcsSystem> factory,
            bool active = true,
            bool nonPausable = false)
        {
            ThrowIfDisposed();
            if (factory == null)
                throw new ArgumentNullException(nameof(factory));

            var state = GetGroup(group);
            var capturedFilters = new List<CapturedFilter>();
            var capturedFilterSet = new HashSet<EcsFilter>(ReferenceComparer<EcsFilter>.Instance);

            void CaptureFilter(EcsFilter filter, FilterMasks masks)
            {
                if (filter != null && capturedFilterSet.Add(filter))
                    capturedFilters.Add(new CapturedFilter(filter, masks));
            }

            EcsSystem system;
            World.FilterResolved += CaptureFilter;
            try
            {
                system = factory(World);
            }
            finally
            {
                World.FilterResolved -= CaptureFilter;
            }

            if (system == null)
                throw new InvalidOperationException("The system factory returned null.");

            var systemType = system.GetType();
            if (state.RegistrationsByType.ContainsKey(systemType))
                throw new InvalidOperationException(
                    $"System '{systemType.FullName}' is already registered in group '{group}'.");

            var filters = new EcsFilter[capturedFilters.Count];
            for (int i = 0; i < capturedFilters.Count; i++)
                filters[i] = capturedFilters[i].Filter;

            var registration = new Registration(system, active, nonPausable, filters);
            var systemIndex = state.Registrations.Count;
            state.Registrations.Add(registration);
            state.RegistrationsByType.Add(systemType, registration);
            state.Graph.AddSystem(systemIndex, capturedFilters);
            _registrations.Add(registration);
            return registration;
        }

        public Registration GetRegistration<TSystem>(TGroup group) where TSystem : EcsSystem
        {
            return GetRegistration(group, typeof(TSystem));
        }

        public Registration GetRegistration(TGroup group, Type systemType)
        {
            ThrowIfDisposed();
            if (systemType == null)
                throw new ArgumentNullException(nameof(systemType));

            var state = GetGroup(group);
            if (!state.RegistrationsByType.TryGetValue(systemType, out var registration))
                throw new KeyNotFoundException(
                    $"System '{systemType.FullName}' is not registered in group '{group}'.");

            return registration;
        }

        /// <summary>
        /// Initializes active systems in registration order. Initialization is deliberately
        /// not filter-pruned because it commonly creates the first matching entities.
        /// </summary>
        public void Initialize(TGroup group)
        {
            ThrowIfDisposed();
            var registrations = GetGroup(group).Registrations;
            for (int i = 0; i < registrations.Count; i++)
            {
                var registration = registrations[i];
                if (!registration.Active)
                    continue;

                World.Lock();
                try
                {
                    registration.System.Init(World);
                }
                finally
                {
                    World.Unlock();
                }
            }
        }

        /// <summary>
        /// Ticks runnable systems in registration order. A paused pipeline only ticks
        /// non-pausable systems unless force is true.
        /// </summary>
        public void Tick(TGroup group, bool force = false)
        {
            ThrowIfDisposed();
            var state = GetGroup(group);
            var isPaused = IsPaused && !force;

            for (int i = 0; i < state.Registrations.Count; i++)
            {
                var registration = state.Registrations[i];
                if (!registration.Active || (isPaused && !registration.NonPausable))
                    continue;
                if (!state.Graph.ShouldRun(i))
                    continue;

                World.Lock();
                try
                {
                    registration.System.Tick(World);
                }
                finally
                {
                    // Unlock is the reactive flush boundary between systems. Filter
                    // emptiness changes raised during the flush dirty the graph before
                    // the next registration is considered.
                    World.Unlock();
                }
            }
        }

        public void Pause()
        {
            ThrowIfDisposed();
            IsPaused = true;
        }

        public void Unpause()
        {
            ThrowIfDisposed();
            IsPaused = false;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            foreach (var state in _groups.Values)
                state.Dispose();

            Exception firstException = null;
            var disposedSystems = new HashSet<EcsSystem>(ReferenceComparer<EcsSystem>.Instance);
            for (int i = 0; i < _registrations.Count; i++)
            {
                var system = _registrations[i].System;
                if (!(system is IDisposable disposable) || !disposedSystems.Add(system))
                    continue;

                try
                {
                    disposable.Dispose();
                }
                catch (Exception exception)
                {
                    if (firstException == null)
                        firstException = exception;
                }
            }

            if (firstException != null)
                throw firstException;
        }

        private GroupState GetGroup(TGroup group)
        {
            if (!_groups.TryGetValue(group, out var state))
                throw new ArgumentOutOfRangeException(nameof(group), group, "The group is not part of this pipeline.");

            return state;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(GetType().FullName);
        }
    }
}
