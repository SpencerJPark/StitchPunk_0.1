using DotsAnimationToolkit;
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace StitchPunk.Tests.PlayMode
{
    // Pins RG-T3's two fixes: torque about the actor's own forward (not world up, which Planar2D
    // freezes) and an impulse scaled by the root body's mass so launchForceX/Y read as velocities.
    // Both assertions fail against the pre-T3 system.
    public sealed class RagdollLaunchInitSystemTests
    {
        private World testWorld;
        private Entity unit;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("RagdollLaunchInitSystemTests");
            EntityManager entityManager = testWorld.EntityManager;

            Entity configEntity = entityManager.CreateEntity();
            entityManager.AddComponent<GameSceneTag>(configEntity);

            unit = entityManager.CreateEntity();
            entityManager.AddComponent<Dead>(unit);
            entityManager.SetComponentEnabled<Dead>(unit, true);

            entityManager.AddComponentData(unit, new Health
            {
                killSpin = 1f,
                killRagdollForce = 1f,
                killLaunchForceX = 1f,
                killLaunchForceY = 0f,
                killSourcePosition = new float3(-1f, 0f, 0f),
            });

            entityManager.AddComponentData(unit, LocalTransform.FromPositionRotation(
                float3.zero, quaternion.RotateY(math.radians(90f))));

            entityManager.AddComponent<RagdollActor>(unit);
            entityManager.SetComponentEnabled<RagdollActor>(unit, false);

            entityManager.AddComponent<RagdollLaunch>(unit);
            entityManager.SetComponentEnabled<RagdollLaunch>(unit, false);

            DynamicBuffer<RagdollBody> ragdollBodyBuffer = entityManager.AddBuffer<RagdollBody>(unit);
            ragdollBodyBuffer.Add(new RagdollBody
            {
                bodyId = default,
                node = Entity.Null,
                parentBodyIndex = -1,
                parameters = new RagdollBodyParams { invMass = 0.5f },
                state = default,
            });
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
        public void TorqueIsAboutActorForwardNotWorldUp()
        {
            testWorld.GetOrCreateSystem<RagdollLaunchInitSystem>().Update(testWorld.Unmanaged);

            EntityManager entityManager = testWorld.EntityManager;
            RagdollLaunch ragdollLaunch = entityManager.GetComponentData<RagdollLaunch>(unit);
            float3 normalizedTorque = math.normalize(ragdollLaunch.worldTorque);

            Assert.That(normalizedTorque.x, Is.EqualTo(1f).Within(1e-3f));
            Assert.That(normalizedTorque.y, Is.EqualTo(0f).Within(1e-3f));
            Assert.That(normalizedTorque.z, Is.EqualTo(0f).Within(1e-3f));

            Assert.IsTrue(entityManager.IsComponentEnabled<RagdollActor>(unit));
            Assert.IsTrue(entityManager.IsComponentEnabled<RagdollLaunch>(unit));
        }

        [Test]
        public void ImpulseIsScaledByRootBodyMass()
        {
            testWorld.GetOrCreateSystem<RagdollLaunchInitSystem>().Update(testWorld.Unmanaged);

            EntityManager entityManager = testWorld.EntityManager;
            RagdollLaunch ragdollLaunch = entityManager.GetComponentData<RagdollLaunch>(unit);

            // velocity 1 (killLaunchForceX * ragdollForce) / invMass 0.5 == mass 2 * velocity 1 == 2.
            Assert.That(ragdollLaunch.worldImpulse.x, Is.EqualTo(2f).Within(1e-4f));
        }
    }
}
