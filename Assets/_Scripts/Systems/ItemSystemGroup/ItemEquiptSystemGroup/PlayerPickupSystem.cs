using Unity.Burst;
using Unity.Entities;
using Unity.Collections;

/// <summary>
/// When the player presses interact with a targeted Item, writes the equip
/// relationship onto the item and enables EquipRequest so ItemEquipSystem
/// finalises the link on the same frame.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(ItemEquiptSystemGroup))]
[UpdateBefore(typeof(ItemEquipSystem))]
public partial struct PlayerPickupSystem : ISystem
{
    private ComponentLookup<Item> itemLookup;
    private ComponentLookup<UnitEquipt> unitEquiptLookup;
    private ComponentLookup<EquiptBy> equiptByLookup;
    private ComponentLookup<AttachedTo> attachedToLookup;
    private ComponentLookup<EquipAction> equipRequestLookup;
    private ComponentLookup<AttachRequest> attachRequestLookup;
    private ComponentLookup<PlayerInteractable> playerInteractableLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();

        itemLookup               = state.GetComponentLookup<Item>(true);
        unitEquiptLookup         = state.GetComponentLookup<UnitEquipt>(true);
        equiptByLookup           = state.GetComponentLookup<EquiptBy>(false);
        attachedToLookup         = state.GetComponentLookup<AttachedTo>(false);
        equipRequestLookup       = state.GetComponentLookup<EquipAction>(false);
        attachRequestLookup      = state.GetComponentLookup<AttachRequest>(false);
        playerInteractableLookup = state.GetComponentLookup<PlayerInteractable>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        itemLookup.Update(ref state);
        unitEquiptLookup.Update(ref state);
        equiptByLookup.Update(ref state);
        attachedToLookup.Update(ref state);
        equipRequestLookup.Update(ref state);
        attachRequestLookup.Update(ref state);
        playerInteractableLookup.Update(ref state);

        state.Dependency = new PlayerPickupJob
        {
            itemLookup               = itemLookup,
            unitEquiptLookup         = unitEquiptLookup,
            equiptByLookup           = equiptByLookup,
            attachedToLookup         = attachedToLookup,
            equipRequestLookup       = equipRequestLookup,
            attachRequestLookup      = attachRequestLookup,
            playerInteractableLookup = playerInteractableLookup,
        }.Schedule(state.Dependency);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state) { }
}

[BurstCompile]
[WithAll(typeof(Player))]
[WithPresent(typeof(Target), typeof(OnInteractPlayerInput))]
public partial struct PlayerPickupJob : IJobEntity
{
    [ReadOnly] public ComponentLookup<Item> itemLookup;
    [ReadOnly] public ComponentLookup<UnitEquipt> unitEquiptLookup;
    public ComponentLookup<EquiptBy> equiptByLookup;
    public ComponentLookup<AttachedTo> attachedToLookup;
    public ComponentLookup<EquipAction> equipRequestLookup;
    public ComponentLookup<AttachRequest> attachRequestLookup;
    public ComponentLookup<PlayerInteractable> playerInteractableLookup;

    public void Execute(
        Entity playerEntity,
        RefRW<Target> target,
        EnabledRefRW<Target> targetEnabled,
        EnabledRefRW<OnInteractPlayerInput> interactEnabled)
    {
        if (!interactEnabled.ValueRO) return;

        interactEnabled.ValueRW = false;

        if (!targetEnabled.ValueRO) return;

        Entity itemEntity = target.ValueRO.entity;
        if (!itemLookup.HasComponent(itemEntity)) return;

        Entity socketEntity = unitEquiptLookup.HasComponent(playerEntity)
            ? unitEquiptLookup[playerEntity].socketEntity
            : Entity.Null;

        equiptByLookup[itemEntity]   = new EquiptBy   { owner   = playerEntity };
        attachedToLookup[itemEntity] = new AttachedTo { socket  = socketEntity };
        equipRequestLookup.SetComponentEnabled(itemEntity, true);
        attachRequestLookup.SetComponentEnabled(itemEntity, true);
        playerInteractableLookup.SetComponentEnabled(itemEntity, false);
        targetEnabled.ValueRW = false;
    }
}
