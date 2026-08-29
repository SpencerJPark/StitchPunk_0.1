using DotsMovementToolkit;
using Unity.Burst;
using Unity.Entities;

[BurstCompile]
[UpdateInGroup(typeof(HealthSystemGroup))]
public partial struct ReviveRequestSystem : ISystem
{
    private ComponentLookup<PlayerUnitBrain>        playerBrainLookup;
    private ComponentLookup<ActionInterruptRequest> interruptLookup;
    private ComponentLookup<SwapBrainRequest>       swapBrainLookup;
    private ComponentLookup<Minion>                 minionLookup;
    private ComponentLookup<PlayerInteractable>     playerInteractableLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<UnitDataLibrary>();
        playerBrainLookup        = state.GetComponentLookup<PlayerUnitBrain>(false);
        interruptLookup          = state.GetComponentLookup<ActionInterruptRequest>(false);
        swapBrainLookup          = state.GetComponentLookup<SwapBrainRequest>(false);
        minionLookup             = state.GetComponentLookup<Minion>(false);
        playerInteractableLookup = state.GetComponentLookup<PlayerInteractable>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        playerBrainLookup.Update(ref state);
        interruptLookup.Update(ref state);
        swapBrainLookup.Update(ref state);
        minionLookup.Update(ref state);
        playerInteractableLookup.Update(ref state);

        BlobAssetReference<UnitLibraryBlob> unitLibrary =
            SystemAPI.GetSingleton<UnitDataLibrary>().library;

        state.Dependency = new ReviveJob
        {
            playerBrainLookup = playerBrainLookup,
            interruptLookup   = interruptLookup,
            swapBrainLookup   = swapBrainLookup,
            minionLookup      = minionLookup,
            playerInteractableLookup = playerInteractableLookup,
            unitLibrary       = unitLibrary,
        }.Schedule(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(Dead))]
[WithPresent(typeof(Undead))]
[WithPresent(typeof(Movement))]
[WithPresent(typeof(Gravity))]
public partial struct ReviveJob : IJobEntity
{
    public ComponentLookup<PlayerUnitBrain>        playerBrainLookup;
    public ComponentLookup<ActionInterruptRequest> interruptLookup;
    public ComponentLookup<SwapBrainRequest>       swapBrainLookup;
    public ComponentLookup<Minion>                 minionLookup;
    public ComponentLookup<PlayerInteractable>     playerInteractableLookup;
    public BlobAssetReference<UnitLibraryBlob>     unitLibrary;

    public void Execute(
        Entity entity,
        ref Health health,
        in UnitData unitData,
        ref UnitAction unitAction,
        EnabledRefRW<ReviveRequest> reviveEnabled,
        EnabledRefRW<Undead>        undeadEnabled,
        EnabledRefRW<Dead>          deadEnabled,
        EnabledRefRW<Movement>      movementEnabled,
        EnabledRefRW<Gravity>       gravityEnabled)
    {
        // Only a corpse with a declared converted form (becomesUnitType) can be revived. With no
        // zombie form there is nothing to reanimate into — consume the request and stay dead.
        int srcIdx = unitLibrary.Value.FindByUnitType(unitData.unitType);
        UnitType becomes = srcIdx >= 0 ? unitLibrary.Value.units[srcIdx].becomesUnitType : UnitType.None;
        if (becomes == UnitType.None || !swapBrainLookup.HasComponent(entity))
        {
            reviveEnabled.ValueRW = false;
            return;
        }

        health.healthAmount   = health.healthAmountMax;
        reviveEnabled.ValueRW = false;
        undeadEnabled.ValueRW = true;
        // Dead disabled = alive. Disabling it also drops this entity from the [WithAll(Dead)]
        // filter next frame, so the revive runs exactly once.
        deadEnabled.ValueRW   = false;
        movementEnabled.ValueRW = true;
        gravityEnabled.ValueRW  = true;

        // Alive again — no longer a reviver target (DeathSystem re-enables this on re-kill).
        if (playerInteractableLookup.HasComponent(entity))
            playerInteractableLookup.SetComponentEnabled(entity, false);

        // Clear the death latch so a re-killed reanimated unit re-enters DeathSystem.
        unitAction.current = ActionType.Idle;

        // Convert the brain to the zombie form — SwapBrainSystem (same frame, after this) rebuilds
        // faction/attacks/motivations — and hand the unit to the player: enable Minion (selectable)
        // + PlayerUnitBrain (decisions now come from player commands). UtilityBrain stays DISABLED
        // (death left it off) — a revived minion is never driven by utility AI. Single-threaded
        // .Schedule() makes these ComponentLookup writes safe.
        swapBrainLookup[entity] = new SwapBrainRequest { newUnit = becomes };
        swapBrainLookup.SetComponentEnabled(entity, true);

        if (minionLookup.HasComponent(entity))
            minionLookup.SetComponentEnabled(entity, true);
        if (playerBrainLookup.HasComponent(entity))
            playerBrainLookup.SetComponentEnabled(entity, true);

        // Fire an interrupt so the action system tears down DeathAction and re-enters action
        // selection on the next frame.
        if (interruptLookup.HasComponent(entity))
            interruptLookup.SetComponentEnabled(entity, true);
    }
}
