#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;

namespace CodexECS.Tests
{
    public sealed class EcsWorldCreationAndReactiveTests
    {
        private struct Anchor : IComponent { }
        private struct MatchA : IComponent { }
        private struct MatchB : IComponent { }
        private struct MatchC : IComponent { }
        private struct ReactiveTag : IComponent { }

        private struct InitializedComponent : IComponent
        {
            public int Value;
            public int InitCalls;

            private static InitializedComponent Default => new() { Value = 40 };

            private static void Init(ref InitializedComponent instance)
            {
                instance.Value++;
                instance.InitCalls++;
            }

            private static void Cleanup(ref InitializedComponent instance)
            {
                instance.Value = -1;
            }
        }

        private struct RemovedValue : IComponent
        {
            public int Value;
        }

        [Test]
        public void CreateWithComponents_UsesOnlyDestinationArchetypeAndMetadataDefaults()
        {
            var world = new EcsWorld();
            var destination = new BitMask(
                ComponentMeta<InitializedComponent>.Id,
                ComponentMeta<Anchor>.Id);

            var eid = world.CreateWithComponents(destination);

            Assert.IsTrue(world.Have<InitializedComponent>(eid));
            Assert.IsTrue(world.Have<Anchor>(eid));
            Assert.AreEqual(41, world.Get<InitializedComponent>(eid).Value);
            Assert.AreEqual(1, world.Get<InitializedComponent>(eid).InitCalls);
            Assert.AreEqual(2, GetArchetypeCount(world),
                "Only the empty and final archetypes should exist after bulk creation.");

            destination.Unset(ComponentMeta<InitializedComponent>.Id);
            destination.Set(ComponentMeta<MatchC>.Id);
            Assert.IsTrue(world.Have<InitializedComponent>(eid),
                "The world must not retain the caller's mutable mask storage.");
            Assert.IsFalse(world.Have<MatchC>(eid));

            world.Delete(eid);
            var reused = world.CreateWithComponents(new BitMask(ComponentMeta<InitializedComponent>.Id));
            Assert.AreEqual(41, world.Get<InitializedComponent>(reused).Value);
            Assert.AreEqual(1, world.Get<InitializedComponent>(reused).InitCalls,
                "A recycled pool slot must run Init exactly once for its new owner.");
        }

        [Test]
        public void AddAndRemoveSubscriptions_ExposeOnlyTheirEventGeneration()
        {
            var world = new EcsWorld();
            var addFilter = world.Filter()
                .With<RemovedValue>()
                .With<AddReact<RemovedValue>>()
                .Build();
            var removeFilter = world.Filter()
                .With<RemoveReact<RemovedValue>>()
                .Build();
            var added = new List<int>();
            var removed = new List<(int EId, int Value)>();

            world.SubscribeOnAdd<RemovedValue>(reactiveWorld =>
            {
                foreach (var reactiveEid in addFilter)
                    added.Add(reactiveEid);
            });
            world.SubscribeOnRemove<RemovedValue>(reactiveWorld =>
            {
                foreach (var reactiveEid in removeFilter)
                {
                    removed.Add((
                        reactiveEid,
                        reactiveWorld.Get<RemoveReact<RemovedValue>>(reactiveEid).removedComponent.Value));
                }
            });

            var eid = world.CreateWithComponents(new BitMask(ComponentMeta<Anchor>.Id));
            world.Add(eid, new RemovedValue { Value = 73 });
            world.FlushReactives();

            CollectionAssert.AreEqual(new[] { eid }, added);
            Assert.IsFalse(world.Have<AddReact<RemovedValue>>(eid));

            world.Remove<RemovedValue>(eid);
            world.FlushReactives();

            Assert.AreEqual(1, removed.Count);
            Assert.AreEqual((eid, 73), removed[0]);
            Assert.IsFalse(world.Have<RemoveReact<RemovedValue>>(eid));
            Assert.IsTrue(world.Have<Anchor>(eid));
        }

        [Test]
        public void RemoveSubscription_WorksForTagsAndDeletesEntityAfterLastReaction()
        {
            var world = new EcsWorld();
            var removeFilter = world.Filter()
                .With<RemoveReact<ReactiveTag>>()
                .Build();
            var seen = new List<int>();
            world.SubscribeOnRemove<ReactiveTag>(_ =>
            {
                foreach (var reactiveEid in removeFilter)
                    seen.Add(reactiveEid);
            });

            var eid = world.CreateWithComponents(new BitMask(ComponentMeta<ReactiveTag>.Id));
            world.Remove<ReactiveTag>(eid);

            Assert.IsFalse(world.IsDead(eid), "The remove wrapper keeps the entity alive for its callback.");
            world.FlushReactives();

            CollectionAssert.AreEqual(new[] { eid }, seen);
            Assert.IsTrue(world.IsDead(eid));
        }

        [Test]
        public void RemoveAll_QueuesEverySubscribedRemovalBeforeCleaningStorage()
        {
            var world = new EcsWorld();
            var removeFilter = world.Filter()
                .With<RemoveReact<RemovedValue>>()
                .Build();
            var removedValues = new List<int>();
            world.SubscribeOnRemove<RemovedValue>(reactiveWorld =>
            {
                foreach (var reactiveEid in removeFilter)
                {
                    removedValues.Add(
                        reactiveWorld.Get<RemoveReact<RemovedValue>>(reactiveEid).removedComponent.Value);
                }
            });

            var retained = world.CreateWithComponents(new BitMask(
                ComponentMeta<Anchor>.Id,
                ComponentMeta<RemovedValue>.Id));
            world.Replace(retained, new RemovedValue { Value = 11 });
            var deletedAfterReaction = world.CreateWithComponents(
                new BitMask(ComponentMeta<RemovedValue>.Id));
            world.Replace(deletedAfterReaction, new RemovedValue { Value = 22 });

            world.RemoveAll<RemovedValue>();
            world.FlushReactives();

            CollectionAssert.AreEquivalent(new[] { 11, 22 }, removedValues);
            Assert.IsTrue(world.Have<Anchor>(retained));
            Assert.IsFalse(world.Have<RemovedValue>(retained));
            Assert.IsTrue(world.IsDead(deletedAfterReaction));
        }

        [Test]
        public void ComponentsSetSubscriptions_VisitEveryNewlySatisfiedMaskWithoutCrossTalk()
        {
            var world = new EcsWorld();
            var aMask = new BitMask(ComponentMeta<MatchA>.Id);
            var abMask = new BitMask(ComponentMeta<MatchA>.Id, ComponentMeta<MatchB>.Id);
            var aFilter = world.RegisterFilter(aMask.And(ComponentMeta<MatchReact>.Id));
            var abFilter = world.RegisterFilter(abMask.And(ComponentMeta<MatchReact>.Id));
            var aBatches = new List<List<int>>();
            var abBatches = new List<List<int>>();

            world.SubscribeOnComponentsSetMatch(aMask, _ => aBatches.Add(Snapshot(aFilter)));
            world.SubscribeOnComponentsSetMatch(abMask, _ => abBatches.Add(Snapshot(abFilter)));

            var onlyA = world.CreateWithComponents(aMask);
            var both = world.CreateWithComponents(abMask);
            world.FlushReactives();

            Assert.AreEqual(1, aBatches.Count);
            CollectionAssert.AreEquivalent(new[] { onlyA, both }, aBatches[0]);
            Assert.AreEqual(1, abBatches.Count);
            CollectionAssert.AreEqual(new[] { both }, abBatches[0]);
            Assert.IsFalse(world.Have<MatchReact>(onlyA));
            Assert.IsFalse(world.Have<MatchReact>(both));

            world.Add<MatchC>(both);
            world.FlushReactives();
            Assert.AreEqual(1, aBatches.Count, "An unrelated add must not retrigger an existing match.");
            Assert.AreEqual(1, abBatches.Count, "An unrelated add must not retrigger an existing match.");

            world.Add<MatchB>(onlyA);
            world.FlushReactives();
            Assert.AreEqual(1, aBatches.Count);
            Assert.AreEqual(2, abBatches.Count);
            CollectionAssert.AreEqual(new[] { onlyA }, abBatches[1]);
        }

        [Test]
        public void AddSubscription_PreservesEventsQueuedReentrantly()
        {
            var world = new EcsWorld();
            var filter = world.Filter()
                .With<MatchA>()
                .With<AddReact<MatchA>>()
                .Build();
            var seen = new List<int>();
            var spawned = -1;

            world.SubscribeOnAdd<MatchA>(reactiveWorld =>
            {
                foreach (var reactiveEid in filter)
                    seen.Add(reactiveEid);

                if (spawned >= 0)
                    return;
                spawned = reactiveWorld.Create();
                reactiveWorld.Add<MatchA>(spawned);
            });

            var first = world.Create();
            world.Add<MatchA>(first);
            world.FlushReactives();

            CollectionAssert.AreEquivalent(new[] { first, spawned }, seen);
            Assert.AreEqual(2, seen.Count);
            Assert.IsFalse(world.Have<AddReact<MatchA>>(first));
            Assert.IsFalse(world.Have<AddReact<MatchA>>(spawned));
        }

        [Test]
        public void FilterUnlock_CommitsMembershipBeforeFlushingReactiveCallbacks()
        {
            var world = new EcsWorld();
            var source = world.Filter().With<Anchor>().Build();
            var added = world.Filter()
                .With<MatchA>()
                .With<AddReact<MatchA>>()
                .Build();
            var sourceCountObservedByCallback = -1;

            world.SubscribeOnAdd<MatchA>(_ =>
            {
                // Enumerating the marker filter also verifies that the callback is handling
                // the queued entity rather than merely being invoked with an empty batch.
                var eventCount = 0;
                foreach (var ignored in added)
                    eventCount++;
                Assert.AreEqual(1, eventCount);
                sourceCountObservedByCallback = source.EntitiesCount;
            });

            var eid = world.CreateWithComponents(new BitMask(ComponentMeta<Anchor>.Id));
            foreach (var sourceEid in source)
            {
                Assert.AreEqual(eid, sourceEid);
                world.Add<MatchA>(sourceEid);
                world.Remove<Anchor>(sourceEid);
            }

            Assert.AreEqual(0, sourceCountObservedByCallback,
                "Reactive code must not observe the pre-unlock filter snapshot.");
        }

        private static List<int> Snapshot(EcsFilter filter)
        {
            var result = new List<int>();
            foreach (var eid in filter)
                result.Add(eid);
            return result;
        }

        private static int GetArchetypeCount(EcsWorld world)
        {
            var managerField = typeof(EcsWorld).GetField(
                "_archetypes",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var manager = managerField.GetValue(world);
            var mapField = manager.GetType().GetField(
                "_mToA",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var map = (System.Collections.IDictionary)mapField.GetValue(manager);
            return map.Count;
        }
    }
}
#endif
