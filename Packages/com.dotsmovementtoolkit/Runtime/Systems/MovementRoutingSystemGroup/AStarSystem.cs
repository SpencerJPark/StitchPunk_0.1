using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace DotsMovementToolkit
{
// Consumes AStar PathRequests. Each request is searched in parallel with its own scratch memory and
// writes only its agent's AStarPathWaypoint buffer, so agents never share search state.
// Last in the group: the other routers read PathRequest on the main thread, and would otherwise sit
// waiting for these path jobs to finish.
[BurstCompile]
[UpdateInGroup(typeof(MovementRoutingSystemGroup), OrderLast = true)]
public partial struct AStarSystem : ISystem
{
    // How far (in cells) a target inside a blocked cell is moved to the nearest open one.
    private const int GoalSearchRadiusCells = 4;

    private EntityQuery stairConnectionQuery;
    private EntityQuery pendingRequestQuery;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<NavGridConfig>();
        state.RequireForUpdate<NavGridCostMap>();
        state.RequireForUpdate<NavGridSettings>();
        stairConnectionQuery = state.GetEntityQuery(ComponentType.ReadOnly<NavGridStairConnection>());
        pendingRequestQuery = SystemAPI.QueryBuilder()
            .WithAll<PathRequest>()
            .WithPresent<AStarFollower, AStarPathWaypoint>()
            .Build();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // An enabled-bit check, so a frame with no requests never waits on LocalTransform writers.
        if (pendingRequestQuery.IsEmpty)
            return;

        NavGridConfig gridConfig = SystemAPI.GetSingleton<NavGridConfig>();
        NavGridCostMap gridCostMap = SystemAPI.GetSingleton<NavGridCostMap>();
        NavGridSettings gridSettings = SystemAPI.GetSingleton<NavGridSettings>();

        NativeArray<NavGridStairConnection> stairs = stairConnectionQuery.IsEmpty
            ? new NativeArray<NavGridStairConnection>(0, Allocator.TempJob)
            : state.EntityManager.GetBuffer<NavGridStairConnection>(stairConnectionQuery.GetSingletonEntity(), true)
                .ToNativeArray(Allocator.TempJob);

        state.Dependency = new AStarPathJob
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
                stairs = stairs,
            },
            goalSearchRadiusCells = GoalSearchRadiusCells,
            costMapVersion = gridCostMap.costMapVersion,
        }.ScheduleParallel(state.Dependency);
        state.Dependency = stairs.Dispose(state.Dependency);
    }
}

[BurstCompile]
[WithPresent(typeof(AStarFollower))]
public partial struct AStarPathJob : IJobEntity
{
    public AStarGridView grid;
    public int goalSearchRadiusCells;
    public uint costMapVersion;

    public void Execute(
        in LocalTransform localTransform,
        ref PathRequest pathRequest,
        ref AStarFollower follower,
        DynamicBuffer<AStarPathWaypoint> waypoints,
        ref Movement movement,
        EnabledRefRW<PathRequest> pathRequestEnabled,
        EnabledRefRW<AStarFollower> aStarFollowerEnabled)
    {
        if (pathRequest.requestedMode != PathfindingMode.AStar)
            return;
        pathRequestEnabled.ValueRW = false;

        float3 startPosition = localTransform.Position;
        float3 targetPosition = pathRequest.targetPosition;
        int2 gridMaximum = new int2(grid.width - 1, grid.height - 1);
        int startLayer = grid.ClampedLayerAt(startPosition);
        int2 startCell = math.clamp(PathfindingUtils.WorldToGrid(startPosition, grid.cellSize, grid.gridOrigin), int2.zero, gridMaximum);
        int goalLayer = grid.ClampedLayerAt(targetPosition);
        int2 requestedGoalCell = PathfindingUtils.WorldToGrid(targetPosition, grid.cellSize, grid.gridOrigin);
        bool targetIsOpenCell = grid.IsWalkable(requestedGoalCell, goalLayer);
        int2 goalCell = math.clamp(requestedGoalCell, int2.zero, gridMaximum);

        // Callers commonly re-request every frame toward the same spot. Same goal node, same cost map
        // and still following: keep the path, only move its end to the new exact target.
        int requestedGoalNode = grid.NodeIndex(goalCell, goalLayer);
        if (aStarFollowerEnabled.ValueRO
            && follower.goalNode == requestedGoalNode
            && follower.costMapVersion == costMapVersion
            && waypoints.Length > 0)
        {
            if (targetIsOpenCell)
            {
                AStarPathWaypoint finalWaypoint = waypoints[waypoints.Length - 1];
                finalWaypoint.position = targetPosition;
                waypoints[waypoints.Length - 1] = finalWaypoint;
            }
            follower.targetPosition = targetPosition;
            return;
        }

        waypoints.Clear();
        if (!targetIsOpenCell
            && !AStarSearch.TryFindNearestWalkableCell(goalCell, goalLayer, goalSearchRadiusCells, grid, out goalCell))
        {
            // Nowhere open near the target: walk straight at it and let collision sort it out.
            waypoints.Add(new AStarPathWaypoint { position = targetPosition, layer = goalLayer });
        }
        else
        {
            NativeList<int> nodePath = new NativeList<int>(64, Allocator.Temp);
            NativeList<AStarPathWaypoint> corners = new NativeList<AStarPathWaypoint>(16, Allocator.Temp);
            bool reachedGoal = AStarSearch.FindNodePath(
                grid.NodeIndex(startCell, startLayer), grid.NodeIndex(goalCell, goalLayer), grid, ref nodePath);
            AStarSearch.BuildWaypoints(nodePath, startPosition, targetPosition, reachedGoal && targetIsOpenCell, grid, ref corners);
            waypoints.AddRange(corners.AsArray());
            nodePath.Dispose();
            corners.Dispose();
        }

        follower.currentWaypointIndex = 0;
        follower.targetPosition = targetPosition;
        follower.currentLayer = startLayer;
        follower.goalNode = requestedGoalNode;
        follower.costMapVersion = costMapVersion;
        aStarFollowerEnabled.ValueRW = true;
        movement.targetPosition = waypoints[0].position;
    }
}
}
