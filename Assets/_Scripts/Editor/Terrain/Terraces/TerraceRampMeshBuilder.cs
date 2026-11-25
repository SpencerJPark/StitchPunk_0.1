#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public readonly struct TerraceRampFootprint
{
    public readonly Vector2 centre;
    public readonly float yawDegrees;
    public readonly float widthMetres;
    public readonly float runMetres;
    public readonly float lowerHeight;
    public readonly float upperHeight;

    public TerraceRampFootprint(
        Vector2 centre,
        float yawDegrees,
        float widthMetres,
        float runMetres,
        float lowerHeight,
        float upperHeight)
    {
        this.centre = centre;
        this.yawDegrees = yawDegrees;
        this.widthMetres = widthMetres;
        this.runMetres = runMetres;
        this.lowerHeight = lowerHeight;
        this.upperHeight = upperHeight;
    }
}

// Pure mesh generation for one terrace ramp; no scene or AssetDatabase access.
public static class TerraceRampMeshBuilder
{
    private const float TileSizeMetres = 100f;

    public static void GetFootprintCornersXZ(in TerraceRampFootprint ramp, Vector2[] fourCorners)
    {
        Vector2 forward = ForwardXZ(ramp.yawDegrees);
        Vector2 right = RightXZ(ramp.yawDegrees);
        Vector2 lowerCentre = ramp.centre - forward * (ramp.runMetres * 0.5f);
        Vector2 upperCentre = ramp.centre + forward * (ramp.runMetres * 0.5f);
        Vector2 halfWidth = right * (ramp.widthMetres * 0.5f);
        fourCorners[0] = lowerCentre - halfWidth;
        fourCorners[1] = lowerCentre + halfWidth;
        fourCorners[2] = upperCentre + halfWidth;
        fourCorners[3] = upperCentre - halfWidth;
    }

    public static Mesh BuildRampMesh(in TerraceRampFootprint ramp, Vector3 tileOrigin)
    {
        int cellsAlong = Mathf.Max(1, Mathf.CeilToInt(ramp.runMetres));
        int cellsAcross = Mathf.Max(1, Mathf.CeilToInt(ramp.widthMetres));
        int rowCount = cellsAlong + 1;
        int columnCount = cellsAcross + 1;
        int surfaceVertexCount = rowCount * columnCount;
        int vertexCount = surfaceVertexCount + 2 * 2 * rowCount;

        Vector2 forward = ForwardXZ(ramp.yawDegrees);
        Vector2 right = RightXZ(ramp.yawDegrees);
        float rise = ramp.upperHeight - ramp.lowerHeight;
        float slope = ramp.runMetres > 0f ? rise / ramp.runMetres : 0f;
        float edgeLength = Mathf.Sqrt(ramp.runMetres * ramp.runMetres + rise * rise);
        Vector3 surfaceNormal = new Vector3(-forward.x * slope, 1f, -forward.y * slope).normalized;

        Vector3[] positions = new Vector3[vertexCount];
        Vector3[] normals = new Vector3[vertexCount];
        Vector2[] uvs = new Vector2[vertexCount];

        for (int row = 0; row < rowCount; row++)
        {
            float alongFraction = (float)row / cellsAlong;
            for (int column = 0; column < columnCount; column++)
            {
                float acrossFraction = (float)column / cellsAcross;
                Vector2 worldXZ = PointXZ(ramp, forward, right, alongFraction, acrossFraction);
                int vertexIndex = row * columnCount + column;
                positions[vertexIndex] = ToTileLocal(worldXZ, HeightAt(ramp, alongFraction), tileOrigin);
                normals[vertexIndex] = surfaceNormal;
                uvs[vertexIndex] = worldXZ / TileSizeMetres;
            }
        }

        List<int> surfaceTriangles = new List<int>(cellsAlong * cellsAcross * 6);
        for (int row = 0; row < cellsAlong; row++)
        {
            for (int column = 0; column < cellsAcross; column++)
            {
                int corner = row * columnCount + column;
                surfaceTriangles.Add(corner);
                surfaceTriangles.Add(corner + columnCount);
                surfaceTriangles.Add(corner + 1);
                surfaceTriangles.Add(corner + 1);
                surfaceTriangles.Add(corner + columnCount);
                surfaceTriangles.Add(corner + columnCount + 1);
            }
        }

        List<int> wallTriangles = new List<int>(2 * cellsAlong * 6);
        for (int side = 0; side < 2; side++)
        {
            bool isRightSide = side == 1;
            float acrossFraction = isRightSide ? 1f : 0f;
            Vector2 outwardXZ = isRightSide ? right : -right;
            Vector3 outward = new Vector3(outwardXZ.x, 0f, outwardXZ.y);
            int wallBase = surfaceVertexCount + side * 2 * rowCount;
            for (int row = 0; row < rowCount; row++)
            {
                float alongFraction = (float)row / cellsAlong;
                float topHeight = HeightAt(ramp, alongFraction);
                Vector2 worldXZ = PointXZ(ramp, forward, right, alongFraction, acrossFraction);
                int topIndex = wallBase + row * 2;
                int bottomIndex = topIndex + 1;
                positions[topIndex] = ToTileLocal(worldXZ, topHeight, tileOrigin);
                positions[bottomIndex] = ToTileLocal(worldXZ, ramp.lowerHeight, tileOrigin);
                normals[topIndex] = outward;
                normals[bottomIndex] = outward;
                float arcLength = alongFraction * edgeLength;
                uvs[topIndex] = new Vector2(0f, arcLength);
                uvs[bottomIndex] = new Vector2(topHeight - ramp.lowerHeight, arcLength);
            }

            for (int row = 0; row < cellsAlong; row++)
            {
                int topNear = wallBase + row * 2;
                int bottomNear = topNear + 1;
                int topFar = topNear + 2;
                int bottomFar = topNear + 3;
                // Winding flips per side so both faces point away from the ramp.
                if (isRightSide)
                {
                    wallTriangles.Add(topNear);
                    wallTriangles.Add(topFar);
                    wallTriangles.Add(bottomNear);
                    wallTriangles.Add(topFar);
                    wallTriangles.Add(bottomFar);
                    wallTriangles.Add(bottomNear);
                }
                else
                {
                    wallTriangles.Add(topNear);
                    wallTriangles.Add(bottomNear);
                    wallTriangles.Add(topFar);
                    wallTriangles.Add(topFar);
                    wallTriangles.Add(bottomNear);
                    wallTriangles.Add(bottomFar);
                }
            }
        }

        Mesh mesh = new Mesh();
        mesh.name = "TerraceRamp";
        mesh.indexFormat = vertexCount <= 65535 ? IndexFormat.UInt16 : IndexFormat.UInt32;
        mesh.vertices = positions;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.subMeshCount = 2;
        mesh.SetTriangles(surfaceTriangles, 0);
        mesh.SetTriangles(wallTriangles, 1);
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Vector2 ForwardXZ(float yawDegrees)
    {
        float yawRadians = yawDegrees * Mathf.Deg2Rad;
        return new Vector2(Mathf.Sin(yawRadians), Mathf.Cos(yawRadians));
    }

    private static Vector2 RightXZ(float yawDegrees)
    {
        float yawRadians = yawDegrees * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(yawRadians), -Mathf.Sin(yawRadians));
    }

    private static Vector2 PointXZ(
        in TerraceRampFootprint ramp,
        Vector2 forward,
        Vector2 right,
        float alongFraction,
        float acrossFraction)
    {
        return ramp.centre
            + forward * ((alongFraction - 0.5f) * ramp.runMetres)
            + right * ((acrossFraction - 0.5f) * ramp.widthMetres);
    }

    private static float HeightAt(in TerraceRampFootprint ramp, float alongFraction)
    {
        return Mathf.Lerp(ramp.lowerHeight, ramp.upperHeight, alongFraction);
    }

    private static Vector3 ToTileLocal(Vector2 worldXZ, float height, Vector3 tileOrigin)
    {
        return new Vector3(worldXZ.x - tileOrigin.x, height - tileOrigin.y, worldXZ.y - tileOrigin.z);
    }
}
#endif
