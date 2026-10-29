using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace StitchPunk.Tests.PlayMode
{
    // F6. A delivered unit goes only when the player is far AND it is outside the camera's view circle.
    public sealed class DeliveredUnitDespawnTests
    {
        private World testWorld;
        private EntityManager entityManager;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("DeliveredUnitDespawnTests");
            entityManager = testWorld.EntityManager;
            testWorld.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>();

            Entity playerEntity = entityManager.CreateEntity(typeof(Player), typeof(LocalTransform));
            entityManager.SetComponentData(playerEntity, LocalTransform.FromPosition(float3.zero));
            Entity cameraEntity = entityManager.CreateEntity(typeof(CameraView));
            entityManager.SetComponentData(cameraEntity, new CameraView { center = float3.zero, viewRadius = 30f });
        }

        [TearDown]
        public void TearDown()
        {
            if (testWorld != null && testWorld.IsCreated)
                testWorld.Dispose();
            testWorld = null;
        }

        private Entity AddDeliveredUnit(float3 position, float secondsAtClient)
        {
            Entity unit = entityManager.CreateEntity(typeof(DeliveredToClient), typeof(LocalTransform), typeof(Despawn), typeof(Dead));
            entityManager.SetComponentData(unit, new DeliveredToClient { secondsSinceHandOver = secondsAtClient });
            entityManager.SetComponentData(unit, LocalTransform.FromPosition(position));
            entityManager.SetComponentEnabled<Despawn>(unit, false);
            entityManager.SetComponentEnabled<Dead>(unit, false);
            return unit;
        }

        private void RunDespawnCheck(float3 cameraCenter)
        {
            Entity cameraEntity = entityManager.CreateEntityQuery(typeof(CameraView)).GetSingletonEntity();
            entityManager.SetComponentData(cameraEntity, new CameraView { center = cameraCenter, viewRadius = 30f });

            testWorld.GetOrCreateSystem<DeliveredUnitDespawnSystem>().Update(testWorld.Unmanaged);
            testWorld.GetExistingSystemManaged<EndSimulationEntityCommandBufferSystem>().Update();
            entityManager.CompleteAllTrackedJobs();
        }

        [Test]
        public void FarFromPlayerButStillInView_StaysPut()
        {
            Entity unit = AddDeliveredUnit(new float3(100f, 0f, 0f), 10f);

            RunDespawnCheck(cameraCenter: new float3(100f, 0f, 0f));

            Assert.IsFalse(entityManager.IsComponentEnabled<Despawn>(unit));
            Assert.IsTrue(entityManager.HasComponent<DeliveredToClient>(unit));
        }

        [Test]
        public void FarFromPlayerAndOutOfView_IsDespawnedAndLosesTheMarker()
        {
            Entity unit = AddDeliveredUnit(new float3(100f, 0f, 0f), 10f);

            RunDespawnCheck(cameraCenter: float3.zero);

            Assert.IsTrue(entityManager.IsComponentEnabled<Despawn>(unit));
            Assert.IsFalse(entityManager.HasComponent<DeliveredToClient>(unit), "A pooled unit must not return carrying the marker.");
        }
    }
}
