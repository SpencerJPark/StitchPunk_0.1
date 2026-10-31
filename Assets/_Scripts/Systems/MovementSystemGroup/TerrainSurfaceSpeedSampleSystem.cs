using DotsMovementToolkit;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

// Samples every frame, not every 5th: it is one hash lookup per unit, cheaper than the bookkeeping to stagger it.
[BurstCompile]
[UpdateInGroup(typeof(MovementSystemGroup))]
[UpdateBefore(typeof(DrunkStumbleSystem))]
public partial struct TerrainSurfaceSpeedSampleSystem : ISystem
{
    private EntityQuery unitsMissingSurfaceSpeedQuery;
    private EntityQuery loadedTileQuery;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<TerrainNavWindowSettings>();
        unitsMissingSurfaceSpeedQuery = SystemAPI.QueryBuilder()
            .WithAll<Movement>()
            .WithNone<SurfaceSpeedFactor>()
            .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
            .Build();
        loadedTileQuery = SystemAPI.QueryBuilder().WithAll<TileNavCost>().Build();
        if (!SystemAPI.HasSingleton<TerrainSurfaceSpeedSettings>())
        {
            state.EntityManager.CreateSingleton(new TerrainSurfaceSpeedSettings
            {
                roadMultiplier = 1.25f,
                shallowMultiplier = 0.5f
            });
        }
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!unitsMissingSurfaceSpeedQuery.IsEmpty)
        {
            state.EntityManager.AddComponent(unitsMissingSurfaceSpeedQuery, ComponentType.ReadWrite<SurfaceSpeedFactor>());
        }

        TerrainNavWindowSettings windowSettings = SystemAPI.GetSingleton<TerrainNavWindowSettings>();
        TerrainSurfaceSpeedSettings speedSettings = SystemAPI.GetSingleton<TerrainSurfaceSpeedSettings>();

        NativeArray<TileNavCost> loadedTiles = loadedTileQuery.ToComponentDataArray<TileNavCost>(state.WorldUpdateAllocator);
        NativeParallelHashMap<int2, TileNavCost> tilesByCoordinate =
            new NativeParallelHashMap<int2, TileNavCost>(math.max(loadedTiles.Length, 1), state.WorldUpdateAllocator);
        for (int tileIndex = 0; tileIndex < loadedTiles.Length; tileIndex++)
            tilesByCoordinate.TryAdd(loadedTiles[tileIndex].coordinate, loadedTiles[tileIndex]);

        SampleSurfaceSpeedJob job = new SampleSurfaceSpeedJob
        {
            tilesByCoordinate = tilesByCoordinate,
            tileSizeMetres = windowSettings.tileSizeMetres,
            cellsPerTile = windowSettings.cellsPerTile,
            roadMultiplier = speedSettings.roadMultiplier,
            shallowMultiplier = speedSettings.shallowMultiplier
        };
        state.Dependency = job.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
public partial struct SampleSurfaceSpeedJob : IJobEntity
{
    [ReadOnly] public NativeParallelHashMap<int2, TileNavCost> tilesByCoordinate;
    public float tileSizeMetres;
    public int cellsPerTile;
    public float roadMultiplier;
    public float shallowMultiplier;

    public void Execute(in LocalTransform localTransform, ref SurfaceSpeedFactor surfaceSpeedFactor)
    {
        surfaceSpeedFactor.factor = 1f;

        float3 position = localTransform.Position;
        int2 tileCoordinate = TerrainNavWindowMath.TileCoordinateOf(position, tileSizeMetres);
        if (!tilesByCoordinate.TryGetValue(tileCoordinate, out TileNavCost tile) || !tile.blob.IsCreated)
            return;

        ref TileNavCostBlob tileBlob = ref tile.blob.Value;
        if (tileBlob.cellsPerSide != cellsPerTile)
            return;

        float2 tileMinCorner = new float2(tileCoordinate.x, tileCoordinate.y) * tileSizeMetres - tileSizeMetres * 0.5f;
        float metresPerCell = tileSizeMetres / cellsPerTile;
        int cellX = math.clamp((int)math.floor((position.x - tileMinCorner.x) / metresPerCell), 0, cellsPerTile - 1);
        int cellZ = math.clamp((int)math.floor((position.z - tileMinCorner.y) / metresPerCell), 0, cellsPerTile - 1);

        TerrainSurface surface = (TerrainSurface)tileBlob.surface[cellZ * cellsPerTile + cellX];
        if (surface == TerrainSurface.Road)
            surfaceSpeedFactor.factor = roadMultiplier;
        else if (surface == TerrainSurface.Shallow)
            surfaceSpeedFactor.factor = shallowMultiplier;
    }
}
