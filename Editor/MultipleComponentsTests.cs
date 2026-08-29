#if UNITY_INCLUDE_TESTS
using NUnit.Framework;

namespace CodexECS.Tests
{
    public sealed class MultipleComponentsTests
    {
        private struct Anchor : IComponent { }

        private struct ValueComponent : IComponent
        {
            public int Value;
        }

        private struct RegistrationProbe : IComponent
        {
            public int Value;
        }

        private struct InitializedComponent : IComponent
        {
            public int Value;

            private static InitializedComponent Default => new() { Value = 40 };
            private static void Init(ref InitializedComponent instance) => instance.Value++;
        }

        private sealed class CleanupToken
        {
            public int Calls;
        }

        private sealed class CopyBox
        {
            public int Value;
        }

        private struct OwnedComponent : IComponent
        {
            public CopyBox Box;

            private static void Copy(in OwnedComponent source, ref OwnedComponent destination) =>
                destination.Box = source.Box == null ? null : new CopyBox { Value = source.Box.Value };
        }

        [Test]
        public void SimpleListSwapRemove_ReleasesTheVacatedReferenceSlot()
        {
            var first = new CleanupToken();
            var second = new CleanupToken();
            var values = new SimpleList<CleanupToken>();
            values.Add(first);
            values.Add(second);

            values.SwapRemoveAt(0);

            Assert.AreSame(second, values[0]);
            Assert.IsNull(values._elements[1]);
        }

        private struct CleanupComponent : IComponent
        {
            public CleanupToken Token;

            private static void Cleanup(ref CleanupComponent instance)
            {
                if (instance.Token != null)
                    instance.Token.Calls++;
                instance.Token = null;
            }
        }

        [Test]
        public void GetMultiple_IncludesCanonicalFirstAndMutatesEveryEntryByRef()
        {
            var world = new EcsWorld();
            var eid = world.Create();
            world.Add<Anchor>(eid);

            world.AddMultiple(eid, new ValueComponent { Value = 1 });
            Assert.IsFalse(world.HaveMultiple<ValueComponent>(eid));
            Assert.AreEqual(1, world.Get<ValueComponent>(eid).Value);

            world.AddMultiple(eid, new ValueComponent { Value = 2 });
            world.AddMultiple(eid, new ValueComponent { Value = 3 });
            Assert.IsTrue(world.HaveMultiple<ValueComponent>(eid));

            var components = world.GetMultiple<ValueComponent>(eid);
            Assert.AreEqual(3, components.Count);
            Assert.AreEqual(1, components[0].Value);
            Assert.AreEqual(2, components[1].Value);
            Assert.AreEqual(3, components[2].Value);

            foreach (ref var component in components)
                component.Value += 10;

            Assert.AreEqual(11, world.Get<ValueComponent>(eid).Value);
            Assert.AreEqual(11, components[0].Value);
            Assert.AreEqual(12, components[1].Value);
            Assert.AreEqual(13, components[2].Value);
        }

        [Test]
        public void RemoveMultiple_UsesPublicIndicesAndCollapsesBackToCanonicalStorage()
        {
            var world = new EcsWorld();
            var eid = world.Create();
            world.Add<Anchor>(eid);
            world.AddMultiple(eid, new ValueComponent { Value = 1 });
            world.AddMultiple(eid, new ValueComponent { Value = 2 });
            world.AddMultiple(eid, new ValueComponent { Value = 3 });

            world.RemoveMultiple<ValueComponent>(eid, 1);
            var components = world.GetMultiple<ValueComponent>(eid);
            Assert.AreEqual(2, components.Count);
            Assert.AreEqual(1, world.Get<ValueComponent>(eid).Value);
            Assert.AreEqual(3, components[1].Value);

            world.RemoveMultiple<ValueComponent>(eid, 0);
            Assert.IsFalse(world.HaveMultiple<ValueComponent>(eid));
            Assert.AreEqual(1, world.GetMultiple<ValueComponent>(eid).Count);
            Assert.AreEqual(3, world.Get<ValueComponent>(eid).Value);

            world.RemoveMultiple<ValueComponent>(eid);
            Assert.IsFalse(world.Have<ValueComponent>(eid));
            Assert.IsTrue(world.Have<Anchor>(eid));
        }

        [Test]
        public void AddMultiple_WithoutValue_AppliesComponentDefaultAndInitToEveryEntry()
        {
            var world = new EcsWorld();
            var eid = world.Create();
            world.Add<Anchor>(eid);

            ref var first = ref world.AddMultiple<InitializedComponent>(eid);
            ref var second = ref world.AddMultiple<InitializedComponent>(eid);

            Assert.AreEqual(41, first.Value);
            Assert.AreEqual(41, second.Value);
            Assert.AreEqual(41, world.GetMultiple<InitializedComponent>(eid)[0].Value);
            Assert.AreEqual(41, world.GetMultiple<InitializedComponent>(eid)[1].Value);
        }

        [Test]
        public void HaveMultiple_DoesNotRegisterInternalStorageForCanonicalOnlyEntities()
        {
            var world = new EcsWorld();
            var eid = world.Create();
            world.Add(eid, new RegistrationProbe { Value = 1 });
            var storageType = typeof(MultipleComponents<RegistrationProbe>);

            Assert.IsFalse(ComponentMapping.HaveType(storageType));
            Assert.IsFalse(world.HaveMultiple<RegistrationProbe>(eid));
            Assert.IsFalse(ComponentMapping.HaveType(storageType));
        }

        [Test]
        public void AdditionalStorage_IsIndependentBetweenEntities()
        {
            var world = new EcsWorld();
            var firstEntity = world.Create();
            var secondEntity = world.Create();
            world.Add<Anchor>(firstEntity);
            world.Add<Anchor>(secondEntity);

            world.AddMultiple(firstEntity, new ValueComponent { Value = 1 });
            world.AddMultiple(firstEntity, new ValueComponent { Value = 2 });
            world.AddMultiple(secondEntity, new ValueComponent { Value = 10 });
            world.AddMultiple(secondEntity, new ValueComponent { Value = 20 });

            Assert.AreEqual(2, world.GetMultiple<ValueComponent>(firstEntity).Count);
            Assert.AreEqual(2, world.GetMultiple<ValueComponent>(secondEntity).Count);
            Assert.AreEqual(2, world.GetMultiple<ValueComponent>(firstEntity)[1].Value);
            Assert.AreEqual(20, world.GetMultiple<ValueComponent>(secondEntity)[1].Value);

            world.RemoveAllMultiple<ValueComponent>(firstEntity);

            Assert.IsFalse(world.Have<ValueComponent>(firstEntity));
            Assert.AreEqual(2, world.GetMultiple<ValueComponent>(secondEntity).Count);
            Assert.AreEqual(20, world.GetMultiple<ValueComponent>(secondEntity)[1].Value);
        }

        [Test]
        public void CopyComponents_ClonesAdditionalStorageInsteadOfSharingItsContainer()
        {
            var world = new EcsWorld();
            var source = world.Create();
            var destination = world.Create();
            world.Add<Anchor>(source);
            world.Add<Anchor>(destination);
            world.AddMultiple(source, new ValueComponent { Value = 1 });
            world.AddMultiple(source, new ValueComponent { Value = 2 });

            world.CopyComponents(world.GetMask(source), source, destination);
            world.GetMultiple<ValueComponent>(destination)[1].Value = 20;

            Assert.AreEqual(2, world.GetMultiple<ValueComponent>(source)[1].Value);
            Assert.AreEqual(20, world.GetMultiple<ValueComponent>(destination)[1].Value);
            world.RemoveAllMultiple<ValueComponent>(destination);
            Assert.AreEqual(2, world.GetMultiple<ValueComponent>(source).Count);
            Assert.AreEqual(2, world.GetMultiple<ValueComponent>(source)[1].Value);
        }

        [Test]
        public void CopyComponents_AppliesCustomCopyToCanonicalAndAdditionalEntries()
        {
            var world = new EcsWorld();
            var source = world.Create();
            var destination = world.Create();
            world.Add<Anchor>(source);
            world.Add<Anchor>(destination);
            world.AddMultiple(source, new OwnedComponent { Box = new CopyBox { Value = 1 } });
            world.AddMultiple(source, new OwnedComponent { Box = new CopyBox { Value = 2 } });

            world.CopyComponents(world.GetMask(source), source, destination);
            world.GetMultiple<OwnedComponent>(destination)[0].Box.Value = 10;
            world.GetMultiple<OwnedComponent>(destination)[1].Box.Value = 20;

            Assert.AreEqual(1, world.GetMultiple<OwnedComponent>(source)[0].Box.Value);
            Assert.AreEqual(2, world.GetMultiple<OwnedComponent>(source)[1].Box.Value);
            Assert.AreEqual(10, world.GetMultiple<OwnedComponent>(destination)[0].Box.Value);
            Assert.AreEqual(20, world.GetMultiple<OwnedComponent>(destination)[1].Box.Value);
            Assert.AreNotSame(
                world.GetMultiple<OwnedComponent>(source)[1].Box,
                world.GetMultiple<OwnedComponent>(destination)[1].Box);
        }

        [Test]
        public void RemoveAllMultiple_CleansEachOwnedComponentExactlyOnce()
        {
            var world = new EcsWorld();
            var eid = world.Create();
            world.Add<Anchor>(eid);
            var first = new CleanupToken();
            var second = new CleanupToken();
            var third = new CleanupToken();

            world.AddMultiple(eid, new CleanupComponent { Token = first });
            world.AddMultiple(eid, new CleanupComponent { Token = second });
            world.AddMultiple(eid, new CleanupComponent { Token = third });
            world.RemoveMultiple<CleanupComponent>(eid, 1);

            Assert.AreEqual(0, first.Calls);
            Assert.AreEqual(1, second.Calls);
            Assert.AreEqual(0, third.Calls);

            world.RemoveAllMultiple<CleanupComponent>(eid);
            Assert.AreEqual(1, first.Calls);
            Assert.AreEqual(1, second.Calls);
            Assert.AreEqual(1, third.Calls);
            Assert.IsFalse(world.Have<CleanupComponent>(eid));
        }

        [Test]
        public void RemoveAllByComponentId_AlsoClearsInternalAdditionalStorage()
        {
            var world = new EcsWorld();
            var eid = world.Create();
            world.Add<Anchor>(eid);
            var first = new CleanupToken();
            var second = new CleanupToken();
            world.AddMultiple(eid, new CleanupComponent { Token = first });
            world.AddMultiple(eid, new CleanupComponent { Token = second });

            world.RemoveAll(ComponentMeta<CleanupComponent>.Id);

            Assert.IsFalse(world.Have<CleanupComponent>(eid));
            Assert.IsFalse(world.HaveMultipleStorage<CleanupComponent>(eid));
            Assert.IsTrue(world.Have<Anchor>(eid));
            Assert.AreEqual(1, first.Calls);
            Assert.AreEqual(1, second.Calls);
        }
    }
}
#endif
