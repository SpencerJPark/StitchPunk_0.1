using Unity.Burst;
using Unity.Entities;

/// <summary>
/// Reads an enabled OnEquipmentSlotPlayerInput, resolves which ItemType lives in that slot,
/// and fires the matching equipment event component. Disables OnEquipmentSlotPlayerInput when done.
///
/// To add a new equipment type:
///   1. Add the ItemType value to the ItemType enum.
///   2. Add an IEnableableComponent event struct to PlayerEquipmentComponents.cs.
///   3. Add a case to the switch below.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(PlayerInputSystemGroup))]
public partial struct PlayerEquipmentInputSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Player>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (slotInput, slotEnabled, slots, entity) in
            SystemAPI.Query<
                RefRO<OnEquipmentSlotPlayerInput>,
                EnabledRefRW<OnEquipmentSlotPlayerInput>,
                RefRO<PlayerEquipmentSlots>>()
                    .WithAll<Player>()
                    .WithEntityAccess()
                    .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState))
        {
            if (!slotEnabled.ValueRO) continue;

            ItemType equippedItem = slotInput.ValueRO.slot switch
            {
                1 => slots.ValueRO.itemSlot1,
                2 => slots.ValueRO.itemSlot2,
                3 => slots.ValueRO.itemSlot3,
                4 => slots.ValueRO.itemSlot4,
                _ => ItemType.None,
            };

            switch (equippedItem)
            {
                case ItemType.Reviver:
                    SystemAPI.SetComponentEnabled<OnPlayerReviverEquipt>(entity, true);
                    break;
            }

            slotEnabled.ValueRW = false;
        }
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state) { }
}
