using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics;

/// <summary>
/// Shared grid infrastructure used by both FlowFieldSystem and DStarLiteSystem.
/// Manages the cost map, layers, and provides utility methods for grid operations.
/// </summary>
[UpdateInGroup(typeof(MovementCoordinatorSystemGroup))]
public partial struct GridSystem : ISystem
{
    // Cost constants - shared across all pathfinding
    public const byte WALL_COST = byte.MaxValue;
    public const byte HEAVY_COST = 50;
    public const byte DEFAULT_COST = 1;
    public const byte STAIR_COST = 2; // Slightly higher cost for stairs
    
    /// <summary>
    /// Core grid configuration - shared by all pathfinding systems.
    /// </summary>
    public struct GridConfig : IComponentData
    {
        public int width;
        public int height;
        public int layerCount;
        public float cellSize;
        public float layerHeight; // Vertical distance between layers
    }
    
    /// <summary>
    /// Shared cost map data for all layers.
    /// Layout: [layerIndex * (width * height) + cellIndex]
    /// </summary>
    public struct GridCostMap : IComponentData
    {
        public NativeArray<byte> costs;
    }
    
    /// <summary>
    /// Stair/portal connections between layers.
    /// </summary>
    public struct StairConnection : IBufferElementData
    {
        public int2 gridPosition;
        public int fromLayer;
        public int toLayer;
        public float3 entryWorldPosition;
        public float3 exitWorldPosition;
        public bool bidirectional;
    }
    
    private bool costMapDirty;
    private int lastPhysicsVersion;
    
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        // Default grid configuration
        int width = 100;
        int height = 100;
        int layerCount = 1;
        float cellSize = 2f;
        float layerHeight = 3f;
        
        int cellsPerLayer = width * height;
        int totalCells = cellsPerLayer * layerCount;
        
        // Create grid config
        state.EntityManager.AddComponent<GridConfig>(state.SystemHandle);
        state.EntityManager.SetComponentData(state.SystemHandle, new GridConfig
        {
            width = width,
            height = height,
            layerCount = layerCount,
            cellSize = cellSize,
            layerHeight = layerHeight
        });
        
        // Create cost map
        var costMap = new GridCostMap
        {
            costs = new NativeArray<byte>(totalCells, Allocator.Persistent)
        };
        
        // Initialize to default walkable
        for (int i = 0; i < totalCells; i++)
        {
            costMap.costs[i] = DEFAULT_COST;
        }
        
        state.EntityManager.AddComponent<GridCostMap>(state.SystemHandle);
        state.EntityManager.SetComponentData(state.SystemHandle, costMap);
        
        // Create stair connections buffer on a separate entity
        var stairEntity = state.EntityManager.CreateEntity();
        state.EntityManager.AddBuffer<StairConnection>(stairEntity);
        
        costMapDirty = true;
        lastPhysicsVersion = 0;
    }
    
    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        if (SystemAPI.HasComponent<GridCostMap>(state.SystemHandle))
        {
            var costMap = SystemAPI.GetComponent<GridCostMap>(state.SystemHandle);
            if (costMap.costs.IsCreated) costMap.costs.Dispose();
        }
    }
    
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var gridConfig = SystemAPI.GetComponent<GridConfig>(state.SystemHandle);
        var gridCostMap = SystemAPI.GetComponent<GridCostMap>(state.SystemHandle);
        
        // Check if physics world changed
        if (SystemAPI.HasSingleton<PhysicsWorldSingleton>())
        {
            var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
            int currentPhysicsVersion = physicsWorld.PhysicsWorld.NumBodies;
            
            if (currentPhysicsVersion != lastPhysicsVersion)
            {
                costMapDirty = true;
                lastPhysicsVersion = currentPhysicsVersion;
            }
            
            // Update cost map from physics
            if (costMapDirty)
            {
                costMapDirty = false;
                
                int cellsPerLayer = gridConfig.width * gridConfig.height;
                
                var updateCostJob = new UpdateCostMapJob
                {
                    width = gridConfig.width,
                    cellSize = gridConfig.cellSize,
                    cellSizeHalf = gridConfig.cellSize * 0.5f,
                    layerCount = gridConfig.layerCount,
                    layerHeight = gridConfig.layerHeight,
                    cellsPerLayer = cellsPerLayer,
                    collisionWorld = physicsWorld.CollisionWorld,
                    costs = gridCostMap.costs,
                    wallFilter = new CollisionFilter
                    {
                        BelongsTo = ~0u,
                        CollidesWith = 1u << GlobalGameData.WALLS_LAYER,
                        GroupIndex = 0
                    },
                    heavyFilter = new CollisionFilter
                    {
                        BelongsTo = ~0u,
                        CollidesWith = 1u << GlobalGameData.PATHFINDING_HEAVY_LAYER,
                        GroupIndex = 0
                    }
                };
                
                state.Dependency = updateCostJob.Schedule(
                    gridConfig.layerCount * cellsPerLayer, 64, state.Dependency);
                
                // Complete the job immediately so downstream systems can safely read costs
                state.CompleteDependency();
            }
        }
    }
    
    // ============================================
    // STATIC UTILITY METHODS
    // ============================================
    
    /// <summary>Calculate flat index from 2D position within a layer.</summary>
    public static int CalculateIndex(int x, int y, int width) => x + y * width;
    public static int CalculateIndex(int2 pos, int width) => pos.x + pos.y * width;
    
    /// <summary>Calculate flat index from 3D position (x, y, layer).</summary>
    public static int CalculateIndex(int x, int y, int layer, int width, int height)
        => layer * (width * height) + x + y * width;
    
    public static int CalculateIndex(int2 pos, int layer, int width, int height)
        => layer * (width * height) + pos.x + pos.y * width;
    
    /// <summary>Get 2D grid position from flat index.</summary>
    public static int2 GetGridPositionFromIndex(int index, int width)
    {
        int y = index / width;
        int x = index % width;
        return new int2(x, y);
    }
    
    /// <summary>Get layer and 2D position from flat index.</summary>
    public static void GetPositionFromIndex(int index, int width, int height, out int2 pos, out int layer)
    {
        int cellsPerLayer = width * height;
        layer = index / cellsPerLayer;
        int localIndex = index % cellsPerLayer;
        pos = new int2(localIndex % width, localIndex / width);
    }
    
    /// <summary>Convert world position to grid position (XZ plane).</summary>
    public static int2 GetGridPosition(float3 worldPosition, float cellSize)
    {
        return new int2(
            (int)math.floor(worldPosition.x / cellSize),
            (int)math.floor(worldPosition.z / cellSize)
        );
    }
    
    /// <summary>Get layer index from world Y position.</summary>
    public static int GetLayer(float3 worldPosition, float layerHeight)
    {
        return math.max(0, (int)math.floor(worldPosition.y / layerHeight));
    }
    
    /// <summary>Get both grid position and layer from world position.</summary>
    public static void GetGridPositionAndLayer(float3 worldPosition, float cellSize, float layerHeight, 
        out int2 gridPos, out int layer)
    {
        gridPos = GetGridPosition(worldPosition, cellSize);
        layer = GetLayer(worldPosition, layerHeight);
    }
    
    /// <summary>Convert grid position to world position (corner).</summary>
    public static float3 GetWorldPosition(int x, int y, int layer, float cellSize, float layerHeight)
    {
        return new float3(
            x * cellSize,
            layer * layerHeight,
            y * cellSize
        );
    }
    
    /// <summary>Convert grid position to world position (center of cell).</summary>
    public static float3 GetWorldCenterPosition(int x, int y, int layer, float cellSize, float layerHeight)
    {
        return new float3(
            x * cellSize + cellSize * 0.5f,
            layer * layerHeight,
            y * cellSize + cellSize * 0.5f
        );
    }
    
    /// <summary>Convert grid position to world position (center, single layer).</summary>
    public static float3 GetWorldCenterPosition(int x, int y, float cellSize)
    {
        return new float3(
            x * cellSize + cellSize * 0.5f,
            0f,
            y * cellSize + cellSize * 0.5f
        );
    }
    
    /// <summary>Check if grid position is within bounds.</summary>
    public static bool IsValidGridPosition(int2 pos, int width, int height)
    {
        return pos.x >= 0 && pos.y >= 0 && pos.x < width && pos.y < height;
    }
    
    /// <summary>Check if grid position and layer are within bounds.</summary>
    public static bool IsValidGridPosition(int2 pos, int layer, int width, int height, int layerCount)
    {
        return pos.x >= 0 && pos.y >= 0 && pos.x < width && pos.y < height 
            && layer >= 0 && layer < layerCount;
    }
    
    /// <summary>Convert 2D flow vector to 3D world movement vector.</summary>
    public static float3 GetWorldMovementVector(float2 vector) => new float3(vector.x, 0f, vector.y);
    
    /// <summary>Check if a cell is a wall.</summary>
    public static bool IsWall(int index, NativeArray<byte> costs)
    {
        return costs[index] == WALL_COST;
    }
    
    public static bool IsWall(int2 pos, int width, NativeArray<byte> costs)
    {
        return costs[CalculateIndex(pos, width)] == WALL_COST;
    }
    
    public static bool IsWall(int2 pos, int layer, int width, int height, NativeArray<byte> costs)
    {
        return costs[CalculateIndex(pos, layer, width, height)] == WALL_COST;
    }
    
    /// <summary>Check if world position is walkable.</summary>
    public static bool IsWalkable(float3 worldPosition, GridConfig config, NativeArray<byte> costs)
    {
        int2 gridPos = GetGridPosition(worldPosition, config.cellSize);
        int layer = GetLayer(worldPosition, config.layerHeight);
        
        if (!IsValidGridPosition(gridPos, layer, config.width, config.height, config.layerCount))
            return false;
            
        int index = CalculateIndex(gridPos, layer, config.width, config.height);
        return costs[index] != WALL_COST;
    }
    
    /// <summary>Get movement cost between two adjacent cells.</summary>
    public static float GetMovementCost(int dx, int dy, byte cellCost)
    {
        if (cellCost == WALL_COST) return float.MaxValue;
        
        // Diagonal movement costs more
        float baseCost = (dx != 0 && dy != 0) ? 1.414f : 1f;
        return baseCost * cellCost;
    }
    
    /// <summary>Octile distance heuristic for 8-directional movement.</summary>
    public static float OctileDistance(int2 a, int2 b)
    {
        int dx = math.abs(a.x - b.x);
        int dy = math.abs(a.y - b.y);
        return math.max(dx, dy) + 0.414f * math.min(dx, dy);
    }
    
    /// <summary>Manhattan distance heuristic for 4-directional movement.</summary>
    public static float ManhattanDistance(int2 a, int2 b)
    {
        return math.abs(a.x - b.x) + math.abs(a.y - b.y);
    }
}

/// <summary>
/// Updates cost map based on physics world (walls, obstacles).
/// </summary>
[BurstCompile]
public struct UpdateCostMapJob : IJobParallelFor
{
    [ReadOnly] public int width;
    [ReadOnly] public float cellSize;
    [ReadOnly] public float cellSizeHalf;
    [ReadOnly] public int layerCount;
    [ReadOnly] public float layerHeight;
    [ReadOnly] public int cellsPerLayer;
    [ReadOnly] public CollisionWorld collisionWorld;
    [ReadOnly] public CollisionFilter wallFilter;
    [ReadOnly] public CollisionFilter heavyFilter;
    
    [NativeDisableParallelForRestriction]
    public NativeArray<byte> costs;
    
    public void Execute(int index)
    {
        int layer = index / cellsPerLayer;
        int localIndex = index % cellsPerLayer;
        int x = localIndex % width;
        int y = localIndex / width;
        
        float3 worldPos = new float3(
            x * cellSize + cellSizeHalf,
            layer * layerHeight + 0.5f, // Slightly above ground
            y * cellSize + cellSizeHalf
        );
        
        // Check for walls
        var wallHits = new NativeList<DistanceHit>(Allocator.Temp);
        if (collisionWorld.OverlapSphere(worldPos, cellSizeHalf * 0.9f, ref wallHits, wallFilter))
        {
            costs[index] = GridSystem.WALL_COST;
            wallHits.Dispose();
            return;
        }
        wallHits.Dispose();
        
        // Check for heavy/difficult terrain
        var heavyHits = new NativeList<DistanceHit>(Allocator.Temp);
        if (collisionWorld.OverlapSphere(worldPos, cellSizeHalf * 0.9f, ref heavyHits, heavyFilter))
        {
            costs[index] = GridSystem.HEAVY_COST;
            heavyHits.Dispose();
            return;
        }
        heavyHits.Dispose();
        
        // Default walkable
        costs[index] = GridSystem.DEFAULT_COST;
    }
}