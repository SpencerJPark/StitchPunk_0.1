using DotsAnimationToolkit;
using DotsMovementToolkit;
using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(HealthSystemGroup))]
[UpdateBefore(typeof(ReviveRequestSystem))]
public partial struct DeathSystem : ISystem
{
    private ComponentLookup<ActionInterruptRequest>  interruptLookup;
    private ComponentLookup<AttackRequest>           pendingAttackLookup;
    private ComponentLookup<UtilityBrain>            utilityBrainLookup;
    private ComponentLookup<PlayerInteractable>      playerInteractableLookup;
    private BufferLookup<ThreatEntry>                threatLookup;
    private BufferLookup<MotivationChangeRequest>    motivationRequestLookup;
    private BufferLookup<RecentInteraction>          recentInteractionLookup;
    private BufferLookup<AnimationCommand>           animationCommandLookup;
    private ComponentLookup<AnimationCommandPending> animationCommandPendingLookup;
    private ComponentLookup<AStarFollower>           aStarFollowerLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<UnitDataLibrary>();
        interruptLookup               = state.GetComponentLookup<ActionInterruptRequest>(false);
        pendingAttackLookup           = state.GetComponentLookup<AttackRequest>(false);
        utilityBrainLookup            = state.GetComponentLookup<UtilityBrain>(false);
        playerInteractableLookup      = state.GetComponentLookup<PlayerInteractable>(false);
        threatLookup                  = state.GetBufferLookup<ThreatEntry>(false);
        motivationRequestLookup       = state.GetBufferLookup<MotivationChangeRequest>(false);
        recentInteractionLookup       = state.GetBufferLookup<RecentInteraction>(false);
        animationCommandLookup        = state.GetBufferLookup<AnimationCommand>(false);
        animationCommandPendingLookup = state.GetComponentLookup<AnimationCommandPending>(false);
        aStarFollowerLookup           = state.GetComponentLookup<AStarFollower>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        interruptLookup.Update(ref state);
        pendingAttackLookup.Update(ref state);
        utilityBrainLookup.Update(ref state);
        playerInteractableLookup.Update(ref state);
        threatLookup.Update(ref state);
        motivationRequestLookup.Update(ref state);
        recentInteractionLookup.Update(ref state);
        animationCommandLookup.Update(ref state);
        animationCommandPendingLookup.Update(ref state);
        aStarFollowerLookup.Update(ref state);

        bool loggingEnabled = !SystemAPI.TryGetSingleton<LoggingConfig>(out LoggingConfig loggingCfg)
            || (loggingCfg.EnabledCategories & (int)LogCategory.Health) != 0;

        EntityCommandBuffer ecb = loggingEnabled
            ? SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged)
            : default;

        BlobAssetReference<UnitLibraryBlob> unitLibrary =
            SystemAPI.GetSingleton<UnitDataLibrary>().library;

        state.Dependency = new DeathJob
        {
            interruptLookup               = interruptLookup,
            pendingAttackLookup           = pendingAttackLookup,
            utilityBrainLookup            = utilityBrainLookup,
            playerInteractableLookup      = playerInteractableLookup,
            threatLookup                  = threatLookup,
            motivationRequestLookup       = motivationRequestLookup,
            recentInteractionLookup       = recentInteractionLookup,
            animationCommandLookup        = animationCommandLookup,
            animationCommandPendingLookup = animationCommandPendingLookup,
            aStarFollowerLookup           = aStarFollowerLookup,
            unitLibrary                   = unitLibrary,
            ecb                     = ecb,
            loggingEnabled          = loggingEnabled,
            timestamp               = SystemAPI.Time.ElapsedTime,
        }.Schedule(state.Dependency);
    }
}

[BurstCompile]
[WithPresent(typeof(PathRequest))]
[WithPresent(typeof(DStarLiteFollower))]
[WithPresent(typeof(FlowFieldFollower))]
[WithPresent(typeof(HordeMembership))]
[WithPresent(typeof(Movement))]
[WithPresent(typeof(Gravity))]
public partial struct DeathJob : IJobEntity
{
    public ComponentLookup<ActionInterruptRequest>  interruptLookup;
    public ComponentLookup<AttackRequest>           pendingAttackLookup;
    public ComponentLookup<UtilityBrain>            utilityBrainLookup;
    public ComponentLookup<PlayerInteractable>      playerInteractableLookup;
    public BufferLookup<ThreatEntry>                threatLookup;
    public BufferLookup<MotivationChangeRequest>    motivationRequestLookup;
    public BufferLookup<RecentInteraction>          recentInteractionLookup;
    public BufferLookup<AnimationCommand>           animationCommandLookup;
    public ComponentLookup<AnimationCommandPending> animationCommandPendingLookup;
    // A lookup, not [WithPresent]: units baked before A* existed have no AStarFollower and must still die.
    public ComponentLookup<AStarFollower>           aStarFollowerLookup;
    public BlobAssetReference<UnitLibraryBlob>      unitLibrary;
    public EntityCommandBuffer ecb;
    public bool                loggingEnabled;
    public double              timestamp;

    public void Execute(
        Entity entity,
        in Dead dead,
        in Health health,
        in LocalTransform transform,
        in UnitData unitData,
        ref UnitAction unitAction,
        ref Movement mover,
        EnabledRefRW<PathRequest>      pathRequestEnabled,
        EnabledRefRW<DStarLiteFollower> dStarLiteFollowerEnabled,
        EnabledRefRW<FlowFieldFollower> flowFieldFollowerEnabled,
        EnabledRefRW<HordeMembership>  hordeMembershipEnabled,
        EnabledRefRW<Movement>         movementEnabled,
        EnabledRefRW<Gravity>          gravityEnabled)
    {
        // Self-latching: unitAction.current = Death below stops re-firing the interrupt each frame.
        // Revive resets current to Idle, which re-arms it.
        if (unitAction.current == ActionType.Death)
            return;

        unitAction.current             = ActionType.Death;
        pathRequestEnabled.ValueRW     = false;
        dStarLiteFollowerEnabled.ValueRW = false;
        flowFieldFollowerEnabled.ValueRW = false;
        if (aStarFollowerLookup.HasComponent(entity))
            aStarFollowerLookup.SetComponentEnabled(entity, false);
        hordeMembershipEnabled.ValueRW = false;
        // Ragdoll2DSystem drives the corpse from here.
        movementEnabled.ValueRW        = false;
        gravityEnabled.ValueRW         = false;

        mover.isMoving       = false;
        mover.targetPosition = transform.Position;

        // Makes the corpse targetable by the reviver (PlayerTargetingSystem); only UndeadAuthoring units carry it.
        if (playerInteractableLookup.HasComponent(entity))
            playerInteractableLookup.SetComponentEnabled(entity, true);

        // The interrupt system disables the current action tag and transitions to DeathAction next frame.
        if (interruptLookup.HasComponent(entity))
            interruptLookup.SetComponentEnabled(entity, true);

        // Reset fields AND disable, so no stale targetEntity/hitFired/elapsed survives a revive (ghost hits).
        if (pendingAttackLookup.HasComponent(entity))
        {
            pendingAttackLookup[entity] = default;
            pendingAttackLookup.SetComponentEnabled(entity, false);
        }

        // Only the decision half stops; the execution half is WithPresent so the death behavior still plays.
        if (utilityBrainLookup.HasComponent(entity))
            utilityBrainLookup.SetComponentEnabled(entity, false);

        // Threats, queued motivation changes and interaction cooldowns must not carry across death.
        if (threatLookup.HasBuffer(entity))
            threatLookup[entity].Clear();
        if (motivationRequestLookup.HasBuffer(entity))
            motivationRequestLookup[entity].Clear();
        if (recentInteractionLookup.HasBuffer(entity))
            recentInteractionLookup[entity].Clear();

        // Ragdoll Start/Stop triggers ride on the Death clip itself. Both keys are optional (0).
        int unitIndex = unitLibrary.Value.FindByUnitType(unitData.unitType);
        if (unitIndex >= 0
            && animationCommandLookup.HasBuffer(entity)
            && animationCommandPendingLookup.HasComponent(entity))
        {
            ref UnitDataBlob unitBlob = ref unitLibrary.Value.units[unitIndex];
            uint deathAnimationKey     = AIUtils.GetAnimationKeyByAction(ref unitBlob, ActionType.Death);
            uint deathFaceAnimationKey = AIUtils.GetFaceAnimationKeyByAction(ref unitBlob, ActionType.Death);
            DynamicBuffer<AnimationCommand> animationCommands = animationCommandLookup[entity];

            if (deathAnimationKey != 0)
                PlaybackApi.PlayAnimation(
                    ref animationCommands,
                    animationCommandPendingLookup.GetEnabledRefRW<AnimationCommandPending>(entity),
                    deathAnimationKey);
            if (deathFaceAnimationKey != 0)
                PlaybackApi.PlayAnimation(
                    ref animationCommands,
                    animationCommandPendingLookup.GetEnabledRefRW<AnimationCommandPending>(entity),
                    deathFaceAnimationKey);
        }

        if (loggingEnabled)
            LogUtil.Log(ref ecb,
                $"[Death] Entity {entity.Index} died. Interrupt: {interruptLookup.HasComponent(entity)}. AttackCancelled: {pendingAttackLookup.HasComponent(entity)}",
                LogLevel.Info, timestamp, category: LogCategory.Health);
    }
}
