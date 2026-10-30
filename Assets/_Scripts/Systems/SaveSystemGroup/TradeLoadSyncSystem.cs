using Unity.Entities;

// After PersistentLoadSystem has patched the Player snapshots from the file, writes them back into the live clock and trade
// board. hasData is the "something to apply" flag: a save sets it (snapshot equals live, so nothing changes) and so does a
// load (snapshot differs). Not Burst-compiled, like its save twin.
[UpdateInGroup(typeof(SaveSystemGroup))]
[UpdateAfter(typeof(PersistentLoadSystem))]
public partial struct TradeLoadSyncSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Player>();
        state.RequireForUpdate<TradeBoard>();
        state.RequireForUpdate<WorldClock>();
    }

    public void OnUpdate(ref SystemState state)
    {
        EntityManager entityManager = state.EntityManager;
        Entity playerEntity = SystemAPI.GetSingletonEntity<Player>();
        if (!entityManager.HasComponent<WorldClockSnapshot>(playerEntity) || !entityManager.HasComponent<TradeBoardSnapshot>(playerEntity))
            return;

        WorldClockSnapshot clockSnapshot = entityManager.GetComponentData<WorldClockSnapshot>(playerEntity);
        TradeBoardSnapshot boardSnapshot = entityManager.GetComponentData<TradeBoardSnapshot>(playerEntity);
        if (clockSnapshot.hasData == 0 && boardSnapshot.hasData == 0)
            return;

        clockSnapshot.hasData = 0;
        boardSnapshot.hasData = 0;
        entityManager.SetComponentData(playerEntity, clockSnapshot);
        entityManager.SetComponentData(playerEntity, boardSnapshot);

        Entity clockEntity = SystemAPI.GetSingletonEntity<WorldClock>();
        Entity boardEntity = SystemAPI.GetSingletonEntity<TradeBoard>();
        WorldClock clock = SystemAPI.GetComponent<WorldClock>(clockEntity);
        TradeBoard board = SystemAPI.GetComponent<TradeBoard>(boardEntity);
        DynamicBuffer<TradeOrder> orders = SystemAPI.GetBuffer<TradeOrder>(boardEntity);
        DynamicBuffer<ClientRelationship> relationships = SystemAPI.GetBuffer<ClientRelationship>(boardEntity);

        // A save copies live state into the snapshot, so there is nothing to restore; skip it (and the pigeon) in that case.
        if (SnapshotMatchesLive(clockSnapshot, boardSnapshot, clock, board, orders, relationships))
            return;

        // weekNumber 0 means the saved game predates the first roll; leave the fresh clock to start itself.
        if (clockSnapshot.weekNumber >= 1)
        {
            clock.timeOfDay01 = clockSnapshot.timeOfDay01;
            clock.dayOfWeek = clockSnapshot.dayOfWeek;
            clock.weekNumber = clockSnapshot.weekNumber;
            SystemAPI.SetComponent(clockEntity, clock);
        }

        board.worldSeed = boardSnapshot.worldSeed;
        SystemAPI.SetComponent(boardEntity, board);

        orders.Clear();
        for (int orderIndex = 0; orderIndex < boardSnapshot.orders.Length; orderIndex++)
            orders.Add(boardSnapshot.orders[orderIndex]);
        relationships.Clear();
        for (int relationshipIndex = 0; relationshipIndex < boardSnapshot.relationships.Length; relationshipIndex++)
            relationships.Add(boardSnapshot.relationships[relationshipIndex]);

        // The pigeon is not saved: if this week still has unanswered offers, it flies in again with the letter.
        for (int orderIndex = 0; orderIndex < orders.Length; orderIndex++)
        {
            if (orders[orderIndex].state == TradeOrderState.Offered && orders[orderIndex].weekIssued == clock.weekNumber)
            {
                SystemAPI.GetBuffer<PendingPigeon>(boardEntity).Add(new PendingPigeon { weekNumber = clock.weekNumber });
                break;
            }
        }
    }

    private static bool SnapshotMatchesLive(
        in WorldClockSnapshot clockSnapshot, in TradeBoardSnapshot boardSnapshot, in WorldClock clock, in TradeBoard board,
        DynamicBuffer<TradeOrder> orders, DynamicBuffer<ClientRelationship> relationships)
    {
        if (clockSnapshot.weekNumber != clock.weekNumber || clockSnapshot.dayOfWeek != clock.dayOfWeek
            || clockSnapshot.timeOfDay01 != clock.timeOfDay01 || boardSnapshot.worldSeed != board.worldSeed)
            return false;
        if (boardSnapshot.orders.Length != orders.Length || boardSnapshot.relationships.Length != relationships.Length)
            return false;

        for (int orderIndex = 0; orderIndex < orders.Length; orderIndex++)
        {
            TradeOrder saved = boardSnapshot.orders[orderIndex];
            TradeOrder live = orders[orderIndex];
            if (saved.orderId != live.orderId || saved.state != live.state || saved.dueDayOfWeek != live.dueDayOfWeek)
                return false;
        }

        for (int relationshipIndex = 0; relationshipIndex < relationships.Length; relationshipIndex++)
        {
            ClientRelationship saved = boardSnapshot.relationships[relationshipIndex];
            ClientRelationship live = relationships[relationshipIndex];
            if (saved.clientId != live.clientId || saved.reputation != live.reputation
                || saved.ordersCompleted != live.ordersCompleted || saved.ordersFailed != live.ordersFailed)
                return false;
        }

        return true;
    }
}
