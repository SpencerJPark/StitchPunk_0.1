using NUnit.Framework;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace StitchPunk.Tests.PlayMode
{
    public sealed class WorkstationMountTests
    {
        private World testWorld;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("WorkstationMountTests");
            testWorld.SetTime(new TimeData(1.0, 0.016f));
        }

        [TearDown]
        public void TearDown()
        {
            if (testWorld != null && testWorld.IsCreated) testWorld.Dispose();
            testWorld = null;
        }

        [Test]
        public void InteractOnOwnStation_Mounts_ConsumesInteract_AndSnapsPlayerToMountPoint()
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity player = CreatePlayer();
            Entity station = CreateStation(player, new float3(10f, 0f, 5f));
            LinkTarget(player, station);

            RunMountSystem();

            Assert.IsTrue(entityManager.IsComponentEnabled<WorkstationMounted>(player));
            Assert.AreEqual(station, entityManager.GetComponentData<WorkstationMounted>(player).workstation);
            Assert.AreEqual(50, entityManager.GetComponentData<WorkstationMounted>(player).healthWhenMounted);
            Assert.IsFalse(entityManager.IsComponentEnabled<OnInteractPlayerInput>(player));
            Assert.AreEqual(new float3(10f, 0f, 4f), entityManager.GetComponentData<LocalTransform>(player).Position);
        }

        [Test]
        public void Mounted_WithDismountRequest_Unmounts_AndClearsRequest()
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity player = CreatePlayer();
            Entity station = CreateStation(player, float3.zero);
            LinkTarget(player, station);
            RunMountSystem();
            Assert.IsTrue(entityManager.IsComponentEnabled<WorkstationMounted>(player));

            entityManager.SetComponentEnabled<WorkstationDismountRequest>(player, true);
            RunMountSystem();

            Assert.IsFalse(entityManager.IsComponentEnabled<WorkstationMounted>(player));
            Assert.IsFalse(entityManager.IsComponentEnabled<WorkstationDismountRequest>(player));
        }

        [Test]
        public void Mounted_WhenPlayerHealthDropsBelowMountedHealth_Unmounts()
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity player = CreatePlayer();
            Entity station = CreateStation(player, float3.zero);
            LinkTarget(player, station);
            RunMountSystem();

            entityManager.SetComponentData(player, new Health { healthAmount = 49, healthAmountMax = 50 });
            RunMountSystem();

            Assert.IsFalse(entityManager.IsComponentEnabled<WorkstationMounted>(player));
        }

        [Test]
        public void InteractOnStationOwnedByAnotherEntity_DoesNotMount()
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity player = CreatePlayer();
            Entity otherOwner = entityManager.CreateEntity();
            Entity station = CreateStation(otherOwner, float3.zero);
            LinkTarget(player, station);

            RunMountSystem();

            Assert.IsFalse(entityManager.IsComponentEnabled<WorkstationMounted>(player));
            Assert.IsTrue(entityManager.IsComponentEnabled<OnInteractPlayerInput>(player));
        }

        private void RunMountSystem()
        {
            SystemHandle mountHandle = testWorld.GetOrCreateSystem<WorkstationMountSystem>();
            mountHandle.Update(testWorld.Unmanaged);
            testWorld.EntityManager.CompleteAllTrackedJobs();
        }

        private Entity CreatePlayer()
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity player = entityManager.CreateEntity(
                typeof(Player), typeof(LocalTransform), typeof(Target), typeof(OnInteractPlayerInput),
                typeof(PlayerWorkstationPlacement), typeof(WorkstationMounted),
                typeof(WorkstationDismountRequest), typeof(Health));
            entityManager.SetComponentData(player, LocalTransform.Identity);
            entityManager.SetComponentData(player, new Health { healthAmount = 50, healthAmountMax = 50 });
            entityManager.SetComponentEnabled<Target>(player, true);
            entityManager.SetComponentEnabled<OnInteractPlayerInput>(player, true);
            entityManager.SetComponentEnabled<PlayerWorkstationPlacement>(player, false);
            entityManager.SetComponentEnabled<WorkstationMounted>(player, false);
            entityManager.SetComponentEnabled<WorkstationDismountRequest>(player, false);
            return player;
        }

        private Entity CreateStation(Entity ownerPlayer, float3 position)
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity station = entityManager.CreateEntity(typeof(Workstation), typeof(LocalTransform));
            entityManager.SetComponentData(station, new Workstation
            {
                ownerPlayer = ownerPlayer,
                mountLocalOffset = new float3(0f, 0f, -1f),
            });
            entityManager.SetComponentData(station, LocalTransform.FromPosition(position));
            return station;
        }

        private void LinkTarget(Entity player, Entity station)
        {
            testWorld.EntityManager.SetComponentData(player, new Target { entity = station });
        }
    }
}
