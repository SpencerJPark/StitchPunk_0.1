using Unity.Entities;
using UnityEngine;

// Copies the clock and the trade board into the Player snapshot components just before PersistentSaveSystem serializes them
// (the ResourceSaveSyncSystem pattern). Only on a save request: the board buffers are not worth copying every frame.
// Not Burst-compiled: it adds components to the Player and logs a warning on overflow.
[UpdateInGroup(typeof(SaveSystemGroup))]
[UpdateAfter(typeof(AutoSaveTimerSystem))]
[UpdateBefore(typeof(PersistentSaveSystem))]
public partial struct TradeSaveSyncSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Player>();
        state.RequireForUpdate<GameDataTag>();
        state.RequireForUpdate<TradeBoard>();
        state.RequireForUpdate<WorldClock>();
    }

    public void OnUpdate(ref SystemState state)
    {
        EntityManager entityManager = state.EntityManager;
        Entity playerEntity = SystemAPI.GetSingletonEntity<Player>();

        // The load side can only restore into components the Player already has, so they must exist from the first frame.
        if (!entityManager.HasComponent<WorldClockSnapshot>(playerEntity))
            entityManager.AddComponent<WorldClockSnapshot>(playerEntity);
        if (!entityManager.HasComponent<TradeBoardSnapshot>(playerEntity))
            entityManager.AddComponent<TradeBoardSnapshot>(playerEntity);

        Entity gameDataEntity = SystemAPI.GetSingletonEntity<GameDataTag>();
        if (!entityManager.IsComponentEnabled<SaveRequest>(gameDataEntity))
            return;

        Entity clockEntity = SystemAPI.GetSingletonEntity<WorldClock>();
        Entity boardEntity = SystemAPI.GetSingletonEntity<TradeBoard>();

        WorldClock clock = SystemAPI.GetComponent<WorldClock>(clockEntity);
        entityManager.SetComponentData(playerEntity, new WorldClockSnapshot
        {
            hasData = 1,
            timeOfDay01 = clock.timeOfDay01,
            dayOfWeek = clock.dayOfWeek,
            weekNumber = clock.weekNumber,
        });

        TradeBoardSnapshot boardSnapshot = default;
        boardSnapshot.hasData = 1;
        boardSnapshot.worldSeed = SystemAPI.GetComponent<TradeBoard>(boardEntity).worldSeed;

        DynamicBuffer<TradeOrder> orders = SystemAPI.GetBuffer<TradeOrder>(boardEntity);
        for (int orderIndex = 0; orderIndex < orders.Length; orderIndex++)
        {
            if (boardSnapshot.orders.Length >= boardSnapshot.orders.Capacity)
            {
                Debug.LogWarning($"[TradeSave] The save holds {boardSnapshot.orders.Capacity} orders; {orders.Length - orderIndex} were left out.");
                break;
            }
            boardSnapshot.orders.Add(orders[orderIndex]);
        }

        DynamicBuffer<ClientRelationship> relationships = SystemAPI.GetBuffer<ClientRelationship>(boardEntity);
        for (int relationshipIndex = 0; relationshipIndex < relationships.Length; relationshipIndex++)
        {
            if (boardSnapshot.relationships.Length >= boardSnapshot.relationships.Capacity)
            {
                Debug.LogWarning($"[TradeSave] The save holds {boardSnapshot.relationships.Capacity} client relationships; the rest were left out.");
                break;
            }
            boardSnapshot.relationships.Add(relationships[relationshipIndex]);
        }

        entityManager.SetComponentData(playerEntity, boardSnapshot);
    }
}
