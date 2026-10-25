using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace CodexECS
{
    /// <summary>
    /// Owns and executes ECS systems independently from any engine-specific update loop.
    /// Filters resolved with gateSystem enabled while a system is being constructed are
    /// used to avoid ticking that system when none of its gating filters can produce work.
    /// Systems with no gating filters run regardless of filter emptiness.
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
        /// Gating without cached emptiness: a system with captured filters runs when at least one of them
        /// matches an entity at the moment it is considered, so changes made by earlier systems of the same
        /// Tick are observed. IsEmpty scans until the first match.
        /// </summary>
        private sealed class SystemGraph : IDisposable
        {
            private readonly List<EcsFilter[]> _filtersBySystem = new();

            public void AddSystem(int systemIndex, List<CapturedFilter> filters)
            {
                while (_filtersBySystem.Count <= systemIndex)
                    _filtersBySystem.Add(null);
                if (filters.Count == 0)
                    return;
                var systemFilters = new EcsFilter[filters.Count];
                for (int i = 0; i < filters.Count; i++)
                    systemFilters[i] = filters[i].Filter;
                _filtersBySystem[systemIndex] = systemFilters;
            }

            public bool ShouldRun(int systemIndex)
            {
                var filters = _filtersBySystem[systemIndex];
                if (filters == null)
                    return true;
                for (int i = 0; i < filters.Length; i++)
                {
                    if (!filters[i].IsEmpty)
                        return true;
                }
                return false;
            }

            public void Dispose() => _filtersBySystem.Clear();
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
#if PERFORMANCE_BENCHMARK
        public Action<TGroup, EcsSystem, long> SystemTicked;
#endif

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

            for (int i = 0; i < state.Registrations.Count; i++)
            {
                var registration = state.Registrations[i];
                if (!registration.Active || (IsPaused && !force && !registration.NonPausable))
                    continue;
                if (!state.Graph.ShouldRun(i))
                    continue;

#if PERFORMANCE_BENCHMARK
                var benchmarkStart = SystemTicked != null ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
#endif
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
#if PERFORMANCE_BENCHMARK
                    SystemTicked?.Invoke(group, registration.System,
                        System.Diagnostics.Stopwatch.GetTimestamp() - benchmarkStart);
#endif
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
