using System;

namespace CodexECS
{
    // Per component id: builds that component's storage for one world. Filled by ComponentMeta<T>.
    internal static class PoolFactory
    {
        internal static readonly SparseSet<Func<EcsWorld, int, ComponentStorage>> FactoryMethods;

        static PoolFactory() => FactoryMethods = new ();
    }
}
