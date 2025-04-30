using System;

namespace CodexECS
{
    internal static class PoolFactory
    {
        internal static readonly SparseSet<Func<int, IComponentsPool>> FactoryMethods;

        static PoolFactory() => FactoryMethods = new ();
    }
}