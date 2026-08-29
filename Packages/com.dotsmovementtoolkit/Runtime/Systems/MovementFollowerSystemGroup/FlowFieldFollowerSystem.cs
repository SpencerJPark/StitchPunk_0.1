using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

namespace DotsMovementToolkit
{
/// <summary>
/// Handles flow field following for entities using flow field pathfinding.
/// Updates UnitMover.targetPosition based on flow field vectors.
/// Also handles line-of-sight optimization and stair transitions.
/// </summary>
[UpdateInGroup(typeof(MovementFollowerSystemGroup))]
public partial struct FlowFieldFollowerSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<NavGridConfig>();
        state.RequireForUpdate<NavGridCostMap>();
        state.RequireForUpdate<FlowFieldSystem.FlowFieldData>();
        state.RequireForUpdate<PhysicsWorldSingleton>();
        state.RequireForUpdate<NavGridSettings>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // Complete any pending jobs on the cost map before reading
        state.CompleteDependency();

        var gridConfig = SystemAPI.GetSingleton<NavGridConfig>();
        var gridCostMap = SystemAPI.GetSingleton<NavGridCostMap>();
        var flowFieldData = SystemAPI.GetSingleton<FlowFieldSystem.FlowFieldData>();
        var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
        var gridSettings = SystemAPI.GetSingleton<NavGridSettings>();

        int cellsPerLayer = gridConfig.width * gridConfig.height;

        // First: Check if entities can move straight to target (line of sight optimization)
        var lineOfSightJob = new FlowFieldLineOfSightJob
        {
            collisionWorld = physicsWorld.CollisionWorld,
            wallLayerMask = gridSettings.wallLayerMask
        };
        state.Dependency = lineOfSightJob.ScheduleParallel(state.Dependency);

        // Second: Update movement targets from flow field for entities still following
        var followJob = new FlowFieldFollowJob
        {
            cellSize = gridConfig.cellSize,
            cellSizeDouble = gridConfig.cellSize * 2f,
            width = gridConfig.width,
            height = gridConfig.height,
            cellsPerLayer = cellsPerLayer,
            vectors = flowFieldData.vectors,
            costs = gridCostMap.costs,
            wallCost = gridSettings.wallCost,
            targets = flowFieldData.targets
        };
        state.Dependency = followJob.ScheduleParallel(state.Dependency);
    }

    /// <summary>
    /// Checks if entity has clear line of sight to target.
    /// If so, disables flow field following and sets direct path.
    /// </summary>
    [BurstCompile]
    [WithAll(typeof(FlowFieldFollower))]
    public partial struct FlowFieldLineOfSightJob : IJobEntity
    {
        [ReadOnly] public CollisionWorld collisionWorld;
        [ReadOnly] public uint wallLayerMask;

        public void Execute(
            in LocalTransform localTransform,
            in PathfindingAgent agent,
            ref Movement movement,
            ref FlowFieldFollower follower,
            EnabledRefRW<FlowFieldFollower> followerEnabled)
        {
            if (!agent.isActive)
                return;

            RaycastInput raycastInput = new RaycastInput
            {
                Start = localTransform.Position,
                End = follower.targetPosition,
                Filter = new CollisionFilter
                {
                    BelongsTo = ~0u,
                    CollidesWith = wallLayerMask,
                    GroupIndex = 0
                }
            };

            if (!collisionWorld.CastRay(raycastInput))
            {
                // Clear line of sight - move directly to target
                movement.targetPosition = follower.targetPosition;
                followerEnabled.ValueRW = false;
            }
        }
    }

    /// <summary>
    /// Updates UnitMover target position based on flow field vectors.
    /// </summary>
    [BurstCompile]
    [WithAll(typeof(FlowFieldFollower))]
    public partial struct FlowFieldFollowJob : IJobEntity
    {
        [ReadOnly] public float cellSize;
        [ReadOnly] public float cellSizeDouble;
        [ReadOnly] public int width;
        [ReadOnly] public int height;
        [ReadOnly] public int cellsPerLayer;
        [ReadOnly] public NativeArray<float2> vectors;
        [ReadOnly] public NativeArray<byte> costs;
        [ReadOnly] public byte wallCost;
        [ReadOnly] public NativeArray<FlowFieldSystem.FlowFieldTarget> targets;

        public void Execute(
            in LocalTransform localTransform,
            in PathfindingAgent agent,
            ref Movement movement,
            ref FlowFieldFollower follower,
            EnabledRefRW<FlowFieldFollower> followerEnabled)
        {
            if (!agent.isActive)
                return;

            // Validate flow field
            if (follower.flowFieldIndex < 0 || follower.flowFieldIndex >= FlowFieldSystem.FLOW_FIELD_MAP_COUNT)
            {
                followerEnabled.ValueRW = false;
                return;
            }
            
            if (!targets[follower.flowFieldIndex].isValid)
            {
                followerEnabled.ValueRW = false;
                return;
            }

            int2 gridPosition = NavGridSystem.GetGridPosition(localTransform.Position, cellSize);
            
            // Bounds check
            if (!NavGridSystem.IsValidGridPosition(gridPosition, width, height))
            {
                followerEnabled.ValueRW = false;
                return;
            }
            
            int localIndex = NavGridSystem.CalculateIndex(gridPosition, width);
            int globalIndex = follower.flowFieldIndex * cellsPerLayer + localIndex;
            
            // Get flow vector
            float2 flowVector = vectors[globalIndex];
            float3 moveVector = NavGridSystem.GetWorldMovementVector(flowVector);

            // Handle wall cells - use last valid direction
            if (costs[localIndex] == wallCost)
            {
                moveVector = follower.lastMoveVector;
            }
            else
            {
                follower.lastMoveVector = moveVector;
            }

            // Set target position ahead in flow direction
            movement.targetPosition =
                NavGridSystem.GetWorldCenterPosition(gridPosition.x, gridPosition.y, cellSize) +
                moveVector * cellSizeDouble;

            // Check if reached final destination
            float distToTarget = math.distance(localTransform.Position, follower.targetPosition);
            if (distToTarget < cellSize)
            {
                movement.targetPosition = follower.targetPosition;
                followerEnabled.ValueRW = false;
            }
        }
    }
}
}