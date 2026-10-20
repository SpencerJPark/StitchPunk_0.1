using DotsMovementToolkit;
using NUnit.Framework;

namespace DotsMovementToolkit.Tests.EditMode
{
    // Regression: a long frame used to carry a unit past the floor in one step, after which the
    // ground ray started below the surface and the unit fell forever.
    [TestFixture]
    public sealed class GravityStepTests
    {
        private const float FallSpeed = 16f;
        private const float FloatTolerance = 0.0001f;

        [Test]
        public void FastFallThatWouldPassTheFloorInOneFrameLandsOnIt()
        {
            GravityStepResult result = GravityStep.Resolve(0.5f, -10f, FallSpeed, 0.33f, true, 0f);

            Assert.AreEqual(0f, result.positionY, FloatTolerance);
            Assert.AreEqual(0f, result.verticalVelocity, FloatTolerance);
            Assert.IsTrue(result.isGrounded);
        }

        [Test]
        public void UnitPushedSlightlyBelowTheFloorIsSnappedBackUp()
        {
            GravityStepResult result = GravityStep.Resolve(-0.3f, 0f, FallSpeed, 0.016f, true, 0f);

            Assert.AreEqual(0f, result.positionY, FloatTolerance);
            Assert.IsTrue(result.isGrounded);
        }

        [Test]
        public void SweepReachesPastThisFramesFallStep()
        {
            float fallStep = (10f + FallSpeed * 0.33f) * 0.33f;
            float longFrameSweep = GravityStep.SweepDistance(-10f, FallSpeed, 0.33f, 3f);

            Assert.GreaterOrEqual(longFrameSweep, fallStep);
            Assert.GreaterOrEqual(longFrameSweep, 3f);
            Assert.AreEqual(3f, GravityStep.SweepDistance(0f, FallSpeed, 0.016f, 3f), FloatTolerance);
        }

        [Test]
        public void AirborneUnitAboveTheFloorFallsWithoutSnapping()
        {
            GravityStepResult result = GravityStep.Resolve(2f, 0f, FallSpeed, 0.016f, true, 0f);

            Assert.IsFalse(result.isGrounded);
            Assert.Less(result.positionY, 2f);
            Assert.Greater(result.positionY, 0f);
        }
    }
}
