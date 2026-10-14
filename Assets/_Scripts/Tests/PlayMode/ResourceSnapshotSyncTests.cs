using NUnit.Framework;
using Unity.Entities;

namespace StitchPunk.Tests.PlayMode
{
    public sealed class ResourceSnapshotSyncTests
    {
        private World testWorld;
        private EntityManager entityManager;
        private Entity playerEntity;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("ResourceSnapshotSyncTests");
            entityManager = testWorld.EntityManager;
            playerEntity = entityManager.CreateEntity(
                typeof(Player), typeof(ResourceStack), typeof(PlayerResourcesSnapshot));

            DynamicBuffer<ResourceStack> stacks = entityManager.GetBuffer<ResourceStack>(playerEntity);
            stacks.Add(new ResourceStack { resourceType = ResourceType.Wood, amount = 0 });
            stacks.Add(new ResourceStack { resourceType = ResourceType.ScrapMetal, amount = 0 });
            stacks.Add(new ResourceStack { resourceType = ResourceType.Corpse, amount = 0 });
            stacks.Add(new ResourceStack { resourceType = ResourceType.Electricity, amount = 0 });
            stacks.Add(new ResourceStack { resourceType = ResourceType.Currency, amount = 0 });
            stacks.Add(new ResourceStack { resourceType = ResourceType.RevivalRound, amount = 0 });
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

        private void SetStackAmount(ResourceType resourceType, int amount)
        {
            DynamicBuffer<ResourceStack> stacks = entityManager.GetBuffer<ResourceStack>(playerEntity);
            for (int rowIndex = 0; rowIndex < stacks.Length; rowIndex++)
            {
                if (stacks[rowIndex].resourceType == resourceType)
                {
                    stacks[rowIndex] = new ResourceStack { resourceType = resourceType, amount = amount };
                    return;
                }
            }
        }

        private int ReadStackAmount(ResourceType resourceType)
        {
            DynamicBuffer<ResourceStack> stacks = entityManager.GetBuffer<ResourceStack>(playerEntity);
            for (int rowIndex = 0; rowIndex < stacks.Length; rowIndex++)
            {
                if (stacks[rowIndex].resourceType == resourceType)
                {
                    return stacks[rowIndex].amount;
                }
            }
            return -1;
        }

        private void UpdateSaveSync()
        {
            SystemHandle systemHandle = testWorld.GetOrCreateSystem<ResourceSaveSyncSystem>();
            systemHandle.Update(testWorld.Unmanaged);
            entityManager.CompleteAllTrackedJobs();
        }

        private void UpdateLoadSync()
        {
            SystemHandle systemHandle = testWorld.GetOrCreateSystem<ResourceLoadSyncSystem>();
            systemHandle.Update(testWorld.Unmanaged);
            entityManager.CompleteAllTrackedJobs();
        }

        [Test]
        public void Save_CopiesEverySixStackRowIntoSnapshot()
        {
            SetStackAmount(ResourceType.Wood, 7);
            SetStackAmount(ResourceType.ScrapMetal, 8);
            SetStackAmount(ResourceType.Corpse, 9);
            SetStackAmount(ResourceType.Electricity, 10);
            SetStackAmount(ResourceType.Currency, 11);
            SetStackAmount(ResourceType.RevivalRound, 12);

            UpdateSaveSync();

            PlayerResourcesSnapshot snapshot = entityManager.GetComponentData<PlayerResourcesSnapshot>(playerEntity);
            Assert.AreEqual(7, snapshot.wood);
            Assert.AreEqual(8, snapshot.scrapMetal);
            Assert.AreEqual(9, snapshot.corpse);
            Assert.AreEqual(10, snapshot.electricity);
            Assert.AreEqual(11, snapshot.currency);
            Assert.AreEqual(12, snapshot.revivalRound);
        }

        [Test]
        public void Load_WritesSnapshotWoodIntoStackRow()
        {
            entityManager.SetComponentData(playerEntity, new PlayerResourcesSnapshot { wood = 42 });

            UpdateLoadSync();

            Assert.AreEqual(42, ReadStackAmount(ResourceType.Wood));
        }

        [Test]
        public void Load_AfterSave_LeavesStackUnchanged()
        {
            SetStackAmount(ResourceType.Wood, 7);
            UpdateSaveSync();

            UpdateLoadSync();

            Assert.AreEqual(7, ReadStackAmount(ResourceType.Wood));
        }
    }
}
