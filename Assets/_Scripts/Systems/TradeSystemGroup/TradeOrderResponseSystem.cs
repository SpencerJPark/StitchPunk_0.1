using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

// Not Burst-compiled: a response for an order that is no longer Offered is logged, not silently dropped.
[UpdateInGroup(typeof(TradeSystemGroup))]
public partial struct TradeOrderResponseSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<WorldClock>();
        state.RequireForUpdate<TradeBoard>();
    }

    public void OnUpdate(ref SystemState state)
    {
        Entity boardEntity = SystemAPI.GetSingletonEntity<TradeBoard>();
        DynamicBuffer<TradeOrderResponseRequest> requests = SystemAPI.GetBuffer<TradeOrderResponseRequest>(boardEntity);
        if (requests.Length == 0)
            return;

        DynamicBuffer<TradeOrder> orders = SystemAPI.GetBuffer<TradeOrder>(boardEntity);
        int today = SystemAPI.GetSingleton<WorldClock>().dayOfWeek;

        for (int requestIndex = 0; requestIndex < requests.Length; requestIndex++)
        {
            TradeOrderResponseRequest request = requests[requestIndex];
            int orderIndex = FindOrderIndex(orders, request.orderId);
            if (orderIndex < 0 || orders[orderIndex].state != TradeOrderState.Offered)
            {
                Debug.LogWarning($"[Trade] Ignored a response to order {request.orderId}: it is missing or no longer on offer.");
                continue;
            }

            TradeOrder order = orders[orderIndex];
            if (request.accept)
            {
                order.state = TradeOrderState.Accepted;
                order.dueDayOfWeek = (byte)math.min(today + order.daysAllowed, TradeMath.DaysPerWeek - 1);
            }
            else
            {
                order.state = TradeOrderState.Declined;
            }
            orders[orderIndex] = order;
        }

        requests.Clear();
    }

    private static int FindOrderIndex(DynamicBuffer<TradeOrder> orders, int orderId)
    {
        for (int orderIndex = 0; orderIndex < orders.Length; orderIndex++)
        {
            if (orders[orderIndex].orderId == orderId)
                return orderIndex;
        }

        return -1;
    }
}
