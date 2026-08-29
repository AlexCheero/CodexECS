#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;

namespace CodexECS.Tests
{
    public sealed class ComponentsSetMatchGraphTests
    {
        [Test]
        public void Subscribe_EqualMasksMergeCallbacks()
        {
            var graph = new ComponentsSetMatchGraph();
            var calls = 0;
            var first = graph.Subscribe(new BitMask(1, 35), _ => calls++);
            var second = graph.Subscribe(new BitMask(35, 1), _ => calls += 10);
            var matches = new List<ComponentsSetMatchGraph.Node>();

            graph.CollectMatches(new BitMask(1, 8, 35), matches);

            Assert.AreSame(first, second);
            Assert.AreEqual(1, matches.Count);
            matches[0].Invoke(null);
            Assert.AreEqual(11, calls);
        }

        [Test]
        public void Subscribe_UsesClosestSubsetAndReparentsDirectSupersets()
        {
            var graph = new ComponentsSetMatchGraph();
            var abc = graph.Subscribe(new BitMask(1, 2, 3), _ => { });
            var a = graph.Subscribe(new BitMask(1), _ => { });
            var ab = graph.Subscribe(new BitMask(1, 2), _ => { });

            Assert.AreSame(a, ab.Parent);
            Assert.AreSame(ab, abc.Parent);
        }

        [Test]
        public void CollectMatches_VisitsEverySatisfiedBranchInParentBeforeChildOrder()
        {
            var graph = new ComponentsSetMatchGraph();
            var a = graph.Subscribe(new BitMask(1), _ => { });
            var ab = graph.Subscribe(new BitMask(1, 2), _ => { });
            var ac = graph.Subscribe(new BitMask(1, 3), _ => { });
            var b = graph.Subscribe(new BitMask(2), _ => { });
            var bd = graph.Subscribe(new BitMask(2, 4), _ => { });
            var matches = new List<ComponentsSetMatchGraph.Node>();

            graph.CollectMatches(new BitMask(1, 2, 3), matches);

            CollectionAssert.Contains(matches, a);
            CollectionAssert.Contains(matches, ab);
            CollectionAssert.Contains(matches, ac);
            CollectionAssert.Contains(matches, b);
            CollectionAssert.DoesNotContain(matches, bd);
            Assert.Less(matches.IndexOf(a), matches.IndexOf(ab));
            Assert.Less(matches.IndexOf(a), matches.IndexOf(ac));
        }

        [Test]
        public void CollectNewMatches_DoesNotReportAlreadySatisfiedParents()
        {
            var graph = new ComponentsSetMatchGraph();
            var a = graph.Subscribe(new BitMask(1), _ => { });
            var ab = graph.Subscribe(new BitMask(1, 2), _ => { });
            var ac = graph.Subscribe(new BitMask(1, 3), _ => { });
            var abc = graph.Subscribe(new BitMask(1, 2, 3), _ => { });
            var matches = new List<ComponentsSetMatchGraph.Node>();

            graph.CollectNewMatches(
                new BitMask(1),
                new BitMask(1, 2, 3),
                matches);

            CollectionAssert.DoesNotContain(matches, a);
            CollectionAssert.Contains(matches, ab);
            CollectionAssert.Contains(matches, ac);
            CollectionAssert.Contains(matches, abc);
            Assert.Less(matches.IndexOf(ab), matches.IndexOf(abc));
        }

        [Test]
        public void CollectMatches_HandlesMasksAcrossSeveralChunks()
        {
            var graph = new ComponentsSetMatchGraph();
            var wide = graph.Subscribe(new BitMask(2, 40, 73), _ => { });
            var matches = new List<ComponentsSetMatchGraph.Node>();

            graph.CollectMatches(new BitMask(2, 17, 40, 73, 95), matches);

            Assert.AreEqual(1, matches.Count);
            Assert.AreSame(wide, matches[0]);
        }
    }
}
#endif
