using Unity.Entities;

// After PersistentLoadSystem patches the Player snapshot from the file, replaces the live harvested-node ledger with it.
// A save also sets hasData (snapshot equals live), so an unchanged ledger is left alone. Not Burst-compiled, like its save twin.
[UpdateInGroup(typeof(SaveSystemGroup))]
[UpdateAfter(typeof(PersistentLoadSystem))]
public partial struct HarvestedNodeLoadSyncSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Player>();
        state.RequireForUpdate<HarvestedNodeLedger>();
    }

    public void OnUpdate(ref SystemState state)
    {
        EntityManager entityManager = state.EntityManager;
        Entity playerEntity = SystemAPI.GetSingletonEntity<Player>();
        if (!entityManager.HasComponent<HarvestedNodeLedgerSnapshot>(playerEntity))
            return;

        HarvestedNodeLedgerSnapshot snapshot = entityManager.GetComponentData<HarvestedNodeLedgerSnapshot>(playerEntity);
        if (snapshot.hasData == 0)
            return;
        snapshot.hasData = 0;
        entityManager.SetComponentData(playerEntity, snapshot);

        Entity ledgerEntity = SystemAPI.GetSingletonEntity<HarvestedNodeLedger>();
        DynamicBuffer<HarvestedNodeLedgerEntry> ledger = SystemAPI.GetBuffer<HarvestedNodeLedgerEntry>(ledgerEntity);
        if (SnapshotMatchesLedger(snapshot, ledger))
            return;

        ledger.Clear();
        for (int entryIndex = 0; entryIndex < snapshot.entries.Length; entryIndex++)
            ledger.Add(snapshot.entries[entryIndex]);
    }

    private static bool SnapshotMatchesLedger(in HarvestedNodeLedgerSnapshot snapshot, DynamicBuffer<HarvestedNodeLedgerEntry> ledger)
    {
        if (snapshot.entries.Length != ledger.Length)
            return false;
        for (int entryIndex = 0; entryIndex < ledger.Length; entryIndex++)
        {
            if (snapshot.entries[entryIndex].nodeKey != ledger[entryIndex].nodeKey
                || snapshot.entries[entryIndex].harvestedAtWorldSeconds != ledger[entryIndex].harvestedAtWorldSeconds)
                return false;
        }
        return true;
    }
}
