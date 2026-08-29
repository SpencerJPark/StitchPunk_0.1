// Copyright (c) 2026 Spencer Park. All rights reserved.

using DotsAnimationToolkit.Authoring;
using NUnit.Framework;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// <see cref="DirectionSetAsset.TryGetEffectiveDirections"/> derives the mirror-closed
    /// <see cref="AnimationDirections"/> a set covers from which of its five east-side slots are
    /// filled. Every consumer — a host's bake warning, the 2D Direction Sets panel's coverage
    /// readout, the slider's quantize — reads coverage through that one method, so this is the one
    /// place the derivation can go wrong.
    /// </summary>
    [TestFixture]
    public sealed class DirectionSetCoverageTests
    {
        private DirectionSetAsset directionSet;
        private ClipAsset dummyClip;

        [SetUp]
        public void SetUp()
        {
            directionSet = ScriptableObject.CreateInstance<DirectionSetAsset>();
            dummyClip = ScriptableObject.CreateInstance<ClipAsset>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(directionSet);
            Object.DestroyImmediate(dummyClip);
        }

        [Test]
        public void SouthEastOnly_ResolvesToTwo()
        {
            directionSet.southEast = dummyClip;

            bool isValid = directionSet.TryGetEffectiveDirections(out AnimationDirections effectiveDirections);

            Assert.IsTrue(isValid);
            Assert.AreEqual(AnimationDirections.Two, effectiveDirections);
        }

        [Test]
        public void SouthEastAndNorthEast_ResolvesToFour()
        {
            directionSet.southEast = dummyClip;
            directionSet.northEast = dummyClip;

            bool isValid = directionSet.TryGetEffectiveDirections(out AnimationDirections effectiveDirections);

            Assert.IsTrue(isValid);
            Assert.AreEqual(AnimationDirections.Four, effectiveDirections);
        }

        [Test]
        public void FourPlusSouthAndNorth_ResolvesToSix()
        {
            directionSet.southEast = dummyClip;
            directionSet.northEast = dummyClip;
            directionSet.south = dummyClip;
            directionSet.north = dummyClip;

            bool isValid = directionSet.TryGetEffectiveDirections(out AnimationDirections effectiveDirections);

            Assert.IsTrue(isValid);
            Assert.AreEqual(AnimationDirections.Six, effectiveDirections);
        }

        [Test]
        public void AllFiveSlots_ResolvesToEight()
        {
            directionSet.southEast = dummyClip;
            directionSet.northEast = dummyClip;
            directionSet.south = dummyClip;
            directionSet.north = dummyClip;
            directionSet.east = dummyClip;

            bool isValid = directionSet.TryGetEffectiveDirections(out AnimationDirections effectiveDirections);

            Assert.IsTrue(isValid);
            Assert.AreEqual(AnimationDirections.Eight, effectiveDirections);
        }

        [Test]
        public void SouthOnly_ResolvesToOne()
        {
            directionSet.south = dummyClip;

            bool isValid = directionSet.TryGetEffectiveDirections(out AnimationDirections effectiveDirections);

            Assert.IsTrue(isValid);
            Assert.AreEqual(AnimationDirections.One, effectiveDirections);
        }

        [Test]
        public void NoSlotsFilled_IsInvalidAndDegradesToOne()
        {
            bool isValid = directionSet.TryGetEffectiveDirections(out AnimationDirections effectiveDirections);

            Assert.IsFalse(isValid);
            Assert.AreEqual(AnimationDirections.One, effectiveDirections);
        }

        [Test]
        public void SouthEastPlusSouth_IsInvalidAndRoundsDownToTwo()
        {
            // Not one of the five canonical patterns (South alongside an east-side slot with no
            // North to pair it into Six) — must warn, and must round down rather than silently
            // upgrading past what was actually authored.
            directionSet.southEast = dummyClip;
            directionSet.south = dummyClip;

            bool isValid = directionSet.TryGetEffectiveDirections(out AnimationDirections effectiveDirections);

            Assert.IsFalse(isValid);
            Assert.AreEqual(AnimationDirections.Two, effectiveDirections);
        }

        [Test]
        public void NorthEastOnly_IsInvalidAndRoundsDownToOne()
        {
            // NorthEast with no SouthEast satisfies none of the five valid patterns and has no
            // South slot either, so there is nothing usable to round down to but One.
            directionSet.northEast = dummyClip;

            bool isValid = directionSet.TryGetEffectiveDirections(out AnimationDirections effectiveDirections);

            Assert.IsFalse(isValid);
            Assert.AreEqual(AnimationDirections.One, effectiveDirections);
        }

        /// <summary>
        /// <see cref="DirectionSetAsset.GetRequiredSlots"/> is the inverse of the derivation, and the
        /// panel scaffolds its queue from it — so filling exactly what it names for a coverage must
        /// derive back to that same coverage, or the panel would show a set as finished that the bake
        /// still warns about.
        /// </summary>
        [Test]
        public void FillingEveryRequiredSlot_DerivesBackToThatCoverage()
        {
            AnimationDirections[] coverages = new[]
            {
                AnimationDirections.One, AnimationDirections.Two, AnimationDirections.Four,
                AnimationDirections.Six, AnimationDirections.Eight
            };

            foreach (AnimationDirections coverage in coverages)
            {
                DirectionSetAsset probe = ScriptableObject.CreateInstance<DirectionSetAsset>();
                try
                {
                    foreach (Direction slot in DirectionSetAsset.GetRequiredSlots(coverage))
                    {
                        probe.SetSlot(slot, dummyClip);
                    }

                    bool isValid = probe.TryGetEffectiveDirections(out AnimationDirections derived);

                    Assert.IsTrue(isValid, coverage + "'s required slots must be a valid fill pattern.");
                    Assert.AreEqual(coverage, derived);
                }
                finally
                {
                    Object.DestroyImmediate(probe);
                }
            }
        }
    }
}
