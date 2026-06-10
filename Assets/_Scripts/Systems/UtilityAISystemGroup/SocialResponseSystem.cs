using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

// Responder path of the Talk pipeline: consumes SocialInvite (enabled by the initiator's
// RequestSocialResponse command). Accept = this unit's StateMachine is set to Talk targeting the
// initiator — written directly when idle, or via the P2 pending fields when a behavior is live
// (BehaviorInterruptSystem runs the old behavior's cleanup and swaps later this same frame, since
// this system updates before the StateMachine group). Decline = invite disabled; the initiator
// then exits its WaitTime via the TargetNotEngagedWithSelf qualifier.
// No Dead filter on the query — dead invitees must still consume (decline) their invite.
[BurstCompile]
[UpdateInGroup(typeof(UtilityAISystemGroup))]
[UpdateAfter(typeof(AIAwarenessSystemGroup))]
public partial struct SocialResponseSystem : ISystem
{
    private ComponentLookup<StateMachine> _stateMachineLookup;
    private ComponentLookup<Dead>         _deadLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<BrainLibrary>();
        _stateMachineLookup = state.GetComponentLookup<StateMachine>(true);
        _deadLookup         = state.GetComponentLookup<Dead>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        _stateMachineLookup.Update(ref state);
        _deadLookup.Update(ref state);

        BrainLibrary brainLibrary = SystemAPI.GetSingleton<BrainLibrary>();

        bool loggingEnabled = !SystemAPI.TryGetSingleton<LoggingConfig>(out LoggingConfig loggingCfg)
            || (loggingCfg.EnabledCategories & (int)LogCategory.AI) != 0;

        EntityCommandBuffer.ParallelWriter ecb = SystemAPI
            .GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
            .CreateCommandBuffer(state.WorldUnmanaged)
            .AsParallelWriter();

        state.Dependency = new SocialResponseJob
        {
            stateMachineLookup = _stateMachineLookup,
            deadLookup         = _deadLookup,
            aiConfig           = brainLibrary.blob,
            ecb                = ecb,
            loggingEnabled     = loggingEnabled,
            timestamp          = SystemAPI.Time.ElapsedTime,
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(UtilityBrain), typeof(SocialInvite))]
[WithPresent(typeof(Dead))]
public partial struct SocialResponseJob : IJobEntity
{
    // Read-only lookup aliases the StateMachine this job writes by ref. Safe: the lookup only
    // reads the INITIATOR's StateMachine, and each invitee writes only its own (same documented
    // pattern as BehaviorExecutionJob).
    [ReadOnly] [NativeDisableContainerSafetyRestriction]
    public ComponentLookup<StateMachine> stateMachineLookup;

    [ReadOnly] public ComponentLookup<Dead>                deadLookup;
    [ReadOnly] public BlobAssetReference<BrainLibraryBlob> aiConfig;
    public EntityCommandBuffer.ParallelWriter ecb;
    public bool   loggingEnabled;
    public double timestamp;

    public void Execute(
        [EntityIndexInQuery] int    entityIndex,
        Entity                      self,
        in UtilityBrain             brain,
        ref SocialInvite            socialInvite, // ref, not in: must share one RW type handle with the EnabledRef (aliasing)
        ref StateMachine            stateMachine,
        EnabledRefRW<SocialInvite>  socialInviteEnabled,
        EnabledRefRO<Dead>          deadEnabled)
    {
        // Invites are consumed in one frame, accepted or not — never left to go stale.
        socialInviteEnabled.ValueRW = false;

        Entity initiator = socialInvite.initiator;

        // Initiator must still be alive, in Talk, and targeting this unit.
        if (initiator == Entity.Null
            || !stateMachineLookup.TryGetComponent(initiator, out StateMachine initiatorStateMachine)
            || initiatorStateMachine.action != ActionType.Talk
            || initiatorStateMachine.targetEntity != self
            || !deadLookup.HasComponent(initiator)
            || deadLookup.IsComponentEnabled(initiator))
            return;

        // Already paired with this initiator — the responder's own RequestSocialResponse
        // round-trip lands here. Nothing to do.
        if (stateMachine.action == ActionType.Talk && stateMachine.targetEntity == initiator)
            return;

        int talkDefIndex = BrainBlobUtils.GetActionDefIndex(ref aiConfig.Value, brain.unitType, ActionType.Talk);
        if (talkDefIndex < 0) return; // unit can't talk — decline
        int talkPriority = aiConfig.Value.actionDefs[talkDefIndex].priority;

        bool busy = deadEnabled.ValueRO
            || stateMachine.action.IsCombatAction()
            || stateMachine.activePriority > talkPriority;

        if (busy)
        {
            if (loggingEnabled)
                LogUtil.Log(ref ecb, entityIndex,
                    $"[SocialResponse] Entity {self.Index} declined invite from {initiator.Index} (action: {stateMachine.action.Name()}, priority: {stateMachine.activePriority})",
                    LogLevel.Info, timestamp, category: LogCategory.AI);
            return;
        }

        if (stateMachine.activeBehavior == BehaviorType.None)
        {
            // Idle — assign directly. WinnerSelection runs later this frame and only writes
            // pending when outranked, so this won't be clobbered.
            stateMachine.action              = ActionType.Talk;
            stateMachine.activeBehavior      = BehaviorType.Talk;
            stateMachine.targetEntity        = initiator;
            stateMachine.activePriority      = talkPriority;
            stateMachine.currentPhase        = BehaviorPhase.Execute;
            stateMachine.CurrentCommandIndex = 0;
            stateMachine.CommandTimer        = 0f;
            stateMachine.LoopTimer           = 0f;
            stateMachine.LoopIterations      = 0;
        }
        else
        {
            // Mid-behavior (e.g. Wander) — go through the pending path so
            // BehaviorInterruptSystem runs the old behavior's cleanup before the swap.
            stateMachine.pendingAction   = ActionType.Talk;
            stateMachine.pendingBehavior = BehaviorType.Talk;
            stateMachine.pendingTarget   = initiator;
            stateMachine.pendingPriority = talkPriority;
        }

        if (loggingEnabled)
            LogUtil.Log(ref ecb, entityIndex,
                $"[SocialResponse] Entity {self.Index} accepted invite from {initiator.Index}",
                LogLevel.Info, timestamp, category: LogCategory.AI);
    }
}
