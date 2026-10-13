using Unity.Burst;
using Unity.Entities;

[BurstCompile]
[UpdateInGroup(typeof(PlayerEquipmentSystemGroup))]
public partial struct PlayerReviverSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Player>();
        state.RequireForUpdate<UnitDataLibrary>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach ((EnabledRefRW<OnPlayerReviverEquip> onPlayerReviverEquipEnabled,
                  RefRO<Target> target,
                  EnabledRefRO<Target> targetEnabled,
                  DynamicBuffer<ResourceStack> resourceStack,
                  DynamicBuffer<ResourceChangeRequest> resourceChangeRequests,
                  RefRW<ResourceShortfall> resourceShortfall,
                  EnabledRefRW<ResourceShortfall> resourceShortfallEnabled) in
            SystemAPI.Query<
                EnabledRefRW<OnPlayerReviverEquip>,
                RefRO<Target>,
                EnabledRefRO<Target>,
                DynamicBuffer<ResourceStack>,
                DynamicBuffer<ResourceChangeRequest>,
                RefRW<ResourceShortfall>,
                EnabledRefRW<ResourceShortfall>>()
                    .WithAll<Player>()
                    .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            if (!onPlayerReviverEquipEnabled.ValueRO) continue;

            if (targetEnabled.ValueRO)
            {
                Entity targetEntity = target.ValueRO.entity;
                if (SystemAPI.HasComponent<ReviveRequest>(targetEntity) && IsRevivable(ref state, targetEntity))
                {
                    // A failed spend stamps the shortfall; the revive simply does not happen.
                    DynamicBuffer<ResourceChangeRequest> requests = resourceChangeRequests;
                    bool paid = ResourceLedger.TrySpendSingle(
                        in resourceStack, ref requests, ResourceType.RevivalRound, 1,
                        SystemAPI.Time.ElapsedTime, ref resourceShortfall.ValueRW, resourceShortfallEnabled);
                    if (paid)
                        SystemAPI.SetComponentEnabled<ReviveRequest>(targetEntity, true);
                }
            }

            onPlayerReviverEquipEnabled.ValueRW = false;
        }
    }

    // Mirrors ReviveRequestSystem: no becomesUnitType means the revive would no-op, so it must not cost a round.
    private bool IsRevivable(ref SystemState state, Entity corpseEntity)
    {
        if (!SystemAPI.HasComponent<UnitData>(corpseEntity)) return false;

        UnitType corpseType = SystemAPI.GetComponent<UnitData>(corpseEntity).unitType;
        BlobAssetReference<UnitLibraryBlob> unitLibrary = SystemAPI.GetSingleton<UnitDataLibrary>().library;
        int unitIndex = unitLibrary.Value.FindByUnitType(corpseType);
        return unitIndex >= 0 && unitLibrary.Value.units[unitIndex].becomesUnitType != UnitType.None;
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state) { }
}
