using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

[BurstCompile]
[UpdateInGroup(typeof(SaveSystemGroup))]
[UpdateAfter(typeof(PersistentLoadSystem))]
public partial struct ResourceLoadSyncSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Player>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.Dependency = new CopySnapshotIntoStackJob
        {
            resourceLedgerLinkLookup = SystemAPI.GetComponentLookup<ResourceLedgerLink>(true),
            resourceStackLookup = SystemAPI.GetBufferLookup<ResourceStack>(),
        }.Schedule(state.Dependency);
    }

    // The save mirror runs earlier this frame, so snapshot equals stack unless a load just overwrote
    // the snapshot; that ordering is what stops the two mirrors fighting.
    [BurstCompile]
    [WithAll(typeof(Player))]
    private partial struct CopySnapshotIntoStackJob : IJobEntity
    {
        [ReadOnly] public ComponentLookup<ResourceLedgerLink> resourceLedgerLinkLookup;
        public BufferLookup<ResourceStack> resourceStackLookup;

        private void Execute(Entity playerEntity, in PlayerResourcesSnapshot snapshot)
        {
            Entity ledgerHolder = ResourceLedger.HolderOf(playerEntity, in resourceLedgerLinkLookup);
            if (!resourceStackLookup.TryGetBuffer(ledgerHolder, out DynamicBuffer<ResourceStack> resourceStacks)) return;
            for (int rowIndex = 0; rowIndex < resourceStacks.Length; rowIndex++)
            {
                ResourceStack row = resourceStacks[rowIndex];
                int snapshotAmount = ResourceSnapshotMapping.Read(snapshot, row.resourceType);
                if (row.amount == snapshotAmount)
                {
                    continue;
                }
                row.amount = snapshotAmount;
                resourceStacks[rowIndex] = row;
            }
        }
    }
}
