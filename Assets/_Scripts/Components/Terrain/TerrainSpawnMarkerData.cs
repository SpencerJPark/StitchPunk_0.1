using Unity.Entities;

// Baked on a marker placed inside a tile subscene. Persistent markers spawn once per save; on-demand markers spawn every time the tile loads.
public struct TerrainSpawnMarkerData : IComponentData
{
    public UnitType unitType;
    public int count;
    public float radiusMetres;
    public bool persistent;
    // 0 = never respawn.
    public float respawnAfterGameDays;
}
