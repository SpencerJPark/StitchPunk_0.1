using Unity.Burst;
using Unity.Entities;

[BurstCompile]
[UpdateInGroup(typeof(ItemEquiptSystemGroup))]
public partial struct ItemEquipSystem : ISystem
{
    private ComponentLookup<UnitEquipt> unitEquiptLookup;
    private ComponentLookup<EquiptSocket> equiptSocketLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();

        unitEquiptLookup = state.GetComponentLookup<UnitEquipt>(false);
        equiptSocketLookup = state.GetComponentLookup<EquiptSocket>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        unitEquiptLookup.Update(ref state);
        equiptSocketLookup.Update(ref state);

        state.Dependency = new ItemEquipJob
        {
            unitEquiptLookup = unitEquiptLookup,
            equiptSocketLookup = equiptSocketLookup
        }.Schedule(state.Dependency);
    }
}

// Processes items flagged with EquipRequest (enabled by pickup or AI equip action).
// Links UnitEquipt on the owner and EquiptSocket on the socket, then clears the request.
[BurstCompile]
[WithAll(typeof(EquipAction))]
public partial struct ItemEquipJob : IJobEntity
{
    public ComponentLookup<UnitEquipt> unitEquiptLookup;
    public ComponentLookup<EquiptSocket> equiptSocketLookup;

    public void Execute(
        Entity itemEntity,
        in EquiptBy equiptBy,
        in AttachedTo attachedTo,
        EnabledRefRW<EquipAction> equipRequestEnabled)
    {
        // Link item to owner's UnitEquipt slot
        if (unitEquiptLookup.HasComponent(equiptBy.owner))
        {
            UnitEquipt unitEquipt = unitEquiptLookup[equiptBy.owner];
            unitEquipt.equiptItemEntity = itemEntity;
            unitEquiptLookup[equiptBy.owner] = unitEquipt;
        }

        // Link item to socket's EquiptSocket slot
        if (equiptSocketLookup.HasComponent(attachedTo.socket))
        {
            EquiptSocket socket = equiptSocketLookup[attachedTo.socket];
            socket.attachedItem = itemEntity;
            equiptSocketLookup[attachedTo.socket] = socket;
        }

        equipRequestEnabled.ValueRW = false;
    }
}
