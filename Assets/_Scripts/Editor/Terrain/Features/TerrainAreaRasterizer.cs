#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public struct TerrainAreaStamp
{
    public Vector2[] outlineXZ;
    public float edgeNoiseMetres;
    public int seed;
    public int priority;
    public byte areaIndex;
    public bool paintsGround;
    public TerrainSplatLayer groundLayer;
}

// Pure functions of (areas, tile). Edge noise is sampled in world coordinates so neighbouring tiles agree on a shared edge.
public static class TerrainAreaRasterizer
{
    public const byte NoArea = 0;
    public const float EdgeDistanceRangeMetres = 32f;

    private const float TileSizeMetres = TerrainTileData.TileSizeMetres;
    private const float EdgeNoiseFrequencyPerMetre = 0.35f;
    private const float SeedOffsetScale = 0.37f;

    public static void RasteriseAreaMask(IReadOnlyList<TerrainAreaStamp> areas, Vector2Int tileCoordinate, int resolution, byte[] ownerAreaIndexOut, byte[] edgeDistanceOut)
    {
        int texelCount = resolution * resolution;
        if (ownerAreaIndexOut == null || ownerAreaIndexOut.Length != texelCount
            || edgeDistanceOut == null || edgeDistanceOut.Length != texelCount)
        {
            throw new ArgumentException("Output arrays must be resolution * resolution bytes.");
        }

        Array.Clear(ownerAreaIndexOut, 0, texelCount);
        Array.Clear(edgeDistanceOut, 0, texelCount);
        if (areas == null || resolution < 1)
        {
            return;
        }

        float tileMinX = tileCoordinate.x * TileSizeMetres - TileSizeMetres * 0.5f;
        float tileMinZ = tileCoordinate.y * TileSizeMetres - TileSizeMetres * 0.5f;
        float texelSizeMetres = TileSizeMetres / resolution;
        Bounds tileBounds = new Bounds(
            new Vector3(tileCoordinate.x * TileSizeMetres, 0f, tileCoordinate.y * TileSizeMetres),
            new Vector3(TileSizeMetres, 100000f, TileSizeMetres));
        int[] ownerPriority = new int[texelCount];

        for (int areaListIndex = 0; areaListIndex < areas.Count; areaListIndex++)
        {
            TerrainAreaStamp area = areas[areaListIndex];
            if (area.outlineXZ == null || area.outlineXZ.Length < 3)
            {
                continue;
            }

            Bounds areaBounds = BoundsOf(area);
            if (!areaBounds.Intersects(tileBounds))
            {
                continue;
            }

            int minTexelX = Mathf.Max(0, Mathf.FloorToInt((areaBounds.min.x - tileMinX) / texelSizeMetres - 0.5f));
            int maxTexelX = Mathf.Min(resolution - 1, Mathf.CeilToInt((areaBounds.max.x - tileMinX) / texelSizeMetres - 0.5f));
            int minTexelZ = Mathf.Max(0, Mathf.FloorToInt((areaBounds.min.z - tileMinZ) / texelSizeMetres - 0.5f));
            int maxTexelZ = Mathf.Min(resolution - 1, Mathf.CeilToInt((areaBounds.max.z - tileMinZ) / texelSizeMetres - 0.5f));

            for (int texelZ = minTexelZ; texelZ <= maxTexelZ; texelZ++)
            {
                float worldZ = tileMinZ + (texelZ + 0.5f) * texelSizeMetres;
                for (int texelX = minTexelX; texelX <= maxTexelX; texelX++)
                {
                    float worldX = tileMinX + (texelX + 0.5f) * texelSizeMetres;
                    float signedDistance = SignedDistanceToEdgeMetres(area, worldX, worldZ);
                    if (signedDistance <= 0f)
                    {
                        continue;
                    }

                    int texelIndex = texelZ * resolution + texelX;
                    bool texelUnowned = ownerAreaIndexOut[texelIndex] == NoArea;
                    if (texelUnowned || area.priority >= ownerPriority[texelIndex])
                    {
                        ownerAreaIndexOut[texelIndex] = area.areaIndex;
                        ownerPriority[texelIndex] = area.priority;
                        edgeDistanceOut[texelIndex] = (byte)Mathf.RoundToInt(Mathf.Clamp01(signedDistance / EdgeDistanceRangeMetres) * 255f);
                    }
                }
            }
        }
    }

    public static float SignedDistanceToEdgeMetres(in TerrainAreaStamp area, float worldX, float worldZ)
    {
        Vector2[] outline = area.outlineXZ;
        if (outline == null || outline.Length < 3)
        {
            return -float.MaxValue;
        }

        float minDistanceSquared = float.PositiveInfinity;
        bool inside = false;
        int previousIndex = outline.Length - 1;
        for (int vertexIndex = 0; vertexIndex < outline.Length; vertexIndex++)
        {
            Vector2 segmentStart = outline[previousIndex];
            Vector2 segmentEnd = outline[vertexIndex];

            Vector2 segment = segmentEnd - segmentStart;
            float segmentLengthSquared = segment.sqrMagnitude;
            float fraction = segmentLengthSquared > 0f
                ? Mathf.Clamp01(((worldX - segmentStart.x) * segment.x + (worldZ - segmentStart.y) * segment.y) / segmentLengthSquared)
                : 0f;
            float deltaX = worldX - (segmentStart.x + segment.x * fraction);
            float deltaZ = worldZ - (segmentStart.y + segment.y * fraction);
            minDistanceSquared = Mathf.Min(minDistanceSquared, deltaX * deltaX + deltaZ * deltaZ);

            bool straddlesRay = (segmentStart.y > worldZ) != (segmentEnd.y > worldZ);
            if (straddlesRay)
            {
                float crossingX = segmentStart.x + (worldZ - segmentStart.y) / (segmentEnd.y - segmentStart.y) * segment.x;
                if (worldX < crossingX)
                {
                    inside = !inside;
                }
            }
            previousIndex = vertexIndex;
        }

        float distance = Mathf.Sqrt(minDistanceSquared);
        float signedDistance = inside ? distance : -distance;
        if (area.edgeNoiseMetres == 0f)
        {
            return signedDistance;
        }

        // Seed shifts the sample position in world space, so tiles still agree while seeds differ.
        uint seedHash = math.hash(new int2(area.seed, 0x5bd1e995));
        float2 seedOffset = new float2(seedHash & 0xFFFFu, seedHash >> 16) * SeedOffsetScale;
        float2 samplePoint = new float2(worldX, worldZ) * EdgeNoiseFrequencyPerMetre + seedOffset;
        return signedDistance + noise.snoise(samplePoint) * area.edgeNoiseMetres;
    }

    public static Bounds BoundsOf(in TerrainAreaStamp area)
    {
        if (area.outlineXZ == null || area.outlineXZ.Length == 0)
        {
            return new Bounds(Vector3.zero, Vector3.zero);
        }

        Vector2 minimum = area.outlineXZ[0];
        Vector2 maximum = area.outlineXZ[0];
        for (int vertexIndex = 1; vertexIndex < area.outlineXZ.Length; vertexIndex++)
        {
            minimum = Vector2.Min(minimum, area.outlineXZ[vertexIndex]);
            maximum = Vector2.Max(maximum, area.outlineXZ[vertexIndex]);
        }

        float expansion = Mathf.Abs(area.edgeNoiseMetres);
        Bounds bounds = new Bounds();
        bounds.SetMinMax(
            new Vector3(minimum.x - expansion, -50000f, minimum.y - expansion),
            new Vector3(maximum.x + expansion, 50000f, maximum.y + expansion));
        return bounds;
    }
}
#endif
