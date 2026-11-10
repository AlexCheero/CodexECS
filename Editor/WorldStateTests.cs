#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace CodexECS.Tests
{
    public sealed class WorldStateTests
    {
        private struct Position : IComponent
        {
            public int X;
            public int Y;
        }

        private struct Velocity : IComponent
        {
            public int DX;
        }

        private struct Marker : IComponent { }

        private struct Target : IComponent
        {
            public Entity Entity;
        }

        /// <summary>Presentation-style component that is never captured and counts its Cleanup calls.</summary>
        private struct ViewHandle : IComponent
        {
            public int Handle;
            public static int CleanupCalls;

            private static void Cleanup(ref ViewHandle instance)
            {
                CleanupCalls++;
                instance.Handle = -1;
            }
        }

        private struct ManagedComponent : IComponent
        {
            public int[] Data;
        }

        private static readonly int[] SimulationIds =
        {
            ComponentMeta<Position>.Id, ComponentMeta<Velocity>.Id, ComponentMeta<Marker>.Id, ComponentMeta<Target>.Id,
        };

        private static List<int> Populate(EcsWorld world, int count)
        {
            var ids = new List<int>(count);
            for (int i = 0; i < count; i++)
            {
                int eid = world.Create();
                world.Add(eid, new Position { X = i, Y = -i });
                if (i % 3 == 0)
                    world.Add(eid, new Velocity { DX = i * 2 });
                if (i % 5 == 0)
                    world.Add<Marker>(eid);
                ids.Add(eid);
            }
            for (int i = 1; i < count; i += 7)
                world.Add(ids[i], new Target { Entity = world.GetById(ids[i - 1]) });
            return ids;
        }

        private sealed class Expectation
        {
            public readonly Dictionary<int, long> Handles = new Dictionary<int, long>();
            public readonly Dictionary<int, Position> Positions = new Dictionary<int, Position>();
            public readonly HashSet<int> WithVelocity = new HashSet<int>();
            public readonly Dictionary<int, int> Velocities = new Dictionary<int, int>();
            public readonly HashSet<int> WithMarker = new HashSet<int>();
            public readonly Dictionary<int, long> Targets = new Dictionary<int, long>();
            public int EntitiesCount;

            public static Expectation Of(EcsWorld world, List<int> ids)
            {
                var e = new Expectation { EntitiesCount = world.EntitiesCount };
                foreach (int id in ids)
                {
                    if (!world.IsIdValid(id))
                        continue;
                    e.Handles[id] = world.GetById(id).Val;
                    e.Positions[id] = world.Get<Position>(id);
                    if (world.Have<Velocity>(id))
                    {
                        e.WithVelocity.Add(id);
                        e.Velocities[id] = world.Get<Velocity>(id).DX;
                    }
                    if (world.Have<Marker>(id))
                        e.WithMarker.Add(id);
                    if (world.Have<Target>(id))
                        e.Targets[id] = world.Get<Target>(id).Entity.Val;
                }
                return e;
            }

            public void AssertMatches(EcsWorld world, List<int> ids)
            {
                Assert.AreEqual(EntitiesCount, world.EntitiesCount, "entity count");
                foreach (int id in ids)
                {
                    bool expectedAlive = Handles.ContainsKey(id);
                    Assert.AreEqual(expectedAlive, world.IsIdValid(id), "alive " + id);
                    if (!expectedAlive)
                        continue;
                    Assert.AreEqual(Handles[id], world.GetById(id).Val, "version " + id);
                    Position p = world.Get<Position>(id);
                    Assert.AreEqual(Positions[id].X, p.X, "position x " + id);
                    Assert.AreEqual(Positions[id].Y, p.Y, "position y " + id);
                    Assert.AreEqual(WithVelocity.Contains(id), world.Have<Velocity>(id), "velocity membership " + id);
                    if (WithVelocity.Contains(id))
                        Assert.AreEqual(Velocities[id], world.Get<Velocity>(id).DX, "velocity " + id);
                    Assert.AreEqual(WithMarker.Contains(id), world.Have<Marker>(id), "marker " + id);
                    Assert.AreEqual(Targets.ContainsKey(id), world.Have<Target>(id), "target membership " + id);
                    if (Targets.ContainsKey(id))
                    {
                        Entity target = world.Get<Target>(id).Entity;
                        Assert.AreEqual(Targets[id], target.Val, "target handle " + id);
                        Assert.IsTrue(world.IsEntityValid(target), "target still valid " + id);
                    }
                }
            }
        }

        private static void Mutate(EcsWorld world, List<int> ids, List<int> created)
        {
            for (int i = 0; i < ids.Count; i += 2)
                world.Delete(ids[i]);
            for (int i = 1; i < ids.Count; i += 2)
            {
                ref Position p = ref world.Get<Position>(ids[i]);
                p.X += 1000;
                if (world.Have<Velocity>(ids[i]))
                    world.Remove<Velocity>(ids[i]);
                else
                    world.Add(ids[i], new Velocity { DX = -1 });
            }
            for (int i = 0; i < 100; i++)
            {
                int eid = world.Create();
                world.Add(eid, new Position { X = 777, Y = i });
                created.Add(eid);
            }
        }

        [Test]
        public void CaptureAndRestore_RoundTripsEntitiesAcrossPages()
        {
            var world = new EcsWorld();
            List<int> ids = Populate(world, 300);
            Expectation expected = Expectation.Of(world, ids);
            var state = new WorldState();
            world.CaptureState(state, SimulationIds);
            Assert.IsTrue(state.IsCaptured);
            Assert.AreEqual(300, state.EntitiesCount);
            Assert.IsTrue(state.PageCount >= 5, "300 entities span at least five 64-slot pages");

            var created = new List<int>();
            Mutate(world, ids, created);
            Assert.AreNotEqual(expected.EntitiesCount, world.EntitiesCount);

            world.RestoreState(state);
            expected.AssertMatches(world, ids);
            foreach (int eid in created)
            {
                // A slot reused by a new entity holds the original again; a never-used slot is dead.
                Assert.IsTrue(!world.IsIdValid(eid) || expected.Handles.ContainsKey(eid), "post-capture entity " + eid);
            }

            // The restored world keeps working: allocate, mutate, capture again, restore again.
            List<int> more = Populate(world, 50);
            Assert.AreEqual(350, world.EntitiesCount);
            Expectation expectedAfterGrowth = Expectation.Of(world, ids);
            var second = new WorldState();
            world.CaptureState(second, SimulationIds);
            Mutate(world, more, created);
            world.RestoreState(second);
            Assert.AreEqual(350, world.EntitiesCount);
            expectedAfterGrowth.AssertMatches(world, ids);
        }

        [Test]
        public void RestoreIntoFreshWorld_ReproducesHandlesAndValues()
        {
            var source = new EcsWorld();
            List<int> ids = Populate(source, 130);
            Expectation expected = Expectation.Of(source, ids);
            var state = new WorldState();
            source.CaptureState(state, SimulationIds);

            var target = new EcsWorld();
            target.RestoreState(state);
            expected.AssertMatches(target, ids);

            var recaptured = new WorldState();
            target.CaptureState(recaptured, SimulationIds);
            AssertSameState(state, recaptured);

            int fresh = target.Create();
            Assert.IsTrue(target.IsIdValid(fresh));
            Assert.AreEqual(131, target.EntitiesCount);
        }

        [Test]
        public void PresentationComponentsFollowSlotIdentity()
        {
            ViewHandle.CleanupCalls = 0;
            var world = new EcsWorld();
            int survivor = world.Create();
            world.Add(survivor, new Position { X = 1 });
            world.Add(survivor, new ViewHandle { Handle = 10 });
            int doomed = world.Create();
            world.Add(doomed, new Position { X = 2 });

            var state = new WorldState();
            world.CaptureState(state, SimulationIds);

            world.Delete(doomed);
            int reused = world.Create(); // takes the freed slot with a new version
            Assert.AreEqual(doomed, reused, "freed slot is reused first");
            world.Add(reused, new Position { X = 3 });
            world.Add(reused, new ViewHandle { Handle = 20 });
            int newcomer = world.Create();
            world.Add(newcomer, new ViewHandle { Handle = 30 });
            world.Add(survivor, new Velocity { DX = 5 });

            world.RestoreState(state);

            Assert.IsTrue(world.Have<ViewHandle>(survivor), "presentation state survives on a slot with unchanged identity");
            Assert.AreEqual(10, world.Get<ViewHandle>(survivor).Handle);
            Assert.IsFalse(world.Have<Velocity>(survivor), "captured storages are restored exactly");
            Assert.IsTrue(world.IsIdValid(doomed), "the captured entity is back");
            Assert.AreEqual(2, world.Get<Position>(doomed).X);
            Assert.IsFalse(world.Have<ViewHandle>(doomed), "the reused slot's view belonged to another entity");
            Assert.IsFalse(world.IsIdValid(newcomer));
            Assert.IsFalse(world.Have<ViewHandle>(newcomer));
            Assert.AreEqual(2, ViewHandle.CleanupCalls, "both pruned views were cleaned up");
            Assert.AreEqual(2, world.EntitiesCount);
        }

        [Test]
        public void GrowthAfterCaptureIsUndone()
        {
            var world = new EcsWorld();
            List<int> ids = Populate(world, 10);
            Expectation expected = Expectation.Of(world, ids);
            var state = new WorldState();
            world.CaptureState(state, SimulationIds);
            int pagesBefore = world.StoragePageCount;

            var created = new List<int>();
            for (int i = 0; i < 3000; i++)
            {
                int eid = world.Create();
                world.Add(eid, new Position { X = i });
                created.Add(eid);
            }
            Assert.IsTrue(world.StoragePageCount > pagesBefore);

            world.RestoreState(state);
            Assert.AreEqual(pagesBefore, world.StoragePageCount);
            expected.AssertMatches(world, ids);
            foreach (int eid in created)
                Assert.IsTrue(!world.IsIdValid(eid) || expected.Handles.ContainsKey(eid));

            List<int> more = Populate(world, 200);
            foreach (int eid in more)
                Assert.IsTrue(world.IsIdValid(eid));
            Assert.AreEqual(210, world.EntitiesCount);
        }

        [Test]
        public void StateViewsDescribeTheCapture()
        {
            var world = new EcsWorld();
            List<int> ids = Populate(world, 70);
            var state = new WorldState();
            world.CaptureState(state, SimulationIds);

            Assert.AreEqual(4, state.ComponentIds.Length);
            Assert.AreEqual(0, state.IndexOf(ComponentMeta<Position>.Id));
            Assert.AreEqual(-1, state.IndexOf(ComponentMeta<ViewHandle>.Id));
            Assert.AreEqual(70, state.ComponentCountOf(ComponentMeta<Position>.Id));
            Assert.AreEqual(14, state.ComponentCountOf(ComponentMeta<Marker>.Id));

            int alive = 0;
            foreach (ulong word in state.AliveWords)
                alive += PopCount(word);
            Assert.AreEqual(70, alive);

            ReadOnlySpan<ulong> words = state.MembershipWords(ComponentMeta<Position>.Id);
            int seen = 0;
            for (int page = 0; page < state.PageCount; page++)
            {
                ulong bits = words[page];
                ReadOnlySpan<Position> payload = state.Payload<Position>(ComponentMeta<Position>.Id, page);
                for (int bit = 0; bit < 64; bit++)
                {
                    if ((bits & (1UL << bit)) == 0)
                        continue;
                    int id = (page << 6) | bit;
                    Assert.AreEqual(world.Get<Position>(id).X, payload[bit].X);
                    Assert.AreEqual(world.GetById(id).Val, ((long)state.Versions[id] << 32) | (uint)id);
                    seen++;
                }
            }
            Assert.AreEqual(70, seen);
            Assert.Throws<EcsException>(() => state.Payload<Velocity>(ComponentMeta<Position>.Id, 0));
            Assert.Throws<EcsException>(() => state.Payload<Marker>(ComponentMeta<Marker>.Id, 0));
            Assert.Throws<EcsException>(() => state.MembershipWords(ComponentMeta<ViewHandle>.Id));
        }

        [Test]
        public void ReusedStateAllocatesNothingOnceWarm()
        {
            var world = new EcsWorld();
            Populate(world, 200);
            var state = new WorldState();
            world.CaptureState(state, SimulationIds);
            world.RestoreState(state);
            world.CaptureState(state, SimulationIds);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 50; i++)
            {
                world.CaptureState(state, SimulationIds);
                world.RestoreState(state);
            }
            long after = GC.GetAllocatedBytesForCurrentThread();
            Assert.AreEqual(0L, after - before, "steady-state capture and restore must not allocate");
        }

        [Test]
        public void SerializedStateRestoresIntoAnotherWorld()
        {
            var source = new EcsWorld();
            List<int> ids = Populate(source, 150);
            Expectation expected = Expectation.Of(source, ids);
            var state = new WorldState();
            source.CaptureState(state, SimulationIds);
            var bytes = new byte[state.SerializedSize];
            int written = state.Serialize(bytes);
            Assert.AreEqual(bytes.Length, written, "SerializedSize matches the bytes written");

            var target = new EcsWorld();
            var received = new WorldState();
            Assert.IsTrue(received.Deserialize(bytes, target));
            Assert.AreEqual(state.PageCount, received.PageCount);
            Assert.AreEqual(state.EntitiesCount, received.EntitiesCount);
            target.RestoreState(received);
            expected.AssertMatches(target, ids);

            var roundTrip = new WorldState();
            target.CaptureState(roundTrip, SimulationIds);
            AssertSameState(state, roundTrip);

            Assert.IsFalse(new WorldState().Deserialize(new byte[10], target), "truncated input is rejected");
            bytes[0] ^= 0xFF;
            Assert.IsFalse(new WorldState().Deserialize(bytes, target), "bad magic is rejected");
        }

        [Test]
        public void ManagedComponentCannotBeCaptured()
        {
            var world = new EcsWorld();
            int eid = world.Create();
            world.Add(eid, new ManagedComponent { Data = new int[1] });
            var state = new WorldState();
            Assert.Throws<EcsException>(() => world.CaptureState(state, new[] { ComponentMeta<ManagedComponent>.Id }));
        }

        [Test]
        public void LockedWorldRejectsCaptureAndRestore()
        {
            var world = new EcsWorld();
            Populate(world, 3);
            var state = new WorldState();
            world.CaptureState(state, SimulationIds);
            world.Lock();
            Assert.Throws<EcsException>(() => world.CaptureState(state, SimulationIds));
            Assert.Throws<EcsException>(() => world.RestoreState(state));
            world.Unlock();
            Assert.Throws<EcsException>(() => world.RestoreState(new WorldState()));
            world.RestoreState(state);
        }

        private static void AssertSameState(WorldState a, WorldState b)
        {
            Assert.AreEqual(a.PageCount, b.PageCount);
            Assert.AreEqual(a.EntitiesCount, b.EntitiesCount);
            for (int p = 0; p < a.PageCount; p++)
                Assert.AreEqual(a.AliveWords[p], b.AliveWords[p], "alive page " + p);
            for (int s = 0; s < a.SlotCount; s++)
                Assert.AreEqual(a.Versions[s], b.Versions[s], "version slot " + s);
            foreach (int id in a.ComponentIds)
            {
                ReadOnlySpan<ulong> wa = a.MembershipWords(id);
                ReadOnlySpan<ulong> wb = b.MembershipWords(id);
                for (int p = 0; p < a.PageCount; p++)
                    Assert.AreEqual(wa[p], wb[p], "membership " + id + " page " + p);
            }
            for (int p = 0; p < a.PageCount; p++)
            {
                ReadOnlySpan<Position> pa = a.Payload<Position>(ComponentMeta<Position>.Id, p);
                ReadOnlySpan<Position> pb = b.Payload<Position>(ComponentMeta<Position>.Id, p);
                ulong bits = a.MembershipWords(ComponentMeta<Position>.Id)[p];
                for (int bit = 0; bit < 64; bit++)
                {
                    if ((bits & (1UL << bit)) != 0)
                        Assert.IsTrue(pa[bit].X == pb[bit].X && pa[bit].Y == pb[bit].Y, "payload page " + p + " bit " + bit);
                }
            }
        }

        private static int PopCount(ulong value)
        {
            int count = 0;
            while (value != 0)
            {
                value &= value - 1;
                count++;
            }
            return count;
        }
    }
}
#endif
