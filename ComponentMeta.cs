using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
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
        private static Dictionary<Type, int> TypeToId = new();
        private static Dictionary<int, Type> IdToType = new();
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetIdForType(Type type) => TypeToId[type];
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Type GetTypeForId(int id) => IdToType[id];
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool HaveType(Type type) => TypeToId.ContainsKey(type);
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool HaveId(int id) => IdToType.ContainsKey(id);
        
        public static void Add(Type type, int id)
        {
#if DEBUG && !ECS_PERF_TEST
            if (TypeToId.ContainsKey(type))
                throw new EcsException($"Components mapping desynch. TypeToId already contains {type.FullName}");
            if (IdToType.ContainsKey(id))
                throw new EcsException($"Components mapping desynch. IdToType already contains {id}");
#endif
            
            TypeToId[type] = id;
            IdToType[id] = type;

#if DEBUG && !ECS_PERF_TEST
            foreach (var pair in TypeToId)
            {
                if (!IdToType.ContainsKey(pair.Value))
                    throw new EcsException($"Components mapping desynch");
            }
            
            foreach (var pair in IdToType)
            {
                if (!TypeToId.ContainsKey(pair.Value))
                    throw new EcsException($"Components mapping desynch");
            }
            
            if (TypeToId.GroupBy(kv => kv.Value).Any(g => g.Count() > 1))
                throw new EcsException($"Components mapping desynch");
            if (IdToType.GroupBy(kv => kv.Value).Any(g => g.Count() > 1))
                throw new EcsException($"Components mapping desynch");
#endif
        }

#if DEBUG
        private static StringBuilder _debugBuilder;
        public static string DebugByMask(BitMask mask)
        {
            if (mask.Length == 0)
                return "{ }";
            
            _debugBuilder ??= new();
            _debugBuilder.Clear();
                
            _debugBuilder.Append("{ ");
            foreach (var bit in mask)
                _debugBuilder.Append(GetTypeForId(bit).FullName + ", ");
            _debugBuilder.Remove(_debugBuilder.Length - 2, 2);
            _debugBuilder.Append(" }");
            
            return _debugBuilder.ToString();
        }
#endif
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

        public delegate void InitDelegate(ref T instance);
        public static InitDelegate Init{
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private set;
        } = delegate {};

        static ComponentMeta()
        {
            var type = typeof(T);
#if DEBUG
            if (ComponentMapping.HaveType(type))
                throw new EcsException(
                    "component type already registered. this may happen due to bug in EntityView.CallStaticCtorForComponentMeta");
#endif
            
            Id = Interlocked.Increment(ref ComponentIdCounter.Counter);
            ComponentMapping.Add(type, Id);
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
