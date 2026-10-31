using Unity.Mathematics;

// Tile (cx, cz) is centred on (cx, cz) * tileSizeMetres and spans half a tile either side, matching TerrainTile.
public static class TerrainNavWindowMath
{
    public static int2 TileCoordinateOf(float3 worldPosition, float tileSizeMetres)
    {
        return (int2)math.round(worldPosition.xz / tileSizeMetres);
    }

    public static int WindowTilesPerSide(int radiusTiles)
    {
        return 2 * radiusTiles + 3;
    }

    public static int2 WindowMinTile(int2 focusTile, int radiusTiles)
    {
        return focusTile - (radiusTiles + 1);
    }

    // Cell (0,0)'s corner is the minimum corner of the window's minimum tile.
    public static float3 WindowOrigin(int2 minTile, float tileSizeMetres, float originHeight)
    {
        return new float3(minTile.x * tileSizeMetres - tileSizeMetres * 0.5f, originHeight, minTile.y * tileSizeMetres - tileSizeMetres * 0.5f);
    }

    // False when the tile lies outside the window, in which case it contributes nothing to the cost map.
    public static bool TryGetTileCellOffset(int2 tileCoordinate, int2 minTile, int tilesPerSide, int cellsPerTile, out int2 cellOffset)
    {
        int2 tileOffset = tileCoordinate - minTile;
        cellOffset = tileOffset * cellsPerTile;
        return math.all(tileOffset >= 0) && math.all(tileOffset < tilesPerSide);
    }
}
