using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

// Consumes consumable items flagged with PickupRequest BEFORE ItemEquipSystem links them to a
// hand socket. Weapons (and any non-consumable category) are left untouched and fall through to
// the equip path. Effect resolution mirrors the legacy PickupItemActionSystem consume branch:
//   - EffectType.Healing -> HealRequest on the owner.
//   - Otherwise          -> MotivationChangeRequest (Add) per effect behaviour on the owner.
// The item is destroyed end-of-frame via ECB; both requests are disabled same-frame so the
// attach/equip systems never see it.
[BurstCompile]
[UpdateInGroup(typeof(ItemEquipSystemGroup))]
[UpdateBefore(typeof(ItemEquipSystem))]
public partial struct ItemConsumeSystem : ISystem
{
    private ComponentLookup<HealRequest> _healRequestLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<ItemLibrary>();
        state.RequireForUpdate<EffectLibrary>();

        _healRequestLookup = state.GetComponentLookup<HealRequest>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        _healRequestLookup.Update(ref state);

        BlobAssetReference<ItemLibraryBlob> itemLibrary =
            SystemAPI.GetSingleton<ItemLibrary>().library;
        BlobAssetReference<EffectLibraryBlob> effectLibrary =
            SystemAPI.GetSingleton<EffectLibrary>().library;

        EntityCommandBuffer ecb = SystemAPI
            .GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
            .CreateCommandBuffer(state.WorldUnmanaged);

        // Single-threaded: writes to arbitrary owner entities via lookup, mirroring ItemEquipSystem.
        state.Dependency = new ItemConsumeJob
        {
            itemLibrary       = itemLibrary,
            effectLibrary     = effectLibrary,
            healRequestLookup = _healRequestLookup,
            ecb               = ecb,
        }.Schedule(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(PickupRequest))]
public partial struct ItemConsumeJob : IJobEntity
{
    [ReadOnly] public BlobAssetReference<ItemLibraryBlob>   itemLibrary;
    [ReadOnly] public BlobAssetReference<EffectLibraryBlob> effectLibrary;
    public ComponentLookup<HealRequest> healRequestLookup;
    public EntityCommandBuffer          ecb;

    public void Execute(
        Entity                         itemEntity,
        in Item                        item,
        in EquipBy                     equipBy,
        EnabledRefRW<PickupRequest>    pickupRequestEnabled,
        EnabledRefRW<AttachItemRequest> attachItemRequestEnabled)
    {
        int typeIndex = (int)item.itemType;
        if (typeIndex < 0 || typeIndex >= itemLibrary.Value.items.Length) return;

        ref ItemBlob blob = ref itemLibrary.Value.items[typeIndex];
        if (blob.category != ItemCategory.Consumable) return;

        Entity owner = equipBy.owner;
        if (owner == Entity.Null) return;

        int effectIndex = (int)blob.consumeEffect;
        if (effectIndex >= 0 && effectIndex < effectLibrary.Value.effects.Length)
        {
            ref EffectBlob effectBlob = ref effectLibrary.Value.effects[effectIndex];

            if (effectBlob.effectType == EffectType.Healing)
            {
                if (healRequestLookup.HasComponent(owner))
                {
                    healRequestLookup[owner] = new HealRequest { healAmount = (int)effectBlob.value };
                    healRequestLookup.SetComponentEnabled(owner, true);
                }
            }
            else
            {
                for (int b = 0; b < effectBlob.behaviours.Length; b++)
                {
                    NeedType need = effectBlob.behaviours[b];
                    if (need == NeedType.None) continue;
                    ecb.AppendToBuffer(owner, new MotivationChangeRequest
                    {
                        needType   = need,
                        changeType = MotivationChangeType.Add,
                        value      = effectBlob.value,
                    });
                }
            }
        }

        pickupRequestEnabled.ValueRW     = false;
        attachItemRequestEnabled.ValueRW = false;
        ecb.DestroyEntity(itemEntity);
    }
}
