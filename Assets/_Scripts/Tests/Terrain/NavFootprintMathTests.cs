using NUnit.Framework;
using Unity.Mathematics;

namespace StitchPunk.Tests
{
    [TestFixture]
    public sealed class NavFootprintMathTests
    {
        private const float QuarterTurn = math.PI / 2f;
        private const float EighthTurn = math.PI / 4f;
        private static readonly float2 Origin = float2.zero;

        [TestCase(1.9f, 0f, true)]
        [TestCase(2.1f, 0f, false)]
        [TestCase(-1.9f, 0f, true)]
        [TestCase(-2.1f, 0f, false)]
        [TestCase(0f, 0.9f, true)]
        [TestCase(0f, 1.1f, false)]
        [TestCase(0f, -0.9f, true)]
        [TestCase(0f, -1.1f, false)]
        public void UnrotatedFootprintCoversExactlyItsHalfExtents(float cellX, float cellZ, bool expectedInside)
        {
            bool inside = NavFootprintMath.IsCellCentreInsideFootprint(new float2(cellX, cellZ), Origin, new float2(2f, 1f), 0f);
            Assert.AreEqual(expectedInside, inside);
        }

        [Test]
        public void QuarterTurnSwapsTheLongAxisOntoWorldZ()
        {
            float2 halfExtents = new float2(10f, 2f);
            Assert.IsTrue(NavFootprintMath.IsCellCentreInsideFootprint(new float2(0f, 8f), Origin, halfExtents, QuarterTurn));
            Assert.IsFalse(NavFootprintMath.IsCellCentreInsideFootprint(new float2(8f, 0f), Origin, halfExtents, QuarterTurn));
        }

        [Test]
        public void EighthTurnPutsTheLongAxisOnOneDiagonalAndNotOnWorldX()
        {
            float2 halfExtents = new float2(10f, 1f);
            float diagonalComponent = 5f / math.sqrt(2f);
            // Production yaw runs from +Z toward +X, so the long axis is the (+x, -z) diagonal.
            bool longAxisDiagonalInside = NavFootprintMath.IsCellCentreInsideFootprint(new float2(diagonalComponent, -diagonalComponent), Origin, halfExtents, EighthTurn);
            bool otherDiagonalInside = NavFootprintMath.IsCellCentreInsideFootprint(new float2(diagonalComponent, diagonalComponent), Origin, halfExtents, EighthTurn);
            bool worldXInside = NavFootprintMath.IsCellCentreInsideFootprint(new float2(5f, 0f), Origin, halfExtents, EighthTurn);
            Assert.IsTrue(longAxisDiagonalInside);
            Assert.IsFalse(otherDiagonalInside);
            Assert.IsFalse(worldXInside);
        }

        [TestCase(3f, 1f)]
        [TestCase(-2f, 4f)]
        [TestCase(0.5f, -0.25f)]
        public void RotatedFootprintIsSymmetricAboutItsCentre(float offsetX, float offsetZ)
        {
            float2 halfExtents = new float2(10f, 2f);
            bool forward = NavFootprintMath.IsCellCentreInsideFootprint(new float2(offsetX, offsetZ), Origin, halfExtents, 0.6f);
            bool mirrored = NavFootprintMath.IsCellCentreInsideFootprint(new float2(-offsetX, -offsetZ), Origin, halfExtents, 0.6f);
            Assert.AreEqual(forward, mirrored);
        }

        [TestCase(1f, 0f)]
        [TestCase(0f, 9f)]
        [TestCase(7f, 7f)]
        [TestCase(-6f, 5f)]
        public void NonZeroFootprintCentreOnlyTranslates(float offsetX, float offsetZ)
        {
            float2 halfExtents = new float2(8f, 3f);
            float2 centre = new float2(250f, -40f);
            float2 offset = new float2(offsetX, offsetZ);
            bool atOrigin = NavFootprintMath.IsCellCentreInsideFootprint(offset, Origin, halfExtents, 0.9f);
            bool translated = NavFootprintMath.IsCellCentreInsideFootprint(centre + offset, centre, halfExtents, 0.9f);
            Assert.AreEqual(atOrigin, translated);
        }

        [Test]
        public void AxisAlignedHalfExtentsAreUnchangedAtZeroYawAndSwappedAtQuarterTurn()
        {
            float2 halfExtents = new float2(10f, 2f);
            float2 unrotated = NavFootprintMath.AxisAlignedHalfExtents(halfExtents, 0f);
            float2 quarter = NavFootprintMath.AxisAlignedHalfExtents(halfExtents, QuarterTurn);
            Assert.AreEqual(10f, unrotated.x, 1e-4f);
            Assert.AreEqual(2f, unrotated.y, 1e-4f);
            Assert.AreEqual(2f, quarter.x, 1e-4f);
            Assert.AreEqual(10f, quarter.y, 1e-4f);
        }
    }
}
