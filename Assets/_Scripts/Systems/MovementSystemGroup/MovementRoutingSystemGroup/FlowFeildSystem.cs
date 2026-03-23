using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

/// <summary>
/// Calculates flow fields for horde pathfinding.
/// Uses shared GridSystem for cost map and utilities.
/// </summary>
[UpdateInGroup(typeof(MovementRoutingSystemGroup))]
public partial struct FlowFieldSystem : ISystem
{
    public const int FLOW_FIELD_MAP_COUNT = 50;
    private const int MAX_ITERATIONS = 10000;
    
    /// <summary>
    /// Flow field system state.
    /// </summary>
    public struct FlowFieldSystemData : IComponentData
    {
        public int nextFlowFieldIndex;
    }
    
    /// <summary>
    /// Flow field data arrays.
    /// Layout: [flowFieldIndex * cellsPerLayer + localCellIndex]
    /// Each flow field is per-layer, so total = FLOW_FIELD_MAP_COUNT * layerCount flow fields available.
    /// </summary>
    public struct FlowFieldData : IComponentData
    {
        public NativeArray<int> bestCosts;          // Best cost to reach target
        public NativeArray<float2> vectors;         // Direction to flow toward target
        public NativeArray<FlowFieldTarget> targets; // Target info per flow field
    }
    
    public struct FlowFieldTarget
    {
        public int2 gridPosition;
        public int layer;
        public bool isValid;
    }
    
    private NativeQueue<FlowFieldRequest> pendingRequests;
    
    private struct FlowFieldRequest
    {
        public int2 targetGridPosition;
        public int targetLayer;
        public float3 targetWorldPosition;
        public Entity requester;
    }
    
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GridSystem.GridConfig>();
        state.RequireForUpdate<GridSystem.GridCostMap>();
        
        pendingRequests = new NativeQueue<FlowFieldRequest>(Allocator.Persistent);
        
        // Flow field data will be created once we have grid config
    }
    
    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        if (pendingRequests.IsCreated) pendingRequests.Dispose();
        
        if (SystemAPI.HasComponent<FlowFieldData>(state.SystemHandle))
        {
            var data = SystemAPI.GetComponent<FlowFieldData>(state.SystemHandle);
            if (data.bestCosts.IsCreated) data.bestCosts.Dispose();
            if (data.vectors.IsCreated) data.vectors.Dispose();
            if (data.targets.IsCreated) data.targets.Dispose();
        }
    }
    
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var gridConfig = SystemAPI.GetSingleton<GridSystem.GridConfig>();
        var gridCostMap = SystemAPI.GetSingleton<GridSystem.GridCostMap>();
        
        // Initialize flow field data on first update
        if (!SystemAPI.HasComponent<FlowFieldData>(state.SystemHandle))
        {
            InitializeFlowFieldData(ref state, gridConfig);
        }
        
        var flowFieldSystemData = SystemAPI.GetComponent<FlowFieldSystemData>(state.SystemHandle);
        var flowFieldData = SystemAPI.GetComponent<FlowFieldData>(state.SystemHandle);
        
        int cellsPerLayer = gridConfig.width * gridConfig.height;
        
        // Collect path requests from entities with FlowFieldFollower
        CollectRequests(ref state, gridConfig);
        
        // Process pending requests
        JobHandle flowFieldHandle = state.Dependency;
        
        while (pendingRequests.TryDequeue(out FlowFieldRequest request))
        {
            // Check for existing valid flow field
            int existingIndex = FindExistingFlowField(request.targetGridPosition, request.targetLayer, flowFieldData);
            if (existingIndex >= 0)
            {
                AssignFollowerToFlowField(ref state, request.requester, existingIndex, request.targetWorldPosition);
                continue;
            }
            
            // Allocate new flow field slot
            int flowFieldIndex = flowFieldSystemData.nextFlowFieldIndex;
            flowFieldSystemData.nextFlowFieldIndex = (flowFieldSystemData.nextFlowFieldIndex + 1) % FLOW_FIELD_MAP_COUNT;
            
            // Initialize flow field
            var initJob = new InitializeFlowFieldJob
            {
                flowFieldIndex = flowFieldIndex,
                cellsPerLayer = cellsPerLayer,
                bestCosts = flowFieldData.bestCosts,
                vectors = flowFieldData.vectors
            };
            flowFieldHandle = initJob.Schedule(cellsPerLayer, 64, flowFieldHandle);
            
            // Calculate flow field
            var calculateJob = new CalculateFlowFieldJob
            {
                flowFieldIndex = flowFieldIndex,
                width = gridConfig.width,
                height = gridConfig.height,
                cellsPerLayer = cellsPerLayer,
                targetGridPosition = request.targetGridPosition,
                costs = gridCostMap.costs,
                bestCosts = flowFieldData.bestCosts,
                vectors = flowFieldData.vectors
            };
            flowFieldHandle = calculateJob.Schedule(flowFieldHandle);
            
            // Mark as valid
            var targets = flowFieldData.targets;
            targets[flowFieldIndex] = new FlowFieldTarget
            {
                gridPosition = request.targetGridPosition,
                layer = request.targetLayer,
                isValid = true
            };
            
            AssignFollowerToFlowField(ref state, request.requester, flowFieldIndex, request.targetWorldPosition);
        }
        
        SystemAPI.SetComponent(state.SystemHandle, flowFieldSystemData);
        state.Dependency = flowFieldHandle;
    }
    
    private void InitializeFlowFieldData(ref SystemState state, GridSystem.GridConfig gridConfig)
    {
        int cellsPerLayer = gridConfig.width * gridConfig.height;
        int totalCells = cellsPerLayer * FLOW_FIELD_MAP_COUNT;
        
        var flowFieldData = new FlowFieldData
        {
            bestCosts = new NativeArray<int>(totalCells, Allocator.Persistent),
            vectors = new NativeArray<float2>(totalCells, Allocator.Persistent),
            targets = new NativeArray<FlowFieldTarget>(FLOW_FIELD_MAP_COUNT, Allocator.Persistent)
        };
        
        state.EntityManager.AddComponent<FlowFieldSystemData>(state.SystemHandle);
        state.EntityManager.SetComponentData(state.SystemHandle, new FlowFieldSystemData
        {
            nextFlowFieldIndex = 0
        });
        
        state.EntityManager.AddComponent<FlowFieldData>(state.SystemHandle);
        state.EntityManager.SetComponentData(state.SystemHandle, flowFieldData);
    }
    
    private void CollectRequests(ref SystemState state, GridSystem.GridConfig gridConfig)
    {
        foreach (var (agent, pathRequest, pathRequestEnabled, follower, entity) in 
                 SystemAPI.Query<
        RefRW<PathfindingAgent>,  // Changed to RefRW
            RefRO<PathRequest>,
        EnabledRefRW<PathRequest>,
            RefRW<FlowFieldFollower>>()
            .WithPresent<FlowFieldFollower>()
            .WithEntityAccess())
        {
            // Only handle flow field requests
            if (pathRequest.ValueRO.requestedMode != PathfindingMode.FlowField)
                continue;
        
            int2 targetGridPos = GridSystem.GetGridPosition(pathRequest.ValueRO.targetPosition, gridConfig.cellSize);
            int targetLayer = GridSystem.GetLayer(pathRequest.ValueRO.targetPosition, gridConfig.layerHeight);
        
            // Set current mode
            agent.ValueRW.currentMode = PathfindingMode.FlowField;
            agent.ValueRW.isActive = true;
            agent.ValueRW.needsRepath = false;

            pathRequestEnabled.ValueRW = false;
        
            pendingRequests.Enqueue(new FlowFieldRequest
            {
                targetGridPosition = targetGridPos,
                targetLayer = targetLayer,
                targetWorldPosition = pathRequest.ValueRO.targetPosition,
                requester = entity
            });
        }
    }
    
    private int FindExistingFlowField(int2 targetPos, int layer, FlowFieldData data)
    {
        for (int i = 0; i < FLOW_FIELD_MAP_COUNT; i++)
        {
            var target = data.targets[i];
            if (target.isValid && target.gridPosition.Equals(targetPos) && target.layer == layer)
            {
                return i;
            }
        }
        return -1;
    }
    
    private void AssignFollowerToFlowField(ref SystemState state, Entity entity, int flowFieldIndex, float3 targetPosition)
    {
        if (!SystemAPI.HasComponent<FlowFieldFollower>(entity)) return;
        
        var follower = SystemAPI.GetComponentRW<FlowFieldFollower>(entity);
        follower.ValueRW.flowFieldIndex = flowFieldIndex;
        follower.ValueRW.targetPosition = targetPosition;
        SystemAPI.SetComponentEnabled<FlowFieldFollower>(entity, true);
    }
    
    /// <summary>
    /// Invalidate all flow fields (call when cost map changes significantly).
    /// </summary>
    public static void InvalidateAllFlowFields(ref FlowFieldData data)
    {
        for (int i = 0; i < FLOW_FIELD_MAP_COUNT; i++)
        {
            var target = data.targets[i];
            target.isValid = false;
            data.targets[i] = target;
        }
    }
}

/// <summary>
/// Initialize a flow field slot to default values.
/// </summary>
[BurstCompile]
public struct InitializeFlowFieldJob : IJobParallelFor
{
    [ReadOnly] public int flowFieldIndex;
    [ReadOnly] public int cellsPerLayer;
    
    [NativeDisableParallelForRestriction]
    public NativeArray<int> bestCosts;
    
    [NativeDisableParallelForRestriction]
    public NativeArray<float2> vectors;
    
    public void Execute(int localIndex)
    {
        int globalIndex = flowFieldIndex * cellsPerLayer + localIndex;
        bestCosts[globalIndex] = int.MaxValue;
        vectors[globalIndex] = float2.zero;
    }
}

/// <summary>
/// Calculate flow field using Dijkstra's algorithm.
/// </summary>
[BurstCompile]
public struct CalculateFlowFieldJob : IJob
{
    [ReadOnly] public int flowFieldIndex;
    [ReadOnly] public int width;
    [ReadOnly] public int height;
    [ReadOnly] public int cellsPerLayer;
    [ReadOnly] public int2 targetGridPosition;
    
    [ReadOnly] public NativeArray<byte> costs;
    
    [NativeDisableParallelForRestriction]
    public NativeArray<int> bestCosts;
    
    [NativeDisableParallelForRestriction]
    public NativeArray<float2> vectors;
    
    public void Execute()
    {
        // Validate target
        if (!GridSystem.IsValidGridPosition(targetGridPosition, width, height))
            return;
            
        int targetLocalIndex = GridSystem.CalculateIndex(targetGridPosition, width);
        if (costs[targetLocalIndex] == GlobalGameData.WALL_COST)
            return;
        
        // BFS queue (ring buffer)
        var queue = new NativeArray<int>(cellsPerLayer, Allocator.Temp);
        int queueHead = 0;
        int queueTail = 0;
        
        // Initialize target cell
        int targetGlobalIndex = flowFieldIndex * cellsPerLayer + targetLocalIndex;
        bestCosts[targetGlobalIndex] = 0;
        queue[queueTail++] = targetLocalIndex;
        
        // Dijkstra's algorithm
        int iterations = 0;
        const int MAX_ITERATIONS = 50000;
        
        while (queueHead != queueTail && iterations++ < MAX_ITERATIONS)
        {
            int currentLocalIndex = queue[queueHead++];
            if (queueHead >= cellsPerLayer) queueHead = 0;
            
            int2 currentPos = GridSystem.GetGridPositionFromIndex(currentLocalIndex, width);
            int currentGlobalIndex = flowFieldIndex * cellsPerLayer + currentLocalIndex;
            int currentBestCost = bestCosts[currentGlobalIndex];
            
            // Check all 8 neighbors
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    
                    int2 neighborPos = currentPos + new int2(dx, dy);
                    
                    if (!GridSystem.IsValidGridPosition(neighborPos, width, height))
                        continue;
                    
                    int neighborLocalIndex = GridSystem.CalculateIndex(neighborPos, width);
                    byte neighborCost = costs[neighborLocalIndex];
                    
                    if (neighborCost == GlobalGameData.WALL_COST)
                        continue;
                    
                    // Diagonal costs more
                    int moveCost = (dx != 0 && dy != 0) ? 14 : 10;
                    moveCost *= neighborCost;
                    
                    int newBestCost = currentBestCost + moveCost;
                    int neighborGlobalIndex = flowFieldIndex * cellsPerLayer + neighborLocalIndex;
                    
                    if (newBestCost >= bestCosts[neighborGlobalIndex])
                        continue;
                    
                    bestCosts[neighborGlobalIndex] = newBestCost;
                    vectors[neighborGlobalIndex] = math.normalizesafe(new float2(-dx, -dy));
                    
                    queue[queueTail++] = neighborLocalIndex;
                    if (queueTail >= cellsPerLayer) queueTail = 0;
                }
            }
        }
        
        queue.Dispose();
    }
}