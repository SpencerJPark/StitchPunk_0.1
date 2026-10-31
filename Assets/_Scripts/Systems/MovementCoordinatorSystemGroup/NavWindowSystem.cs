using DotsMovementToolkit;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

// Slides the nav grid over the loaded terrain tiles around the focus entity and rewrites the toolkit cost map.
[BurstCompile]
[UpdateInGroup(typeof(MovementCoordinatorSystemGroup))]
[UpdateAfter(typeof(NavGridSystem))]
public partial struct NavWindowSystem : ISystem
{
    private EntityQuery loadedTileQuery;
    private int2 lastFocusTile;
    private int lastTileComponentOrderVersion;
    private bool hasBuilt;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<NavGridSettings>();
        loadedTileQuery = SystemAPI.QueryBuilder().WithAll<TileNavCost>().Build();
        if (!SystemAPI.HasSingleton<TerrainNavWindowSettings>())
        {
            state.EntityManager.CreateSingleton(new TerrainNavWindowSettings
            {
                radiusTiles = 1,
                tileSizeMetres = 100f,
                cellsPerTile = 100
            });
        }
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        NavGridSettings gridSettings = SystemAPI.GetSingleton<NavGridSettings>();
        if (!gridSettings.externalCostRegions)
            return;
        if (!SystemAPI.HasSingleton<NavGridConfig>() || !SystemAPI.HasSingleton<NavGridCostMap>())
            return;

        bool foundFocus = false;
        float3 focusPosition = float3.zero;
        foreach (RefRO<LocalTransform> focusTransform in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<TerrainNavFocus>())
        {
            focusPosition = focusTransform.ValueRO.Position;
            foundFocus = true;
            break;
        }
        if (!foundFocus)
            return;

        TerrainNavWindowSettings windowSettings = SystemAPI.GetSingleton<TerrainNavWindowSettings>();
        Entity gridEntity = SystemAPI.GetSingletonEntity<NavGridConfig>();
        NavGridConfig gridConfig = SystemAPI.GetComponent<NavGridConfig>(gridEntity);
        NavGridCostMap gridCostMap = SystemAPI.GetComponent<NavGridCostMap>(gridEntity);

        int tilesPerSide = TerrainNavWindowMath.WindowTilesPerSide(windowSettings.radiusTiles);
        int windowCells = tilesPerSide * windowSettings.cellsPerTile;
        // A size mismatch is a scene authoring error (NavGridSettings width/height); stay inert instead of corrupting the map.
        if (gridConfig.width != windowCells || gridConfig.height != windowCells || !gridCostMap.costs.IsCreated)
            return;

        int2 focusTile = TerrainNavWindowMath.TileCoordinateOf(focusPosition, windowSettings.tileSizeMetres);
        int tileComponentOrderVersion = state.EntityManager.GetComponentOrderVersion<TileNavCost>();
        if (hasBuilt && !windowSettings.rebuildRequested && math.all(focusTile == lastFocusTile) && tileComponentOrderVersion == lastTileComponentOrderVersion)
            return;
        if (windowSettings.rebuildRequested)
        {
            windowSettings.rebuildRequested = false;
            SystemAPI.SetSingleton(windowSettings);
        }

        int2 minTile = TerrainNavWindowMath.WindowMinTile(focusTile, windowSettings.radiusTiles);
        NativeArray<TileNavCost> loadedTiles = loadedTileQuery.ToComponentDataArray<TileNavCost>(state.WorldUpdateAllocator);

        state.Dependency = new RebuildNavWindowCostsJob
        {
            costs = gridCostMap.costs,
            tiles = loadedTiles,
            minTile = minTile,
            tilesPerSide = tilesPerSide,
            cellsPerTile = windowSettings.cellsPerTile,
            windowWidth = windowCells,
            wallCost = gridSettings.wallCost,
            layerCount = gridConfig.layerCount
        }.Schedule(state.Dependency);
        // Downstream path systems read the costs this frame, as NavGridSystem does.
        state.CompleteDependency();

        gridConfig.gridOrigin = TerrainNavWindowMath.WindowOrigin(minTile, windowSettings.tileSizeMetres, gridConfig.gridOrigin.y);
        gridCostMap.costMapVersion += 1;
        gridCostMap.windowVersion += 1;
        SystemAPI.SetComponent(gridEntity, gridConfig);
        SystemAPI.SetComponent(gridEntity, gridCostMap);

        // Cached flow fields index cells that now mean other ground; only A* repaths on the version bump.
        if (SystemAPI.TryGetSingletonEntity<FlowFieldSystem.FlowFieldData>(out Entity flowFieldEntity))
        {
            FlowFieldSystem.FlowFieldData flowFieldData = SystemAPI.GetComponent<FlowFieldSystem.FlowFieldData>(flowFieldEntity);
            if (flowFieldData.targets.IsCreated)
                FlowFieldSystem.InvalidateAllFlowFields(ref flowFieldData);
        }

        lastTileComponentOrderVersion = tileComponentOrderVersion;
        lastFocusTile = focusTile;
        hasBuilt = true;
    }
}

[BurstCompile]
public struct RebuildNavWindowCostsJob : IJob
{
    public NativeArray<byte> costs;
    [ReadOnly] public NativeArray<TileNavCost> tiles;
    public int2 minTile;
    public int tilesPerSide;
    public int cellsPerTile;
    public int windowWidth;
    public byte wallCost;
    public int layerCount;

    public void Execute()
    {
        for (int cellIndex = 0; cellIndex < costs.Length; cellIndex++)
            costs[cellIndex] = wallCost;

        int cellsPerTileLayer = cellsPerTile * cellsPerTile;
        int cellsPerWindowLayer = windowWidth * windowWidth;

        for (int tileIndex = 0; tileIndex < tiles.Length; tileIndex++)
        {
            TileNavCost tile = tiles[tileIndex];
            if (!tile.blob.IsCreated)
                continue;
            if (!TerrainNavWindowMath.TryGetTileCellOffset(tile.coordinate, minTile, tilesPerSide, cellsPerTile, out int2 cellOffset))
                continue;

            ref TileNavCostBlob tileBlob = ref tile.blob.Value;
            if (tileBlob.cellsPerSide != cellsPerTile)
                continue;

            int sharedLayerCount = math.min(layerCount, tileBlob.layerCount);
            for (int layer = 0; layer < sharedLayerCount; layer++)
            {
                for (int row = 0; row < cellsPerTile; row++)
                {
                    int sourceRowStart = layer * cellsPerTileLayer + row * cellsPerTile;
                    int destinationRowStart = layer * cellsPerWindowLayer + (cellOffset.y + row) * windowWidth + cellOffset.x;
                    for (int column = 0; column < cellsPerTile; column++)
                        costs[destinationRowStart + column] = tileBlob.costs[sourceRowStart + column];
                }
            }
        }
    }
}
