using Unity.Burst;
using Unity.Entities;

[BurstCompile]
[UpdateInGroup(typeof(HealthSystemGroup))]
public partial struct ReviveRequestSystem : ISystem
{
    private ComponentLookup<UtilityBrain>           aiBrainLookup;
    private ComponentLookup<ActionInterruptRequest> interruptLookup;
    private ComponentLookup<SwapBrainRequest>       swapBrainLookup;
    private ComponentLookup<Minion>                 minionLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<UnitDataLibrary>();
        aiBrainLookup   = state.GetComponentLookup<UtilityBrain>(false);
        interruptLookup = state.GetComponentLookup<ActionInterruptRequest>(false);
        swapBrainLookup = state.GetComponentLookup<SwapBrainRequest>(false);
        minionLookup    = state.GetComponentLookup<Minion>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        aiBrainLookup.Update(ref state);
        interruptLookup.Update(ref state);
        swapBrainLookup.Update(ref state);
        minionLookup.Update(ref state);

        BlobAssetReference<UnitLibraryBlob> unitLibrary =
            SystemAPI.GetSingleton<UnitDataLibrary>().library;

        state.Dependency = new ReviveJob
        {
            aiBrainLookup   = aiBrainLookup,
            interruptLookup = interruptLookup,
            swapBrainLookup = swapBrainLookup,
            minionLookup    = minionLookup,
            unitLibrary     = unitLibrary,
        }.Schedule(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(Dead))]
[WithPresent(typeof(Undead))]
public partial struct ReviveJob : IJobEntity
{
    public ComponentLookup<UtilityBrain>           aiBrainLookup;
    public ComponentLookup<ActionInterruptRequest> interruptLookup;
    public ComponentLookup<SwapBrainRequest>       swapBrainLookup;
    public ComponentLookup<Minion>                 minionLookup;
    public BlobAssetReference<UnitLibraryBlob>     unitLibrary;

    public void Execute(
        Entity entity,
        ref Health health,
        in UnitData unitData,
        ref UnitAction unitAction,
        EnabledRefRW<ReviveRequest> reviveEnabled,
        EnabledRefRW<Undead>        undeadEnabled,
        EnabledRefRW<Dead>          deadEnabled)
    {
        health.healthAmount   = health.healthAmountMax;
        reviveEnabled.ValueRW = false;
        undeadEnabled.ValueRW = true;
        // Dead disabled = alive. Disabling it also drops this entity from the [WithAll(Dead)]
        // filter next frame, so the revive runs exactly once.
        deadEnabled.ValueRW   = false;

        // Clear the death latch so a re-killed reanimated unit re-enters DeathSystem.
        unitAction.current = ActionType.Idle;

        // Conversion: if this unit declares a zombie/converted form, stamp + enable
        // SwapBrainRequest so SwapBrainSystem (same frame, after this) rebuilds its brain,
        // and make it a selectable Minion. Single-threaded .Schedule() makes these
        // ComponentLookup writes safe.
        int srcIdx = unitLibrary.Value.FindByUnitType(unitData.unitType);
        if (srcIdx >= 0)
        {
            UnitType becomes = unitLibrary.Value.units[srcIdx].becomesUnitType;
            if (becomes != UnitType.None && swapBrainLookup.HasComponent(entity))
            {
                swapBrainLookup[entity] = new SwapBrainRequest { newUnit = becomes };
                swapBrainLookup.SetComponentEnabled(entity, true);

                if (minionLookup.HasComponent(entity))
                    minionLookup.SetComponentEnabled(entity, true);
            }
        }

        // Re-enable AI brain and fire an interrupt so the action system tears down
        // DeathAction and re-enters action selection on the next frame.
        if (aiBrainLookup.HasComponent(entity))
            aiBrainLookup.SetComponentEnabled(entity, true);
        if (interruptLookup.HasComponent(entity))
            interruptLookup.SetComponentEnabled(entity, true);
    }
}
