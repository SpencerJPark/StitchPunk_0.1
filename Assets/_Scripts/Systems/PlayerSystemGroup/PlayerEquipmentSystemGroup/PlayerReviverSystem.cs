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
        ComponentLookup<ResourceLedgerLink> resourceLedgerLinkLookup = SystemAPI.GetComponentLookup<ResourceLedgerLink>(true);
        ComponentLookup<ResourceShortfall> resourceShortfallLookup = SystemAPI.GetComponentLookup<ResourceShortfall>();

        foreach ((EnabledRefRW<OnPlayerReviverEquip> onPlayerReviverEquipEnabled,
                  RefRO<Target> target,
                  EnabledRefRO<Target> targetEnabled,
                  Entity playerEntity) in
            SystemAPI.Query<
                EnabledRefRW<OnPlayerReviverEquip>,
                RefRO<Target>,
                EnabledRefRO<Target>>()
                    .WithAll<Player>()
                    .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
                    .WithEntityAccess())
        {
            if (!onPlayerReviverEquipEnabled.ValueRO) continue;

            if (targetEnabled.ValueRO)
            {
                Entity targetEntity = target.ValueRO.entity;
                // Rounds are drawn from the caravan's ledger even far from it (Caravan_System.md CV-Q8).
                Entity ledgerHolder = ResourceLedger.HolderOf(playerEntity, in resourceLedgerLinkLookup);
                if (SystemAPI.HasComponent<ReviveRequest>(targetEntity) && IsRevivable(ref state, targetEntity)
                    && SystemAPI.HasBuffer<ResourceStack>(ledgerHolder)
                    && resourceShortfallLookup.HasComponent(ledgerHolder))
                {
                    // A failed spend stamps the shortfall; the revive simply does not happen.
                    DynamicBuffer<ResourceStack> resourceStack = SystemAPI.GetBuffer<ResourceStack>(ledgerHolder);
                    DynamicBuffer<ResourceChangeRequest> requests = SystemAPI.GetBuffer<ResourceChangeRequest>(ledgerHolder);
                    bool paid = ResourceLedger.TrySpendSingle(
                        in resourceStack, ref requests, ResourceType.RevivalRound, 1,
                        SystemAPI.Time.ElapsedTime, ref resourceShortfallLookup.GetRefRW(ledgerHolder).ValueRW,
                        resourceShortfallLookup.GetEnabledRefRW<ResourceShortfall>(ledgerHolder));
                    if (paid)
                        SystemAPI.SetComponentEnabled<ReviveRequest>(targetEntity, true);
                }
            }

            onPlayerReviverEquipEnabled.ValueRW = false;
        }
    }

    // Field revives are humans only (CL-D4): the corpse must convert into a DIFFERENT unit. No becomesUnitType
    // would no-op in ReviveRequestSystem; becomesUnitType == itself is an undead form (e.g. PlayerZombie).
    private bool IsRevivable(ref SystemState state, Entity corpseEntity)
    {
        if (!SystemAPI.HasComponent<UnitData>(corpseEntity)) return false;

        UnitType corpseType = SystemAPI.GetComponent<UnitData>(corpseEntity).unitType;
        BlobAssetReference<UnitLibraryBlob> unitLibrary = SystemAPI.GetSingleton<UnitDataLibrary>().library;
        int unitIndex = unitLibrary.Value.FindByUnitType(corpseType);
        if (unitIndex < 0) return false;
        UnitType becomesUnitType = unitLibrary.Value.units[unitIndex].becomesUnitType;
        return becomesUnitType != UnitType.None && becomesUnitType != corpseType;
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state) { }
}
