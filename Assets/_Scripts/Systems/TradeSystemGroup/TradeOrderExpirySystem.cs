using Unity.Entities;
using Unity.Mathematics;

// On each new day, an accepted order past its due day fails and costs the client reputation (Trade_System.md TR-D4).
// Only Accepted orders can fail: an Offered order expires silently at the weekly roll, a Declined one never mattered.
[UpdateInGroup(typeof(TradeSystemGroup))]
public partial struct TradeOrderExpirySystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<TradeBoard>();
        state.RequireForUpdate<WorldClock>();
    }

    public void OnUpdate(ref SystemState state)
    {
        Entity clockEntity = SystemAPI.GetSingletonEntity<WorldClock>();
        if (!SystemAPI.IsComponentEnabled<DayStarted>(clockEntity))
            return;

        WorldClock clock = SystemAPI.GetComponent<WorldClock>(clockEntity);
        Entity boardEntity = SystemAPI.GetSingletonEntity<TradeBoard>();
        DynamicBuffer<TradeOrder> orders = SystemAPI.GetBuffer<TradeOrder>(boardEntity);
        DynamicBuffer<ClientRelationship> relationships = SystemAPI.GetBuffer<ClientRelationship>(boardEntity);

        int reputationLoss = 0;
        if (SystemAPI.TryGetSingleton(out ClientLibrary clientLibrary) && clientLibrary.library.IsCreated)
            reputationLoss = clientLibrary.library.Value.tuning.reputationLossFailed;

        int firstFailedOrderId = 0;
        int firstReputationLost = 0;
        for (int orderIndex = 0; orderIndex < orders.Length; orderIndex++)
        {
            TradeOrder order = orders[orderIndex];
            if (order.state != TradeOrderState.Accepted
                || !TradeMath.IsOrderOverdue(order.weekIssued, order.dueDayOfWeek, clock.weekNumber, clock.dayOfWeek))
                continue;

            order.state = TradeOrderState.Failed;
            orders[orderIndex] = order;
            int reputationLost = order.clientId >= 0 ? ApplyFailure(relationships, order.clientId, reputationLoss) : 0;
            if (firstFailedOrderId == 0)
            {
                firstFailedOrderId = order.orderId;
                firstReputationLost = reputationLost;
            }
        }

        if (firstFailedOrderId == 0)
            return;

        TradeToast toast = SystemAPI.GetComponent<TradeToast>(boardEntity);
        toast.sequence++;
        toast.kind = TradeToastKind.OrderFailed;
        toast.orderId = firstFailedOrderId;
        toast.amount = firstReputationLost;
        SystemAPI.SetComponent(boardEntity, toast);
    }

    // Returns the reputation actually lost (it stops at zero).
    private static int ApplyFailure(DynamicBuffer<ClientRelationship> relationships, int clientId, int reputationLoss)
    {
        for (int relationshipIndex = 0; relationshipIndex < relationships.Length; relationshipIndex++)
        {
            if (relationships[relationshipIndex].clientId != clientId)
                continue;

            ClientRelationship relationship = relationships[relationshipIndex];
            int reputationBefore = relationship.reputation;
            relationship.reputation = math.max(0, relationship.reputation - reputationLoss);
            relationship.ordersFailed++;
            relationships[relationshipIndex] = relationship;
            return reputationBefore - relationship.reputation;
        }

        return 0;
    }
}
