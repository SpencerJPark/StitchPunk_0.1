using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Collections;

// Runs the Execute → Complete phase machine for UtilityBrainV2 units.
// The execution sequence is fully authored: designers place an Approach command wherever they want
// movement to happen, with FloatParam = stopping distance and IntParam = StanceType (0=walk, 2=run).
// Pre-approach animations or actions are simply commands placed before Approach in the sequence.
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
            behaviorLib     = behaviorLib.blob,
            transformLookup = _transformLookup,
            deltaTime       = deltaTime,
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
        [EntityIndexInQuery] int          entityIndex,
        in LocalTransform                 transform,
        ref StateMachine                  stateMachine,
        ref PathRequest                   pathRequest,
        EnabledRefRW<PathRequest>         pathRequestEnabled,
        ref DynamicBuffer<RecentWaypoint> recentWaypoints)
    {
        if (stateMachine.activeBehavior == BehaviorType.None) return;

        ref BehaviorConfigBlob behavior =
            ref behaviorLib.Value.behaviors[(int)stateMachine.activeBehavior];

        switch (stateMachine.currentPhase)
        {
            case BehaviorPhase.Execute:
                RunExecute(ref stateMachine, ref pathRequest, pathRequestEnabled,
                    in transform, ref behavior);
                break;

            case BehaviorPhase.Complete:
                if (stateMachine.targetEntity != Entity.Null)
                    PushRecent(ref recentWaypoints, stateMachine.targetEntity);

                stateMachine.action              = ActionType.Idle;
                stateMachine.activeBehavior      = BehaviorType.None;
                stateMachine.targetEntity        = Entity.Null;
                stateMachine.currentPhase        = BehaviorPhase.Execute;
                stateMachine.CurrentCommandIndex = 0;
                stateMachine.CommandTimer        = 0f;
                break;
        }
    }

    private void RunExecute(
        ref StateMachine          stateMachine,
        ref PathRequest           pathRequest,
        EnabledRefRW<PathRequest> pathRequestEnabled,
        in LocalTransform         transform,
        ref BehaviorConfigBlob    behavior)
    {
        if (stateMachine.CurrentCommandIndex >= behavior.executionSequence.Length)
        {
            stateMachine.currentPhase = BehaviorPhase.Complete;
            return;
        }

        ref BehaviorCommand cmd =
            ref behavior.executionSequence[stateMachine.CurrentCommandIndex];

        switch (cmd.type)
        {
            case BehaviorCommandType.Approach:
                RunApproach(ref stateMachine, ref pathRequest, pathRequestEnabled,
                    in transform, cmd.FloatParam, cmd.IntParam);
                return; // blocking — RunApproach advances CommandIndex on arrival

            case BehaviorCommandType.WaitTime:
                stateMachine.CommandTimer += deltaTime;
                if (stateMachine.CommandTimer >= cmd.Duration)
                {
                    stateMachine.CurrentCommandIndex++;
                    stateMachine.CommandTimer = 0f;
                }
                break;

            default:
                // Unhandled command types advance immediately (no-op for now).
                stateMachine.CurrentCommandIndex++;
                stateMachine.CommandTimer = 0f;
                break;
        }
    }

    private void RunApproach(
        ref StateMachine          stateMachine,
        ref PathRequest           pathRequest,
        EnabledRefRW<PathRequest> pathRequestEnabled,
        in LocalTransform         transform,
        float                     stoppingDist,
        int                       stanceIntParam)
    {
        // If no target is set the behavior can't approach anything — abort.
        if (stateMachine.targetEntity == Entity.Null)
        {
            stateMachine.currentPhase = BehaviorPhase.Complete;
            return;
        }

        // Kick off path on the first frame of this command.
        if (!pathRequestEnabled.ValueRO)
        {
            if (!transformLookup.TryGetComponent(stateMachine.targetEntity, out LocalTransform tgt))
            {
                stateMachine.currentPhase = BehaviorPhase.Complete;
                return;
            }
            stateMachine.currentStance = (StanceType)stanceIntParam;
            AIUtils.BeginPathRequest(ref pathRequest, pathRequestEnabled, tgt.Position, stoppingDist);
        }

        // Advance to next command on arrival.
        float arrivalSq = stoppingDist * stoppingDist;
        if (math.distancesq(transform.Position, pathRequest.targetPosition) <= arrivalSq)
        {
            AIUtils.HaltPathing(ref pathRequest, pathRequestEnabled);
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
