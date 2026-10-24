using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace DotsMovementToolkit
{
[BurstCompile]
[UpdateInGroup(typeof(MovementCoordinatorSystemGroup))]
public partial struct PathRequestSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.Dependency = new PathRequestJob
        {
            aStarFollowerLookup = SystemAPI.GetComponentLookup<AStarFollower>(false),
        }.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithPresent(typeof(DStarLiteFollower), typeof(FlowFieldFollower))]
public partial struct PathRequestJob : IJobEntity
{
    // A lookup, not a [WithPresent] parameter: entities baked before A* existed have no AStarFollower
    // and must still be dispatched. Each entity writes only its own bit.
    [NativeDisableParallelForRestriction] public ComponentLookup<AStarFollower> aStarFollowerLookup;

    public void Execute(
        Entity                           entity,
        in LocalTransform                localTransform,
        ref PathRequest                  pathRequest,
        ref PathfindingAgent             agent,
        ref Movement                     movement,
        EnabledRefRW<PathRequest>        pathRequestEnabled,
        EnabledRefRW<DStarLiteFollower>  dStarLiteFollowerEnabled,
        EnabledRefRW<FlowFieldFollower>  flowFieldFollowerEnabled)
    {
        switch (pathRequest.requestedMode)
        {
            case PathfindingMode.Stop:
                movement.targetPosition    = localTransform.Position;
                agent.isActive             = false;
                pathRequest.targetPosition = new float3(float.MaxValue);
                dStarLiteFollowerEnabled.ValueRW = false;
                flowFieldFollowerEnabled.ValueRW = false;
                DisableAStarFollower(entity);
                pathRequestEnabled.ValueRW = false;
                break;

            case PathfindingMode.DStarLite:
                ActivateAgent(ref agent, pathRequest, PathfindingMode.DStarLite);
                flowFieldFollowerEnabled.ValueRW = false;
                DisableAStarFollower(entity);
                // Enabled here so DStarLiteSystem can skip frames with only other modes' requests;
                // it fills in the follower in the same frame, before any follower system reads it.
                dStarLiteFollowerEnabled.ValueRW = true;
                // PathRequest left enabled — DStarLiteSystem consumes it downstream
                break;

            case PathfindingMode.FlowField:
                ActivateAgent(ref agent, pathRequest, PathfindingMode.FlowField);
                dStarLiteFollowerEnabled.ValueRW = false;
                DisableAStarFollower(entity);
                // PathRequest left enabled — FlowFieldSystem consumes it downstream
                break;

            case PathfindingMode.AStar:
                ActivateAgent(ref agent, pathRequest, PathfindingMode.AStar);
                dStarLiteFollowerEnabled.ValueRW = false;
                flowFieldFollowerEnabled.ValueRW = false;
                // PathRequest left enabled — AStarSystem consumes it downstream
                break;
        }
    }

    private static void ActivateAgent(ref PathfindingAgent agent, in PathRequest pathRequest, PathfindingMode mode)
    {
        agent.targetPosition      = pathRequest.targetPosition;
        agent.stoppingDistance    = pathRequest.stoppingDistance;
        agent.currentMode         = mode;
        agent.isActive            = true;
        agent.needsRepath         = false;
        agent.timeSinceLastRepath = 0f;
    }

    private void DisableAStarFollower(Entity entity)
    {
        if (aStarFollowerLookup.HasComponent(entity))
            aStarFollowerLookup.SetComponentEnabled(entity, false);
    }
}
}
