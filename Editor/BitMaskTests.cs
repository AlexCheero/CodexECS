#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace CodexECS.Tests
{
    public sealed class BitMaskTests
    {
        [Test]
        public void DefaultMask_IsEmptyAndCanBeReused()
        {
            var mask = default(BitMask);
            AssertMask(mask);
            Assert.AreEqual(0, mask.GetMaskHash());
            Assert.IsFalse(mask.Check(-1));
            Assert.IsFalse(mask.Check(int.MaxValue));
            Assert.AreEqual(-1, mask.GetNextSetBit(-10));
            Assert.AreEqual(-1, mask.GetNextSetBit(int.MaxValue));

            mask.Set(-1);
            mask.Unset(-1);
            mask.Unset(int.MaxValue);
            mask.Clear();
            AssertMask(mask);

            mask.Set(63);
            AssertMask(mask, 63);
            mask.Unset(63);
            AssertMask(mask);
            mask.Set(128);
            AssertMask(mask, 128);
        }

        [Test]
        public void SetAndUnset_PreserveBitsAcrossOldAndNewPartBoundaries()
        {
            Assert.AreEqual(64, BitMask.SizeOfPartInBits);
            var positions = new[] { 0, 31, 32, 62, 63, 64, 95, 127, 128 };
            var mask = new BitMask(positions);
            mask.Set(positions);
            AssertMask(mask, positions);

            for (var i = positions.Length - 1; i >= 0; i--)
            {
                Assert.AreEqual(positions[i], mask.GetNextSetBit(positions[i]));
                mask.Unset(positions[i]);
                mask.Unset(positions[i]);
                var remaining = new int[i];
                Array.Copy(positions, remaining, i);
                AssertMask(mask, remaining);
            }
        }

        [Test]
        public void SignBits_AreCountedAndEnumeratedAsOrdinaryBits()
        {
            var mask = new BitMask(63, 127, 191);
            AssertMask(mask, 63, 127, 191);
            Assert.AreEqual(63, mask.GetNextSetBit(-1));
            Assert.AreEqual(63, mask.GetNextSetBit(62));
            Assert.AreEqual(127, mask.GetNextSetBit(64));
            Assert.AreEqual(191, mask.GetNextSetBit(128));
            Assert.AreEqual(-1, mask.GetNextSetBit(192));

            var allFirstPart = new int[64];
            for (var i = 0; i < allFirstPart.Length; i++)
                allFirstPart[i] = i;
            var full = new BitMask(allFirstPart);
            var copy = default(BitMask);
            copy.Set(full);
            AssertMask(copy, allFirstPart);
            copy.Unset(new BitMask(0, 31, 32, 62));
            Assert.AreEqual(60, copy.SetBitsCount);
            Assert.AreEqual(64, copy.Length);
            Assert.AreEqual(63, copy.GetNextSetBit(62));
            copy.Unset(full);
            AssertMask(copy);
        }

        [Test]
        public void SetAndUnsetMask_ApplyUnionAndDifferenceAcrossParts()
        {
            var mask = new BitMask(0, 31, 63, 128);
            var other = new BitMask(31, 32, 63, 64, 127, 256);
            mask.Set(other);
            AssertMask(mask, 0, 31, 32, 63, 64, 127, 128, 256);
            AssertMask(other, 31, 32, 63, 64, 127, 256);

            mask.Unset(new BitMask(0, 63, 127, 256, 512));
            AssertMask(mask, 31, 32, 64, 128);
            mask.Set(default(BitMask));
            mask.Unset(default(BitMask));
            AssertMask(mask, 31, 32, 64, 128);
            mask.Unset(mask);
            AssertMask(mask);
        }

        [Test]
        public void FiltersAndIntersection_HandleEmptyAndUnequalPartCounts()
        {
            var mask = new BitMask(31, 63, 64, 127, 128);
            var empty = default(BitMask);
            var subset = new BitMask(63, 128);
            var disjoint = new BitMask(32, 95, 512);
            var partial = new BitMask(127, 512);

            Assert.IsTrue(mask.InclusivePass(empty));
            Assert.IsTrue(empty.InclusivePass(empty));
            Assert.IsFalse(empty.InclusivePass(mask));
            Assert.IsTrue(mask.InclusivePass(subset));
            Assert.IsFalse(subset.InclusivePass(mask));
            Assert.IsFalse(mask.InclusivePass(partial));
            Assert.IsFalse(mask.InclusivePass(disjoint));
            Assert.IsTrue(mask.ExclusivePass(empty));
            Assert.IsTrue(empty.ExclusivePass(mask));
            Assert.IsTrue(mask.ExclusivePass(disjoint));
            Assert.IsTrue(disjoint.ExclusivePass(mask));
            Assert.IsFalse(mask.ExclusivePass(partial));
            Assert.IsFalse(partial.ExclusivePass(mask));
            Assert.IsTrue(mask.Intersects(partial));
            Assert.IsTrue(partial.Intersects(mask));
            Assert.IsFalse(mask.Intersects(disjoint));
            Assert.IsFalse(disjoint.Intersects(mask));
            Assert.IsFalse(mask.Intersects(empty));
            Assert.IsFalse(empty.Intersects(mask));
        }

        [Test]
        public void CopyAndDuplicate_DoNotShareStorageWithTheirSource()
        {
            var source = new BitMask(1, 63, 64, 127, 128);
            var duplicate = source.Duplicate();
            var copy = new BitMask(2, 512);
            copy.Copy(source);
            AssertMask(copy, 1, 63, 64, 127, 128);

            source.Unset(1);
            source.Set(62);
            AssertMask(duplicate, 1, 63, 64, 127, 128);
            AssertMask(copy, 1, 63, 64, 127, 128);

            duplicate.Unset(63);
            duplicate.Set(512);
            copy.Clear();
            AssertMask(source, 62, 63, 64, 127, 128);
            AssertMask(duplicate, 1, 64, 127, 128, 512);
            AssertMask(copy);

            copy.Copy(source);
            copy.Copy(copy);
            AssertMask(copy, 62, 63, 64, 127, 128);
            copy.Copy(default(BitMask));
            AssertMask(copy);
        }

        [Test]
        public void AndAndNot_ReturnIndependentModifiedCopies()
        {
            var original = new BitMask(1, 63, 128);
            AssertMask(original.And(127), 1, 63, 127, 128);
            AssertMask(original.And(32, 64), 1, 32, 63, 64, 128);
            AssertMask(original.AndNot(128), 1, 63);
            AssertMask(original.AndNot(1, 63), 128);
            AssertMask(original, 1, 63, 128);
        }

        [Test]
        public void UnsetHighestBit_InvalidatesCachedHashBeforeLengthShrinks()
        {
            var mask = new BitMask(1, 63, 127, 128);
            foreach (var highest in new[] { 128, 127, 63, 1 })
            {
                mask.GetMaskHash();
                mask.Unset(highest);
                var expectedBits = new List<int>();
                foreach (var bit in new[] { 1, 63, 127, 128 })
                    if (bit < highest)
                        expectedBits.Add(bit);
                var expected = new BitMask(expectedBits.ToArray());
                Assert.AreEqual(expected.GetMaskHash(), mask.GetMaskHash());
                Assert.IsTrue(BitMask.MaskComparer.Equals(expected, mask));

                var lookup = new Dictionary<BitMask, int>(BitMask.MaskComparer)
                {
                    { expected, highest }
                };
                Assert.IsTrue(lookup.ContainsKey(mask));
            }
        }

        [Test]
        public void ClearAndCopy_IgnoreRetainedCapacityForEqualityAndHash()
        {
            var retained = new BitMask(4095);
            retained.GetMaskHash();
            retained.Clear();
            var empty = default(BitMask);
            AssertMask(retained);
            Assert.IsTrue(retained.MasksEquals(empty));
            Assert.IsTrue(empty.MasksEquals(retained));
            Assert.AreEqual(empty.GetMaskHash(), retained.GetMaskHash());

            retained.Set(31, 63, 128);
            var exact = new BitMask(31, 63, 128);
            Assert.IsTrue(retained.MasksEquals(exact));
            Assert.IsTrue(exact.MasksEquals(retained));
            Assert.AreEqual(exact.GetMaskHash(), retained.GetMaskHash());
            Assert.IsFalse(exact.MasksEquals(new BitMask(32, 63, 128)));

            retained.Copy(new BitMask(63));
            AssertMask(retained, 63);
            Assert.AreEqual(new BitMask(63).GetMaskHash(), retained.GetMaskHash());
        }

        [Test]
        public void RandomMutations_MatchASetOracle()
        {
            var random = new Random(1729);
            var mask = default(BitMask);
            var expected = new SortedSet<int>();
            for (var step = 0; step < 500; step++)
            {
                var position = random.Next(-2, 260);
                switch (random.Next(5))
                {
                    case 0:
                        mask.Set(position);
                        if (position >= 0) expected.Add(position);
                        break;
                    case 1:
                        mask.Unset(position);
                        expected.Remove(position);
                        break;
                    case 2:
                    case 3:
                        var positions = new[] { position, random.Next(260), random.Next(260) };
                        var other = new BitMask(positions);
                        if (step % 2 == 0)
                        {
                            mask.Set(other);
                            foreach (var bit in positions)
                                if (bit >= 0) expected.Add(bit);
                        }
                        else
                        {
                            mask.Unset(other);
                            foreach (var bit in positions) expected.Remove(bit);
                        }
                        break;
                    default:
                        if (step % 17 == 0)
                        {
                            mask.Clear();
                            expected.Clear();
                        }
                        break;
                }

                var expectedBits = new int[expected.Count];
                expected.CopyTo(expectedBits);
                AssertMask(mask, expectedBits);
                for (var bit = -1; bit <= 260; bit++)
                    Assert.AreEqual(expected.Contains(bit), mask.Check(bit), "Step {0}, bit {1}", step, bit);
                Assert.AreEqual(new BitMask(expectedBits).GetMaskHash(), mask.GetMaskHash(), "Step {0}", step);
                var from = random.Next(-2, 262);
                var next = -1;
                foreach (var bit in expected)
                {
                    if (bit < from) continue;
                    next = bit;
                    break;
                }
                Assert.AreEqual(next, mask.GetNextSetBit(from), "Step {0}, from {1}", step, from);
            }
        }

#if DEBUG && !ECS_PERF_TEST
        [Test]
        public void DebugHelpers_PreservePublicSignaturesAndFormatting()
        {
            var mask = default(BitMask);
            Assert.AreEqual("{ }", mask.ToString());
            Func<uint, string> formatLegacyChunk = mask.ChunkToString;
            Assert.AreEqual(new string('0', 32), formatLegacyChunk(0u));
            Assert.AreEqual(new string('0', 31) + "1", formatLegacyChunk(1u));
            Assert.AreEqual("1" + new string('0', 31), formatLegacyChunk(0x80000000u));
            Assert.AreEqual(new string('1', 32), formatLegacyChunk(uint.MaxValue));
            Func<long, string> formatPart = mask.ChunkToString;
            Assert.AreEqual(new string('0', 64), formatPart(0L));
            Assert.AreEqual(new string('0', 63) + "1", formatPart(1L));
            Assert.AreEqual("1" + new string('0', 63), formatPart(long.MinValue));
            Assert.AreEqual(new string('1', 64), formatPart(-1L));

            mask.Set(128);
            mask.SetBits(new[] { 1, 0, 2 });
            AssertMask(mask, 0, 2, 128);
            Assert.AreEqual("{ 0, 2, 128 }", mask.ToString());
        }
#endif

        private static void AssertMask(BitMask mask, params int[] expected)
        {
            Assert.AreEqual(expected.Length, mask.SetBitsCount);
            Assert.AreEqual(expected.Length == 0 ? 0 : expected[expected.Length - 1] + 1, mask.Length);
            var actual = new List<int>();
            foreach (var bit in mask)
            {
                actual.Add(bit);
                Assert.LessOrEqual(actual.Count, expected.Length, "Enumeration must terminate without repeated bits.");
            }
            CollectionAssert.AreEqual(expected, actual);
            foreach (var bit in expected)
                Assert.IsTrue(mask.Check(bit), "Expected bit {0}", bit);
            Assert.IsFalse(mask.Check(mask.Length));
            Assert.AreEqual(-1, mask.GetNextSetBit(mask.Length));
        }
    }
}
#endif
