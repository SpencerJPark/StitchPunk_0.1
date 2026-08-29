using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace DotsMovementToolkit
{
[UpdateInGroup(typeof(MovementRoutingSystemGroup))]
public partial struct DStarLiteSystem : ISystem
{
    public const int MAX_ACTIVE_PATHS   = 256;
    public const int MAX_NODES_PER_PATH = 512;

    public struct DStarLiteData : IComponentData
    {
        public int width;
        public int height;
        public float nodeSize;
        public NativeArray<DStarNode> nodes;
        public NativeArray<PathData>  activePaths;
        public int nextPathIndex;
    }

    public struct DStarNode
    {
        public float g;
        public float rhs;
        public float2 key;
        public int2   position;
        public bool   inOpenSet;
    }

    public struct PathData
    {
        public int2   startPosition;
        public int2   goalPosition;
        public Entity owner;
        public bool   isValid;
        public bool   needsUpdate;
        public float  km;
    }

    private bool isInitialized;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<NavGridConfig>();
        state.RequireForUpdate<NavGridCostMap>();
        state.RequireForUpdate<NavGridSettings>();
        isInitialized = false;
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        if (SystemAPI.HasComponent<DStarLiteData>(state.SystemHandle))
        {
            DStarLiteData data = SystemAPI.GetComponent<DStarLiteData>(state.SystemHandle);
            if (data.nodes.IsCreated)       data.nodes.Dispose();
            if (data.activePaths.IsCreated) data.activePaths.Dispose();
        }
    }

    public void OnUpdate(ref SystemState state)
    {
        if (!isInitialized)
        {
            InitializeFromNavGrid(ref state);
            isInitialized = true;
        }

        if (!SystemAPI.HasComponent<DStarLiteData>(state.SystemHandle))
            return;

        DStarLiteData dstarData = SystemAPI.GetComponent<DStarLiteData>(state.SystemHandle);
        NavGridCostMap gridCostMap = SystemAPI.GetSingleton<NavGridCostMap>();
        byte wallCost = SystemAPI.GetSingleton<NavGridSettings>().wallCost;

        // PathRequestSystem already set agent state and left PathRequest enabled for us.
        // Iterate inline — no separate gather job needed.
        foreach (var (pathRequest, transform, pathRequestEnabled, entity) in
            SystemAPI.Query<
                RefRO<PathRequest>,
                RefRO<LocalTransform>,
                EnabledRefRW<PathRequest>>()
                .WithEntityAccess())
        {
            if (pathRequest.ValueRO.requestedMode != PathfindingMode.DStarLite)
                continue;

            ProcessRequest(ref state, ref dstarData, gridCostMap, wallCost, entity,
                transform.ValueRO.Position, pathRequest.ValueRO.targetPosition);

            pathRequestEnabled.ValueRW = false;
        }

        SystemAPI.SetComponent(state.SystemHandle, dstarData);
    }

    private void ProcessRequest(
        ref SystemState state,
        ref DStarLiteData dstarData,
        NavGridCostMap gridCostMap,
        byte wallCost,
        Entity entity,
        float3 currentPos,
        float3 targetPos)
    {
        if (!SystemAPI.HasComponent<DStarLiteFollower>(entity)) return;
        if (!SystemAPI.HasComponent<Movement>(entity))           return;
        if (!SystemAPI.HasComponent<PathfindingAgent>(entity))   return;

        int2 startGrid = PathfindingUtils.WorldToGrid(currentPos, dstarData.nodeSize);
        int2 goalGrid  = PathfindingUtils.WorldToGrid(targetPos,  dstarData.nodeSize);

        int pathIndex = FindOrAllocatePathSlot(ref dstarData, entity);
        if (pathIndex < 0) return;

        PathData pathData = dstarData.activePaths[pathIndex];
        pathData.startPosition = startGrid;
        pathData.goalPosition  = goalGrid;
        pathData.owner         = entity;
        pathData.isValid       = true;
        pathData.needsUpdate   = true;
        pathData.km            = 0;
        dstarData.activePaths[pathIndex] = pathData;

        ComputeDStarLitePath(ref dstarData, pathIndex, gridCostMap.costs, wallCost);

        int2  nextNode    = GetNextNodeTowardGoal(ref dstarData, startGrid, gridCostMap.costs, wallCost);
        float3 nextWaypoint = PathfindingUtils.GridToWorld(nextNode, dstarData.nodeSize);

        RefRW<DStarLiteFollower> follower = SystemAPI.GetComponentRW<DStarLiteFollower>(entity);
        follower.ValueRW.pathDataIndex    = pathIndex;
        follower.ValueRW.goalNodeIndex    = PathfindingUtils.CalculateIndex(goalGrid, dstarData.width);
        follower.ValueRW.targetPosition   = targetPos;
        follower.ValueRW.nextWaypoint     = nextWaypoint;
        follower.ValueRW.currentNodeIndex = PathfindingUtils.CalculateIndex(startGrid, dstarData.width);

        SystemAPI.SetComponentEnabled<DStarLiteFollower>(entity, true);

        RefRW<Movement> mover = SystemAPI.GetComponentRW<Movement>(entity);
        mover.ValueRW.targetPosition = nextWaypoint;
    }

    private void InitializeFromNavGrid(ref SystemState state)
    {
        NavGridConfig gridConfig = SystemAPI.GetSingleton<NavGridConfig>();
        int cellCount = gridConfig.width * gridConfig.height;

        DStarLiteData dstarData = new DStarLiteData
        {
            width        = gridConfig.width,
            height       = gridConfig.height,
            nodeSize     = gridConfig.cellSize,
            nodes        = new NativeArray<DStarNode>(cellCount, Allocator.Persistent),
            activePaths  = new NativeArray<PathData>(MAX_ACTIVE_PATHS, Allocator.Persistent),
            nextPathIndex = 0
        };

        for (int i = 0; i < cellCount; i++)
        {
            int x = i % dstarData.width;
            int y = i / dstarData.width;
            dstarData.nodes[i] = new DStarNode
            {
                g         = float.MaxValue,
                rhs       = float.MaxValue,
                key       = new float2(float.MaxValue, float.MaxValue),
                position  = new int2(x, y),
                inOpenSet = false
            };
        }

        state.EntityManager.AddComponent<DStarLiteData>(state.SystemHandle);
        state.EntityManager.SetComponentData(state.SystemHandle, dstarData);
    }

    private void ComputeDStarLitePath(ref DStarLiteData dstarData, int pathIndex, NativeArray<byte> costMap, byte wallCost)
    {
        PathData pathData = dstarData.activePaths[pathIndex];
        int cellCount = dstarData.width * dstarData.height;

        for (int i = 0; i < cellCount; i++)
        {
            DStarNode node = dstarData.nodes[i];
            node.g        = float.MaxValue;
            node.rhs      = float.MaxValue;
            node.inOpenSet = false;
            dstarData.nodes[i] = node;
        }

        if (!PathfindingUtils.IsValidPosition(pathData.goalPosition,  dstarData.width, dstarData.height)) return;
        if (!PathfindingUtils.IsValidPosition(pathData.startPosition, dstarData.width, dstarData.height)) return;

        int goalIndex = PathfindingUtils.CalculateIndex(pathData.goalPosition, dstarData.width);
        if (costMap[goalIndex] == wallCost) return;

        DStarNode goalNode = dstarData.nodes[goalIndex];
        goalNode.rhs      = 0;
        goalNode.key      = PathfindingUtils.CalculateDStarKey(pathData.goalPosition, pathData.startPosition, goalNode.g, 0, pathData.km);
        goalNode.inOpenSet = true;
        dstarData.nodes[goalIndex] = goalNode;

        NativeList<int> openSet = new NativeList<int>(256, Allocator.Temp);
        openSet.Add(goalIndex);

        int maxIterations = 10000;
        int iterations    = 0;

        while (openSet.Length > 0 && iterations++ < maxIterations)
        {
            int    bestIdx = 0;
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

            int        currentIndex = openSet[bestIdx];
            DStarNode  currentNode  = dstarData.nodes[currentIndex];

            int       startIndex = PathfindingUtils.CalculateIndex(pathData.startPosition, dstarData.width);
            DStarNode startNode  = dstarData.nodes[startIndex];

            float2 startKey = PathfindingUtils.CalculateDStarKey(pathData.startPosition, pathData.startPosition,
                startNode.g, startNode.rhs, pathData.km);

            if (!PathfindingUtils.KeyLessThan(bestKey, startKey) && math.abs(startNode.rhs - startNode.g) < 0.001f)
                break;

            openSet.RemoveAtSwapBack(bestIdx);
            currentNode.inOpenSet = false;

            if (currentNode.g > currentNode.rhs)
            {
                currentNode.g = currentNode.rhs;
                dstarData.nodes[currentIndex] = currentNode;
                UpdatePredecessors(ref dstarData, currentIndex, ref openSet, pathData, costMap, wallCost);
            }
            else
            {
                currentNode.g = float.MaxValue;
                dstarData.nodes[currentIndex] = currentNode;
                UpdateVertex(ref dstarData, currentIndex, ref openSet, pathData, costMap, wallCost);
                UpdatePredecessors(ref dstarData, currentIndex, ref openSet, pathData, costMap, wallCost);
            }
        }

        openSet.Dispose();

        pathData.needsUpdate = false;
        dstarData.activePaths[pathIndex] = pathData;
    }

    private void UpdatePredecessors(
        ref DStarLiteData dstarData, int nodeIndex,
        ref NativeList<int> openSet, PathData pathData, NativeArray<byte> costMap, byte wallCost)
    {
        int2 pos = dstarData.nodes[nodeIndex].position;

        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;

                int2 neighborPos = pos + new int2(dx, dy);
                if (!PathfindingUtils.IsValidPosition(neighborPos, dstarData.width, dstarData.height)) continue;

                int neighborIndex = PathfindingUtils.CalculateIndex(neighborPos, dstarData.width);
                if (costMap[neighborIndex] == wallCost) continue;

                UpdateVertex(ref dstarData, neighborIndex, ref openSet, pathData, costMap, wallCost);
            }
        }
    }

    private void UpdateVertex(
        ref DStarLiteData dstarData, int nodeIndex,
        ref NativeList<int> openSet, PathData pathData, NativeArray<byte> costMap, byte wallCost)
    {
        DStarNode node     = dstarData.nodes[nodeIndex];
        int2      pos      = node.position;
        int       goalIndex = PathfindingUtils.CalculateIndex(pathData.goalPosition, dstarData.width);

        if (nodeIndex != goalIndex)
        {
            float minRhs = float.MaxValue;

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;

                    int2 neighborPos = pos + new int2(dx, dy);
                    if (!PathfindingUtils.IsValidPosition(neighborPos, dstarData.width, dstarData.height)) continue;

                    int neighborIndex = PathfindingUtils.CalculateIndex(neighborPos, dstarData.width);
                    if (costMap[neighborIndex] == wallCost) continue;

                    DStarNode neighborNode = dstarData.nodes[neighborIndex];
                    float cost             = PathfindingUtils.CalculateMoveCost(dx, dy, costMap[neighborIndex]);
                    float candidateRhs     = neighborNode.g + cost;

                    if (candidateRhs < minRhs) minRhs = candidateRhs;
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
            node.key       = PathfindingUtils.CalculateDStarKey(pos, pathData.startPosition, node.g, node.rhs, pathData.km);
            node.inOpenSet = true;
            openSet.Add(nodeIndex);
        }

        dstarData.nodes[nodeIndex] = node;
    }

    private int2 GetNextNodeTowardGoal(ref DStarLiteData dstarData, int2 currentPos, NativeArray<byte> costMap, byte wallCost)
    {
        float bestScore    = float.MaxValue;
        int2  bestNeighbor = currentPos;

        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;

                int2 neighborPos = currentPos + new int2(dx, dy);
                if (!PathfindingUtils.IsValidPosition(neighborPos, dstarData.width, dstarData.height)) continue;

                int neighborIndex = PathfindingUtils.CalculateIndex(neighborPos, dstarData.width);
                if (costMap[neighborIndex] == wallCost) continue;

                DStarNode neighborNode = dstarData.nodes[neighborIndex];
                if (neighborNode.g >= float.MaxValue * 0.5f) continue;

                float cost  = PathfindingUtils.CalculateMoveCost(dx, dy, costMap[neighborIndex]);
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

    private int FindOrAllocatePathSlot(ref DStarLiteData dstarData, Entity entity)
    {
        for (int i = 0; i < MAX_ACTIVE_PATHS; i++)
        {
            if (dstarData.activePaths[i].owner == entity) return i;
        }

        for (int i = 0; i < MAX_ACTIVE_PATHS; i++)
        {
            if (!dstarData.activePaths[i].isValid) return i;
        }

        int slot = dstarData.nextPathIndex;
        dstarData.nextPathIndex = (dstarData.nextPathIndex + 1) % MAX_ACTIVE_PATHS;
        return slot;
    }
}
}
