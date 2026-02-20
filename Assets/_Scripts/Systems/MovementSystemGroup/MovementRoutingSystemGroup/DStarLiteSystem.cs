using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// DOTS-compatible D* Lite pathfinding system.
/// Optimized for individual NPCs with unique destinations.
/// </summary>
[UpdateInGroup(typeof(MovementRoutingSystemGroup))]
public partial struct DStarLiteSystem : ISystem
{
    public const int MAX_ACTIVE_PATHS = 256;
    public const int MAX_NODES_PER_PATH = 512;
    public const float NODE_SIZE = 1f; // Grid resolution for D* Lite
    
    public struct DStarLiteData : IComponentData
    {
        public int width;
        public int height;
        public float nodeSize;
        public NativeArray<byte> costMap;           // Shared with flow field system
        public NativeArray<DStarNode> nodes;        // Node data for pathfinding
        public NativeArray<PathData> activePaths;   // Active path computations
        public int nextPathIndex;
    }
    
    public struct DStarNode
    {
        public float g;      // Cost from start
        public float rhs;    // One-step lookahead cost
        public float2 key;   // Priority key (k1, k2)
        public int2 position;
        public bool inOpenSet;
    }
    
    public struct PathData
    {
        public int2 startPosition;
        public int2 goalPosition;
        public Entity owner;
        public bool isValid;
        public bool needsUpdate;
        public float km;     // Key modifier for incremental updates
    }
    
    private NativeQueue<PathComputeRequest> pendingRequests;
    private bool isInitialized;
    
    private struct PathComputeRequest
    {
        public Entity entity;
        public int2 startPosition;
        public int2 goalPosition;
    }

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GridSystem.GridConfig>();
        state.RequireForUpdate<GridSystem.GridCostMap>();
        
        pendingRequests = new NativeQueue<PathComputeRequest>(Allocator.Persistent);
        isInitialized = false;
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        if (pendingRequests.IsCreated) pendingRequests.Dispose();
        
        if (SystemAPI.HasComponent<DStarLiteData>(state.SystemHandle))
        {
            var data = SystemAPI.GetComponent<DStarLiteData>(state.SystemHandle);
            if (data.nodes.IsCreated) data.nodes.Dispose();
            if (data.activePaths.IsCreated) data.activePaths.Dispose();
        }
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // Initialize using grid system data if not already done
        if (!isInitialized)
        {
            InitializeFromFlowFieldSystem(ref state);
            isInitialized = true;
        }
        
        var dstarData = SystemAPI.GetComponent<DStarLiteData>(state.SystemHandle);
        var gridCostMap = SystemAPI.GetSingleton<GridSystem.GridCostMap>();
        
        // Collect path requests from D* Lite followers
        CollectPathRequests(ref state, ref dstarData);
        
        // Process pending path computations
        ProcessPathRequests(ref state, ref dstarData, gridCostMap);
        
        // Update followers with computed paths
        UpdateFollowers(ref state, ref dstarData);
        
        SystemAPI.SetComponent(state.SystemHandle, dstarData);
    }
    
    private void InitializeFromFlowFieldSystem(ref SystemState state)
    {
        var gridConfig = SystemAPI.GetSingleton<GridSystem.GridConfig>();
        int cellCount = gridConfig.width * gridConfig.height;
        
        var dstarData = new DStarLiteData
        {
            width = gridConfig.width,
            height = gridConfig.height,
            nodeSize = gridConfig.cellSize,
            nodes = new NativeArray<DStarNode>(cellCount, Allocator.Persistent),
            activePaths = new NativeArray<PathData>(MAX_ACTIVE_PATHS, Allocator.Persistent),
            nextPathIndex = 0
        };
        
        // Initialize nodes
        for (int i = 0; i < cellCount; i++)
        {
            int x = i % dstarData.width;
            int y = i / dstarData.width;
            dstarData.nodes[i] = new DStarNode
            {
                g = float.MaxValue,
                rhs = float.MaxValue,
                key = new float2(float.MaxValue, float.MaxValue),
                position = new int2(x, y),
                inOpenSet = false
            };
        }
        
        state.EntityManager.AddComponent<DStarLiteData>(state.SystemHandle);
        state.EntityManager.SetComponentData(state.SystemHandle, dstarData);
    }
    
    private void CollectPathRequests(ref SystemState state, ref DStarLiteData dstarData)
    {
        pendingRequests.Clear();
        
        foreach (var (agent, follower, request, requestEnabled, entity) in 
            SystemAPI.Query<
                RefRO<PathfindingAgent>,
                RefRW<DStarLiteFollower>,
                RefRO<PathRequest>,
                EnabledRefRW<PathRequest>>()
            .WithAll<DStarLiteFollower>()
            .WithEntityAccess())
        {
            if (agent.ValueRO.currentMode != PathfindingMode.DStarLite)
                continue;
                
            int2 startPos = WorldToGrid(follower.ValueRO.nextWaypoint, dstarData.nodeSize);
            int2 goalPos = WorldToGrid(request.ValueRO.targetPosition, dstarData.nodeSize);
            
            pendingRequests.Enqueue(new PathComputeRequest
            {
                entity = entity,
                startPosition = startPos,
                goalPosition = goalPos
            });
            
            requestEnabled.ValueRW = false;
        }
    }
    
    private void ProcessPathRequests(ref SystemState state, ref DStarLiteData dstarData, 
        GridSystem.GridCostMap gridCostMap)
    {
        while (pendingRequests.TryDequeue(out PathComputeRequest request))
        {
            // Find or allocate path slot
            int pathIndex = FindOrAllocatePathSlot(ref dstarData, request.entity);
            if (pathIndex < 0) continue;
            
            // Update path data
            var pathData = dstarData.activePaths[pathIndex];
            pathData.startPosition = request.startPosition;
            pathData.goalPosition = request.goalPosition;
            pathData.owner = request.entity;
            pathData.isValid = true;
            pathData.needsUpdate = true;
            pathData.km = 0;
            dstarData.activePaths[pathIndex] = pathData;
            
            // Compute path using D* Lite algorithm
            ComputeDStarLitePath(ref dstarData, pathIndex, gridCostMap.costs);
            
            // Update the follower component
            if (SystemAPI.HasComponent<DStarLiteFollower>(request.entity))
            {
                var follower = SystemAPI.GetComponentRW<DStarLiteFollower>(request.entity);
                follower.ValueRW.pathDataIndex = pathIndex;
                follower.ValueRW.goalNodeIndex = CalculateIndex(request.goalPosition, dstarData.width);
                follower.ValueRW.targetPosition = GridToWorld(request.goalPosition, dstarData.nodeSize);
                
                // Set initial waypoint
                int2 nextNode = GetNextNodeTowardGoal(ref dstarData, pathIndex, request.startPosition, gridCostMap.costs);
                follower.ValueRW.nextWaypoint = GridToWorld(nextNode, dstarData.nodeSize);
                
                SystemAPI.SetComponentEnabled<DStarLiteFollower>(request.entity, true);
            }
        }
    }
    
    private void ComputeDStarLitePath(ref DStarLiteData dstarData, int pathIndex, NativeArray<byte> costMap)
    {
        var pathData = dstarData.activePaths[pathIndex];
        int cellCount = dstarData.width * dstarData.height;
        
        // Reset nodes for this computation
        for (int i = 0; i < cellCount; i++)
        {
            var node = dstarData.nodes[i];
            node.g = float.MaxValue;
            node.rhs = float.MaxValue;
            node.inOpenSet = false;
            dstarData.nodes[i] = node;
        }
        
        // Initialize goal node
        int goalIndex = CalculateIndex(pathData.goalPosition, dstarData.width);
        var goalNode = dstarData.nodes[goalIndex];
        goalNode.rhs = 0;
        goalNode.key = CalculateKey(pathData.goalPosition, pathData.startPosition, 0, 0, pathData.km);
        goalNode.inOpenSet = true;
        dstarData.nodes[goalIndex] = goalNode;
        
        // Use a simple array-based priority queue
        var openSet = new NativeList<int>(256, Allocator.Temp);
        openSet.Add(goalIndex);
        
        int maxIterations = 5000;
        int iterations = 0;
        
        while (openSet.Length > 0 && iterations++ < maxIterations)
        {
            // Find node with smallest key
            int bestIdx = 0;
            float2 bestKey = GetNodeKey(dstarData.nodes[openSet[0]]);
            
            for (int i = 1; i < openSet.Length; i++)
            {
                float2 key = GetNodeKey(dstarData.nodes[openSet[i]]);
                if (KeyLessThan(key, bestKey))
                {
                    bestKey = key;
                    bestIdx = i;
                }
            }
            
            int currentIndex = openSet[bestIdx];
            var currentNode = dstarData.nodes[currentIndex];
            
            int startIndex = CalculateIndex(pathData.startPosition, dstarData.width);
            var startNode = dstarData.nodes[startIndex];
            
            // Check termination conditions
            if (!KeyLessThan(bestKey, CalculateKey(pathData.startPosition, pathData.startPosition, 
                startNode.g, startNode.rhs, pathData.km)) && 
                math.abs(startNode.rhs - startNode.g) < 0.001f)
            {
                break;
            }
            
            // Remove from open set
            openSet.RemoveAtSwapBack(bestIdx);
            currentNode.inOpenSet = false;
            
            if (currentNode.g > currentNode.rhs)
            {
                currentNode.g = currentNode.rhs;
                dstarData.nodes[currentIndex] = currentNode;
                
                // Update predecessors
                UpdatePredecessors(ref dstarData, currentIndex, ref openSet, pathData, costMap);
            }
            else
            {
                currentNode.g = float.MaxValue;
                dstarData.nodes[currentIndex] = currentNode;
                
                UpdateVertex(ref dstarData, currentIndex, ref openSet, pathData, costMap);
                UpdatePredecessors(ref dstarData, currentIndex, ref openSet, pathData, costMap);
            }
        }
        
        openSet.Dispose();
        
        // Mark path as valid
        pathData.needsUpdate = false;
        dstarData.activePaths[pathIndex] = pathData;
    }
    
    private void UpdatePredecessors(ref DStarLiteData dstarData, int nodeIndex, 
        ref NativeList<int> openSet, PathData pathData, NativeArray<byte> costMap)
    {
        int2 pos = dstarData.nodes[nodeIndex].position;
        
        // Check all 8 neighbors
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                
                int2 neighborPos = pos + new int2(dx, dy);
                if (!IsValidPosition(neighborPos, dstarData.width, dstarData.height))
                    continue;
                    
                int neighborIndex = CalculateIndex(neighborPos, dstarData.width);
                if (costMap[neighborIndex] == GridSystem.WALL_COST)
                    continue;
                    
                UpdateVertex(ref dstarData, neighborIndex, ref openSet, pathData, costMap);
            }
        }
    }
    
    private void UpdateVertex(ref DStarLiteData dstarData, int nodeIndex, 
        ref NativeList<int> openSet, PathData pathData, NativeArray<byte> costMap)
    {
        var node = dstarData.nodes[nodeIndex];
        int2 pos = node.position;
        int goalIndex = CalculateIndex(pathData.goalPosition, dstarData.width);
        
        if (nodeIndex != goalIndex)
        {
            // Calculate minimum RHS from successors
            float minRhs = float.MaxValue;
            
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    
                    int2 neighborPos = pos + new int2(dx, dy);
                    if (!IsValidPosition(neighborPos, dstarData.width, dstarData.height))
                        continue;
                        
                    int neighborIndex = CalculateIndex(neighborPos, dstarData.width);
                    if (costMap[neighborIndex] == GridSystem.WALL_COST)
                        continue;
                        
                    var neighborNode = dstarData.nodes[neighborIndex];
                    float cost = CalculateMoveCost(dx, dy, costMap[neighborIndex]);
                    float candidateRhs = neighborNode.g + cost;
                    
                    if (candidateRhs < minRhs)
                        minRhs = candidateRhs;
                }
            }
            
            node.rhs = minRhs;
        }
        
        // Remove from open set if present
        if (node.inOpenSet)
        {
            for (int i = 0; i < openSet.Length; i++)
            {
                if (openSet[i] == nodeIndex)
                {
                    openSet.RemoveAtSwapBack(i);
                    break;
                }
            }
            node.inOpenSet = false;
        }
        
        // Add to open set if inconsistent
        if (math.abs(node.g - node.rhs) > 0.001f)
        {
            node.key = CalculateKey(pos, pathData.startPosition, node.g, node.rhs, pathData.km);
            node.inOpenSet = true;
            openSet.Add(nodeIndex);
        }
        
        dstarData.nodes[nodeIndex] = node;
    }
    
    private int2 GetNextNodeTowardGoal(ref DStarLiteData dstarData, int pathIndex, 
        int2 currentPos, NativeArray<byte> costMap)
    {
        float bestScore = float.MaxValue;
        int2 bestNeighbor = currentPos;
        
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                
                int2 neighborPos = currentPos + new int2(dx, dy);
                if (!IsValidPosition(neighborPos, dstarData.width, dstarData.height))
                    continue;
                    
                int neighborIndex = CalculateIndex(neighborPos, dstarData.width);
                if (costMap[neighborIndex] == GridSystem.WALL_COST)
                    continue;
                    
                var neighborNode = dstarData.nodes[neighborIndex];
                float cost = CalculateMoveCost(dx, dy, costMap[neighborIndex]);
                float score = cost + neighborNode.g;
                
                if (score < bestScore)
                {
                    bestScore = score;
                    bestNeighbor = neighborPos;
                }
            }
        }
        
        return bestNeighbor;
    }
    
    private void UpdateFollowers(ref SystemState state, ref DStarLiteData dstarData)
    {
        var gridCostMap = SystemAPI.GetSingleton<GridSystem.GridCostMap>();
        
        foreach (var (follower, transform, entity) in 
            SystemAPI.Query<RefRW<DStarLiteFollower>, RefRO<Unity.Transforms.LocalTransform>>()
            .WithEntityAccess())
        {
            if (follower.ValueRO.pathDataIndex < 0 || 
                follower.ValueRO.pathDataIndex >= MAX_ACTIVE_PATHS)
                continue;
                
            var pathData = dstarData.activePaths[follower.ValueRO.pathDataIndex];
            if (!pathData.isValid || pathData.owner != entity)
                continue;
            
            // Check if reached current waypoint
            float3 currentPos = transform.ValueRO.Position;
            float distToWaypoint = math.distance(
                new float2(currentPos.x, currentPos.z),
                new float2(follower.ValueRO.nextWaypoint.x, follower.ValueRO.nextWaypoint.z));
                
            if (distToWaypoint < dstarData.nodeSize * 0.5f)
            {
                // Get next waypoint
                int2 currentGrid = WorldToGrid(currentPos, dstarData.nodeSize);
                int2 nextNode = GetNextNodeTowardGoal(ref dstarData, follower.ValueRO.pathDataIndex, 
                    currentGrid, gridCostMap.costs);
                    
                follower.ValueRW.nextWaypoint = GridToWorld(nextNode, dstarData.nodeSize);
                follower.ValueRW.currentNodeIndex = CalculateIndex(currentGrid, dstarData.width);
            }
            
            // Update last move direction
            float3 toWaypoint = follower.ValueRO.nextWaypoint - currentPos;
            if (math.lengthsq(toWaypoint) > 0.001f)
            {
                follower.ValueRW.lastMoveDirection = math.normalize(toWaypoint);
            }
        }
    }
    
    private int FindOrAllocatePathSlot(ref DStarLiteData dstarData, Entity entity)
    {
        // First, look for existing slot for this entity
        for (int i = 0; i < MAX_ACTIVE_PATHS; i++)
        {
            if (dstarData.activePaths[i].owner == entity)
                return i;
        }
        
        // Look for empty slot
        for (int i = 0; i < MAX_ACTIVE_PATHS; i++)
        {
            if (!dstarData.activePaths[i].isValid)
                return i;
        }
        
        // Use round-robin if all slots full
        int slot = dstarData.nextPathIndex;
        dstarData.nextPathIndex = (dstarData.nextPathIndex + 1) % MAX_ACTIVE_PATHS;
        return slot;
    }
    
    // Utility functions
    private static int2 WorldToGrid(float3 worldPos, float nodeSize)
    {
        return new int2(
            (int)math.floor(worldPos.x / nodeSize),
            (int)math.floor(worldPos.z / nodeSize)
        );
    }
    
    private static float3 GridToWorld(int2 gridPos, float nodeSize)
    {
        return new float3(
            gridPos.x * nodeSize + nodeSize * 0.5f,
            0f,
            gridPos.y * nodeSize + nodeSize * 0.5f
        );
    }
    
    private static int CalculateIndex(int2 pos, int width) => pos.x + pos.y * width;
    
    private static bool IsValidPosition(int2 pos, int width, int height)
    {
        return pos.x >= 0 && pos.y >= 0 && pos.x < width && pos.y < height;
    }
    
    private static float CalculateMoveCost(int dx, int dy, byte terrainCost)
    {
        float baseCost = (dx != 0 && dy != 0) ? 1.414f : 1f; // Diagonal vs cardinal
        return baseCost * terrainCost;
    }
    
    private static float2 CalculateKey(int2 pos, int2 start, float g, float rhs, float km)
    {
        float h = Heuristic(pos, start);
        float minGRhs = math.min(g, rhs);
        return new float2(minGRhs + h + km, minGRhs);
    }
    
    private static float2 GetNodeKey(DStarNode node) => node.key;
    
    private static bool KeyLessThan(float2 a, float2 b)
    {
        return a.x < b.x || (math.abs(a.x - b.x) < 0.001f && a.y < b.y);
    }
    
    private static float Heuristic(int2 a, int2 b)
    {
        // Octile distance
        int dx = math.abs(a.x - b.x);
        int dy = math.abs(a.y - b.y);
        return math.max(dx, dy) + 0.414f * math.min(dx, dy);
    }
}




