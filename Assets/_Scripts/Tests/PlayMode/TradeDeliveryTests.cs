using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace StitchPunk.Tests.PlayMode
{
    // F3. Exactly the ordered quantity leaves, closest first, and a unit below the order's health floor does not count.
    public sealed class TradeDeliveryTests
    {
        private World testWorld;
        private EntityManager entityManager;
        private Entity boardEntity;
        private Entity playerEntity;
        private Entity dropOffEntity;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("TradeDeliveryTests");
            entityManager = testWorld.EntityManager;

            boardEntity = entityManager.CreateEntity(
                typeof(TradeBoard), typeof(TradeOrder), typeof(ClientRelationship), typeof(TradeToast));
            Entity clockEntity = entityManager.CreateEntity(typeof(WorldClock));
            entityManager.SetComponentData(clockEntity, new WorldClock { dayOfWeek = 1, weekNumber = 1 });
            playerEntity = entityManager.CreateEntity(typeof(Player), typeof(ResourceChangeRequest));

            dropOffEntity = entityManager.CreateEntity(
                typeof(TradeDropOff), typeof(TradeHandOverRequest), typeof(PlayerInteractable), typeof(LocalTransform));
            entityManager.SetComponentData(dropOffEntity, new TradeDropOff
            {
                dropOffId = 1, handOverRadius = 6f, standPoint = new float3(0f, 0f, 20f),
            });
            entityManager.SetComponentData(dropOffEntity, LocalTransform.FromPosition(float3.zero));
        }

        [TearDown]
        public void TearDown()
        {
            if (testWorld != null && testWorld.IsCreated)
                testWorld.Dispose();
            testWorld = null;
        }

        private void AddAcceptedOrder(int quantity, float minHealthFraction, int reward)
        {
            entityManager.GetBuffer<TradeOrder>(boardEntity).Add(new TradeOrder
            {
                orderId = 101, clientId = -101, dropOffId = 1, unitType = UnitType.PlayerZombieBrute,
                quantity = (byte)quantity, minHealthFraction = minHealthFraction, currencyReward = reward,
                dueDayOfWeek = 4, state = TradeOrderState.Accepted,
            });
        }

        private Entity AddBrute(float distance, int health)
        {
            Entity unit = entityManager.CreateEntity(typeof(UnitData), typeof(Health), typeof(LocalTransform), typeof(Minion), typeof(Dead));
            entityManager.SetComponentData(unit, new UnitData { unitType = UnitType.PlayerZombieBrute });
            entityManager.SetComponentData(unit, new Health { healthAmount = health, healthAmountMax = 100 });
            entityManager.SetComponentData(unit, LocalTransform.FromPosition(new float3(distance, 0f, 0f)));
            entityManager.SetComponentEnabled<Dead>(unit, false);
            return unit;
        }

        private TradeOrder RunHandOver()
        {
            testWorld.GetOrCreateSystem<TradeDeliverySystem>().Update(testWorld.Unmanaged);
            entityManager.CompleteAllTrackedJobs();
            return entityManager.GetBuffer<TradeOrder>(boardEntity)[0];
        }

        [Test]
        public void ThreeInRangeOrderWantsTwo_TakesTheClosestTwoAndPaysOnce()
        {
            AddAcceptedOrder(quantity: 2, minHealthFraction: 0f, reward: 130);
            Entity closest = AddBrute(1f, 100);
            Entity second = AddBrute(2f, 100);
            Entity third = AddBrute(3f, 100);

            TradeOrder order = RunHandOver();

            Assert.AreEqual(TradeOrderState.Delivered, order.state);
            Assert.IsTrue(entityManager.HasComponent<DeliveredToClient>(closest));
            Assert.IsTrue(entityManager.HasComponent<DeliveredToClient>(second));
            Assert.IsFalse(entityManager.HasComponent<DeliveredToClient>(third), "Only the ordered quantity leaves.");
            Assert.IsFalse(entityManager.IsComponentEnabled<Minion>(closest));
            Assert.IsTrue(entityManager.IsComponentEnabled<Minion>(third));

            DynamicBuffer<ResourceChangeRequest> ledgerRequests = entityManager.GetBuffer<ResourceChangeRequest>(playerEntity);
            Assert.AreEqual(1, ledgerRequests.Length);
            Assert.AreEqual(ResourceType.Currency, ledgerRequests[0].resourceType);
            Assert.AreEqual(130, ledgerRequests[0].value);
            Assert.AreEqual(TradeToastKind.HandOverDone, entityManager.GetComponentData<TradeToast>(boardEntity).kind);
        }

        [Test]
        public void OneUnitBelowTheHealthFloor_IsRefusedAndNothingChanges()
        {
            AddAcceptedOrder(quantity: 2, minHealthFraction: 0.75f, reward: 130);
            Entity healthy = AddBrute(1f, 100);
            Entity wounded = AddBrute(2f, 50);

            TradeOrder order = RunHandOver();

            Assert.AreEqual(TradeOrderState.Accepted, order.state);
            Assert.IsFalse(entityManager.HasComponent<DeliveredToClient>(healthy));
            Assert.IsFalse(entityManager.HasComponent<DeliveredToClient>(wounded));
            Assert.AreEqual(0, entityManager.GetBuffer<ResourceChangeRequest>(playerEntity).Length);

            TradeToast toast = entityManager.GetComponentData<TradeToast>(boardEntity);
            Assert.AreEqual(TradeToastKind.HandOverRefused, toast.kind);
            Assert.AreEqual(1, toast.amount, "One more fresh unit is needed.");
        }
    }
}
