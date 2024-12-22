using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;

namespace CodexECS
{
    public interface IComponent { }
    public interface ITag { }

    static class ComponentIdCounter
    {
        internal static int Counter = -1;
    }

    public static class ComponentMapping
    {
        public static Dictionary<Type, int> TypeToId = new();
        public static Dictionary<int, Type> IdToType = new();
    }

    public static class ComponentMeta<T>
    {
        public static int Id
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private set;
        }

        public static bool IsTag
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private set;
        }

        private static readonly T Default;
        public static T GetDefault()
        {
            var defaultValue = Default;
            Init(ref defaultValue);
            return defaultValue;
        }

        private delegate void InitDelegate(ref T instance);
        private static readonly InitDelegate Init = delegate {};

        static ComponentMeta()
        {
            Id = Interlocked.Increment(ref ComponentIdCounter.Counter);
            var type = typeof(T);
            ComponentMapping.TypeToId[type] = Id;
            ComponentMapping.IdToType[Id] = type;
            IsTag = typeof(ITag).IsAssignableFrom(type);
            var defaultValueGetter = type.GetProperty("Default",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (defaultValueGetter != null)
                Default = (T)defaultValueGetter.GetValue(null);
            else
                Default = default;

            var initMethod = type.GetMethod("Init", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (initMethod != null)
                Init = (InitDelegate)Delegate.CreateDelegate(typeof(InitDelegate), initMethod);

            if (IsTag)
                PoolFactory.FactoryMethods.Add(Id, (poolSize) => new TagsPool<T>(poolSize));
            else
                PoolFactory.FactoryMethods.Add(Id, (poolSize) => new ComponentsPool<T>(poolSize));
        }
    }

    internal static class PoolFactory
    {
        internal static readonly SparseSet<Func<int, IComponentsPool>> FactoryMethods;

        static PoolFactory() => FactoryMethods = new ();
    }
}
