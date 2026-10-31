using Unity.Entities;

// Singleton read by NavWindowSystem; the window is (2 * radiusTiles + 3) tiles per side, so the baked
// NavGridSettings width and height must equal that times cellsPerTile (radius 1 = 500 x 500 cells).
public struct TerrainNavWindowSettings : IComponentData
{
    public int radiusTiles;
    public float tileSizeMetres;
    public int cellsPerTile;
}

// The nav window follows the first entity carrying this (the player or the driven caravan).
public struct TerrainNavFocus : IComponentData { }
