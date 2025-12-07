using System;

namespace CodexECS
{
    internal static class PoolFactory
    {
        internal static readonly SparseSet<Func<IComponentsPool>> FactoryMethods;

        static PoolFactory() => FactoryMethods = new ();
    }
}