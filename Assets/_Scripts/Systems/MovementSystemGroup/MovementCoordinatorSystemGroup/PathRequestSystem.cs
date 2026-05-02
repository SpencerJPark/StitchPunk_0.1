using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(MovementCoordinatorSystemGroup))]
public partial struct PathRequestSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameSceneTag>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.Dependency = new PathRequestJob().ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithPresent(typeof(DStarLiteFollower), typeof(FlowFieldFollower))]
public partial struct PathRequestJob : IJobEntity
{
    public void Execute(
        in LocalTransform                localTransform,
        ref PathRequest                  pathRequest,
        ref PathfindingAgent             agent,
        ref Movement                     movement,
        EnabledRefRW<PathRequest>        pathRequestEnabled,
        EnabledRefRW<DStarLiteFollower>  dstarEnabled,
        EnabledRefRW<FlowFieldFollower>  flowFieldEnabled)
    {
        switch (pathRequest.requestedMode)
        {
            case PathfindingMode.Stop:
                movement.targetPosition    = localTransform.Position;
                agent.isActive             = false;
                pathRequest.targetPosition = new float3(float.MaxValue);
                dstarEnabled.ValueRW       = false;
                flowFieldEnabled.ValueRW   = false;
                pathRequestEnabled.ValueRW = false;
                break;

            case PathfindingMode.DStarLite:
                agent.targetPosition      = pathRequest.targetPosition;
                agent.stoppingDistance    = pathRequest.stoppingDistance;
                agent.currentMode         = PathfindingMode.DStarLite;
                agent.isActive            = true;
                agent.needsRepath         = false;
                agent.timeSinceLastRepath = 0f;
                flowFieldEnabled.ValueRW  = false;
                // PathRequest left enabled — DStarLiteSystem consumes it downstream
                break;

            case PathfindingMode.FlowField:
                agent.targetPosition      = pathRequest.targetPosition;
                agent.stoppingDistance    = pathRequest.stoppingDistance;
                agent.currentMode         = PathfindingMode.FlowField;
                agent.isActive            = true;
                agent.needsRepath         = false;
                agent.timeSinceLastRepath = 0f;
                dstarEnabled.ValueRW      = false;
                // PathRequest left enabled — FlowFieldSystem consumes it downstream
                break;
        }
    }
}
