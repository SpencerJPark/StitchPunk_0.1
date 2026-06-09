using Unity.Burst;
using Unity.Entities;

[BurstCompile]
[UpdateInGroup(typeof(ItemEquipSystemGroup))]
public partial struct ItemEquipSystem : ISystem
{
    private ComponentLookup<UnitEquip> unitEquipLookup;
    private ComponentLookup<EquipSocket> equipSocketLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();

        unitEquipLookup = state.GetComponentLookup<UnitEquip>(false);
        equipSocketLookup = state.GetComponentLookup<EquipSocket>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        unitEquipLookup.Update(ref state);
        equipSocketLookup.Update(ref state);

        state.Dependency = new ItemEquipJob
        {
            unitEquipLookup = unitEquipLookup,
            equipSocketLookup = equipSocketLookup
        }.Schedule(state.Dependency);
    }
}

// Processes items flagged with EquipRequest (enabled by pickup or AI equip action).
// Links UnitEquip on the owner and EquipSocket on the socket, then clears the request.
[BurstCompile]
[WithAll(typeof(PickupRequest))]
public partial struct ItemEquipJob : IJobEntity
{
    public ComponentLookup<UnitEquip> unitEquipLookup;
    public ComponentLookup<EquipSocket> equipSocketLookup;

    public void Execute(
        Entity itemEntity,
        in EquipBy equipBy,
        in AttachedTo attachedTo,
        EnabledRefRW<PickupRequest> equipRequestEnabled)
    {
        // Link item to owner's UnitEquip slot
        if (unitEquipLookup.HasComponent(equipBy.owner))
        {
            UnitEquip unitEquip = unitEquipLookup[equipBy.owner];
            unitEquip.equipItemEntity = itemEntity;
            unitEquipLookup[equipBy.owner] = unitEquip;
        }

        // Link item to socket's EquipSocket slot
        if (equipSocketLookup.HasComponent(attachedTo.socket))
        {
            EquipSocket socket = equipSocketLookup[attachedTo.socket];
            socket.attachedItem = itemEntity;
            equipSocketLookup[attachedTo.socket] = socket;
        }

        equipRequestEnabled.ValueRW = false;
    }
}
