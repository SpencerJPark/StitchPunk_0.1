using NUnit.Framework;

namespace StitchPunk.Tests
{
    [TestFixture]
    public sealed class CaravanDriveMathTests
    {
        private const float FrameSeconds = 1f / 60f;

        // The original CaravanMovementData numbers.
        private static CaravanDriveTuning DefaultTuning()
        {
            return new CaravanDriveTuning
            {
                moveSpeed = 7f,
                turnSpeed = 50f,
                forwardAcceleration = 1.2f,
                forwardDeceleration = 3f,
                idleTurnSpeedFactor = 0.4f,
                turnSmoothTime = 2.44f,
                wheelRadius = 1.2f,
            };
        }

        // The feel the owner tuned: a slow, heavy pull away and a quicker settle when you let go.
        [Test]
        public void Speed_PullsAwayAtAccelerationAndSettlesAtTheFasterDeceleration()
        {
            CaravanDriveTuning tuning = DefaultTuning();
            float speed = 0f;
            int framesToFullSpeed = 0;
            while (speed < tuning.moveSpeed && framesToFullSpeed < 10000)
            {
                speed = CaravanDriveMath.IntegrateSpeed(speed, tuning.moveSpeed, in tuning, FrameSeconds);
                framesToFullSpeed++;
            }
            int framesToStop = 0;
            while (speed > 0f && framesToStop < 10000)
            {
                speed = CaravanDriveMath.IntegrateSpeed(speed, 0f, in tuning, FrameSeconds);
                framesToStop++;
            }

            Assert.AreEqual(tuning.moveSpeed / tuning.forwardAcceleration, framesToFullSpeed * FrameSeconds, 0.05f);
            Assert.AreEqual(tuning.moveSpeed / tuning.forwardDeceleration, framesToStop * FrameSeconds, 0.05f);
        }

        // Steering round past south must take the short way, never swing 340 degrees the long way round.
        [Test]
        public void Heading_TurnsTheShortWayAcrossTheWrap_AndNeverFasterThanTheTurnCap()
        {
            CaravanDriveTuning tuning = DefaultTuning();
            float heading = 170f;
            float turnVelocity = 0f;
            float turnCap = CaravanDriveMath.TurnSpeedLimit(tuning.moveSpeed, in tuning);

            for (int frameIndex = 0; frameIndex < 600; frameIndex++)
            {
                float previousHeading = heading;
                heading = CaravanDriveMath.SmoothDampAngle(heading, -170f, ref turnVelocity, tuning.turnSmoothTime, turnCap, FrameSeconds);
                float turnedThisFrame = CaravanDriveMath.DeltaAngle(previousHeading, heading);
                Assert.GreaterOrEqual(turnedThisFrame, -0.0001f, "Turned the long way round on frame " + frameIndex);
                Assert.LessOrEqual(turnedThisFrame, turnCap * FrameSeconds + 0.001f, "Beat the turn cap on frame " + frameIndex);
            }
            Assert.AreEqual(0f, CaravanDriveMath.DeltaAngle(heading, -170f), 1f);
        }

        // Standing still you can still turn, but only at idleTurnSpeedFactor of the cap.
        [Test]
        public void TurnCap_AtStandstill_IsTheIdleFractionOfFullSpeed()
        {
            CaravanDriveTuning tuning = DefaultTuning();
            Assert.AreEqual(tuning.turnSpeed * tuning.idleTurnSpeedFactor, CaravanDriveMath.TurnSpeedLimit(0f, in tuning), 0.0001f);
            Assert.AreEqual(tuning.turnSpeed, CaravanDriveMath.TurnSpeedLimit(tuning.moveSpeed, in tuning), 0.0001f);
        }

        // The slam is what sells the smoke-burst entrance: it must swing past fully open, then settle at Open.
        [Test]
        public void BurstOpenDoors_OvershootFullyOpen_ThenSettleOpen()
        {
            CaravanDoors doors = new CaravanDoors { phase = CaravanDoorPhase.Closed, openSeconds = 0.6f, closeSeconds = 0.5f, burstSeconds = 0.35f };
            CaravanDoorControl.BurstOpen(ref doors);

            float largestOpenness = 0f;
            for (int frameIndex = 0; frameIndex < 60; frameIndex++)
            {
                largestOpenness = System.Math.Max(largestOpenness, CaravanDoorControl.Openness(in doors));
                CaravanDoorControl.Advance(ref doors, FrameSeconds);
            }

            Assert.Greater(largestOpenness, 1.05f);
            Assert.AreEqual(CaravanDoorPhase.Open, doors.phase);
            Assert.AreEqual(1f, CaravanDoorControl.Openness(in doors), 0.0001f);
        }
    }
}
