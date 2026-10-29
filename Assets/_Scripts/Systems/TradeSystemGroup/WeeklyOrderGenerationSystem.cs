using System;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

// Not Burst-compiled: the roll logs through the managed Debug.Log and seeds from the wall clock on a brand-new board.
[UpdateInGroup(typeof(TradeSystemGroup))]
public partial struct WeeklyOrderGenerationSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<WorldClock>();
        state.RequireForUpdate<TradeBoard>();
        state.RequireForUpdate<ClientLibrary>();
    }

    public void OnUpdate(ref SystemState state)
    {
        Entity clockEntity = SystemAPI.GetSingletonEntity<WorldClock>();
        Entity boardEntity = SystemAPI.GetSingletonEntity<TradeBoard>();

        bool weekStarted = SystemAPI.IsComponentEnabled<WeekStarted>(clockEntity);
        bool rerollRequested = SystemAPI.IsComponentEnabled<TradeRerollRequest>(boardEntity);
        if (!weekStarted && !rerollRequested)
            return;

        BlobAssetReference<ClientLibraryBlob> library = SystemAPI.GetSingleton<ClientLibrary>().library;
        if (!library.IsCreated)
            return;

        RefRW<TradeBoard> board = SystemAPI.GetComponentRW<TradeBoard>(boardEntity);
        if (rerollRequested)
        {
            board.ValueRW.worldSeed = SystemAPI.GetComponent<TradeRerollRequest>(boardEntity).seed;
            SystemAPI.SetComponentEnabled<TradeRerollRequest>(boardEntity, false);
        }
        else if (board.ValueRO.worldSeed == 0u)
        {
            board.ValueRW.worldSeed = (uint)(DateTime.UtcNow.Ticks & 0x7FFFFFFF) | 1u;
        }

        BlobAssetReference<UnitLibraryBlob> unitLibrary = default;
        if (SystemAPI.TryGetSingleton(out UnitDataLibrary unitDataLibrary))
            unitLibrary = unitDataLibrary.library;

        int weekNumber = SystemAPI.GetComponent<WorldClock>(clockEntity).weekNumber;
        DynamicBuffer<TradeOrder> orders = SystemAPI.GetBuffer<TradeOrder>(boardEntity);
        DynamicBuffer<ClientRelationship> relationships = SystemAPI.GetBuffer<ClientRelationship>(boardEntity);

        EntityQuery dropOffQuery = SystemAPI.QueryBuilder().WithAll<TradeDropOff>().Build();
        NativeArray<TradeDropOff> dropOffs = dropOffQuery.ToComponentDataArray<TradeDropOff>(Allocator.Temp);

        TradeOrderRoller.RollWeek(
            ref library.Value, unitLibrary, dropOffs, orders, relationships, board.ValueRO.worldSeed, weekNumber);

        if (weekStarted && !rerollRequested)
            SystemAPI.GetBuffer<PendingPigeon>(boardEntity).Add(new PendingPigeon { weekNumber = weekNumber });

        Debug.Log($"[Trade] Week {weekNumber} rolled with seed {board.ValueRO.worldSeed}: {CountOffersOfWeek(orders, weekNumber)} offers, {dropOffs.Length} drop-offs in the scene.");
        dropOffs.Dispose();
    }

    private static int CountOffersOfWeek(DynamicBuffer<TradeOrder> orders, int weekNumber)
    {
        int offerCount = 0;
        for (int orderIndex = 0; orderIndex < orders.Length; orderIndex++)
        {
            if (orders[orderIndex].weekIssued == weekNumber)
                offerCount++;
        }

        return offerCount;
    }
}
