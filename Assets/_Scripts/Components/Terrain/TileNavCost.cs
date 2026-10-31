using Unity.Entities;
using Unity.Mathematics;

public enum TerrainSurface : byte
{
    Normal = 0,
    Road = 1,
    Shallow = 2,
    Deep = 3
}

public struct TileNavCostBlob
{
    public int cellsPerSide;
    public int layerCount;
    // Both arrays are laid out [layer * cellsPerSide * cellsPerSide + z * cellsPerSide + x]; cell (0,0) is the tile's
    // minimum-x/minimum-z corner. costs use the nav grid's byte cost (255 = wall).
    public BlobArray<byte> costs;
    public BlobArray<byte> surface;
}

public struct TileNavCost : IComponentData
{
    public BlobAssetReference<TileNavCostBlob> blob;
    public int2 coordinate;
}
