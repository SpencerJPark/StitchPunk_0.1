using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(AIExecutionSystemGroup))]
[UpdateAfter(typeof(AIExecutionSystem))]
public partial struct WanderSystem : ISystem
{
    private ComponentLookup<UnitMover> unitMoverLookup;
    private ComponentLookup<LocalTransform> transformLookup;
    private ComponentLookup<TargetPositionPathQueued> pathQueuedLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        unitMoverLookup = SystemAPI.GetComponentLookup<UnitMover>(false);
        transformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true);
        pathQueuedLookup = SystemAPI.GetComponentLookup<TargetPositionPathQueued>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        unitMoverLookup.Update(ref state);
        transformLookup.Update(ref state);
        pathQueuedLookup.Update(ref state);

        uint seed = (uint)(SystemAPI.Time.ElapsedTime * 1000) + 1;

        state.Dependency = new WanderJob
        {
            baseSeed = seed,
            unitMoverLookup = unitMoverLookup,
            transformLookup = transformLookup,
            pathQueuedLookup = pathQueuedLookup
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
public partial struct WanderJob : IJobEntity
{
    public uint baseSeed;

    [ReadOnly] public ComponentLookup<LocalTransform> transformLookup;
    [NativeDisableParallelForRestriction] public ComponentLookup<UnitMover> unitMoverLookup;
    [NativeDisableParallelForRestriction] public ComponentLookup<TargetPositionPathQueued> pathQueuedLookup;

    public void Execute(
        ref WanderState wanderState,
        in SelectedAction selectedAction,
        in BrainLink brainLink,
        in CanWander canWander,
        [EntityIndexInQuery] int index)
    {
        if (selectedAction.current != ActionType.Wander)
            return;

        Entity body = brainLink.body;

        if (!transformLookup.TryGetComponent(body, out LocalTransform bodyTransform))
            return;

        if (!unitMoverLookup.TryGetComponent(body, out UnitMover mover))
            return;

        float3 currentPos = bodyTransform.Position;
        float distToTargetSq = math.distancesq(currentPos, wanderState.wanderTarget);

        // Need new wander target if close to current or no target set
        bool needNewTarget = distToTargetSq < 1f || wanderState.wanderTarget.Equals(float3.zero);

        if (needNewTarget)
        {
            Unity.Mathematics.Random random = new Unity.Mathematics.Random(baseSeed + (uint)index + 1);

            float wanderRadius = wanderState.wanderRadius;
            float angle = random.NextFloat(0f, math.PI * 2f);
            float distance = random.NextFloat(wanderRadius * 0.5f, wanderRadius);

            float3 offset = new float3(math.cos(angle) * distance, 0f, math.sin(angle) * distance);
            wanderState.wanderTarget = currentPos + offset;

            if (pathQueuedLookup.HasComponent(body))
            {
                pathQueuedLookup[body] = new TargetPositionPathQueued { targetPosition = wanderState.wanderTarget };
                pathQueuedLookup.SetComponentEnabled(body, true);
            }
            else
            {
                mover.targetPosition = wanderState.wanderTarget;
                unitMoverLookup[body] = mover;
            }
        }
    }
}