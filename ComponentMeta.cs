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
}
