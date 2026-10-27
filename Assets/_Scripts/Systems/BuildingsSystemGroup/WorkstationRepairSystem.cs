using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

[BurstCompile]
[UpdateInGroup(typeof(BuildingsSystemGroup))]
public partial struct WorkstationRepairSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        ComponentLookup<ResourceShortfall> shortfallLookup = SystemAPI.GetComponentLookup<ResourceShortfall>();
        ComponentLookup<ResourceLedgerLink> resourceLedgerLinkLookup = SystemAPI.GetComponentLookup<ResourceLedgerLink>(true);

        foreach ((RefRO<Workstation> workstation,
                  RefRO<WorkstationRecipes> recipes,
                  RefRW<Health> health,
                  EnabledRefRW<WorkstationRepairRequest> workstationRepairRequestEnabled,
                  Entity workstationEntity) in
            SystemAPI.Query<
                RefRO<Workstation>,
                RefRO<WorkstationRecipes>,
                RefRW<Health>,
                EnabledRefRW<WorkstationRepairRequest>>()
                .WithEntityAccess())
        {
            workstationRepairRequestEnabled.ValueRW = false;

            // A destroyed hut has already latched CampLoopGameOver; a broken caravan is exactly what repair is for.
            if (health.ValueRO.healthAmount <= 0 && !SystemAPI.HasComponent<Caravan>(workstationEntity)) continue;
            if (health.ValueRO.healthAmount >= health.ValueRO.healthAmountMax) continue;

            Entity ownerPlayer = workstation.ValueRO.ownerPlayer;
            if (ownerPlayer == Entity.Null) continue;
            Entity ledgerHolder = ResourceLedger.HolderOf(ownerPlayer, in resourceLedgerLinkLookup);
            if (!SystemAPI.HasBuffer<ResourceStack>(ledgerHolder)
                || !SystemAPI.HasBuffer<ResourceChangeRequest>(ledgerHolder)
                || !SystemAPI.HasComponent<ResourceShortfall>(ledgerHolder)) continue;

            DynamicBuffer<ResourceStack> resourceStack = SystemAPI.GetBuffer<ResourceStack>(ledgerHolder);
            DynamicBuffer<ResourceChangeRequest> resourceChangeRequests = SystemAPI.GetBuffer<ResourceChangeRequest>(ledgerHolder);
            RefRW<ResourceShortfall> resourceShortfall = shortfallLookup.GetRefRW(ledgerHolder);
            EnabledRefRW<ResourceShortfall> resourceShortfallEnabled = shortfallLookup.GetEnabledRefRW<ResourceShortfall>(ledgerHolder);

            // A failed spend stamps the shortfall; the repair simply does not happen.
            bool paid = ResourceLedger.TrySpendSingle(
                in resourceStack, ref resourceChangeRequests, ResourceType.Wood, recipes.ValueRO.repairWoodCost,
                SystemAPI.Time.ElapsedTime, ref resourceShortfall.ValueRW, resourceShortfallEnabled);
            if (!paid) continue;

            health.ValueRW.healthAmount = math.min(
                health.ValueRO.healthAmountMax,
                health.ValueRO.healthAmount + recipes.ValueRO.repairHealthAmount);
        }
    }
}
