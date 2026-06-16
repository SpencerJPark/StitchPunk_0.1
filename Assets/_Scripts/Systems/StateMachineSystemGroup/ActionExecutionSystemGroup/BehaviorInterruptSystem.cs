using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

// The single teardown path for a live behavior. Triggered by either:
//   (a) ActionInterruptRequest — death / revive / path-stuck — resets the unit to Idle, or
//   (b) StateMachine pending fields — WinnerSelection preemption — swaps to the pending behavior.
// Runs OrderFirst in ActionExecutionSystemGroup: after WinnerSelection (selection group precedes
// execution group) and before BehaviorExecutionSystem, so pending never lingers past the frame.
// Executes the active behavior's interruptionCleanup blob in one pass (non-blocking commands only;
// bake validation flags blocking types), then halts pathing and cancels any in-flight attack.
// Does NOT exclude Dead — death interrupts must process.
[BurstCompile]
[UpdateInGroup(typeof(ActionExecutionSystemGroup), OrderFirst = true)]
public partial struct BehaviorInterruptSystem : ISystem
{
    private ComponentLookup<AttackRequest>    _attackRequestLookup;
    private ComponentLookup<AnimationRequest> _animationRequestLookup;
    private BufferLookup<SetAnimation>        _setAnimationLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<BehaviorLibrary>();
        _attackRequestLookup    = state.GetComponentLookup<AttackRequest>(false);
        _animationRequestLookup = state.GetComponentLookup<AnimationRequest>(false);
        _setAnimationLookup     = state.GetBufferLookup<SetAnimation>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        _attackRequestLookup.Update(ref state);
        _animationRequestLookup.Update(ref state);
        _setAnimationLookup.Update(ref state);

        BehaviorLibrary behaviorLib = SystemAPI.GetSingleton<BehaviorLibrary>();
        EntityCommandBuffer.ParallelWriter ecb = SystemAPI
            .GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
            .CreateCommandBuffer(state.WorldUnmanaged)
            .AsParallelWriter();

        bool loggingEnabled = !SystemAPI.TryGetSingleton<LoggingConfig>(out LoggingConfig loggingCfg)
            || (loggingCfg.EnabledCategories & (int)LogCategory.StateMachine) != 0;

        state.Dependency = new BehaviorInterruptJob
        {
            behaviorLib            = behaviorLib.blob,
            attackRequestLookup    = _attackRequestLookup,
            animationRequestLookup = _animationRequestLookup,
            setAnimationLookup     = _setAnimationLookup,
            ecb                    = ecb,
            timestamp              = SystemAPI.Time.ElapsedTime,
            loggingEnabled         = loggingEnabled,
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
// WithPresent (not WithAll): teardown must run with UtilityBrain disabled — the death interrupt
// fires on corpses (UtilityBrain off) and pending-preemption swaps happen on player minions.
[WithPresent(typeof(UtilityBrain))]
[WithPresent(typeof(ActionInterruptRequest))]
[WithPresent(typeof(PathRequest))]
public partial struct BehaviorInterruptJob : IJobEntity
{
    [ReadOnly] public BlobAssetReference<BehaviorLibraryBlob> behaviorLib;

    // NativeDisableParallelForRestriction is safe: each lookup write targets the executing
    // unit itself, so no two threads write to the same component.
    [NativeDisableParallelForRestriction] public ComponentLookup<AttackRequest>    attackRequestLookup;
    [NativeDisableParallelForRestriction] public ComponentLookup<AnimationRequest> animationRequestLookup;
    [NativeDisableParallelForRestriction] public BufferLookup<SetAnimation>        setAnimationLookup;

    public EntityCommandBuffer.ParallelWriter ecb;
    public double timestamp;
    public bool   loggingEnabled;

    public void Execute(
        [EntityIndexInQuery] int             entityIndex,
        Entity                               unit,
        ref StateMachine                     stateMachine,
        ref PathRequest                      pathRequest,
        EnabledRefRW<PathRequest>            pathRequestEnabled,
        EnabledRefRW<ActionInterruptRequest> actionInterruptRequestEnabled,
        ref DynamicBuffer<UtilityActions>    actions,
        ref DynamicBuffer<RecentInteraction> recentInteractions)
    {
        bool interrupted = actionInterruptRequestEnabled.ValueRO;
        bool hasPending  = stateMachine.pendingBehavior != BehaviorType.None;
        if (!interrupted && !hasPending) return;

        // Tear down the live behavior: authored cleanup first, then the generic teardown that
        // every behavior needs regardless of authoring.
        if (stateMachine.activeBehavior != BehaviorType.None)
        {
            ref BehaviorConfigBlob behavior =
                ref behaviorLib.Value.behaviors[(int)stateMachine.activeBehavior];

            for (int i = 0; i < behavior.interruptionCleanup.Length; i++)
            {
                ref BehaviorCommand cmd = ref behavior.interruptionCleanup[i];
                RunCleanupCommand(entityIndex, unit, ref cmd, ref stateMachine, ref recentInteractions);
            }

            AIUtils.HaltPathing(ref pathRequest, pathRequestEnabled);

            if (attackRequestLookup.HasComponent(unit))
                attackRequestLookup.SetComponentEnabled(unit, false);
        }

        // Interrupt beats pending: death/revive/stuck resets the unit to Idle and re-decides;
        // a pending preemption carries the chosen action straight into execution.
        if (interrupted)
        {
            if (loggingEnabled)
                LogUtil.Log(ref ecb, entityIndex,
                    $"[BehaviorInterrupt] Entity {unit.Index} interrupted {stateMachine.activeBehavior.Name()} (action: {stateMachine.action.Name()}) — reset to Idle",
                    LogLevel.Info, timestamp, category: LogCategory.StateMachine);

            stateMachine.action                   = ActionType.Idle;
            stateMachine.activeBehavior           = BehaviorType.None;
            stateMachine.targetEntity             = Entity.Null;
            stateMachine.targetPosition           = default;
            stateMachine.hasTargetPosition        = false;
            stateMachine.activePriority           = 0;
            stateMachine.pendingAction            = ActionType.Idle;
            stateMachine.pendingBehavior          = BehaviorType.None;
            stateMachine.pendingTarget            = Entity.Null;
            stateMachine.pendingTargetPosition    = default;
            stateMachine.pendingHasTargetPosition = false;
            stateMachine.pendingPriority          = 0;

            // ClearOptions excludes dead units, so a corpse would otherwise keep stale options.
            actions.Clear();
            actionInterruptRequestEnabled.ValueRW = false;
        }
        else
        {
            if (loggingEnabled)
                LogUtil.Log(ref ecb, entityIndex,
                    $"[BehaviorInterrupt] Entity {unit.Index} preempted {stateMachine.activeBehavior.Name()} (action: {stateMachine.action.Name()}) with {stateMachine.pendingBehavior.Name()} (action: {stateMachine.pendingAction.Name()})",
                    LogLevel.Info, timestamp, category: LogCategory.StateMachine);

            stateMachine.action                   = stateMachine.pendingAction;
            stateMachine.activeBehavior           = stateMachine.pendingBehavior;
            stateMachine.targetEntity             = stateMachine.pendingTarget;
            stateMachine.targetPosition           = stateMachine.pendingTargetPosition;
            stateMachine.hasTargetPosition        = stateMachine.pendingHasTargetPosition;
            stateMachine.activePriority           = stateMachine.pendingPriority;
            stateMachine.pendingAction            = ActionType.Idle;
            stateMachine.pendingBehavior          = BehaviorType.None;
            stateMachine.pendingTarget            = Entity.Null;
            stateMachine.pendingTargetPosition    = default;
            stateMachine.pendingHasTargetPosition = false;
            stateMachine.pendingPriority          = 0;
        }

        stateMachine.currentPhase        = BehaviorPhase.Execute;
        // Back to walking — a swapped-in pending behavior re-sets stance via its own
        // Approach/FleeFromTarget command on its first Execute tick.
        stateMachine.currentStance       = StanceType.Normal;
        stateMachine.CurrentCommandIndex = 0;
        stateMachine.CommandTimer        = 0f;
        stateMachine.LoopTimer           = 0f;
        stateMachine.LoopIterations      = 0;
    }

    // Cleanup runs in one frame, so only non-blocking commands are legal here. Bake validation
    // (BehaviorLibraryBakingSystem) flags blocking types at author time; this is the runtime guard.
    private void RunCleanupCommand(
        int                                  entityIndex,
        Entity                               unit,
        ref BehaviorCommand                  cmd,
        ref StateMachine                     stateMachine,
        ref DynamicBuffer<RecentInteraction> recentInteractions)
    {
        switch (cmd.type)
        {
            case BehaviorCommandType.ModifyMotivation:
                ecb.AppendToBuffer(entityIndex, unit, new MotivationChangeRequest
                {
                    needType   = (NeedType)cmd.IntParam,
                    changeType = MotivationChangeType.Add,
                    value      = cmd.FloatParam,
                });
                break;

            case BehaviorCommandType.ReleaseInteraction:
                if (stateMachine.targetEntity != Entity.Null)
                {
                    float cooldownEnd = (float)timestamp + (cmd.FloatParam > 0f ? cmd.FloatParam : 30f);
                    if (recentInteractions.Length >= 8) recentInteractions.RemoveAt(0);
                    recentInteractions.Add(new RecentInteraction
                    {
                        entity          = stateMachine.targetEntity,
                        cooldownEndTime = cooldownEnd,
                    });
                }
                break;

            case BehaviorCommandType.StopAnimation:
                if (animationRequestLookup.HasComponent(unit) && setAnimationLookup.HasBuffer(unit))
                {
                    setAnimationLookup[unit].Add(new SetAnimation
                    {
                        layer     = AnimationLayerType.Action,
                        animation = AnimationType.None,
                        speed     = 1f,
                        looping   = false,
                    });
                    animationRequestLookup.SetComponentEnabled(unit, true);
                }
                break;

            default:
                if (loggingEnabled)
                    LogUtil.Log(ref ecb, entityIndex,
                        $"[BehaviorInterrupt] Skipped {cmd.type.Name()} in {stateMachine.activeBehavior.Name()} interruptionCleanup — only non-blocking cleanup commands are supported",
                        LogLevel.Warning, timestamp, category: LogCategory.StateMachine);
                break;
        }
    }
}
