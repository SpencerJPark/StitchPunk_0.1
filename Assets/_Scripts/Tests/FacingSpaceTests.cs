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
    }
}
