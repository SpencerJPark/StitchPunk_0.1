using Unity.Collections;
using Unity.Mathematics;

// Reads ground height straight from loaded tiles' height blobs. Interpolates on the same two triangles per cell
// that TerrainMeshBuilder emits, so the result matches the rendered and collided ground.
public static class TerrainHeightSampler
{
    private const float TileSizeMetres = 100f;

    // False when no loaded tile covers the position (the tile has not streamed in, or there is no tile there).
    public static bool TrySampleGroundHeight(in NativeArray<TerrainTileHeightField> loadedTiles, float3 worldPosition, out float groundHeight)
    {
        int2 tileCoordinate = TerrainNavWindowMath.TileCoordinateOf(worldPosition, TileSizeMetres);
        for (int tileIndex = 0; tileIndex < loadedTiles.Length; tileIndex++)
        {
            TerrainTileHeightField tile = loadedTiles[tileIndex];
            if (!math.all(tile.coordinate == tileCoordinate) || !tile.blob.IsCreated) continue;
            groundHeight = SampleTile(ref tile.blob.Value, tile.coordinate, worldPosition.x, worldPosition.z);
            return true;
        }
        groundHeight = worldPosition.y;
        return false;
    }

    public static float SampleTile(ref TerrainTileHeightBlob tile, int2 tileCoordinate, float worldX, float worldZ)
    {
        int lastCell = tile.samplesPerSide - 2;
        float spanMetres = (tile.samplesPerSide - 1) * tile.sampleSpacingMetres;
        float2 minCorner = (float2)tileCoordinate * TileSizeMetres - TileSizeMetres * 0.5f;
        float2 local = math.clamp(new float2(worldX, worldZ) - minCorner, 0f, spanMetres) / tile.sampleSpacingMetres;
        int2 cell = math.min((int2)local, lastCell);
        float2 fraction = local - cell;

        int rowStart = cell.y * tile.samplesPerSide + cell.x;
        float heightMinMin = tile.groundHeights[rowStart];
        float heightMaxMin = tile.groundHeights[rowStart + 1];
        float heightMinMax = tile.groundHeights[rowStart + tile.samplesPerSide];
        float heightMaxMax = tile.groundHeights[rowStart + tile.samplesPerSide + 1];

        // The cell's diagonal runs from (x+1, z) to (x, z+1).
        if (fraction.x + fraction.y <= 1f)
            return heightMinMin + (heightMaxMin - heightMinMin) * fraction.x + (heightMinMax - heightMinMin) * fraction.y;
        return heightMaxMax + (heightMinMax - heightMaxMax) * (1f - fraction.x) + (heightMaxMin - heightMaxMax) * (1f - fraction.y);
    }
}
