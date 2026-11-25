#if UNITY_EDITOR
using System;
using UnityEngine;

// Pure terrace outline maths for one tile; no scene or AssetDatabase access.
public static class TerraceLevelField
{
    // Output is clamped so a seam's two tiles agree: only features inside the padded margin are guaranteed visible.
    public const float SignedDistanceClampMetres = 4f;

    private const int CellsPerSide = TerrainTileData.TerraceCellsPerSide;
    private const double NoFeatureDistanceSquared = 1e20;

    // Signed level -3..+3.
    public static int ResolvedLevel(TerrainTileData data, int cellX, int cellZ)
    {
        int cellIndex = cellZ * CellsPerSide + cellX;
        byte[] handLevels = data.handTerraceLevels;
        if (handLevels != null && cellIndex < handLevels.Length && handLevels[cellIndex] != TerrainTileData.NoHandTerraceLevel)
        {
            return handLevels[cellIndex] - TerrainTileData.TerraceLevelBias;
        }

        byte[] baseLevels = data.baseTerraceLevels;
        if (baseLevels != null && cellIndex < baseLevels.Length)
        {
            return baseLevels[cellIndex] - TerrainTileData.TerraceLevelBias;
        }

        return 0;
    }

    // side = 100 * samplesPerMetre + 1; index z * side + x; sample (x, z) sits at tile-local metres (x / spm, z / spm); value = signed metres to the rounded outline, positive inside.
    public static float[] BuildSignedDistance(
        TerrainTileData data,
        int threshold,
        float cornerRadiusMetres,
        Func<Vector2Int, TerrainTileData> neighbourLookup,
        int samplesPerMetre)
    {
        int samplesPerMetreClamped = Mathf.Max(1, samplesPerMetre);
        float radiusMetres = Mathf.Max(0f, cornerRadiusMetres);
        // Rounding is opening then closing, so a sample depends on cells up to three radii away.
        int marginCells = Mathf.Min(
            CellsPerSide,
            Mathf.CeilToInt(3f * radiusMetres) + Mathf.CeilToInt(SignedDistanceClampMetres) + 2);
        int paddedCells = CellsPerSide + 2 * marginCells;
        int side = CellsPerSide * samplesPerMetreClamped + 1;
        int marginSamples = marginCells * samplesPerMetreClamped;
        int paddedSide = side + 2 * marginSamples;

        int[] paddedLevels = BuildPaddedLevels(data, marginCells, neighbourLookup);
        bool[] inside = new bool[paddedSide * paddedSide];
        for (int sampleZ = 0; sampleZ < paddedSide; sampleZ++)
        {
            int cellZ = Math.Min(sampleZ / samplesPerMetreClamped, paddedCells - 1);
            for (int sampleX = 0; sampleX < paddedSide; sampleX++)
            {
                int cellX = Math.Min(sampleX / samplesPerMetreClamped, paddedCells - 1);
                inside[sampleZ * paddedSide + sampleX] = paddedLevels[cellZ * paddedCells + cellX] >= threshold;
            }
        }

        if (radiusMetres > 0f)
        {
            inside = RoundOutline(inside, paddedSide, radiusMetres * samplesPerMetreClamped);
        }

        double[] distanceSquaredToOutside = DistanceSquaredToFeatures(Invert(inside), paddedSide);
        double[] distanceSquaredToInside = DistanceSquaredToFeatures(inside, paddedSide);

        float[] signedDistance = new float[side * side];
        for (int sampleZ = 0; sampleZ < side; sampleZ++)
        {
            for (int sampleX = 0; sampleX < side; sampleX++)
            {
                int paddedIndex = (sampleZ + marginSamples) * paddedSide + sampleX + marginSamples;
                double metres;
                if (inside[paddedIndex])
                {
                    metres = (Math.Sqrt(distanceSquaredToOutside[paddedIndex]) - 0.5) / samplesPerMetreClamped;
                }
                else
                {
                    metres = -(Math.Sqrt(distanceSquaredToInside[paddedIndex]) - 0.5) / samplesPerMetreClamped;
                }

                signedDistance[sampleZ * side + sampleX] =
                    (float)Math.Max(-SignedDistanceClampMetres, Math.Min(SignedDistanceClampMetres, metres));
            }
        }

        return signedDistance;
    }

    private static int[] BuildPaddedLevels(
        TerrainTileData data,
        int marginCells,
        Func<Vector2Int, TerrainTileData> neighbourLookup)
    {
        int paddedCells = CellsPerSide + 2 * marginCells;
        int[] paddedLevels = new int[paddedCells * paddedCells];
        for (int paddedZ = 0; paddedZ < paddedCells; paddedZ++)
        {
            int worldCellZ = paddedZ - marginCells;
            int offsetZ = FloorDivide(worldCellZ, CellsPerSide);
            for (int paddedX = 0; paddedX < paddedCells; paddedX++)
            {
                int worldCellX = paddedX - marginCells;
                int offsetX = FloorDivide(worldCellX, CellsPerSide);
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
                        // No fake wall at the world edge: repeat this tile's nearest edge cell.
                        localX = Mathf.Clamp(worldCellX, 0, CellsPerSide - 1);
                        localZ = Mathf.Clamp(worldCellZ, 0, CellsPerSide - 1);
                    }
                }

                paddedLevels[paddedZ * paddedCells + paddedX] = ResolvedLevel(source, localX, localZ);
            }
        }

        return paddedLevels;
    }

    private static bool[] RoundOutline(bool[] inside, int side, double radiusSamples)
    {
        double reachSquared = (radiusSamples + 0.5) * (radiusSamples + 0.5);
        int sampleCount = inside.Length;

        double[] distanceToOutside = DistanceSquaredToFeatures(Invert(inside), side);
        bool[] eroded = new bool[sampleCount];
        for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
        {
            eroded[sampleIndex] = inside[sampleIndex] && distanceToOutside[sampleIndex] >= reachSquared;
        }

        double[] distanceToEroded = DistanceSquaredToFeatures(eroded, side);
        bool[] opened = new bool[sampleCount];
        for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
        {
            opened[sampleIndex] = distanceToEroded[sampleIndex] <= reachSquared;
        }

        double[] distanceToOpened = DistanceSquaredToFeatures(opened, side);
        bool[] dilated = new bool[sampleCount];
        for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
        {
            dilated[sampleIndex] = distanceToOpened[sampleIndex] <= reachSquared;
        }

        double[] distanceToOutsideOfDilated = DistanceSquaredToFeatures(Invert(dilated), side);
        bool[] closed = new bool[sampleCount];
        for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
        {
            closed[sampleIndex] = dilated[sampleIndex] && distanceToOutsideOfDilated[sampleIndex] >= reachSquared;
        }

        return closed;
    }

    private static bool[] Invert(bool[] source)
    {
        bool[] inverted = new bool[source.Length];
        for (int sampleIndex = 0; sampleIndex < source.Length; sampleIndex++)
        {
            inverted[sampleIndex] = !source[sampleIndex];
        }

        return inverted;
    }

    // Felzenszwalb squared Euclidean distance transform, two separable passes.
    private static double[] DistanceSquaredToFeatures(bool[] features, int side)
    {
        double[] grid = new double[side * side];
        for (int sampleIndex = 0; sampleIndex < grid.Length; sampleIndex++)
        {
            grid[sampleIndex] = features[sampleIndex] ? 0.0 : NoFeatureDistanceSquared;
        }

        double[] lineSource = new double[side];
        double[] lineResult = new double[side];
        int[] parabolaVertices = new int[side];
        double[] parabolaBoundaries = new double[side + 1];

        for (int column = 0; column < side; column++)
        {
            for (int row = 0; row < side; row++)
            {
                lineSource[row] = grid[row * side + column];
            }

            TransformLine(lineSource, lineResult, parabolaVertices, parabolaBoundaries, side);
            for (int row = 0; row < side; row++)
            {
                grid[row * side + column] = lineResult[row];
            }
        }

        for (int row = 0; row < side; row++)
        {
            int rowStart = row * side;
            for (int column = 0; column < side; column++)
            {
                lineSource[column] = grid[rowStart + column];
            }

            TransformLine(lineSource, lineResult, parabolaVertices, parabolaBoundaries, side);
            for (int column = 0; column < side; column++)
            {
                grid[rowStart + column] = lineResult[column];
            }
        }

        return grid;
    }

    private static void TransformLine(
        double[] source,
        double[] result,
        int[] parabolaVertices,
        double[] parabolaBoundaries,
        int length)
    {
        int parabolaCount = 0;
        parabolaVertices[0] = 0;
        parabolaBoundaries[0] = double.NegativeInfinity;
        parabolaBoundaries[1] = double.PositiveInfinity;
        for (int position = 1; position < length; position++)
        {
            double intersection;
            while (true)
            {
                int vertex = parabolaVertices[parabolaCount];
                intersection = ((source[position] + (double)position * position) - (source[vertex] + (double)vertex * vertex))
                    / (2.0 * position - 2.0 * vertex);
                if (intersection <= parabolaBoundaries[parabolaCount] && parabolaCount > 0)
                {
                    parabolaCount--;
                }
                else
                {
                    break;
                }
            }

            parabolaCount++;
            parabolaVertices[parabolaCount] = position;
            parabolaBoundaries[parabolaCount] = intersection;
            parabolaBoundaries[parabolaCount + 1] = double.PositiveInfinity;
        }

        parabolaCount = 0;
        for (int position = 0; position < length; position++)
        {
            while (parabolaBoundaries[parabolaCount + 1] < position)
            {
                parabolaCount++;
            }

            int vertex = parabolaVertices[parabolaCount];
            double offset = position - vertex;
            result[position] = offset * offset + source[vertex];
        }
    }

    private static int FloorDivide(int numerator, int denominator)
    {
        int quotient = numerator / denominator;
        if (numerator % denominator != 0 && (numerator < 0) != (denominator < 0))
        {
            quotient--;
        }

        return quotient;
    }
}
#endif
