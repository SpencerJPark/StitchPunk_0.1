using NUnit.Framework;
using Unity.Entities;

namespace StitchPunk.Tests.PlayMode
{
    // F7. The clock, the board and the reputations survive a save/load; a save frame itself changes nothing.
    public sealed class TradeSaveTests
    {
        private World testWorld;
        private EntityManager entityManager;
        private Entity playerEntity;
        private Entity gameDataEntity;
        private Entity clockEntity;
        private Entity boardEntity;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("TradeSaveTests");
            entityManager = testWorld.EntityManager;

            playerEntity = entityManager.CreateEntity(typeof(Player));
            gameDataEntity = entityManager.CreateEntity(typeof(GameDataTag), typeof(SaveRequest));
            clockEntity = entityManager.CreateEntity(typeof(WorldClock));
            entityManager.SetComponentData(clockEntity, new WorldClock { weekNumber = 2, dayOfWeek = 3, timeOfDay01 = 0.4f });
            boardEntity = entityManager.CreateEntity(
                typeof(TradeBoard), typeof(TradeOrder), typeof(ClientRelationship), typeof(PendingPigeon));
            entityManager.SetComponentData(boardEntity, new TradeBoard { worldSeed = 777u });

            DynamicBuffer<TradeOrder> orders = entityManager.GetBuffer<TradeOrder>(boardEntity);
            orders.Add(new TradeOrder { orderId = 201, clientId = 1, quantity = 2, state = TradeOrderState.Accepted, dueDayOfWeek = 5, weekIssued = 2, currencyReward = 130 });
            orders.Add(new TradeOrder { orderId = 202, clientId = -202, quantity = 1, state = TradeOrderState.Offered, weekIssued = 2, currencyReward = 30 });
            entityManager.GetBuffer<ClientRelationship>(boardEntity).Add(
                new ClientRelationship { clientId = 1, reputation = 40, ordersCompleted = 3, ordersFailed = 1 });
        }

        [TearDown]
        public void TearDown()
        {
            if (testWorld != null && testWorld.IsCreated)
                testWorld.Dispose();
            testWorld = null;
        }

        private void RunSave()
        {
            testWorld.GetOrCreateSystem<TradeSaveSyncSystem>().Update(testWorld.Unmanaged);
        }

        private void RunLoad()
        {
            testWorld.GetOrCreateSystem<TradeLoadSyncSystem>().Update(testWorld.Unmanaged);
        }

        private void WipeLiveState()
        {
            entityManager.SetComponentData(clockEntity, new WorldClock { weekNumber = 1, dayOfWeek = 0, timeOfDay01 = 0.25f });
            entityManager.SetComponentData(boardEntity, new TradeBoard { worldSeed = 1u });
            entityManager.GetBuffer<TradeOrder>(boardEntity).Clear();
            entityManager.GetBuffer<ClientRelationship>(boardEntity).Clear();
        }

        [Test]
        public void TheSnapshotsSurviveTheGenericSaveEncoder()
        {
            RunSave();
            TradeBoardSnapshot saved = entityManager.GetComponentData<TradeBoardSnapshot>(playerEntity);
            EntityRecord record = SaveSerialization.WriteEntity(entityManager, playerEntity, SaveRoles.Player);

            Entity freshPlayer = entityManager.CreateEntity(typeof(WorldClockSnapshot), typeof(TradeBoardSnapshot));
            SaveSerialization.ApplyEntity(entityManager, freshPlayer, record);

            TradeBoardSnapshot restored = entityManager.GetComponentData<TradeBoardSnapshot>(freshPlayer);
            Assert.AreEqual(saved.orders.Length, restored.orders.Length);
            Assert.AreEqual(2, restored.orders.Length);
            Assert.AreEqual(201, restored.orders[0].orderId);
            Assert.AreEqual(TradeOrderState.Accepted, restored.orders[0].state);
            Assert.AreEqual(40, restored.relationships[0].reputation);
            Assert.AreEqual(777u, restored.worldSeed);
            Assert.AreEqual(2, entityManager.GetComponentData<WorldClockSnapshot>(freshPlayer).weekNumber);
        }

        [Test]
        public void ALoadBringsBackTheClockTheOrdersAndTheReputations_AndRequeuesThePigeon()
        {
            RunSave();
            RunLoad();
            Assert.AreEqual(0, entityManager.GetBuffer<PendingPigeon>(boardEntity).Length, "A save frame restores nothing, so it sends no pigeon.");

            // Now the session is replaced by the file: the live state is something else, the snapshot is what was loaded.
            WorldClockSnapshot clockSnapshot = entityManager.GetComponentData<WorldClockSnapshot>(playerEntity);
            TradeBoardSnapshot boardSnapshot = entityManager.GetComponentData<TradeBoardSnapshot>(playerEntity);
            clockSnapshot.hasData = 1;
            boardSnapshot.hasData = 1;
            entityManager.SetComponentData(playerEntity, clockSnapshot);
            entityManager.SetComponentData(playerEntity, boardSnapshot);
            WipeLiveState();

            RunLoad();

            WorldClock clock = entityManager.GetComponentData<WorldClock>(clockEntity);
            Assert.AreEqual(2, clock.weekNumber);
            Assert.AreEqual(3, clock.dayOfWeek);
            Assert.AreEqual(0.4f, clock.timeOfDay01, 0.0001f);
            Assert.AreEqual(777u, entityManager.GetComponentData<TradeBoard>(boardEntity).worldSeed);

            DynamicBuffer<TradeOrder> orders = entityManager.GetBuffer<TradeOrder>(boardEntity);
            Assert.AreEqual(2, orders.Length);
            Assert.AreEqual(TradeOrderState.Accepted, orders[0].state);
            Assert.AreEqual(5, orders[0].dueDayOfWeek);

            ClientRelationship relationship = entityManager.GetBuffer<ClientRelationship>(boardEntity)[0];
            Assert.AreEqual(40, relationship.reputation);
            Assert.AreEqual(1, relationship.ordersFailed);

            Assert.AreEqual(1, entityManager.GetBuffer<PendingPigeon>(boardEntity).Length, "Offer #202 is still unanswered, so the letter comes again.");
            Assert.AreEqual(0, entityManager.GetComponentData<TradeBoardSnapshot>(playerEntity).hasData, "The snapshot is applied once.");
        }
    }
}
