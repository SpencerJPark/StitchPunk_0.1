using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

/// <summary>
/// Handles flow field following for entities using flow field pathfinding.
/// Updates UnitMover.targetPosition based on flow field vectors.
/// Also handles line-of-sight optimization and stair transitions.
/// </summary>
[UpdateInGroup(typeof(MovementFollowerSystemGroup))]
public partial struct FlowFieldFollowerSystem : ISystem
{
    private ComponentLookup<FlowFieldFollower> flowFieldFollowerLookup;
    private ComponentLookup<PathfindingAgent> pathfindingAgentLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GridSystem.GridConfig>();
        state.RequireForUpdate<GridSystem.GridCostMap>();
        state.RequireForUpdate<FlowFieldSystem.FlowFieldData>();
        state.RequireForUpdate<PhysicsWorldSingleton>();
        
        flowFieldFollowerLookup = state.GetComponentLookup<FlowFieldFollower>(false);
        pathfindingAgentLookup = state.GetComponentLookup<PathfindingAgent>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var gridConfig = SystemAPI.GetSingleton<GridSystem.GridConfig>();
        var gridCostMap = SystemAPI.GetSingleton<GridSystem.GridCostMap>();
        var flowFieldData = SystemAPI.GetSingleton<FlowFieldSystem.FlowFieldData>();
        var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
        
        flowFieldFollowerLookup.Update(ref state);
        pathfindingAgentLookup.Update(ref state);
        
        int cellsPerLayer = gridConfig.width * gridConfig.height;
        
        // First: Check if entities can move straight to target (line of sight optimization)
        var lineOfSightJob = new FlowFieldLineOfSightJob
        {
            collisionWorld = physicsWorld.CollisionWorld,
            flowFieldFollowerLookup = flowFieldFollowerLookup,
            pathfindingAgentLookup = pathfindingAgentLookup
        };
        state.Dependency = lineOfSightJob.ScheduleParallel(state.Dependency);
        
        // Second: Update movement targets from flow field for entities still following
        var followJob = new FlowFieldFollowJob
        {
            flowFieldFollowerLookup = flowFieldFollowerLookup,
            pathfindingAgentLookup = pathfindingAgentLookup,
            cellSize = gridConfig.cellSize,
            cellSizeDouble = gridConfig.cellSize * 2f,
            width = gridConfig.width,
            height = gridConfig.height,
            cellsPerLayer = cellsPerLayer,
            vectors = flowFieldData.vectors,
            costs = gridCostMap.costs,
            targets = flowFieldData.targets
        };
        state.Dependency = followJob.ScheduleParallel(state.Dependency);
    }
}

/// <summary>
/// Checks if entity has clear line of sight to target.
/// If so, disables flow field following and sets direct path.
/// </summary>
[BurstCompile]
[WithAll(typeof(FlowFieldFollower))]
public partial struct FlowFieldLineOfSightJob : IJobEntity
{
    [NativeDisableParallelForRestriction] 
    public ComponentLookup<FlowFieldFollower> flowFieldFollowerLookup;
    
    [NativeDisableParallelForRestriction]
    public ComponentLookup<PathfindingAgent> pathfindingAgentLookup;
    
    [ReadOnly] public CollisionWorld collisionWorld;

    public void Execute(
        in LocalTransform localTransform,
        ref UnitMover unitMover,
        Entity entity)
    {
        FlowFieldFollower follower = flowFieldFollowerLookup[entity];

        RaycastInput raycastInput = new RaycastInput
        {
            Start = localTransform.Position,
            End = follower.targetPosition,
            Filter = new CollisionFilter
            {
                BelongsTo = ~0u,
                CollidesWith = 1u << GlobalGameData.WALLS_LAYER,
                GroupIndex = 0
            }
        };

        if (!collisionWorld.CastRay(raycastInput))
        {
            // Clear line of sight - move directly to target
            unitMover.targetPosition = follower.targetPosition;
            flowFieldFollowerLookup.SetComponentEnabled(entity, false);
            
            // Update pathfinding agent state
            if (pathfindingAgentLookup.HasComponent(entity))
            {
                var agent = pathfindingAgentLookup[entity];
                agent.isActive = false;
                pathfindingAgentLookup[entity] = agent;
            }
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
    [NativeDisableParallelForRestriction] 
    public ComponentLookup<FlowFieldFollower> flowFieldFollowerLookup;
    
    [NativeDisableParallelForRestriction]
    public ComponentLookup<PathfindingAgent> pathfindingAgentLookup;

    [ReadOnly] public float cellSize;
    [ReadOnly] public float cellSizeDouble;
    [ReadOnly] public int width;
    [ReadOnly] public int height;
    [ReadOnly] public int cellsPerLayer;
    [ReadOnly] public NativeArray<float2> vectors;
    [ReadOnly] public NativeArray<byte> costs;
    [ReadOnly] public NativeArray<FlowFieldSystem.FlowFieldTarget> targets;

    public void Execute(
        in LocalTransform localTransform,
        ref UnitMover unitMover,
        Entity entity)
    {
        FlowFieldFollower follower = flowFieldFollowerLookup[entity];
        
        // Validate flow field
        if (follower.flowFieldIndex < 0 || follower.flowFieldIndex >= FlowFieldSystem.FLOW_FIELD_MAP_COUNT)
        {
            flowFieldFollowerLookup.SetComponentEnabled(entity, false);
            return;
        }
        
        if (!targets[follower.flowFieldIndex].isValid)
        {
            flowFieldFollowerLookup.SetComponentEnabled(entity, false);
            return;
        }

        int2 gridPosition = GridSystem.GetGridPosition(localTransform.Position, cellSize);
        
        // Bounds check
        if (!GridSystem.IsValidGridPosition(gridPosition, width, height))
        {
            flowFieldFollowerLookup.SetComponentEnabled(entity, false);
            return;
        }
        
        int localIndex = GridSystem.CalculateIndex(gridPosition, width);
        int globalIndex = follower.flowFieldIndex * cellsPerLayer + localIndex;
        
        // Get flow vector
        float2 flowVector = vectors[globalIndex];
        float3 moveVector = GridSystem.GetWorldMovementVector(flowVector);

        // Handle wall cells - use last valid direction
        if (costs[localIndex] == GridSystem.WALL_COST)
        {
            moveVector = follower.lastMoveVector;
        }
        else
        {
            follower.lastMoveVector = moveVector;
        }

        // Set target position ahead in flow direction
        unitMover.targetPosition =
            GridSystem.GetWorldCenterPosition(gridPosition.x, gridPosition.y, cellSize) +
            moveVector * cellSizeDouble;

        // Check if reached final destination
        float distToTarget = math.distance(localTransform.Position, follower.targetPosition);
        if (distToTarget < cellSize)
        {
            unitMover.targetPosition = follower.targetPosition;
            flowFieldFollowerLookup.SetComponentEnabled(entity, false);
            
            if (pathfindingAgentLookup.HasComponent(entity))
            {
                var agent = pathfindingAgentLookup[entity];
                agent.isActive = false;
                pathfindingAgentLookup[entity] = agent;
            }
        }

        flowFieldFollowerLookup[entity] = follower;
    }
}