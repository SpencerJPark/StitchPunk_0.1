using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

// Picks the winner from UtilityActions: a player-ordered entry first, else top priority tier, then highest totalUtility.
// Never clobbers a live behavior: a winner that outranks activePriority goes into the StateMachine
// pending fields and BehaviorInterruptSystem performs the swap the same frame.
[BurstCompile]
[UpdateInGroup(typeof(ActionSelectionSystemGroup), OrderLast = true)]
public partial struct WinnerSelectionSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<BrainLibrary>();
        state.RequireForUpdate<BehaviorLibrary>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        BrainLibrary brainLibrary = SystemAPI.GetSingleton<BrainLibrary>();
        BehaviorLibrary behaviorLibrary = SystemAPI.GetSingleton<BehaviorLibrary>();
        EntityCommandBuffer.ParallelWriter ecb = SystemAPI
            .GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
            .CreateCommandBuffer(state.WorldUnmanaged)
            .AsParallelWriter();

        bool loggingEnabled = !SystemAPI.TryGetSingleton<LoggingConfig>(out LoggingConfig loggingCfg)
            || (loggingCfg.EnabledCategories & (int)LogCategory.AI) != 0;

        state.Dependency = new WinnerSelectJob
        {
            aiConfig        = brainLibrary.blob,
            behaviorLib     = behaviorLibrary.blob,
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
[WithDisabled(typeof(CutsceneActor))]
public partial struct WinnerSelectJob : IJobEntity
{
    [ReadOnly] public BlobAssetReference<BrainLibraryBlob> aiConfig;
    [ReadOnly] public BlobAssetReference<BehaviorLibraryBlob> behaviorLib;
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

        for (int actionIndex = 0; actionIndex < actions.Length; actionIndex++)
        {
            if (!actions[actionIndex].isPlayerOrdered) continue;
            best  = actions[actionIndex];
            found = true;
            break;
        }

        if (!found)
        {
            int highestPriority = int.MinValue;
            for (int actionIndex = 0; actionIndex < actions.Length; actionIndex++)
            {
                if (actions[actionIndex].priority > highestPriority)
                    highestPriority = actions[actionIndex].priority;
            }

            best.totalUtility = float.MinValue;
            for (int actionIndex = 0; actionIndex < actions.Length; actionIndex++)
            {
                UtilityActions candidate = actions[actionIndex];
                if (candidate.priority < highestPriority) continue;
                if (candidate.totalUtility <= best.totalUtility) continue;
                best  = candidate;
                found = true;
            }
        }

        if (!found) return;

        // Same action as the live one is left alone, except a player order to a different target
        // always re-targets (orders are consumed once, so it cannot re-preempt every frame).
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

            StateMachineTransitions.StartBehavior(ref stateMachine, best.actionType, behavior, best.targetEntity, best.targetPosition, best.hasTargetPosition, winnerPriority);
            return;
        }

        // Preempt only if winnerPriority > activePriority and >= the behavior's minimumInterruptPriority.
        int minimumInterruptPriority = 0;
        int activeBehaviorIndex = (int)stateMachine.activeBehavior;
        if (activeBehaviorIndex >= 0 && activeBehaviorIndex < behaviorLib.Value.behaviors.Length)
        {
            ref BehaviorConfigBlob activeBehaviorConfig = ref behaviorLib.Value.behaviors[activeBehaviorIndex];
            minimumInterruptPriority = activeBehaviorConfig.minimumInterruptPriority;
        }

        if (!best.isPlayerOrdered
            && (winnerPriority <= stateMachine.activePriority || winnerPriority < minimumInterruptPriority))
            return;

        if (loggingEnabled)
            LogUtil.Log(ref ecb, entityIndex,
                $"[WinnerSelection] Entity {entity.Index} preempting {stateMachine.activeBehavior.Name()} (priority {stateMachine.activePriority}, interrupt threshold {minimumInterruptPriority}) with {best.actionType.Name()} behavior: {behavior.Name()} (priority {winnerPriority})",
                LogLevel.Info, timestamp, category: LogCategory.AI);

        StateMachineTransitions.SetPending(ref stateMachine, best.actionType, behavior, best.targetEntity, best.targetPosition, best.hasTargetPosition, winnerPriority);
    }
}
