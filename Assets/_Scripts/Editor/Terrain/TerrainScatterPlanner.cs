#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public struct TerrainScatterRule
{
    public float onDirt;
    public float onGrass;
    public float onForest;
    public float onStone;
    public Vector2 slopeRangeDegrees;
    public Vector2 heightRange;
    public float minSpacingMetres;
    public Vector2 scaleRange;
    public float yawJitterDegrees;
    public float densityPer100SquareMetres;
    public float clusterNoiseScale;
    public float clusterThreshold;
    public bool alignToNormal;
}

public struct TerrainScatterPlacement
{
    public int ruleIndex;
    public Vector3 position;
    public Vector3 normal;
    public float yawDegrees;
    public float scale;
}

public struct TerrainScatterObstacle
{
    public Vector3 position;
    public float radius;
}

public interface ITerrainScatterSampler
{
    float HeightAt(float worldX, float worldZ);
    Vector3 NormalAt(float worldX, float worldZ);
    // False means blocked: water, or off the map.
    bool TryGetBiome(float worldX, float worldZ, out float dirt, out float grass, out float forest, out float stone);
}

// Pure planner shared by decal and prop scatter; all RNG comes from one System.Random so a seed reproduces the plan.
public static class TerrainScatterPlanner
{
    private const float MinimumCellSizeMetres = 0.5f;
    private const int CandidateTriesPerTarget = 30;

    private struct PlacedInstance
    {
        public float worldX;
        public float worldZ;
        public float spacing;
    }

    public static List<TerrainScatterPlacement> Plan(
        IReadOnlyList<TerrainScatterRule> rules,
        ITerrainScatterSampler sampler,
        Rect worldRectXZ,
        int seed,
        IReadOnlyList<TerrainScatterObstacle> obstacles)
    {
        List<TerrainScatterPlacement> placements = new List<TerrainScatterPlacement>();
        float area = worldRectXZ.width * worldRectXZ.height;
        if (rules == null || rules.Count == 0 || sampler == null || area <= 0f)
        {
            return placements;
        }

        float cellSize = MinimumCellSizeMetres;
        for (int ruleIndex = 0; ruleIndex < rules.Count; ruleIndex++)
        {
            cellSize = Mathf.Max(cellSize, rules[ruleIndex].minSpacingMetres);
        }

        System.Random rng = new System.Random(seed);
        float2 seedOffset = CreateSeedOffset(seed);
        Dictionary<long, List<PlacedInstance>> grid = new Dictionary<long, List<PlacedInstance>>();

        for (int ruleIndex = 0; ruleIndex < rules.Count; ruleIndex++)
        {
            TerrainScatterRule rule = rules[ruleIndex];
            int target = Mathf.RoundToInt(rule.densityPer100SquareMetres * area / 100f);
            int maximumTries = target * CandidateTriesPerTarget;
            int accepted = 0;

            for (int tryIndex = 0; tryIndex < maximumTries && accepted < target; tryIndex++)
            {
                float worldX = worldRectXZ.xMin + (float)rng.NextDouble() * worldRectXZ.width;
                float worldZ = worldRectXZ.yMin + (float)rng.NextDouble() * worldRectXZ.height;

                if (!sampler.TryGetBiome(worldX, worldZ, out float dirt, out float grass, out float forest, out float stone))
                {
                    continue;
                }

                float height = sampler.HeightAt(worldX, worldZ);
                if (height < rule.heightRange.x || height > rule.heightRange.y)
                {
                    continue;
                }

                Vector3 normal = sampler.NormalAt(worldX, worldZ);
                float slopeDegrees = Mathf.Acos(Mathf.Clamp(normal.y, -1f, 1f)) * Mathf.Rad2Deg;
                if (slopeDegrees < rule.slopeRangeDegrees.x || slopeDegrees > rule.slopeRangeDegrees.y)
                {
                    continue;
                }

                float biomeWeight = dirt * rule.onDirt + grass * rule.onGrass + forest * rule.onForest + stone * rule.onStone;
                if (rng.NextDouble() >= Mathf.Min(1f, biomeWeight))
                {
                    continue;
                }

                if (rule.clusterNoiseScale > 0f)
                {
                    float clusterValue = noise.snoise(new float2(worldX, worldZ) * rule.clusterNoiseScale + seedOffset) * 0.5f + 0.5f;
                    if (clusterValue < rule.clusterThreshold)
                    {
                        continue;
                    }
                }

                if (IsTooCloseToPlacedInstance(grid, cellSize, worldX, worldZ, rule.minSpacingMetres))
                {
                    continue;
                }

                if (IsTooCloseToObstacle(obstacles, worldX, worldZ, rule.minSpacingMetres))
                {
                    continue;
                }

                float yawDegrees = (float)(rng.NextDouble() * rule.yawJitterDegrees);
                float scale = Mathf.Lerp(rule.scaleRange.x, rule.scaleRange.y, (float)rng.NextDouble());
                placements.Add(new TerrainScatterPlacement
                {
                    ruleIndex = ruleIndex,
                    position = new Vector3(worldX, height, worldZ),
                    normal = normal,
                    yawDegrees = yawDegrees,
                    scale = scale,
                });
                AddToGrid(grid, cellSize, new PlacedInstance { worldX = worldX, worldZ = worldZ, spacing = rule.minSpacingMetres });
                accepted++;
            }
        }

        return placements;
    }

    private static float2 CreateSeedOffset(int seed)
    {
        System.Random offsetRandom = new System.Random(seed ^ 0x5bd1e995);
        return new float2((float)offsetRandom.NextDouble() * 1000f, (float)offsetRandom.NextDouble() * 1000f);
    }

    private static long CellKey(int cellX, int cellZ)
    {
        return ((long)cellX << 32) ^ (uint)cellZ;
    }

    private static void AddToGrid(Dictionary<long, List<PlacedInstance>> grid, float cellSize, PlacedInstance instance)
    {
        long key = CellKey(Mathf.FloorToInt(instance.worldX / cellSize), Mathf.FloorToInt(instance.worldZ / cellSize));
        if (!grid.TryGetValue(key, out List<PlacedInstance> cell))
        {
            cell = new List<PlacedInstance>();
            grid[key] = cell;
        }
        cell.Add(instance);
    }

    // Required separation never exceeds the largest spacing, which is the cell size, so the 3x3 neighbourhood is enough.
    private static bool IsTooCloseToPlacedInstance(
        Dictionary<long, List<PlacedInstance>> grid, float cellSize, float worldX, float worldZ, float ownSpacing)
    {
        int centreCellX = Mathf.FloorToInt(worldX / cellSize);
        int centreCellZ = Mathf.FloorToInt(worldZ / cellSize);
        for (int offsetX = -1; offsetX <= 1; offsetX++)
        {
            for (int offsetZ = -1; offsetZ <= 1; offsetZ++)
            {
                if (!grid.TryGetValue(CellKey(centreCellX + offsetX, centreCellZ + offsetZ), out List<PlacedInstance> cell))
                {
                    continue;
                }
                for (int index = 0; index < cell.Count; index++)
                {
                    PlacedInstance other = cell[index];
                    float requiredDistance = (ownSpacing + other.spacing) * 0.5f;
                    float deltaX = other.worldX - worldX;
                    float deltaZ = other.worldZ - worldZ;
                    if (deltaX * deltaX + deltaZ * deltaZ < requiredDistance * requiredDistance)
                    {
                        return true;
                    }
                }
            }
        }
        return false;
    }

    private static bool IsTooCloseToObstacle(
        IReadOnlyList<TerrainScatterObstacle> obstacles, float worldX, float worldZ, float ownSpacing)
    {
        if (obstacles == null)
        {
            return false;
        }
        for (int index = 0; index < obstacles.Count; index++)
        {
            TerrainScatterObstacle obstacle = obstacles[index];
            float requiredDistance = obstacle.radius + ownSpacing * 0.5f;
            float deltaX = obstacle.position.x - worldX;
            float deltaZ = obstacle.position.z - worldZ;
            if (deltaX * deltaX + deltaZ * deltaZ < requiredDistance * requiredDistance)
            {
                return true;
            }
        }
        return false;
    }
}
#endif
