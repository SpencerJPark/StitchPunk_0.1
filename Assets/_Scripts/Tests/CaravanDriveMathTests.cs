using NUnit.Framework;

namespace StitchPunk.Tests
{
    [TestFixture]
    public sealed class CaravanDriveMathTests
    {
        private const float FrameSeconds = 1f / 60f;

        private static CaravanDriveTuning DefaultTuning()
        {
            return new CaravanDriveTuning
            {
                maxForwardSpeed = 7f,
                maxReverseSpeed = 2.5f,
                acceleration = 3.5f,
                brakeDeceleration = 9f,
                coastDeceleration = 1.5f,
                turnDegreesPerSecondAtFullSpeed = 55f,
                turnDegreesPerSecondAtLowSpeed = 25f,
                wheelRadius = 0.79f,
            };
        }

        // Pulling back at speed is the brake: it must stop the cart at brake strength, not merely
        // accelerate backwards through zero, and then reverse no faster than maxReverseSpeed.
        [Test]
        public void PullingBackWhileRollingForward_BrakesToAStopThenReversesNoFasterThanMaxReverse()
        {
            CaravanDriveTuning tuning = DefaultTuning();
            float forwardSpeed = tuning.maxForwardSpeed;
            int framesToStop = -1;

            for (int frameIndex = 0; frameIndex < 600; frameIndex++)
            {
                forwardSpeed = CaravanDriveMath.IntegrateForwardSpeed(forwardSpeed, -1f, in tuning, FrameSeconds);
                if (framesToStop < 0 && forwardSpeed <= 0.1f) framesToStop = frameIndex + 1;
            }

            int framesAtBrakeStrength = (int)System.Math.Ceiling(tuning.maxForwardSpeed / tuning.brakeDeceleration / FrameSeconds);
            Assert.GreaterOrEqual(framesToStop, 1, "Never came to a stop");
            Assert.LessOrEqual(framesToStop, framesAtBrakeStrength + 2, "Stopped at acceleration strength, not brake strength");
            Assert.AreEqual(-tuning.maxReverseSpeed, forwardSpeed, 0.001f);
        }

        // A cart cannot spin on the spot, and backing up swings the nose the other way.
        [Test]
        public void Yaw_IsZeroAtStandstill_AndFlipsWhenReversing()
        {
            CaravanDriveTuning tuning = DefaultTuning();

            Assert.AreEqual(0f, CaravanDriveMath.YawDegreesThisFrame(0f, 1f, in tuning, FrameSeconds), 0.0001f);

            float forwardYaw = CaravanDriveMath.YawDegreesThisFrame(4f, 1f, in tuning, FrameSeconds);
            float reverseYaw = CaravanDriveMath.YawDegreesThisFrame(-2f, 1f, in tuning, FrameSeconds);
            Assert.Greater(forwardYaw, 0f);
            Assert.Less(reverseYaw, 0f);
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
