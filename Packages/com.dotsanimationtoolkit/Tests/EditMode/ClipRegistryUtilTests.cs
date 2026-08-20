// Copyright (c) 2026 Spencer Park. All rights reserved.

using NUnit.Framework;
using Unity.Entities;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// EditMode coverage of the binary-search id resolve contract (architecture sections 4.3,
    /// 8 M3): hit on first/middle/last entries, misses below/between/above the range, the reserved
    /// invalid id, and the empty registry.
    ///
    /// The fixture hands clip ids over as {5, 2, 100, 9} to prove authoring order is discarded: the
    /// bake sorts clips ascending by id (section 4.5.1), so the baked order is {2, 5, 9, 100} and a
    /// clip's dense index is simply its position there — 2→0, 5→1, 9→2, 100→3. Amendment A11 deleted
    /// the old clipIndexById indirection precisely because that made it the identity map. Target ids
    /// {30, 10, 20} likewise bake to {10, 20, 30}.
    /// </summary>
    public sealed class ClipRegistryUtilTests
    {
        private BlobAssetReference<ClipRegistryBlob> registryReference;
        private BlobAssetReference<ClipRegistryBlob> emptyRegistryReference;

        [SetUp]
        public void CreateRegistryFixtures()
        {
            TestBlobFactory.ClipSpec[] clipSpecs =
            {
                new TestBlobFactory.ClipSpec { clipId = 5, debugName = "Five" },
                new TestBlobFactory.ClipSpec { clipId = 2, debugName = "Two" },
                new TestBlobFactory.ClipSpec { clipId = 100, debugName = "Hundred" },
                new TestBlobFactory.ClipSpec { clipId = 9, debugName = "Nine" }
            };
            registryReference = TestBlobFactory.BuildRegistry(clipSpecs, new uint[] { 30, 10, 20 });
            emptyRegistryReference = TestBlobFactory.BuildRegistry(
                new TestBlobFactory.ClipSpec[0], new uint[0]);
        }

        [TearDown]
        public void DisposeRegistryFixtures()
        {
            if (registryReference.IsCreated)
            {
                registryReference.Dispose();
            }
            registryReference = default;
            if (emptyRegistryReference.IsCreated)
            {
                emptyRegistryReference.Dispose();
            }
            emptyRegistryReference = default;
        }

        [Test]
        public void TryResolveClip_FirstSortedId_ResolvesToItsBakedPosition()
        {
            bool resolved = ClipRegistryUtil.TryResolveClip(
                ref registryReference.Value, new ClipId(2), out int clipIndex);
            Assert.IsTrue(resolved);
            Assert.AreEqual(
                0,
                clipIndex,
                "Id 2 is the lowest id, so it bakes first and its dense index is 0 — regardless of "
                + "having been authored second.");
        }

        [Test]
        public void TryResolveClip_LastSortedId_Resolves()
        {
            bool resolved = ClipRegistryUtil.TryResolveClip(
                ref registryReference.Value, new ClipId(100), out int clipIndex);
            Assert.IsTrue(resolved);
            Assert.AreEqual(3, clipIndex, "Id 100 is the highest id, so it bakes last.");
        }

        [Test]
        public void TryResolveClip_MiddleSortedIds_Resolve()
        {
            bool resolvedFive = ClipRegistryUtil.TryResolveClip(
                ref registryReference.Value, new ClipId(5), out int fiveIndex);
            Assert.IsTrue(resolvedFive);
            Assert.AreEqual(1, fiveIndex);

            bool resolvedNine = ClipRegistryUtil.TryResolveClip(
                ref registryReference.Value, new ClipId(9), out int nineIndex);
            Assert.IsTrue(resolvedNine);
            Assert.AreEqual(2, nineIndex);
        }

        [Test]
        public void TryResolveClip_UnknownIdBetweenEntries_FailsWithMinusOne()
        {
            bool resolved = ClipRegistryUtil.TryResolveClip(
                ref registryReference.Value, new ClipId(7), out int clipIndex);
            Assert.IsFalse(resolved);
            Assert.AreEqual(-1, clipIndex);
        }

        [Test]
        public void TryResolveClip_IdsOutsideTheSortedRange_Fail()
        {
            bool resolvedBelow = ClipRegistryUtil.TryResolveClip(
                ref registryReference.Value, new ClipId(1), out int belowIndex);
            Assert.IsFalse(resolvedBelow);
            Assert.AreEqual(-1, belowIndex);

            bool resolvedAbove = ClipRegistryUtil.TryResolveClip(
                ref registryReference.Value, new ClipId(200), out int aboveIndex);
            Assert.IsFalse(resolvedAbove);
            Assert.AreEqual(-1, aboveIndex);
        }

        [Test]
        public void TryResolveClip_ReservedInvalidId_Fails()
        {
            bool resolved = ClipRegistryUtil.TryResolveClip(
                ref registryReference.Value, new ClipId(0), out int clipIndex);
            Assert.IsFalse(resolved);
            Assert.AreEqual(-1, clipIndex);
        }

        [Test]
        public void TryResolveClip_EmptyRegistry_Fails()
        {
            bool resolved = ClipRegistryUtil.TryResolveClip(
                ref emptyRegistryReference.Value, new ClipId(5), out int clipIndex);
            Assert.IsFalse(resolved);
            Assert.AreEqual(-1, clipIndex);
        }

        [Test]
        public void ResolveTargetIndex_KnownIds_ReturnTheirSortedPositions()
        {
            bool resolvedTen = ClipRegistryUtil.ResolveTargetIndex(
                ref registryReference.Value, new TargetId(10), out int tenIndex);
            Assert.IsTrue(resolvedTen);
            Assert.AreEqual(0, tenIndex);

            bool resolvedTwenty = ClipRegistryUtil.ResolveTargetIndex(
                ref registryReference.Value, new TargetId(20), out int twentyIndex);
            Assert.IsTrue(resolvedTwenty);
            Assert.AreEqual(1, twentyIndex);

            bool resolvedThirty = ClipRegistryUtil.ResolveTargetIndex(
                ref registryReference.Value, new TargetId(30), out int thirtyIndex);
            Assert.IsTrue(resolvedThirty);
            Assert.AreEqual(2, thirtyIndex);
        }

        [Test]
        public void ResolveTargetIndex_UnknownId_FailsWithMinusOne()
        {
            bool resolved = ClipRegistryUtil.ResolveTargetIndex(
                ref registryReference.Value, new TargetId(99), out int targetIndex);
            Assert.IsFalse(resolved);
            Assert.AreEqual(-1, targetIndex);
        }

        [Test]
        public void ResolveTargetIndex_ReservedInvalidIdAndEmptyRegistry_Fail()
        {
            bool resolvedInvalid = ClipRegistryUtil.ResolveTargetIndex(
                ref registryReference.Value, new TargetId(0), out int invalidIndex);
            Assert.IsFalse(resolvedInvalid);
            Assert.AreEqual(-1, invalidIndex);

            bool resolvedEmpty = ClipRegistryUtil.ResolveTargetIndex(
                ref emptyRegistryReference.Value, new TargetId(10), out int emptyIndex);
            Assert.IsFalse(resolvedEmpty);
            Assert.AreEqual(-1, emptyIndex);
        }
    }
}
