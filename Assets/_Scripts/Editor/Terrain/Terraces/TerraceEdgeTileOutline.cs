#if UNITY_EDITOR
using System;
using UnityEngine;

public static class TerraceEdgeTileOutline
{
    private const int TileCells = TerrainTileData.TerraceCellsPerSide;
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
