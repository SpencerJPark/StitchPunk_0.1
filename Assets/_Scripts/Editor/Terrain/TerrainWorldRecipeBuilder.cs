#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

public sealed class TerrainWorldRecipe
{
    public int seed;
    public int tilesPerSide;
    public float worldHalfExtentMetres;
    public List<TerrainMountainFeature> mountains = new List<TerrainMountainFeature>();
    public TerrainFlatArea campFlatArea;
    public List<Vector2> locations = new List<Vector2>();
    public List<List<Vector3>> roads = new List<List<Vector3>>();
    public List<Vector3> river = new List<Vector3>();
}

public static class TerrainWorldRecipeBuilder
{
    private const float TileSizeMetres = 100f;
    private const float CampFlatRadiusMetres = 45f;
    private const float CampBlendEndRadiusMetres = 110f;
    private const float LandLiftMetres = 15f;
    private const float MountainMinimumDistanceFromOriginMetres = 200f;
    private const float LocationMinimumSpacingMetres = 150f;
    private const float LocationEdgeMarginMetres = 60f;
    private const float LocationMaximumSlopeDegrees = 12f;
    private const float RoadGridCellMetres = 20f;
    private const float RoadKnotSpacingMetres = 40f;
    private const float RoadImpassableSlopeDegrees = 35f;
    private const float RiverStepMetres = 10f;
    private const int RiverMaximumSteps = 600;
    private const int RiverKeepEveryNthPoint = 4;
    private const float RiverMinimumLengthMetres = 150f;
    private const float RiverMinimumDistanceFromOriginMetres = 80f;
    private const int RiverSourceAttempts = 8;

    public static TerrainWorldRecipe Build(int seed, int tilesPerSide, TerrainGenerationProfile profile)
    {
        Unity.Mathematics.Random random = new Unity.Mathematics.Random(((uint)seed * 747796405u + 2891336453u) | 1u);
        float halfExtent = tilesPerSide * TileSizeMetres * 0.5f;

        TerrainWorldRecipe recipe = new TerrainWorldRecipe();
        recipe.seed = seed;
        recipe.tilesPerSide = tilesPerSide;
        recipe.worldHalfExtentMetres = halfExtent;
        recipe.campFlatArea = new TerrainFlatArea
        {
            centreWorld = Vector2.zero,
            flatRadiusMetres = CampFlatRadiusMetres,
            blendEndRadiusMetres = CampBlendEndRadiusMetres,
            heightMetres = 0f,
        };

        int mountainCount = 1 + tilesPerSide / 4;
        for (int mountainIndex = 0; mountainIndex < mountainCount; mountainIndex++)
        {
            recipe.mountains.Add(PlaceMountain(ref random, halfExtent));
        }

        profile.seed = seed;
        profile.landLiftMetres = LandLiftMetres;
        profile.mountains = new List<TerrainMountainFeature>(recipe.mountains);
        profile.flatAreas = new List<TerrainFlatArea> { recipe.campFlatArea };

        int locationCount = 3 + tilesPerSide / 4;
        PlaceLocations(ref random, profile, recipe, halfExtent, locationCount);
        BuildRoads(profile, recipe, halfExtent);
        recipe.river = BuildRiver(ref random, profile, recipe, halfExtent);
        return recipe;
    }

    private static TerrainMountainFeature PlaceMountain(ref Unity.Mathematics.Random random, float halfExtent)
    {
        float radius = random.NextFloat(110f, 180f);
        float height = random.NextFloat(90f, 200f);
        bool isRidge = random.NextBool();
        float inset = Mathf.Min(radius, halfExtent * 0.5f);
        float limit = halfExtent - inset;

        for (int attemptIndex = 0; attemptIndex < 60; attemptIndex++)
        {
            Vector2 centre = new Vector2(random.NextFloat(-limit, limit), random.NextFloat(-limit, limit));
            Vector2 start = centre;
            Vector2 end = centre;
            if (isRidge)
            {
                float angle = random.NextFloat(0f, Mathf.PI * 2f);
                Vector2 halfRidge = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (random.NextFloat(150f, 300f) * 0.5f);
                start = ClampToSquare(centre - halfRidge, limit);
                end = ClampToSquare(centre + halfRidge, limit);
            }
            if (DistanceToSegment(Vector2.zero, start, end) >= MountainMinimumDistanceFromOriginMetres)
            {
                return new TerrainMountainFeature { startWorld = start, endWorld = end, radiusMetres = radius, heightMetres = height };
            }
        }

        Vector2 corner = new Vector2(limit, limit);
        return new TerrainMountainFeature { startWorld = corner, endWorld = corner, radiusMetres = radius, heightMetres = height };
    }

    private static void PlaceLocations(ref Unity.Mathematics.Random random, TerrainGenerationProfile profile, TerrainWorldRecipe recipe, float halfExtent, int locationCount)
    {
        float limit = halfExtent - LocationEdgeMarginMetres;
        int triesLeft = locationCount * 60;
        while (recipe.locations.Count < locationCount && triesLeft > 0)
        {
            triesLeft--;
            Vector2 candidate = new Vector2(random.NextFloat(-limit, limit), random.NextFloat(-limit, limit));
            if (candidate.magnitude < LocationMinimumSpacingMetres) continue;
            if (IsNearAnyLocation(recipe.locations, candidate)) continue;
            if (IsInsideAnyMountain(recipe.mountains, candidate)) continue;
            if (SlopeDegreesAt(profile, candidate.x, candidate.y) >= LocationMaximumSlopeDegrees) continue;
            recipe.locations.Add(candidate);
        }
    }

    private static void BuildRoads(TerrainGenerationProfile profile, TerrainWorldRecipe recipe, float halfExtent)
    {
        List<Vector2> nodes = new List<Vector2> { Vector2.zero };
        nodes.AddRange(recipe.locations);

        bool[] inTree = new bool[nodes.Count];
        float[] bestDistance = new float[nodes.Count];
        int[] bestParent = new int[nodes.Count];
        for (int nodeIndex = 0; nodeIndex < nodes.Count; nodeIndex++)
        {
            bestDistance[nodeIndex] = Vector2.Distance(nodes[0], nodes[nodeIndex]);
            bestParent[nodeIndex] = 0;
        }
        inTree[0] = true;

        for (int addedCount = 1; addedCount < nodes.Count; addedCount++)
        {
            int nextNode = -1;
            for (int nodeIndex = 1; nodeIndex < nodes.Count; nodeIndex++)
            {
                if (inTree[nodeIndex]) continue;
                if (nextNode < 0 || bestDistance[nodeIndex] < bestDistance[nextNode]) nextNode = nodeIndex;
            }
            inTree[nextNode] = true;
            recipe.roads.Add(RouteRoad(profile, nodes[bestParent[nextNode]], nodes[nextNode], halfExtent));
            for (int nodeIndex = 1; nodeIndex < nodes.Count; nodeIndex++)
            {
                if (inTree[nodeIndex]) continue;
                float distance = Vector2.Distance(nodes[nextNode], nodes[nodeIndex]);
                if (distance < bestDistance[nodeIndex])
                {
                    bestDistance[nodeIndex] = distance;
                    bestParent[nodeIndex] = nextNode;
                }
            }
        }
    }

    private static List<Vector3> BuildRiver(ref Unity.Mathematics.Random random, TerrainGenerationProfile profile, TerrainWorldRecipe recipe, float halfExtent)
    {
        if (recipe.mountains.Count == 0) return new List<Vector3>();

        TerrainMountainFeature highest = recipe.mountains[0];
        for (int mountainIndex = 1; mountainIndex < recipe.mountains.Count; mountainIndex++)
        {
            if (recipe.mountains[mountainIndex].heightMetres > highest.heightMetres) highest = recipe.mountains[mountainIndex];
        }

        for (int attemptIndex = 0; attemptIndex < RiverSourceAttempts; attemptIndex++)
        {
            Vector2 onCentreLine = Vector2.Lerp(highest.startWorld, highest.endWorld, random.NextFloat(0f, 1f));
            float angle = random.NextFloat(0f, Mathf.PI * 2f);
            Vector2 source = ClampToSquare(onCentreLine + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (highest.radiusMetres * 0.6f), halfExtent);
            List<Vector3> river = TraceRiverDownhill(profile, source, halfExtent);
            if (IsAcceptableRiver(river)) return river;
        }
        return new List<Vector3>();
    }

    private static bool IsAcceptableRiver(List<Vector3> river)
    {
        if (river.Count < 2) return false;
        float length = 0f;
        for (int pointIndex = 1; pointIndex < river.Count; pointIndex++)
        {
            Vector2 previous = new Vector2(river[pointIndex - 1].x, river[pointIndex - 1].z);
            Vector2 current = new Vector2(river[pointIndex].x, river[pointIndex].z);
            length += Vector2.Distance(previous, current);
            if (DistanceToSegment(Vector2.zero, previous, current) < RiverMinimumDistanceFromOriginMetres) return false;
        }
        return length >= RiverMinimumLengthMetres;
    }

    public static List<Vector3> TraceRiverDownhill(TerrainGenerationProfile profile, Vector2 source, float worldHalfExtentMetres)
    {
        List<Vector3> traced = new List<Vector3>();
        Vector2 position = source;
        Vector2 direction = Vector2.zero;
        float height = TerrainProceduralGenerator.AnalyticHeightAt(profile, position.x, position.y);
        traced.Add(new Vector3(position.x, height, position.y));

        for (int stepIndex = 0; stepIndex < RiverMaximumSteps; stepIndex++)
        {
            Vector2 gradient = HeightGradientAt(profile, position.x, position.y);
            if (gradient.magnitude < 0.0005f) break;

            Vector2 steepest = -gradient.normalized;
            Vector2 blended = direction == Vector2.zero ? steepest : Vector2.Lerp(direction, steepest, 0.6f);
            if (blended.sqrMagnitude < 0.0001f) break;
            direction = blended.normalized;

            Vector2 next = position + direction * RiverStepMetres;
            if (Mathf.Abs(next.x) > worldHalfExtentMetres || Mathf.Abs(next.y) > worldHalfExtentMetres) break;

            float nextHeight = TerrainProceduralGenerator.AnalyticHeightAt(profile, next.x, next.y);
            if (nextHeight >= height - 0.001f) break;

            position = next;
            height = nextHeight;
            traced.Add(new Vector3(position.x, height, position.y));
        }

        List<Vector3> kept = new List<Vector3>();
        for (int pointIndex = 0; pointIndex < traced.Count; pointIndex++)
        {
            if (pointIndex % RiverKeepEveryNthPoint == 0 || pointIndex == traced.Count - 1) kept.Add(traced[pointIndex]);
        }
        return kept;
    }

    public static List<Vector3> RouteRoad(TerrainGenerationProfile profile, Vector2 from, Vector2 to, float worldHalfExtentMetres)
    {
        int cellsPerSide = Mathf.RoundToInt(2f * worldHalfExtentMetres / RoadGridCellMetres) + 1;
        float[] heights = new float[cellsPerSide * cellsPerSide];
        for (int cellZ = 0; cellZ < cellsPerSide; cellZ++)
        {
            for (int cellX = 0; cellX < cellsPerSide; cellX++)
            {
                Vector2 cellCentre = CellCentre(cellX, cellZ, worldHalfExtentMetres);
                heights[cellZ * cellsPerSide + cellX] = TerrainProceduralGenerator.AnalyticHeightAt(profile, cellCentre.x, cellCentre.y);
            }
        }

        int startCell = CellIndexAt(from, cellsPerSide, worldHalfExtentMetres);
        int goalCell = CellIndexAt(to, cellsPerSide, worldHalfExtentMetres);
        List<int> cellPath = FindCellPath(heights, cellsPerSide, startCell, goalCell);
        if (cellPath == null) return StraightRoad(from, to);

        List<Vector3> road = new List<Vector3> { new Vector3(from.x, 0f, from.y) };
        float sinceLastKnot = 0f;
        Vector2 previousPoint = from;
        for (int pathIndex = 1; pathIndex < cellPath.Count - 1; pathIndex++)
        {
            Vector2 cellPoint = CellCentre(cellPath[pathIndex] % cellsPerSide, cellPath[pathIndex] / cellsPerSide, worldHalfExtentMetres);
            sinceLastKnot += Vector2.Distance(previousPoint, cellPoint);
            previousPoint = cellPoint;
            if (sinceLastKnot < RoadKnotSpacingMetres) continue;
            if (Vector2.Distance(cellPoint, to) < RoadKnotSpacingMetres * 0.4f) continue;
            road.Add(new Vector3(cellPoint.x, 0f, cellPoint.y));
            sinceLastKnot = 0f;
        }
        road.Add(new Vector3(to.x, 0f, to.y));
        return road;
    }

    private static List<int> FindCellPath(float[] heights, int cellsPerSide, int startCell, int goalCell)
    {
        int cellTotal = cellsPerSide * cellsPerSide;
        float[] costSoFar = new float[cellTotal];
        int[] cameFrom = new int[cellTotal];
        for (int cellIndex = 0; cellIndex < cellTotal; cellIndex++)
        {
            costSoFar[cellIndex] = float.MaxValue;
            cameFrom[cellIndex] = -1;
        }

        SortedSet<(float, int)> open = new SortedSet<(float, int)>();
        costSoFar[startCell] = 0f;
        open.Add((CellHeuristic(startCell, goalCell, cellsPerSide), startCell));

        while (open.Count > 0)
        {
            (float, int) best = open.Min;
            open.Remove(best);
            int current = best.Item2;
            if (current == goalCell) break;

            int currentX = current % cellsPerSide;
            int currentZ = current / cellsPerSide;
            for (int offsetZ = -1; offsetZ <= 1; offsetZ++)
            {
                for (int offsetX = -1; offsetX <= 1; offsetX++)
                {
                    if (offsetX == 0 && offsetZ == 0) continue;
                    int neighbourX = currentX + offsetX;
                    int neighbourZ = currentZ + offsetZ;
                    if (neighbourX < 0 || neighbourZ < 0 || neighbourX >= cellsPerSide || neighbourZ >= cellsPerSide) continue;

                    int neighbour = neighbourZ * cellsPerSide + neighbourX;
                    float horizontal = RoadGridCellMetres * (offsetX != 0 && offsetZ != 0 ? 1.41421356f : 1f);
                    float slopeDegrees = Mathf.Atan(Mathf.Abs(heights[neighbour] - heights[current]) / horizontal) * Mathf.Rad2Deg;
                    if (slopeDegrees > RoadImpassableSlopeDegrees) continue;

                    float stepCost = horizontal * (1f + SlopePenalty(slopeDegrees));
                    float newCost = costSoFar[current] + stepCost;
                    if (newCost >= costSoFar[neighbour]) continue;

                    if (costSoFar[neighbour] != float.MaxValue) open.Remove((costSoFar[neighbour] + CellHeuristic(neighbour, goalCell, cellsPerSide), neighbour));
                    costSoFar[neighbour] = newCost;
                    cameFrom[neighbour] = current;
                    open.Add((newCost + CellHeuristic(neighbour, goalCell, cellsPerSide), neighbour));
                }
            }
        }

        if (cameFrom[goalCell] < 0 && goalCell != startCell) return null;

        List<int> path = new List<int>();
        for (int cell = goalCell; cell >= 0; cell = cameFrom[cell])
        {
            path.Add(cell);
        }
        path.Reverse();
        return path;
    }

    private static float SlopePenalty(float slopeDegrees)
    {
        if (slopeDegrees <= 15f) return slopeDegrees / 15f * 0.5f;
        float overshoot = (slopeDegrees - 15f) / 5f;
        return 0.5f + overshoot * overshoot;
    }

    private static float CellHeuristic(int cell, int goalCell, int cellsPerSide)
    {
        float deltaX = Mathf.Abs(cell % cellsPerSide - goalCell % cellsPerSide);
        float deltaZ = Mathf.Abs(cell / cellsPerSide - goalCell / cellsPerSide);
        return RoadGridCellMetres * Mathf.Sqrt(deltaX * deltaX + deltaZ * deltaZ);
    }

    private static List<Vector3> StraightRoad(Vector2 from, Vector2 to)
    {
        int segmentCount = Mathf.Max(1, Mathf.RoundToInt(Vector2.Distance(from, to) / RoadKnotSpacingMetres));
        List<Vector3> road = new List<Vector3>();
        for (int segmentIndex = 0; segmentIndex <= segmentCount; segmentIndex++)
        {
            Vector2 point = segmentIndex == segmentCount ? to : Vector2.Lerp(from, to, (float)segmentIndex / segmentCount);
            road.Add(new Vector3(point.x, 0f, point.y));
        }
        return road;
    }

    private static Vector2 CellCentre(int cellX, int cellZ, float halfExtent)
    {
        return new Vector2(-halfExtent + cellX * RoadGridCellMetres, -halfExtent + cellZ * RoadGridCellMetres);
    }

    private static int CellIndexAt(Vector2 world, int cellsPerSide, float halfExtent)
    {
        int cellX = Mathf.Clamp(Mathf.RoundToInt((world.x + halfExtent) / RoadGridCellMetres), 0, cellsPerSide - 1);
        int cellZ = Mathf.Clamp(Mathf.RoundToInt((world.y + halfExtent) / RoadGridCellMetres), 0, cellsPerSide - 1);
        return cellZ * cellsPerSide + cellX;
    }

    private static Vector2 HeightGradientAt(TerrainGenerationProfile profile, float worldX, float worldZ)
    {
        const float Delta = 3f;
        float gradientX = (TerrainProceduralGenerator.AnalyticHeightAt(profile, worldX + Delta, worldZ) - TerrainProceduralGenerator.AnalyticHeightAt(profile, worldX - Delta, worldZ)) / (2f * Delta);
        float gradientZ = (TerrainProceduralGenerator.AnalyticHeightAt(profile, worldX, worldZ + Delta) - TerrainProceduralGenerator.AnalyticHeightAt(profile, worldX, worldZ - Delta)) / (2f * Delta);
        return new Vector2(gradientX, gradientZ);
    }

    private static float SlopeDegreesAt(TerrainGenerationProfile profile, float worldX, float worldZ)
    {
        return Mathf.Atan(HeightGradientAt(profile, worldX, worldZ).magnitude) * Mathf.Rad2Deg;
    }

    private static bool IsNearAnyLocation(List<Vector2> locations, Vector2 candidate)
    {
        for (int locationIndex = 0; locationIndex < locations.Count; locationIndex++)
        {
            if (Vector2.Distance(locations[locationIndex], candidate) < LocationMinimumSpacingMetres) return true;
        }
        return false;
    }

    private static bool IsInsideAnyMountain(List<TerrainMountainFeature> mountains, Vector2 candidate)
    {
        for (int mountainIndex = 0; mountainIndex < mountains.Count; mountainIndex++)
        {
            TerrainMountainFeature mountain = mountains[mountainIndex];
            if (DistanceToSegment(candidate, mountain.startWorld, mountain.endWorld) < mountain.radiusMetres) return true;
        }
        return false;
    }

    private static Vector2 ClampToSquare(Vector2 point, float limit)
    {
        return new Vector2(Mathf.Clamp(point.x, -limit, limit), Mathf.Clamp(point.y, -limit, limit));
    }

    public static float DistanceToSegment(Vector2 point, Vector2 segmentStart, Vector2 segmentEnd)
    {
        Vector2 segment = segmentEnd - segmentStart;
        float lengthSquared = segment.sqrMagnitude;
        if (lengthSquared < 0.0001f) return Vector2.Distance(point, segmentStart);
        float along = Mathf.Clamp01(Vector2.Dot(point - segmentStart, segment) / lengthSquared);
        return Vector2.Distance(point, segmentStart + segment * along);
    }
}
#endif
