using System.Reflection;
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
        
        public static bool IsTag { get; private set; }

        private const BindingFlags SearchFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        static ComponentMeta()
        {
            Id = Interlocked.Increment(ref ComponentIdCounter.Counter);
            IsTag = typeof(T).GetFields(SearchFlags).Length == 0 &&
                    typeof(T).GetProperties(SearchFlags).Length == 0;
        }
    }
}
