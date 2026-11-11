using Unity.Entities;
using Unity.Mathematics;

public struct TerrainTileHeightBlob
{
    public int samplesPerSide;
    public float sampleSpacingMetres;
    // Final ground world Y (every height layer summed, ground-top offset included), laid out [z * samplesPerSide + x];
    // sample (0,0) is the tile's minimum-x/minimum-z corner.
    public BlobArray<float> groundHeights;
}

// The tile's ground as plain data, so anything that must never fall through (the caravan) can read the height
// without a physics ray that can miss a seam or a collider that has not streamed in.
public struct TerrainTileHeightField : IComponentData
{
    public BlobAssetReference<TerrainTileHeightBlob> blob;
    public int2 coordinate;
}
