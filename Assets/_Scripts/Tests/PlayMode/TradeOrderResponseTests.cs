using NUnit.Framework;
using Unity.Entities;

namespace StitchPunk.Tests.PlayMode
{
    // F5. The due-day clamp and the "only Offered orders can be answered" guard are the two real rules in TradeOrderResponseSystem.
    public sealed class TradeOrderResponseTests
    {
        private World testWorld;
        private EntityManager entityManager;
        private Entity boardEntity;
        private Entity clockEntity;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("TradeOrderResponseTests");
            entityManager = testWorld.EntityManager;
            boardEntity = entityManager.CreateEntity(
                typeof(TradeBoard), typeof(TradeOrder), typeof(TradeOrderResponseRequest));
            clockEntity = entityManager.CreateEntity(typeof(WorldClock));
        }

        [TearDown]
        public void TearDown()
        {
            if (testWorld != null && testWorld.IsCreated)
                testWorld.Dispose();
            testWorld = null;
        }

        private void AddOrder(int orderId, TradeOrderState state, byte daysAllowed)
        {
            entityManager.GetBuffer<TradeOrder>(boardEntity).Add(
                new TradeOrder { orderId = orderId, state = state, daysAllowed = daysAllowed });
        }

        private TradeOrder Respond(int orderId, bool accept, int today)
        {
            entityManager.SetComponentData(clockEntity, new WorldClock { dayOfWeek = today, weekNumber = 1 });
            entityManager.GetBuffer<TradeOrderResponseRequest>(boardEntity).Add(
                new TradeOrderResponseRequest { orderId = orderId, accept = accept });

            testWorld.GetOrCreateSystem<TradeOrderResponseSystem>().Update(testWorld.Unmanaged);
            entityManager.CompleteAllTrackedJobs();

            DynamicBuffer<TradeOrder> orders = entityManager.GetBuffer<TradeOrder>(boardEntity);
            for (int orderIndex = 0; orderIndex < orders.Length; orderIndex++)
            {
                if (orders[orderIndex].orderId == orderId)
                    return orders[orderIndex];
            }
            return default;
        }

        [Test]
        public void Accept_SetsDueDayFromTodayAndClampsToSunday()
        {
            AddOrder(1, TradeOrderState.Offered, 5);

            TradeOrder order = Respond(1, true, 4);

            Assert.AreEqual(TradeOrderState.Accepted, order.state);
            Assert.AreEqual(6, order.dueDayOfWeek, "Thursday + 5 days would overrun the week; the week end is the limit.");
        }

        [Test]
        public void Decline_MarksDeclinedAndAnsweredOrdersAreLeftAlone()
        {
            AddOrder(1, TradeOrderState.Offered, 3);
            AddOrder(2, TradeOrderState.Declined, 3);

            Assert.AreEqual(TradeOrderState.Declined, Respond(1, false, 0).state);
            Assert.AreEqual(TradeOrderState.Declined, Respond(2, true, 0).state, "A declined order cannot be accepted afterwards.");
        }
    }
}
