using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace DotsMovementToolkit
{
// Walks each agent along its own AStarPathWaypoint corners. Reads only the agent's buffer and the
// cost map, so it runs in parallel with no physics raycasts.
[BurstCompile]
[UpdateInGroup(typeof(MovementFollowerSystemGroup))]
public partial struct AStarFollowerSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<NavGridConfig>();
        state.RequireForUpdate<NavGridCostMap>();
        state.RequireForUpdate<NavGridSettings>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        NavGridConfig gridConfig = SystemAPI.GetSingleton<NavGridConfig>();
        NavGridCostMap gridCostMap = SystemAPI.GetSingleton<NavGridCostMap>();
        NavGridSettings gridSettings = SystemAPI.GetSingleton<NavGridSettings>();
        // The follower never reads stairs; an empty array keeps the shared grid view fully assigned.
        NativeArray<NavGridStairConnection> noStairs = new NativeArray<NavGridStairConnection>(0, Allocator.TempJob);

        state.Dependency = new AStarFollowJob
        {
            grid = new AStarGridView
            {
                width = gridConfig.width,
                height = gridConfig.height,
                layerCount = gridConfig.layerCount,
                cellSize = gridConfig.cellSize,
                layerHeight = gridConfig.layerHeight,
                gridOrigin = gridConfig.gridOrigin,
                wallCost = gridSettings.wallCost,
                costs = gridCostMap.costs,
                stairs = noStairs,
            },
            arrivalRadius = gridConfig.cellSize * 0.35f,
        }.ScheduleParallel(state.Dependency);
        state.Dependency = noStairs.Dispose(state.Dependency);
    }
}

[BurstCompile]
public partial struct AStarFollowJob : IJobEntity
{
    public AStarGridView grid;
    public float arrivalRadius;

    public void Execute(
        in LocalTransform localTransform,
        in PathfindingAgent agent,
        in DynamicBuffer<AStarPathWaypoint> waypoints,
        ref AStarFollower follower,
        ref Movement movement,
        EnabledRefRW<AStarFollower> aStarFollowerEnabled)
    {
        if (!agent.isActive || agent.currentMode != PathfindingMode.AStar)
            return;
        int waypointCount = waypoints.Length;
        if (waypointCount == 0)
        {
            aStarFollowerEnabled.ValueRW = false;
            return;
        }

        float3 position = localTransform.Position;
        float arrivalRadiusSquared = arrivalRadius * arrivalRadius;
        int waypointIndex = math.min(follower.currentWaypointIndex, waypointCount - 1);
        // Never advance onto another floor's corner: StairTransitionAStarJob makes that step once the
        // agent stands on the stair, and moving on early would leave it walking the wrong floor.
        while (waypointIndex < waypointCount - 1
               && waypoints[waypointIndex + 1].layer == follower.currentLayer
               && math.distancesq(position.xz, waypoints[waypointIndex].position.xz) < arrivalRadiusSquared)
            waypointIndex++;

        // Pushed off the line by a crowd or collision: skip a corner the agent can already see past,
        // at most one per frame. Never across a stair, whose two corners are not on one floor.
        if (waypointIndex < waypointCount - 1)
        {
            AStarPathWaypoint currentCorner = waypoints[waypointIndex];
            AStarPathWaypoint nextCorner = waypoints[waypointIndex + 1];
            if (currentCorner.layer == nextCorner.layer && nextCorner.layer == follower.currentLayer)
            {
                int shortcutMaxCost = math.max(CostAt(currentCorner), CostAt(nextCorner));
                if (AStarSearch.IsSegmentClear(grid.WorldToCellSpace(position), grid.WorldToCellSpace(nextCorner.position),
                        nextCorner.layer, shortcutMaxCost, true, grid))
                    waypointIndex++;
            }
        }

        follower.currentWaypointIndex = waypointIndex;
        float3 waypointPosition = waypoints[waypointIndex].position;
        movement.targetPosition = waypointPosition;

        float3 toWaypoint = waypointPosition - position;
        if (math.lengthsq(toWaypoint) > 0.001f)
            follower.lastMoveDirection = math.normalize(toWaypoint);

        if (waypointIndex == waypointCount - 1 && math.distancesq(position.xz, waypointPosition.xz) < arrivalRadiusSquared)
            aStarFollowerEnabled.ValueRW = false;
    }

    private int CostAt(AStarPathWaypoint waypoint)
    {
        int2 cell = PathfindingUtils.WorldToGrid(waypoint.position, grid.cellSize, grid.gridOrigin);
        return grid.IsInside(cell, waypoint.layer) ? grid.costs[grid.NodeIndex(cell, waypoint.layer)] : 1;
    }
}
}
