using DotsAnimationToolkit;
using DotsMovementToolkit;
using Unity.Burst;
using Unity.Entities;

[BurstCompile]
[UpdateInGroup(typeof(HealthSystemGroup))]
public partial struct ReviveRequestSystem : ISystem
{
    private ComponentLookup<PlayerUnitBrain>         playerBrainLookup;
    private ComponentLookup<ActionInterruptRequest>  interruptLookup;
    private ComponentLookup<SwapBrainRequest>        swapBrainLookup;
    private ComponentLookup<Minion>                  minionLookup;
    private ComponentLookup<PlayerInteractable>      playerInteractableLookup;
    private BufferLookup<AnimationCommand>           animationCommandLookup;
    private ComponentLookup<AnimationCommandPending> animationCommandPendingLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<UnitDataLibrary>();
        playerBrainLookup             = state.GetComponentLookup<PlayerUnitBrain>(false);
        interruptLookup               = state.GetComponentLookup<ActionInterruptRequest>(false);
        swapBrainLookup               = state.GetComponentLookup<SwapBrainRequest>(false);
        minionLookup                  = state.GetComponentLookup<Minion>(false);
        playerInteractableLookup      = state.GetComponentLookup<PlayerInteractable>(false);
        animationCommandLookup        = state.GetBufferLookup<AnimationCommand>(false);
        animationCommandPendingLookup = state.GetComponentLookup<AnimationCommandPending>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        playerBrainLookup.Update(ref state);
        interruptLookup.Update(ref state);
        swapBrainLookup.Update(ref state);
        minionLookup.Update(ref state);
        playerInteractableLookup.Update(ref state);
        animationCommandLookup.Update(ref state);
        animationCommandPendingLookup.Update(ref state);

        BlobAssetReference<UnitLibraryBlob> unitLibrary =
            SystemAPI.GetSingleton<UnitDataLibrary>().library;

        state.Dependency = new ReviveJob
        {
            playerBrainLookup = playerBrainLookup,
            interruptLookup   = interruptLookup,
            swapBrainLookup   = swapBrainLookup,
            minionLookup      = minionLookup,
            playerInteractableLookup      = playerInteractableLookup,
            animationCommandLookup        = animationCommandLookup,
            animationCommandPendingLookup = animationCommandPendingLookup,
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
    public ComponentLookup<SwapBrainRequest>         swapBrainLookup;
    public ComponentLookup<Minion>                   minionLookup;
    public ComponentLookup<PlayerInteractable>       playerInteractableLookup;
    public BufferLookup<AnimationCommand>            animationCommandLookup;
    public ComponentLookup<AnimationCommandPending>  animationCommandPendingLookup;
    public BlobAssetReference<UnitLibraryBlob>       unitLibrary;

    public void Execute(
        Entity entity,
        ref Health health,
        in UnitData unitData,
        ref UnitAction unitAction,
        EnabledRefRW<ReviveRequest> reviveRequestEnabled,
        EnabledRefRW<Undead>        undeadEnabled,
        EnabledRefRW<Dead>          deadEnabled,
        EnabledRefRW<Movement>      movementEnabled,
        EnabledRefRW<Gravity>       gravityEnabled)
    {
        // No becomesUnitType means nothing to reanimate into: consume the request and stay dead.
        int srcIdx = unitLibrary.Value.FindByUnitType(unitData.unitType);
        UnitType becomes = srcIdx >= 0 ? unitLibrary.Value.units[srcIdx].becomesUnitType : UnitType.None;
        if (becomes == UnitType.None || !swapBrainLookup.HasComponent(entity))
        {
            reviveRequestEnabled.ValueRW = false;
            return;
        }

        health.healthAmount   = health.healthAmountMax;
        reviveRequestEnabled.ValueRW = false;
        undeadEnabled.ValueRW = true;
        // Disabling Dead also drops this entity from [WithAll(Dead)], so the revive runs once.
        deadEnabled.ValueRW   = false;
        movementEnabled.ValueRW = true;
        gravityEnabled.ValueRW  = true;

        // DeathSystem re-enables PlayerInteractable on re-kill.
        if (playerInteractableLookup.HasComponent(entity))
            playerInteractableLookup.SetComponentEnabled(entity, false);

        // Clear the death latch so a re-killed reanimated unit re-enters DeathSystem.
        unitAction.current = ActionType.Idle;

        // The ragdoll Stop trigger rides on the Resurrection clip. Key 0 = no clip, skipped.
        if (animationCommandLookup.HasBuffer(entity) && animationCommandPendingLookup.HasComponent(entity))
        {
            ref UnitDataBlob unitBlob = ref unitLibrary.Value.units[srcIdx];
            uint resurrectionAnimationKey = AIUtils.GetAnimationKeyByAction(ref unitBlob, ActionType.Resurrection);
            uint deathFaceAnimationKey    = AIUtils.GetFaceAnimationKeyByAction(ref unitBlob, ActionType.Death);
            uint resurrectionFaceKey      = AIUtils.GetFaceAnimationKeyByAction(ref unitBlob, ActionType.Resurrection);
            DynamicBuffer<AnimationCommand> animationCommands = animationCommandLookup[entity];

            if (resurrectionAnimationKey != 0)
                PlaybackApi.PlayAnimation(
                    ref animationCommands,
                    animationCommandPendingLookup.GetEnabledRefRW<AnimationCommandPending>(entity),
                    resurrectionAnimationKey);
            if (deathFaceAnimationKey != 0)
                PlaybackApi.StopAnimation(
                    ref animationCommands,
                    animationCommandPendingLookup.GetEnabledRefRW<AnimationCommandPending>(entity),
                    deathFaceAnimationKey);
            if (resurrectionFaceKey != 0)
                PlaybackApi.PlayAnimation(
                    ref animationCommands,
                    animationCommandPendingLookup.GetEnabledRefRW<AnimationCommandPending>(entity),
                    resurrectionFaceKey);
        }

        // SwapBrainSystem rebuilds faction/attacks later this frame. UtilityBrain stays disabled:
        // a revived minion is player-driven. Single-threaded .Schedule() keeps lookup writes safe.
        swapBrainLookup[entity] = new SwapBrainRequest { newUnit = becomes };
        swapBrainLookup.SetComponentEnabled(entity, true);

        if (minionLookup.HasComponent(entity))
            minionLookup.SetComponentEnabled(entity, true);
        if (playerBrainLookup.HasComponent(entity))
            playerBrainLookup.SetComponentEnabled(entity, true);

        // Interrupt tears down DeathAction so action selection re-enters next frame.
        if (interruptLookup.HasComponent(entity))
            interruptLookup.SetComponentEnabled(entity, true);
    }
}
