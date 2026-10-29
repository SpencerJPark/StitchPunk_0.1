using DotsMovementToolkit;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Answers a TradeHandOverRequest: finds the player's minions in range that fit each accepted order at this drop-off, takes exactly
// the ordered quantity (closest first), pays, raises reputation and releases the units to the client (Trade_System.md TR-A5/A6).
// Not Burst-compiled: it makes structural changes and calls the EntityManager-based HordeUtils.
[UpdateInGroup(typeof(TradeSystemGroup))]
public partial struct TradeDeliverySystem : ISystem
{
    private struct HandOverCandidate
    {
        public Entity unitEntity;
        public UnitType unitType;
        public float healthFraction;
        public float distanceSquared;
        public bool claimed;
    }

    private struct PendingRelease
    {
        public Entity unitEntity;
        public TradeOrder order;
        public int slotIndex;
    }

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<TradeBoard>();
        state.RequireForUpdate<WorldClock>();
    }

    public void OnUpdate(ref SystemState state)
    {
        Entity boardEntity = SystemAPI.GetSingletonEntity<TradeBoard>();
        DynamicBuffer<TradeOrder> orders = SystemAPI.GetBuffer<TradeOrder>(boardEntity);

        // A drop-off is only a target while an accepted order waits there.
        foreach ((RefRO<TradeDropOff> dropOff, EnabledRefRW<PlayerInteractable> playerInteractableEnabled)
            in SystemAPI.Query<RefRO<TradeDropOff>, EnabledRefRW<PlayerInteractable>>()
                .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            playerInteractableEnabled.ValueRW = HasAcceptedOrderAt(orders, dropOff.ValueRO.dropOffId);
        }

        NativeList<Entity> requestedDropOffs = new NativeList<Entity>(Allocator.Temp);
        foreach ((RefRO<TradeDropOff> _, Entity dropOffEntity)
            in SystemAPI.Query<RefRO<TradeDropOff>>().WithAll<TradeHandOverRequest>().WithEntityAccess())
        {
            requestedDropOffs.Add(dropOffEntity);
        }

        for (int requestIndex = 0; requestIndex < requestedDropOffs.Length; requestIndex++)
        {
            Entity dropOffEntity = requestedDropOffs[requestIndex];
            SystemAPI.SetComponentEnabled<TradeHandOverRequest>(dropOffEntity, false);
            HandOver(ref state, boardEntity, dropOffEntity);
        }

        requestedDropOffs.Dispose();
    }

    private void HandOver(ref SystemState state, Entity boardEntity, Entity dropOffEntity)
    {
        EntityManager entityManager = state.EntityManager;
        TradeDropOff dropOff = SystemAPI.GetComponent<TradeDropOff>(dropOffEntity);
        float3 dropOffPosition = SystemAPI.GetComponent<LocalTransform>(dropOffEntity).Position;
        float radiusSquared = dropOff.handOverRadius * dropOff.handOverRadius;
        int today = SystemAPI.GetSingleton<WorldClock>().dayOfWeek;

        NativeList<HandOverCandidate> candidates = new NativeList<HandOverCandidate>(Allocator.Temp);
        foreach ((RefRO<UnitData> unitData, RefRO<Health> health, RefRO<LocalTransform> transform, Entity unitEntity)
            in SystemAPI.Query<RefRO<UnitData>, RefRO<Health>, RefRO<LocalTransform>>()
                .WithAll<Minion>()
                .WithDisabled<Dead>()
                .WithEntityAccess())
        {
            float distanceSquared = math.distancesq(transform.ValueRO.Position, dropOffPosition);
            if (distanceSquared > radiusSquared)
                continue;

            int healthMax = math.max(health.ValueRO.healthAmountMax, 1);
            candidates.Add(new HandOverCandidate
            {
                unitEntity = unitEntity,
                unitType = unitData.ValueRO.unitType,
                healthFraction = health.ValueRO.healthAmount / (float)healthMax,
                distanceSquared = distanceSquared,
            });
        }

        DynamicBuffer<TradeOrder> orders = SystemAPI.GetBuffer<TradeOrder>(boardEntity);
        DynamicBuffer<ClientRelationship> relationships = SystemAPI.GetBuffer<ClientRelationship>(boardEntity);
        bool hasLibrary = SystemAPI.TryGetSingleton(out ClientLibrary clientLibrary);
        TradeTuningBlob tuning = hasLibrary && clientLibrary.library.IsCreated ? clientLibrary.library.Value.tuning : default;
        Entity playerEntity = SystemAPI.GetSingletonEntity<Player>();
        DynamicBuffer<ResourceChangeRequest> ledgerRequests =
            entityManager.GetBuffer<ResourceChangeRequest>(ResourceLedger.HolderOf(entityManager, playerEntity));

        // Releasing a unit is a structural change, which would invalidate the buffers above, so it waits until they are done.
        NativeList<PendingRelease> pendingReleases = new NativeList<PendingRelease>(Allocator.Temp);
        bool deliveredAny = false;
        int deliveredPayout = 0;
        int deliveredOrderId = 0;
        int firstRefusedOrderId = 0;
        int firstRefusedMissing = 0;

        for (int orderIndex = 0; orderIndex < orders.Length; orderIndex++)
        {
            TradeOrder order = orders[orderIndex];
            if (order.state != TradeOrderState.Accepted || order.dropOffId != dropOff.dropOffId)
                continue;

            int eligibleCount = CountEligible(candidates, order);
            if (eligibleCount < order.quantity)
            {
                if (firstRefusedOrderId == 0)
                {
                    firstRefusedOrderId = order.orderId;
                    firstRefusedMissing = order.quantity - eligibleCount;
                }
                continue;
            }

            for (int takenCount = 0; takenCount < order.quantity; takenCount++)
            {
                int closestIndex = FindClosestEligible(candidates, order);
                HandOverCandidate taken = candidates[closestIndex];
                taken.claimed = true;
                candidates[closestIndex] = taken;
                pendingReleases.Add(new PendingRelease { unitEntity = taken.unitEntity, order = order, slotIndex = takenCount });
            }

            TradePayout.QueuePayout(order, hasLibrary, hasLibrary ? clientLibrary.library : default, ledgerRequests);
            if (order.clientId >= 0)
                TradePayout.RecordDelivery(relationships, order.clientId, today < order.dueDayOfWeek, in tuning);

            order.state = TradeOrderState.Delivered;
            orders[orderIndex] = order;
            deliveredAny = true;
            deliveredPayout = order.currencyReward;
            deliveredOrderId = order.orderId;
        }

        candidates.Dispose();

        TradeToast toast = SystemAPI.GetComponent<TradeToast>(boardEntity);
        if (deliveredAny)
        {
            toast.kind = TradeToastKind.HandOverDone;
            toast.orderId = deliveredOrderId;
            toast.amount = deliveredPayout;
        }
        else if (firstRefusedOrderId != 0)
        {
            toast.kind = TradeToastKind.HandOverRefused;
            toast.orderId = firstRefusedOrderId;
            toast.amount = firstRefusedMissing;
        }
        if (deliveredAny || firstRefusedOrderId != 0)
        {
            toast.sequence++;
            SystemAPI.SetComponent(boardEntity, toast);
        }

        for (int releaseIndex = 0; releaseIndex < pendingReleases.Length; releaseIndex++)
        {
            PendingRelease release = pendingReleases[releaseIndex];
            ReleaseToClient(entityManager, release.unitEntity, dropOff, release.order, release.slotIndex);
        }
        pendingReleases.Dispose();
    }

    private static bool HasAcceptedOrderAt(DynamicBuffer<TradeOrder> orders, int dropOffId)
    {
        for (int orderIndex = 0; orderIndex < orders.Length; orderIndex++)
        {
            if (orders[orderIndex].state == TradeOrderState.Accepted && orders[orderIndex].dropOffId == dropOffId)
                return true;
        }

        return false;
    }

    private static bool Fits(in HandOverCandidate candidate, in TradeOrder order)
    {
        return !candidate.claimed
            && candidate.unitType == order.unitType
            && candidate.healthFraction >= order.minHealthFraction;
    }

    private static int CountEligible(NativeList<HandOverCandidate> candidates, in TradeOrder order)
    {
        int eligibleCount = 0;
        for (int candidateIndex = 0; candidateIndex < candidates.Length; candidateIndex++)
        {
            if (Fits(candidates[candidateIndex], in order))
                eligibleCount++;
        }

        return eligibleCount;
    }

    private static int FindClosestEligible(NativeList<HandOverCandidate> candidates, in TradeOrder order)
    {
        int closestIndex = -1;
        for (int candidateIndex = 0; candidateIndex < candidates.Length; candidateIndex++)
        {
            if (!Fits(candidates[candidateIndex], in order))
                continue;
            if (closestIndex < 0 || candidates[candidateIndex].distanceSquared < candidates[closestIndex].distanceSquared)
                closestIndex = candidateIndex;
        }

        return closestIndex;
    }

    // The inverse of PlayerMinionSpawnInitSystem, plus leaving the squad. UtilityBrain is already off for player units, so the
    // delivered unit does not wander; DeliveredUnitWalkSystem sends it to its spot a beat later, after the interrupt has torn down.
    private static void ReleaseToClient(
        EntityManager entityManager, Entity unitEntity, in TradeDropOff dropOff, in TradeOrder order, int slotIndex)
    {
        HordeUtils.LeaveHorde(entityManager, unitEntity);
        entityManager.SetComponentEnabled<Minion>(unitEntity, false);
        if (entityManager.HasComponent<PlayerUnitBrain>(unitEntity))
            entityManager.SetComponentEnabled<PlayerUnitBrain>(unitEntity, false);
        if (entityManager.HasComponent<Selected>(unitEntity))
            entityManager.SetComponentEnabled<Selected>(unitEntity, false);
        if (entityManager.HasComponent<ActionInterruptRequest>(unitEntity))
            entityManager.SetComponentEnabled<ActionInterruptRequest>(unitEntity, true);

        // A ring of spots around the stand point so a delivered squad does not stack on one tile.
        float slotAngle = slotIndex * 2.399963f;
        float slotRadius = slotIndex == 0 ? 0f : 0.8f + 0.25f * slotIndex;
        float3 standPoint = dropOff.standPoint + new float3(math.cos(slotAngle), 0f, math.sin(slotAngle)) * slotRadius;

        entityManager.AddComponentData(unitEntity, new DeliveredToClient
        {
            dropOffId = dropOff.dropOffId,
            orderId = order.orderId,
            standPoint = standPoint,
        });
    }
}
