// Copyright (c) 2026 Stitch Punk. All rights reserved.

using NUnit.Framework;

namespace StitchPunk.AnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// EditMode coverage of flipbook index resolution.
    /// </summary>
    /// <remarks>
    /// <strong>These exist to pin the property that is easiest to lose.</strong> A relative key
    /// stores its offset and nothing else, so moving a track's base retargets every key at once and
    /// no offset is recomputed. An implementation that stored resolved indices would pass a naive
    /// "does it resolve correctly" test and fail every retargeting test below, which is why the
    /// retargeting cases are written as retargeting rather than as arithmetic.
    /// </remarks>
    public sealed class SpriteIndexResolverTests
    {
        [Test]
        public void Absolute_ResolvesToTheStoredValue_WhateverTheBase()
        {
            Assert.AreEqual(12, SpriteIndexResolver.Resolve(12, SpriteIndexMode.Absolute, 0));
            Assert.AreEqual(12, SpriteIndexResolver.Resolve(12, SpriteIndexMode.Absolute, 7));
            Assert.AreEqual(12, SpriteIndexResolver.Resolve(12, SpriteIndexMode.Absolute, 100));
        }

        [Test]
        public void Absolute_PassesTheNoChangeSentinelThrough()
        {
            Assert.AreEqual(
                SpriteIndexResolver.NoChangeSentinel,
                SpriteIndexResolver.Resolve(
                    SpriteIndexResolver.NoChangeSentinel, SpriteIndexMode.Absolute, 32),
                "-1 has to survive resolution for the caller to still recognise it as \"no change\".");
        }

        [Test]
        public void Relative_ResolvesAgainstTheBase()
        {
            Assert.AreEqual(12, SpriteIndexResolver.Resolve(5, SpriteIndexMode.RelativeToBase, 7));
            Assert.AreEqual(7, SpriteIndexResolver.Resolve(0, SpriteIndexMode.RelativeToBase, 7));
            Assert.AreEqual(5, SpriteIndexResolver.Resolve(-2, SpriteIndexMode.RelativeToBase, 7));
        }

        [Test]
        public void Relative_TreatsNegativesAsOffsets_NotAsTheSentinel()
        {
            // -1 is one frame back from the base, not "no change". A resolver that special-cased it
            // would make the frame before the base unreachable on every relative track.
            Assert.AreEqual(31, SpriteIndexResolver.Resolve(-1, SpriteIndexMode.RelativeToBase, 32));
        }

        [Test]
        public void MovingTheBase_RetargetsEveryRelativeKey_WithoutTouchingTheOffsets()
        {
            // The mouth set authored against block 0, then retargeted onto block 32. The offsets are
            // the authored data and must be the *same numbers* before and after — this is the test a
            // resolved-index implementation cannot pass.
            int[] authoredOffsets = new int[] { 0, 3, 7, 2, -1 };

            int[] resolvedAtBaseZero = new int[authoredOffsets.Length];
            int[] resolvedAtBaseThirtyTwo = new int[authoredOffsets.Length];
            for (int keyIndex = 0; keyIndex < authoredOffsets.Length; keyIndex++)
            {
                resolvedAtBaseZero[keyIndex] = SpriteIndexResolver.Resolve(
                    authoredOffsets[keyIndex], SpriteIndexMode.RelativeToBase, 0);
                resolvedAtBaseThirtyTwo[keyIndex] = SpriteIndexResolver.Resolve(
                    authoredOffsets[keyIndex], SpriteIndexMode.RelativeToBase, 32);
            }

            CollectionAssert.AreEqual(new int[] { 0, 3, 7, 2, -1 }, resolvedAtBaseZero);
            CollectionAssert.AreEqual(new int[] { 32, 35, 39, 34, 31 }, resolvedAtBaseThirtyTwo);
            CollectionAssert.AreEqual(
                new int[] { 0, 3, 7, 2, -1 }, authoredOffsets,
                "Retargeting must not rewrite the stored offsets — that is the whole point of " +
                "storing offsets rather than resolved indices.");
        }

        [Test]
        public void TogglingModes_PreservesTheResolvedIndex()
        {
            const int BaseIndex = 32;

            // Absolute -> Relative: the offset is computed from the current base.
            int absoluteValue = 37;
            int asRelative = SpriteIndexResolver.StoredValueFor(
                absoluteValue, SpriteIndexMode.RelativeToBase, BaseIndex);
            Assert.AreEqual(5, asRelative);
            Assert.AreEqual(
                absoluteValue,
                SpriteIndexResolver.Resolve(asRelative, SpriteIndexMode.RelativeToBase, BaseIndex),
                "Converting to relative must not move the frame the key shows.");

            // Relative -> Absolute: the resolved value is baked out.
            int relativeOffset = 5;
            int resolved = SpriteIndexResolver.Resolve(
                relativeOffset, SpriteIndexMode.RelativeToBase, BaseIndex);
            int asAbsolute = SpriteIndexResolver.StoredValueFor(
                resolved, SpriteIndexMode.Absolute, BaseIndex);
            Assert.AreEqual(37, asAbsolute);
            Assert.AreEqual(
                resolved,
                SpriteIndexResolver.Resolve(asAbsolute, SpriteIndexMode.Absolute, BaseIndex),
                "Converting to absolute must not move the frame the key shows.");
        }

        [Test]
        public void StoredValueFor_IsTheInverseOfResolve()
        {
            for (int baseIndex = 0; baseIndex <= 64; baseIndex += 16)
            {
                for (int targetIndex = 0; targetIndex <= 40; targetIndex += 8)
                {
                    int storedRelative = SpriteIndexResolver.StoredValueFor(
                        targetIndex, SpriteIndexMode.RelativeToBase, baseIndex);
                    Assert.AreEqual(
                        targetIndex,
                        SpriteIndexResolver.Resolve(
                            storedRelative, SpriteIndexMode.RelativeToBase, baseIndex));

                    int storedAbsolute = SpriteIndexResolver.StoredValueFor(
                        targetIndex, SpriteIndexMode.Absolute, baseIndex);
                    Assert.AreEqual(
                        targetIndex,
                        SpriteIndexResolver.Resolve(
                            storedAbsolute, SpriteIndexMode.Absolute, baseIndex));
                }
            }
        }

        [Test]
        public void IndependentTracks_DriveTheSameArrayFromDifferentBases()
        {
            // One texture array, a mouth set at 0 and an eye set at 32, each keyed with its own
            // offsets. Neither track needs to know the other exists.
            Assert.AreEqual(3, SpriteIndexResolver.Resolve(3, SpriteIndexMode.RelativeToBase, 0));
            Assert.AreEqual(35, SpriteIndexResolver.Resolve(3, SpriteIndexMode.RelativeToBase, 32));
        }
    }
}
