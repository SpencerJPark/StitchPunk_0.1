using Unity.Burst;
using Unity.Entities;

// Smoke whenever the furnace is actually burning wood, parked or on the road (Caravan_System.md CV-D8).
[BurstCompile]
[UpdateInGroup(typeof(BuildingsSystemGroup))]
[UpdateAfter(typeof(WorkstationFurnaceSystem))]
public partial struct CaravanChimneySystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // Main-thread lookup reads/writes on other entities wait for nothing on their own (Gotchas.md).
        state.CompleteDependency();
        ComponentLookup<ResourceLedgerLink> resourceLedgerLinkLookup = SystemAPI.GetComponentLookup<ResourceLedgerLink>(true);
        BufferLookup<ResourceStack> resourceStackLookup = SystemAPI.GetBufferLookup<ResourceStack>(true);

        foreach ((RefRO<WorkstationFurnace> furnace, RefRO<Workstation> workstation,
                     EnabledRefRW<CaravanChimneySmoking> caravanChimneySmokingEnabled)
                 in SystemAPI.Query<RefRO<WorkstationFurnace>, RefRO<Workstation>, EnabledRefRW<CaravanChimneySmoking>>()
                     .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            bool hasWood = false;
            Entity ownerPlayer = workstation.ValueRO.ownerPlayer;
            if (furnace.ValueRO.isBurning && ownerPlayer != Entity.Null)
            {
                Entity ledgerHolder = ResourceLedger.HolderOf(ownerPlayer, in resourceLedgerLinkLookup);
                hasWood = resourceStackLookup.TryGetBuffer(ledgerHolder, out DynamicBuffer<ResourceStack> resourceStack)
                    && ResourceLedger.Get(in resourceStack, ResourceType.Wood) >= 1;
            }
            caravanChimneySmokingEnabled.ValueRW = hasWood;
        }
    }
}
