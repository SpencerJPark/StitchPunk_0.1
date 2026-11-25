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
    private const int FilletSteps = 4;
    private const int VerticesPerWallRow = (FilletSteps + 1) * 2 + 2;
    private const float ProfiledVertexUvOffset = 10f;
    private const float WallRowsPerMetre = 2f;

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
        return BuildRampMesh(ramp, tileOrigin, 0.2f, 0.2f);
    }

    public static Mesh BuildRampMesh(
        in TerraceRampFootprint ramp,
        Vector3 tileOrigin,
        float topRoundMetres,
        float footRoundMetres)
    {
        int cellsAlong = Mathf.Max(1, Mathf.CeilToInt(ramp.runMetres));
        int cellsAcross = Mathf.Max(1, Mathf.CeilToInt(ramp.widthMetres));
        int rowCount = cellsAlong + 1;
        int columnCount = cellsAcross + 1;
        int surfaceVertexCount = rowCount * columnCount;
        int wallCellsAlong = WallCellsAlong(ramp);
        int wallRowCount = wallCellsAlong + 1;
        int vertexCount = surfaceVertexCount + 2 * VerticesPerWallRow * wallRowCount;

        Vector2 forward = ForwardXZ(ramp.yawDegrees);
        Vector2 right = RightXZ(ramp.yawDegrees);
        float rise = ramp.upperHeight - ramp.lowerHeight;
        float slope = ramp.runMetres > 0f ? rise / ramp.runMetres : 0f;
        float edgeLength = Mathf.Sqrt(ramp.runMetres * ramp.runMetres + rise * rise);
        Vector3 surfaceNormal = new Vector3(-forward.x * slope, 1f, -forward.y * slope).normalized;

        Vector3[] positions = new Vector3[vertexCount];
        Vector3[] normals = new Vector3[vertexCount];
        Vector4[] uvs = new Vector4[vertexCount];

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
                Vector2 surfaceUv = worldXZ / TileSizeMetres;
                uvs[vertexIndex] = new Vector4(surfaceUv.x, surfaceUv.y, 0f, 0f);
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

        List<int> wallTriangles = new List<int>(2 * wallCellsAlong * (FilletSteps * 2 + 1) * 6);
        int[] groupStarts = { 0, FilletSteps + 1, FilletSteps + 3 };
        int[] groupRowCounts = { FilletSteps + 1, 2, FilletSteps + 1 };
        for (int side = 0; side < 2; side++)
        {
            bool isRightSide = side == 1;
            float acrossFraction = isRightSide ? 1f : 0f;
            Vector2 outwardXZ = isRightSide ? right : -right;
            int wallBase = surfaceVertexCount + side * VerticesPerWallRow * wallRowCount;
            float[] wallHeights = new float[wallRowCount];
            for (int row = 0; row < wallRowCount; row++)
            {
                float alongFraction = (float)row / wallCellsAlong;
                float topHeight = HeightAt(ramp, alongFraction);
                float wallHeight = topHeight - ramp.lowerHeight;
                wallHeights[row] = wallHeight;
                Vector2 worldXZ = PointXZ(ramp, forward, right, alongFraction, acrossFraction);
                float alongMetres = alongFraction * edgeLength;
                float topRound = Mathf.Min(topRoundMetres, 0.3f * wallHeight);
                float footRound = Mathf.Min(footRoundMetres, 0.3f * wallHeight);
                float batter = 0.08f * wallHeight;
                float faceRun = Mathf.Max(0f, wallHeight - footRound - topRound);
                float topGrassEndArc = topRound * Mathf.PI * 0.5f;
                float faceLength = Mathf.Sqrt(batter * batter + faceRun * faceRun);
                float footGrassStartArc = topGrassEndArc + faceLength;
                int rowBase = wallBase + row * VerticesPerWallRow;

                for (int step = 0; step <= FilletSteps; step++)
                {
                    float angle = (float)step / FilletSteps * Mathf.PI * 0.5f;
                    WriteWallVertex(positions, normals, uvs, rowBase + step, worldXZ, topHeight, tileOrigin, outwardXZ,
                        topRound * angle, topRound * Mathf.Sin(angle), topRound * (1f - Mathf.Cos(angle)),
                        Mathf.Sin(angle), Mathf.Cos(angle), alongMetres, topGrassEndArc, footGrassStartArc);
                }

                float faceNormalOut = faceLength > 1e-6f ? faceRun / faceLength : 1f;
                float faceNormalUp = faceLength > 1e-6f ? batter / faceLength : 0f;
                WriteWallVertex(positions, normals, uvs, rowBase + FilletSteps + 1, worldXZ, topHeight, tileOrigin, outwardXZ,
                    topGrassEndArc, topRound, topRound, faceNormalOut, faceNormalUp, alongMetres, topGrassEndArc, footGrassStartArc);
                WriteWallVertex(positions, normals, uvs, rowBase + FilletSteps + 2, worldXZ, topHeight, tileOrigin, outwardXZ,
                    footGrassStartArc, topRound + batter, wallHeight - footRound, faceNormalOut, faceNormalUp,
                    alongMetres, topGrassEndArc, footGrassStartArc);

                for (int step = 0; step <= FilletSteps; step++)
                {
                    float angle = (float)step / FilletSteps * Mathf.PI * 0.5f;
                    WriteWallVertex(positions, normals, uvs, rowBase + FilletSteps + 3 + step, worldXZ, topHeight, tileOrigin, outwardXZ,
                        footGrassStartArc + footRound * angle,
                        topRound + batter + footRound * (1f - Mathf.Cos(angle)),
                        wallHeight - footRound + footRound * Mathf.Sin(angle),
                        Mathf.Cos(angle), Mathf.Sin(angle), alongMetres, topGrassEndArc, footGrassStartArc);
                }
            }

            for (int row = 0; row < wallCellsAlong; row++)
            {
                if (wallHeights[row] < 0.01f && wallHeights[row + 1] < 0.01f)
                {
                    continue;
                }

                for (int group = 0; group < 3; group++)
                {
                    for (int step = 0; step < groupRowCounts[group] - 1; step++)
                    {
                        int nearUpper = wallBase + row * VerticesPerWallRow + groupStarts[group] + step;
                        int farUpper = nearUpper + VerticesPerWallRow;
                        AddOrientedTriangle(wallTriangles, positions, normals, nearUpper, farUpper, nearUpper + 1);
                        AddOrientedTriangle(wallTriangles, positions, normals, farUpper, farUpper + 1, nearUpper + 1);
                    }
                }
            }
        }

        Mesh mesh = new Mesh();
        mesh.name = "TerraceRamp";
        mesh.indexFormat = vertexCount <= 65535 ? IndexFormat.UInt16 : IndexFormat.UInt32;
        mesh.vertices = positions;
        mesh.normals = normals;
        mesh.SetUVs(0, new List<Vector4>(uvs));
        mesh.subMeshCount = 2;
        mesh.SetTriangles(surfaceTriangles, 0);
        mesh.SetTriangles(wallTriangles, 1);
        mesh.RecalculateBounds();
        return mesh;
    }

    // One chain per side (left, right), one point per wall row, for the edge-fringe dressing.
    public static List<TerraceOutlineChain> BuildSideWallOutlineChains(
        in TerraceRampFootprint ramp,
        Vector3 tileOrigin,
        float topRoundMetres,
        float footRoundMetres)
    {
        int wallCellsAlong = WallCellsAlong(ramp);
        int wallRowCount = wallCellsAlong + 1;
        Vector2 forward = ForwardXZ(ramp.yawDegrees);
        Vector2 right = RightXZ(ramp.yawDegrees);
        List<TerraceOutlineChain> chains = new List<TerraceOutlineChain>(2);
        for (int side = 0; side < 2; side++)
        {
            bool isRightSide = side == 1;
            float acrossFraction = isRightSide ? 1f : 0f;
            Vector2 outwardXZ = isRightSide ? right : -right;
            TerraceOutlineChain chain = new TerraceOutlineChain();
            chain.points = new Vector3[wallRowCount];
            chain.bottomHeights = new float[wallRowCount];
            chain.startArcPhase = 0f;
            chain.outwardNormals = new Vector2[wallRowCount];
            chain.topRoundMetres = new float[wallRowCount];
            chain.footOffsetMetres = new float[wallRowCount];
            chain.footHeights = new float[wallRowCount];
            float bottomHeight = ramp.lowerHeight - tileOrigin.y;
            for (int row = 0; row < wallRowCount; row++)
            {
                float alongFraction = (float)row / wallCellsAlong;
                float wallHeight = HeightAt(ramp, alongFraction) - ramp.lowerHeight;
                float topRound = Mathf.Min(topRoundMetres, 0.3f * wallHeight);
                float footRound = Mathf.Min(footRoundMetres, 0.3f * wallHeight);
                float batter = 0.08f * wallHeight;
                Vector2 worldXZ = PointXZ(ramp, forward, right, alongFraction, acrossFraction);
                chain.points[row] = ToTileLocal(worldXZ, HeightAt(ramp, alongFraction), tileOrigin);
                chain.bottomHeights[row] = bottomHeight;
                chain.outwardNormals[row] = outwardXZ;
                chain.topRoundMetres[row] = wallHeight < 0.05f ? 0f : topRound;
                chain.footOffsetMetres[row] = topRound + batter + footRound;
                chain.footHeights[row] = bottomHeight + 0.03f;
            }

            chains.Add(chain);
        }

        return chains;
    }

    private static int WallCellsAlong(in TerraceRampFootprint ramp)
    {
        int cellsAlong = Mathf.Max(1, Mathf.CeilToInt(ramp.runMetres));
        return Mathf.Max(cellsAlong, Mathf.CeilToInt(ramp.runMetres * WallRowsPerMetre));
    }

    private static void WriteWallVertex(
        Vector3[] positions,
        Vector3[] normals,
        Vector4[] uvs,
        int vertexIndex,
        Vector2 edgeWorldXZ,
        float topHeight,
        Vector3 tileOrigin,
        Vector2 outwardXZ,
        float arc,
        float outDistance,
        float downDistance,
        float normalOut,
        float normalUp,
        float alongMetres,
        float topGrassEndArc,
        float footGrassStartArc)
    {
        Vector2 worldXZ = edgeWorldXZ + outwardXZ * outDistance;
        positions[vertexIndex] = ToTileLocal(worldXZ, topHeight - downDistance, tileOrigin);
        normals[vertexIndex] = new Vector3(outwardXZ.x * normalOut, normalUp, outwardXZ.y * normalOut).normalized;
        uvs[vertexIndex] = new Vector4(
            arc,
            alongMetres,
            ProfiledVertexUvOffset + arc - topGrassEndArc,
            ProfiledVertexUvOffset + footGrassStartArc - arc);
    }

    private static void AddOrientedTriangle(
        List<int> triangles,
        Vector3[] positions,
        Vector3[] normals,
        int first,
        int second,
        int third)
    {
        Vector3 geometricNormal = Vector3.Cross(positions[second] - positions[first], positions[third] - positions[first]);
        float agreement = Vector3.Dot(geometricNormal, normals[first] + normals[second] + normals[third]);
        triangles.Add(first);
        if (agreement >= 0f)
        {
            triangles.Add(second);
            triangles.Add(third);
        }
        else
        {
            triangles.Add(third);
            triangles.Add(second);
        }
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
