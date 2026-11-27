#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

// Section 8 shape metrics M1-M6 for one tile, measured on the smooth outline the mesh builder will draw.
public struct TerracePlateauTileMetrics
{
    public float totalOutlineMetres;
    public float axisAlignedOutlineShare;        // M1: share of outline length within 5 degrees of a world axis
    public float longestStraightStretchMetres;   // M2: longest stretch that turns less than 5 degrees
    public List<float> regionCompactness;        // M3: 4*pi*area / perimeter^2 per connected region (level >= 1)
    public int neckOrGapViolationCells;          // M4: cells changed by OpenThenClose at CleanupRadiusMetres
    public float terracedAreaShare;              // M5: share of cells at level >= 1
    public float lowStepOutlineMetres;           // M6 numerator: outline length of Low steps
}

public static class TerracePlateauMetrics
{
    private const int CellsPerSide = 100;
    private const int SamplesPerMetre = 4;
    private const float StraightToleranceDegrees = 5f;
    private const float EndpointJoinScale = 1e4f;
    private const float BorderToleranceMetres = 0.01f;
    private const float MinimumRegionAreaSquareMetres = 50f;
    private const float InsideProbeMetres = 0.3f;
    private const float OpenEdgeSegmentMarginMetres = 1.5f;
    private const int WindowMarginCells = 8;

    private struct OutlineSegment
    {
        public Vector2 start;
        public Vector2 end;
        public Vector2 insideDirection;
        public float Length { get { return (end - start).magnitude; } }
        public Vector2 Midpoint { get { return (start + end) * 0.5f; } }
    }

    public static TerracePlateauTileMetrics Measure(TerrainTileData data, Func<Vector2Int, TerrainTileData> neighbourLookup,
        float cornerRadiusMetres, int maxLevel)
    {
        TerracePlateauTileMetrics metrics = new TerracePlateauTileMetrics();
        metrics.regionCompactness = new List<float>();

        int[] levels = BuildLevelGrid(data);
        bool[] neighbourMissing = BuildNeighbourMissingTable(data, neighbourLookup);
        int[] windowLevels = BuildWindowLevels(data, neighbourLookup);
        List<OutlineSegment> firstLevelSegments = new List<OutlineSegment>();
        float axisAlignedLength = 0f;

        for (int threshold = 1; threshold <= maxLevel; threshold++)
        {
            float[] field = TerraceLevelField.BuildSignedDistance(data, threshold, cornerRadiusMetres, neighbourLookup, SamplesPerMetre);
            List<OutlineSegment> segments = ExtractSegments(field);
            segments.RemoveAll(segment => IsNearOpenEdge(segment.Midpoint, OpenEdgeSegmentMarginMetres, neighbourMissing));

            for (int segmentIndex = 0; segmentIndex < segments.Count; segmentIndex++)
            {
                OutlineSegment segment = segments[segmentIndex];
                float length = segment.Length;
                metrics.totalOutlineMetres += length;
                if (IsAxisAligned(segment.end - segment.start))
                {
                    axisAlignedLength += length;
                }
                if (IsHillStepSegment(data, segment, threshold))
                {
                    metrics.lowStepOutlineMetres += length;
                }
            }

            metrics.longestStraightStretchMetres = Mathf.Max(metrics.longestStraightStretchMetres, LongestStraightStretch(segments));
            metrics.neckOrGapViolationCells += CountCleanupChanges(windowLevels, threshold, neighbourMissing);
            if (threshold == 1)
            {
                firstLevelSegments = segments;
            }
        }

        metrics.axisAlignedOutlineShare = metrics.totalOutlineMetres > 0f ? axisAlignedLength / metrics.totalOutlineMetres : 0f;
        metrics.terracedAreaShare = CountTerracedCells(levels) / (float)(CellsPerSide * CellsPerSide);
        AppendRegionCompactness(levels, firstLevelSegments, metrics.regionCompactness);
        return metrics;
    }

    private static int[] BuildLevelGrid(TerrainTileData data)
    {
        int[] levels = new int[CellsPerSide * CellsPerSide];
        for (int cellZ = 0; cellZ < CellsPerSide; cellZ++)
        {
            for (int cellX = 0; cellX < CellsPerSide; cellX++)
            {
                levels[cellZ * CellsPerSide + cellX] = TerraceLevelField.ResolvedLevel(data, cellX, cellZ);
            }
        }
        return levels;
    }

    private static int CountTerracedCells(int[] levels)
    {
        int count = 0;
        for (int cellIndex = 0; cellIndex < levels.Length; cellIndex++)
        {
            if (levels[cellIndex] >= 1)
            {
                count++;
            }
        }
        return count;
    }

    private static bool IsAxisAligned(Vector2 direction)
    {
        float angleFromXAxisDegrees = Mathf.Atan2(Mathf.Abs(direction.y), Mathf.Abs(direction.x)) * Mathf.Rad2Deg;
        return angleFromXAxisDegrees < StraightToleranceDegrees || angleFromXAxisDegrees > 90f - StraightToleranceDegrees;
    }

    private static bool IsHillStepSegment(TerrainTileData data, OutlineSegment segment, int signedStep)
    {
        Vector2 probe = segment.Midpoint + segment.insideDirection * InsideProbeMetres;
        int cellX = Mathf.FloorToInt(probe.x);
        int cellZ = Mathf.FloorToInt(probe.y);
        if (cellX < 0 || cellX >= CellsPerSide || cellZ < 0 || cellZ >= CellsPerSide)
        {
            return false;
        }
        byte stepKinds = data.EffectiveTerraceStepKinds(cellZ * CellsPerSide + cellX);
        return TerrainTileData.IsHillStep(stepKinds, signedStep);
    }

    // Index (offsetZ + 1) * 3 + offsetX + 1; true where the neighbour tile is absent (the world edge).
    private static bool[] BuildNeighbourMissingTable(TerrainTileData data, Func<Vector2Int, TerrainTileData> neighbourLookup)
    {
        bool[] missing = new bool[9];
        for (int offsetZ = -1; offsetZ <= 1; offsetZ++)
        {
            for (int offsetX = -1; offsetX <= 1; offsetX++)
            {
                if (offsetX == 0 && offsetZ == 0)
                {
                    continue;
                }
                missing[(offsetZ + 1) * 3 + offsetX + 1] = neighbourLookup == null
                    || neighbourLookup(data.coordinate + new Vector2Int(offsetX, offsetZ)) == null;
            }
        }
        return missing;
    }

    private static bool IsNearOpenEdge(Vector2 pointMetres, float marginMetres, bool[] neighbourMissing)
    {
        float tileMetres = CellsPerSide;
        int offsetX = pointMetres.x < marginMetres ? -1 : (pointMetres.x > tileMetres - marginMetres ? 1 : 0);
        int offsetZ = pointMetres.y < marginMetres ? -1 : (pointMetres.y > tileMetres - marginMetres ? 1 : 0);
        if (offsetX == 0 && offsetZ == 0)
        {
            return false;
        }
        return (offsetX != 0 && neighbourMissing[1 * 3 + offsetX + 1])
            || (offsetZ != 0 && neighbourMissing[(offsetZ + 1) * 3 + 1])
            || (offsetX != 0 && offsetZ != 0 && neighbourMissing[(offsetZ + 1) * 3 + offsetX + 1]);
    }

    private static int[] BuildWindowLevels(TerrainTileData data, Func<Vector2Int, TerrainTileData> neighbourLookup)
    {
        int windowSide = CellsPerSide + 2 * WindowMarginCells;
        int[] windowLevels = new int[windowSide * windowSide];
        for (int windowZ = 0; windowZ < windowSide; windowZ++)
        {
            int worldCellZ = windowZ - WindowMarginCells;
            int offsetZ = worldCellZ < 0 ? -1 : (worldCellZ >= CellsPerSide ? 1 : 0);
            for (int windowX = 0; windowX < windowSide; windowX++)
            {
                int worldCellX = windowX - WindowMarginCells;
                int offsetX = worldCellX < 0 ? -1 : (worldCellX >= CellsPerSide ? 1 : 0);
                TerrainTileData source = data;
                int localX = worldCellX;
                int localZ = worldCellZ;
                if (offsetX != 0 || offsetZ != 0)
                {
                    TerrainTileData neighbour = neighbourLookup != null
                        ? neighbourLookup(data.coordinate + new Vector2Int(offsetX, offsetZ))
                        : null;
                    if (neighbour != null)
                    {
                        source = neighbour;
                        localX = worldCellX - CellsPerSide * offsetX;
                        localZ = worldCellZ - CellsPerSide * offsetZ;
                    }
                    else
                    {
                        localX = Mathf.Clamp(worldCellX, 0, CellsPerSide - 1);
                        localZ = Mathf.Clamp(worldCellZ, 0, CellsPerSide - 1);
                    }
                }
                windowLevels[windowZ * windowSide + windowX] = TerraceLevelField.ResolvedLevel(source, localX, localZ);
            }
        }
        return windowLevels;
    }

    private static int CountCleanupChanges(int[] windowLevels, int threshold, bool[] neighbourMissing)
    {
        int windowSide = CellsPerSide + 2 * WindowMarginCells;
        bool[] mask = new bool[windowLevels.Length];
        for (int cellIndex = 0; cellIndex < windowLevels.Length; cellIndex++)
        {
            mask[cellIndex] = windowLevels[cellIndex] >= threshold;
        }

        bool[] cleaned = TerracePlateauRaster.OpenThenClose(mask, windowSide, TerracePlateauRaster.CleanupRadiusMetres);
        int changed = 0;
        for (int coreZ = 0; coreZ < CellsPerSide; coreZ++)
        {
            for (int coreX = 0; coreX < CellsPerSide; coreX++)
            {
                if (IsNearOpenEdge(new Vector2(coreX + 0.5f, coreZ + 0.5f), TerracePlateauRaster.CleanupRadiusMetres, neighbourMissing))
                {
                    continue;
                }
                int windowIndex = (coreZ + WindowMarginCells) * windowSide + coreX + WindowMarginCells;
                if (cleaned[windowIndex] != mask[windowIndex])
                {
                    changed++;
                }
            }
        }
        return changed;
    }

    private static List<OutlineSegment> ExtractSegments(float[] field)
    {
        int side = CellsPerSide * SamplesPerMetre + 1;
        List<OutlineSegment> segments = new List<OutlineSegment>();
        Vector2[] crossings = new Vector2[4];

        for (int sampleZ = 0; sampleZ < side - 1; sampleZ++)
        {
            for (int sampleX = 0; sampleX < side - 1; sampleX++)
            {
                float valueLowLow = field[sampleZ * side + sampleX];
                float valueHighLow = field[sampleZ * side + sampleX + 1];
                float valueHighHigh = field[(sampleZ + 1) * side + sampleX + 1];
                float valueLowHigh = field[(sampleZ + 1) * side + sampleX];

                int crossingCount = 0;
                Vector2 cornerLowLow = SampleToMetres(sampleX, sampleZ);
                Vector2 cornerHighLow = SampleToMetres(sampleX + 1, sampleZ);
                Vector2 cornerHighHigh = SampleToMetres(sampleX + 1, sampleZ + 1);
                Vector2 cornerLowHigh = SampleToMetres(sampleX, sampleZ + 1);

                TryAddCrossing(cornerLowLow, valueLowLow, cornerHighLow, valueHighLow, crossings, ref crossingCount);
                TryAddCrossing(cornerHighLow, valueHighLow, cornerHighHigh, valueHighHigh, crossings, ref crossingCount);
                TryAddCrossing(cornerLowHigh, valueLowHigh, cornerHighHigh, valueHighHigh, crossings, ref crossingCount);
                TryAddCrossing(cornerLowLow, valueLowLow, cornerLowHigh, valueLowHigh, crossings, ref crossingCount);
                if (crossingCount < 2)
                {
                    continue;
                }

                Vector2 gradient = new Vector2(
                    (valueHighLow + valueHighHigh) - (valueLowLow + valueLowHigh),
                    (valueLowHigh + valueHighHigh) - (valueLowLow + valueHighLow));
                Vector2 insideDirection = gradient.sqrMagnitude > 1e-12f ? gradient.normalized : Vector2.zero;

                segments.Add(new OutlineSegment { start = crossings[0], end = crossings[1], insideDirection = insideDirection });
                if (crossingCount == 4)
                {
                    segments.Add(new OutlineSegment { start = crossings[2], end = crossings[3], insideDirection = insideDirection });
                }
            }
        }
        return segments;
    }

    private static Vector2 SampleToMetres(int sampleX, int sampleZ)
    {
        return new Vector2(sampleX / (float)SamplesPerMetre, sampleZ / (float)SamplesPerMetre);
    }

    // Corners are passed in a fixed low-to-high order so the two cells sharing an edge compute the same point bit for bit.
    private static void TryAddCrossing(Vector2 cornerA, float valueA, Vector2 cornerB, float valueB, Vector2[] crossings, ref int crossingCount)
    {
        if ((valueA > 0f) == (valueB > 0f))
        {
            return;
        }
        float blend = valueA / (valueA - valueB);
        crossings[crossingCount] = Vector2.Lerp(cornerA, cornerB, blend);
        crossingCount++;
    }

    private static long JoinKey(Vector2 point)
    {
        long quantizedX = (long)Mathf.Round(point.x * EndpointJoinScale);
        long quantizedZ = (long)Mathf.Round(point.y * EndpointJoinScale);
        return quantizedX * 4000003L + quantizedZ;
    }

    private static float LongestStraightStretch(List<OutlineSegment> segments)
    {
        Dictionary<long, List<int>> segmentsByEndpoint = new Dictionary<long, List<int>>();
        for (int segmentIndex = 0; segmentIndex < segments.Count; segmentIndex++)
        {
            AddEndpoint(segmentsByEndpoint, JoinKey(segments[segmentIndex].start), segmentIndex);
            AddEndpoint(segmentsByEndpoint, JoinKey(segments[segmentIndex].end), segmentIndex);
        }

        bool[] visited = new bool[segments.Count];
        float longest = 0f;
        for (int seedIndex = 0; seedIndex < segments.Count; seedIndex++)
        {
            if (visited[seedIndex])
            {
                continue;
            }
            visited[seedIndex] = true;
            List<Vector2> forwardPoints = new List<Vector2>();
            List<Vector2> backwardPoints = new List<Vector2>();
            ExtendChain(segments, segmentsByEndpoint, visited, segments[seedIndex].end, forwardPoints);
            ExtendChain(segments, segmentsByEndpoint, visited, segments[seedIndex].start, backwardPoints);

            List<Vector2> polyline = new List<Vector2>();
            for (int backIndex = backwardPoints.Count - 1; backIndex >= 0; backIndex--)
            {
                polyline.Add(backwardPoints[backIndex]);
            }
            polyline.Add(segments[seedIndex].start);
            polyline.Add(segments[seedIndex].end);
            polyline.AddRange(forwardPoints);

            Vector2 first = polyline[0];
            Vector2 last = polyline[polyline.Count - 1];
            bool isClosed = JoinKey(first) == JoinKey(last);
            if (!isClosed && IsOnTileBorder(first) && IsOnTileBorder(last))
            {
                continue;
            }
            longest = Mathf.Max(longest, LongestStraightWindow(polyline));
        }
        return longest;
    }

    private static void AddEndpoint(Dictionary<long, List<int>> segmentsByEndpoint, long key, int segmentIndex)
    {
        List<int> list;
        if (!segmentsByEndpoint.TryGetValue(key, out list))
        {
            list = new List<int>(2);
            segmentsByEndpoint[key] = list;
        }
        list.Add(segmentIndex);
    }

    // Appends each newly reached far endpoint; the seed's own endpoint is already marked visited via its segment.
    private static void ExtendChain(List<OutlineSegment> segments, Dictionary<long, List<int>> segmentsByEndpoint,
        bool[] visited, Vector2 tip, List<Vector2> appendedPoints)
    {
        while (true)
        {
            List<int> candidates;
            if (!segmentsByEndpoint.TryGetValue(JoinKey(tip), out candidates))
            {
                return;
            }
            int nextIndex = -1;
            for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
            {
                if (!visited[candidates[candidateIndex]])
                {
                    nextIndex = candidates[candidateIndex];
                    break;
                }
            }
            if (nextIndex < 0)
            {
                return;
            }
            visited[nextIndex] = true;
            OutlineSegment next = segments[nextIndex];
            tip = JoinKey(next.start) == JoinKey(tip) ? next.end : next.start;
            appendedPoints.Add(tip);
        }
    }

    private static bool IsOnTileBorder(Vector2 point)
    {
        float tileMetres = CellsPerSide;
        return point.x < BorderToleranceMetres || point.x > tileMetres - BorderToleranceMetres
            || point.y < BorderToleranceMetres || point.y > tileMetres - BorderToleranceMetres;
    }

    private static float LongestStraightWindow(List<Vector2> polyline)
    {
        int segmentCount = polyline.Count - 1;
        float longest = 0f;
        for (int startIndex = 0; startIndex < segmentCount; startIndex++)
        {
            Vector2 startHeading = polyline[startIndex + 1] - polyline[startIndex];
            if (startHeading.sqrMagnitude < 1e-12f)
            {
                continue;
            }
            float windowLength = 0f;
            for (int endIndex = startIndex; endIndex < segmentCount; endIndex++)
            {
                Vector2 heading = polyline[endIndex + 1] - polyline[endIndex];
                if (heading.sqrMagnitude < 1e-12f)
                {
                    continue;
                }
                if (Mathf.Abs(Vector2.Angle(startHeading, heading)) > StraightToleranceDegrees)
                {
                    break;
                }
                windowLength += heading.magnitude;
            }
            longest = Mathf.Max(longest, windowLength);
        }
        return longest;
    }

    private static void AppendRegionCompactness(int[] levels, List<OutlineSegment> firstLevelSegments, List<float> compactness)
    {
        int[] regionIds = new int[levels.Length];
        Array.Fill(regionIds, -1);
        List<int> regionAreas = new List<int>();
        List<bool> regionTouchesBorder = new List<bool>();
        Stack<int> pending = new Stack<int>();

        for (int cellIndex = 0; cellIndex < levels.Length; cellIndex++)
        {
            if (levels[cellIndex] < 1 || regionIds[cellIndex] >= 0)
            {
                continue;
            }
            int regionId = regionAreas.Count;
            int area = 0;
            bool touchesBorder = false;
            regionIds[cellIndex] = regionId;
            pending.Push(cellIndex);
            while (pending.Count > 0)
            {
                int current = pending.Pop();
                int currentX = current % CellsPerSide;
                int currentZ = current / CellsPerSide;
                area++;
                if (currentX == 0 || currentZ == 0 || currentX == CellsPerSide - 1 || currentZ == CellsPerSide - 1)
                {
                    touchesBorder = true;
                }
                TryPushNeighbour(levels, regionIds, pending, currentX - 1, currentZ, regionId);
                TryPushNeighbour(levels, regionIds, pending, currentX + 1, currentZ, regionId);
                TryPushNeighbour(levels, regionIds, pending, currentX, currentZ - 1, regionId);
                TryPushNeighbour(levels, regionIds, pending, currentX, currentZ + 1, regionId);
            }
            regionAreas.Add(area);
            regionTouchesBorder.Add(touchesBorder);
        }

        float[] perimeters = new float[regionAreas.Count];
        for (int segmentIndex = 0; segmentIndex < firstLevelSegments.Count; segmentIndex++)
        {
            OutlineSegment segment = firstLevelSegments[segmentIndex];
            int regionId = NearestRegionId(regionIds, segment.Midpoint);
            if (regionId >= 0)
            {
                perimeters[regionId] += segment.Length;
            }
        }

        for (int regionId = 0; regionId < regionAreas.Count; regionId++)
        {
            if (regionTouchesBorder[regionId] || regionAreas[regionId] < MinimumRegionAreaSquareMetres || perimeters[regionId] <= 0f)
            {
                continue;
            }
            compactness.Add(4f * Mathf.PI * regionAreas[regionId] / (perimeters[regionId] * perimeters[regionId]));
        }
    }

    private static void TryPushNeighbour(int[] levels, int[] regionIds, Stack<int> pending, int cellX, int cellZ, int regionId)
    {
        if (cellX < 0 || cellX >= CellsPerSide || cellZ < 0 || cellZ >= CellsPerSide)
        {
            return;
        }
        int cellIndex = cellZ * CellsPerSide + cellX;
        if (levels[cellIndex] < 1 || regionIds[cellIndex] >= 0)
        {
            return;
        }
        regionIds[cellIndex] = regionId;
        pending.Push(cellIndex);
    }

    private static int NearestRegionId(int[] regionIds, Vector2 point)
    {
        int centreX = Mathf.Clamp(Mathf.FloorToInt(point.x), 0, CellsPerSide - 1);
        int centreZ = Mathf.Clamp(Mathf.FloorToInt(point.y), 0, CellsPerSide - 1);
        int bestRegion = -1;
        float bestDistanceSquared = float.MaxValue;
        for (int offsetZ = -1; offsetZ <= 1; offsetZ++)
        {
            for (int offsetX = -1; offsetX <= 1; offsetX++)
            {
                int cellX = centreX + offsetX;
                int cellZ = centreZ + offsetZ;
                if (cellX < 0 || cellX >= CellsPerSide || cellZ < 0 || cellZ >= CellsPerSide)
                {
                    continue;
                }
                int regionId = regionIds[cellZ * CellsPerSide + cellX];
                if (regionId < 0)
                {
                    continue;
                }
                float nearestX = Mathf.Clamp(point.x, cellX, cellX + 1);
                float nearestZ = Mathf.Clamp(point.y, cellZ, cellZ + 1);
                float distanceSquared = (nearestX - point.x) * (nearestX - point.x) + (nearestZ - point.y) * (nearestZ - point.y);
                if (distanceSquared < bestDistanceSquared)
                {
                    bestDistanceSquared = distanceSquared;
                    bestRegion = regionId;
                }
            }
        }
        return bestRegion;
    }
}
#endif
