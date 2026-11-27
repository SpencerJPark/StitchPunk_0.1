#if UNITY_EDITOR
using System;
using UnityEngine;

// Plateau outline is the smooth case-arc contour (CE-D1) on a 2 m dual grid: TerraceCaseContour lines and quarter circles.
public static class TerraceEdgeTileOutline
{
    private const int TileCells = TerrainTileData.TerraceCellsPerSide;
    private const float CellMetres = 2f;
    private const int CellsPerSide = 50;
    private const int CornersPerSide = CellsPerSide + 1;
    private const int PaddingCells = 2;

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
    public static float[] BuildSignedDistanceFromCaseContour(
        TerrainTileData data,
        int threshold,
        Func<Vector2Int, TerrainTileData> neighbourLookup,
        int samplesPerMetre)
    {
        int side = TileCells * samplesPerMetre + 1;
        float[] signedDistance = new float[side * side];
        int[] cornerLevels = BuildCornerLevels(data, neighbourLookup);

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

                int caseIndex = TerraceCaseContour.CaseFor(
                    cornerLevels[(cellZ + 1) * CornersPerSide + cellX],
                    cornerLevels[(cellZ + 1) * CornersPerSide + cellX + 1],
                    cornerLevels[cellZ * CornersPerSide + cellX + 1],
                    cornerLevels[cellZ * CornersPerSide + cellX],
                    threshold);

                signedDistance[sampleZ * side + sampleX] = TerraceCaseContour.SignedDistanceInCell(
                    caseIndex, new Vector2(cellLocalX * CellMetres, cellLocalZ * CellMetres));
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
}
#endif
