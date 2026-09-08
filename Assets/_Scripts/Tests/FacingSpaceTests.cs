using DotsAnimationToolkit;
using NUnit.Framework;
using Unity.Mathematics;

namespace StitchPunk.Tests
{
    // World-fixed velocity.xz mapped straight onto FacingResolver's facing space (+x east, +y away
    // from camera) — DirectionFacing_System.md §2/§5. FacingResolver's own quantization is pinned by
    // the toolkit's FacingResolverTests, and the fill-pattern → coverage derivation by the toolkit's
    // DirectionSetCoverageTests (it moved there with DirectionSetAsset). This pins only the mapping
    // UnitFacingJob feeds the resolver, which is the part the game owns.
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

        // The toolkit measures a cutscene facing FROM +X TOWARD +Z, so it lands in facing space with
        // no reflection. A LocalTransform Y euler measures from +Z instead, and swapping the two
        // silently turns an actor walking east into one facing north (toolkit A65's own bug).
        [Test]
        public void CutsceneAngle_IsMeasuredFromEastTowardNorth()
        {
            float2 east = UnitFacingJob.CutsceneAngleToFacingSpace(0f);
            Assert.AreEqual(1f, east.x, 1e-5f);
            Assert.AreEqual(0f, east.y, 1e-5f);

            float2 north = UnitFacingJob.CutsceneAngleToFacingSpace(90f);
            Assert.AreEqual(0f, north.x, 1e-5f);
            Assert.AreEqual(1f, north.y, 1e-5f);
        }

        [Test]
        public void CutsceneFacing_OverridesMovementDerivedFacing()
        {
            float3 walkingEast = new float3(10f, 0f, 0f);
            float2 aimingSouth = new float2(0f, -1f);

            float2 underACutscene = UnitFacingJob.ResolveMovementXY(
                true, 90f, true, in aimingSouth, walkingEast);
            Assert.AreEqual(0f, underACutscene.x, 1e-5f);
            Assert.AreEqual(1f, underACutscene.y, 1e-5f,
                "A cutscene's own facing wins over both the aim override and the movement delta.");

            float2 withoutACutscene = UnitFacingJob.ResolveMovementXY(
                false, 90f, false, in aimingSouth, walkingEast);
            Assert.AreEqual(walkingEast.x, withoutACutscene.x, 1e-5f);
            Assert.AreEqual(walkingEast.z, withoutACutscene.y, 1e-5f,
                "Without one, the movement delta still decides.");
        }

        // G6-P3: UnitFacingJob and the toolkit's own CutsceneTimelineSystem (A73 §3.3, WriteActorFacing)
        // both snap CutsceneFacing.angleDegrees through FacingResolver.FromMovement — one writer's
        // fold read twice, not two folds that could drift. This pins that the game's own pipeline
        // (ResolveMovementXY + CutsceneAngleToFacingSpace) feeds FromMovement the identical vector
        // the toolkit feeds it, so a future change to either side's angle-to-vector convention (the
        // toolkit A65 east/north-vs-euler bug this file already guards) cannot silently diverge them.
        [TestCase(0f)]
        [TestCase(45f)]
        [TestCase(90f)]
        [TestCase(135f)]
        [TestCase(180f)]
        [TestCase(225f)]
        [TestCase(270f)]
        [TestCase(315f)]
        public void CutsceneAngle_SnapsToTheSameDirection_AsTheToolkit(float angleDegrees)
        {
            AnimationDirections[] directionSets =
            {
                AnimationDirections.Six, AnimationDirections.Four, AnimationDirections.Two,
            };

            // Independently reconstructs the toolkit's own convention (CutsceneFacing.angleDegrees
            // measured FROM +X TOWARD +Z, A65 §3.3) rather than calling through
            // UnitFacingJob.CutsceneAngleToFacingSpace — routing both sides through the same helper
            // would make this pass even if that helper's convention drifted from the toolkit's.
            float angleRadians = math.radians(angleDegrees);
            float2 toolkitMovementXY = new float2(math.cos(angleRadians), math.sin(angleRadians));

            foreach (AnimationDirections directions in directionSets)
            {
                float2 gameMovementXY = UnitFacingJob.ResolveMovementXY(
                    true, angleDegrees, false, in float2.zero, float3.zero);
                Direction gameSnap = FacingResolver.FromMovement(in gameMovementXY, directions, Direction.South);

                Direction toolkitSnap = FacingResolver.FromMovement(in toolkitMovementXY, directions, Direction.South);

                Assert.AreEqual(toolkitSnap, gameSnap,
                    $"angle={angleDegrees}, directions={directions}: the game's snap must match the toolkit's own fold exactly — two writers computing the same thing is the risk this guards against.");
            }
        }
    }
}
