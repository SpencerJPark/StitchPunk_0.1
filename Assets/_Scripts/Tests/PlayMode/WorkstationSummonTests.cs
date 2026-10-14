using NUnit.Framework;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace StitchPunk.Tests.PlayMode
{
    public sealed class WorkstationSummonTests
    {
        private World testWorld;
        private BlobAssetReference<UnitLibraryBlob> unitLibraryBlob;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("WorkstationSummonTests");
            unitLibraryBlob = BuildLibrary();
            testWorld.EntityManager.AddComponentData(
                testWorld.EntityManager.CreateEntity(), new UnitDataLibrary { library = unitLibraryBlob });
            testWorld.SetTime(new TimeData(1.0, 0.016f));
        }

        [TearDown]
        public void TearDown()
        {
            if (testWorld != null && testWorld.IsCreated) testWorld.Dispose();
            testWorld = null;
            if (unitLibraryBlob.IsCreated) unitLibraryBlob.Dispose();
        }

        [Test]
        public void Request_WithEnoughStock_QueuesThreeSpendsAndOneBuild()
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity player = CreatePlayer(1, 1, 1);
            Entity station = CreateStation(player);
            AddRequest(station, UnitType.PlayerZombie);

            RunSummonSystem();

            DynamicBuffer<ResourceChangeRequest> changes = entityManager.GetBuffer<ResourceChangeRequest>(player);
            Assert.AreEqual(3, changes.Length);
            for (int changeIndex = 0; changeIndex < changes.Length; changeIndex++)
                Assert.AreEqual(-1, changes[changeIndex].value);
            Assert.AreEqual(ResourceType.Corpse, changes[0].resourceType);
            Assert.AreEqual(ResourceType.ScrapMetal, changes[1].resourceType);
            Assert.AreEqual(ResourceType.Electricity, changes[2].resourceType);

            DynamicBuffer<WorkstationSummonQueueEntry> queue = entityManager.GetBuffer<WorkstationSummonQueueEntry>(station);
            Assert.AreEqual(1, queue.Length);
            Assert.AreEqual(UnitType.PlayerZombie, queue[0].unitType);
            Assert.AreEqual(4f, queue[0].totalSeconds);
            Assert.AreEqual(0, entityManager.GetBuffer<WorkstationSummonRequest>(station).Length);
        }

        [Test]
        public void QueuedBuild_PastBuildSeconds_ProducesPendingSpawnAtOffset()
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity player = CreatePlayer(1, 1, 1);
            Entity station = CreateStation(player);
            AddRequest(station, UnitType.PlayerZombie);
            RunSummonSystem();

            testWorld.SetTime(new TimeData(2.0, 5f));
            RunSummonSystem();

            DynamicBuffer<PendingMinionSpawn> spawns = entityManager.GetBuffer<PendingMinionSpawn>(station);
            Assert.AreEqual(1, spawns.Length);
            Assert.AreEqual(UnitType.PlayerZombie, spawns[0].unitType);
            Assert.AreEqual(0f, spawns[0].position.x, 0.001f);
            Assert.AreEqual(2f, spawns[0].position.z, 0.001f);
            Assert.AreEqual(0, entityManager.GetBuffer<WorkstationSummonQueueEntry>(station).Length);
        }

        [Test]
        public void Request_WithNoScrapMetal_QueuesNothingAndStampsShortfall()
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity player = CreatePlayer(1, 0, 1);
            Entity station = CreateStation(player);
            AddRequest(station, UnitType.PlayerZombie);

            RunSummonSystem();

            Assert.AreEqual(0, entityManager.GetBuffer<WorkstationSummonQueueEntry>(station).Length);
            Assert.AreEqual(0, entityManager.GetBuffer<ResourceChangeRequest>(player).Length);
            Assert.IsTrue(entityManager.IsComponentEnabled<ResourceShortfall>(player));
            Assert.AreEqual(ResourceType.ScrapMetal, entityManager.GetComponentData<ResourceShortfall>(player).resourceType);
        }

        [Test]
        public void Request_ForUnitTypeNotOffered_IsIgnored()
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity player = CreatePlayer(1, 1, 1);
            Entity station = CreateStation(player);
            AddRequest(station, UnitType.PlayerZombieBrute);

            RunSummonSystem();

            Assert.AreEqual(0, entityManager.GetBuffer<WorkstationSummonQueueEntry>(station).Length);
            Assert.AreEqual(0, entityManager.GetBuffer<ResourceChangeRequest>(player).Length);
        }

        private void RunSummonSystem()
        {
            SystemHandle summonHandle = testWorld.GetOrCreateSystem<WorkstationSummonSystem>();
            summonHandle.Update(testWorld.Unmanaged);
            testWorld.EntityManager.CompleteAllTrackedJobs();
        }

        private void AddRequest(Entity station, UnitType unitType)
        {
            testWorld.EntityManager.GetBuffer<WorkstationSummonRequest>(station).Add(
                new WorkstationSummonRequest { unitType = unitType });
        }

        private Entity CreatePlayer(int corpses, int scrapMetal, int electricity)
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity player = entityManager.CreateEntity(
                typeof(Player), typeof(ResourceStack), typeof(ResourceChangeRequest), typeof(ResourceShortfall));
            entityManager.SetComponentEnabled<ResourceShortfall>(player, false);
            DynamicBuffer<ResourceStack> stack = entityManager.GetBuffer<ResourceStack>(player);
            stack.Add(new ResourceStack { resourceType = ResourceType.Wood, amount = 0 });
            stack.Add(new ResourceStack { resourceType = ResourceType.ScrapMetal, amount = scrapMetal });
            stack.Add(new ResourceStack { resourceType = ResourceType.Corpse, amount = corpses });
            stack.Add(new ResourceStack { resourceType = ResourceType.Electricity, amount = electricity });
            stack.Add(new ResourceStack { resourceType = ResourceType.Currency, amount = 0 });
            stack.Add(new ResourceStack { resourceType = ResourceType.RevivalRound, amount = 0 });
            return player;
        }

        private Entity CreateStation(Entity ownerPlayer)
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity station = entityManager.CreateEntity(
                typeof(Workstation), typeof(LocalTransform), typeof(WorkstationSummonOption),
                typeof(WorkstationSummonRequest), typeof(WorkstationSummonQueueEntry), typeof(PendingMinionSpawn));
            entityManager.SetComponentData(station, new Workstation
            {
                ownerPlayer = ownerPlayer,
                summonSpawnLocalOffset = new float3(0f, 0f, 2f)
            });
            entityManager.SetComponentData(station, LocalTransform.Identity);
            entityManager.GetBuffer<WorkstationSummonOption>(station).Add(
                new WorkstationSummonOption { unitType = UnitType.PlayerZombie, buildSeconds = 4f });
            return station;
        }

        private static BlobAssetReference<UnitLibraryBlob> BuildLibrary()
        {
            using BlobBuilder builder = new BlobBuilder(Allocator.Temp);
            ref UnitLibraryBlob root = ref builder.ConstructRoot<UnitLibraryBlob>();
            BlobBuilderArray<UnitDataBlob> unitsBuilder = builder.Allocate(ref root.units, 1);
            ref UnitDataBlob unit = ref unitsBuilder[0];
            unit.unitType = UnitType.PlayerZombie;
            unit.summonCost = new SummonCostBlob { corpses = 1, scrapMetal = 1, electricity = 1 };
            builder.Allocate(ref unit.motivation, 0);
            builder.Allocate(ref unit.randomMotivations, 0);
            builder.Allocate(ref unit.attackFactions, 0);
            builder.Allocate(ref unit.socialFactions, 0);
            builder.Allocate(ref unit.attacks, 0);
            builder.Allocate(ref unit.actionAnimationKeys, 0);
            builder.Allocate(ref unit.faceAnimationKeys, 0);
            builder.Allocate(ref unit.stanceAnimationKeys, 0);
            return builder.CreateBlobAssetReference<UnitLibraryBlob>(Allocator.Persistent);
        }
    }
}
