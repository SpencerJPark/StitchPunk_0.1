using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

// Reads the scored UtilityActions buffer and writes the winning action to StateMachine.
// Selection rules (in order):
//   1. Player-ordered entry (isPlayerOrdered == true) always wins unconditionally.
//   2. Highest priority tier gate — lower-priority options are ignored.
//   3. Within the top tier: highest totalUtility wins.
// Does not interrupt an in-flight behavior with the same action.
// Never clobbers a live behavior: when one is active, the winner is written to the StateMachine
// pending fields only if it outranks activePriority; BehaviorInterruptSystem performs the swap
// same-frame after running interruptionCleanup.
[BurstCompile]
[UpdateInGroup(typeof(ActionSelectionSystemGroup), OrderLast = true)]
public partial struct WinnerSelectionSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<BrainLibrary>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        BrainLibrary brainLibrary = SystemAPI.GetSingleton<BrainLibrary>();
        EntityCommandBuffer.ParallelWriter ecb = SystemAPI
            .GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
            .CreateCommandBuffer(state.WorldUnmanaged)
            .AsParallelWriter();

        bool loggingEnabled = !SystemAPI.TryGetSingleton<LoggingConfig>(out LoggingConfig loggingCfg)
            || (loggingCfg.EnabledCategories & (int)LogCategory.AI) != 0;

        state.Dependency = new WinnerSelectJob
        {
            aiConfig        = brainLibrary.blob,
            ecb             = ecb,
            timestamp       = SystemAPI.Time.ElapsedTime,
            loggingEnabled  = loggingEnabled,
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
// WithPresent (not WithAll): selection must turn a player minion's UtilityActions into a
// StateMachine decision even though its UtilityBrain is disabled (decisions come from the player).
[WithPresent(typeof(UtilityBrain))]
[WithDisabled(typeof(Dead))]
public partial struct WinnerSelectJob : IJobEntity
{
    [ReadOnly] public BlobAssetReference<BrainLibraryBlob> aiConfig;
    public EntityCommandBuffer.ParallelWriter ecb;
    public double timestamp;
    public bool loggingEnabled;

    public void Execute(
        [EntityIndexInQuery] int         entityIndex,
        Entity                           entity,
        ref StateMachine                 stateMachine,
        in DynamicBuffer<UtilityActions> actions)
    {
        if (actions.IsEmpty) return;

        UtilityActions best   = default;
        bool           found  = false;

        // Player-ordered entry wins unconditionally — check first.
        for (int i = 0; i < actions.Length; i++)
        {
            if (!actions[i].isPlayerOrdered) continue;
            best  = actions[i];
            found = true;
            break;
        }

        // Otherwise: priority tier gate + highest utility.
        if (!found)
        {
            int highestPriority = int.MinValue;
            for (int i = 0; i < actions.Length; i++)
            {
                if (actions[i].priority > highestPriority)
                    highestPriority = actions[i].priority;
            }

            best.totalUtility = float.MinValue;
            for (int i = 0; i < actions.Length; i++)
            {
                UtilityActions candidate = actions[i];
                if (candidate.priority < highestPriority) continue;
                if (candidate.totalUtility <= best.totalUtility) continue;
                best  = candidate;
                found = true;
            }
        }

        if (!found) return;

        // Don't interrupt an in-flight behavior with the same action — let it complete naturally.
        // Exception: a player order to a different target/destination always re-targets (orders
        // are one-shot consumed by MinionActionSelectionSystem, so this can't re-preempt every frame).
        bool sameTarget = best.targetEntity == stateMachine.targetEntity
            && best.hasTargetPosition == stateMachine.hasTargetPosition
            && math.all(best.targetPosition == stateMachine.targetPosition);
        bool playerRetarget = best.isPlayerOrdered && !sameTarget;
        if (!playerRetarget)
        {
            if (best.actionType == stateMachine.action && stateMachine.activeBehavior != BehaviorType.None)
                return;
            if (best.actionType == stateMachine.action && sameTarget)
                return;
        }

        BehaviorType behavior = BehaviorType.None;
        if (best.actionDefIndex >= 0 && best.actionDefIndex < aiConfig.Value.actionDefs.Length)
            behavior = aiConfig.Value.actionDefs[best.actionDefIndex].behavior;

        int winnerPriority = best.isPlayerOrdered ? int.MaxValue : best.priority;

        if (stateMachine.activeBehavior == BehaviorType.None)
        {
            if (loggingEnabled)
                LogUtil.Log(ref ecb, entityIndex,
                    $"[WinnerSelection] Entity {entity.Index} selected action: {best.actionType.Name()} behavior: {behavior.Name()} utility: {best.totalUtility:G}",
                    LogLevel.Info, timestamp, category: LogCategory.AI);

            stateMachine.action              = best.actionType;
            stateMachine.activeBehavior      = behavior;
            stateMachine.targetEntity        = best.targetEntity;
            stateMachine.targetPosition      = best.targetPosition;
            stateMachine.hasTargetPosition   = best.hasTargetPosition;
            stateMachine.activePriority      = winnerPriority;
            stateMachine.currentPhase        = BehaviorPhase.Execute;
            stateMachine.CurrentCommandIndex = 0;
            stateMachine.CommandTimer        = 0f;
            stateMachine.LoopTimer           = 0f;
            stateMachine.LoopIterations      = 0;
            return;
        }

        // A behavior is live — preempt only when the winner outranks it. Pending fields are
        // consumed by BehaviorInterruptSystem this frame; live state is never touched here.
        if (winnerPriority <= stateMachine.activePriority && !best.isPlayerOrdered)
            return;

        if (loggingEnabled)
            LogUtil.Log(ref ecb, entityIndex,
                $"[WinnerSelection] Entity {entity.Index} preempting {stateMachine.activeBehavior.Name()} (priority {stateMachine.activePriority}) with {best.actionType.Name()} behavior: {behavior.Name()} (priority {winnerPriority})",
                LogLevel.Info, timestamp, category: LogCategory.AI);

        stateMachine.pendingAction            = best.actionType;
        stateMachine.pendingBehavior          = behavior;
        stateMachine.pendingTarget            = best.targetEntity;
        stateMachine.pendingTargetPosition    = best.targetPosition;
        stateMachine.pendingHasTargetPosition = best.hasTargetPosition;
        stateMachine.pendingPriority          = winnerPriority;
    }
}
