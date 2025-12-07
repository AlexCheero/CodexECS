using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;

namespace CodexECS
{
    public interface IComponent { }

    static class ComponentIdCounter
    {
        internal static int Counter = -1;
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

        private static void DefaultInit(ref T defaultValue) { }
        public delegate void InitDelegate(ref T instance);
        public static InitDelegate Init //Init methods should always assume that component is already inited
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private set;
        } = DefaultInit;
        
        private static void DefaultCleanup(ref T defaultValue) => defaultValue = Default;
        public delegate void CleanupDelegate(ref T instance);
        public static CleanupDelegate Cleanup
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private set;
        } = DefaultCleanup;

        public static readonly int InitialPoolSize;
        
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
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            IsTag = fields.Length == 0 && type.IsValueType && !type.IsEnum;

            if (!IsTag)
            {
                var defaultValueGetter = type.GetProperty(nameof(Default),
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (defaultValueGetter != null)
                    Default = (T)defaultValueGetter.GetValue(null);
                else
                    Default = default;

                var initMethod = type.GetMethod(nameof(Init), BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (initMethod != null)
                    Init = (InitDelegate)Delegate.CreateDelegate(typeof(InitDelegate), initMethod);
                
                var cleanupMethod = type.GetMethod(nameof(Cleanup), BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (cleanupMethod != null)
                    Cleanup = (CleanupDelegate)Delegate.CreateDelegate(typeof(CleanupDelegate), cleanupMethod);
                
                var initialPoolSizeField = type.GetField(nameof(InitialPoolSize), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (initialPoolSizeField != null)
                    InitialPoolSize = (int)initialPoolSizeField.GetRawConstantValue();
                else
                    InitialPoolSize = 2;//default value
                
                PoolFactory.FactoryMethods.Add(Id, GetComponentsPool);
            }
            else
            {
                PoolFactory.FactoryMethods.Add(Id, GetTagsPool);  
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static IComponentsPool GetComponentsPool()
        {
#if DEBUG
            if (IsTag)
                throw new EcsException("trying to get tags pool for data component");
#endif
            return new ComponentsPool<T>();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static IComponentsPool GetTagsPool()
        {
#if DEBUG
            if (!IsTag)
                throw new EcsException("trying to get components pool for tag component");
#endif
            return new TagsPool<T>();
        }
    }
}
