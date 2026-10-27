using DotsMovementToolkit;
using NUnit.Framework;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace StitchPunk.Tests.PlayMode
{
    public sealed class FakeRagdollTests
    {
        private const float FrameSeconds = 1f / 30f;

        private World testWorld;
        private double elapsedSeconds;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("FakeRagdollTests");
            elapsedSeconds = 10.0;
        }

        [TearDown]
        public void TearDown()
        {
            if (testWorld != null && testWorld.IsCreated) testWorld.Dispose();
            testWorld = null;
        }

        // The whole point of the get-up: the unit is back on its feet where it landed, with exactly the pose it had,
        // and its AI gate released. A missed restore leaves it lying sideways or with a bent arm forever.
        [Test]
        public void LivingUnit_KnockedOver_LandsAwayStandsUpAndGetsItsExactRestPoseBack()
        {
            float4x4 visualRestMatrix = float4x4.Scale(2f);
            quaternion limbRestRotation = quaternion.RotateZ(0.3f);
            Entity unit = CreateKnockedUnit(visualRestMatrix, limbRestRotation, isDead: false, out Entity visual, out Entity limb);
            EntityManager entityManager = testWorld.EntityManager;

            bool sawLimbFlail = false;
            bool sawAiGated = false;
            for (int frameIndex = 0; frameIndex < 30; frameIndex++)
            {
                Step();
                quaternion limbRotation = entityManager.GetComponentData<LocalTransform>(limb).Rotation;
                if (math.abs(math.dot(limbRotation.value, limbRestRotation.value)) < 0.999f) sawLimbFlail = true;
                if (entityManager.IsComponentEnabled<CutsceneActor>(unit)) sawAiGated = true;
            }
            Assert.IsTrue(sawLimbFlail, "Limbs never flailed while airborne");
            Assert.IsTrue(sawAiGated, "AI was never gated while the unit was down");

            for (int frameIndex = 0; frameIndex < 200 && entityManager.IsComponentEnabled<FakeRagdoll>(unit); frameIndex++)
                Step();

            Assert.IsFalse(entityManager.IsComponentEnabled<FakeRagdoll>(unit), "Never got back up");
            Assert.IsFalse(entityManager.IsComponentEnabled<CutsceneActor>(unit), "AI gate left on after standing up");
            float3 standingPosition = entityManager.GetComponentData<LocalTransform>(unit).Position;
            Assert.Greater(standingPosition.x, 1f, "Stood up where it was hit instead of where it landed");
            Assert.AreEqual(0f, standingPosition.y, 0.001f);
            AssertMatricesEqual(visualRestMatrix, entityManager.GetComponentData<PostTransformMatrix>(visual).Value);
            Assert.Greater(math.abs(math.dot(entityManager.GetComponentData<LocalTransform>(limb).Rotation.value, limbRestRotation.value)), 0.9999f);
        }

        // A corpse stays on the ground; only a revive (Dead cleared) lets it stand.
        [Test]
        public void DeadUnit_StaysDownUntilDeadIsCleared()
        {
            Entity unit = CreateKnockedUnit(float4x4.identity, quaternion.identity, isDead: true, out Entity _, out Entity _);
            EntityManager entityManager = testWorld.EntityManager;

            for (int frameIndex = 0; frameIndex < 300; frameIndex++) Step();
            Assert.IsTrue(entityManager.IsComponentEnabled<FakeRagdoll>(unit));
            Assert.AreEqual(FakeRagdollPhase.Lying, entityManager.GetComponentData<FakeRagdoll>(unit).phase);

            entityManager.SetComponentEnabled<Dead>(unit, false);
            for (int frameIndex = 0; frameIndex < 60; frameIndex++) Step();
            Assert.IsFalse(entityManager.IsComponentEnabled<FakeRagdoll>(unit), "A revived corpse never stood up");
        }

        private Entity CreateKnockedUnit(float4x4 visualRestMatrix, quaternion limbRestRotation, bool isDead,
            out Entity visual, out Entity limb)
        {
            EntityManager entityManager = testWorld.EntityManager;
            visual = entityManager.CreateEntity(typeof(LocalTransform), typeof(PostTransformMatrix));
            entityManager.SetComponentData(visual, LocalTransform.Identity);
            entityManager.SetComponentData(visual, new PostTransformMatrix { Value = visualRestMatrix });
            limb = entityManager.CreateEntity(typeof(LocalTransform));
            entityManager.SetComponentData(limb, LocalTransform.FromRotation(limbRestRotation));

            Entity unit = entityManager.CreateEntity(typeof(LocalTransform), typeof(Movement), typeof(Gravity),
                typeof(CutsceneActor), typeof(Dead), typeof(FakeRagdollRig), typeof(FakeRagdoll), typeof(FakeRagdollLimb));
            entityManager.SetComponentData(unit, LocalTransform.Identity);
            entityManager.SetComponentEnabled<CutsceneActor>(unit, false);
            entityManager.SetComponentEnabled<Dead>(unit, isDead);
            entityManager.SetComponentData(unit, new FakeRagdollRig { visual = visual });
            entityManager.GetBuffer<FakeRagdollLimb>(unit).Add(new FakeRagdollLimb { limb = limb, flailDegrees = 60f });

            FakeRagdoll fakeRagdoll = default;
            FakeRagdollControl.Knock(ref fakeRagdoll, float3.zero, new float3(4f, 5f, 0f), 0.5f, false);
            entityManager.SetComponentData(unit, fakeRagdoll);
            entityManager.SetComponentEnabled<FakeRagdoll>(unit, true);
            return unit;
        }

        private void Step()
        {
            elapsedSeconds += FrameSeconds;
            testWorld.SetTime(new TimeData(elapsedSeconds, FrameSeconds));
            RunSystem<FakeRagdollMotionSystem>();
            RunSystem<FakeRagdollPoseSystem>();
        }

        private void RunSystem<T>() where T : unmanaged, ISystem
        {
            SystemHandle handle = testWorld.GetOrCreateSystem<T>();
            handle.Update(testWorld.Unmanaged);
            testWorld.EntityManager.CompleteAllTrackedJobs();
        }

        private static void AssertMatricesEqual(float4x4 expected, float4x4 actual)
        {
            for (int column = 0; column < 4; column++)
                for (int row = 0; row < 4; row++)
                    Assert.AreEqual(expected[column][row], actual[column][row], 0.0001f);
        }
    }
}
