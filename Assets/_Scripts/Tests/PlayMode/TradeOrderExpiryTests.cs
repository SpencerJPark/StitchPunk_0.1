using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;

namespace StitchPunk.Tests.PlayMode
{
    // F4. An accepted order past its due day fails and costs reputation; nothing else is touched.
    public sealed class TradeOrderExpiryTests
    {
        private World testWorld;
        private EntityManager entityManager;
        private Entity boardEntity;
        private BlobAssetReference<ClientLibraryBlob> libraryBlob;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("TradeOrderExpiryTests");
            entityManager = testWorld.EntityManager;

            using (BlobBuilder builder = new BlobBuilder(Allocator.Temp))
            {
                ref ClientLibraryBlob root = ref builder.ConstructRoot<ClientLibraryBlob>();
                builder.Allocate(ref root.clients, 0);
                builder.Allocate(ref root.household.templates, 0);
                root.tuning = new TradeTuningBlob { reputationLossFailed = 15 };
                libraryBlob = builder.CreateBlobAssetReference<ClientLibraryBlob>(Allocator.Persistent);
            }

            boardEntity = entityManager.CreateEntity(
                typeof(TradeBoard), typeof(TradeOrder), typeof(ClientRelationship), typeof(TradeToast), typeof(ClientLibrary));
            entityManager.SetComponentData(boardEntity, new ClientLibrary { library = libraryBlob });
            entityManager.GetBuffer<ClientRelationship>(boardEntity).Add(new ClientRelationship { clientId = 1, reputation = 40 });

            // Thursday: Wednesday (day 2) is now past.
            Entity clockEntity = entityManager.CreateEntity(typeof(WorldClock), typeof(DayStarted));
            entityManager.SetComponentData(clockEntity, new WorldClock { dayOfWeek = 3, weekNumber = 1 });
        }

        [TearDown]
        public void TearDown()
        {
            if (testWorld != null && testWorld.IsCreated)
                testWorld.Dispose();
            testWorld = null;
            if (libraryBlob.IsCreated)
                libraryBlob.Dispose();
        }

        private void AddOrder(int orderId, int clientId, TradeOrderState state, byte dueDayOfWeek)
        {
            entityManager.GetBuffer<TradeOrder>(boardEntity).Add(new TradeOrder
            {
                orderId = orderId, clientId = clientId, state = state, dueDayOfWeek = dueDayOfWeek, weekIssued = 1,
            });
        }

        private DynamicBuffer<TradeOrder> RunDayStarted()
        {
            testWorld.GetOrCreateSystem<TradeOrderExpirySystem>().Update(testWorld.Unmanaged);
            entityManager.CompleteAllTrackedJobs();
            return entityManager.GetBuffer<TradeOrder>(boardEntity);
        }

        [Test]
        public void AcceptedPastDue_FailsAndCostsReputation_OthersAreUntouched()
        {
            AddOrder(1, 1, TradeOrderState.Accepted, 2);   // due Wed, today Thu: fails
            AddOrder(2, 1, TradeOrderState.Declined, 2);   // answered long ago: untouched
            AddOrder(3, 1, TradeOrderState.Accepted, 3);   // due today: still open
            AddOrder(4, -4, TradeOrderState.Accepted, 2);  // household one-off: fails, no relationship

            DynamicBuffer<TradeOrder> orders = RunDayStarted();

            Assert.AreEqual(TradeOrderState.Failed, orders[0].state);
            Assert.AreEqual(TradeOrderState.Declined, orders[1].state, "Only accepted orders can fail.");
            Assert.AreEqual(TradeOrderState.Accepted, orders[2].state, "An order due today is not late until tomorrow.");
            Assert.AreEqual(TradeOrderState.Failed, orders[3].state);

            ClientRelationship relationship = entityManager.GetBuffer<ClientRelationship>(boardEntity)[0];
            Assert.AreEqual(25, relationship.reputation, "40 - 15, once: the household order costs nobody.");
            Assert.AreEqual(1, relationship.ordersFailed);
            Assert.AreEqual(TradeToastKind.OrderFailed, entityManager.GetComponentData<TradeToast>(boardEntity).kind);
        }

        [Test]
        public void ReputationStopsAtZero()
        {
            DynamicBuffer<ClientRelationship> relationships = entityManager.GetBuffer<ClientRelationship>(boardEntity);
            relationships[0] = new ClientRelationship { clientId = 1, reputation = 6 };
            AddOrder(1, 1, TradeOrderState.Accepted, 2);

            RunDayStarted();

            Assert.AreEqual(0, entityManager.GetBuffer<ClientRelationship>(boardEntity)[0].reputation);
            Assert.AreEqual(6, entityManager.GetComponentData<TradeToast>(boardEntity).amount, "The toast reports what was really lost.");
        }
    }
}
