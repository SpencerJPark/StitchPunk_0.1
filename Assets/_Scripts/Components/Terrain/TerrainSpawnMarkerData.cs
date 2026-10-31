using Unity.Entities;
using Unity.Mathematics;

// Baked on a marker placed inside a tile subscene. Persistent markers spawn once per session; on-demand markers spawn every time the tile loads.
public struct TerrainSpawnMarkerData : IComponentData
{
    public UnitType unitType;
    public int count;
    public float radiusMetres;
    public bool persistent;
    // 0 = never respawn. Only read for persistent markers.
    public float respawnAfterGameDays;
    // Stable across reloads of the tile: hash of the authored position and unit type.
    public uint markerKey;
}

// Baked disabled; TerrainSpawnSystem enables it once it has asked for this marker's units. A tile reload bakes a fresh marker, so it is unhandled again.
public struct TerrainSpawnMarkerHandled : IComponentData, IEnableableComponent { }

// Stamped by UnitSpawnerSystem on every unit a marker spawns. On-demand units leave with their home tile; persistent ones go abstract instead.
public struct SpawnedByMarker : IComponentData
{
    public uint markerKey;
    public int2 homeTile;
    public bool despawnWithTile;
}

// Singleton buffer of persistent markers that already spawned. Not saved: NPC units are not saved either, so a loaded game must respawn them.
public struct TerrainSpawnLedgerEntry : IBufferElementData
{
    public uint markerKey;
    public double spawnedAtWorldSeconds;
}
