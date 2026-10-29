using Unity.Burst;
using Unity.Entities;

// The caravan is placed in the subscene, not deployed, so nothing else ever sets its owner. On the first frame
// both exist this makes it the player's workstation, parks it and hands it the player's stock (Caravan_System.md §5).
[BurstCompile]
[UpdateInGroup(typeof(WorkstationInputSystemGroup), OrderFirst = true)]
public partial struct CaravanLinkSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Player>();
        state.RequireForUpdate<Caravan>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        Entity playerEntity = SystemAPI.GetSingletonEntity<Player>();

        foreach ((RefRW<Workstation> workstation, EnabledRefRW<CaravanParked> caravanParkedEnabled, Entity caravanEntity)
                 in SystemAPI.Query<RefRW<Workstation>, EnabledRefRW<CaravanParked>>()
                     .WithAll<Caravan>()
                     .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
                     .WithEntityAccess())
        {
            if (workstation.ValueRO.ownerPlayer != Entity.Null) continue;

            // Writes to the player's components and buffers below wait for nothing on their own (Gotchas.md).
            state.CompleteDependency();
            workstation.ValueRW.ownerPlayer = playerEntity;
            caravanParkedEnabled.ValueRW = true;

            if (SystemAPI.HasComponent<PlayerWorkstationLink>(playerEntity))
                SystemAPI.SetComponent(playerEntity, new PlayerWorkstationLink { workstation = caravanEntity });

            if (SystemAPI.HasComponent<ResourceLedgerLink>(playerEntity))
            {
                TakeOverLedger(ref state, playerEntity, caravanEntity);
                SystemAPI.SetComponent(playerEntity, new ResourceLedgerLink { ledgerHolder = caravanEntity });
            }
        }
    }

    // Moves the rows rather than copying them, so any code still reading the player's own buffers sees
    // zeros and shows up as a bug instead of quietly spending a second copy of the stock.
    private void TakeOverLedger(ref SystemState state, Entity playerEntity, Entity caravanEntity)
    {
        if (!SystemAPI.HasBuffer<ResourceStack>(playerEntity)) return;

        DynamicBuffer<ResourceStack> playerStacks = SystemAPI.GetBuffer<ResourceStack>(playerEntity);
        DynamicBuffer<ResourceStack> caravanStacks = SystemAPI.GetBuffer<ResourceStack>(caravanEntity);
        if (caravanStacks.Length == 0)
        {
            for (int rowIndex = 0; rowIndex < playerStacks.Length; rowIndex++)
                caravanStacks.Add(playerStacks[rowIndex]);
        }
        for (int rowIndex = 0; rowIndex < playerStacks.Length; rowIndex++)
        {
            ResourceStack emptiedRow = playerStacks[rowIndex];
            emptiedRow.amount = 0;
            playerStacks[rowIndex] = emptiedRow;
        }

        DynamicBuffer<StorageCapacity> playerCapacities = SystemAPI.GetBuffer<StorageCapacity>(playerEntity);
        DynamicBuffer<StorageCapacity> caravanCapacities = SystemAPI.GetBuffer<StorageCapacity>(caravanEntity);
        if (caravanCapacities.Length == 0)
        {
            for (int rowIndex = 0; rowIndex < playerCapacities.Length; rowIndex++)
                caravanCapacities.Add(playerCapacities[rowIndex]);
        }

        // Deltas queued this frame before the link (a gather deposit, a debug +10) follow the stock.
        DynamicBuffer<ResourceChangeRequest> playerRequests = SystemAPI.GetBuffer<ResourceChangeRequest>(playerEntity);
        DynamicBuffer<ResourceChangeRequest> caravanRequests = SystemAPI.GetBuffer<ResourceChangeRequest>(caravanEntity);
        for (int requestIndex = 0; requestIndex < playerRequests.Length; requestIndex++)
            caravanRequests.Add(playerRequests[requestIndex]);
        playerRequests.Clear();
    }
}
