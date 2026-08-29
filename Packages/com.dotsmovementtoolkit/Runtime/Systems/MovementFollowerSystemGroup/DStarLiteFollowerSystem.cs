using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

namespace DotsMovementToolkit
{
[BurstCompile]
[UpdateInGroup(typeof(MovementFollowerSystemGroup))]
public partial struct DStarLiteFollowerSystem : ISystem
{
    private ComponentLookup<DStarLiteFollower> dstarFollowerLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PhysicsWorldSingleton>();
        state.RequireForUpdate<DStarLiteSystem.DStarLiteData>();
        state.RequireForUpdate<GridSystem.GridCostMap>();
        state.RequireForUpdate<MovementGridSettings>();

        dstarFollowerLookup = state.GetComponentLookup<DStarLiteFollower>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        DStarLiteSystem.DStarLiteData dstarData    = SystemAPI.GetSingleton<DStarLiteSystem.DStarLiteData>();
        GridSystem.GridCostMap        gridCostMap   = SystemAPI.GetSingleton<GridSystem.GridCostMap>();
        PhysicsWorldSingleton         physicsWorld  = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
        MovementGridSettings          gridSettings  = SystemAPI.GetSingleton<MovementGridSettings>();

        EndSimulationEntityCommandBufferSystem.Singleton ecbSingleton =
            SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        EntityCommandBuffer.ParallelWriter ecb =
            ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();

        dstarFollowerLookup.Update(ref state);

        // 1. Advance waypoints along the computed D* path; detect per-segment arrival
        state.Dependency = new UpdateFollowersJob
        {
            nodeSize    = dstarData.nodeSize,
            width       = dstarData.width,
            height      = dstarData.height,
            nodes       = dstarData.nodes,
            activePaths = dstarData.activePaths,
            costs       = gridCostMap.costs,
            wallCost    = gridSettings.wallCost,
            ecb         = ecb
        }.ScheduleParallel(state.Dependency);

        // 2. Collapse remaining path when line-of-sight to final target is clear
        state.Dependency = new DStarLineOfSightJob
        {
            collisionWorld      = physicsWorld.CollisionWorld,
            dstarFollowerLookup = dstarFollowerLookup,
            wallLayerMask       = gridSettings.wallLayerMask
        }.ScheduleParallel(state.Dependency);

        // 3. Apply current waypoint to Movement
        state.Dependency = new DStarMoveJob().ScheduleParallel(state.Dependency);
    }
}

/// <summary>
/// Advances the unit along its D* Lite waypoints and detects final arrival.
/// </summary>
[BurstCompile]
[WithAll(typeof(DStarLiteFollower))]
public partial struct UpdateFollowersJob : IJobEntity
{
    public float nodeSize;
    public int   width;
    public int   height;

    [ReadOnly] public NativeArray<DStarLiteSystem.DStarNode> nodes;
    [ReadOnly] public NativeArray<DStarLiteSystem.PathData>  activePaths;
    [ReadOnly] public NativeArray<byte>                      costs;
    [ReadOnly] public byte                                   wallCost;

    public EntityCommandBuffer.ParallelWriter ecb;

    public void Execute(
        ref DStarLiteFollower  follower,
        ref Movement           mover,
        in LocalTransform      transform,
        in PathfindingAgent    agent,
        Entity                 entity,
        [EntityIndexInQuery] int sortKey)
    {
        if (!agent.isActive) return;

        if (follower.pathDataIndex < 0 || follower.pathDataIndex >= DStarLiteSystem.MAX_ACTIVE_PATHS)
            return;

        DStarLiteSystem.PathData pathData = activePaths[follower.pathDataIndex];
        if (!pathData.isValid || pathData.owner != entity) return;

        float3 currentPos = transform.Position;
        float3 nextWP     = follower.nextWaypoint;

        float distToWaypoint = math.distance(
            new float2(currentPos.x, currentPos.z),
            new float2(nextWP.x, nextWP.z));

        if (distToWaypoint < nodeSize * 0.5f)
        {
            float distToGoal = math.distance(
                new float2(currentPos.x, currentPos.z),
                new float2(follower.targetPosition.x, follower.targetPosition.z));

            if (distToGoal < nodeSize * 0.75f)
            {
                mover.targetPosition = follower.targetPosition;
                ecb.SetComponentEnabled<DStarLiteFollower>(sortKey, entity, false);
                return;
            }

            int2   currentGrid  = PathfindingUtils.WorldToGrid(currentPos, nodeSize);
            int2   nextNode     = GetNextNodeTowardGoal(currentGrid);
            float3 newWaypoint  = PathfindingUtils.GridToWorld(nextNode, nodeSize);

            follower.nextWaypoint     = newWaypoint;
            follower.currentNodeIndex = PathfindingUtils.CalculateIndex(currentGrid, width);

            mover.targetPosition = newWaypoint;
        }
        else
        {
            mover.targetPosition = nextWP;
        }

        float3 toWaypoint = follower.nextWaypoint - currentPos;
        if (math.lengthsq(toWaypoint) > 0.001f)
            follower.lastMoveDirection = math.normalize(toWaypoint);
    }

    private int2 GetNextNodeTowardGoal(int2 currentPos)
    {
        float bestScore    = float.MaxValue;
        int2  bestNeighbor = currentPos;

        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;

                int2 neighborPos = currentPos + new int2(dx, dy);
                if (!PathfindingUtils.IsValidPosition(neighborPos, width, height)) continue;

                int neighborIndex = PathfindingUtils.CalculateIndex(neighborPos, width);
                if (costs[neighborIndex] == wallCost) continue;

                DStarLiteSystem.DStarNode neighborNode = nodes[neighborIndex];
                if (neighborNode.g >= float.MaxValue * 0.5f) continue;

                float cost  = PathfindingUtils.CalculateMoveCost(dx, dy, costs[neighborIndex]);
                float score = cost + neighborNode.g;

                if (score < bestScore)
                {
                    bestScore    = score;
                    bestNeighbor = neighborPos;
                }
            }
        }

        return bestNeighbor;
    }
}

/// <summary>
/// When line-of-sight to the final target is clear, collapses the remaining
/// waypoints to a single direct move. DStarLiteFollower stays enabled so
/// arrival detection works normally.
/// </summary>
[BurstCompile]
[WithAll(typeof(DStarLiteFollower))]
public partial struct DStarLineOfSightJob : IJobEntity
{
    [NativeDisableParallelForRestriction]
    public ComponentLookup<DStarLiteFollower> dstarFollowerLookup;

    [ReadOnly] public CollisionWorld collisionWorld;
    [ReadOnly] public uint wallLayerMask;

    public void Execute(
        in LocalTransform localTransform,
        in Movement       movement,
        Entity            entity)
    {
        DStarLiteFollower follower = dstarFollowerLookup[entity];

        RaycastInput raycastInput = new RaycastInput
        {
            Start  = localTransform.Position,
            End    = follower.targetPosition,
            Filter = new CollisionFilter
            {
                BelongsTo   = ~0u,
                CollidesWith = wallLayerMask,
                GroupIndex  = 0
            }
        };

        if (!collisionWorld.CastRay(raycastInput))
        {
            follower.nextWaypoint       = follower.targetPosition;
            dstarFollowerLookup[entity] = follower;
        }
    }
}

/// <summary>
/// Sets Movement.targetPosition to the current D* Lite waypoint.
/// </summary>
[BurstCompile]
[WithAll(typeof(DStarLiteFollower))]
public partial struct DStarMoveJob : IJobEntity
{
    public void Execute(
        in DStarLiteFollower follower,
        in PathfindingAgent  agent,
        ref Movement         movement)
    {
        if (!agent.isActive) return;
        if (agent.currentMode != PathfindingMode.DStarLite) return;

        movement.targetPosition = follower.nextWaypoint;
    }
}
}
