using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace CodexECS
{
    public static class ComponentMapping
    {
        public interface IWorldCallDispatcher
        {
            public void Add(EcsWorld world, int id, object obj);
            public void AddMultiple(EcsWorld world, int id, object obj);
            public void Copy(EcsWorld world, int from, int to);
        }

        private class WorldCallDispatcher<T> : IWorldCallDispatcher
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Add(EcsWorld world, int id, object obj) => world.Add(id, (T)obj);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void AddMultiple(EcsWorld world, int id, object obj) => world.AddMultiple(id, (T)obj);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Copy(EcsWorld world, int from, int to) => world.CopyComponent<T>(from, to);
        }

        public static readonly Dictionary<Type, IWorldCallDispatcher> CallDispatchers = new();
        private static readonly Dictionary<Type, int> TypeToId = new();
        private static readonly Dictionary<int, Type> IDToType = new();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetIdForType(Type type) => TypeToId[type];
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Type GetTypeForId(int id) => IDToType[id];
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool HaveType(Type type) => TypeToId.ContainsKey(type);
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool HaveId(int id) => IDToType.ContainsKey(id);
        
        public static void Add(Type type, int id)
        {
#if DEBUG && !ECS_PERF_TEST
            if (TypeToId.ContainsKey(type))
                throw new EcsException($"Components mapping desynch. TypeToId already contains {type.FullName}");
            if (IDToType.ContainsKey(id))
                throw new EcsException($"Components mapping desynch. IdToType already contains {id}");
#endif
            
            TypeToId[type] = id;
            IDToType[id] = type;
            
            var closedType = typeof(WorldCallDispatcher<>).MakeGenericType(type);
            CallDispatchers[type] = (IWorldCallDispatcher)Activator.CreateInstance(closedType);

#if DEBUG && !ECS_PERF_TEST
            foreach (var pair in TypeToId)
            {
                if (!IDToType.ContainsKey(pair.Value))
                    throw new EcsException($"Components mapping desynch");
            }
            
            foreach (var pair in IDToType)
            {
                if (!TypeToId.ContainsKey(pair.Value))
                    throw new EcsException($"Components mapping desynch");
            }
            
            if (TypeToId.GroupBy(kv => kv.Value).Any(g => g.Count() > 1))
                throw new EcsException($"Components mapping desynch");
            if (IDToType.GroupBy(kv => kv.Value).Any(g => g.Count() > 1))
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
                _debugBuilder.Append(GetTypeForId(bit).FullName + ",\n");
            _debugBuilder.Remove(_debugBuilder.Length - 2, 2);
            _debugBuilder.Append(" }");
            
            return _debugBuilder.ToString();
        }
#endif
    }
}