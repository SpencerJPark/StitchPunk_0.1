#if UNITY_EDITOR
using System;
using UnityEngine;
using UnityEngine.Rendering;

// Pure mesh generation for one terrain tile; no scene or AssetDatabase access.
public static class TerrainMeshBuilder
{
    private const int SamplesAlongEdge = TerrainTileData.HeightSamplesPerSide - 1;
    private static readonly int[] StepsThatDivideTileEdge = { 1, 2, 4, 5, 10, 20, 25, 50 };

    public static int VertexCountPerSide(int step)
    {
        return SamplesAlongEdge / ClampStepToDivisor(step) + 1;
    }

    public static Mesh BuildTileMesh(
        TerrainTileData data,
        int step,
        Func<Vector2Int, TerrainTileData> neighbourLookup = null,
        float verticalOffsetMetres = 0f)
    {
        int clampedStep = ClampStepToDivisor(step);
        int columns = SamplesAlongEdge / clampedStep + 1;
        int vertexCount = columns * columns;
        float halfTile = TerrainTileData.TileSizeMetres * 0.5f;

        Vector3[] positions = new Vector3[vertexCount];
        Vector3[] normals = new Vector3[vertexCount];
        Vector2[] uvs = new Vector2[vertexCount];

        for (int row = 0; row < columns; row++)
        {
            int sampleZ = row * clampedStep;
            for (int column = 0; column < columns; column++)
            {
                int sampleX = column * clampedStep;
                int vertexIndex = row * columns + column;
                float localX = sampleX - halfTile;
                float localZ = sampleZ - halfTile;
                positions[vertexIndex] = new Vector3(
                    localX,
                    data.HeightAt(sampleX, sampleZ) + verticalOffsetMetres,
                    localZ);
                float worldX = data.coordinate.x * TerrainTileData.TileSizeMetres + localX;
                float worldZ = data.coordinate.y * TerrainTileData.TileSizeMetres + localZ;
                uvs[vertexIndex] = new Vector2(worldX / TerrainTileData.TileSizeMetres, worldZ / TerrainTileData.TileSizeMetres);
                normals[vertexIndex] = NormalAt(data, neighbourLookup, verticalOffsetMetres, sampleX, sampleZ, clampedStep);
            }
        }

        int cellsPerSide = columns - 1;
        int[] triangles = new int[cellsPerSide * cellsPerSide * 6];
        int triangleWrite = 0;
        for (int row = 0; row < cellsPerSide; row++)
        {
            for (int column = 0; column < cellsPerSide; column++)
            {
                int corner = row * columns + column;
                triangles[triangleWrite++] = corner;
                triangles[triangleWrite++] = corner + columns;
                triangles[triangleWrite++] = corner + 1;
                triangles[triangleWrite++] = corner + 1;
                triangles[triangleWrite++] = corner + columns;
                triangles[triangleWrite++] = corner + columns + 1;
            }
        }

        Mesh mesh = new Mesh();
        mesh.name = "TerrainTile_" + data.coordinate.x + "_" + data.coordinate.y;
        mesh.indexFormat = vertexCount <= 65535 ? IndexFormat.UInt16 : IndexFormat.UInt32;
        mesh.vertices = positions;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        return mesh;
    }

    private static int ClampStepToDivisor(int step)
    {
        int best = StepsThatDivideTileEdge[0];
        int bestDistance = int.MaxValue;
        for (int index = 0; index < StepsThatDivideTileEdge.Length; index++)
        {
            int distance = Mathf.Abs(StepsThatDivideTileEdge[index] - step);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = StepsThatDivideTileEdge[index];
            }
        }
        return best;
    }

    private static Vector3 NormalAt(
        TerrainTileData data,
        Func<Vector2Int, TerrainTileData> neighbourLookup,
        float verticalOffsetMetres,
        int sampleX,
        int sampleZ,
        int step)
    {
        float centre = data.HeightAt(sampleX, sampleZ) + verticalOffsetMetres;

        float slopeX = SlopeAlongAxis(
            data, neighbourLookup, verticalOffsetMetres, centre,
            sampleX - step, sampleZ, sampleX + step, sampleZ, step);
        float slopeZ = SlopeAlongAxis(
            data, neighbourLookup, verticalOffsetMetres, centre,
            sampleX, sampleZ - step, sampleX, sampleZ + step, step);

        return new Vector3(-slopeX, 1f, -slopeZ).normalized;
    }

    private static float SlopeAlongAxis(
        TerrainTileData data,
        Func<Vector2Int, TerrainTileData> neighbourLookup,
        float verticalOffsetMetres,
        float centreHeight,
        int lowX, int lowZ, int highX, int highZ,
        int step)
    {
        bool hasLow = TrySampleHeight(data, neighbourLookup, verticalOffsetMetres, lowX, lowZ, out float lowHeight);
        bool hasHigh = TrySampleHeight(data, neighbourLookup, verticalOffsetMetres, highX, highZ, out float highHeight);

        if (hasLow && hasHigh)
        {
            return (highHeight - lowHeight) / (2f * step);
        }
        if (hasHigh)
        {
            return (highHeight - centreHeight) / step;
        }
        if (hasLow)
        {
            return (centreHeight - lowHeight) / step;
        }
        return 0f;
    }

    // Sample coordinates may fall one tile past an edge; tile x+1 sample 0 is this tile's sample 100.
    private static bool TrySampleHeight(
        TerrainTileData data,
        Func<Vector2Int, TerrainTileData> neighbourLookup,
        float verticalOffsetMetres,
        int sampleX,
        int sampleZ,
        out float height)
    {
        int tileOffsetX = sampleX < 0 ? -1 : (sampleX > SamplesAlongEdge ? 1 : 0);
        int tileOffsetZ = sampleZ < 0 ? -1 : (sampleZ > SamplesAlongEdge ? 1 : 0);

        if (tileOffsetX == 0 && tileOffsetZ == 0)
        {
            height = data.HeightAt(sampleX, sampleZ) + verticalOffsetMetres;
            return true;
        }

        height = 0f;
        if (neighbourLookup == null)
        {
            return false;
        }
        TerrainTileData neighbour = neighbourLookup(data.coordinate + new Vector2Int(tileOffsetX, tileOffsetZ));
        if (neighbour == null)
        {
            return false;
        }
        height = neighbour.HeightAt(
            sampleX - tileOffsetX * SamplesAlongEdge,
            sampleZ - tileOffsetZ * SamplesAlongEdge) + verticalOffsetMetres;
        return true;
    }
}
#endif
