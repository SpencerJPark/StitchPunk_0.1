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

    private const int CoreSide = TerrainTileData.TerraceCellsPerSide;
    private const int WindowSide = CoreSide + 2 * WindowMarginCells;
    private const int ChannelMinimumInsetCells = 8;
    private const float ChannelHalfWidthMetres = 3.5f;
    private const int MaxChannelCutsPerStep = 16;

    // outLevels: 0..maxLevel per 1 m cell, length 10000, index z * 100 + x, cell (x, z) centre at
    // world (tileCoordinate * 100 - 50) + (x + 0.5, z + 0.5). outStepKinds: TerrainTileData bit layout, bit k-1 set = step +k is Low.
    public static void RasterizeTile(int worldSeed, Vector2Int tileCoordinate, int rollIndex, IReadOnlyList<TerracePlateauStyle> styles,
        int maxLevel, float minPlateauAreaSquareMetres, int[] outLevels, byte[] outStepKinds)
    {
        RasterizeTile(worldSeed, tileCoordinate, rollIndex, styles, maxLevel, minPlateauAreaSquareMetres,
            (System.Func<Vector2Int, TerrainTileData>)null, (System.Func<Vector2, bool, int>)null, outLevels, outStepKinds);
    }

    // NC-D8 overload for the generator: margin from stored neighbours, lock band from the world field only, kind inheritance.
    // maxLevelAtWorldPoint(point, useRolledField) caps the level per cell (biome rule); the world pass passes false so the lock band ignores rolls. null = maxLevel everywhere.
    public static void RasterizeTile(int worldSeed, Vector2Int tileCoordinate, int rollIndex, IReadOnlyList<TerracePlateauStyle> styles,
        int maxLevel, float minPlateauAreaSquareMetres, System.Func<Vector2Int, TerrainTileData> neighbourLookup,
        System.Func<Vector2, bool, int> maxLevelAtWorldPoint, int[] outLevels, byte[] outStepKinds)
    {
        int cellCount = WindowSide * WindowSide;
        int[] storedLevels = new int[cellCount];
        byte[] storedKinds = new byte[cellCount];
        FillStoredNeighbourWindow(tileCoordinate, maxLevel, neighbourLookup, storedLevels, storedKinds);

        bool[] isLockBandCell = new bool[cellCount];
        bool[] isFixedCell = new bool[cellCount];
        bool[] isProtectedCell = new bool[cellCount];
        for (int windowZ = 0; windowZ < WindowSide; windowZ++)
        {
            for (int windowX = 0; windowX < WindowSide; windowX++)
            {
                int coreX = windowX - WindowMarginCells;
                int coreZ = windowZ - WindowMarginCells;
                bool insideCore = coreX >= 0 && coreX < CoreSide && coreZ >= 0 && coreZ < CoreSide;
                int windowIndex = windowZ * WindowSide + windowX;
                bool isLockBand = insideCore && InsetCellsFromBorder(coreX, coreZ) + 0.5f < TerracePlateauField.LockMetres;
                isLockBandCell[windowIndex] = isLockBand;
                isFixedCell[windowIndex] = !insideCore || isLockBand;
                isProtectedCell[windowIndex] = isLockBand || storedLevels[windowIndex] >= 0;
            }
        }

        // W: world field only. R: rolled field. Both pin stored neighbour cells, so only R carries the tile's own roll.
        int[] worldLevels = BuildCleanedWindowLevels(worldSeed, tileCoordinate, rollIndex, styles, false, maxLevel, maxLevelAtWorldPoint, storedLevels);
        int[] rolledLevels = BuildCleanedWindowLevels(worldSeed, tileCoordinate, rollIndex, styles, true, maxLevel, maxLevelAtWorldPoint, storedLevels);

        bool[][] masks = new bool[maxLevel + 1][];
        for (int level = 1; level <= maxLevel; level++)
        {
            masks[level] = new bool[cellCount];
        }
        for (int windowIndex = 0; windowIndex < cellCount; windowIndex++)
        {
            int combinedLevel = isFixedCell[windowIndex] ? worldLevels[windowIndex] : rolledLevels[windowIndex];
            for (int level = 1; level <= combinedLevel; level++)
            {
                masks[level][windowIndex] = true;
            }
        }

        // Rolled interior is re-cleaned against the fixed W / stored cells; fixed cells are never written back.
        for (int level = 1; level <= maxLevel; level++)
        {
            bool[] cleaned = OpenThenClose(masks[level], WindowSide, CleanupRadiusMetres);
            for (int windowIndex = 0; windowIndex < cellCount; windowIndex++)
            {
                if (!isFixedCell[windowIndex])
                {
                    masks[level][windowIndex] = cleaned[windowIndex];
                }
            }
            if (level >= 2)
            {
                AndInPlace(masks[level], masks[level - 1]);
                ClearBelowStackedSetback(masks, level, isFixedCell);
            }
        }

        for (int level = 1; level <= maxLevel; level++)
        {
            if (level >= 2)
            {
                AndInPlace(masks[level], masks[level - 1]);
            }
            CullSmallRegionsTouchingCore(masks[level], WindowSide, minPlateauAreaSquareMetres, isProtectedCell);
        }

        byte[] windowKinds = AssignWindowStepKinds(worldSeed, tileCoordinate, rollIndex, styles, maxLevel, masks, storedLevels, storedKinds, isLockBandCell, worldLevels);

        // Kinds are already assigned, so a full open+close is safe here: cells the close adds copy the nearest kind. AND + set-back can reopen a thin spot, hence two passes.
        for (int cleanupPass = 0; cleanupPass < FinalCleanupPassCount; cleanupPass++)
        {
            ApplyFinalCleanupToFreeCells(masks, windowKinds, isFixedCell, maxLevel);
        }

        for (int coreZ = 0; coreZ < CoreSide; coreZ++)
        {
            for (int coreX = 0; coreX < CoreSide; coreX++)
            {
                int coreIndex = coreZ * CoreSide + coreX;
                int windowIndex = (coreZ + WindowMarginCells) * WindowSide + coreX + WindowMarginCells;
                int cellLevel = 0;
                for (int level = 1; level <= maxLevel; level++)
                {
                    if (masks[level][windowIndex])
                    {
                        cellLevel = level;
                    }
                }
                outLevels[coreIndex] = cellLevel;
                // A channel cut can leave a kind bit on a cell that dropped below that step.
                outStepKinds[coreIndex] = (byte)(windowKinds[windowIndex] & ((1 << cellLevel) - 1));
            }
        }
    }

    private const int FinalCleanupPassCount = 2;

    private static void ApplyFinalCleanupToFreeCells(bool[][] masks, byte[] windowKinds, bool[] isFixedCell, int maxLevel)
    {
        int cellCount = WindowSide * WindowSide;
        int searchRadiusCells = Mathf.CeilToInt(CleanupRadiusMetres);
        for (int level = 1; level <= maxLevel; level++)
        {
            bool[] maskBeforeCleanup = (bool[])masks[level].Clone();
            bool[] cleaned = OpenThenClose(maskBeforeCleanup, WindowSide, CleanupRadiusMetres);
            byte levelKindBit = (byte)(1 << (level - 1));
            for (int windowIndex = 0; windowIndex < cellCount; windowIndex++)
            {
                if (isFixedCell[windowIndex] || cleaned[windowIndex] == maskBeforeCleanup[windowIndex])
                {
                    continue;
                }
                masks[level][windowIndex] = cleaned[windowIndex];
                if (cleaned[windowIndex])
                {
                    int sourceIndex = FindNearestSetCell(maskBeforeCleanup, windowIndex, searchRadiusCells);
                    bool sourceIsLow = sourceIndex >= 0 && (windowKinds[sourceIndex] & levelKindBit) != 0;
                    windowKinds[windowIndex] = (byte)(sourceIsLow ? (windowKinds[windowIndex] | levelKindBit) : (windowKinds[windowIndex] & ~levelKindBit));
                }
            }
            if (level >= 2)
            {
                AndInPlace(masks[level], masks[level - 1]);
                ClearBelowStackedSetback(masks, level, isFixedCell);
            }
        }
    }

    private static int FindNearestSetCell(bool[] mask, int windowIndex, int searchRadiusCells)
    {
        int cellX = windowIndex % WindowSide;
        int cellZ = windowIndex / WindowSide;
        int nearestIndex = -1;
        int nearestDistanceSquared = int.MaxValue;
        for (int offsetZ = -searchRadiusCells; offsetZ <= searchRadiusCells; offsetZ++)
        {
            for (int offsetX = -searchRadiusCells; offsetX <= searchRadiusCells; offsetX++)
            {
                int neighbourX = cellX + offsetX;
                int neighbourZ = cellZ + offsetZ;
                if (neighbourX < 0 || neighbourX >= WindowSide || neighbourZ < 0 || neighbourZ >= WindowSide)
                {
                    continue;
                }
                int neighbourIndex = neighbourZ * WindowSide + neighbourX;
                int distanceSquared = offsetX * offsetX + offsetZ * offsetZ;
                if (mask[neighbourIndex] && distanceSquared < nearestDistanceSquared)
                {
                    nearestDistanceSquared = distanceSquared;
                    nearestIndex = neighbourIndex;
                }
            }
        }
        return nearestIndex;
    }

    // Non-fixed cells of a stacked level survive only where the level below is at least StackedSetbackMetres from its own edge.
    private static void ClearBelowStackedSetback(bool[][] masks, int level, bool[] isFixedCell)
    {
        bool[] setbackAllowed = ErodeByDisc(masks[level - 1], WindowSide, StackedSetbackMetres);
        for (int windowIndex = 0; windowIndex < setbackAllowed.Length; windowIndex++)
        {
            if (!isFixedCell[windowIndex] && !setbackAllowed[windowIndex])
            {
                masks[level][windowIndex] = false;
            }
        }
    }

    private static int InsetCellsFromBorder(int coreX, int coreZ)
    {
        return math.min(math.min(coreX, coreZ), math.min(CoreSide - 1 - coreX, CoreSide - 1 - coreZ));
    }

    // storedLevels is -1 where the window cell is inside the tile, or outside it with no neighbour data.
    private static void FillStoredNeighbourWindow(Vector2Int tileCoordinate, int maxLevel, System.Func<Vector2Int, TerrainTileData> neighbourLookup,
        int[] storedLevels, byte[] storedKinds)
    {
        for (int windowIndex = 0; windowIndex < storedLevels.Length; windowIndex++)
        {
            storedLevels[windowIndex] = -1;
        }
        if (neighbourLookup == null)
        {
            return;
        }
        TerrainTileData[] neighbours = new TerrainTileData[9];
        for (int offsetZ = -1; offsetZ <= 1; offsetZ++)
        {
            for (int offsetX = -1; offsetX <= 1; offsetX++)
            {
                if (offsetX == 0 && offsetZ == 0)
                {
                    continue;
                }
                TerrainTileData neighbour = neighbourLookup(new Vector2Int(tileCoordinate.x + offsetX, tileCoordinate.y + offsetZ));
                if (neighbour != null && neighbour.baseTerraceLevels != null)
                {
                    neighbours[(offsetZ + 1) * 3 + offsetX + 1] = neighbour;
                }
            }
        }
        for (int windowZ = 0; windowZ < WindowSide; windowZ++)
        {
            int coreZ = windowZ - WindowMarginCells;
            int offsetZ = coreZ < 0 ? -1 : coreZ >= CoreSide ? 1 : 0;
            for (int windowX = 0; windowX < WindowSide; windowX++)
            {
                int coreX = windowX - WindowMarginCells;
                int offsetX = coreX < 0 ? -1 : coreX >= CoreSide ? 1 : 0;
                TerrainTileData neighbour = neighbours[(offsetZ + 1) * 3 + offsetX + 1];
                if (neighbour == null || (offsetX == 0 && offsetZ == 0))
                {
                    continue;
                }
                int sourceX = coreX - offsetX * CoreSide;
                int sourceZ = coreZ - offsetZ * CoreSide;
                int windowIndex = windowZ * WindowSide + windowX;
                storedLevels[windowIndex] = math.clamp(TerraceLevelField.ResolvedLevel(neighbour, sourceX, sourceZ), 0, maxLevel);
                storedKinds[windowIndex] = neighbour.EffectiveTerraceStepKinds(sourceZ * CoreSide + sourceX);
            }
        }
    }

    // Threshold -> cap -> stored cells pinned -> 8 m set-back -> open+close -> stored cells pinned again. Returns a level per window cell.
    private static int[] BuildCleanedWindowLevels(int worldSeed, Vector2Int tileCoordinate, int rollIndex, IReadOnlyList<TerracePlateauStyle> styles,
        bool useRolledField, int maxLevel, System.Func<Vector2, bool, int> maxLevelAtWorldPoint, int[] storedLevels)
    {
        int cellCount = WindowSide * WindowSide;
        int2 tileCoordinateInt2 = new int2(tileCoordinate.x, tileCoordinate.y);
        float2 windowOriginWorld = new float2(tileCoordinate.x * 100f - 50f - WindowMarginCells, tileCoordinate.y * 100f - 50f - WindowMarginCells);
        float2 worldSeedOffset = TerracePlateauField.WorldSeedOffset(worldSeed);
        TerracePlateauStyle worldStyle = styles[0];

        bool[][] masks = new bool[maxLevel + 1][];
        for (int level = 1; level <= maxLevel; level++)
        {
            masks[level] = new bool[cellCount];
        }
        for (int windowZ = 0; windowZ < WindowSide; windowZ++)
        {
            for (int windowX = 0; windowX < WindowSide; windowX++)
            {
                int windowIndex = windowZ * WindowSide + windowX;
                float2 cellCentreWorld = windowOriginWorld + new float2(windowX + 0.5f, windowZ + 0.5f);
                float fieldValue;
                float firstLevelThreshold;
                float secondLevelThreshold;
                if (useRolledField)
                {
                    fieldValue = TerracePlateauField.SampleTileField(worldSeed, tileCoordinateInt2, rollIndex, styles, cellCentreWorld,
                        out firstLevelThreshold, out secondLevelThreshold);
                }
                else
                {
                    fieldValue = TerracePlateauField.SampleStyle(cellCentreWorld, worldSeedOffset, worldStyle);
                    firstLevelThreshold = worldStyle.firstLevelThreshold;
                    secondLevelThreshold = worldStyle.secondLevelThreshold;
                }
                int cellLevel = fieldValue >= secondLevelThreshold ? 2 : fieldValue >= firstLevelThreshold ? 1 : 0;
                cellLevel = math.min(cellLevel, maxLevel);
                if (maxLevelAtWorldPoint != null)
                {
                    cellLevel = math.min(cellLevel, math.max(0, maxLevelAtWorldPoint(new Vector2(cellCentreWorld.x, cellCentreWorld.y), useRolledField)));
                }
                if (storedLevels[windowIndex] >= 0)
                {
                    cellLevel = storedLevels[windowIndex];
                }
                for (int level = 1; level <= cellLevel; level++)
                {
                    masks[level][windowIndex] = true;
                }
            }
        }

        for (int level = 2; level <= maxLevel; level++)
        {
            bool[] setbackAllowed = ErodeByDisc(masks[level - 1], WindowSide, StackedSetbackMetres);
            AndInPlace(masks[level], setbackAllowed);
        }
        for (int level = 1; level <= maxLevel; level++)
        {
            masks[level] = OpenThenClose(masks[level], WindowSide, CleanupRadiusMetres);
            if (level >= 2)
            {
                AndInPlace(masks[level], masks[level - 1]);
            }
        }

        int[] windowLevels = new int[cellCount];
        for (int windowIndex = 0; windowIndex < cellCount; windowIndex++)
        {
            int cellLevel = 0;
            if (storedLevels[windowIndex] >= 0)
            {
                cellLevel = storedLevels[windowIndex];
            }
            else
            {
                for (int level = 1; level <= maxLevel; level++)
                {
                    if (masks[level][windowIndex])
                    {
                        cellLevel = level;
                    }
                }
            }
            windowLevels[windowIndex] = cellLevel;
        }
        return windowLevels;
    }

    private const byte FixedKindNone = 0;
    private const byte FixedKindHigh = 1;
    private const byte FixedKindLow = 2;

    // Lock-band and stored cells get their kind from the W window levels alone (BuildFixedCellKindsForStep), so no roll reaches them.
    // The combined (rolled) regions then only inherit those kinds; a region joining two different fixed kinds is cut by a channel.
    // Fully interior combined regions use the tile roll. May cut channels in masks.
    private static byte[] AssignWindowStepKinds(int worldSeed, Vector2Int tileCoordinate, int rollIndex, IReadOnlyList<TerracePlateauStyle> styles,
        int maxLevel, bool[][] masks, int[] storedLevels, byte[] storedKinds, bool[] isLockBandCell, int[] worldLevels)
    {
        int cellCount = WindowSide * WindowSide;
        byte[] windowKinds = new byte[cellCount];
        float tileLowShare = 0.5f;
        if (styles != null && styles.Count > 0)
        {
            tileLowShare = styles[TerracePlateauField.StyleIndexFor(worldSeed, new int2(tileCoordinate.x, tileCoordinate.y), rollIndex, styles)].lowStepShare;
        }
        uint tileHash = math.hash(new int4(worldSeed, tileCoordinate.x, tileCoordinate.y, rollIndex));

        bool[] previousLevelIsLow = new bool[cellCount];
        bool[] worldPreviousLevelIsLow = new bool[cellCount];
        bool[] worldMask = new bool[cellCount];
        for (int level = 1; level <= maxLevel; level++)
        {
            for (int windowIndex = 0; windowIndex < cellCount; windowIndex++)
            {
                worldMask[windowIndex] = worldLevels[windowIndex] >= level;
            }
            byte[] fixedCellKinds = BuildFixedCellKindsForStep(worldSeed, tileCoordinate, styles, level, worldMask, storedLevels, storedKinds,
                isLockBandCell, worldPreviousLevelIsLow, out bool[] worldLevelIsLow);
            worldPreviousLevelIsLow = worldLevelIsLow;

            int[] regionLabels = null;
            int regionCount = 0;
            int[] regionFirstCell = null;
            int[] regionArea = null;
            int[] regionFixedLowCell = null;
            int[] regionFixedHighCell = null;
            int channelCutCount = 0;
            while (true)
            {
                regionLabels = LabelRegions(masks[level], WindowSide, out regionCount, out regionFirstCell, out regionArea);
                regionFixedLowCell = new int[regionCount];
                regionFixedHighCell = new int[regionCount];
                for (int region = 0; region < regionCount; region++)
                {
                    regionFixedLowCell[region] = -1;
                    regionFixedHighCell[region] = -1;
                }
                for (int windowIndex = 0; windowIndex < cellCount; windowIndex++)
                {
                    int region = regionLabels[windowIndex];
                    if (region < 0 || fixedCellKinds[windowIndex] == FixedKindNone)
                    {
                        continue;
                    }
                    if (fixedCellKinds[windowIndex] == FixedKindLow)
                    {
                        if (regionFixedLowCell[region] < 0)
                        {
                            regionFixedLowCell[region] = windowIndex;
                        }
                    }
                    else if (regionFixedHighCell[region] < 0)
                    {
                        regionFixedHighCell[region] = windowIndex;
                    }
                }
                int conflictRegion = -1;
                for (int region = 0; region < regionCount; region++)
                {
                    if (regionFixedLowCell[region] >= 0 && regionFixedHighCell[region] >= 0)
                    {
                        conflictRegion = region;
                        break;
                    }
                }
                if (conflictRegion < 0)
                {
                    break;
                }
                bool cutMade = channelCutCount < MaxChannelCutsPerStep
                    && CutLevelZeroChannelBetween(masks, regionLabels, conflictRegion, regionFixedLowCell[conflictRegion], regionFixedHighCell[conflictRegion]);
                if (!cutMade)
                {
                    Debug.LogWarning("TerracePlateauRaster: tile " + tileCoordinate + " step " + level
                        + " joins two fixed (lock band or stored neighbour) regions of different kinds and no channel could separate them; interior cells take the nearest fixed kind.");
                    break;
                }
                channelCutCount++;
                Debug.LogWarning("TerracePlateauRaster: tile " + tileCoordinate + " step " + level
                    + " joined two fixed (lock band or stored neighbour) regions of different kinds; cut a level-0 channel between them.");
            }

            bool[] regionIsLow = new bool[regionCount];
            bool[] regionIsMixed = new bool[regionCount];
            bool anyRegionIsMixed = false;
            for (int region = 0; region < regionCount; region++)
            {
                int lowCell = regionFixedLowCell[region];
                int highCell = regionFixedHighCell[region];
                if (lowCell >= 0 || highCell >= 0)
                {
                    regionIsLow[region] = lowCell >= 0 && (highCell < 0 || lowCell < highCell);
                    regionIsMixed[region] = lowCell >= 0 && highCell >= 0;
                    anyRegionIsMixed |= regionIsMixed[region];
                    continue;
                }
                int firstCell = regionFirstCell[region];
                float tileRoll = RollZeroToOne(tileHash, level, firstCell);
                if (level == 1)
                {
                    float lowChance = regionArea[region] < SmallRegionAreaSquareMetres ? SmallRegionLowChance : tileLowShare;
                    regionIsLow[region] = tileRoll < lowChance;
                }
                else
                {
                    bool enclosingIsLow = previousLevelIsLow[firstCell];
                    regionIsLow[region] = tileRoll < SecondStepOppositeKindChance ? !enclosingIsLow : enclosingIsLow;
                }
            }

            bool[] levelIsLow = new bool[cellCount];
            for (int windowIndex = 0; windowIndex < cellCount; windowIndex++)
            {
                int region = regionLabels[windowIndex];
                if (region < 0)
                {
                    continue;
                }
                // A fixed cell never takes its region's kind, only its own.
                levelIsLow[windowIndex] = fixedCellKinds[windowIndex] != FixedKindNone ? fixedCellKinds[windowIndex] == FixedKindLow : regionIsLow[region];
            }
            if (anyRegionIsMixed)
            {
                AssignNearestFixedKindInMixedRegions(levelIsLow, regionLabels, regionIsMixed, fixedCellKinds);
            }
            for (int windowIndex = 0; windowIndex < cellCount; windowIndex++)
            {
                if (levelIsLow[windowIndex])
                {
                    windowKinds[windowIndex] |= (byte)(1 << (level - 1));
                }
            }
            previousLevelIsLow = levelIsLow;
        }
        return windowKinds;
    }

    // Decides kinds on the W window levels only: a W region holding a stored cell inherits it; else a W region with a lock-band cell
    // hashes its smallest lock-band world cell (WORLD style share, W area; step 2 opposes the enclosing W step-1 kind). Returns a
    // FixedKind per lock-band or stored cell. worldLevelIsLow is the kind of every decided W region, for the next step's enclosing lookup.
    private static byte[] BuildFixedCellKindsForStep(int worldSeed, Vector2Int tileCoordinate, IReadOnlyList<TerracePlateauStyle> styles, int level,
        bool[] worldMask, int[] storedLevels, byte[] storedKinds, bool[] isLockBandCell, bool[] worldPreviousLevelIsLow, out bool[] worldLevelIsLow)
    {
        int cellCount = WindowSide * WindowSide;
        float worldLowShare = styles != null && styles.Count > 0 ? styles[0].lowStepShare : 0.5f;
        int windowOriginCellX = tileCoordinate.x * 100 - 50 - WindowMarginCells;
        int windowOriginCellZ = tileCoordinate.y * 100 - 50 - WindowMarginCells;

        int[] regionLabels = LabelRegions(worldMask, WindowSide, out int regionCount, out int[] _, out int[] regionArea);
        int[] regionStoredLowCell = new int[regionCount];
        int[] regionStoredHighCell = new int[regionCount];
        int[] regionLockBandCell = new int[regionCount];
        for (int region = 0; region < regionCount; region++)
        {
            regionStoredLowCell[region] = -1;
            regionStoredHighCell[region] = -1;
            regionLockBandCell[region] = -1;
        }
        for (int windowIndex = 0; windowIndex < cellCount; windowIndex++)
        {
            int region = regionLabels[windowIndex];
            if (region < 0)
            {
                continue;
            }
            if (storedLevels[windowIndex] >= level)
            {
                bool storedIsLow = ((storedKinds[windowIndex] >> (level - 1)) & 1) != 0;
                if (storedIsLow && regionStoredLowCell[region] < 0)
                {
                    regionStoredLowCell[region] = windowIndex;
                }
                else if (!storedIsLow && regionStoredHighCell[region] < 0)
                {
                    regionStoredHighCell[region] = windowIndex;
                }
            }
            if (isLockBandCell[windowIndex] && regionLockBandCell[region] < 0)
            {
                regionLockBandCell[region] = windowIndex;
            }
        }

        bool[] regionIsDecided = new bool[regionCount];
        bool[] regionIsLow = new bool[regionCount];
        for (int region = 0; region < regionCount; region++)
        {
            int lowCell = regionStoredLowCell[region];
            int highCell = regionStoredHighCell[region];
            int lockBandCell = regionLockBandCell[region];
            if (lowCell >= 0 || highCell >= 0)
            {
                regionIsLow[region] = lowCell >= 0 && (highCell < 0 || lowCell < highCell);
                regionIsDecided[region] = true;
            }
            else if (lockBandCell >= 0)
            {
                float worldRoll = RollFromWorldCell(worldSeed, level, windowOriginCellX + lockBandCell % WindowSide, windowOriginCellZ + lockBandCell / WindowSide);
                if (level == 1)
                {
                    float lowChance = regionArea[region] < SmallRegionAreaSquareMetres ? SmallRegionLowChance : worldLowShare;
                    regionIsLow[region] = worldRoll < lowChance;
                }
                else
                {
                    bool enclosingIsLow = worldPreviousLevelIsLow[lockBandCell];
                    regionIsLow[region] = worldRoll < SecondStepOppositeKindChance ? !enclosingIsLow : enclosingIsLow;
                }
                regionIsDecided[region] = true;
            }
        }

        byte[] fixedCellKinds = new byte[cellCount];
        worldLevelIsLow = new bool[cellCount];
        for (int windowIndex = 0; windowIndex < cellCount; windowIndex++)
        {
            int region = regionLabels[windowIndex];
            if (region < 0 || !regionIsDecided[region])
            {
                continue;
            }
            worldLevelIsLow[windowIndex] = regionIsLow[region];
            if (storedLevels[windowIndex] >= level)
            {
                bool storedIsLow = ((storedKinds[windowIndex] >> (level - 1)) & 1) != 0;
                fixedCellKinds[windowIndex] = storedIsLow ? FixedKindLow : FixedKindHigh;
            }
            else if (isLockBandCell[windowIndex])
            {
                fixedCellKinds[windowIndex] = regionIsLow[region] ? FixedKindLow : FixedKindHigh;
            }
        }
        return fixedCellKinds;
    }

    // Rare fallback: a mixed region no channel could split gives each non-fixed cell the kind of its nearest fixed cell.
    private static void AssignNearestFixedKindInMixedRegions(bool[] levelIsLow, int[] regionLabels, bool[] regionIsMixed, byte[] fixedCellKinds)
    {
        List<int> fixedCells = new List<int>();
        for (int region = 0; region < regionIsMixed.Length; region++)
        {
            if (!regionIsMixed[region])
            {
                continue;
            }
            fixedCells.Clear();
            for (int windowIndex = 0; windowIndex < regionLabels.Length; windowIndex++)
            {
                if (regionLabels[windowIndex] == region && fixedCellKinds[windowIndex] != FixedKindNone)
                {
                    fixedCells.Add(windowIndex);
                }
            }
            for (int windowIndex = 0; windowIndex < regionLabels.Length; windowIndex++)
            {
                if (regionLabels[windowIndex] != region || fixedCellKinds[windowIndex] != FixedKindNone)
                {
                    continue;
                }
                int cellX = windowIndex % WindowSide;
                int cellZ = windowIndex / WindowSide;
                int bestDistanceSquared = int.MaxValue;
                int bestCell = fixedCells[0];
                for (int fixedIndex = 0; fixedIndex < fixedCells.Count; fixedIndex++)
                {
                    int deltaX = fixedCells[fixedIndex] % WindowSide - cellX;
                    int deltaZ = fixedCells[fixedIndex] / WindowSide - cellZ;
                    int distanceSquared = deltaX * deltaX + deltaZ * deltaZ;
                    if (distanceSquared < bestDistanceSquared)
                    {
                        bestDistanceSquared = distanceSquared;
                        bestCell = fixedCells[fixedIndex];
                    }
                }
                levelIsLow[windowIndex] = fixedCellKinds[bestCell] == FixedKindLow;
            }
        }
    }

    // Clears every level on a 6 m band across the region, on the perpendicular bisector of the two cells, at least 8 m inside the tile.
    private static bool CutLevelZeroChannelBetween(bool[][] masks, int[] regionLabels, int region, int firstCell, int secondCell)
    {
        float2 firstPoint = new float2(firstCell % WindowSide, firstCell / WindowSide);
        float2 secondPoint = new float2(secondCell % WindowSide, secondCell / WindowSide);
        float2 midpoint = (firstPoint + secondPoint) * 0.5f;
        float2 direction = math.normalizesafe(secondPoint - firstPoint);
        bool anyCellCut = false;
        for (int windowIndex = 0; windowIndex < regionLabels.Length; windowIndex++)
        {
            if (regionLabels[windowIndex] != region)
            {
                continue;
            }
            int windowX = windowIndex % WindowSide;
            int windowZ = windowIndex / WindowSide;
            int coreX = windowX - WindowMarginCells;
            int coreZ = windowZ - WindowMarginCells;
            if (coreX < 0 || coreX >= CoreSide || coreZ < 0 || coreZ >= CoreSide || InsetCellsFromBorder(coreX, coreZ) < ChannelMinimumInsetCells)
            {
                continue;
            }
            if (math.abs(math.dot(new float2(windowX, windowZ) - midpoint, direction)) > ChannelHalfWidthMetres)
            {
                continue;
            }
            for (int level = 1; level < masks.Length; level++)
            {
                masks[level][windowIndex] = false;
            }
            anyCellCut = true;
        }
        return anyCellCut;
    }

    private static float RollFromWorldCell(int worldSeed, int stepLevel, int worldCellX, int worldCellZ)
    {
        uint hash = math.hash(new int4(worldSeed ^ 0x51ED270B, stepLevel, worldCellX, worldCellZ));
        return (hash >> 8) / 16777216f;
    }

    // Morphological opening then closing of a square mask (side x side) with a disc of radiusCells. NC-D4.
    public static bool[] OpenThenClose(bool[] mask, int side, float radiusCells)
    {
        bool[] opened = DilateByDisc(ErodeByDisc(mask, side, radiusCells), side, radiusCells);
        return ErodeByDisc(DilateByDisc(opened, side, radiusCells), side, radiusCells);
    }

    private const int NoFeatureDistance = int.MaxValue;

    // Exact squared Euclidean distance (cells) from every cell to the nearest set cell: column sweeps, then Felzenszwalb row envelopes.
    // Cells with no set cell anywhere in their column and row envelope get NoFeatureDistance.
    private static int[] SquaredDistanceToNearestSetCell(bool[] featureMask, int side)
    {
        int[] columnDistanceSquared = new int[featureMask.Length];
        for (int cellX = 0; cellX < side; cellX++)
        {
            int runLength = NoFeatureDistance;
            for (int cellZ = 0; cellZ < side; cellZ++)
            {
                int cellIndex = cellZ * side + cellX;
                runLength = featureMask[cellIndex] ? 0 : (runLength == NoFeatureDistance ? NoFeatureDistance : runLength + 1);
                columnDistanceSquared[cellIndex] = runLength;
            }
            runLength = NoFeatureDistance;
            for (int cellZ = side - 1; cellZ >= 0; cellZ--)
            {
                int cellIndex = cellZ * side + cellX;
                runLength = featureMask[cellIndex] ? 0 : (runLength == NoFeatureDistance ? NoFeatureDistance : runLength + 1);
                if (runLength < columnDistanceSquared[cellIndex])
                {
                    columnDistanceSquared[cellIndex] = runLength;
                }
            }
            for (int cellZ = 0; cellZ < side; cellZ++)
            {
                int cellIndex = cellZ * side + cellX;
                int columnDistance = columnDistanceSquared[cellIndex];
                columnDistanceSquared[cellIndex] = columnDistance == NoFeatureDistance ? NoFeatureDistance : columnDistance * columnDistance;
            }
        }

        int[] result = new int[featureMask.Length];
        int[] parabolaVertex = new int[side];
        float[] parabolaStart = new float[side + 1];
        for (int cellZ = 0; cellZ < side; cellZ++)
        {
            int rowOffset = cellZ * side;
            int top = -1;
            for (int cellX = 0; cellX < side; cellX++)
            {
                int cellHeight = columnDistanceSquared[rowOffset + cellX];
                if (cellHeight == NoFeatureDistance)
                {
                    continue;
                }
                float start = float.NegativeInfinity;
                while (top >= 0)
                {
                    int vertex = parabolaVertex[top];
                    start = ((cellHeight + cellX * cellX) - (columnDistanceSquared[rowOffset + vertex] + vertex * vertex)) / (float)(2 * cellX - 2 * vertex);
                    if (start <= parabolaStart[top])
                    {
                        top--;
                        continue;
                    }
                    break;
                }
                if (top < 0)
                {
                    start = float.NegativeInfinity;
                }
                top++;
                parabolaVertex[top] = cellX;
                parabolaStart[top] = start;
            }
            if (top < 0)
            {
                for (int cellX = 0; cellX < side; cellX++)
                {
                    result[rowOffset + cellX] = NoFeatureDistance;
                }
                continue;
            }
            int active = 0;
            for (int cellX = 0; cellX < side; cellX++)
            {
                while (active < top && parabolaStart[active + 1] < cellX)
                {
                    active++;
                }
                int vertex = parabolaVertex[active];
                int deltaX = cellX - vertex;
                result[rowOffset + cellX] = deltaX * deltaX + columnDistanceSquared[rowOffset + vertex];
            }
        }
        return result;
    }

    // Out-of-grid neighbours count as set, so the window edge never eats a mask.
    private static bool[] ErodeByDisc(bool[] mask, int side, float radiusCells)
    {
        bool[] unsetCells = new bool[mask.Length];
        for (int cellIndex = 0; cellIndex < mask.Length; cellIndex++)
        {
            unsetCells[cellIndex] = !mask[cellIndex];
        }
        int[] distanceSquared = SquaredDistanceToNearestSetCell(unsetCells, side);
        float radiusSquared = radiusCells * radiusCells;
        bool[] result = new bool[mask.Length];
        for (int cellIndex = 0; cellIndex < mask.Length; cellIndex++)
        {
            result[cellIndex] = distanceSquared[cellIndex] == NoFeatureDistance || distanceSquared[cellIndex] > radiusSquared;
        }
        return result;
    }

    // Out-of-grid neighbours are ignored.
    private static bool[] DilateByDisc(bool[] mask, int side, float radiusCells)
    {
        int[] distanceSquared = SquaredDistanceToNearestSetCell(mask, side);
        float radiusSquared = radiusCells * radiusCells;
        bool[] result = new bool[mask.Length];
        for (int cellIndex = 0; cellIndex < mask.Length; cellIndex++)
        {
            result[cellIndex] = distanceSquared[cellIndex] != NoFeatureDistance && distanceSquared[cellIndex] <= radiusSquared;
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

    // Regions touching the window edge are kept (their true area is unknown), as are regions holding a lock-band or stored neighbour cell
    // (touchesWindowEdge doubles as "never cull"); regions with no core cell are left alone.
    private static void CullSmallRegionsTouchingCore(bool[] mask, int windowSide, float minAreaSquareMetres, bool[] protectedCells)
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
                if (cellX == 0 || cellZ == 0 || cellX == windowSide - 1 || cellZ == windowSide - 1 || protectedCells[cellZ * windowSide + cellX])
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
