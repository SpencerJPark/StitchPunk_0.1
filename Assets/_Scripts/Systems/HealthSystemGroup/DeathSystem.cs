using DotsMovementToolkit;
using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(HealthSystemGroup))]
[UpdateBefore(typeof(ReviveRequestSystem))]
public partial struct DeathSystem : ISystem
{
    private ComponentLookup<ActionInterruptRequest> interruptLookup;
    private ComponentLookup<AttackRequest>          pendingAttackLookup;
    private ComponentLookup<UtilityBrain>           utilityBrainLookup;
    private ComponentLookup<PlayerInteractable>     playerInteractableLookup;
    private BufferLookup<ThreatEntry>               threatLookup;
    private BufferLookup<MotivationChangeRequest>   motivationRequestLookup;
    private BufferLookup<RecentInteraction>         recentInteractionLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        interruptLookup          = state.GetComponentLookup<ActionInterruptRequest>(false);
        pendingAttackLookup      = state.GetComponentLookup<AttackRequest>(false);
        utilityBrainLookup       = state.GetComponentLookup<UtilityBrain>(false);
        playerInteractableLookup = state.GetComponentLookup<PlayerInteractable>(false);
        threatLookup             = state.GetBufferLookup<ThreatEntry>(false);
        motivationRequestLookup  = state.GetBufferLookup<MotivationChangeRequest>(false);
        recentInteractionLookup  = state.GetBufferLookup<RecentInteraction>(false);
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

        bool loggingEnabled = !SystemAPI.TryGetSingleton<LoggingConfig>(out LoggingConfig loggingCfg)
            || (loggingCfg.EnabledCategories & (int)LogCategory.Health) != 0;

        EntityCommandBuffer ecb = loggingEnabled
            ? SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged)
            : default;

        state.Dependency = new DeathJob
        {
            interruptLookup          = interruptLookup,
            pendingAttackLookup      = pendingAttackLookup,
            utilityBrainLookup       = utilityBrainLookup,
            playerInteractableLookup = playerInteractableLookup,
            threatLookup             = threatLookup,
            motivationRequestLookup  = motivationRequestLookup,
            recentInteractionLookup  = recentInteractionLookup,
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
    public ComponentLookup<ActionInterruptRequest> interruptLookup;
    public ComponentLookup<AttackRequest>          pendingAttackLookup;
    public ComponentLookup<UtilityBrain>           utilityBrainLookup;
    public ComponentLookup<PlayerInteractable>     playerInteractableLookup;
    public BufferLookup<ThreatEntry>               threatLookup;
    public BufferLookup<MotivationChangeRequest>   motivationRequestLookup;
    public BufferLookup<RecentInteraction>         recentInteractionLookup;
    public EntityCommandBuffer ecb;
    public bool                loggingEnabled;
    public double              timestamp;

    public void Execute(
        Entity entity,
        in Dead dead,
        in Health health,
        in LocalTransform transform,
        ref UnitAction unitAction,
        ref Movement mover,
        EnabledRefRW<PathRequest>      pathRequestEnabled,
        EnabledRefRW<DStarLiteFollower> dStarEnabled,
        EnabledRefRW<FlowFieldFollower> flowFieldEnabled,
        EnabledRefRW<HordeMembership>  hordeMembershipEnabled,
        EnabledRefRW<Movement>         movementEnabled,
        EnabledRefRW<Gravity>          gravityEnabled)
    {
        // Guard: only run the first-death-frame work once. The job itself sets
        // unitAction.current = Death below, so this self-latches; subsequent frames the
        // unit is already fully in death state — skip to avoid re-triggering
        // ActionInterruptRequest every frame while dead. Revive resets current to Idle,
        // which re-arms this latch for a re-killed reanimated minion.
        if (unitAction.current == ActionType.Death)
            return;

        // 1. Flip life/death state flags
        unitAction.current             = ActionType.Death;
        pathRequestEnabled.ValueRW     = false;
        dStarEnabled.ValueRW           = false;
        flowFieldEnabled.ValueRW       = false;
        hordeMembershipEnabled.ValueRW = false;
        // Movement/Gravity disabled on death — Ragdoll2DSystem drives the corpse from here.
        movementEnabled.ValueRW        = false;
        gravityEnabled.ValueRW         = false;

        // 2. Stop movement and snap target
        mover.isMoving       = false;
        mover.targetPosition = transform.Position;

        // 2b. Corpse becomes targetable by the player's reviver (PlayerTargetingSystem scans
        //     PlayerInteractable). Only revivable units (UndeadAuthoring) carry the component,
        //     so this is a no-op on everything else.
        if (playerInteractableLookup.HasComponent(entity))
            playerInteractableLookup.SetComponentEnabled(entity, true);

        // 3. Fire ActionInterruptRequest — the interrupt system will disable the current
        //    action tag cleanly and transition to DeathAction next frame.
        if (interruptLookup.HasComponent(entity))
            interruptLookup.SetComponentEnabled(entity, true);

        // 4. Cancel any in-flight attack — reset the fields AND disable it, so no stale
        //    targetEntity/hitFired/elapsed survives into a revived minion (ghost hits).
        if (pendingAttackLookup.HasComponent(entity))
        {
            pendingAttackLookup[entity] = default;
            pendingAttackLookup.SetComponentEnabled(entity, false);
        }

        // 5. Disable the UtilityBrain — death cuts off the decision/awareness half (the "utility AI").
        //    The StateMachine/execution half keeps running (BehaviorExecution/Interrupt are
        //    WithPresent, not WithAll), so the death behavior still plays. A blank slate: a player
        //    revive re-routes decisions through PlayerUnitBrain, not the utility brain.
        if (utilityBrainLookup.HasComponent(entity))
            utilityBrainLookup.SetComponentEnabled(entity, false);

        // 6. Wipe stale per-unit decision state so a revived minion starts clean — old threats,
        //    queued motivation changes, and interaction cooldowns must not carry across death.
        if (threatLookup.HasBuffer(entity))
            threatLookup[entity].Clear();
        if (motivationRequestLookup.HasBuffer(entity))
            motivationRequestLookup[entity].Clear();
        if (recentInteractionLookup.HasBuffer(entity))
            recentInteractionLookup[entity].Clear();

        if (loggingEnabled)
            LogUtil.Log(ref ecb,
                $"[Death] Entity {entity.Index} died. Interrupt: {interruptLookup.HasComponent(entity)}. AttackCancelled: {pendingAttackLookup.HasComponent(entity)}",
                LogLevel.Info, timestamp, category: LogCategory.Health);
    }
}
