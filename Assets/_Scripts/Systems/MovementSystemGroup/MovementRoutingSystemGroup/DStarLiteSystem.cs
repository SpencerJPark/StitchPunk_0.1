using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

/// <summary>
/// DOTS-compatible D* Lite pathfinding system.
/// Path computation is sequential, but follower updates are parallel.
/// </summary>
[UpdateInGroup(typeof(MovementRoutingSystemGroup))]
public partial struct DStarLiteSystem : ISystem
{
    public const int MAX_ACTIVE_PATHS = 256;
    public const int MAX_NODES_PER_PATH = 512;
    
    public struct DStarLiteData : IComponentData
    {
        public int width;
        public int height;
        public float nodeSize;
        public NativeArray<DStarNode> nodes;
        public NativeArray<PathData> activePaths;
        public int nextPathIndex;
    }
    
    public struct DStarNode
    {
        public float g;
        public float rhs;
        public float2 key;
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
        public float km;
    }
    
    public struct PathComputeRequest
    {
        public Entity entity;
        public float3 currentPosition;
        public float3 targetPosition;
    }
    
    private NativeList<PathComputeRequest> pendingRequests;
    private bool isInitialized;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GridSystem.GridConfig>();
        state.RequireForUpdate<GridSystem.GridCostMap>();
        
        pendingRequests = new NativeList<PathComputeRequest>(64, Allocator.Persistent);
        isInitialized = false;
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        if (pendingRequests.IsCreated) 
            pendingRequests.Dispose();
        
        if (SystemAPI.HasComponent<DStarLiteData>(state.SystemHandle))
        {
            DStarLiteData data = SystemAPI.GetComponent<DStarLiteData>(state.SystemHandle);
            if (data.nodes.IsCreated) 
                data.nodes.Dispose();
            if (data.activePaths.IsCreated) 
                data.activePaths.Dispose();
        }
    }

    public void OnUpdate(ref SystemState state)
    {
        if (!isInitialized)
        {
            InitializeFromGridSystem(ref state);
            isInitialized = true;
        }
        
        if (!SystemAPI.HasComponent<DStarLiteData>(state.SystemHandle))
            return;
        
        DStarLiteData dstarData = SystemAPI.GetComponent<DStarLiteData>(state.SystemHandle);
        GridSystem.GridCostMap gridCostMap = SystemAPI.GetSingleton<GridSystem.GridCostMap>();
        
        EndSimulationEntityCommandBufferSystem.Singleton ecbSingleton = 
            SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        
        // Step 1: Collect path requests (parallel job writes to NativeList)
        pendingRequests.Clear();
        
        CollectPathRequestsJob collectJob = new CollectPathRequestsJob
        {
            pendingRequests = pendingRequests.AsParallelWriter()
        };
        state.Dependency = collectJob.ScheduleParallel(state.Dependency);
        state.Dependency.Complete();
        
        // Step 2: Process collected requests (sequential - path computation)
        ProcessPathRequests(ref state, ref dstarData, gridCostMap);
        
        // Step 3: Update followers (parallel job)
        EntityCommandBuffer.ParallelWriter ecb = 
            ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();
        
        UpdateFollowersJob updateJob = new UpdateFollowersJob
        {
            nodeSize = dstarData.nodeSize,
            width = dstarData.width,
            height = dstarData.height,
            nodes = dstarData.nodes,
            activePaths = dstarData.activePaths,
            costs = gridCostMap.costs,
            ecb = ecb
        };
        state.Dependency = updateJob.ScheduleParallel(state.Dependency);
        
        SystemAPI.SetComponent(state.SystemHandle, dstarData);
    }
    
    private void InitializeFromGridSystem(ref SystemState state)
    {
        GridSystem.GridConfig gridConfig = SystemAPI.GetSingleton<GridSystem.GridConfig>();
        int cellCount = gridConfig.width * gridConfig.height;
        
        DStarLiteData dstarData = new DStarLiteData
        {
            width = gridConfig.width,
            height = gridConfig.height,
            nodeSize = gridConfig.cellSize,
            nodes = new NativeArray<DStarNode>(cellCount, Allocator.Persistent),
            activePaths = new NativeArray<PathData>(MAX_ACTIVE_PATHS, Allocator.Persistent),
            nextPathIndex = 0
        };
        
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
    
    private void ProcessPathRequests(ref SystemState state, ref DStarLiteData dstarData, GridSystem.GridCostMap gridCostMap)
    {
        for (int r = 0; r < pendingRequests.Length; r++)
        {
            PathComputeRequest request = pendingRequests[r];
            Entity entity = request.entity;
            
            if (!SystemAPI.HasComponent<DStarLiteFollower>(entity))
                continue;
            if (!SystemAPI.HasComponent<UnitMover>(entity))
                continue;
            if (!SystemAPI.HasComponent<PathfindingAgent>(entity))
                continue;
            
            float3 currentPos = request.currentPosition;
            float3 targetPos = request.targetPosition;
            
            int2 startGrid = PathfindingUtils.WorldToGrid(currentPos, dstarData.nodeSize);
            int2 goalGrid = PathfindingUtils.WorldToGrid(targetPos, dstarData.nodeSize);
            
            int pathIndex = FindOrAllocatePathSlot(ref dstarData, entity);
            if (pathIndex < 0)
            {
                SystemAPI.SetComponentEnabled<PathRequest>(entity, false);
                continue;
            }
            
            PathData pathData = dstarData.activePaths[pathIndex];
            pathData.startPosition = startGrid;
            pathData.goalPosition = goalGrid;
            pathData.owner = entity;
            pathData.isValid = true;
            pathData.needsUpdate = true;
            pathData.km = 0;
            dstarData.activePaths[pathIndex] = pathData;
            
            ComputeDStarLitePath(ref dstarData, pathIndex, gridCostMap.costs);
            
            int2 nextNode = GetNextNodeTowardGoal(ref dstarData, startGrid, gridCostMap.costs);
            float3 nextWaypoint = PathfindingUtils.GridToWorld(nextNode, dstarData.nodeSize);
            
            RefRW<DStarLiteFollower> follower = SystemAPI.GetComponentRW<DStarLiteFollower>(entity);
            follower.ValueRW.pathDataIndex = pathIndex;
            follower.ValueRW.goalNodeIndex = PathfindingUtils.CalculateIndex(goalGrid, dstarData.width);
            follower.ValueRW.targetPosition = targetPos;
            follower.ValueRW.nextWaypoint = nextWaypoint;
            follower.ValueRW.currentNodeIndex = PathfindingUtils.CalculateIndex(startGrid, dstarData.width);
            
            SystemAPI.SetComponentEnabled<DStarLiteFollower>(entity, true);
            
            RefRW<UnitMover> mover = SystemAPI.GetComponentRW<UnitMover>(entity);
            mover.ValueRW.targetPosition = nextWaypoint;
            
            RefRW<PathfindingAgent> agent = SystemAPI.GetComponentRW<PathfindingAgent>(entity);
            agent.ValueRW.currentMode = PathfindingMode.DStarLite;
            agent.ValueRW.isActive = true;
            agent.ValueRW.needsRepath = false;

            SystemAPI.SetComponentEnabled<PathRequest>(entity, false);
        }
    }
    
    private void ComputeDStarLitePath(ref DStarLiteData dstarData, int pathIndex, NativeArray<byte> costMap)
    {
        PathData pathData = dstarData.activePaths[pathIndex];
        int cellCount = dstarData.width * dstarData.height;
        
        for (int i = 0; i < cellCount; i++)
        {
            DStarNode node = dstarData.nodes[i];
            node.g = float.MaxValue;
            node.rhs = float.MaxValue;
            node.inOpenSet = false;
            dstarData.nodes[i] = node;
        }
        
        if (!PathfindingUtils.IsValidPosition(pathData.goalPosition, dstarData.width, dstarData.height))
            return;
        if (!PathfindingUtils.IsValidPosition(pathData.startPosition, dstarData.width, dstarData.height))
            return;
        
        int goalIndex = PathfindingUtils.CalculateIndex(pathData.goalPosition, dstarData.width);
        
        if (costMap[goalIndex] == GlobalGameData.WALL_COST)
            return;
        
        DStarNode goalNode = dstarData.nodes[goalIndex];
        goalNode.rhs = 0;
        goalNode.key = PathfindingUtils.CalculateDStarKey(pathData.goalPosition, pathData.startPosition, goalNode.g, 0, pathData.km);
        goalNode.inOpenSet = true;
        dstarData.nodes[goalIndex] = goalNode;
        
        NativeList<int> openSet = new NativeList<int>(256, Allocator.Temp);
        openSet.Add(goalIndex);
        
        int maxIterations = 10000;
        int iterations = 0;
        
        while (openSet.Length > 0 && iterations++ < maxIterations)
        {
            int bestIdx = 0;
            float2 bestKey = dstarData.nodes[openSet[0]].key;
            
            for (int i = 1; i < openSet.Length; i++)
            {
                float2 key = dstarData.nodes[openSet[i]].key;
                if (PathfindingUtils.KeyLessThan(key, bestKey))
                {
                    bestKey = key;
                    bestIdx = i;
                }
            }
            
            int currentIndex = openSet[bestIdx];
            DStarNode currentNode = dstarData.nodes[currentIndex];
            
            int startIndex = PathfindingUtils.CalculateIndex(pathData.startPosition, dstarData.width);
            DStarNode startNode = dstarData.nodes[startIndex];
            
            float2 startKey = PathfindingUtils.CalculateDStarKey(pathData.startPosition, pathData.startPosition, 
                startNode.g, startNode.rhs, pathData.km);
            
            if (!PathfindingUtils.KeyLessThan(bestKey, startKey) && math.abs(startNode.rhs - startNode.g) < 0.001f)
            {
                break;
            }
            
            openSet.RemoveAtSwapBack(bestIdx);
            currentNode.inOpenSet = false;
            
            if (currentNode.g > currentNode.rhs)
            {
                currentNode.g = currentNode.rhs;
                dstarData.nodes[currentIndex] = currentNode;
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
        
        pathData.needsUpdate = false;
        dstarData.activePaths[pathIndex] = pathData;
    }
    
    private void UpdatePredecessors(ref DStarLiteData dstarData, int nodeIndex, 
        ref NativeList<int> openSet, PathData pathData, NativeArray<byte> costMap)
    {
        int2 pos = dstarData.nodes[nodeIndex].position;
        
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                
                int2 neighborPos = pos + new int2(dx, dy);
                if (!PathfindingUtils.IsValidPosition(neighborPos, dstarData.width, dstarData.height))
                    continue;
                    
                int neighborIndex = PathfindingUtils.CalculateIndex(neighborPos, dstarData.width);
                if (costMap[neighborIndex] == GlobalGameData.WALL_COST)
                    continue;
                    
                UpdateVertex(ref dstarData, neighborIndex, ref openSet, pathData, costMap);
            }
        }
    }
    
    private void UpdateVertex(ref DStarLiteData dstarData, int nodeIndex, 
        ref NativeList<int> openSet, PathData pathData, NativeArray<byte> costMap)
    {
        DStarNode node = dstarData.nodes[nodeIndex];
        int2 pos = node.position;
        int goalIndex = PathfindingUtils.CalculateIndex(pathData.goalPosition, dstarData.width);
        
        if (nodeIndex != goalIndex)
        {
            float minRhs = float.MaxValue;
            
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    
                    int2 neighborPos = pos + new int2(dx, dy);
                    if (!PathfindingUtils.IsValidPosition(neighborPos, dstarData.width, dstarData.height))
                        continue;
                        
                    int neighborIndex = PathfindingUtils.CalculateIndex(neighborPos, dstarData.width);
                    if (costMap[neighborIndex] == GlobalGameData.WALL_COST)
                        continue;
                        
                    DStarNode neighborNode = dstarData.nodes[neighborIndex];
                    float cost = PathfindingUtils.CalculateMoveCost(dx, dy, costMap[neighborIndex]);
                    float candidateRhs = neighborNode.g + cost;
                    
                    if (candidateRhs < minRhs)
                        minRhs = candidateRhs;
                }
            }
            
            node.rhs = minRhs;
        }
        
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
        
        if (math.abs(node.g - node.rhs) > 0.001f)
        {
            node.key = PathfindingUtils.CalculateDStarKey(pos, pathData.startPosition, node.g, node.rhs, pathData.km);
            node.inOpenSet = true;
            openSet.Add(nodeIndex);
        }
        
        dstarData.nodes[nodeIndex] = node;
    }
    
    private int2 GetNextNodeTowardGoal(ref DStarLiteData dstarData, int2 currentPos, NativeArray<byte> costMap)
    {
        float bestScore = float.MaxValue;
        int2 bestNeighbor = currentPos;
        
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                
                int2 neighborPos = currentPos + new int2(dx, dy);
                if (!PathfindingUtils.IsValidPosition(neighborPos, dstarData.width, dstarData.height))
                    continue;
                    
                int neighborIndex = PathfindingUtils.CalculateIndex(neighborPos, dstarData.width);
                if (costMap[neighborIndex] == GlobalGameData.WALL_COST)
                    continue;
                    
                DStarNode neighborNode = dstarData.nodes[neighborIndex];
                
                if (neighborNode.g >= float.MaxValue * 0.5f)
                    continue;
                
                float cost = PathfindingUtils.CalculateMoveCost(dx, dy, costMap[neighborIndex]);
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
    
    private int FindOrAllocatePathSlot(ref DStarLiteData dstarData, Entity entity)
    {
        for (int i = 0; i < MAX_ACTIVE_PATHS; i++)
        {
            if (dstarData.activePaths[i].owner == entity)
                return i;
        }
        
        for (int i = 0; i < MAX_ACTIVE_PATHS; i++)
        {
            if (!dstarData.activePaths[i].isValid)
                return i;
        }
        
        int slot = dstarData.nextPathIndex;
        dstarData.nextPathIndex = (dstarData.nextPathIndex + 1) % MAX_ACTIVE_PATHS;
        return slot;
    }
    
    // ============================================
    // PARALLEL JOBS
    // ============================================
    
    [BurstCompile]
    [WithAll(typeof(PathRequest))]
    public partial struct CollectPathRequestsJob : IJobEntity
    {
        public NativeList<PathComputeRequest>.ParallelWriter pendingRequests;

        public void Execute(
            in PathRequest request,
            in LocalTransform transform,
            Entity entity)
        {
            // Only handle DStarLite requests; FlowField requests are handled by FlowFieldSystem.
            if (request.requestedMode != PathfindingMode.DStarLite)
                return;

            pendingRequests.AddNoResize(new PathComputeRequest
            {
                entity = entity,
                currentPosition = transform.Position,
                targetPosition = request.targetPosition
            });
        }
    }
    
    [BurstCompile]
    [WithAll(typeof(DStarLiteFollower))]
    public partial struct UpdateFollowersJob : IJobEntity
    {
        public float nodeSize;
        public int width;
        public int height;
        
        [ReadOnly] public NativeArray<DStarNode> nodes;
        [ReadOnly] public NativeArray<PathData> activePaths;
        [ReadOnly] public NativeArray<byte> costs;
        
        public EntityCommandBuffer.ParallelWriter ecb;

        public void Execute(
            ref DStarLiteFollower follower,
            ref UnitMover mover,
            in LocalTransform transform,
            in PathfindingAgent agent,
            Entity entity,
            [EntityIndexInQuery] int sortKey)
        {
            if (follower.pathDataIndex < 0 || follower.pathDataIndex >= MAX_ACTIVE_PATHS)
                return;
            
            PathData pathData = activePaths[follower.pathDataIndex];
            if (!pathData.isValid || pathData.owner != entity)
                return;
            
            float3 currentPos = transform.Position;
            float3 nextWP = follower.nextWaypoint;
            
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
                
                int2 currentGrid = PathfindingUtils.WorldToGrid(currentPos, nodeSize);
                int2 nextNode = GetNextNodeTowardGoalParallel(currentGrid);
                
                float3 newWaypoint = PathfindingUtils.GridToWorld(nextNode, nodeSize);
                follower.nextWaypoint = newWaypoint;
                follower.currentNodeIndex = PathfindingUtils.CalculateIndex(currentGrid, width);
                
                mover.targetPosition = newWaypoint;
            }
            else
            {
                mover.targetPosition = nextWP;
            }
            
            float3 toWaypoint = follower.nextWaypoint - currentPos;
            if (math.lengthsq(toWaypoint) > 0.001f)
            {
                follower.lastMoveDirection = math.normalize(toWaypoint);
            }
        } 
        
        private int2 GetNextNodeTowardGoalParallel(int2 currentPos)
        {
            float bestScore = float.MaxValue;
            int2 bestNeighbor = currentPos;
            
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    
                    int2 neighborPos = currentPos + new int2(dx, dy);
                    if (!PathfindingUtils.IsValidPosition(neighborPos, width, height))
                        continue;
                        
                    int neighborIndex = PathfindingUtils.CalculateIndex(neighborPos, width);
                    if (costs[neighborIndex] == GlobalGameData.WALL_COST)
                        continue;
                        
                    DStarNode neighborNode = nodes[neighborIndex];
                    
                    if (neighborNode.g >= float.MaxValue * 0.5f)
                        continue;
                    
                    float cost = PathfindingUtils.CalculateMoveCost(dx, dy, costs[neighborIndex]);
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
    }
}
