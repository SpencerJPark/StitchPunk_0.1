using NUnit.Framework;
using Unity.Core;
using Unity.Entities;

namespace StitchPunk.Tests.PlayMode
{
    public sealed class WorkstationFurnaceCraftTests
    {
        private World testWorld;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("WorkstationFurnaceCraftTests");
            testWorld.SetTime(new TimeData(10.0, 5f));
        }

        [TearDown]
        public void TearDown()
        {
            if (testWorld != null && testWorld.IsCreated) testWorld.Dispose();
            testWorld = null;
        }

        [Test]
        public void BurningFurnace_WithWood_QueuesWoodSpendAndElectricityGain()
        {
            Entity player = CreatePlayer(wood: 3, scrapMetal: 0, electricity: 0);
            CreateStation(player);

            RunSystem<WorkstationFurnaceSystem>();

            DynamicBuffer<ResourceChangeRequest> requests = testWorld.EntityManager.GetBuffer<ResourceChangeRequest>(player);
            Assert.AreEqual(-1, SumRequests(requests, ResourceType.Wood));
            Assert.AreEqual(2, SumRequests(requests, ResourceType.Electricity));
        }

        [Test]
        public void BurningFurnace_WithoutWood_QueuesNothing()
        {
            Entity player = CreatePlayer(wood: 0, scrapMetal: 0, electricity: 0);
            CreateStation(player);

            RunSystem<WorkstationFurnaceSystem>();

            Assert.AreEqual(0, testWorld.EntityManager.GetBuffer<ResourceChangeRequest>(player).Length);
        }

        [Test]
        public void CraftTwoRounds_WithOnlyOneAffordable_CraftsOneAndStampsScrapShortfall()
        {
            Entity player = CreatePlayer(wood: 0, scrapMetal: 1, electricity: 5);
            Entity station = CreateStation(player);
            testWorld.EntityManager.SetComponentData(station, new CraftRevivalRoundRequest { count = 2 });
            testWorld.EntityManager.SetComponentEnabled<CraftRevivalRoundRequest>(station, true);

            RunSystem<WorkstationCraftSystem>();

            EntityManager entityManager = testWorld.EntityManager;
            DynamicBuffer<ResourceChangeRequest> requests = entityManager.GetBuffer<ResourceChangeRequest>(player);
            Assert.AreEqual(1, SumRequests(requests, ResourceType.RevivalRound));
            Assert.AreEqual(-1, SumRequests(requests, ResourceType.ScrapMetal));
            Assert.AreEqual(-1, SumRequests(requests, ResourceType.Electricity));
            Assert.IsTrue(entityManager.IsComponentEnabled<ResourceShortfall>(player));
            Assert.AreEqual(ResourceType.ScrapMetal, entityManager.GetComponentData<ResourceShortfall>(player).resourceType);
            Assert.IsFalse(entityManager.IsComponentEnabled<CraftRevivalRoundRequest>(station));
        }

        private void RunSystem<T>() where T : unmanaged, ISystem
        {
            SystemHandle handle = testWorld.GetOrCreateSystem<T>();
            handle.Update(testWorld.Unmanaged);
            testWorld.EntityManager.CompleteAllTrackedJobs();
        }

        private static int SumRequests(DynamicBuffer<ResourceChangeRequest> requests, ResourceType resourceType)
        {
            int total = 0;
            for (int index = 0; index < requests.Length; index++)
            {
                if (requests[index].resourceType == resourceType) total += requests[index].value;
            }
            return total;
        }

        private Entity CreatePlayer(int wood, int scrapMetal, int electricity)
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity player = entityManager.CreateEntity(
                typeof(Player), typeof(ResourceStack), typeof(ResourceChangeRequest), typeof(ResourceShortfall));
            entityManager.SetComponentEnabled<ResourceShortfall>(player, false);
            DynamicBuffer<ResourceStack> stacks = entityManager.GetBuffer<ResourceStack>(player);
            stacks.Add(new ResourceStack { resourceType = ResourceType.Wood, amount = wood });
            stacks.Add(new ResourceStack { resourceType = ResourceType.ScrapMetal, amount = scrapMetal });
            stacks.Add(new ResourceStack { resourceType = ResourceType.Corpse, amount = 0 });
            stacks.Add(new ResourceStack { resourceType = ResourceType.Electricity, amount = electricity });
            stacks.Add(new ResourceStack { resourceType = ResourceType.Currency, amount = 0 });
            stacks.Add(new ResourceStack { resourceType = ResourceType.RevivalRound, amount = 0 });
            return player;
        }

        private Entity CreateStation(Entity ownerPlayer)
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity station = entityManager.CreateEntity(
                typeof(Workstation), typeof(WorkstationFurnace), typeof(WorkstationFurnaceToggleRequest),
                typeof(WorkstationRecipes), typeof(CraftRevivalRoundRequest));
            entityManager.SetComponentData(station, new Workstation { ownerPlayer = ownerPlayer });
            entityManager.SetComponentData(station, new WorkstationFurnace
            {
                isBurning = true,
                burnIntervalSeconds = 5f,
                electricityPerWood = 2,
            });
            entityManager.SetComponentData(station, new WorkstationRecipes
            {
                revivalRoundScrapCost = 1,
                revivalRoundElectricityCost = 1,
                repairWoodCost = 2,
                repairHealthAmount = 25,
            });
            entityManager.SetComponentEnabled<WorkstationFurnaceToggleRequest>(station, false);
            entityManager.SetComponentEnabled<CraftRevivalRoundRequest>(station, false);
            return station;
        }
    }
}
