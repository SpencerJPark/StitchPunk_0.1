#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public struct TerrainRiverStamp
{
    public Vector3[] points;
    public float[] widths;
    public float[] waterLevels;
    public float bedDepthMetres;
    public float bankSlopeMetres;
    public float shallowBandMetres;
    public float deepBlocksBelowDepthMetres;
}

// Pure functions of (rivers, tile). Water levels are authored once per river so every tile agrees on the surface.
public static class TerrainRiverBuilder
{
    private const int SamplesPerSide = TerrainTileData.HeightSamplesPerSide;
    private const float TileSizeMetres = TerrainTileData.TileSizeMetres;
    private const float WaterBelowTerrainMetres = 0.3f;
    private const float MinimumWaterDepthMetres = 0.02f;
    private const float RibbonUvMetresPerV = 10f;

    public static float[] ComputeWaterLevels(Vector3[] points, float[] terrainHeightAtPoints, float bedDepthMetres, float startWaterLevelMetres)
    {
        if (points == null || terrainHeightAtPoints == null || points.Length == 0 || terrainHeightAtPoints.Length < points.Length)
        {
            return new float[0];
        }

        float[] levels = new float[points.Length];
        levels[0] = float.IsNaN(startWaterLevelMetres) ? terrainHeightAtPoints[0] - WaterBelowTerrainMetres : startWaterLevelMetres;
        for (int pointIndex = 1; pointIndex < points.Length; pointIndex++)
        {
            levels[pointIndex] = Mathf.Min(levels[pointIndex - 1], terrainHeightAtPoints[pointIndex] - WaterBelowTerrainMetres);
        }
        return levels;
    }

    public static void ApplyRiverCarve(IReadOnlyList<TerrainRiverStamp> rivers, Vector2Int tileCoordinate, float[] terrainHeightsWithoutRiver, float[] riverDeltaOut)
    {
        if (riverDeltaOut == null || riverDeltaOut.Length < SamplesPerSide * SamplesPerSide)
        {
            return;
        }

        System.Array.Clear(riverDeltaOut, 0, SamplesPerSide * SamplesPerSide);
        if (rivers == null || terrainHeightsWithoutRiver == null || terrainHeightsWithoutRiver.Length < SamplesPerSide * SamplesPerSide)
        {
            return;
        }

        float minX = tileCoordinate.x * TileSizeMetres - TileSizeMetres * 0.5f;
        float minZ = tileCoordinate.y * TileSizeMetres - TileSizeMetres * 0.5f;
        Rect tileRect = new Rect(minX, minZ, TileSizeMetres, TileSizeMetres);

        for (int riverIndex = 0; riverIndex < rivers.Count; riverIndex++)
        {
            TerrainRiverStamp river = rivers[riverIndex];
            if (!IsValid(river) || !OverlapsTile(river, tileRect))
            {
                continue;
            }

            float reach = MaxHalfWidth(river) + Mathf.Max(0f, river.bankSlopeMetres);
            for (int sampleZ = 0; sampleZ < SamplesPerSide; sampleZ++)
            {
                for (int sampleX = 0; sampleX < SamplesPerSide; sampleX++)
                {
                    float worldX = minX + sampleX;
                    float worldZ = minZ + sampleZ;
                    float distance;
                    int segmentIndex;
                    float segmentT;
                    if (!TryFindNearest(river.points, worldX, worldZ, reach, out distance, out segmentIndex, out segmentT))
                    {
                        continue;
                    }

                    float halfWidth = Mathf.Lerp(river.widths[segmentIndex], river.widths[segmentIndex + 1], segmentT) * 0.5f;
                    float bedHeight = Mathf.Lerp(river.waterLevels[segmentIndex], river.waterLevels[segmentIndex + 1], segmentT) - river.bedDepthMetres;
                    int heightIndex = sampleZ * SamplesPerSide + sampleX;
                    float terrainHeight = terrainHeightsWithoutRiver[heightIndex];

                    float target;
                    if (distance <= halfWidth)
                    {
                        target = bedHeight;
                    }
                    else if (river.bankSlopeMetres > 0f && distance < halfWidth + river.bankSlopeMetres)
                    {
                        float blend = Mathf.SmoothStep(0f, 1f, (distance - halfWidth) / river.bankSlopeMetres);
                        target = Mathf.Lerp(bedHeight, terrainHeight, blend);
                    }
                    else
                    {
                        continue;
                    }

                    float delta = Mathf.Min(0f, target - terrainHeight);
                    if (delta < riverDeltaOut[heightIndex])
                    {
                        riverDeltaOut[heightIndex] = delta;
                    }
                }
            }
        }
    }

    public static void RasteriseSurfaceMasks(IReadOnlyList<TerrainRiverStamp> rivers, Vector2Int tileCoordinate, int resolution, float[] groundHeightsAfterCarve, byte[] shallowOut, byte[] deepOut)
    {
        int texelCount = resolution * resolution;
        if (resolution < 1 || shallowOut == null || deepOut == null || shallowOut.Length < texelCount || deepOut.Length < texelCount)
        {
            return;
        }

        System.Array.Clear(shallowOut, 0, texelCount);
        System.Array.Clear(deepOut, 0, texelCount);
        if (rivers == null || groundHeightsAfterCarve == null || groundHeightsAfterCarve.Length < SamplesPerSide * SamplesPerSide)
        {
            return;
        }

        float minX = tileCoordinate.x * TileSizeMetres - TileSizeMetres * 0.5f;
        float minZ = tileCoordinate.y * TileSizeMetres - TileSizeMetres * 0.5f;
        float texelSize = TileSizeMetres / resolution;
        Rect tileRect = new Rect(minX, minZ, TileSizeMetres, TileSizeMetres);

        for (int riverIndex = 0; riverIndex < rivers.Count; riverIndex++)
        {
            TerrainRiverStamp river = rivers[riverIndex];
            if (!IsValid(river) || !OverlapsTile(river, tileRect))
            {
                continue;
            }

            float reach = MaxHalfWidth(river) + Mathf.Max(0f, river.bankSlopeMetres);
            for (int texelZ = 0; texelZ < resolution; texelZ++)
            {
                for (int texelX = 0; texelX < resolution; texelX++)
                {
                    float worldX = minX + (texelX + 0.5f) * texelSize;
                    float worldZ = minZ + (texelZ + 0.5f) * texelSize;
                    float distance;
                    int segmentIndex;
                    float segmentT;
                    if (!TryFindNearest(river.points, worldX, worldZ, reach, out distance, out segmentIndex, out segmentT))
                    {
                        continue;
                    }

                    float halfWidth = Mathf.Lerp(river.widths[segmentIndex], river.widths[segmentIndex + 1], segmentT) * 0.5f;
                    if (distance > halfWidth + river.bankSlopeMetres)
                    {
                        continue;
                    }

                    float waterLevel = Mathf.Lerp(river.waterLevels[segmentIndex], river.waterLevels[segmentIndex + 1], segmentT);
                    float depth = waterLevel - BilinearGround(groundHeightsAfterCarve, worldX - minX, worldZ - minZ);
                    if (depth <= MinimumWaterDepthMetres)
                    {
                        continue;
                    }

                    int texelIndex = texelZ * resolution + texelX;
                    if (depth > river.deepBlocksBelowDepthMetres && distance < halfWidth - river.shallowBandMetres)
                    {
                        deepOut[texelIndex] = 255;
                        shallowOut[texelIndex] = 0;
                    }
                    else if (deepOut[texelIndex] == 0)
                    {
                        shallowOut[texelIndex] = 255;
                    }
                }
            }
        }
    }

    public static Mesh BuildRibbonMeshForTile(TerrainRiverStamp river, Vector2Int tileCoordinate, float surfaceOffsetMetres)
    {
        if (!IsValid(river))
        {
            return null;
        }

        int pointCount = river.points.Length;
        int segmentCount = pointCount - 1;
        Vector2[] segmentPerpendiculars = new Vector2[segmentCount];
        float[] cumulativeLength = new float[pointCount];
        for (int segmentIndex = 0; segmentIndex < segmentCount; segmentIndex++)
        {
            Vector2 direction = new Vector2(
                river.points[segmentIndex + 1].x - river.points[segmentIndex].x,
                river.points[segmentIndex + 1].z - river.points[segmentIndex].z);
            float length = direction.magnitude;
            cumulativeLength[segmentIndex + 1] = cumulativeLength[segmentIndex] + length;
            segmentPerpendiculars[segmentIndex] = length > 1e-5f ? new Vector2(-direction.y, direction.x) / length : Vector2.left;
        }

        int[] pointVertexBase = new int[pointCount];
        for (int pointIndex = 0; pointIndex < pointCount; pointIndex++)
        {
            pointVertexBase[pointIndex] = -1;
        }

        List<Vector3> positions = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<int> triangles = new List<int>();
        float tileCentreX = tileCoordinate.x * TileSizeMetres;
        float tileCentreZ = tileCoordinate.y * TileSizeMetres;

        for (int segmentIndex = 0; segmentIndex < segmentCount; segmentIndex++)
        {
            float midX = (river.points[segmentIndex].x + river.points[segmentIndex + 1].x) * 0.5f;
            float midZ = (river.points[segmentIndex].z + river.points[segmentIndex + 1].z) * 0.5f;
            if (Mathf.RoundToInt(midX / TileSizeMetres) != tileCoordinate.x || Mathf.RoundToInt(midZ / TileSizeMetres) != tileCoordinate.y)
            {
                continue;
            }

            for (int endOffset = 0; endOffset < 2; endOffset++)
            {
                int pointIndex = segmentIndex + endOffset;
                if (pointVertexBase[pointIndex] >= 0)
                {
                    continue;
                }

                Vector2 perpendicular = segmentPerpendiculars[Mathf.Min(pointIndex, segmentCount - 1)];
                if (pointIndex > 0 && pointIndex < segmentCount)
                {
                    Vector2 averaged = segmentPerpendiculars[pointIndex - 1] + segmentPerpendiculars[pointIndex];
                    if (averaged.sqrMagnitude > 1e-6f)
                    {
                        perpendicular = averaged.normalized;
                    }
                }

                float halfWidth = river.widths[pointIndex] * 0.5f;
                float localX = river.points[pointIndex].x - tileCentreX;
                float localZ = river.points[pointIndex].z - tileCentreZ;
                float surfaceHeight = river.waterLevels[pointIndex] + surfaceOffsetMetres;
                float flowV = cumulativeLength[pointIndex] / RibbonUvMetresPerV;

                pointVertexBase[pointIndex] = positions.Count;
                positions.Add(new Vector3(localX + perpendicular.x * halfWidth, surfaceHeight, localZ + perpendicular.y * halfWidth));
                uvs.Add(new Vector2(0f, flowV));
                positions.Add(new Vector3(localX - perpendicular.x * halfWidth, surfaceHeight, localZ - perpendicular.y * halfWidth));
                uvs.Add(new Vector2(1f, flowV));
            }

            int leftStart = pointVertexBase[segmentIndex];
            int leftEnd = pointVertexBase[segmentIndex + 1];
            triangles.Add(leftStart);
            triangles.Add(leftEnd);
            triangles.Add(leftStart + 1);
            triangles.Add(leftStart + 1);
            triangles.Add(leftEnd);
            triangles.Add(leftEnd + 1);
        }

        if (triangles.Count == 0)
        {
            return null;
        }

        Vector3[] normals = new Vector3[positions.Count];
        for (int normalIndex = 0; normalIndex < normals.Length; normalIndex++)
        {
            normals[normalIndex] = Vector3.up;
        }

        Mesh mesh = new Mesh();
        mesh.name = "River_" + tileCoordinate.x + "_" + tileCoordinate.y;
        mesh.indexFormat = positions.Count <= 65535 ? IndexFormat.UInt16 : IndexFormat.UInt32;
        mesh.SetVertices(positions);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    public static Bounds BoundsOf(TerrainRiverStamp river)
    {
        if (!IsValid(river))
        {
            return new Bounds(Vector3.zero, Vector3.zero);
        }

        Vector3 minimum = river.points[0];
        Vector3 maximum = river.points[0];
        float minimumLevel = river.waterLevels[0];
        float maximumLevel = river.waterLevels[0];
        for (int pointIndex = 1; pointIndex < river.points.Length; pointIndex++)
        {
            minimum = Vector3.Min(minimum, river.points[pointIndex]);
            maximum = Vector3.Max(maximum, river.points[pointIndex]);
            minimumLevel = Mathf.Min(minimumLevel, river.waterLevels[pointIndex]);
            maximumLevel = Mathf.Max(maximumLevel, river.waterLevels[pointIndex]);
        }

        float horizontalExpand = MaxHalfWidth(river) + river.bankSlopeMetres;
        minimum = new Vector3(minimum.x - horizontalExpand, minimumLevel - river.bedDepthMetres - 1f, minimum.z - horizontalExpand);
        maximum = new Vector3(maximum.x + horizontalExpand, maximumLevel + river.bedDepthMetres + 1f, maximum.z + horizontalExpand);
        Bounds bounds = new Bounds();
        bounds.SetMinMax(minimum, maximum);
        return bounds;
    }

    private static bool IsValid(TerrainRiverStamp river)
    {
        return river.points != null && river.widths != null && river.waterLevels != null
            && river.points.Length >= 2
            && river.widths.Length == river.points.Length
            && river.waterLevels.Length == river.points.Length;
    }

    private static float MaxHalfWidth(TerrainRiverStamp river)
    {
        float maximum = 0f;
        for (int pointIndex = 0; pointIndex < river.widths.Length; pointIndex++)
        {
            maximum = Mathf.Max(maximum, river.widths[pointIndex]);
        }
        return maximum * 0.5f;
    }

    private static bool OverlapsTile(TerrainRiverStamp river, Rect tileRect)
    {
        Bounds bounds = BoundsOf(river);
        return bounds.max.x >= tileRect.xMin && bounds.min.x <= tileRect.xMax
            && bounds.max.z >= tileRect.yMin && bounds.min.z <= tileRect.yMax;
    }

    // Segments whose reach-expanded AABB misses the query point are skipped without a distance test.
    private static bool TryFindNearest(Vector3[] points, float queryX, float queryZ, float reach, out float nearestDistance, out int nearestSegment, out float nearestT)
    {
        float bestSquared = reach * reach;
        bool found = false;
        nearestSegment = 0;
        nearestT = 0f;
        for (int segmentIndex = 0; segmentIndex < points.Length - 1; segmentIndex++)
        {
            Vector3 start = points[segmentIndex];
            Vector3 end = points[segmentIndex + 1];
            if (queryX < Mathf.Min(start.x, end.x) - reach || queryX > Mathf.Max(start.x, end.x) + reach
                || queryZ < Mathf.Min(start.z, end.z) - reach || queryZ > Mathf.Max(start.z, end.z) + reach)
            {
                continue;
            }

            float segmentX = end.x - start.x;
            float segmentZ = end.z - start.z;
            float lengthSquared = segmentX * segmentX + segmentZ * segmentZ;
            float segmentFraction = lengthSquared > 1e-8f
                ? Mathf.Clamp01(((queryX - start.x) * segmentX + (queryZ - start.z) * segmentZ) / lengthSquared)
                : 0f;
            float offsetX = queryX - (start.x + segmentX * segmentFraction);
            float offsetZ = queryZ - (start.z + segmentZ * segmentFraction);
            float distanceSquared = offsetX * offsetX + offsetZ * offsetZ;
            if (distanceSquared <= bestSquared)
            {
                bestSquared = distanceSquared;
                nearestSegment = segmentIndex;
                nearestT = segmentFraction;
                found = true;
            }
        }

        nearestDistance = found ? Mathf.Sqrt(bestSquared) : float.MaxValue;
        return found;
    }

    private static float BilinearGround(float[] heights, float localX, float localZ)
    {
        float maxLocal = SamplesPerSide - 1;
        localX = Mathf.Clamp(localX, 0f, maxLocal);
        localZ = Mathf.Clamp(localZ, 0f, maxLocal);
        int lowX = Mathf.Min((int)localX, SamplesPerSide - 2);
        int lowZ = Mathf.Min((int)localZ, SamplesPerSide - 2);
        float fractionX = localX - lowX;
        float fractionZ = localZ - lowZ;
        float lower = Mathf.Lerp(heights[lowZ * SamplesPerSide + lowX], heights[lowZ * SamplesPerSide + lowX + 1], fractionX);
        float upper = Mathf.Lerp(heights[(lowZ + 1) * SamplesPerSide + lowX], heights[(lowZ + 1) * SamplesPerSide + lowX + 1], fractionX);
        return Mathf.Lerp(lower, upper, fractionZ);
    }
}
#endif
