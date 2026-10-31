using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

// A marker in a loaded tile asks the normal UnitSpawner pipeline for its units; UnitSpawnerSystem does the spawning and stamps SpawnedByMarker.
[UpdateInGroup(typeof(TerrainSystemGroup))]
[UpdateAfter(typeof(AbstractArrivalSystem))]
public partial struct TerrainSpawnSystem : ISystem
{
    private const float TileSizeMetres = 100f;
    // TT-D28: wilderness tiles stay cheap, so one tile never hosts more than this many marker units.
    private const int MaxMarkerUnitsPerTile = 64;

    private Entity ledgerEntity;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<TerrainTileEntry>();
        state.RequireForUpdate<WorldClock>();
        ledgerEntity = state.EntityManager.CreateEntity();
        state.EntityManager.AddBuffer<TerrainSpawnLedgerEntry>(ledgerEntity);
    }

    public void OnUpdate(ref SystemState state)
    {
        Entity settingsEntity = SystemAPI.GetSingletonEntity<TerrainStreamingSettings>();
        DynamicBuffer<TerrainTileEntry> tileEntries = SystemAPI.GetBuffer<TerrainTileEntry>(settingsEntity);
        DynamicBuffer<TerrainSpawnLedgerEntry> ledger = SystemAPI.GetBuffer<TerrainSpawnLedgerEntry>(ledgerEntity);
        WorldClock clock = SystemAPI.GetSingleton<WorldClock>();

        NativeArray<SpawnedByMarker> livingMarkerUnits = SystemAPI.QueryBuilder().WithAll<SpawnedByMarker>().Build()
            .ToComponentDataArray<SpawnedByMarker>(Allocator.Temp);
        NativeList<int2> requestedTiles = new NativeList<int2>(Allocator.Temp);
        NativeList<int> requestedCounts = new NativeList<int>(Allocator.Temp);

        foreach ((RefRO<TerrainSpawnMarkerData> marker, RefRO<LocalTransform> transform, RefRW<UnitSpawner> spawner,
                  EnabledRefRW<UnitSpawner> spawnerEnabled, EnabledRefRW<TerrainSpawnMarkerHandled> handledEnabled)
            in SystemAPI.Query<RefRO<TerrainSpawnMarkerData>, RefRO<LocalTransform>, RefRW<UnitSpawner>,
                EnabledRefRW<UnitSpawner>, EnabledRefRW<TerrainSpawnMarkerHandled>>()
                .WithPresent<UnitSpawner, TerrainSpawnMarkerHandled>())
        {
            if (handledEnabled.ValueRO)
                continue;

            int2 tile = TerrainNavWindowMath.TileCoordinateOf(transform.ValueRO.Position, TileSizeMetres);
            if (!IsTileLoaded(tileEntries, tile))
                continue;

            TerrainSpawnMarkerData data = marker.ValueRO;
            int ledgerIndex = data.persistent ? FindLedgerIndex(ledger, data.markerKey) : -1;
            handledEnabled.ValueRW = true;
            if (ledgerIndex >= 0 && !RespawnIsDue(ledger[ledgerIndex], data, clock))
                continue;

            int budget = MaxMarkerUnitsPerTile - CountForTile(livingMarkerUnits, requestedTiles, requestedCounts, tile);
            int spawnCount = math.min(data.count, budget);
            if (spawnCount < data.count)
                Debug.LogWarning($"[TerrainSpawn] Tile {tile.x},{tile.y} is at its {MaxMarkerUnitsPerTile}-unit marker budget; a marker asked for {data.count} and got {math.max(spawnCount, 0)}.");
            if (spawnCount <= 0)
                continue;

            spawner.ValueRW = new UnitSpawner { unitType = data.unitType, spawnCount = spawnCount, range = data.radiusMetres };
            spawnerEnabled.ValueRW = true;
            requestedTiles.Add(tile);
            requestedCounts.Add(spawnCount);

            if (data.persistent)
                RecordInLedger(ledger, ledgerIndex, data.markerKey, clock.totalWorldSeconds);
        }

        livingMarkerUnits.Dispose();
        requestedTiles.Dispose();
        requestedCounts.Dispose();
    }

    private static bool IsTileLoaded(DynamicBuffer<TerrainTileEntry> tileEntries, int2 tile)
    {
        for (int index = 0; index < tileEntries.Length; index++)
        {
            if (tileEntries[index].coordinate.Equals(tile))
                return tileEntries[index].state == TerrainTileState.Loaded;
        }
        return false;
    }

    private static int FindLedgerIndex(DynamicBuffer<TerrainSpawnLedgerEntry> ledger, uint markerKey)
    {
        for (int index = 0; index < ledger.Length; index++)
        {
            if (ledger[index].markerKey == markerKey)
                return index;
        }
        return -1;
    }

    private static bool RespawnIsDue(in TerrainSpawnLedgerEntry entry, in TerrainSpawnMarkerData data, in WorldClock clock)
    {
        if (data.respawnAfterGameDays <= 0f || clock.secondsPerDay <= 0f)
            return false;
        return clock.totalWorldSeconds - entry.spawnedAtWorldSeconds >= data.respawnAfterGameDays * clock.secondsPerDay;
    }

    private static void RecordInLedger(DynamicBuffer<TerrainSpawnLedgerEntry> ledger, int ledgerIndex, uint markerKey, double nowWorldSeconds)
    {
        TerrainSpawnLedgerEntry entry = new TerrainSpawnLedgerEntry { markerKey = markerKey, spawnedAtWorldSeconds = nowWorldSeconds };
        if (ledgerIndex >= 0)
            ledger[ledgerIndex] = entry;
        else
            ledger.Add(entry);
    }

    private static int CountForTile(NativeArray<SpawnedByMarker> livingMarkerUnits, NativeList<int2> requestedTiles, NativeList<int> requestedCounts, int2 tile)
    {
        int total = 0;
        for (int index = 0; index < livingMarkerUnits.Length; index++)
        {
            if (livingMarkerUnits[index].homeTile.Equals(tile))
                total++;
        }
        for (int index = 0; index < requestedTiles.Length; index++)
        {
            if (requestedTiles[index].Equals(tile))
                total += requestedCounts[index];
        }
        return total;
    }
}

// On-demand units belong to their tile: when it stops being Loaded they are sent through the normal Despawn path (pooled, not destroyed).
[BurstCompile]
[UpdateInGroup(typeof(TerrainSystemGroup))]
[UpdateAfter(typeof(TerrainSpawnSystem))]
public partial struct TerrainSpawnDespawnSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<TerrainTileEntry>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        Entity settingsEntity = SystemAPI.GetSingletonEntity<TerrainStreamingSettings>();
        NativeArray<TerrainTileEntry> tileEntries = SystemAPI.GetBuffer<TerrainTileEntry>(settingsEntity)
            .ToNativeArray(state.WorldUpdateAllocator);
        state.Dependency = new TerrainSpawnDespawnJob { tileEntries = tileEntries }.Schedule(state.Dependency);
    }
}

[BurstCompile]
[WithPresent(typeof(Despawn))]
public partial struct TerrainSpawnDespawnJob : IJobEntity
{
    [ReadOnly] public NativeArray<TerrainTileEntry> tileEntries;

    public void Execute(in SpawnedByMarker stamp, EnabledRefRW<Despawn> despawnEnabled)
    {
        if (!stamp.despawnWithTile || despawnEnabled.ValueRO)
            return;

        for (int index = 0; index < tileEntries.Length; index++)
        {
            if (!tileEntries[index].coordinate.Equals(stamp.homeTile))
                continue;
            if (tileEntries[index].state != TerrainTileState.Loaded)
                despawnEnabled.ValueRW = true;
            return;
        }
    }
}
