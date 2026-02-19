using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateInGroup(typeof(MovementSystemGroup))]
partial struct MoveOverrideSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        MoveOverrideJob moveOverrideJob = new MoveOverrideJob();

        state.Dependency = moveOverrideJob.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
partial struct MoveOverrideJob : IJobEntity
{
    public void Execute(
        in LocalTransform localTransform,
        ref MoveOverride moveOverride,
        EnabledRefRW<MoveOverride> moveOverrideEnabled,
        ref UnitMover unitMover)
    {
        if (math.distancesq(localTransform.Position, moveOverride.targetPosition) > UnitMoverSystem.REACHED_TARGET_POSITION_DISTANCE_SQ)
        {
            unitMover.targetPosition = moveOverride.targetPosition;
        }
        else
        {
            moveOverrideEnabled.ValueRW = false;
        }
    }
}