using DotsMovementToolkit;
using NUnit.Framework;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace StitchPunk.Tests.PlayMode
{
    // PlayerRollStartSystem + PlayerRollMoveSystem without physics: the roll only writes Movement's
    // per-frame speed, so summing speed * deltaTime is the distance UnitMoverSystem would cover.
    public sealed class PlayerRollTests
    {
        private const float FrameSeconds = 1f / 60f;
        private const float WalkSpeed = 4f;

        private World testWorld;
        private double elapsedSeconds;
        private SystemHandle rollStartSystemHandle;
        private SystemHandle rollMoveSystemHandle;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("PlayerRollTests");
            testWorld.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>();
            rollStartSystemHandle = testWorld.GetOrCreateSystem<PlayerRollStartSystem>();
            rollMoveSystemHandle = testWorld.GetOrCreateSystem<PlayerRollMoveSystem>();
            elapsedSeconds = 0d;
        }

        [TearDown]
        public void TearDown()
        {
            if (testWorld != null && testWorld.IsCreated)
            {
                testWorld.Dispose();
            }
            testWorld = null;
        }

        [Test]
        public void Roll_PerFrameSpeeds_CoverRollDistance_ThenRestoreWalkSpeed()
        {
            EntityManager entityManager = testWorld.EntityManager;
            PlayerRollSettings rollSettings = DefaultRollSettings();
            Entity playerEntity = CreatePlayer(entityManager, rollSettings);

            PressRoll(entityManager, playerEntity, rollSettings);
            float distanceCovered = 0f;
            for (int frameIndex = 0; frameIndex < 120 && !RollFinished(entityManager, playerEntity); frameIndex++)
            {
                StepFrame();
                if (entityManager.IsComponentEnabled<PlayerRoll>(playerEntity))
                    distanceCovered += entityManager.GetComponentData<Movement>(playerEntity).moveSpeed * FrameSeconds;
            }

            Assert.AreEqual(rollSettings.rollDistance, distanceCovered, rollSettings.rollDistance * 0.02f,
                "Summed per-frame roll speed should cover rollDistance.");
            Assert.AreEqual(WalkSpeed, entityManager.GetComponentData<Movement>(playerEntity).moveSpeed, 0.0001f,
                "Walk speed should be restored when the roll ends.");
        }

        [Test]
        public void Roll_IsInvulnerableEarly_AndVulnerableAfterTheCutoff()
        {
            EntityManager entityManager = testWorld.EntityManager;
            PlayerRollSettings rollSettings = DefaultRollSettings();
            Entity playerEntity = CreatePlayer(entityManager, rollSettings);

            PressRoll(entityManager, playerEntity, rollSettings);
            StepUntilRollFraction(entityManager, playerEntity, rollSettings, 0.5f);
            Assert.IsTrue(entityManager.IsComponentEnabled<DodgeInvulnerable>(playerEntity),
                "Halfway through the roll the player should be invulnerable.");

            StepUntilRollFraction(entityManager, playerEntity, rollSettings, 0.85f);
            Assert.IsFalse(entityManager.IsComponentEnabled<DodgeInvulnerable>(playerEntity),
                "Past invulnerableUntilFraction the player should be hittable again.");
        }

        [Test]
        public void RollPressedJustBeforeLanding_StartsTheMomentTheCooldownEnds()
        {
            EntityManager entityManager = testWorld.EntityManager;
            PlayerRollSettings rollSettings = DefaultRollSettings();
            Entity playerEntity = CreatePlayer(entityManager, rollSettings);

            PressRoll(entityManager, playerEntity, rollSettings);
            StepUntilRollFraction(entityManager, playerEntity, rollSettings, 0.9f);
            PressRoll(entityManager, playerEntity, rollSettings);

            bool secondRollStarted = StepUntilSecondRoll(entityManager, playerEntity);
            Assert.IsTrue(secondRollStarted, "A press inside the buffer window should come out after the cooldown.");
        }

        [Test]
        public void RollPressedTooEarly_IsDropped()
        {
            EntityManager entityManager = testWorld.EntityManager;
            PlayerRollSettings rollSettings = DefaultRollSettings();
            Entity playerEntity = CreatePlayer(entityManager, rollSettings);

            PressRoll(entityManager, playerEntity, rollSettings);
            StepUntilRollFraction(entityManager, playerEntity, rollSettings, 0.1f);
            PressRoll(entityManager, playerEntity, rollSettings);

            bool secondRollStarted = StepUntilSecondRoll(entityManager, playerEntity);
            Assert.IsFalse(secondRollStarted, "A press made long before the roll is allowed should expire.");
        }

        private static PlayerRollSettings DefaultRollSettings()
        {
            return new PlayerRollSettings
            {
                rollDistance = 3.2f,
                rollDuration = 0.34f,
                speedCurvePower = 1.6f,
                invulnerableUntilFraction = 0.8f,
                cooldownAfterRoll = 0.2f,
                inputBufferWindow = 0.15f,
                attackBufferWindow = 0.15f,
                afterimageCount = 0,
                landingSquashDuration = 0.12f,
            };
        }

        private static Entity CreatePlayer(EntityManager entityManager, PlayerRollSettings rollSettings)
        {
            Entity playerEntity = entityManager.CreateEntity(
                typeof(Player), typeof(PlayerActionMap), typeof(Movement), typeof(LocalTransform),
                typeof(MovePlayerInput), typeof(OnRollPlayerInput), typeof(PlayerRollSettings),
                typeof(PlayerRoll), typeof(RollCooldown), typeof(DodgeInvulnerable), typeof(OnAttackPlayerInput));

            entityManager.SetComponentData(playerEntity, new PlayerActionMap { activeActionMap = ActionMaps.Player });
            entityManager.SetComponentData(playerEntity, new Movement { moveSpeed = WalkSpeed, runSpeed = WalkSpeed * 2f });
            entityManager.SetComponentData(playerEntity, LocalTransform.Identity);
            entityManager.SetComponentData(playerEntity, new MovePlayerInput { moveInput = new float2(1f, 0f) });
            entityManager.SetComponentData(playerEntity, rollSettings);
            entityManager.SetComponentEnabled<OnRollPlayerInput>(playerEntity, false);
            entityManager.SetComponentEnabled<PlayerRoll>(playerEntity, false);
            entityManager.SetComponentEnabled<RollCooldown>(playerEntity, false);
            entityManager.SetComponentEnabled<DodgeInvulnerable>(playerEntity, false);
            entityManager.SetComponentEnabled<OnAttackPlayerInput>(playerEntity, false);
            return playerEntity;
        }

        private static void PressRoll(EntityManager entityManager, Entity playerEntity, PlayerRollSettings rollSettings)
        {
            entityManager.SetComponentData(playerEntity, new OnRollPlayerInput { bufferRemaining = rollSettings.inputBufferWindow });
            entityManager.SetComponentEnabled<OnRollPlayerInput>(playerEntity, true);
        }

        private void StepFrame()
        {
            elapsedSeconds += FrameSeconds;
            testWorld.SetTime(new TimeData(elapsedSeconds, FrameSeconds));
            rollStartSystemHandle.Update(testWorld.Unmanaged);
            rollMoveSystemHandle.Update(testWorld.Unmanaged);
            testWorld.EntityManager.CompleteAllTrackedJobs();
        }

        private static bool RollFinished(EntityManager entityManager, Entity playerEntity)
        {
            return entityManager.GetComponentData<PlayerRoll>(playerEntity).elapsed > 0f
                && !entityManager.IsComponentEnabled<PlayerRoll>(playerEntity);
        }

        private void StepUntilRollFraction(EntityManager entityManager, Entity playerEntity,
            PlayerRollSettings rollSettings, float targetFraction)
        {
            for (int frameIndex = 0; frameIndex < 120; frameIndex++)
            {
                StepFrame();
                float rollFraction = entityManager.GetComponentData<PlayerRoll>(playerEntity).elapsed / rollSettings.rollDuration;
                if (rollFraction >= targetFraction) return;
            }
            Assert.Fail("The roll never reached fraction " + targetFraction);
        }

        // Steps through the end of the first roll and its cooldown; true if a fresh roll began.
        private bool StepUntilSecondRoll(EntityManager entityManager, Entity playerEntity)
        {
            bool firstRollEnded = false;
            for (int frameIndex = 0; frameIndex < 120; frameIndex++)
            {
                StepFrame();
                bool rolling = entityManager.IsComponentEnabled<PlayerRoll>(playerEntity);
                if (!rolling) firstRollEnded = true;
                if (firstRollEnded && rolling) return true;
            }
            return false;
        }
    }
}
