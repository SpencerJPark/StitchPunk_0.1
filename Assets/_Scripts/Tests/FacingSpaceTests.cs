using DotsAnimationToolkit;
using DotsAnimationToolkit.Authoring;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace StitchPunk.Tests
{
    // World-fixed velocity.xz mapped straight onto FacingResolver's facing space (+x east, +y away
    // from camera) — DirectionFacing_System.md §2/§5. FacingResolver's own quantization is pinned by
    // the toolkit's FacingResolverTests; this only pins the mapping UnitFacingJob feeds it.
    [TestFixture]
    public sealed class FacingSpaceMappingTests
    {
        [Test]
        public void WorldToFacingSpace_MapsWorldXAndZOntoFacingXAndY()
        {
            float2 facingSpace = UnitFacingJob.WorldToFacingSpace(new float3(3f, 99f, -5f));
            Assert.AreEqual(3f, facingSpace.x);
            Assert.AreEqual(-5f, facingSpace.y);
        }

        [Test]
        public void WorldToFacingSpace_IgnoresWorldY()
        {
            float2 lowY = UnitFacingJob.WorldToFacingSpace(new float3(1f, -50f, 2f));
            float2 highY = UnitFacingJob.WorldToFacingSpace(new float3(1f, 50f, 2f));
            Assert.AreEqual(lowY, highY);
        }

        [Test]
        public void WorldToFacingSpace_ZeroVectorMapsToZero()
        {
            float2 facingSpace = UnitFacingJob.WorldToFacingSpace(float3.zero);
            Assert.AreEqual(float2.zero, facingSpace);
        }
    }

    // DirectionSetSO.TryGetEffectiveDirections derives the mirror-closed AnimationDirections a set
    // covers from which of its five east-side slots are filled — DirectionFacing_System.md §4. Shared
    // by DirectionSetBakeUtil (the bake-time warning) and the Direction Set Editor window's live
    // coverage readout, so this is the one place that derivation can go wrong.
    [TestFixture]
    public sealed class DirectionSetEffectiveDirectionsTests
    {
        private DirectionSetSO directionSet;
        private ClipAsset dummyClip;

        [SetUp]
        public void SetUp()
        {
            directionSet = ScriptableObject.CreateInstance<DirectionSetSO>();
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
    }
}
