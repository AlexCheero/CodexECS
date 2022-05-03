using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace ECS
{
    static class ComponentIdCounter
    {
        internal static int Counter = -1;
    }

    public static class ComponentMeta<T>
    {
        public static int Id
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] get;
            [MethodImpl(MethodImplOptions.AggressiveInlining)] private set;
        }

        static ComponentMeta()
        {
            Id = Interlocked.Increment(ref ComponentIdCounter.Counter);
        }
    }

    public static class ComponentRegistartor
    {
#if DEBUG
        private static HashSet<Type> _registeredTypes;
#endif

        internal delegate IComponentsPool PoolFactory();
        private static Dictionary<int, PoolFactory> _poolFactories;

#if DEBUG
        internal static bool IsRegistered<T>()
        {
            return _registeredTypes.Contains(typeof(T));
        }
#endif

        internal static IComponentsPool CreatePool(int typeIdx)
        {
#if DEBUG
            if (!_poolFactories.ContainsKey(typeIdx))
                throw new EcsException("component with id " + typeIdx + "not registered");
#endif
            return _poolFactories[typeIdx]();
        }

        static ComponentRegistartor()
        {
#if DEBUG
            _registeredTypes = new HashSet<Type>();
#endif
            _poolFactories = new Dictionary<int, PoolFactory>();
        }

        public static void RegisterComponent<T>()
        {
#if DEBUG
            if (IsRegistered<T>())
                throw new EcsException("component already registered " + typeof(T));
            _registeredTypes.Add(typeof(T));
#endif
            var id = ComponentMeta<T>.Id;
            _poolFactories.Add(id, () => ComponentsPool<T>.CreateUninitialized());
        }

        public static void RegisterTag<T>()
        {
#if DEBUG
            if (IsRegistered<T>())
                throw new EcsException("component already registered " + typeof(T));
            _registeredTypes.Add(typeof(T));
#endif
            var id = ComponentMeta<T>.Id;
            _poolFactories.Add(id, () => new TagsPool<T>());
        }
    }
}
