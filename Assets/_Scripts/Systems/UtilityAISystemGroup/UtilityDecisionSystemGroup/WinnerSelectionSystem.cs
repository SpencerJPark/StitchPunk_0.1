using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

// Reads the scored UtilityActions buffer and writes the winning action to StateMachine.
// Selection rules (in order):
//   1. Player-ordered entry (isPlayerOrdered == true) always wins unconditionally.
//   2. Highest priority tier gate — lower-priority options are ignored.
//   3. Within the top tier: highest totalUtility wins.
// Does not interrupt an in-flight behavior with the same action.
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
[WithAll(typeof(UtilityBrain))]
public partial struct WinnerSelectJob : IJobEntity
{
    [ReadOnly] public BlobAssetReference<BrainLibraryBlob> aiConfig;
    public EntityCommandBuffer.ParallelWriter ecb;
    public double timestamp;
    public bool loggingEnabled;

    public void Execute(
        [EntityIndexInQuery] int         entityIndex,
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
        if (best.actionType == stateMachine.action && stateMachine.activeBehavior != BehaviorType.None)
            return;
        if (best.actionType == stateMachine.action && best.targetEntity == stateMachine.targetEntity)
            return;

        BehaviorType behavior = BehaviorType.None;
        if (best.actionDefIndex >= 0 && best.actionDefIndex < aiConfig.Value.actionDefs.Length)
            behavior = aiConfig.Value.actionDefs[best.actionDefIndex].behavior;

        if (loggingEnabled)
            LogUtil.Log(ref ecb, entityIndex,
                $"[WinnerSelection] Selected action: {best.actionType} behavior: {behavior} utility: {best.totalUtility:G}",
                LogLevel.Info, timestamp, category: LogCategory.AI);

        stateMachine.action              = best.actionType;
        stateMachine.activeBehavior      = behavior;
        stateMachine.targetEntity        = best.targetEntity;
        stateMachine.currentPhase        = BehaviorPhase.Execute;
        stateMachine.CurrentCommandIndex = 0;
        stateMachine.CommandTimer        = 0f;
    }
}
