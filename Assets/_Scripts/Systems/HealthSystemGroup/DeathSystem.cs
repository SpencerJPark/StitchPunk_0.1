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

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        interruptLookup     = state.GetComponentLookup<ActionInterruptRequest>(false);
        pendingAttackLookup = state.GetComponentLookup<AttackRequest>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        interruptLookup.Update(ref state);
        pendingAttackLookup.Update(ref state);

        bool loggingEnabled = !SystemAPI.TryGetSingleton<LoggingConfig>(out LoggingConfig loggingCfg)
            || (loggingCfg.EnabledCategories & (int)LogCategory.Health) != 0;

        EntityCommandBuffer ecb = loggingEnabled
            ? SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged)
            : default;

        state.Dependency = new DeathJob
        {
            interruptLookup     = interruptLookup,
            pendingAttackLookup = pendingAttackLookup,
            ecb                 = ecb,
            loggingEnabled      = loggingEnabled,
            timestamp           = SystemAPI.Time.ElapsedTime,
        }.Schedule(state.Dependency);
    }
}

[BurstCompile]
[WithPresent(typeof(PathRequest))]
[WithPresent(typeof(DStarLiteFollower))]
[WithPresent(typeof(FlowFieldFollower))]
[WithPresent(typeof(HordeMembership))]
public partial struct DeathJob : IJobEntity
{
    public ComponentLookup<ActionInterruptRequest> interruptLookup;
    public ComponentLookup<AttackRequest>          pendingAttackLookup;
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
        EnabledRefRW<Alive>            aliveEnabled,
        EnabledRefRW<PathRequest>      pathRequestEnabled,
        EnabledRefRW<DStarLiteFollower> dStarEnabled,
        EnabledRefRW<FlowFieldFollower> flowFieldEnabled,
        EnabledRefRW<HordeMembership>  hordeMembershipEnabled)
    {
        // Guard: Alive is still enabled only on the first death frame.
        // Subsequent frames the unit is already fully in death state — skip to avoid
        // re-triggering ActionInterruptRequest on every frame while dead.
        if (!aliveEnabled.ValueRO)
            return;

        // 1. Flip life/death state flags
        unitAction.current             = ActionType.Death;
        aliveEnabled.ValueRW           = false;
        pathRequestEnabled.ValueRW     = false;
        dStarEnabled.ValueRW           = false;
        flowFieldEnabled.ValueRW       = false;
        hordeMembershipEnabled.ValueRW = false;

        // 2. Stop movement and snap target
        mover.isMoving       = false;
        mover.targetPosition = transform.Position;

        // 3. Fire ActionInterruptRequest — the interrupt system will disable the current
        //    action tag cleanly and transition to DeathAction next frame.
        if (interruptLookup.HasComponent(entity))
            interruptLookup.SetComponentEnabled(entity, true);

        // 4. Cancel any in-flight attack — prevents ghost hits from dead attackers.
        if (pendingAttackLookup.HasComponent(entity))
            pendingAttackLookup.SetComponentEnabled(entity, false);

        if (loggingEnabled)
            LogUtil.Log(ref ecb,
                $"[Death] Entity {entity.Index} died. Interrupt: {interruptLookup.HasComponent(entity)}. AttackCancelled: {pendingAttackLookup.HasComponent(entity)}",
                LogLevel.Info, timestamp, category: LogCategory.Health);
    }
}
