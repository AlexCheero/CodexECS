#if UNITY_INCLUDE_TESTS
using System;
using NUnit.Framework;

namespace CodexECS.Tests
{
    public sealed class ArchetypesManagerRemoveAllTests
    {
        private struct Retained : IComponent { public int Value; }
        private struct Removed : IComponent { public int Value; }
        private struct Auxiliary : IComponent { public int Value; }
        private struct Missing : IComponent { public int Value; }

        private struct Padding00 : IComponent { }
        private struct Padding01 : IComponent { }
        private struct Padding02 : IComponent { }
        private struct Padding03 : IComponent { }
        private struct Padding04 : IComponent { }
        private struct Padding05 : IComponent { }
        private struct Padding06 : IComponent { }
        private struct Padding07 : IComponent { }
        private struct Padding08 : IComponent { }
        private struct Padding09 : IComponent { }
        private struct Padding10 : IComponent { }
        private struct Padding11 : IComponent { }
        private struct Padding12 : IComponent { }
        private struct Padding13 : IComponent { }
        private struct Padding14 : IComponent { }
        private struct Padding15 : IComponent { }
        private struct Padding16 : IComponent { }
        private struct Padding17 : IComponent { }
        private struct Padding18 : IComponent { }
        private struct Padding19 : IComponent { }
        private struct Padding20 : IComponent { }
        private struct Padding21 : IComponent { }
        private struct Padding22 : IComponent { }
        private struct Padding23 : IComponent { }
        private struct Padding24 : IComponent { }
        private struct Padding25 : IComponent { }
        private struct Padding26 : IComponent { }
        private struct Padding27 : IComponent { }
        private struct Padding28 : IComponent { }
        private struct Padding29 : IComponent { }
        private struct Padding30 : IComponent { }
        private struct Padding31 : IComponent { }
        private struct Padding32 : IComponent { }
        private struct Padding33 : IComponent { }
        private struct Padding34 : IComponent { }
        private struct Padding35 : IComponent { }
        private struct Padding36 : IComponent { }
        private struct Padding37 : IComponent { }
        private struct Padding38 : IComponent { }
        private struct Padding39 : IComponent { }
        private struct Padding40 : IComponent { }
        private struct Padding41 : IComponent { }
        private struct Padding42 : IComponent { }
        private struct Padding43 : IComponent { }
        private struct Padding44 : IComponent { }
        private struct Padding45 : IComponent { }
        private struct Padding46 : IComponent { }
        private struct Padding47 : IComponent { }
        private struct Padding48 : IComponent { }
        private struct Padding49 : IComponent { }
        private struct Padding50 : IComponent { }
        private struct Padding51 : IComponent { }
        private struct Padding52 : IComponent { }
        private struct Padding53 : IComponent { }
        private struct Padding54 : IComponent { }
        private struct Padding55 : IComponent { }
        private struct Padding56 : IComponent { }
        private struct Padding57 : IComponent { }
        private struct Padding58 : IComponent { }
        private struct Padding59 : IComponent { }
        private struct Padding60 : IComponent { }
        private struct Padding61 : IComponent { }
        private struct Padding62 : IComponent { }
        private struct Padding63 : IComponent { }
        private struct Padding64 : IComponent { }
        private struct HighIdComponent : IComponent { public int Value; }

        [Test]
        public void RemoveAll_ExistingTarget_ReusesScratchWithoutAllocating()
        {
            var world = new EcsWorld();
            var withRemoved = world.Filter().With<Retained>().With<Removed>().Build();
            var withoutRemoved = world.Filter().With<Retained>().Without<Removed>().Build();

            var target = world.Create();
            world.Add(target, new Retained { Value = 10 });
            var first = CreateSource(world, 20, 200);
            var second = CreateSource(world, 30, 300);

            world.RemoveAll<Removed>();
            Assert.AreEqual(0, withRemoved.EntitiesCount);
            Assert.AreEqual(3, withoutRemoved.EntitiesCount);

            // Recreate the already-known source archetype and warm all entity/filter storage.
            world.Add(first, new Removed { Value = 201 });
            world.Add(second, new Removed { Value = 301 });
            world.RemoveAll<Removed>();
            world.Add(first, new Removed { Value = 202 });
            world.Add(second, new Removed { Value = 302 });

            var collectionsBefore = TotalCollections();
            var bytesBefore = GC.GetAllocatedBytesForCurrentThread();
            world.RemoveAll<Removed>();
            var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - bytesBefore;
            var collectionsAfter = TotalCollections();

            Assert.AreEqual(0L, allocatedBytes, "Warmed RemoveAll allocated managed memory.");
            Assert.AreEqual(collectionsBefore, collectionsAfter, "Warmed RemoveAll triggered a collection.");
            Assert.AreEqual(0, withRemoved.EntitiesCount);
            Assert.AreEqual(3, withoutRemoved.EntitiesCount);
            Assert.IsFalse(world.Have<Removed>(first));
            Assert.IsFalse(world.Have<Removed>(second));
            Assert.AreEqual(20, world.Get<Retained>(first).Value);
            Assert.AreEqual(30, world.Get<Retained>(second).Value);
            Assert.IsFalse(world.HasAny<Removed>());
        }

        [Test]
        public void RemoveAll_SeveralArchetypesAndUnseenTargets_PreservesMappingsAndFilters()
        {
            var highId = EnsureHighComponentId();
            Assert.GreaterOrEqual(highId, BitMask.SizeOfPartInBits);

            var world = new EcsWorld();
            var removedFilter = world.Filter().With<Removed>().Build();
            var retainedWithoutRemoved = world.Filter().With<Retained>().Without<Removed>().Build();
            var highWithoutRemoved = world.Filter().With<HighIdComponent>().Without<Removed>().Build();

            var retainedOnly = world.Create();
            world.Add(retainedOnly, new Retained { Value = 1 });

            var retainedSource = CreateSource(world, 2, 20);
            var auxiliarySource = world.Create();
            world.Add(auxiliarySource, new Retained { Value = 3 });
            world.Add(auxiliarySource, new Auxiliary { Value = 30 });
            world.Add(auxiliarySource, new Removed { Value = 300 });

            var highSource = world.Create();
            world.Add(highSource, new HighIdComponent { Value = 4 });
            world.Add(highSource, new Removed { Value = 400 });

            Assert.AreEqual(3, removedFilter.EntitiesCount);
            world.RemoveAll<Removed>();

            Assert.AreEqual(0, removedFilter.EntitiesCount);
            Assert.AreEqual(3, retainedWithoutRemoved.EntitiesCount);
            Assert.AreEqual(1, highWithoutRemoved.EntitiesCount);
            Assert.AreEqual(2, world.Get<Retained>(retainedSource).Value);
            Assert.AreEqual(30, world.Get<Auxiliary>(auxiliarySource).Value);
            Assert.AreEqual(4, world.Get<HighIdComponent>(highSource).Value);
            Assert.IsFalse(world.Have<Removed>(retainedSource));
            Assert.IsFalse(world.Have<Removed>(auxiliarySource));
            Assert.IsFalse(world.Have<Removed>(highSource));

            // Reusing the scratch buffer after inserting unseen persistent targets must not
            // mutate those dictionary keys. Re-enter every source and resolve them again.
            world.Add(retainedSource, new Removed { Value = 21 });
            world.Add(auxiliarySource, new Removed { Value = 301 });
            world.Add(highSource, new Removed { Value = 401 });
            world.RemoveAll<Removed>();

            Assert.AreEqual(0, removedFilter.EntitiesCount);
            Assert.AreEqual(3, retainedWithoutRemoved.EntitiesCount);
            Assert.AreEqual(1, highWithoutRemoved.EntitiesCount);
        }

        [Test]
        public void ScratchCompatibleMasks_HandleEmptyNonemptyAndMissingBitsBeyondFirstChunk()
        {
            var highId = EnsureHighComponentId();
            var empty = new BitMask();
            var scratch = new BitMask();
            scratch.Copy(empty);
            scratch.Unset(highId);
            Assert.AreEqual(0, scratch.Length);
            Assert.AreEqual(0, scratch.SetBitsCount);

            var source = new BitMask(1, highId);
            scratch.Copy(source);
            scratch.Unset(highId + 1);
            Assert.IsTrue(scratch.MasksEquals(source));
            scratch.Unset(highId);
            Assert.IsTrue(scratch.Check(1));
            Assert.IsFalse(scratch.Check(highId));
            Assert.AreEqual(1, scratch.SetBitsCount);

            var world = new EcsWorld();
            var eid = world.Create();
            world.Add(eid, new Retained { Value = 7 });
            world.RemoveAll<Missing>();
            Assert.IsTrue(world.Have<Retained>(eid));
            Assert.AreEqual(7, world.Get<Retained>(eid).Value);
        }

        private static int CreateSource(EcsWorld world, int retainedValue, int removedValue)
        {
            var eid = world.Create();
            world.Add(eid, new Retained { Value = retainedValue });
            world.Add(eid, new Removed { Value = removedValue });
            return eid;
        }

        private static int TotalCollections() =>
            GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2);

        private static int EnsureHighComponentId()
        {
            _ = ComponentMeta<Padding00>.Id;
            _ = ComponentMeta<Padding01>.Id;
            _ = ComponentMeta<Padding02>.Id;
            _ = ComponentMeta<Padding03>.Id;
            _ = ComponentMeta<Padding04>.Id;
            _ = ComponentMeta<Padding05>.Id;
            _ = ComponentMeta<Padding06>.Id;
            _ = ComponentMeta<Padding07>.Id;
            _ = ComponentMeta<Padding08>.Id;
            _ = ComponentMeta<Padding09>.Id;
            _ = ComponentMeta<Padding10>.Id;
            _ = ComponentMeta<Padding11>.Id;
            _ = ComponentMeta<Padding12>.Id;
            _ = ComponentMeta<Padding13>.Id;
            _ = ComponentMeta<Padding14>.Id;
            _ = ComponentMeta<Padding15>.Id;
            _ = ComponentMeta<Padding16>.Id;
            _ = ComponentMeta<Padding17>.Id;
            _ = ComponentMeta<Padding18>.Id;
            _ = ComponentMeta<Padding19>.Id;
            _ = ComponentMeta<Padding20>.Id;
            _ = ComponentMeta<Padding21>.Id;
            _ = ComponentMeta<Padding22>.Id;
            _ = ComponentMeta<Padding23>.Id;
            _ = ComponentMeta<Padding24>.Id;
            _ = ComponentMeta<Padding25>.Id;
            _ = ComponentMeta<Padding26>.Id;
            _ = ComponentMeta<Padding27>.Id;
            _ = ComponentMeta<Padding28>.Id;
            _ = ComponentMeta<Padding29>.Id;
            _ = ComponentMeta<Padding30>.Id;
            _ = ComponentMeta<Padding31>.Id;
            _ = ComponentMeta<Padding32>.Id;
            _ = ComponentMeta<Padding33>.Id;
            _ = ComponentMeta<Padding34>.Id;
            _ = ComponentMeta<Padding35>.Id;
            _ = ComponentMeta<Padding36>.Id;
            _ = ComponentMeta<Padding37>.Id;
            _ = ComponentMeta<Padding38>.Id;
            _ = ComponentMeta<Padding39>.Id;
            _ = ComponentMeta<Padding40>.Id;
            _ = ComponentMeta<Padding41>.Id;
            _ = ComponentMeta<Padding42>.Id;
            _ = ComponentMeta<Padding43>.Id;
            _ = ComponentMeta<Padding44>.Id;
            _ = ComponentMeta<Padding45>.Id;
            _ = ComponentMeta<Padding46>.Id;
            _ = ComponentMeta<Padding47>.Id;
            _ = ComponentMeta<Padding48>.Id;
            _ = ComponentMeta<Padding49>.Id;
            _ = ComponentMeta<Padding50>.Id;
            _ = ComponentMeta<Padding51>.Id;
            _ = ComponentMeta<Padding52>.Id;
            _ = ComponentMeta<Padding53>.Id;
            _ = ComponentMeta<Padding54>.Id;
            _ = ComponentMeta<Padding55>.Id;
            _ = ComponentMeta<Padding56>.Id;
            _ = ComponentMeta<Padding57>.Id;
            _ = ComponentMeta<Padding58>.Id;
            _ = ComponentMeta<Padding59>.Id;
            _ = ComponentMeta<Padding60>.Id;
            _ = ComponentMeta<Padding61>.Id;
            _ = ComponentMeta<Padding62>.Id;
            _ = ComponentMeta<Padding63>.Id;
            _ = ComponentMeta<Padding64>.Id;
            return ComponentMeta<HighIdComponent>.Id;
        }
    }
}
#endif
