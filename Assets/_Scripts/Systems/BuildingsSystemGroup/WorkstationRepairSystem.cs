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

        foreach ((RefRO<Workstation> workstation,
                  RefRO<WorkstationRecipes> recipes,
                  RefRW<Health> health,
                  EnabledRefRW<WorkstationRepairRequest> workstationRepairRequestEnabled) in
            SystemAPI.Query<
                RefRO<Workstation>,
                RefRO<WorkstationRecipes>,
                RefRW<Health>,
                EnabledRefRW<WorkstationRepairRequest>>())
        {
            workstationRepairRequestEnabled.ValueRW = false;

            // A destroyed station has already latched CampLoopGameOver; it cannot be repaired back.
            if (health.ValueRO.healthAmount <= 0) continue;
            if (health.ValueRO.healthAmount >= health.ValueRO.healthAmountMax) continue;

            Entity ownerPlayer = workstation.ValueRO.ownerPlayer;
            if (ownerPlayer == Entity.Null
                || !SystemAPI.HasBuffer<ResourceStack>(ownerPlayer)
                || !SystemAPI.HasBuffer<ResourceChangeRequest>(ownerPlayer)
                || !SystemAPI.HasComponent<ResourceShortfall>(ownerPlayer)) continue;

            DynamicBuffer<ResourceStack> resourceStack = SystemAPI.GetBuffer<ResourceStack>(ownerPlayer);
            DynamicBuffer<ResourceChangeRequest> resourceChangeRequests = SystemAPI.GetBuffer<ResourceChangeRequest>(ownerPlayer);
            RefRW<ResourceShortfall> resourceShortfall = shortfallLookup.GetRefRW(ownerPlayer);
            EnabledRefRW<ResourceShortfall> resourceShortfallEnabled = shortfallLookup.GetEnabledRefRW<ResourceShortfall>(ownerPlayer);

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
