using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Runs the Approach → Execute → Complete phase machine for UtilityBrainV2 units.
// Approach: resolves a concrete target from AwarenessTarget buffer (Self actions), requests pathing
//           via AIUtils.BeginPathRequest, and detects arrival via pathRequest.targetPosition distance.
// Execute:  steps through BehaviorConfigBlob.executionSequence (WaitTime etc.).
// Complete: clears state; WinnerSelectionSystem picks the next action on the next cadence tick.
[BurstCompile]
[UpdateInGroup(typeof(ActionSelectionSystemGroup))]
public partial struct BehaviorExecutionSystem : ISystem
{
    private ComponentLookup<LocalTransform> _transformLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
        state.RequireForUpdate<BehaviorLibrary>();
        _transformLookup = state.GetComponentLookup<LocalTransform>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        _transformLookup.Update(ref state);
        BehaviorLibrary behaviorLib = SystemAPI.GetSingleton<BehaviorLibrary>();
        float deltaTime = SystemAPI.Time.DeltaTime;

        state.Dependency = new BehaviorExecutionJob
        {
            behaviorLib      = behaviorLib.blob,
            transformLookup  = _transformLookup,
            deltaTime        = deltaTime,
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(UtilityBrainV2))]
[WithPresent(typeof(PathRequest))]
public partial struct BehaviorExecutionJob : IJobEntity
{
    [ReadOnly] public BlobAssetReference<BehaviorLibraryBlob> behaviorLib;
    [ReadOnly] public ComponentLookup<LocalTransform>          transformLookup;
    public float deltaTime;

    public void Execute(
        [EntityIndexInQuery] int              entityIndex,
        in LocalTransform                     transform,
        ref StateMachine                      stateMachine,
        ref PathRequest                       pathRequest,
        EnabledRefRW<PathRequest>             pathRequestEnabled,
        ref DynamicBuffer<RecentWaypoint>     recentWaypoints,
        in DynamicBuffer<AwarenessTarget>     awarenessTargets)
    {
        if (stateMachine.activeBehavior == BehaviorType.None) return;

        ref BehaviorConfigBlob behavior =
            ref behaviorLib.Value.behaviors[(int)stateMachine.activeBehavior];

        switch (stateMachine.currentPhase)
        {
            case BehaviorPhase.Approach:
                RunApproach(ref stateMachine, ref pathRequest, pathRequestEnabled,
                    in transform, in awarenessTargets, ref behavior, entityIndex);
                break;

            case BehaviorPhase.Execute:
                RunExecute(ref stateMachine, ref behavior);
                break;

            case BehaviorPhase.Complete:
                // Record visited waypoint so WanderPerceptionSystem skips it next round.
                if (stateMachine.targetEntity != Entity.Null)
                    PushRecent(ref recentWaypoints, stateMachine.targetEntity);

                stateMachine.action              = ActionType.Idle;
                stateMachine.activeBehavior      = BehaviorType.None;
                stateMachine.targetEntity        = Entity.Null;
                stateMachine.currentPhase        = BehaviorPhase.Approach;
                stateMachine.CurrentCommandIndex = 0;
                stateMachine.CommandTimer        = 0f;
                break;
        }
    }

    private void RunApproach(
        ref StateMachine             stateMachine,
        ref PathRequest              pathRequest,
        EnabledRefRW<PathRequest>    pathRequestEnabled,
        in LocalTransform            transform,
        in DynamicBuffer<AwarenessTarget> awarenessTargets,
        ref BehaviorConfigBlob       behavior,
        int                          entityIndex)
    {
        // Pick concrete target on first approach frame (Self-targeting actions).
        if (stateMachine.targetEntity == Entity.Null)
        {
            if (awarenessTargets.Length == 0)
            {
                stateMachine.currentPhase = BehaviorPhase.Complete;
                return;
            }
            // Randomize waypoint choice so units don't all converge on the same one.
            Unity.Mathematics.Random rng =
                new Unity.Mathematics.Random((uint)(entityIndex + 1) * 2654435769u);
            int index = rng.NextInt(0, awarenessTargets.Length);
            stateMachine.targetEntity = awarenessTargets[index].targetEntity;
        }

        // Kick off path if not already in flight.
        if (!pathRequestEnabled.ValueRO)
        {
            if (!transformLookup.TryGetComponent(stateMachine.targetEntity, out LocalTransform tgt))
            {
                stateMachine.currentPhase = BehaviorPhase.Complete;
                return;
            }
            float stoppingDist = behavior.targetRange * 0.5f;
            AIUtils.BeginPathRequest(ref pathRequest, pathRequestEnabled, tgt.Position, stoppingDist);
        }

        // Arrival check against the cached path destination (avoids per-frame entity lookup).
        float arrivalSq = behavior.targetRange * behavior.targetRange;
        if (math.distancesq(transform.Position, pathRequest.targetPosition) <= arrivalSq)
        {
            AIUtils.HaltPathing(ref pathRequest, pathRequestEnabled);
            stateMachine.currentPhase        = BehaviorPhase.Execute;
            stateMachine.CurrentCommandIndex = 0;
            stateMachine.CommandTimer        = 0f;
        }
    }

    private void RunExecute(ref StateMachine stateMachine, ref BehaviorConfigBlob behavior)
    {
        if (stateMachine.CurrentCommandIndex >= behavior.executionSequence.Length)
        {
            stateMachine.currentPhase = BehaviorPhase.Complete;
            return;
        }

        ref BehaviorCommand cmd =
            ref behavior.executionSequence[stateMachine.CurrentCommandIndex];

        stateMachine.CommandTimer += deltaTime;
        if (stateMachine.CommandTimer >= cmd.Duration)
        {
            stateMachine.CurrentCommandIndex++;
            stateMachine.CommandTimer = 0f;
        }
    }

    private static void PushRecent(ref DynamicBuffer<RecentWaypoint> buf, Entity entity)
    {
        if (buf.Length >= 4) buf.RemoveAt(0);
        buf.Add(new RecentWaypoint { entity = entity });
    }
}
