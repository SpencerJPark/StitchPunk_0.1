using NUnit.Framework;
using Unity.Core;
using Unity.Entities;

namespace StitchPunk.Tests
{
    [TestFixture]
    public sealed class HarvestedNodeTests
    {
        private const uint NodeKeyValue = 42;
        private const int FullAmount = 10;

        private World testWorld;
        private double elapsedSeconds;
        private Entity clockEntity;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("HarvestedNodeTests");
            elapsedSeconds = 10.0;
            testWorld.SetTime(new TimeData(elapsedSeconds, 0.5f));
            clockEntity = testWorld.EntityManager.CreateEntity(typeof(WorldClock));
            testWorld.EntityManager.SetComponentData(clockEntity, new WorldClock { secondsPerDay = 100f, totalWorldSeconds = 0.0 });
        }

        [TearDown]
        public void TearDown()
        {
            if (testWorld != null && testWorld.IsCreated) testWorld.Dispose();
            testWorld = null;
        }

        [Test]
        public void DepletedNode_StaysStumpedAcrossReload_AndRegrowsWhenDue()
        {
            Entity node = CreateFullNode();

            testWorld.EntityManager.SetComponentData(node, new ResourceNode { remaining = 0 });
            UpdateBothSystems();

            Assert.IsTrue(testWorld.EntityManager.IsComponentEnabled<HarvestedNodeStump>(node));
            Assert.AreEqual(1, GetLedger().Length);

            testWorld.EntityManager.DestroyEntity(node);
            Entity reloadedNode = CreateFullNode();
            UpdateBothSystems();

            Assert.IsTrue(testWorld.EntityManager.IsComponentEnabled<HarvestedNodeStump>(reloadedNode));
            Assert.AreEqual(0, testWorld.EntityManager.GetComponentData<ResourceNode>(reloadedNode).remaining);

            testWorld.EntityManager.SetComponentData(clockEntity, new WorldClock { secondsPerDay = 100f, totalWorldSeconds = 101.0 });
            UpdateBothSystems();

            Assert.AreEqual(FullAmount, testWorld.EntityManager.GetComponentData<ResourceNode>(reloadedNode).remaining);
            Assert.IsFalse(testWorld.EntityManager.IsComponentEnabled<HarvestedNodeStump>(reloadedNode));
            Assert.AreEqual(0, GetLedger().Length);
        }

        private Entity CreateFullNode()
        {
            Entity node = testWorld.EntityManager.CreateEntity(typeof(ResourceNode), typeof(HarvestableNodeKey), typeof(HarvestedNodeStump));
            testWorld.EntityManager.SetComponentData(node, new ResourceNode { remaining = FullAmount });
            testWorld.EntityManager.SetComponentData(node, new HarvestableNodeKey { nodeKey = NodeKeyValue, fullAmount = FullAmount, respawnAfterGameDays = 1f });
            testWorld.EntityManager.SetComponentEnabled<HarvestedNodeStump>(node, false);
            return node;
        }

        private DynamicBuffer<HarvestedNodeLedgerEntry> GetLedger()
        {
            EntityQuery ledgerQuery = testWorld.EntityManager.CreateEntityQuery(typeof(HarvestedNodeLedger));
            return testWorld.EntityManager.GetBuffer<HarvestedNodeLedgerEntry>(ledgerQuery.GetSingletonEntity());
        }

        // The restore system throttles to 1 Hz of elapsed time, so each step advances the clock past it.
        private void UpdateBothSystems()
        {
            elapsedSeconds += 2.0;
            testWorld.SetTime(new TimeData(elapsedSeconds, 0.5f));
            testWorld.GetOrCreateSystem<HarvestedNodeRecordSystem>().Update(testWorld.Unmanaged);
            testWorld.EntityManager.CompleteAllTrackedJobs();
            testWorld.GetOrCreateSystem<HarvestedNodeRestoreSystem>().Update(testWorld.Unmanaged);
            testWorld.EntityManager.CompleteAllTrackedJobs();
        }
    }
}
