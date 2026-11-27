#if UNITY_EDITOR
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

// NC-D3..D5: field -> 1 m level raster for one tile, cleaned and with step kinds. Pure; no scene or AssetDatabase access.
public static class TerracePlateauRaster
{
    public const float CleanupRadiusMetres = 3f;
    public const float StackedSetbackMetres = 8f;
    public const float SmallRegionAreaSquareMetres = 600f;
    public const float SmallRegionLowChance = 0.85f;
    public const float SecondStepOppositeKindChance = 0.75f;
    public const int WindowMarginCells = 16;

    private static readonly int[] NeighbourOffsetsX = { 1, -1, 0, 0 };
    private static readonly int[] NeighbourOffsetsZ = { 0, 0, 1, -1 };

    // outLevels: 0..maxLevel per 1 m cell, length 10000, index z * 100 + x, cell (x, z) centre at
    // world (tileCoordinate * 100 - 50) + (x + 0.5, z + 0.5). outStepKinds: TerrainTileData bit layout, bit k-1 set = step +k is Low.
    public static void RasterizeTile(int worldSeed, Vector2Int tileCoordinate, int rollIndex, IReadOnlyList<TerracePlateauStyle> styles,
        int maxLevel, float minPlateauAreaSquareMetres, int[] outLevels, byte[] outStepKinds)
    {
        const int coreSide = TerrainTileData.TerraceCellsPerSide;
        const int windowSide = coreSide + 2 * WindowMarginCells;
        int2 tileCoordinateInt2 = new int2(tileCoordinate.x, tileCoordinate.y);
        float2 windowOriginWorld = new float2(tileCoordinate.x * 100f - 50f - WindowMarginCells, tileCoordinate.y * 100f - 50f - WindowMarginCells);

        // masks[k] = cells at level >= k; index 0 is unused.
        bool[][] masks = new bool[maxLevel + 1][];
        for (int level = 1; level <= maxLevel; level++)
        {
            masks[level] = new bool[windowSide * windowSide];
        }
        for (int windowZ = 0; windowZ < windowSide; windowZ++)
        {
            for (int windowX = 0; windowX < windowSide; windowX++)
            {
                float2 cellCentreWorld = windowOriginWorld + new float2(windowX + 0.5f, windowZ + 0.5f);
                float fieldValue = TerracePlateauField.SampleTileField(worldSeed, tileCoordinateInt2, rollIndex, styles, cellCentreWorld,
                    out float firstLevelThreshold, out float secondLevelThreshold);
                int cellLevel = fieldValue >= secondLevelThreshold ? 2 : fieldValue >= firstLevelThreshold ? 1 : 0;
                cellLevel = math.min(cellLevel, maxLevel);
                int cellIndex = windowZ * windowSide + windowX;
                for (int level = 1; level <= cellLevel; level++)
                {
                    masks[level][cellIndex] = true;
                }
            }
        }

        // Morphology is the last shaping step per level: set-back first, then open+close, so nothing re-creates a neck.
        for (int level = 2; level <= maxLevel; level++)
        {
            bool[] setbackAllowed = ErodeByDisc(masks[level - 1], windowSide, StackedSetbackMetres);
            AndInPlace(masks[level], setbackAllowed);
        }

        for (int level = 1; level <= maxLevel; level++)
        {
            masks[level] = OpenThenClose(masks[level], windowSide, CleanupRadiusMetres);
            if (level >= 2)
            {
                // Closing grows a mask by <= 3 m, inside the 8 m set-back; this is only a safety net.
                AndInPlace(masks[level], masks[level - 1]);
            }
        }

        // Culling removes whole regions, which cannot create a neck; ascending order drops higher regions that lost their footing.
        for (int level = 1; level <= maxLevel; level++)
        {
            if (level >= 2)
            {
                AndInPlace(masks[level], masks[level - 1]);
            }
            CullSmallRegionsTouchingCore(masks[level], windowSide, minPlateauAreaSquareMetres);
        }

        bool[][] coreMasks = new bool[maxLevel + 1][];
        for (int level = 1; level <= maxLevel; level++)
        {
            coreMasks[level] = new bool[coreSide * coreSide];
        }
        for (int coreZ = 0; coreZ < coreSide; coreZ++)
        {
            for (int coreX = 0; coreX < coreSide; coreX++)
            {
                int coreIndex = coreZ * coreSide + coreX;
                int windowIndex = (coreZ + WindowMarginCells) * windowSide + coreX + WindowMarginCells;
                int cellLevel = 0;
                for (int level = 1; level <= maxLevel; level++)
                {
                    coreMasks[level][coreIndex] = masks[level][windowIndex];
                    if (masks[level][windowIndex])
                    {
                        cellLevel = level;
                    }
                }
                outLevels[coreIndex] = cellLevel;
                outStepKinds[coreIndex] = 0;
            }
        }

        float tileStyleLowShare = 0.5f;
        if (styles != null && styles.Count > 0)
        {
            int styleIndex = TerracePlateauField.StyleIndexFor(worldSeed, tileCoordinateInt2, rollIndex, styles);
            tileStyleLowShare = styles[styleIndex].lowStepShare;
        }

        uint tileHash = math.hash(new int4(worldSeed, tileCoordinate.x, tileCoordinate.y, rollIndex));
        bool[] previousLevelIsLow = null;
        for (int level = 1; level <= maxLevel; level++)
        {
            bool[] levelIsLow = new bool[coreSide * coreSide];
            int[] regionLabels = LabelRegions(coreMasks[level], coreSide, out int regionCount, out int[] regionFirstCell, out int[] regionArea);
            bool[] regionIsLow = new bool[regionCount];
            for (int region = 0; region < regionCount; region++)
            {
                int firstCell = regionFirstCell[region];
                float roll = RollZeroToOne(tileHash, level, firstCell);
                if (level == 1)
                {
                    float lowChance = regionArea[region] < SmallRegionAreaSquareMetres ? SmallRegionLowChance : tileStyleLowShare;
                    regionIsLow[region] = roll < lowChance;
                }
                else
                {
                    bool enclosingIsLow = previousLevelIsLow[firstCell];
                    bool flips = roll < SecondStepOppositeKindChance;
                    regionIsLow[region] = flips ? !enclosingIsLow : enclosingIsLow;
                }
            }
            for (int coreIndex = 0; coreIndex < coreSide * coreSide; coreIndex++)
            {
                int region = regionLabels[coreIndex];
                if (region >= 0 && regionIsLow[region])
                {
                    levelIsLow[coreIndex] = true;
                    outStepKinds[coreIndex] |= (byte)(1 << (level - 1));
                }
            }
            previousLevelIsLow = levelIsLow;
        }
    }

    // Morphological opening then closing of a square mask (side x side) with a disc of radiusCells. NC-D4.
    public static bool[] OpenThenClose(bool[] mask, int side, float radiusCells)
    {
        bool[] opened = DilateByDisc(ErodeByDisc(mask, side, radiusCells), side, radiusCells);
        return ErodeByDisc(DilateByDisc(opened, side, radiusCells), side, radiusCells);
    }

    private static List<int2> BuildDiscOffsets(float radiusCells)
    {
        List<int2> offsets = new List<int2>();
        int radiusCeiling = (int)math.ceil(radiusCells);
        float radiusSquared = radiusCells * radiusCells;
        for (int offsetZ = -radiusCeiling; offsetZ <= radiusCeiling; offsetZ++)
        {
            for (int offsetX = -radiusCeiling; offsetX <= radiusCeiling; offsetX++)
            {
                if (offsetX * offsetX + offsetZ * offsetZ <= radiusSquared)
                {
                    offsets.Add(new int2(offsetX, offsetZ));
                }
            }
        }
        return offsets;
    }

    // Out-of-grid neighbours count as set, so the window edge never eats a mask.
    private static bool[] ErodeByDisc(bool[] mask, int side, float radiusCells)
    {
        List<int2> offsets = BuildDiscOffsets(radiusCells);
        bool[] result = new bool[mask.Length];
        for (int cellZ = 0; cellZ < side; cellZ++)
        {
            for (int cellX = 0; cellX < side; cellX++)
            {
                bool allSet = true;
                for (int offsetIndex = 0; offsetIndex < offsets.Count && allSet; offsetIndex++)
                {
                    int neighbourX = cellX + offsets[offsetIndex].x;
                    int neighbourZ = cellZ + offsets[offsetIndex].y;
                    bool outsideGrid = neighbourX < 0 || neighbourX >= side || neighbourZ < 0 || neighbourZ >= side;
                    allSet = outsideGrid || mask[neighbourZ * side + neighbourX];
                }
                result[cellZ * side + cellX] = allSet;
            }
        }
        return result;
    }

    // Out-of-grid neighbours are ignored.
    private static bool[] DilateByDisc(bool[] mask, int side, float radiusCells)
    {
        List<int2> offsets = BuildDiscOffsets(radiusCells);
        bool[] result = new bool[mask.Length];
        for (int cellZ = 0; cellZ < side; cellZ++)
        {
            for (int cellX = 0; cellX < side; cellX++)
            {
                bool anySet = false;
                for (int offsetIndex = 0; offsetIndex < offsets.Count && !anySet; offsetIndex++)
                {
                    int neighbourX = cellX + offsets[offsetIndex].x;
                    int neighbourZ = cellZ + offsets[offsetIndex].y;
                    anySet = neighbourX >= 0 && neighbourX < side && neighbourZ >= 0 && neighbourZ < side && mask[neighbourZ * side + neighbourX];
                }
                result[cellZ * side + cellX] = anySet;
            }
        }
        return result;
    }

    private static void AndInPlace(bool[] target, bool[] other)
    {
        for (int cellIndex = 0; cellIndex < target.Length; cellIndex++)
        {
            target[cellIndex] = target[cellIndex] && other[cellIndex];
        }
    }

    // Regions touching the window edge are kept (their true area is unknown); regions with no core cell are left alone.
    private static void CullSmallRegionsTouchingCore(bool[] mask, int windowSide, float minAreaSquareMetres)
    {
        int coreEnd = WindowMarginCells + TerrainTileData.TerraceCellsPerSide;
        int[] regionLabels = LabelRegions(mask, windowSide, out int regionCount, out int[] _, out int[] regionArea);
        bool[] touchesWindowEdge = new bool[regionCount];
        bool[] touchesCore = new bool[regionCount];
        for (int cellZ = 0; cellZ < windowSide; cellZ++)
        {
            for (int cellX = 0; cellX < windowSide; cellX++)
            {
                int region = regionLabels[cellZ * windowSide + cellX];
                if (region < 0)
                {
                    continue;
                }
                if (cellX == 0 || cellZ == 0 || cellX == windowSide - 1 || cellZ == windowSide - 1)
                {
                    touchesWindowEdge[region] = true;
                }
                if (cellX >= WindowMarginCells && cellX < coreEnd && cellZ >= WindowMarginCells && cellZ < coreEnd)
                {
                    touchesCore[region] = true;
                }
            }
        }
        for (int cellIndex = 0; cellIndex < mask.Length; cellIndex++)
        {
            int region = regionLabels[cellIndex];
            if (region >= 0 && touchesCore[region] && !touchesWindowEdge[region] && regionArea[region] < minAreaSquareMetres)
            {
                mask[cellIndex] = false;
            }
        }
    }

    // Labels are -1 for empty cells; regions are numbered in row-major order of their first cell, which is that region's smallest index.
    private static int[] LabelRegions(bool[] mask, int side, out int regionCount, out int[] regionFirstCell, out int[] regionArea)
    {
        int[] labels = new int[mask.Length];
        for (int cellIndex = 0; cellIndex < labels.Length; cellIndex++)
        {
            labels[cellIndex] = -1;
        }
        List<int> firstCells = new List<int>();
        List<int> areas = new List<int>();
        Stack<int> pending = new Stack<int>();
        for (int startIndex = 0; startIndex < mask.Length; startIndex++)
        {
            if (!mask[startIndex] || labels[startIndex] >= 0)
            {
                continue;
            }
            int region = firstCells.Count;
            int area = 0;
            labels[startIndex] = region;
            pending.Push(startIndex);
            while (pending.Count > 0)
            {
                int cellIndex = pending.Pop();
                area++;
                int cellX = cellIndex % side;
                int cellZ = cellIndex / side;
                for (int direction = 0; direction < 4; direction++)
                {
                    int neighbourX = cellX + NeighbourOffsetsX[direction];
                    int neighbourZ = cellZ + NeighbourOffsetsZ[direction];
                    if (neighbourX < 0 || neighbourX >= side || neighbourZ < 0 || neighbourZ >= side)
                    {
                        continue;
                    }
                    int neighbourIndex = neighbourZ * side + neighbourX;
                    if (mask[neighbourIndex] && labels[neighbourIndex] < 0)
                    {
                        labels[neighbourIndex] = region;
                        pending.Push(neighbourIndex);
                    }
                }
            }
            firstCells.Add(startIndex);
            areas.Add(area);
        }
        regionCount = firstCells.Count;
        regionFirstCell = firstCells.ToArray();
        regionArea = areas.ToArray();
        return labels;
    }

    private static float RollZeroToOne(uint tileHash, int stepLevel, int smallestCoreCellIndex)
    {
        uint hash = math.hash(new int4((int)tileHash, stepLevel, smallestCoreCellIndex, unchecked((int)0x9E3779B9u)));
        return (hash >> 8) / 16777216f;
    }
}
#endif
