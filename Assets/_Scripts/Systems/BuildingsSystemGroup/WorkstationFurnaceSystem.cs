using Unity.Burst;
using Unity.Entities;

[BurstCompile]
[UpdateInGroup(typeof(BuildingsSystemGroup))]
public partial struct WorkstationFurnaceSystem : ISystem
{
    private BufferLookup<ResourceStack> resourceStackLookup;
    private BufferLookup<ResourceChangeRequest> resourceChangeRequestLookup;
    private ComponentLookup<ResourceLedgerLink> resourceLedgerLinkLookup;

    public void OnCreate(ref SystemState state)
    {
        resourceStackLookup = state.GetBufferLookup<ResourceStack>(true);
        resourceChangeRequestLookup = state.GetBufferLookup<ResourceChangeRequest>();
        resourceLedgerLinkLookup = state.GetComponentLookup<ResourceLedgerLink>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        resourceStackLookup.Update(ref state);
        resourceChangeRequestLookup.Update(ref state);
        resourceLedgerLinkLookup.Update(ref state);
        float deltaTime = SystemAPI.Time.DeltaTime;

        // The toggle request is usually disabled, so the query must ignore enabled state; the bit is checked by hand.
        foreach ((RefRW<WorkstationFurnace> furnace, RefRO<Workstation> workstation, EnabledRefRW<WorkstationFurnaceToggleRequest> workstationFurnaceToggleRequestEnabled)
                 in SystemAPI.Query<RefRW<WorkstationFurnace>, RefRO<Workstation>, EnabledRefRW<WorkstationFurnaceToggleRequest>>()
                     .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            if (workstationFurnaceToggleRequestEnabled.ValueRO)
            {
                furnace.ValueRW.isBurning = !furnace.ValueRO.isBurning;
                workstationFurnaceToggleRequestEnabled.ValueRW = false;
            }

            Entity owner = workstation.ValueRO.ownerPlayer;
            Entity ledgerHolder = owner == Entity.Null ? Entity.Null : ResourceLedger.HolderOf(owner, in resourceLedgerLinkLookup);
            bool hasWood = false;
            if (furnace.ValueRO.isBurning
                && ledgerHolder != Entity.Null
                && resourceStackLookup.TryGetBuffer(ledgerHolder, out DynamicBuffer<ResourceStack> resourceStack))
            {
                hasWood = ResourceLedger.Get(in resourceStack, ResourceType.Wood) >= 1;
            }

            if (!hasWood)
            {
                furnace.ValueRW.burnTimer = 0f;
                continue;
            }

            furnace.ValueRW.burnTimer += deltaTime;
            if (furnace.ValueRO.burnTimer < furnace.ValueRO.burnIntervalSeconds) continue;

            furnace.ValueRW.burnTimer -= furnace.ValueRO.burnIntervalSeconds;
            if (resourceChangeRequestLookup.TryGetBuffer(ledgerHolder, out DynamicBuffer<ResourceChangeRequest> resourceChangeRequests))
            {
                ResourceLedger.QueueAdd(ref resourceChangeRequests, ResourceType.Wood, -1);
                ResourceLedger.QueueAdd(ref resourceChangeRequests, ResourceType.Electricity, furnace.ValueRO.electricityPerWood);
            }
        }
    }
}
