#if UNITY_EDITOR
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public struct TerrainRoadStamp
{
    public Vector3[] points;
    public float widthMetres;
    public float shoulderMetres;
    public float edgeNoiseMetres;
    public float carveShoulderMetres;
    public bool carveHeight;
    public AnimationCurve carveBlendCurve;
    public TerrainSplatLayer surfaceLayer;
    public int priority;
}

// Pure functions of (roads, tile). Edge noise is sampled in world coordinates so neighbouring tiles agree on a shared edge.
public static class TerrainRoadRasterizer
{
    private const int SamplesPerSide = TerrainTileData.HeightSamplesPerSide;
    private const float TileSizeMetres = TerrainTileData.TileSizeMetres;
    private const float EdgeNoiseFrequencyPerMetre = 0.7f;
    private const byte NoOwner = 255;
    private const byte OwnerCoverageThreshold = 128;

    public static byte[] RasteriseRoadMask(IReadOnlyList<TerrainRoadStamp> roads, Vector2Int tileCoordinate, int resolution)
    {
        if (resolution < 1)
        {
            return new byte[0];
        }

        byte[] coverage = new byte[resolution * resolution];
        byte[] ownerRoadIndex = new byte[resolution * resolution];
        RasteriseRoadMask(roads, tileCoordinate, resolution, coverage, ownerRoadIndex);
        return coverage;
    }

    public static void RasteriseRoadMask(IReadOnlyList<TerrainRoadStamp> roads, Vector2Int tileCoordinate, int resolution, byte[] coverageOut, byte[] ownerRoadIndexOut)
    {
        if (resolution < 1)
        {
            return;
        }

        int texelCount = resolution * resolution;
        if (coverageOut != null && coverageOut.Length >= texelCount)
        {
            System.Array.Clear(coverageOut, 0, texelCount);
        }
        else
        {
            return;
        }

        bool trackOwner = ownerRoadIndexOut != null && ownerRoadIndexOut.Length >= texelCount;
        int[] ownerPriority = null;
        if (trackOwner)
        {
            for (int texelIndex = 0; texelIndex < texelCount; texelIndex++)
            {
                ownerRoadIndexOut[texelIndex] = NoOwner;
            }
            ownerPriority = new int[texelCount];
        }

        if (roads == null)
        {
            return;
        }

        float tileMinX = tileCoordinate.x * TileSizeMetres - TileSizeMetres * 0.5f;
        float tileMinZ = tileCoordinate.y * TileSizeMetres - TileSizeMetres * 0.5f;
        float texelSizeMetres = TileSizeMetres / resolution;
        Bounds tileBounds = new Bounds(
            new Vector3(tileCoordinate.x * TileSizeMetres, 0f, tileCoordinate.y * TileSizeMetres),
            new Vector3(TileSizeMetres, 100000f, TileSizeMetres));
        float[] minDistanceSquared = new float[texelCount];

        for (int roadIndex = 0; roadIndex < roads.Count; roadIndex++)
        {
            TerrainRoadStamp road = roads[roadIndex];
            if (road.points == null || road.points.Length == 0)
            {
                continue;
            }

            Bounds roadBounds = BoundsOf(road);
            roadBounds.Expand(new Vector3(0f, 100000f, 0f));
            if (!roadBounds.Intersects(tileBounds))
            {
                continue;
            }

            float halfWidth = road.widthMetres * 0.5f;
            float reach = halfWidth + Mathf.Abs(road.edgeNoiseMetres) + texelSizeMetres;
            for (int texelIndex = 0; texelIndex < texelCount; texelIndex++)
            {
                minDistanceSquared[texelIndex] = float.PositiveInfinity;
            }

            int segmentCount = Mathf.Max(1, road.points.Length - 1);
            for (int segmentIndex = 0; segmentIndex < segmentCount; segmentIndex++)
            {
                Vector3 segmentStart = road.points[segmentIndex];
                Vector3 segmentEnd = road.points[Mathf.Min(segmentIndex + 1, road.points.Length - 1)];
                int minTexelX = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(segmentStart.x, segmentEnd.x) - reach - tileMinX) / texelSizeMetres - 0.5f));
                int maxTexelX = Mathf.Min(resolution - 1, Mathf.CeilToInt((Mathf.Max(segmentStart.x, segmentEnd.x) + reach - tileMinX) / texelSizeMetres - 0.5f));
                int minTexelZ = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(segmentStart.z, segmentEnd.z) - reach - tileMinZ) / texelSizeMetres - 0.5f));
                int maxTexelZ = Mathf.Min(resolution - 1, Mathf.CeilToInt((Mathf.Max(segmentStart.z, segmentEnd.z) + reach - tileMinZ) / texelSizeMetres - 0.5f));

                for (int texelZ = minTexelZ; texelZ <= maxTexelZ; texelZ++)
                {
                    float worldZ = tileMinZ + (texelZ + 0.5f) * texelSizeMetres;
                    for (int texelX = minTexelX; texelX <= maxTexelX; texelX++)
                    {
                        float worldX = tileMinX + (texelX + 0.5f) * texelSizeMetres;
                        float distanceSquared = DistanceSquaredToSegment(worldX, worldZ, segmentStart, segmentEnd, out float unusedFraction);
                        int texelIndex = texelZ * resolution + texelX;
                        if (distanceSquared < minDistanceSquared[texelIndex])
                        {
                            minDistanceSquared[texelIndex] = distanceSquared;
                        }
                    }
                }
            }

            for (int texelZ = 0; texelZ < resolution; texelZ++)
            {
                float worldZ = tileMinZ + (texelZ + 0.5f) * texelSizeMetres;
                for (int texelX = 0; texelX < resolution; texelX++)
                {
                    int texelIndex = texelZ * resolution + texelX;
                    float distanceSquared = minDistanceSquared[texelIndex];
                    if (float.IsPositiveInfinity(distanceSquared))
                    {
                        continue;
                    }

                    float worldX = tileMinX + (texelX + 0.5f) * texelSizeMetres;
                    float noisyDistance = Mathf.Sqrt(distanceSquared)
                        + noise.snoise(new float2(worldX, worldZ) * EdgeNoiseFrequencyPerMetre) * road.edgeNoiseMetres;
                    float roadCoverage = Mathf.Clamp01((halfWidth - noisyDistance) / texelSizeMetres + 0.5f);
                    byte coverageByte = (byte)Mathf.RoundToInt(roadCoverage * 255f);
                    if (coverageByte > coverageOut[texelIndex])
                    {
                        coverageOut[texelIndex] = coverageByte;
                    }

                    if (trackOwner && coverageByte >= OwnerCoverageThreshold
                        && (ownerRoadIndexOut[texelIndex] == NoOwner || road.priority > ownerPriority[texelIndex]))
                    {
                        ownerRoadIndexOut[texelIndex] = (byte)Mathf.Min(roadIndex, NoOwner - 1);
                        ownerPriority[texelIndex] = road.priority;
                    }
                }
            }
        }
    }

    public static void ApplyHeightCarve(IReadOnlyList<TerrainRoadStamp> roads, Vector2Int tileCoordinate, float[] terrainHeightsWithoutRoad, float[] roadDeltaOut)
    {
        int sampleCount = SamplesPerSide * SamplesPerSide;
        if (roadDeltaOut == null || roadDeltaOut.Length < sampleCount)
        {
            return;
        }

        System.Array.Clear(roadDeltaOut, 0, sampleCount);
        if (roads == null || terrainHeightsWithoutRoad == null || terrainHeightsWithoutRoad.Length < sampleCount)
        {
            return;
        }

        List<int> roadOrder = new List<int>(roads.Count);
        for (int roadIndex = 0; roadIndex < roads.Count; roadIndex++)
        {
            roadOrder.Add(roadIndex);
        }
        // Insertion sort keeps equal priorities in input order.
        for (int sortIndex = 1; sortIndex < roadOrder.Count; sortIndex++)
        {
            int moving = roadOrder[sortIndex];
            int slot = sortIndex - 1;
            while (slot >= 0 && roads[roadOrder[slot]].priority > roads[moving].priority)
            {
                roadOrder[slot + 1] = roadOrder[slot];
                slot--;
            }
            roadOrder[slot + 1] = moving;
        }

        float tileMinX = tileCoordinate.x * TileSizeMetres - TileSizeMetres * 0.5f;
        float tileMinZ = tileCoordinate.y * TileSizeMetres - TileSizeMetres * 0.5f;

        for (int orderIndex = 0; orderIndex < roadOrder.Count; orderIndex++)
        {
            TerrainRoadStamp road = roads[roadOrder[orderIndex]];
            if (!road.carveHeight || road.points == null || road.points.Length == 0)
            {
                continue;
            }

            float halfWidth = road.widthMetres * 0.5f;
            float carveShoulder = Mathf.Max(0f, road.carveShoulderMetres);
            float reach = halfWidth + carveShoulder;
            float[] bestDistanceSquared = new float[sampleCount];
            float[] bestBedHeight = new float[sampleCount];
            for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
            {
                bestDistanceSquared[sampleIndex] = float.PositiveInfinity;
            }

            int segmentCount = Mathf.Max(1, road.points.Length - 1);
            for (int segmentIndex = 0; segmentIndex < segmentCount; segmentIndex++)
            {
                Vector3 segmentStart = road.points[segmentIndex];
                Vector3 segmentEnd = road.points[Mathf.Min(segmentIndex + 1, road.points.Length - 1)];
                int minSampleX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(segmentStart.x, segmentEnd.x) - reach - tileMinX));
                int maxSampleX = Mathf.Min(SamplesPerSide - 1, Mathf.CeilToInt(Mathf.Max(segmentStart.x, segmentEnd.x) + reach - tileMinX));
                int minSampleZ = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(segmentStart.z, segmentEnd.z) - reach - tileMinZ));
                int maxSampleZ = Mathf.Min(SamplesPerSide - 1, Mathf.CeilToInt(Mathf.Max(segmentStart.z, segmentEnd.z) + reach - tileMinZ));

                for (int sampleZ = minSampleZ; sampleZ <= maxSampleZ; sampleZ++)
                {
                    for (int sampleX = minSampleX; sampleX <= maxSampleX; sampleX++)
                    {
                        float distanceSquared = DistanceSquaredToSegment(tileMinX + sampleX, tileMinZ + sampleZ, segmentStart, segmentEnd, out float fraction);
                        int sampleIndex = sampleZ * SamplesPerSide + sampleX;
                        if (distanceSquared < bestDistanceSquared[sampleIndex])
                        {
                            bestDistanceSquared[sampleIndex] = distanceSquared;
                            bestBedHeight[sampleIndex] = Mathf.Lerp(segmentStart.y, segmentEnd.y, fraction);
                        }
                    }
                }
            }

            for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
            {
                if (float.IsPositiveInfinity(bestDistanceSquared[sampleIndex]))
                {
                    continue;
                }

                float distance = Mathf.Sqrt(bestDistanceSquared[sampleIndex]);
                float weight;
                if (distance <= halfWidth)
                {
                    weight = 1f;
                }
                else if (distance <= reach && carveShoulder > 0f)
                {
                    float shoulderFraction = (distance - halfWidth) / carveShoulder;
                    weight = road.carveBlendCurve != null
                        ? Mathf.Clamp01(road.carveBlendCurve.Evaluate(shoulderFraction))
                        : 1f - shoulderFraction;
                    weight = Mathf.Clamp01(weight);
                }
                else
                {
                    continue;
                }

                float targetDelta = bestBedHeight[sampleIndex] - terrainHeightsWithoutRoad[sampleIndex];
                roadDeltaOut[sampleIndex] = Mathf.Lerp(roadDeltaOut[sampleIndex], targetDelta, weight);
            }
        }
    }

    public static void SmoothPointHeights(Vector3[] points, float windowMetres)
    {
        if (points == null || points.Length < 2 || windowMetres <= 0f)
        {
            return;
        }

        float[] arcLengths = new float[points.Length];
        for (int pointIndex = 1; pointIndex < points.Length; pointIndex++)
        {
            float deltaX = points[pointIndex].x - points[pointIndex - 1].x;
            float deltaZ = points[pointIndex].z - points[pointIndex - 1].z;
            arcLengths[pointIndex] = arcLengths[pointIndex - 1] + Mathf.Sqrt(deltaX * deltaX + deltaZ * deltaZ);
        }

        float[] originalHeights = new float[points.Length];
        for (int pointIndex = 0; pointIndex < points.Length; pointIndex++)
        {
            originalHeights[pointIndex] = points[pointIndex].y;
        }

        float halfWindow = windowMetres * 0.5f;
        for (int pointIndex = 0; pointIndex < points.Length; pointIndex++)
        {
            float heightSum = 0f;
            int contributorCount = 0;
            for (int otherIndex = 0; otherIndex < points.Length; otherIndex++)
            {
                if (Mathf.Abs(arcLengths[otherIndex] - arcLengths[pointIndex]) <= halfWindow)
                {
                    heightSum += originalHeights[otherIndex];
                    contributorCount++;
                }
            }

            Vector3 point = points[pointIndex];
            point.y = heightSum / contributorCount;
            points[pointIndex] = point;
        }
    }

    public static Bounds BoundsOf(TerrainRoadStamp road)
    {
        if (road.points == null || road.points.Length == 0)
        {
            return new Bounds(Vector3.zero, Vector3.zero);
        }

        Vector3 minimum = road.points[0];
        Vector3 maximum = road.points[0];
        for (int pointIndex = 1; pointIndex < road.points.Length; pointIndex++)
        {
            minimum = Vector3.Min(minimum, road.points[pointIndex]);
            maximum = Vector3.Max(maximum, road.points[pointIndex]);
        }

        float expansion = road.widthMetres * 0.5f + Mathf.Max(road.carveShoulderMetres, road.shoulderMetres) + road.edgeNoiseMetres;
        minimum -= new Vector3(expansion, 1f, expansion);
        maximum += new Vector3(expansion, 1f, expansion);
        Bounds bounds = new Bounds();
        bounds.SetMinMax(minimum, maximum);
        return bounds;
    }

    private static float DistanceSquaredToSegment(float pointX, float pointZ, Vector3 segmentStart, Vector3 segmentEnd, out float fraction)
    {
        float segmentX = segmentEnd.x - segmentStart.x;
        float segmentZ = segmentEnd.z - segmentStart.z;
        float lengthSquared = segmentX * segmentX + segmentZ * segmentZ;
        fraction = 0f;
        if (lengthSquared > 1e-12f)
        {
            fraction = Mathf.Clamp01(((pointX - segmentStart.x) * segmentX + (pointZ - segmentStart.z) * segmentZ) / lengthSquared);
        }

        float offsetX = pointX - (segmentStart.x + segmentX * fraction);
        float offsetZ = pointZ - (segmentStart.z + segmentZ * fraction);
        return offsetX * offsetX + offsetZ * offsetZ;
    }
}
#endif
