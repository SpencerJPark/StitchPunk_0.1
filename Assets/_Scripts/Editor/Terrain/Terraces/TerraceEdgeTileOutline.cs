#if UNITY_EDITOR
using System;
using System.IO;
using UnityEngine;

// Plateau outline taken from the grass edge-tile silhouette on a 2 m dual grid, so terraces and biome edges share lobes.
public static class TerraceEdgeTileOutline
{
    public const string AtlasPath = "Assets/PaintedGround/Textures/EdgeTiles/T_EdgeAtlas_GrassOverDirt.png";

    private const int TileCells = TerrainTileData.TerraceCellsPerSide;
    private const float CellMetres = 2f;
    private const int CellsPerSide = 50;
    private const int CornersPerSide = CellsPerSide + 1;
    private const int PaddingCells = 2;
    // Metres of field per unit of ink coverage; only the 0.5 crossing matters to marching squares.
    private const float CoverageToMetres = 1f;

    private static float[] cachedCoverage;
    private static int cachedWidth;
    private static int cachedHeight;
    private static DateTime cachedWriteTimeUtc;

    // Corner (i, j) sits at tile-local metres (2i, 2j); level = majority of the four touching 1 m cells, ties to the higher level.
    public static int[] BuildCornerLevels(TerrainTileData data, Func<Vector2Int, TerrainTileData> neighbourLookup)
    {
        int[] paddedLevels = TerraceLevelField.BuildPaddedLevels(data, PaddingCells, neighbourLookup);
        int paddedCells = TileCells + 2 * PaddingCells;
        int[] cornerLevels = new int[CornersPerSide * CornersPerSide];
        int[] touching = new int[4];
        for (int cornerZ = 0; cornerZ < CornersPerSide; cornerZ++)
        {
            for (int cornerX = 0; cornerX < CornersPerSide; cornerX++)
            {
                int firstCellX = 2 * cornerX - 1 + PaddingCells;
                int firstCellZ = 2 * cornerZ - 1 + PaddingCells;
                touching[0] = paddedLevels[firstCellZ * paddedCells + firstCellX];
                touching[1] = paddedLevels[firstCellZ * paddedCells + firstCellX + 1];
                touching[2] = paddedLevels[(firstCellZ + 1) * paddedCells + firstCellX];
                touching[3] = paddedLevels[(firstCellZ + 1) * paddedCells + firstCellX + 1];
                cornerLevels[cornerZ * CornersPerSide + cornerX] = MajorityTiesHigher(touching);
            }
        }

        return cornerLevels;
    }

    // Same layout, sign and units as TerraceLevelField.BuildSignedDistance (side = 100 * spm + 1, z * side + x, positive inside).
    public static float[] BuildSignedDistanceFromEdgeTiles(
        TerrainTileData data,
        int threshold,
        Func<Vector2Int, TerrainTileData> neighbourLookup,
        int samplesPerMetre)
    {
        float[] coverage = LoadAtlasCoverage(out int atlasWidth, out int atlasHeight);
        int side = TileCells * samplesPerMetre + 1;
        float[] signedDistance = new float[side * side];
        int[] cornerLevels = BuildCornerLevels(data, neighbourLookup);
        // Tile min corner is (coordinate * 100 - 50) m, i.e. this many 2 m cells from the world origin.
        int globalCellOffsetX = data.coordinate.x * CellsPerSide - CellsPerSide / 2;
        int globalCellOffsetZ = data.coordinate.y * CellsPerSide - CellsPerSide / 2;
        float clamp = TerraceLevelField.SignedDistanceClampMetres;

        for (int sampleZ = 0; sampleZ < side; sampleZ++)
        {
            float localZ = sampleZ / (float)samplesPerMetre;
            int cellZ = Math.Min((int)(localZ / CellMetres), CellsPerSide - 1);
            float cellLocalZ = localZ / CellMetres - cellZ;
            for (int sampleX = 0; sampleX < side; sampleX++)
            {
                float localX = sampleX / (float)samplesPerMetre;
                int cellX = Math.Min((int)(localX / CellMetres), CellsPerSide - 1);
                float cellLocalX = localX / CellMetres - cellX;

                bool topLeft = cornerLevels[(cellZ + 1) * CornersPerSide + cellX] >= threshold;
                bool topRight = cornerLevels[(cellZ + 1) * CornersPerSide + cellX + 1] >= threshold;
                bool bottomRight = cornerLevels[cellZ * CornersPerSide + cellX + 1] >= threshold;
                bool bottomLeft = cornerLevels[cellZ * CornersPerSide + cellX] >= threshold;
                int caseIndex = TerrainEdgeTileCases.CaseFor(topLeft, topRight, bottomRight, bottomLeft);

                float value;
                if (caseIndex == 0)
                {
                    value = -clamp;
                }
                else if (caseIndex == 15)
                {
                    value = clamp;
                }
                else
                {
                    int variant = TerrainEdgeTileCases.VariantFor(
                        globalCellOffsetX + cellX, globalCellOffsetZ + cellZ, TerrainEdgeTileCases.PlateauVariantSeed);
                    float inkCoverage = SampleCoverageBilinear(
                        coverage, atlasWidth, atlasHeight, caseIndex, variant, cellLocalX, cellLocalZ);
                    value = Mathf.Clamp((inkCoverage - 0.5f) * CoverageToMetres, -clamp, clamp);
                }

                signedDistance[sampleZ * side + sampleX] = value;
            }
        }

        return signedDistance;
    }

    private static int MajorityTiesHigher(int[] levels)
    {
        int bestLevel = int.MinValue;
        int bestCount = 0;
        for (int candidateIndex = 0; candidateIndex < levels.Length; candidateIndex++)
        {
            int candidate = levels[candidateIndex];
            int count = 0;
            for (int otherIndex = 0; otherIndex < levels.Length; otherIndex++)
            {
                if (levels[otherIndex] == candidate)
                {
                    count++;
                }
            }

            if (count > bestCount || (count == bestCount && candidate > bestLevel))
            {
                bestCount = count;
                bestLevel = candidate;
            }
        }

        return bestLevel;
    }

    // Bilinear inside the atlas cell only, so neighbouring atlas cells never bleed into each other.
    private static float SampleCoverageBilinear(
        float[] coverage, int atlasWidth, int atlasHeight, int caseIndex, int variant, float cellLocalX, float cellLocalZ)
    {
        int cellPixels = TerrainEdgeTileCases.CellPixels;
        int cellOriginX = (caseIndex % TerrainEdgeTileCases.AtlasColumns) * cellPixels;
        int cellOriginY = (caseIndex / TerrainEdgeTileCases.AtlasColumns + 2 * variant) * cellPixels;
        float pixelX = Mathf.Clamp(cellLocalX * cellPixels - 0.5f, 0f, cellPixels - 1f);
        float pixelYFromTop = Mathf.Clamp((1f - cellLocalZ) * cellPixels - 0.5f, 0f, cellPixels - 1f);
        int lowX = (int)pixelX;
        int lowY = (int)pixelYFromTop;
        int highX = Math.Min(lowX + 1, cellPixels - 1);
        int highY = Math.Min(lowY + 1, cellPixels - 1);
        float blendX = pixelX - lowX;
        float blendY = pixelYFromTop - lowY;

        float topValue = Mathf.Lerp(
            Texel(coverage, atlasWidth, atlasHeight, cellOriginX + lowX, cellOriginY + lowY),
            Texel(coverage, atlasWidth, atlasHeight, cellOriginX + highX, cellOriginY + lowY), blendX);
        float bottomValue = Mathf.Lerp(
            Texel(coverage, atlasWidth, atlasHeight, cellOriginX + lowX, cellOriginY + highY),
            Texel(coverage, atlasWidth, atlasHeight, cellOriginX + highX, cellOriginY + highY), blendX);
        return Mathf.Lerp(topValue, bottomValue, blendY);
    }

    private static float Texel(float[] coverage, int atlasWidth, int atlasHeight, int pixelX, int pixelYFromTop)
    {
        int clampedX = Math.Min(atlasWidth - 1, Math.Max(0, pixelX));
        int clampedY = Math.Min(atlasHeight - 1, Math.Max(0, pixelYFromTop));
        return coverage[clampedY * atlasWidth + clampedX];
    }

    // Reads the PNG bytes (not the import) so the texture needs no Read/Write flag; row 0 of the result is the PNG top.
    private static float[] LoadAtlasCoverage(out int atlasWidth, out int atlasHeight)
    {
        DateTime writeTimeUtc = File.GetLastWriteTimeUtc(AtlasPath);
        if (cachedCoverage != null && writeTimeUtc == cachedWriteTimeUtc)
        {
            atlasWidth = cachedWidth;
            atlasHeight = cachedHeight;
            return cachedCoverage;
        }

        Texture2D atlas = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        try
        {
            if (!ImageConversion.LoadImage(atlas, File.ReadAllBytes(AtlasPath), false))
            {
                throw new InvalidOperationException("Could not decode edge atlas " + AtlasPath);
            }

            Color32[] pixels = atlas.GetPixels32();
            cachedWidth = atlas.width;
            cachedHeight = atlas.height;
            cachedCoverage = new float[pixels.Length];
            for (int rowFromBottom = 0; rowFromBottom < cachedHeight; rowFromBottom++)
            {
                int rowFromTop = cachedHeight - 1 - rowFromBottom;
                for (int column = 0; column < cachedWidth; column++)
                {
                    cachedCoverage[rowFromTop * cachedWidth + column] = pixels[rowFromBottom * cachedWidth + column].r / 255f;
                }
            }

            cachedWriteTimeUtc = writeTimeUtc;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(atlas);
        }

        atlasWidth = cachedWidth;
        atlasHeight = cachedHeight;
        return cachedCoverage;
    }
}
#endif
