using Unity.Burst;
using Unity.Entities;

// A depleted node gets its stump enabled and its key written to the ledger so the node stays gone across tile unloads.
[UpdateInGroup(typeof(TerrainSystemGroup))]
public partial struct HarvestedNodeRecordSystem : ISystem
{
    // Deliberately not Burst-compiled (BC1028): creating the singleton uses managed EntityManager calls.
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<WorldClock>();
        EnsureLedger(ref state);
    }

    internal static Entity EnsureLedger(ref SystemState state)
    {
        EntityQuery ledgerQuery = state.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<HarvestedNodeLedger>());
        if (!ledgerQuery.IsEmptyIgnoreFilter)
            return ledgerQuery.GetSingletonEntity();

        Entity ledgerEntity = state.EntityManager.CreateEntity(typeof(HarvestedNodeLedger));
        state.EntityManager.AddBuffer<HarvestedNodeLedgerEntry>(ledgerEntity);
        return ledgerEntity;
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        Entity ledgerEntity = SystemAPI.GetSingletonEntity<HarvestedNodeLedger>();
        double nowWorldSeconds = SystemAPI.GetSingleton<WorldClock>().totalWorldSeconds;

        state.Dependency = new RecordDepletedNodesJob
        {
            ledgerEntity = ledgerEntity,
            ledgerLookup = SystemAPI.GetBufferLookup<HarvestedNodeLedgerEntry>(false),
            nowWorldSeconds = nowWorldSeconds,
        }.Schedule(state.Dependency);
    }

    [BurstCompile]
    [WithPresent(typeof(HarvestedNodeStump))]
    private partial struct RecordDepletedNodesJob : IJobEntity
    {
        public Entity ledgerEntity;
        public BufferLookup<HarvestedNodeLedgerEntry> ledgerLookup;
        public double nowWorldSeconds;

        private void Execute(in ResourceNode resourceNode, in HarvestableNodeKey nodeKey,
            EnabledRefRW<HarvestedNodeStump> harvestedNodeStumpEnabled)
        {
            if (harvestedNodeStumpEnabled.ValueRO || resourceNode.remaining > 0)
                return;

            harvestedNodeStumpEnabled.ValueRW = true;

            DynamicBuffer<HarvestedNodeLedgerEntry> ledger = ledgerLookup[ledgerEntity];
            for (int entryIndex = 0; entryIndex < ledger.Length; entryIndex++)
            {
                if (ledger[entryIndex].nodeKey == nodeKey.nodeKey)
                    return;
            }

            ledger.Add(new HarvestedNodeLedgerEntry { nodeKey = nodeKey.nodeKey, harvestedAtWorldSeconds = nowWorldSeconds });
        }
    }
}
