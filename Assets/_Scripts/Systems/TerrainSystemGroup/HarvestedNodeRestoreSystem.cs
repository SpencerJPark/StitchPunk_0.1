using Unity.Burst;
using Unity.Entities;

// Keeps ledgered nodes stumped after a tile reload re-bakes them full, and regrows stumps once their respawn is due.
[UpdateInGroup(typeof(TerrainSystemGroup))]
[UpdateAfter(typeof(HarvestedNodeRecordSystem))]
public partial struct HarvestedNodeRestoreSystem : ISystem
{
    private const double CheckIntervalElapsedSeconds = 1.0;

    private double nextCheckElapsedSeconds;

    // Deliberately not Burst-compiled (BC1028): creating the singleton uses managed EntityManager calls.
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<WorldClock>();
        HarvestedNodeRecordSystem.EnsureLedger(ref state);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        double elapsedSeconds = SystemAPI.Time.ElapsedTime;
        if (elapsedSeconds < nextCheckElapsedSeconds)
            return;
        nextCheckElapsedSeconds = elapsedSeconds + CheckIntervalElapsedSeconds;

        WorldClock clock = SystemAPI.GetSingleton<WorldClock>();
        state.Dependency = new RestoreNodesJob
        {
            ledgerEntity = SystemAPI.GetSingletonEntity<HarvestedNodeLedger>(),
            ledgerLookup = SystemAPI.GetBufferLookup<HarvestedNodeLedgerEntry>(false),
            nowWorldSeconds = clock.totalWorldSeconds,
            worldSecondsPerGameDay = clock.secondsPerDay,
        }.Schedule(state.Dependency);
    }

    [BurstCompile]
    [WithPresent(typeof(HarvestedNodeStump))]
    private partial struct RestoreNodesJob : IJobEntity
    {
        public Entity ledgerEntity;
        public BufferLookup<HarvestedNodeLedgerEntry> ledgerLookup;
        public double nowWorldSeconds;
        public double worldSecondsPerGameDay;

        private void Execute(ref ResourceNode resourceNode, in HarvestableNodeKey nodeKey,
            EnabledRefRW<HarvestedNodeStump> harvestedNodeStumpEnabled)
        {
            DynamicBuffer<HarvestedNodeLedgerEntry> ledger = ledgerLookup[ledgerEntity];
            int ledgerIndex = HarvestedNodeMath.IndexOfKey(in ledger, nodeKey.nodeKey);
            if (ledgerIndex < 0)
                return;

            bool respawnDue = HarvestedNodeMath.IsRespawnDue(ledger[ledgerIndex].harvestedAtWorldSeconds,
                nowWorldSeconds, nodeKey.respawnAfterGameDays, worldSecondsPerGameDay);

            if (!harvestedNodeStumpEnabled.ValueRO)
            {
                if (respawnDue)
                    return;
                harvestedNodeStumpEnabled.ValueRW = true;
                resourceNode.remaining = 0;
                return;
            }

            if (!respawnDue)
                return;
            resourceNode.remaining = nodeKey.fullAmount;
            harvestedNodeStumpEnabled.ValueRW = false;
            ledger.RemoveAtSwapBack(ledgerIndex);
        }
    }
}
