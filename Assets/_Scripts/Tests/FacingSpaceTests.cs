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
    }
}
