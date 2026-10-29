using NUnit.Framework;

namespace StitchPunk.Tests
{
    [TestFixture]
    public sealed class CaravanDriveMathTests
    {
        private const float FrameSeconds = 1f / 60f;

        // The original CaravanMovementData numbers.
        private const float MoveSpeed = 7f;
        private const float TurnSpeed = 50f;
        private const float ForwardAcceleration = 1.2f;
        private const float ForwardDeceleration = 3f;
        private const float IdleTurnSpeedFactor = 0.4f;
        private const float TurnSmoothTime = 2.44f;

        // The feel the owner tuned: a slow, heavy pull away and a quicker settle when you let go.
        [Test]
        public void Speed_PullsAwayAtAccelerationAndSettlesAtTheFasterDeceleration()
        {
            float speed = 0f;
            int framesToFullSpeed = 0;
            while (speed < MoveSpeed && framesToFullSpeed < 10000)
            {
                speed = CaravanDriveMath.IntegrateSpeed(speed, MoveSpeed, ForwardAcceleration, ForwardDeceleration, FrameSeconds);
                framesToFullSpeed++;
            }
            int framesToStop = 0;
            while (speed > 0f && framesToStop < 10000)
            {
                speed = CaravanDriveMath.IntegrateSpeed(speed, 0f, ForwardAcceleration, ForwardDeceleration, FrameSeconds);
                framesToStop++;
            }

            Assert.AreEqual(MoveSpeed / ForwardAcceleration, framesToFullSpeed * FrameSeconds, 0.05f);
            Assert.AreEqual(MoveSpeed / ForwardDeceleration, framesToStop * FrameSeconds, 0.05f);
        }

        // Steering round past south must take the short way, never swing 340 degrees the long way round.
        [Test]
        public void Heading_TurnsTheShortWayAcrossTheWrap_AndNeverFasterThanTheTurnCap()
        {
            float heading = 170f;
            float turnVelocity = 0f;
            float turnCap = CaravanDriveMath.TurnSpeedLimit(MoveSpeed, MoveSpeed, TurnSpeed, IdleTurnSpeedFactor);

            for (int frameIndex = 0; frameIndex < 600; frameIndex++)
            {
                float previousHeading = heading;
                heading = CaravanDriveMath.SmoothDampAngle(heading, -170f, ref turnVelocity, TurnSmoothTime, turnCap, FrameSeconds);
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
            Assert.AreEqual(TurnSpeed * IdleTurnSpeedFactor, CaravanDriveMath.TurnSpeedLimit(0f, MoveSpeed, TurnSpeed, IdleTurnSpeedFactor), 0.0001f);
            Assert.AreEqual(TurnSpeed, CaravanDriveMath.TurnSpeedLimit(MoveSpeed, MoveSpeed, TurnSpeed, IdleTurnSpeedFactor), 0.0001f);
        }

        // The slam is what sells the smoke-burst entrance: it must swing past fully open, then settle at Open.
        [Test]
        public void BurstOpenDoors_OvershootFullyOpen_ThenSettleOpen()
        {
            Door door = new Door { phase = DoorPhase.Closed, openSeconds = 0.6f, closeSeconds = 0.5f, burstSeconds = 0.35f };
            DoorControl.BurstOpen(ref door);

            float largestOpenness = 0f;
            for (int frameIndex = 0; frameIndex < 60; frameIndex++)
            {
                largestOpenness = System.Math.Max(largestOpenness, DoorControl.Openness(in door));
                DoorControl.Advance(ref door, FrameSeconds);
            }

            Assert.Greater(largestOpenness, 1.05f);
            Assert.AreEqual(DoorPhase.Open, door.phase);
            Assert.AreEqual(1f, DoorControl.Openness(in door), 0.0001f);
        }

        // A smoke burst needs shut doors to burst through, so a script hold must beat a player standing in the zone.
        [Test]
        public void ScriptOverride_BeatsTheZone_AndNoneHandsItBack()
        {
            Door door = new Door { phase = DoorPhase.Open, openSeconds = 0.6f, closeSeconds = 0.5f, zoneWantsOpen = true };

            door.scriptOverride = DoorOverride.HoldShut;
            DoorControl.ApplyRequests(ref door);
            Assert.AreEqual(DoorPhase.Closing, door.phase);

            door = new Door { phase = DoorPhase.Closed, zoneWantsOpen = false, scriptOverride = DoorOverride.HoldOpen };
            DoorControl.ApplyRequests(ref door);
            Assert.AreEqual(DoorPhase.Opening, door.phase);

            door = new Door { phase = DoorPhase.Closed, zoneWantsOpen = true, scriptOverride = DoorOverride.None };
            DoorControl.ApplyRequests(ref door);
            Assert.AreEqual(DoorPhase.Opening, door.phase);
        }
    }
}
