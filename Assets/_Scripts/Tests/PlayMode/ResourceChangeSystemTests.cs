using NUnit.Framework;
using Unity.Entities;

namespace StitchPunk.Tests.PlayMode
{
    public sealed class ResourceChangeSystemTests
    {
        private World testWorld;
        private EntityManager entityManager;
        private Entity playerEntity;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("ResourceChangeSystemTests");
            entityManager = testWorld.EntityManager;
            playerEntity = entityManager.CreateEntity(
                typeof(Player), typeof(ResourceStack), typeof(StorageCapacity), typeof(ResourceChangeRequest));
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

        private void AddStack(ResourceType resourceType, int amount)
        {
            entityManager.GetBuffer<ResourceStack>(playerEntity).Add(new ResourceStack { resourceType = resourceType, amount = amount });
        }

        private void AddCap(ResourceType resourceType, int cap)
        {
            entityManager.GetBuffer<StorageCapacity>(playerEntity).Add(new StorageCapacity { resourceType = resourceType, cap = cap });
        }

        private void QueueRequest(ResourceType resourceType, ResourceChangeType changeType, int value)
        {
            entityManager.GetBuffer<ResourceChangeRequest>(playerEntity).Add(
                new ResourceChangeRequest { resourceType = resourceType, changeType = changeType, value = value });
        }

        private int RunAndReadAmount(ResourceType resourceType)
        {
            SystemHandle systemHandle = testWorld.GetOrCreateSystem<ResourceChangeSystem>();
            systemHandle.Update(testWorld.Unmanaged);
            entityManager.CompleteAllTrackedJobs();

            DynamicBuffer<ResourceStack> stacks = entityManager.GetBuffer<ResourceStack>(playerEntity);
            for (int stackIndex = 0; stackIndex < stacks.Length; stackIndex++)
            {
                if (stacks[stackIndex].resourceType == resourceType)
                    return stacks[stackIndex].amount;
            }
            return -1;
        }

        [Test]
        public void Add_NegativeBeyondAmount_ClampsAtZero()
        {
            AddStack(ResourceType.Wood, 3);
            QueueRequest(ResourceType.Wood, ResourceChangeType.Add, -10);
            Assert.AreEqual(0, RunAndReadAmount(ResourceType.Wood));
        }

        [Test]
        public void Add_PastCap_ClampsAtCap()
        {
            AddStack(ResourceType.Wood, 8);
            AddCap(ResourceType.Wood, 10);
            QueueRequest(ResourceType.Wood, ResourceChangeType.Add, 50);
            Assert.AreEqual(10, RunAndReadAmount(ResourceType.Wood));
        }

        [Test]
        public void Set_ClampsToZeroAndCap()
        {
            AddStack(ResourceType.Wood, 5);
            AddCap(ResourceType.Wood, 10);
            QueueRequest(ResourceType.Wood, ResourceChangeType.Set, 99);
            Assert.AreEqual(10, RunAndReadAmount(ResourceType.Wood));

            QueueRequest(ResourceType.Wood, ResourceChangeType.Set, -4);
            Assert.AreEqual(0, RunAndReadAmount(ResourceType.Wood));
        }

        [Test]
        public void MissingCapRow_IsUncapped()
        {
            AddStack(ResourceType.ScrapMetal, 0);
            QueueRequest(ResourceType.ScrapMetal, ResourceChangeType.Add, 100000);
            Assert.AreEqual(100000, RunAndReadAmount(ResourceType.ScrapMetal));
        }

        [Test]
        public void RequestsBuffer_IsEmptyAfterUpdate()
        {
            AddStack(ResourceType.Wood, 0);
            QueueRequest(ResourceType.Wood, ResourceChangeType.Add, 1);
            QueueRequest(ResourceType.Corpse, ResourceChangeType.Add, 1);
            RunAndReadAmount(ResourceType.Wood);
            Assert.AreEqual(0, entityManager.GetBuffer<ResourceChangeRequest>(playerEntity).Length);
        }
    }
}
