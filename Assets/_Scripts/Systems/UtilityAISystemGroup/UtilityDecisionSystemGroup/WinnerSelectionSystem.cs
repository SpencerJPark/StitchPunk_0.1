using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

// Two-pass winner selection for UtilityBrainV2 units.
// Pass 1 (WinnerCollectJob): each active action entity buckets itself by ownerEntity.
// Pass 2 (WinnerSelectJob): each unit iterates candidates, picks priority tier + highest score,
//         writes to StateMachine only when the winner changes.
// Decision throttled by DecideCadence; no update until remaining <= 0.
[BurstCompile]
[UpdateInGroup(typeof(UtilityDecisionSystemGroup), OrderLast = true)]
public partial struct WinnerSelectionSystem : ISystem
{
    private EntityQuery _candidateQuery;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<BrainLibrary>();
        _candidateQuery = SystemAPI.QueryBuilder().WithAll<ActionActive, UtilityAction>().Build();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        int candidateCount = _candidateQuery.CalculateEntityCount();
        NativeParallelMultiHashMap<Entity, WinnerCandidate> candidates =
            new NativeParallelMultiHashMap<Entity, WinnerCandidate>(
                math.max(candidateCount, 1), Allocator.TempJob);

        BrainLibrary brainLibrary  = SystemAPI.GetSingleton<BrainLibrary>();
        float    deltaTime = SystemAPI.Time.DeltaTime;

        state.Dependency  = new WinnerCollectJob
        {
            candidates = candidates.AsParallelWriter(),
            aiConfig   = brainLibrary.blob,
        }.ScheduleParallel(state.Dependency);

        state.Dependency  = new WinnerSelectJob
        {
            candidates = candidates,
            deltaTime  = deltaTime,
        }.ScheduleParallel(state.Dependency);

        candidates.Dispose(state.Dependency);
        state.Dependency = state.Dependency;
    }
}

public struct WinnerCandidate
{
    public ActionType   action;
    public BehaviorType behavior;
    public Entity       targetEntity;
    public float        totalUtility;
    public int          priority;
}

[BurstCompile]
[WithAll(typeof(ActionActive))]
public partial struct WinnerCollectJob : IJobEntity
{
    [ReadOnly] public BlobAssetReference<AIConfigBlob> aiConfig;
    public NativeParallelMultiHashMap<Entity, WinnerCandidate>.ParallelWriter candidates;

    public void Execute(in UtilityAction utilityAction)
    {
        ref ActionDefBlob actionDef = ref aiConfig.Value.actionDefs[utilityAction.actionDefIndex];
        candidates.Add(utilityAction.ownerEntity, new WinnerCandidate
        {
            action       = utilityAction.action,
            behavior     = actionDef.behavior,
            targetEntity = utilityAction.targetEntity,
            totalUtility = utilityAction.totalUtility,
            priority     = utilityAction.priority,
        });
    }
}

[BurstCompile]
[WithAll(typeof(UtilityBrainV2))]
public partial struct WinnerSelectJob : IJobEntity
{
    [ReadOnly] public NativeParallelMultiHashMap<Entity, WinnerCandidate> candidates;
    public float deltaTime;

    public void Execute(Entity unit, ref StateMachine stateMachine, ref DecideCadence cadence)
    {
        cadence.remaining -= deltaTime;
        if (cadence.remaining > 0f) return;
        cadence.remaining = cadence.interval;

        if (!candidates.TryGetFirstValue(unit, out WinnerCandidate best,
            out NativeParallelMultiHashMapIterator<Entity> iterator))
            return;

        while (candidates.TryGetNextValue(out WinnerCandidate next, ref iterator))
        {
            if (next.priority > best.priority ||
                (next.priority == best.priority && next.totalUtility > best.totalUtility))
                best = next;
        }

        if (best.action == stateMachine.action && best.targetEntity == stateMachine.targetEntity)
            return;

        stateMachine.action              = best.action;
        stateMachine.activeBehavior      = best.behavior;
        stateMachine.targetEntity        = best.targetEntity;
        stateMachine.currentPhase        = BehaviorPhase.Approach;
        stateMachine.CurrentCommandIndex = 0;
        stateMachine.CommandTimer        = 0f;
    }
}
