#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

public struct HeightBrushDirtyRegion
{
    public TerrainTileData tile;
    public RectInt sampleRect;
}

// Hand sculpting writes only handHeightDelta. Every op reads from a snapshot before writing so the
// result never depends on iteration order, and a shared edge sample gets one value in every tile (TT-D7).
public static class TerrainHeightBrush
{
    private const float MaximumRadiusMetres = 100f;
    private const int TileSpan = 100;
    private const int TileHalfSpan = 50;

    private delegate float DeltaComputation(int worldX, int worldZ, HeightSnapshot snapshot);

    private sealed class HeightSnapshot
    {
        public int originX;
        public int originZ;
        public int width;
        public int depth;
        public float[] finalHeights;
        public float[] handDeltas;
        public bool[] exists;

        public bool TryGet(int worldX, int worldZ, out float finalHeight, out float handDelta)
        {
            int localX = worldX - originX;
            int localZ = worldZ - originZ;
            finalHeight = 0f;
            handDelta = 0f;
            if (localX < 0 || localX >= width || localZ < 0 || localZ >= depth)
            {
                return false;
            }
            int index = localZ * width + localX;
            if (!exists[index])
            {
                return false;
            }
            finalHeight = finalHeights[index];
            handDelta = handDeltas[index];
            return true;
        }
    }

    public static float Falloff(float distanceMetres, float radiusMetres, float hardness)
    {
        if (radiusMetres <= 0f || distanceMetres >= radiusMetres)
        {
            return 0f;
        }
        float innerRadius = Mathf.Clamp01(hardness) * radiusMetres;
        if (distanceMetres <= innerRadius)
        {
            return 1f;
        }
        float progress = (distanceMetres - innerRadius) / (radiusMetres - innerRadius);
        return 1f - progress * progress * (3f - 2f * progress);
    }

    public static List<HeightBrushDirtyRegion> Raise(Vector2 centreWorldXZ, float radiusMetres, float hardness, float strengthMetres, Func<Vector2Int, TerrainTileData> tileLookup)
    {
        return ApplyDisc(centreWorldXZ, radiusMetres, hardness, tileLookup,
            (float weight, float finalHeight, float handDelta, int worldX, int worldZ, HeightSnapshot snapshot) =>
                handDelta + strengthMetres * weight);
    }

    public static List<HeightBrushDirtyRegion> Lower(Vector2 centreWorldXZ, float radiusMetres, float hardness, float strengthMetres, Func<Vector2Int, TerrainTileData> tileLookup)
    {
        return ApplyDisc(centreWorldXZ, radiusMetres, hardness, tileLookup,
            (float weight, float finalHeight, float handDelta, int worldX, int worldZ, HeightSnapshot snapshot) =>
                handDelta - strengthMetres * weight);
    }

    public static List<HeightBrushDirtyRegion> Smooth(Vector2 centreWorldXZ, float radiusMetres, float hardness, float strength01, Func<Vector2Int, TerrainTileData> tileLookup)
    {
        float clampedStrength = Mathf.Clamp01(strength01);
        return ApplyDisc(centreWorldXZ, radiusMetres, hardness, tileLookup,
            (float weight, float finalHeight, float handDelta, int worldX, int worldZ, HeightSnapshot snapshot) =>
            {
                float sum = 0f;
                int count = 0;
                for (int offsetZ = -1; offsetZ <= 1; offsetZ++)
                {
                    for (int offsetX = -1; offsetX <= 1; offsetX++)
                    {
                        float neighbourHeight;
                        float unusedDelta;
                        if (snapshot.TryGet(worldX + offsetX, worldZ + offsetZ, out neighbourHeight, out unusedDelta))
                        {
                            sum += neighbourHeight;
                            count++;
                        }
                    }
                }
                float average = count > 0 ? sum / count : finalHeight;
                return handDelta + (average - finalHeight) * weight * clampedStrength;
            });
    }

    public static List<HeightBrushDirtyRegion> Flatten(Vector2 centreWorldXZ, float radiusMetres, float hardness, float strength01, float targetHeightMetres, Func<Vector2Int, TerrainTileData> tileLookup)
    {
        float clampedStrength = Mathf.Clamp01(strength01);
        return ApplyDisc(centreWorldXZ, radiusMetres, hardness, tileLookup,
            (float weight, float finalHeight, float handDelta, int worldX, int worldZ, HeightSnapshot snapshot) =>
            {
                float desiredDelta = targetHeightMetres - (finalHeight - handDelta);
                return Mathf.Lerp(handDelta, desiredDelta, weight * clampedStrength);
            });
    }

    public static List<HeightBrushDirtyRegion> Noise(Vector2 centreWorldXZ, float radiusMetres, float hardness, int seed, float amplitudeMetres, float frequencyPerMetre, Func<Vector2Int, TerrainTileData> tileLookup)
    {
        Unity.Mathematics.float2 seedOffset = new Unity.Mathematics.float2(seed * 12.9898f, seed * 78.233f) % 1000f;
        return ApplyDisc(centreWorldXZ, radiusMetres, hardness, tileLookup,
            (float weight, float finalHeight, float handDelta, int worldX, int worldZ, HeightSnapshot snapshot) =>
            {
                Unity.Mathematics.float2 samplePoint = new Unity.Mathematics.float2(worldX, worldZ) * frequencyPerMetre + seedOffset;
                return handDelta + amplitudeMetres * Unity.Mathematics.noise.snoise(samplePoint) * weight;
            });
    }

    public static List<HeightBrushDirtyRegion> Ramp(Vector2 startWorldXZ, Vector2 endWorldXZ, float widthMetres, float hardness, float strength01, Func<Vector2Int, TerrainTileData> tileLookup)
    {
        float halfWidth = Mathf.Min(widthMetres * 0.5f, MaximumRadiusMetres);
        float clampedStrength = Mathf.Clamp01(strength01);
        Vector2 segment = endWorldXZ - startWorldXZ;
        float segmentLengthSquared = segment.sqrMagnitude;

        int minX = Mathf.FloorToInt(Mathf.Min(startWorldXZ.x, endWorldXZ.x) - halfWidth);
        int maxX = Mathf.CeilToInt(Mathf.Max(startWorldXZ.x, endWorldXZ.x) + halfWidth);
        int minZ = Mathf.FloorToInt(Mathf.Min(startWorldXZ.y, endWorldXZ.y) - halfWidth);
        int maxZ = Mathf.CeilToInt(Mathf.Max(startWorldXZ.y, endWorldXZ.y) + halfWidth);

        Dictionary<Vector2Int, TerrainTileData> tileCache = new Dictionary<Vector2Int, TerrainTileData>();
        HeightSnapshot snapshot = BuildSnapshot(minX, maxX, minZ, maxZ, tileLookup, tileCache);

        float startHeight;
        float endHeight;
        float unusedDelta;
        snapshot.TryGet(Mathf.RoundToInt(startWorldXZ.x), Mathf.RoundToInt(startWorldXZ.y), out startHeight, out unusedDelta);
        snapshot.TryGet(Mathf.RoundToInt(endWorldXZ.x), Mathf.RoundToInt(endWorldXZ.y), out endHeight, out unusedDelta);

        return ApplyComputation(minX, maxX, minZ, maxZ, snapshot, tileCache,
            (int worldX, int worldZ, HeightSnapshot activeSnapshot) =>
            {
                float finalHeight;
                float handDelta;
                if (!activeSnapshot.TryGet(worldX, worldZ, out finalHeight, out handDelta))
                {
                    return float.NaN;
                }
                Vector2 samplePoint = new Vector2(worldX, worldZ);
                float along = segmentLengthSquared > 0f
                    ? Mathf.Clamp01(Vector2.Dot(samplePoint - startWorldXZ, segment) / segmentLengthSquared)
                    : 0f;
                float distanceFromSegment = Vector2.Distance(samplePoint, startWorldXZ + segment * along);
                float weight = Falloff(distanceFromSegment, halfWidth, hardness);
                if (weight <= 0f)
                {
                    return float.NaN;
                }
                float targetHeight = Mathf.Lerp(startHeight, endHeight, along);
                float desiredDelta = targetHeight - (finalHeight - handDelta);
                return Mathf.Lerp(handDelta, desiredDelta, weight * clampedStrength);
            });
    }

    private delegate float DiscDelta(float weight, float finalHeight, float handDelta, int worldX, int worldZ, HeightSnapshot snapshot);

    private static List<HeightBrushDirtyRegion> ApplyDisc(Vector2 centreWorldXZ, float radiusMetres, float hardness, Func<Vector2Int, TerrainTileData> tileLookup, DiscDelta discDelta)
    {
        float radius = Mathf.Min(radiusMetres, MaximumRadiusMetres);
        int minX = Mathf.FloorToInt(centreWorldXZ.x - radius);
        int maxX = Mathf.CeilToInt(centreWorldXZ.x + radius);
        int minZ = Mathf.FloorToInt(centreWorldXZ.y - radius);
        int maxZ = Mathf.CeilToInt(centreWorldXZ.y + radius);

        Dictionary<Vector2Int, TerrainTileData> tileCache = new Dictionary<Vector2Int, TerrainTileData>();
        HeightSnapshot snapshot = BuildSnapshot(minX, maxX, minZ, maxZ, tileLookup, tileCache);

        return ApplyComputation(minX, maxX, minZ, maxZ, snapshot, tileCache,
            (int worldX, int worldZ, HeightSnapshot activeSnapshot) =>
            {
                float finalHeight;
                float handDelta;
                if (!activeSnapshot.TryGet(worldX, worldZ, out finalHeight, out handDelta))
                {
                    return float.NaN;
                }
                float distance = Vector2.Distance(new Vector2(worldX, worldZ), centreWorldXZ);
                float weight = Falloff(distance, radius, hardness);
                if (weight <= 0f)
                {
                    return float.NaN;
                }
                return discDelta(weight, finalHeight, handDelta, worldX, worldZ, activeSnapshot);
            });
    }

    // Tile coordinates whose sample range [c*100-50, c*100+50] contains the world sample (1 or 2 per axis).
    private static void TileRangeForWorldSample(int worldSample, out int firstTile, out int lastTile)
    {
        firstTile = Mathf.CeilToInt((worldSample - TileHalfSpan) / (float)TileSpan);
        lastTile = Mathf.FloorToInt((worldSample + TileHalfSpan) / (float)TileSpan);
    }

    private static TerrainTileData LookupCached(Vector2Int coordinate, Func<Vector2Int, TerrainTileData> tileLookup, Dictionary<Vector2Int, TerrainTileData> tileCache)
    {
        TerrainTileData tile;
        if (tileCache.TryGetValue(coordinate, out tile))
        {
            return tile;
        }
        tile = tileLookup(coordinate);
        if (tile != null)
        {
            tile.EnsureHeightArrays();
        }
        tileCache[coordinate] = tile;
        return tile;
    }

    private static HeightSnapshot BuildSnapshot(int minX, int maxX, int minZ, int maxZ, Func<Vector2Int, TerrainTileData> tileLookup, Dictionary<Vector2Int, TerrainTileData> tileCache)
    {
        HeightSnapshot snapshot = new HeightSnapshot();
        snapshot.originX = minX - 1;
        snapshot.originZ = minZ - 1;
        snapshot.width = maxX - minX + 3;
        snapshot.depth = maxZ - minZ + 3;
        int sampleCount = snapshot.width * snapshot.depth;
        snapshot.finalHeights = new float[sampleCount];
        snapshot.handDeltas = new float[sampleCount];
        snapshot.exists = new bool[sampleCount];

        for (int localZ = 0; localZ < snapshot.depth; localZ++)
        {
            int worldZ = snapshot.originZ + localZ;
            int firstTileZ;
            int lastTileZ;
            TileRangeForWorldSample(worldZ, out firstTileZ, out lastTileZ);
            for (int localX = 0; localX < snapshot.width; localX++)
            {
                int worldX = snapshot.originX + localX;
                int firstTileX;
                int lastTileX;
                TileRangeForWorldSample(worldX, out firstTileX, out lastTileX);
                TerrainTileData owner = null;
                int tileSampleX = 0;
                int tileSampleZ = 0;
                for (int tileZ = firstTileZ; tileZ <= lastTileZ && owner == null; tileZ++)
                {
                    for (int tileX = firstTileX; tileX <= lastTileX && owner == null; tileX++)
                    {
                        TerrainTileData candidate = LookupCached(new Vector2Int(tileX, tileZ), tileLookup, tileCache);
                        if (candidate != null)
                        {
                            owner = candidate;
                            tileSampleX = worldX - (tileX * TileSpan - TileHalfSpan);
                            tileSampleZ = worldZ - (tileZ * TileSpan - TileHalfSpan);
                        }
                    }
                }
                if (owner == null)
                {
                    continue;
                }
                int index = localZ * snapshot.width + localX;
                snapshot.exists[index] = true;
                snapshot.finalHeights[index] = owner.HeightAt(tileSampleX, tileSampleZ);
                snapshot.handDeltas[index] = owner.handHeightDelta[tileSampleZ * TerrainTileData.HeightSamplesPerSide + tileSampleX];
            }
        }
        return snapshot;
    }

    private static List<HeightBrushDirtyRegion> ApplyComputation(int minX, int maxX, int minZ, int maxZ, HeightSnapshot snapshot, Dictionary<Vector2Int, TerrainTileData> tileCache, DeltaComputation computation)
    {
        int footprintWidth = maxX - minX + 1;
        int footprintDepth = maxZ - minZ + 1;
        float[] newDeltas = new float[footprintWidth * footprintDepth];
        for (int offsetZ = 0; offsetZ < footprintDepth; offsetZ++)
        {
            for (int offsetX = 0; offsetX < footprintWidth; offsetX++)
            {
                newDeltas[offsetZ * footprintWidth + offsetX] = computation(minX + offsetX, minZ + offsetZ, snapshot);
            }
        }

        Dictionary<TerrainTileData, RectInt> regionByTile = new Dictionary<TerrainTileData, RectInt>();
        List<TerrainTileData> tileOrder = new List<TerrainTileData>();
        for (int offsetZ = 0; offsetZ < footprintDepth; offsetZ++)
        {
            int worldZ = minZ + offsetZ;
            int firstTileZ;
            int lastTileZ;
            TileRangeForWorldSample(worldZ, out firstTileZ, out lastTileZ);
            for (int offsetX = 0; offsetX < footprintWidth; offsetX++)
            {
                float newDelta = newDeltas[offsetZ * footprintWidth + offsetX];
                if (float.IsNaN(newDelta))
                {
                    continue;
                }
                int worldX = minX + offsetX;
                int firstTileX;
                int lastTileX;
                TileRangeForWorldSample(worldX, out firstTileX, out lastTileX);
                for (int tileZ = firstTileZ; tileZ <= lastTileZ; tileZ++)
                {
                    for (int tileX = firstTileX; tileX <= lastTileX; tileX++)
                    {
                        TerrainTileData tile;
                        if (!tileCache.TryGetValue(new Vector2Int(tileX, tileZ), out tile) || tile == null)
                        {
                            continue;
                        }
                        int tileSampleX = worldX - (tileX * TileSpan - TileHalfSpan);
                        int tileSampleZ = worldZ - (tileZ * TileSpan - TileHalfSpan);
                        tile.handHeightDelta[tileSampleZ * TerrainTileData.HeightSamplesPerSide + tileSampleX] = newDelta;
                        RectInt rect;
                        if (regionByTile.TryGetValue(tile, out rect))
                        {
                            int rectMinX = Mathf.Min(rect.xMin, tileSampleX);
                            int rectMinZ = Mathf.Min(rect.yMin, tileSampleZ);
                            int rectMaxX = Mathf.Max(rect.xMax - 1, tileSampleX);
                            int rectMaxZ = Mathf.Max(rect.yMax - 1, tileSampleZ);
                            regionByTile[tile] = new RectInt(rectMinX, rectMinZ, rectMaxX - rectMinX + 1, rectMaxZ - rectMinZ + 1);
                        }
                        else
                        {
                            regionByTile[tile] = new RectInt(tileSampleX, tileSampleZ, 1, 1);
                            tileOrder.Add(tile);
                        }
                    }
                }
            }
        }

        List<HeightBrushDirtyRegion> dirtyRegions = new List<HeightBrushDirtyRegion>();
        foreach (TerrainTileData tile in tileOrder)
        {
            HeightBrushDirtyRegion region;
            region.tile = tile;
            region.sampleRect = regionByTile[tile];
            dirtyRegions.Add(region);
        }
        return dirtyRegions;
    }
}
#endif
