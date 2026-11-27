#if UNITY_EDITOR
// 16-case dual-grid edge tiles shared by the CPU (terrace outlines) and the ground shader (PaintedTransitionTile_float).
// Both must pick the same case, variant and atlas cell, so these formulas mirror PaintedGroundCore.hlsl exactly.
public static class TerrainEdgeTileCases
{
    public const int AtlasColumns = 8;
    public const int AtlasRows = 4;
    public const int CellPixels = 128;
    public const int PlateauVariantSeed = 47;

    // Bits: top-left 1, top-right 2, bottom-right 4, bottom-left 8; top = +Z.
    public static int CaseFor(bool topLeftInside, bool topRightInside, bool bottomRightInside, bool bottomLeftInside)
    {
        return (topLeftInside ? 1 : 0) | (topRightInside ? 2 : 0) | (bottomRightInside ? 4 : 0) | (bottomLeftInside ? 8 : 0);
    }

    // Integer hash so the GPU and CPU agree bit for bit (a sin-based hash differs between them).
    public static int VariantFor(int cellX, int cellZ, int seed)
    {
        unchecked
        {
            uint hash = (uint)(cellX * 73856093) ^ (uint)(cellZ * 19349663) ^ (uint)(seed * 83492791);
            hash ^= hash >> 13;
            hash *= 0x5bd1e995u;
            hash ^= hash >> 15;
            return (int)(hash & 1u);
        }
    }

    // Pixel of the atlas PNG (row 0 = top of the image) for case/variant and a cell-local position in 0..1 (+Z up).
    public static void AtlasPixelFor(int caseIndex, int variant, float cellLocalX01, float cellLocalZ01, out int pixelX, out int pixelYFromTop)
    {
        int atlasColumn = caseIndex % AtlasColumns;
        int atlasRow = caseIndex / AtlasColumns + 2 * variant;
        int localPixelX = System.Math.Min(CellPixels - 1, System.Math.Max(0, (int)(cellLocalX01 * CellPixels)));
        int localPixelYFromTop = System.Math.Min(CellPixels - 1, System.Math.Max(0, (int)((1f - cellLocalZ01) * CellPixels)));
        pixelX = atlasColumn * CellPixels + localPixelX;
        pixelYFromTop = atlasRow * CellPixels + localPixelYFromTop;
    }
}
#endif
