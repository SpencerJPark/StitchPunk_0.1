using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class TerrainSeamVerifier
{
    const float HeightToleranceMetres = 0.001f;
    const float MaskChannelTolerance = 0.1f;

    [MenuItem("Stitch Punk/Terrain/Verify Seams")]
    static void VerifySeams()
    {
        Dictionary<Vector2Int, TerrainTileData> tilesByCoordinate = new Dictionary<Vector2Int, TerrainTileData>();
        string[] guids = AssetDatabase.FindAssets("t:TerrainTileData");
        for (int guidIndex = 0; guidIndex < guids.Length; guidIndex++)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guids[guidIndex]);
            TerrainTileData tile = AssetDatabase.LoadAssetAtPath<TerrainTileData>(assetPath);
            if (tile != null)
            {
                tilesByCoordinate[tile.coordinate] = tile;
            }
        }

        HashSet<TerrainTileData> unreadableMaskTiles = new HashSet<TerrainTileData>();
        int pairsChecked = 0;
        int pairsFailing = 0;
        foreach (KeyValuePair<Vector2Int, TerrainTileData> entry in tilesByCoordinate)
        {
            Vector2Int[] neighbourOffsets = { new Vector2Int(1, 0), new Vector2Int(0, 1) };
            for (int offsetIndex = 0; offsetIndex < neighbourOffsets.Length; offsetIndex++)
            {
                Vector2Int offset = neighbourOffsets[offsetIndex];
                TerrainTileData neighbour;
                if (!tilesByCoordinate.TryGetValue(entry.Key + offset, out neighbour))
                {
                    continue;
                }
                pairsChecked++;
                bool isEastNeighbour = offset.x == 1;
                if (CheckPair(entry.Value, neighbour, isEastNeighbour, unreadableMaskTiles))
                {
                    pairsFailing++;
                }
            }
        }

        Debug.Log("Terrain seam check: " + pairsChecked + " pairs checked, " + pairsFailing + " failing.");
        if (pairsFailing == 0)
        {
            Debug.Log("Terrain seam check: no seams.");
        }
    }

    static bool CheckPair(TerrainTileData tileA, TerrainTileData tileB, bool isEastNeighbour, HashSet<TerrainTileData> unreadableMaskTiles)
    {
        bool failed = false;
        string pairName = tileA.coordinate + " / " + tileB.coordinate + (isEastNeighbour ? " (east edge)" : " (north edge)");
        int lastSample = TerrainTileData.HeightSamplesPerSide - 1;

        float[] heightsA = new float[TerrainTileData.HeightSamplesPerSide];
        float[] heightsB = new float[TerrainTileData.HeightSamplesPerSide];
        for (int sampleIndex = 0; sampleIndex <= lastSample; sampleIndex++)
        {
            heightsA[sampleIndex] = isEastNeighbour ? tileA.HeightAt(lastSample, sampleIndex) : tileA.HeightAt(sampleIndex, lastSample);
            heightsB[sampleIndex] = isEastNeighbour ? tileB.HeightAt(0, sampleIndex) : tileB.HeightAt(sampleIndex, 0);
        }
        int worstHeightIndex;
        float worstHeightMismatch = WorstHeightMismatch(heightsA, heightsB, out worstHeightIndex);
        if (worstHeightMismatch > HeightToleranceMetres)
        {
            failed = true;
            Debug.LogWarning("Seam height mismatch " + pairName + ": worst " + worstHeightMismatch.ToString("F4")
                + " m at edge sample " + worstHeightIndex + " of " + lastSample, tileA);
        }

        Color[] maskEdgeA = ReadMaskEdge(tileA.baseMask, isEastNeighbour, true, tileA, unreadableMaskTiles);
        Color[] maskEdgeB = ReadMaskEdge(tileB.baseMask, isEastNeighbour, false, tileB, unreadableMaskTiles);
        if (maskEdgeA != null && maskEdgeB != null)
        {
            float worstNormalisedPosition;
            float worstMaskMismatch = WorstMaskMismatch(maskEdgeA, maskEdgeB, out worstNormalisedPosition);
            if (worstMaskMismatch > MaskChannelTolerance)
            {
                failed = true;
                Debug.LogWarning("Seam mask discontinuity " + pairName + ": worst channel difference " + worstMaskMismatch.ToString("F3")
                    + " at " + (worstNormalisedPosition * TerrainTileData.TileSizeMetres).ToString("F1") + " m along the edge", tileA);
            }
        }
        return failed;
    }

    // Edge pixels: the outermost column (east) or row (north) on the side facing the neighbour.
    static Color[] ReadMaskEdge(Texture2D mask, bool isEastNeighbour, bool isLowerTile, TerrainTileData owner, HashSet<TerrainTileData> unreadableMaskTiles)
    {
        if (mask == null || unreadableMaskTiles.Contains(owner))
        {
            return null;
        }
        int edgePixelIndex = isLowerTile ? mask.width - 1 : 0;
        int edgeLength = isEastNeighbour ? mask.height : mask.width;
        if (!isEastNeighbour)
        {
            edgePixelIndex = isLowerTile ? mask.height - 1 : 0;
        }
        Color[] edgePixels = new Color[edgeLength];
        try
        {
            for (int pixelIndex = 0; pixelIndex < edgeLength; pixelIndex++)
            {
                edgePixels[pixelIndex] = isEastNeighbour ? mask.GetPixel(edgePixelIndex, pixelIndex) : mask.GetPixel(pixelIndex, edgePixelIndex);
            }
        }
        catch (UnityException)
        {
            unreadableMaskTiles.Add(owner);
            Debug.LogWarning("Seam check skipped the mask for " + owner.name + ": baseMask is not readable.", owner);
            return null;
        }
        return edgePixels;
    }

    internal static float WorstHeightMismatch(float[] edgeHeightsA, float[] edgeHeightsB, out int worstIndex)
    {
        worstIndex = 0;
        float worstMismatch = 0f;
        int sharedCount = Mathf.Min(edgeHeightsA.Length, edgeHeightsB.Length);
        for (int sampleIndex = 0; sampleIndex < sharedCount; sampleIndex++)
        {
            float mismatch = Mathf.Abs(edgeHeightsA[sampleIndex] - edgeHeightsB[sampleIndex]);
            if (mismatch > worstMismatch)
            {
                worstMismatch = mismatch;
                worstIndex = sampleIndex;
            }
        }
        return worstMismatch;
    }

    // Edges may differ in resolution, so both are sampled at the same normalised positions.
    internal static float WorstMaskMismatch(Color[] edgePixelsA, Color[] edgePixelsB, out float worstNormalisedPosition)
    {
        worstNormalisedPosition = 0f;
        float worstMismatch = 0f;
        int stepCount = Mathf.Max(edgePixelsA.Length, edgePixelsB.Length);
        for (int stepIndex = 0; stepIndex < stepCount; stepIndex++)
        {
            float normalisedPosition = stepCount > 1 ? (float)stepIndex / (stepCount - 1) : 0f;
            Color colorA = edgePixelsA[Mathf.RoundToInt(normalisedPosition * (edgePixelsA.Length - 1))];
            Color colorB = edgePixelsB[Mathf.RoundToInt(normalisedPosition * (edgePixelsB.Length - 1))];
            float mismatch = Mathf.Max(
                Mathf.Max(Mathf.Abs(colorA.r - colorB.r), Mathf.Abs(colorA.g - colorB.g)),
                Mathf.Max(Mathf.Abs(colorA.b - colorB.b), Mathf.Abs(colorA.a - colorB.a)));
            if (mismatch > worstMismatch)
            {
                worstMismatch = mismatch;
                worstNormalisedPosition = normalisedPosition;
            }
        }
        return worstMismatch;
    }
}
