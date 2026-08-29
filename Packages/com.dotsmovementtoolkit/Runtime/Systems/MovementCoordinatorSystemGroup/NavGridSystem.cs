using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics;

namespace DotsMovementToolkit
{
/// <summary>
/// Owns the navigation grid: builds NavGridConfig/NavGridCostMap from the baked NavGridSettings,
/// then rebuilds the cost map from physics whenever the collision world changes. Also the home of
/// the package's grid math (world↔cell, index, bounds) that FlowFieldSystem and DStarLiteSystem share.
/// </summary>
[UpdateInGroup(typeof(MovementCoordinatorSystemGroup), OrderFirst = true)]
public partial struct NavGridSystem : ISystem
{
    private bool isInitialized;
    private bool costMapDirty;
    private int lastPhysicsVersion;
    private byte wallCost;
    private byte heavyCost;
    private byte defaultCost;
    private uint wallLayerMask;
    private uint heavyLayerMask;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<NavGridSettings>();
        isInitialized = false;
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        if (SystemAPI.HasComponent<NavGridCostMap>(state.SystemHandle))
        {
            var costMap = SystemAPI.GetComponent<NavGridCostMap>(state.SystemHandle);
            if (costMap.costs.IsCreated) costMap.costs.Dispose();
        }
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!isInitialized)
        {
            InitializeFromSettings(ref state, SystemAPI.GetSingleton<NavGridSettings>());
            isInitialized = true;
        }

        var gridConfig = SystemAPI.GetComponent<NavGridConfig>(state.SystemHandle);
        var gridCostMap = SystemAPI.GetComponent<NavGridCostMap>(state.SystemHandle);

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

                var updateCostJob = new UpdateNavGridCostMapJob
                {
                    width = gridConfig.width,
                    cellSize = gridConfig.cellSize,
                    cellSizeHalf = gridConfig.cellSize * 0.5f,
                    gridOrigin = gridConfig.gridOrigin,
                    layerCount = gridConfig.layerCount,
                    layerHeight = gridConfig.layerHeight,
                    cellsPerLayer = cellsPerLayer,
                    collisionWorld = physicsWorld.CollisionWorld,
                    costs = gridCostMap.costs,
                    wallCost = wallCost,
                    heavyCost = heavyCost,
                    defaultCost = defaultCost,
                    wallFilter = new CollisionFilter
                    {
                        BelongsTo = ~0u,
                        CollidesWith = wallLayerMask,
                        GroupIndex = 0
                    },
                    heavyFilter = new CollisionFilter
                    {
                        BelongsTo = ~0u,
                        CollidesWith = heavyLayerMask,
                        GroupIndex = 0
                    }
                };

                state.Dependency = updateCostJob.Schedule(
                    gridConfig.layerCount * cellsPerLayer, 64, state.Dependency);

                // Complete the job immediately so downstream systems can safely read costs
                state.CompleteDependency();

                gridCostMap.costMapVersion++;
                SystemAPI.SetComponent(state.SystemHandle, gridCostMap);
            }
        }
    }

    // One-time setup, run on the first OnUpdate — NavGridSettings is baked into a
    // subscene, so it does not exist yet when OnCreate runs at world creation.
    private void InitializeFromSettings(ref SystemState state, NavGridSettings settings)
    {
        wallCost = settings.wallCost;
        heavyCost = settings.heavyCost;
        defaultCost = settings.defaultCost;
        wallLayerMask = settings.wallLayerMask;
        heavyLayerMask = settings.heavyLayerMask;

        int cellsPerLayer = settings.width * settings.height;
        int totalCells = cellsPerLayer * settings.layerCount;

        state.EntityManager.AddComponent<NavGridConfig>(state.SystemHandle);
        state.EntityManager.SetComponentData(state.SystemHandle, new NavGridConfig
        {
            width = settings.width,
            height = settings.height,
            layerCount = settings.layerCount,
            cellSize = settings.cellSize,
            layerHeight = settings.layerHeight,
            gridOrigin = settings.gridOrigin
        });

        var costMap = new NavGridCostMap
        {
            costs = new NativeArray<byte>(totalCells, Allocator.Persistent)
        };

        // Initialize to default walkable
        for (int i = 0; i < totalCells; i++)
        {
            costMap.costs[i] = settings.defaultCost;
        }

        state.EntityManager.AddComponent<NavGridCostMap>(state.SystemHandle);
        state.EntityManager.SetComponentData(state.SystemHandle, costMap);

        // Create stair connections buffer on a separate entity
        var stairEntity = state.EntityManager.CreateEntity();
        state.EntityManager.AddBuffer<NavGridStairConnection>(stairEntity);

        costMapDirty = true;
        lastPhysicsVersion = 0;
    }
    
    // ============================================
    // STATIC UTILITY METHODS
    // ============================================
    
    /// <summary>Calculate flat index from 2D position within a layer. Delegates to PathfindingUtils — the two classes'
    /// non-layer-aware grid math was duplicated before the movement toolkit extraction merged it.</summary>
    public static int CalculateIndex(int x, int y, int width) => PathfindingUtils.CalculateIndex(x, y, width);
    public static int CalculateIndex(int2 pos, int width) => PathfindingUtils.CalculateIndex(pos, width);

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
    
    // Every world<->grid conversion below takes gridOrigin (NavGridConfig.gridOrigin, the world
    // position of cell (0,0)'s corner). The NavGridConfig overloads are the ones to reach for;
    // the loose-parameter versions exist for jobs that carry cellSize/gridOrigin as fields.

    /// <summary>Convert world position to grid position (XZ plane). Delegates to PathfindingUtils.WorldToGrid.</summary>
    public static int2 GetGridPosition(float3 worldPosition, float cellSize, float3 gridOrigin)
        => PathfindingUtils.WorldToGrid(worldPosition, cellSize, gridOrigin);

    public static int2 GetGridPosition(float3 worldPosition, in NavGridConfig config)
        => PathfindingUtils.WorldToGrid(worldPosition, config.cellSize, config.gridOrigin);

    /// <summary>Get layer index from world Y position.</summary>
    public static int GetLayer(float3 worldPosition, float layerHeight, float3 gridOrigin)
    {
        return math.max(0, (int)math.floor((worldPosition.y - gridOrigin.y) / layerHeight));
    }

    public static int GetLayer(float3 worldPosition, in NavGridConfig config)
        => GetLayer(worldPosition, config.layerHeight, config.gridOrigin);

    /// <summary>Get both grid position and layer from world position.</summary>
    public static void GetGridPositionAndLayer(float3 worldPosition, in NavGridConfig config,
        out int2 gridPos, out int layer)
    {
        gridPos = GetGridPosition(worldPosition, config);
        layer = GetLayer(worldPosition, config);
    }
    
    /// <summary>Convert grid position to world position (corner).</summary>
    public static float3 GetWorldPosition(int x, int y, int layer, float cellSize, float layerHeight, float3 gridOrigin)
    {
        return new float3(
            gridOrigin.x + x * cellSize,
            gridOrigin.y + layer * layerHeight,
            gridOrigin.z + y * cellSize
        );
    }

    public static float3 GetWorldPosition(int x, int y, int layer, in NavGridConfig config)
        => GetWorldPosition(x, y, layer, config.cellSize, config.layerHeight, config.gridOrigin);

    /// <summary>Convert grid position to world position (center of cell).</summary>
    public static float3 GetWorldCenterPosition(int x, int y, int layer, float cellSize, float layerHeight, float3 gridOrigin)
    {
        return new float3(
            gridOrigin.x + x * cellSize + cellSize * 0.5f,
            gridOrigin.y + layer * layerHeight,
            gridOrigin.z + y * cellSize + cellSize * 0.5f
        );
    }

    public static float3 GetWorldCenterPosition(int x, int y, int layer, in NavGridConfig config)
        => GetWorldCenterPosition(x, y, layer, config.cellSize, config.layerHeight, config.gridOrigin);

    /// <summary>Convert grid position to world position (center, layer 0).</summary>
    public static float3 GetWorldCenterPosition(int x, int y, float cellSize, float3 gridOrigin)
    {
        return new float3(
            gridOrigin.x + x * cellSize + cellSize * 0.5f,
            gridOrigin.y,
            gridOrigin.z + y * cellSize + cellSize * 0.5f
        );
    }
    
    /// <summary>Check if grid position is within bounds. Delegates to PathfindingUtils.IsValidPosition.</summary>
    public static bool IsValidGridPosition(int2 pos, int width, int height)
        => PathfindingUtils.IsValidPosition(pos, width, height);
    
    /// <summary>Check if grid position and layer are within bounds.</summary>
    public static bool IsValidGridPosition(int2 pos, int layer, int width, int height, int layerCount)
    {
        return pos.x >= 0 && pos.y >= 0 && pos.x < width && pos.y < height 
            && layer >= 0 && layer < layerCount;
    }
    
    /// <summary>Convert 2D flow vector to 3D world movement vector.</summary>
    public static float3 GetWorldMovementVector(float2 vector) => new float3(vector.x, 0f, vector.y);
    
    /// <summary>Check if a cell is a wall.</summary>
    public static bool IsWall(int index, NativeArray<byte> costs, byte wallCost)
    {
        return costs[index] == wallCost;
    }

    public static bool IsWall(int2 pos, int width, NativeArray<byte> costs, byte wallCost)
    {
        return costs[CalculateIndex(pos, width)] == wallCost;
    }

    public static bool IsWall(int2 pos, int layer, int width, int height, NativeArray<byte> costs, byte wallCost)
    {
        return costs[CalculateIndex(pos, layer, width, height)] == wallCost;
    }

    /// <summary>Check if world position is walkable.</summary>
    public static bool IsWalkable(float3 worldPosition, NavGridConfig config, NativeArray<byte> costs, byte wallCost)
    {
        int2 gridPos = GetGridPosition(worldPosition, config);
        int layer = GetLayer(worldPosition, config);

        if (!IsValidGridPosition(gridPos, layer, config.width, config.height, config.layerCount))
            return false;

        int index = CalculateIndex(gridPos, layer, config.width, config.height);
        return costs[index] != wallCost;
    }

    /// <summary>Get movement cost between two adjacent cells.</summary>
    public static float GetMovementCost(int dx, int dy, byte cellCost, byte wallCost)
    {
        if (cellCost == wallCost) return float.MaxValue;

        // Diagonal movement costs more
        float baseCost = (dx != 0 && dy != 0) ? 1.414f : 1f;
        return baseCost * cellCost;
    }
    
    /// <summary>Octile distance heuristic for 8-directional movement. Delegates to PathfindingUtils.OctileDistance.</summary>
    public static float OctileDistance(int2 a, int2 b) => PathfindingUtils.OctileDistance(a, b);
    
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
public struct UpdateNavGridCostMapJob : IJobParallelFor
{
    [ReadOnly] public int width;
    [ReadOnly] public float cellSize;
    [ReadOnly] public float cellSizeHalf;
    [ReadOnly] public float3 gridOrigin;
    [ReadOnly] public int layerCount;
    [ReadOnly] public float layerHeight;
    [ReadOnly] public int cellsPerLayer;
    [ReadOnly] public CollisionWorld collisionWorld;
    [ReadOnly] public CollisionFilter wallFilter;
    [ReadOnly] public CollisionFilter heavyFilter;
    [ReadOnly] public byte wallCost;
    [ReadOnly] public byte heavyCost;
    [ReadOnly] public byte defaultCost;

    [NativeDisableParallelForRestriction]
    public NativeArray<byte> costs;
    
    public void Execute(int index)
    {
        int layer = index / cellsPerLayer;
        int localIndex = index % cellsPerLayer;
        int x = localIndex % width;
        int y = localIndex / width;
        
        float3 worldPos = new float3(
            gridOrigin.x + x * cellSize + cellSizeHalf,
            gridOrigin.y + layer * layerHeight + 0.5f, // Slightly above ground
            gridOrigin.z + y * cellSize + cellSizeHalf
        );
        
        // Check for walls
        var wallHits = new NativeList<DistanceHit>(Allocator.Temp);
        if (collisionWorld.OverlapSphere(worldPos, cellSizeHalf * 0.9f, ref wallHits, wallFilter))
        {
            costs[index] = wallCost;
            wallHits.Dispose();
            return;
        }
        wallHits.Dispose();
        
        // Check for heavy/difficult terrain
        var heavyHits = new NativeList<DistanceHit>(Allocator.Temp);
        if (collisionWorld.OverlapSphere(worldPos, cellSizeHalf * 0.9f, ref heavyHits, heavyFilter))
        {
            costs[index] = heavyCost;
            heavyHits.Dispose();
            return;
        }
        heavyHits.Dispose();

        // Default walkable
        costs[index] = defaultCost;
    }
}
}