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
    private ComponentLookup<LocalTransform> transformLookup;
    private ComponentLookup<UnitMover> unitMoverLookup;
    private ComponentLookup<TargetPositionPathQueued> pathQueuedLookup;

    public void OnCreate(ref SystemState state)
    {
        transformLookup = state.GetComponentLookup<LocalTransform>(true);
        unitMoverLookup = state.GetComponentLookup<UnitMover>(false);
        pathQueuedLookup = state.GetComponentLookup<TargetPositionPathQueued>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        transformLookup.Update(ref state);
        unitMoverLookup.Update(ref state);
        pathQueuedLookup.Update(ref state);

        uint seed = (uint)(SystemAPI.Time.ElapsedTime * 10000) + 1;

        state.Dependency = new WanderJob
        {
            seed = seed,
            transformLookup = transformLookup,
            unitMoverLookup = unitMoverLookup,
            pathQueuedLookup = pathQueuedLookup
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(CanWander))]
public partial struct WanderJob : IJobEntity
{
    public uint seed;

    [ReadOnly] public ComponentLookup<LocalTransform> transformLookup;
    [NativeDisableParallelForRestriction] public ComponentLookup<UnitMover> unitMoverLookup;
    [NativeDisableParallelForRestriction] public ComponentLookup<TargetPositionPathQueued> pathQueuedLookup;

    public void Execute(
        ref WanderState wanderState,
        in SelectedAction selectedAction,
        in BrainLink brainLink,
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

        bool targetUninitialized = math.all(wanderState.wanderTarget == float3.zero);
        bool reachedTarget = distToTargetSq < 2f;

        if (reachedTarget || targetUninitialized)
        {
            Unity.Mathematics.Random random = new Unity.Mathematics.Random(seed + (uint)index + 1);

            float wanderRadius = wanderState.wanderRadius;
            float angle = random.NextFloat(0f, math.PI * 2f);
            float distance = random.NextFloat(wanderRadius * 0.3f, wanderRadius);

            float3 offset = new float3(math.cos(angle) * distance, 0f, math.sin(angle) * distance);
            wanderState.wanderTarget = currentPos + offset;

            SetMovementTarget(body, wanderState.wanderTarget);
        }
    }

    private void SetMovementTarget(Entity body, float3 targetPosition)
    {
        if (pathQueuedLookup.HasComponent(body))
        {
            pathQueuedLookup[body] = new TargetPositionPathQueued { targetPosition = targetPosition };
            pathQueuedLookup.SetComponentEnabled(body, true);
        }
        else if (unitMoverLookup.TryGetComponent(body, out UnitMover mover))
        {
            mover.targetPosition = targetPosition;
            unitMoverLookup[body] = mover;
        }
    }
}