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
        }

        public class WorldCallDispatcher<T> : IWorldCallDispatcher
        {
            public void Add(EcsWorld world, int id, object obj) => world.Add(id, (T)obj);
            public void AddMultiple(EcsWorld world, int id, object obj) => world.AddMultiple(id, (T)obj);
        }

        public readonly static Dictionary<Type, IWorldCallDispatcher> CallDispatchers;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static WorldCallDispatcher<T> GetCallDispatcher<T>() =>
            (WorldCallDispatcher<T>)CallDispatchers[typeof(T)];

        private static Dictionary<Type, int> _typeToId;
        private static Dictionary<int, Type> _idToType;

        static ComponentMapping()
        {
            _typeToId = new();
            _idToType = new();
            CallDispatchers = new();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetIdForType(Type type) => _typeToId[type];
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Type GetTypeForId(int id) => _idToType[id];
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool HaveType(Type type) => _typeToId.ContainsKey(type);
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool HaveId(int id) => _idToType.ContainsKey(id);
        
        public static void Add<T>(Type type, int id)
        {
#if DEBUG && !ECS_PERF_TEST
            if (_typeToId.ContainsKey(type))
                throw new EcsException($"Components mapping desynch. TypeToId already contains {type.FullName}");
            if (_idToType.ContainsKey(id))
                throw new EcsException($"Components mapping desynch. IdToType already contains {id}");
#endif
            
            _typeToId[type] = id;
            _idToType[id] = type;
            CallDispatchers[type] = new WorldCallDispatcher<T>();

#if DEBUG && !ECS_PERF_TEST
            foreach (var pair in _typeToId)
            {
                if (!_idToType.ContainsKey(pair.Value))
                    throw new EcsException($"Components mapping desynch");
            }
            
            foreach (var pair in _idToType)
            {
                if (!_typeToId.ContainsKey(pair.Value))
                    throw new EcsException($"Components mapping desynch");
            }
            
            if (_typeToId.GroupBy(kv => kv.Value).Any(g => g.Count() > 1))
                throw new EcsException($"Components mapping desynch");
            if (_idToType.GroupBy(kv => kv.Value).Any(g => g.Count() > 1))
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