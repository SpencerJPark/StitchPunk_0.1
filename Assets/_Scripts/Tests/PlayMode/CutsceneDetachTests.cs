using DotsAnimationToolkit;
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace StitchPunk.Tests.PlayMode
{
    // G2-P4: a detach signal becomes a throw on an item and nothing but a cleared signal on a unit.
    public sealed class CutsceneDetachTests
    {
        private World testWorld;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("CutsceneDetachTests");
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
        public void DetachSignal_BecomesAThrownItemRequestOnItems()
        {
            EntityManager entityManager = testWorld.EntityManager;
            float3 releasePosition = new float3(4f, 1.5f, -2f);
            float3 worldImpulse    = new float3(6f, 0f, 3f);
            Entity previousHost    = entityManager.CreateEntity();

            Entity itemEntity = CreateDetachedThing(entityManager, releasePosition, worldImpulse, previousHost);
            entityManager.AddComponent<ThrownItemRequest>(itemEntity);
            entityManager.SetComponentEnabled<ThrownItemRequest>(itemEntity, false);

            Entity unitEntity = CreateDetachedThing(entityManager, releasePosition, worldImpulse, previousHost);

            RunSystem<CutsceneDetachSystem>();

            Assert.IsTrue(entityManager.IsComponentEnabled<ThrownItemRequest>(itemEntity),
                "An item let go by a cutscene is thrown through the pipeline a hand throw already uses.");
            ThrownItemRequest thrownItem = entityManager.GetComponentData<ThrownItemRequest>(itemEntity);
            Assert.AreEqual(worldImpulse, thrownItem.velocity, "The toolkit's impulse is the throw velocity.");
            Assert.AreEqual(previousHost, thrownItem.thrower, "Whatever it was riding is credited with the throw.");
            Assert.AreEqual(releasePosition, thrownItem.throwOrigin);

            Assert.IsFalse(entityManager.IsComponentEnabled<CutsceneDetachSignal>(itemEntity),
                "The signal is a one-frame hand-over; the host consumes it.");
            Assert.IsFalse(entityManager.IsComponentEnabled<CutsceneDetachSignal>(unitEntity),
                "A unit's signal is consumed too — it is simply placed, never launched.");
            Assert.IsFalse(entityManager.HasComponent<ThrownItemRequest>(unitEntity),
                "Nothing turns a detached unit into a thrown object.");
        }

        private static Entity CreateDetachedThing(
            EntityManager entityManager, float3 position, float3 worldImpulse, Entity previousHost)
        {
            Entity detachedEntity = entityManager.CreateEntity();
            entityManager.AddComponentData(detachedEntity, new LocalTransform
            {
                Position = position,
                Rotation = quaternion.identity,
                Scale    = 1f,
            });
            entityManager.AddComponentData(detachedEntity, new CutsceneDetachSignal
            {
                worldImpulse = worldImpulse,
                previousHost = previousHost,
            });
            entityManager.SetComponentEnabled<CutsceneDetachSignal>(detachedEntity, true);
            return detachedEntity;
        }

        private void RunSystem<TSystem>() where TSystem : unmanaged, ISystem
        {
            SystemHandle systemHandle = testWorld.GetOrCreateSystem<TSystem>();
            systemHandle.Update(testWorld.Unmanaged);
            testWorld.EntityManager.CompleteAllTrackedJobs();
        }
    }
}
