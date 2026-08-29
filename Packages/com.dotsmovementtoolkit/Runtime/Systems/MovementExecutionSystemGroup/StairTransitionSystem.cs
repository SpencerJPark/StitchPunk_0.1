using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace DotsMovementToolkit
{
/// <summary>
/// Handles entity transitions between grid layers via stairs/ramps/elevators.
/// When an entity enters a stair cell, smoothly transitions them to the connected layer.
/// </summary>
[UpdateInGroup(typeof(MovementExecutionSystemGroup))]
[UpdateBefore(typeof(UnitMoverSystem))]
public partial struct StairTransitionSystem : ISystem
{
    private EntityQuery stairConnectionQuery;
    
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GridSystem.GridConfig>();
        stairConnectionQuery = state.GetEntityQuery(ComponentType.ReadOnly<GridSystem.StairConnection>());
    }
    
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var gridConfig = SystemAPI.GetSingleton<GridSystem.GridConfig>();
        
        // Get stair connections
        if (stairConnectionQuery.IsEmpty) return;
        
        var stairEntity = stairConnectionQuery.GetSingletonEntity();
        var stairConnections = state.EntityManager.GetBuffer<GridSystem.StairConnection>(stairEntity);
        
        if (stairConnections.Length == 0) return;
        
        // Copy to native array for job
        var stairs = new NativeArray<GridSystem.StairConnection>(stairConnections.Length, Allocator.TempJob);
        for (int i = 0; i < stairConnections.Length; i++)
        {
            stairs[i] = stairConnections[i];
        }
        
        // Process flow field followers
        var flowFieldJob = new StairTransitionFlowFieldJob
        {
            cellSize = gridConfig.cellSize,
            layerHeight = gridConfig.layerHeight,
            stairs = stairs,
            transitionDistance = gridConfig.cellSize * 0.5f
        };
        state.Dependency = flowFieldJob.ScheduleParallel(state.Dependency);
        
        // Process D* Lite followers
        var dstarJob = new StairTransitionDStarJob
        {
            cellSize = gridConfig.cellSize,
            layerHeight = gridConfig.layerHeight,
            stairs = stairs,
            transitionDistance = gridConfig.cellSize * 0.5f
        };
        state.Dependency = dstarJob.ScheduleParallel(state.Dependency);
        
        // Dispose stairs array after jobs complete
        state.Dependency = stairs.Dispose(state.Dependency);
    }
}

/// <summary>
/// Handles stair transitions for flow field followers.
/// </summary>
[BurstCompile]
[WithAll(typeof(FlowFieldFollower))]
public partial struct StairTransitionFlowFieldJob : IJobEntity
{
    [ReadOnly] public float cellSize;
    [ReadOnly] public float layerHeight;
    [ReadOnly] public NativeArray<GridSystem.StairConnection> stairs;
    [ReadOnly] public float transitionDistance;
    
    public void Execute(
        ref LocalTransform localTransform,
        ref FlowFieldFollower follower,
        ref Movement movement)
    {
        float3 position = localTransform.Position;
        int2 gridPos = GridSystem.GetGridPosition(position, cellSize);
        int currentLayer = follower.currentLayer;
        
        // Check if on a stair cell
        for (int i = 0; i < stairs.Length; i++)
        {
            var stair = stairs[i];
            
            // Check if we're at this stair's entry point
            bool atEntry = gridPos.Equals(stair.gridPosition) && currentLayer == stair.fromLayer;
            bool atExit = stair.bidirectional && gridPos.Equals(stair.gridPosition) && currentLayer == stair.toLayer;
            
            if (!atEntry && !atExit) continue;
            
            // Check distance to stair center
            float3 stairCenter = atEntry ? stair.entryWorldPosition : stair.exitWorldPosition;
            float distToStair = math.distance(new float2(position.x, position.z), 
                                               new float2(stairCenter.x, stairCenter.z));
            
            if (distToStair > transitionDistance) continue;
            
            // Transition to new layer
            if (atEntry)
            {
                follower.currentLayer = stair.toLayer;
                localTransform.Position = stair.exitWorldPosition;
                movement.targetPosition = stair.exitWorldPosition;
            }
            else // atExit (bidirectional going back)
            {
                follower.currentLayer = stair.fromLayer;
                localTransform.Position = stair.entryWorldPosition;
                movement.targetPosition = stair.entryWorldPosition;
            }
            
            break;
        }
    }
}

/// <summary>
/// Handles stair transitions for D* Lite followers.
/// </summary>
[BurstCompile]
[WithAll(typeof(DStarLiteFollower))]
public partial struct StairTransitionDStarJob : IJobEntity
{
    [ReadOnly] public float cellSize;
    [ReadOnly] public float layerHeight;
    [ReadOnly] public NativeArray<GridSystem.StairConnection> stairs;
    [ReadOnly] public float transitionDistance;
    
    public void Execute(
        ref LocalTransform localTransform,
        ref DStarLiteFollower follower,
        ref Movement movement)
    {
        float3 position = localTransform.Position;
        int2 gridPos = GridSystem.GetGridPosition(position, cellSize);
        int currentLayer = follower.currentLayer;
        
        // Check if on a stair cell
        for (int i = 0; i < stairs.Length; i++)
        {
            var stair = stairs[i];
            
            bool atEntry = gridPos.Equals(stair.gridPosition) && currentLayer == stair.fromLayer;
            bool atExit = stair.bidirectional && gridPos.Equals(stair.gridPosition) && currentLayer == stair.toLayer;
            
            if (!atEntry && !atExit) continue;
            
            float3 stairCenter = atEntry ? stair.entryWorldPosition : stair.exitWorldPosition;
            float distToStair = math.distance(new float2(position.x, position.z), 
                                               new float2(stairCenter.x, stairCenter.z));
            
            if (distToStair > transitionDistance) continue;
            
            if (atEntry)
            {
                follower.currentLayer = stair.toLayer;
                localTransform.Position = stair.exitWorldPosition;
                movement.targetPosition = stair.exitWorldPosition;
            }
            else
            {
                follower.currentLayer = stair.fromLayer;
                localTransform.Position = stair.entryWorldPosition;
                movement.targetPosition = stair.entryWorldPosition;
            }
            
            break;
        }
    }
}

/// <summary>
/// Utility methods for working with stairs.
/// </summary>
public static class StairUtils
{
    /// <summary>
    /// Add a stair connection between two layers.
    /// </summary>
    public static void AddStairConnection(
        EntityManager em,
        int2 gridPosition,
        int fromLayer,
        int toLayer,
        float3 entryWorldPosition,
        float3 exitWorldPosition,
        bool bidirectional = true)
    {
        var query = em.CreateEntityQuery(ComponentType.ReadWrite<GridSystem.StairConnection>());
        if (query.IsEmpty) return;
        
        var stairEntity = query.GetSingletonEntity();
        var buffer = em.GetBuffer<GridSystem.StairConnection>(stairEntity);
        
        buffer.Add(new GridSystem.StairConnection
        {
            gridPosition = gridPosition,
            fromLayer = fromLayer,
            toLayer = toLayer,
            entryWorldPosition = entryWorldPosition,
            exitWorldPosition = exitWorldPosition,
            bidirectional = bidirectional
        });
    }
    
    /// <summary>
    /// Create stair connections for a standard staircase.
    /// </summary>
    public static void CreateStaircase(
        EntityManager em,
        float3 bottomPosition,
        float3 topPosition,
        float cellSize,
        float layerHeight,
        bool bidirectional = true)
    {
        int2 bottomGridPos = GridSystem.GetGridPosition(bottomPosition, cellSize);
        int2 topGridPos = GridSystem.GetGridPosition(topPosition, cellSize);
        int bottomLayer = GridSystem.GetLayer(bottomPosition, layerHeight);
        int topLayer = GridSystem.GetLayer(topPosition, layerHeight);
        
        AddStairConnection(em, bottomGridPos, bottomLayer, topLayer, bottomPosition, topPosition, bidirectional);
    }
}
}