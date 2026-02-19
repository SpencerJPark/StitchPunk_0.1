//#define GRID_DEBUG

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics;
using UnityEngine;

public partial struct GridSystem : ISystem {

    public const byte WALL_COST = byte.MaxValue;
    public const byte HEAVY_COST = 50;
    public const byte DEFAULT_COST = 1;
    public const int FLOW_FIELD_MAP_COUNT = 50;
    private const int MAX_ITERATIONS = 10000;

    public struct GridSystemData : IComponentData {
        public int width;
        public int height;
        public float gridNodeSize;
        public int nextGridIndex;
    }

    /// <summary>
    /// Stores all grid data in contiguous arrays for cache-friendly access.
    /// Layout: [gridIndex * (width * height) + cellIndex]
    /// </summary>
    public struct GridDataArrays : IComponentData {
        public NativeArray<byte> costMap;           // Shared cost map (physics-based)
        public NativeArray<int> bestCosts;          // Per flow field: best cost to target
        public NativeArray<float2> vectors;         // Per flow field: direction to flow
        public NativeArray<int2> targetPositions;   // Per flow field: target grid position
        public NativeArray<bool> isValid;           // Per flow field: is this flow field valid
    }

    private NativeQueue<PathRequest> pendingRequests;
    private bool costMapDirty;
    private int lastPhysicsVersion;

    private struct PathRequest {
        public int2 targetGridPosition;
        public float3 targetWorldPosition;
        public Entity requester;
    }

    [BurstCompile]
    public void OnCreate(ref SystemState state) {
        int width = 20;
        int height = 10;
        float gridNodeSize = 5f;
        int cellCount = width * height;

        // Create data arrays
        var gridDataArrays = new GridDataArrays {
            costMap = new NativeArray<byte>(cellCount, Allocator.Persistent),
            bestCosts = new NativeArray<int>(cellCount * FLOW_FIELD_MAP_COUNT, Allocator.Persistent),
            vectors = new NativeArray<float2>(cellCount * FLOW_FIELD_MAP_COUNT, Allocator.Persistent),
            targetPositions = new NativeArray<int2>(FLOW_FIELD_MAP_COUNT, Allocator.Persistent),
            isValid = new NativeArray<bool>(FLOW_FIELD_MAP_COUNT, Allocator.Persistent)
        };

        // Initialize cost map to default walkable
        for (int i = 0; i < cellCount; i++) {
            gridDataArrays.costMap[i] = DEFAULT_COST;
        }

        state.EntityManager.AddComponent<GridSystemData>(state.SystemHandle);
        state.EntityManager.SetComponentData(state.SystemHandle, new GridSystemData {
            width = width,
            height = height,
            gridNodeSize = gridNodeSize,
            nextGridIndex = 0
        });

        state.EntityManager.AddComponent<GridDataArrays>(state.SystemHandle);
        state.EntityManager.SetComponentData(state.SystemHandle, gridDataArrays);

        pendingRequests = new NativeQueue<PathRequest>(Allocator.Persistent);
        costMapDirty = true;
        lastPhysicsVersion = 0;
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state) {
        if (SystemAPI.HasComponent<GridDataArrays>(state.SystemHandle)) {
            var arrays = SystemAPI.GetComponent<GridDataArrays>(state.SystemHandle);
            if (arrays.costMap.IsCreated) arrays.costMap.Dispose();
            if (arrays.bestCosts.IsCreated) arrays.bestCosts.Dispose();
            if (arrays.vectors.IsCreated) arrays.vectors.Dispose();
            if (arrays.targetPositions.IsCreated) arrays.targetPositions.Dispose();
            if (arrays.isValid.IsCreated) arrays.isValid.Dispose();
        }
        if (pendingRequests.IsCreated) pendingRequests.Dispose();
    }

#if !GRID_DEBUG
    [BurstCompile]
#endif
    public void OnUpdate(ref SystemState state) {
        var gridSystemData = SystemAPI.GetComponent<GridSystemData>(state.SystemHandle);
        var gridDataArrays = SystemAPI.GetComponent<GridDataArrays>(state.SystemHandle);
        int cellCount = gridSystemData.width * gridSystemData.height;

        // Check if physics world changed (simplified - you might want a more sophisticated check)
        var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
        int currentPhysicsVersion = physicsWorld.PhysicsWorld.NumBodies;
        if (currentPhysicsVersion != lastPhysicsVersion) {
            costMapDirty = true;
            lastPhysicsVersion = currentPhysicsVersion;
        }

        // Update cost map only when needed
        JobHandle costMapHandle = state.Dependency;
        if (costMapDirty) {
            costMapDirty = false;
            
            // Invalidate all existing flow fields when cost map changes
            var invalidateJob = new InvalidateFlowFieldsJob {
                isValid = gridDataArrays.isValid
            };
            costMapHandle = invalidateJob.Schedule(FLOW_FIELD_MAP_COUNT, 16, state.Dependency);

            var updateCostJob = new UpdateCostMapJob {
                width = gridSystemData.width,
                gridNodeSize = gridSystemData.gridNodeSize,
                gridNodeSizeHalf = gridSystemData.gridNodeSize * 0.5f,
                collisionWorld = physicsWorld.CollisionWorld,
                costMap = gridDataArrays.costMap,
                collisionFilterWall = new CollisionFilter {
                    BelongsTo = ~0u,
                    CollidesWith = 1u << GlobalGameData.WALLS_LAYER,
                    GroupIndex = 0
                },
                collisionFilterHeavy = new CollisionFilter {
                    BelongsTo = ~0u,
                    CollidesWith = 1u << GlobalGameData.PATHFINDING_HEAVY_LAYER,
                    GroupIndex = 0
                }
            };
            costMapHandle = updateCostJob.Schedule(cellCount, 64, costMapHandle);
        }

        // Collect all path requests this frame
        pendingRequests.Clear();
        
        foreach (var (pathRequest, pathRequestEnabled, follower, followerEnabled, entity) 
            in SystemAPI.Query<
                RefRW<FlowFieldPathRequest>,
                EnabledRefRW<FlowFieldPathRequest>,
                RefRW<FlowFieldFollower>,
                EnabledRefRW<FlowFieldFollower>>()
            .WithPresent<FlowFieldFollower>()
            .WithEntityAccess()) {

            int2 targetGridPosition = GetGridPosition(pathRequest.ValueRO.targetPosition, gridSystemData.gridNodeSize);
            pathRequestEnabled.ValueRW = false;

            // Check for existing valid flow field
            int existingIndex = FindExistingFlowField(targetGridPosition, gridDataArrays);
            if (existingIndex >= 0) {
                follower.ValueRW.gridIndex = existingIndex;
                follower.ValueRW.targetPosition = pathRequest.ValueRO.targetPosition;
                followerEnabled.ValueRW = true;
                continue;
            }

            // Queue for batch processing
            pendingRequests.Enqueue(new PathRequest {
                targetGridPosition = targetGridPosition,
                targetWorldPosition = pathRequest.ValueRO.targetPosition,
                requester = entity
            });
        }

        // Process pending requests
        JobHandle flowFieldHandle = costMapHandle;
        
        while (pendingRequests.TryDequeue(out PathRequest request)) {
            // Double-check we didn't just create this flow field
            int existingIndex = FindExistingFlowField(request.targetGridPosition, gridDataArrays);
            if (existingIndex >= 0) {
                AssignFollowerToFlowField(ref state, request.requester, existingIndex, request.targetWorldPosition);
                continue;
            }

            int gridIndex = gridSystemData.nextGridIndex;
            gridSystemData.nextGridIndex = (gridSystemData.nextGridIndex + 1) % FLOW_FIELD_MAP_COUNT;

            // Schedule flow field calculation
            var initJob = new InitializeFlowFieldJob {
                gridIndex = gridIndex,
                cellCount = cellCount,
                targetGridPosition = request.targetGridPosition,
                width = gridSystemData.width,
                costMap = gridDataArrays.costMap,
                bestCosts = gridDataArrays.bestCosts,
                vectors = gridDataArrays.vectors
            };
            flowFieldHandle = initJob.Schedule(cellCount, 64, flowFieldHandle);

            var calculateJob = new CalculateFlowFieldJob {
                gridIndex = gridIndex,
                width = gridSystemData.width,
                height = gridSystemData.height,
                cellCount = cellCount,
                targetGridPosition = request.targetGridPosition,
                costMap = gridDataArrays.costMap,
                bestCosts = gridDataArrays.bestCosts,
                vectors = gridDataArrays.vectors
            };
            flowFieldHandle = calculateJob.Schedule(flowFieldHandle);

            // Mark as valid
            gridDataArrays.targetPositions[gridIndex] = request.targetGridPosition;
            gridDataArrays.isValid[gridIndex] = true;

            AssignFollowerToFlowField(ref state, request.requester, gridIndex, request.targetWorldPosition);
        }

        SystemAPI.SetComponent(state.SystemHandle, gridSystemData);
        state.Dependency = flowFieldHandle;

#if GRID_DEBUG
        flowFieldHandle.Complete();
        GridSystemDebug.Instance?.UpdateGrid(gridSystemData, gridDataArrays);
#endif
    }

    private int FindExistingFlowField(int2 targetGridPosition, GridDataArrays arrays) {
        for (int i = 0; i < FLOW_FIELD_MAP_COUNT; i++) {
            if (arrays.isValid[i] && arrays.targetPositions[i].Equals(targetGridPosition)) {
                return i;
            }
        }
        return -1;
    }

    private void AssignFollowerToFlowField(ref SystemState state, Entity entity, int gridIndex, float3 targetPosition) {
        var follower = SystemAPI.GetComponentRW<FlowFieldFollower>(entity);
        follower.ValueRW.gridIndex = gridIndex;
        follower.ValueRW.targetPosition = targetPosition;
        SystemAPI.SetComponentEnabled<FlowFieldFollower>(entity, true);
    }

    // Static utility methods
    public static int CalculateIndex(int x, int y, int width) => x + y * width;
    public static int CalculateIndex(int2 pos, int width) => pos.x + pos.y * width;
    
    public static int2 GetGridPositionFromIndex(int index, int width) {
        int y = index / width;
        int x = index % width;
        return new int2(x, y);
    }
    
    public static int2 GetGridPosition(float3 worldPosition, float gridNodeSize) {
        return new int2(
            (int)math.floor(worldPosition.x / gridNodeSize),
            (int)math.floor(worldPosition.z / gridNodeSize)
        );
    }
    
    public static float3 GetWorldPosition(int x, int y, float gridNodeSize) {
        return new float3(
            x * gridNodeSize,
            0f,
            y * gridNodeSize
        );
    }

    public static float3 GetWorldCenterPosition(int x, int y, float gridNodeSize) {
        return new float3(
            x * gridNodeSize + gridNodeSize * 0.5f,
            0f,
            y * gridNodeSize + gridNodeSize * 0.5f
        );
    }

    public static bool IsValidGridPosition(int2 pos, int width, int height) {
        return pos.x >= 0 && pos.y >= 0 && pos.x < width && pos.y < height;
    }

    public static float3 GetWorldMovementVector(float2 vector) => new float3(vector.x, 0, vector.y);
    
    public static bool IsWall(int2 gridPosition, int width, NativeArray<byte> costMap) {
        return costMap[CalculateIndex(gridPosition, width)] == WALL_COST;
    }
    
    public static bool IsValidWalkableGridPosition(float3 worldPosition, int width, int height, NativeArray<byte> costMap, float gridNodeSize) {
        int2 gridPosition = GetGridPosition(worldPosition, gridNodeSize);
        return IsValidGridPosition(gridPosition, width, height) && !IsWall(gridPosition, width, costMap);
    }
    
    public static float2 CalculateVector(int fromX, int fromY, int toX, int toY) {
        return new float2(toX, toY) - new float2(fromX, fromY);
    }
}


[BurstCompile]
public struct InvalidateFlowFieldsJob : IJobParallelFor {
    public NativeArray<bool> isValid;
    
    public void Execute(int index) {
        isValid[index] = false;
    }
}


[BurstCompile]
public struct UpdateCostMapJob : IJobParallelFor {
    [ReadOnly] public int width;
    [ReadOnly] public float gridNodeSize;
    [ReadOnly] public float gridNodeSizeHalf;
    [ReadOnly] public CollisionWorld collisionWorld;
    [ReadOnly] public CollisionFilter collisionFilterWall;
    [ReadOnly] public CollisionFilter collisionFilterHeavy;
    
    [NativeDisableParallelForRestriction]
    public NativeArray<byte> costMap;

    public void Execute(int index) {
        int x = index % width;
        int y = index / width;
        float3 worldPos = GridSystem.GetWorldCenterPosition(x, y, gridNodeSize);
        
        // Reset to default
        byte cost = GridSystem.DEFAULT_COST;
        
        // Allocate hit list for physics queries
        var hitList = new NativeList<DistanceHit>(4, Allocator.Temp);
        
        // Check for walls first (highest priority)
        if (collisionWorld.OverlapSphere(worldPos, gridNodeSizeHalf, ref hitList, collisionFilterWall)) {
            cost = GridSystem.WALL_COST;
        }
        // Only check heavy if not already a wall
        else {
            hitList.Clear();
            if (collisionWorld.OverlapSphere(worldPos, gridNodeSizeHalf, ref hitList, collisionFilterHeavy)) {
                cost = GridSystem.HEAVY_COST;
            }
        }
        
        hitList.Dispose();
        costMap[index] = cost;
    }
}


[BurstCompile]
public struct InitializeFlowFieldJob : IJobParallelFor {
    [ReadOnly] public int gridIndex;
    [ReadOnly] public int cellCount;
    [ReadOnly] public int2 targetGridPosition;
    [ReadOnly] public int width;
    [ReadOnly] public NativeArray<byte> costMap;
    
    [NativeDisableParallelForRestriction]
    public NativeArray<int> bestCosts;
    [NativeDisableParallelForRestriction]
    public NativeArray<float2> vectors;

    public void Execute(int localIndex) {
        int globalIndex = gridIndex * cellCount + localIndex;
        int x = localIndex % width;
        int y = localIndex / width;
        
        vectors[globalIndex] = new float2(0, 1);
        
        if (x == targetGridPosition.x && y == targetGridPosition.y) {
            bestCosts[globalIndex] = 0;
        } else {
            bestCosts[globalIndex] = int.MaxValue;
        }
    }
}


/// <summary>
/// Burst-compiled Dijkstra-style flow field calculation.
/// Uses a simple array-based queue for better performance than NativeQueue in tight loops.
/// </summary>
[BurstCompile]
public struct CalculateFlowFieldJob : IJob {
    [ReadOnly] public int gridIndex;
    [ReadOnly] public int width;
    [ReadOnly] public int height;
    [ReadOnly] public int cellCount;
    [ReadOnly] public int2 targetGridPosition;
    [ReadOnly] public NativeArray<byte> costMap;
    
    [NativeDisableParallelForRestriction]
    public NativeArray<int> bestCosts;
    [NativeDisableParallelForRestriction]
    public NativeArray<float2> vectors;

    public void Execute() {
        int baseOffset = gridIndex * cellCount;
        
        // Use a simple array-based queue (ring buffer style)
        var queue = new NativeArray<int>(cellCount, Allocator.Temp);
        int queueHead = 0;
        int queueTail = 0;
        
        // Enqueue target
        int targetIndex = targetGridPosition.x + targetGridPosition.y * width;
        queue[queueTail++] = targetIndex;
        
        // Process queue
        while (queueHead < queueTail) {
            int currentLocalIndex = queue[queueHead++];
            int currentGlobalIndex = baseOffset + currentLocalIndex;
            int currentBestCost = bestCosts[currentGlobalIndex];
            
            int currentX = currentLocalIndex % width;
            int currentY = currentLocalIndex / width;
            
            // Process all 8 neighbors
            ProcessNeighbor(currentX, currentY, -1,  0, currentBestCost, baseOffset, ref queue, ref queueTail);
            ProcessNeighbor(currentX, currentY,  1,  0, currentBestCost, baseOffset, ref queue, ref queueTail);
            ProcessNeighbor(currentX, currentY,  0,  1, currentBestCost, baseOffset, ref queue, ref queueTail);
            ProcessNeighbor(currentX, currentY,  0, -1, currentBestCost, baseOffset, ref queue, ref queueTail);
            ProcessNeighbor(currentX, currentY, -1, -1, currentBestCost, baseOffset, ref queue, ref queueTail);
            ProcessNeighbor(currentX, currentY,  1, -1, currentBestCost, baseOffset, ref queue, ref queueTail);
            ProcessNeighbor(currentX, currentY, -1,  1, currentBestCost, baseOffset, ref queue, ref queueTail);
            ProcessNeighbor(currentX, currentY,  1,  1, currentBestCost, baseOffset, ref queue, ref queueTail);
        }
        
        queue.Dispose();
    }
    
    private void ProcessNeighbor(int currentX, int currentY, int offsetX, int offsetY, int currentBestCost, 
                                  int baseOffset, ref NativeArray<int> queue, ref int queueTail) {
        int neighborX = currentX + offsetX;
        int neighborY = currentY + offsetY;
        
        // Bounds check
        if (neighborX < 0 || neighborX >= width || neighborY < 0 || neighborY >= height)
            return;
        
        int neighborLocalIndex = neighborX + neighborY * width;
        byte neighborCost = costMap[neighborLocalIndex];
        
        // Skip walls
        if (neighborCost == GridSystem.WALL_COST)
            return;
        
        int neighborGlobalIndex = baseOffset + neighborLocalIndex;
        int newBestCost = currentBestCost + neighborCost;
        
        // Only update if we found a better path
        if (newBestCost < bestCosts[neighborGlobalIndex]) {
            bestCosts[neighborGlobalIndex] = newBestCost;
            
            // Vector points from neighbor toward current (toward target)
            vectors[neighborGlobalIndex] = math.normalizesafe(new float2(-offsetX, -offsetY));
            
            queue[queueTail++] = neighborLocalIndex;
        }
    }
}


/// <summary>
/// Example of how a follower system would read the flow field data.
/// </summary>
[BurstCompile]
public partial struct FlowFieldFollowerSystem : ISystem {
    
    [BurstCompile]
    public void OnUpdate(ref SystemState state) {
        var gridSystemData = SystemAPI.GetComponent<GridSystem.GridSystemData>(
            SystemAPI.GetSingletonEntity<GridSystem.GridSystemData>());
        var gridDataArrays = SystemAPI.GetComponent<GridSystem.GridDataArrays>(
            SystemAPI.GetSingletonEntity<GridSystem.GridDataArrays>());
        
        int cellCount = gridSystemData.width * gridSystemData.height;
        
        var job = new FollowFlowFieldJob {
            width = gridSystemData.width,
            height = gridSystemData.height,
            cellCount = cellCount,
            gridNodeSize = gridSystemData.gridNodeSize,
            vectors = gridDataArrays.vectors,
            isValid = gridDataArrays.isValid,
            deltaTime = SystemAPI.Time.DeltaTime
        };
        
        state.Dependency = job.ScheduleParallel(state.Dependency);
    }
}


[BurstCompile]
public partial struct FollowFlowFieldJob : IJobEntity {
    [ReadOnly] public int width;
    [ReadOnly] public int height;
    [ReadOnly] public int cellCount;
    [ReadOnly] public float gridNodeSize;
    [ReadOnly] public NativeArray<float2> vectors;
    [ReadOnly] public NativeArray<bool> isValid;
    [ReadOnly] public float deltaTime;
    
    public void Execute(ref Unity.Transforms.LocalTransform transform, in FlowFieldFollower follower) {
        if (!isValid[follower.gridIndex])
            return;
        
        // Get current grid position
        int2 gridPos = GridSystem.GetGridPosition(transform.Position, gridNodeSize);
        
        if (!GridSystem.IsValidGridPosition(gridPos, width, height))
            return;
        
        int localIndex = gridPos.x + gridPos.y * width;
        int globalIndex = follower.gridIndex * cellCount + localIndex;
        
        float2 flowVector = vectors[globalIndex];
        float3 movement = new float3(flowVector.x, 0, flowVector.y);
        
        // Apply movement (you'd typically also have speed, acceleration, etc.)
        transform.Position += movement * deltaTime * 10f; // 10 is example speed
    }
}