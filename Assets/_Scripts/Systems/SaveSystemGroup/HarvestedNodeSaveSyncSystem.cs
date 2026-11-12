using Unity.Entities;
using UnityEngine;

// Copies the harvested-node ledger into the Player snapshot just before PersistentSaveSystem serializes it (TradeSaveSyncSystem pattern).
// Not Burst-compiled: it adds a component to the Player and logs a warning on overflow.
[UpdateInGroup(typeof(SaveSystemGroup))]
[UpdateAfter(typeof(AutoSaveTimerSystem))]
[UpdateBefore(typeof(PersistentSaveSystem))]
public partial struct HarvestedNodeSaveSyncSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Player>();
        state.RequireForUpdate<GameDataTag>();
        state.RequireForUpdate<HarvestedNodeLedger>();
    }

    public void OnUpdate(ref SystemState state)
    {
        EntityManager entityManager = state.EntityManager;
        Entity playerEntity = SystemAPI.GetSingletonEntity<Player>();

        // The load side can only restore into a component the Player already has, so it must exist from the first frame.
        if (!entityManager.HasComponent<HarvestedNodeLedgerSnapshot>(playerEntity))
            entityManager.AddComponent<HarvestedNodeLedgerSnapshot>(playerEntity);

        Entity gameDataEntity = SystemAPI.GetSingletonEntity<GameDataTag>();
        if (!entityManager.IsComponentEnabled<SaveRequest>(gameDataEntity))
            return;

        Entity ledgerEntity = SystemAPI.GetSingletonEntity<HarvestedNodeLedger>();
        DynamicBuffer<HarvestedNodeLedgerEntry> ledger = SystemAPI.GetBuffer<HarvestedNodeLedgerEntry>(ledgerEntity);
        HarvestedNodeLedgerSnapshot snapshot = default;
        snapshot.hasData = 1;
        // Newest cuts last in the ledger; keep the newest when the save is full, they are the ones still due to regrow.
        int firstKeptIndex = Mathf.Max(0, ledger.Length - snapshot.entries.Capacity);
        if (firstKeptIndex > 0)
            Debug.LogWarning($"[HarvestedNodeSave] The save holds {snapshot.entries.Capacity} cut nodes; the {firstKeptIndex} oldest were left out and will regrow on load.");
        for (int entryIndex = firstKeptIndex; entryIndex < ledger.Length; entryIndex++)
            snapshot.entries.Add(ledger[entryIndex]);

        entityManager.SetComponentData(playerEntity, snapshot);
    }
}
