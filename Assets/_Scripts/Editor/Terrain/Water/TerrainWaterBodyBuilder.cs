#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public struct TerrainWaterBodyStamp
{
    public Vector3 boundsMin, boundsMax;
    public float waterLevelMetres, deepBlocksBelowDepthMetres;
}

public static class TerrainWaterBodyBuilder
{
    private const int SamplesPerSide = TerrainTileData.HeightSamplesPerSide;
    private const float TileSizeMetres = TerrainTileData.TileSizeMetres;
    private const float MinimumWaterDepthMetres = 0.02f;
    private const float CellSizeMetres = 2f;
    private const float UvMetresPerUnit = 10f;
    private const byte Covered = 255;

    public static Bounds BoundsOf(TerrainWaterBodyStamp body)
    {
        Bounds bounds = new Bounds();
        bounds.SetMinMax(body.boundsMin, body.boundsMax);
        return bounds;
    }

    // Only ever sets texels to 255; a river may already have written the same masks.
    public static void RasteriseSurfaceMasks(
        TerrainWaterBodyStamp body,
        Vector2Int tileCoordinate,
        int resolution,
        float[] groundHeights,
        byte[] shallowOut,
        byte[] deepOut)
    {
        float tileMinX = tileCoordinate.x * TileSizeMetres - TileSizeMetres * 0.5f;
        float tileMinZ = tileCoordinate.y * TileSizeMetres - TileSizeMetres * 0.5f;
        float texelSizeMetres = TileSizeMetres / resolution;

        for (int row = 0; row < resolution; row++)
        {
            float worldZ = tileMinZ + (row + 0.5f) * texelSizeMetres;
            if (worldZ < body.boundsMin.z || worldZ > body.boundsMax.z)
            {
                continue;
            }

            for (int column = 0; column < resolution; column++)
            {
                float worldX = tileMinX + (column + 0.5f) * texelSizeMetres;
                if (worldX < body.boundsMin.x || worldX > body.boundsMax.x)
                {
                    continue;
                }

                float ground = SampleGroundBilinear(groundHeights, worldX - tileMinX, worldZ - tileMinZ);
                float depth = body.waterLevelMetres - ground;
                if (depth <= MinimumWaterDepthMetres)
                {
                    continue;
                }

                int texelIndex = row * resolution + column;
                if (depth > body.deepBlocksBelowDepthMetres)
                {
                    deepOut[texelIndex] = Covered;
                }
                else
                {
                    shallowOut[texelIndex] = Covered;
                }
            }
        }
    }

    public static Mesh BuildSurfaceMeshForTile(
        TerrainWaterBodyStamp body,
        Vector2Int tileCoordinate,
        float[] groundHeights,
        float surfaceOffsetMetres)
    {
        float tileMinX = tileCoordinate.x * TileSizeMetres - TileSizeMetres * 0.5f;
        float tileMinZ = tileCoordinate.y * TileSizeMetres - TileSizeMetres * 0.5f;
        float halfTile = TileSizeMetres * 0.5f;
        int cellsPerSide = Mathf.RoundToInt(TileSizeMetres / CellSizeMetres);
        float surfaceY = body.waterLevelMetres + surfaceOffsetMetres;

        List<Vector3> vertices = new List<Vector3>();
        List<Vector3> normals = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<int> triangles = new List<int>();

        for (int cellZ = 0; cellZ < cellsPerSide; cellZ++)
        {
            float localMinZ = cellZ * CellSizeMetres;
            float localMaxZ = localMinZ + CellSizeMetres;
            float cellCenterWorldZ = tileMinZ + localMinZ + CellSizeMetres * 0.5f;
            if (cellCenterWorldZ < body.boundsMin.z || cellCenterWorldZ > body.boundsMax.z)
            {
                continue;
            }

            for (int cellX = 0; cellX < cellsPerSide; cellX++)
            {
                float localMinX = cellX * CellSizeMetres;
                float localMaxX = localMinX + CellSizeMetres;
                float cellCenterWorldX = tileMinX + localMinX + CellSizeMetres * 0.5f;
                if (cellCenterWorldX < body.boundsMin.x || cellCenterWorldX > body.boundsMax.x)
                {
                    continue;
                }

                bool anyCornerBelowLevel =
                    SampleGroundBilinear(groundHeights, localMinX, localMinZ) < body.waterLevelMetres
                    || SampleGroundBilinear(groundHeights, localMinX, localMaxZ) < body.waterLevelMetres
                    || SampleGroundBilinear(groundHeights, localMaxX, localMaxZ) < body.waterLevelMetres
                    || SampleGroundBilinear(groundHeights, localMaxX, localMinZ) < body.waterLevelMetres;
                if (!anyCornerBelowLevel)
                {
                    continue;
                }

                int firstVertex = vertices.Count;
                AddCornerVertex(vertices, normals, uvs, localMinX, localMinZ, surfaceY, tileMinX, tileMinZ, halfTile);
                AddCornerVertex(vertices, normals, uvs, localMinX, localMaxZ, surfaceY, tileMinX, tileMinZ, halfTile);
                AddCornerVertex(vertices, normals, uvs, localMaxX, localMaxZ, surfaceY, tileMinX, tileMinZ, halfTile);
                AddCornerVertex(vertices, normals, uvs, localMaxX, localMinZ, surfaceY, tileMinX, tileMinZ, halfTile);

                triangles.Add(firstVertex);
                triangles.Add(firstVertex + 1);
                triangles.Add(firstVertex + 2);
                triangles.Add(firstVertex);
                triangles.Add(firstVertex + 2);
                triangles.Add(firstVertex + 3);
            }
        }

        if (triangles.Count == 0)
        {
            return null;
        }

        Mesh mesh = new Mesh();
        mesh.name = "Water_" + tileCoordinate.x + "_" + tileCoordinate.y;
        mesh.indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void AddCornerVertex(
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector2> uvs,
        float localFromMinX,
        float localFromMinZ,
        float surfaceY,
        float tileMinX,
        float tileMinZ,
        float halfTile)
    {
        vertices.Add(new Vector3(localFromMinX - halfTile, surfaceY, localFromMinZ - halfTile));
        normals.Add(Vector3.up);
        uvs.Add(new Vector2(
            (tileMinX + localFromMinX) / UvMetresPerUnit,
            (tileMinZ + localFromMinZ) / UvMetresPerUnit));
    }

    // Offsets are metres from the tile's min corner; the array is 1 m spaced, index = sz * 101 + sx.
    private static float SampleGroundBilinear(float[] groundHeights, float metresFromMinX, float metresFromMinZ)
    {
        int lastIndex = SamplesPerSide - 1;
        float clampedX = Mathf.Clamp(metresFromMinX, 0f, lastIndex);
        float clampedZ = Mathf.Clamp(metresFromMinZ, 0f, lastIndex);
        int sampleX = Mathf.Min(Mathf.FloorToInt(clampedX), lastIndex - 1);
        int sampleZ = Mathf.Min(Mathf.FloorToInt(clampedZ), lastIndex - 1);
        float fractionX = clampedX - sampleX;
        float fractionZ = clampedZ - sampleZ;

        float lowerLeft = groundHeights[sampleZ * SamplesPerSide + sampleX];
        float lowerRight = groundHeights[sampleZ * SamplesPerSide + sampleX + 1];
        float upperLeft = groundHeights[(sampleZ + 1) * SamplesPerSide + sampleX];
        float upperRight = groundHeights[(sampleZ + 1) * SamplesPerSide + sampleX + 1];
        return Mathf.Lerp(
            Mathf.Lerp(lowerLeft, lowerRight, fractionX),
            Mathf.Lerp(upperLeft, upperRight, fractionX),
            fractionZ);
    }
}
#endif
