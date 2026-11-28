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
    public readonly TerraceRampKind kind;
    public readonly bool risesToTheRight;

    public TerraceRampFootprint(
        Vector2 centre,
        float yawDegrees,
        float widthMetres,
        float runMetres,
        float lowerHeight,
        float upperHeight,
        TerraceRampKind kind = TerraceRampKind.Straight,
        bool risesToTheRight = true)
    {
        this.centre = centre;
        this.yawDegrees = yawDegrees;
        this.widthMetres = widthMetres;
        this.runMetres = runMetres;
        this.lowerHeight = lowerHeight;
        this.upperHeight = upperHeight;
        this.kind = kind;
        this.risesToTheRight = risesToTheRight;
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
    private const float SkirtSampleSpacingMetres = 0.5f;
    private const float SkirtOutwardProbeMetres = 0.35f;
    public const float AlongCliffLandingMetres = 2f;

    // AlongCliff keeps no wall on the cliff side: left of forward when it rises to the right, right of forward otherwise.
    public static bool HasSideWall(in TerraceRampFootprint ramp, bool rightSide)
    {
        if (ramp.kind != TerraceRampKind.AlongCliff)
        {
            return true;
        }

        bool cliffIsOnRightSide = !ramp.risesToTheRight;
        return rightSide != cliffIsOnRightSide;
    }

    // Straight ramps cut through the cliff, so the whole footprint clips the wall; an AlongCliff ramp only needs
    // the wall removed beside its flat landing.
    public static void GetWallClipCornersXZ(in TerraceRampFootprint ramp, Vector2[] fourCorners)
    {
        GetFootprintCornersXZ(ramp, fourCorners);
        if (ramp.kind != TerraceRampKind.AlongCliff)
        {
            return;
        }

        Vector2 forward = ForwardXZ(ramp.yawDegrees);
        Vector2 landingStartOffset = forward * (LandingMetres(ramp));
        Vector2 upperLeft = fourCorners[3];
        Vector2 upperRight = fourCorners[2];
        fourCorners[0] = upperLeft - landingStartOffset;
        fourCorners[1] = upperRight - landingStartOffset;
    }

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
        return BuildRampMesh(ramp, tileOrigin, topRoundMetres, footRoundMetres, null, null);
    }

    // baseHeight follows the terrain under the ramp; groundHeight (terrace level included) drives the edge skirts.
    public static Mesh BuildRampMesh(
        in TerraceRampFootprint ramp,
        Vector3 tileOrigin,
        float topRoundMetres,
        float footRoundMetres,
        System.Func<Vector2, float> baseHeightAtWorldXZ,
        System.Func<Vector2, float> groundHeightAtWorldXZ)
    {
        float baseAtLowerEnd = 0f;
        float baseAtUpperEnd = 0f;
        if (baseHeightAtWorldXZ != null)
        {
            Vector2 endForward = ForwardXZ(ramp.yawDegrees);
            baseAtLowerEnd = baseHeightAtWorldXZ(ramp.centre - endForward * (ramp.runMetres * 0.5f));
            baseAtUpperEnd = baseHeightAtWorldXZ(ramp.centre + endForward * (ramp.runMetres * 0.5f));
        }

        int cellsAlong = Mathf.Max(1, Mathf.CeilToInt(ramp.runMetres));
        int cellsAcross = Mathf.Max(1, Mathf.CeilToInt(ramp.widthMetres));
        int rowCount = cellsAlong + 1;
        int columnCount = cellsAcross + 1;
        int surfaceVertexCount = rowCount * columnCount;
        int wallCellsAlong = WallCellsAlong(ramp);
        int wallRowCount = wallCellsAlong + 1;
        int wallSideCount = (HasSideWall(ramp, false) ? 1 : 0) + (HasSideWall(ramp, true) ? 1 : 0);
        int endWallRowCount = HasEndWall(ramp) ? EndWallCellsAcross(ramp) + 1 : 0;
        int sideWallVertexCount = wallSideCount * VerticesPerWallRow * wallRowCount;
        int vertexCount = surfaceVertexCount + sideWallVertexCount + endWallRowCount * VerticesPerWallRow;

        Vector2 forward = ForwardXZ(ramp.yawDegrees);
        Vector2 right = RightXZ(ramp.yawDegrees);
        float rise = ramp.upperHeight - ramp.lowerHeight;
        float slopeRunMetres = SlopeRunMetres(ramp);
        float slope = ramp.runMetres > 0f ? rise / slopeRunMetres : 0f;
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
                float surfaceHeight = AdjustedHeightAt(ramp, alongFraction, worldXZ, baseHeightAtWorldXZ, baseAtLowerEnd, baseAtUpperEnd);
                positions[vertexIndex] = ToTileLocal(worldXZ, surfaceHeight, tileOrigin);
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
        int emittedWallSides = 0;
        for (int side = 0; side < 2; side++)
        {
            bool isRightSide = side == 1;
            if (!HasSideWall(ramp, isRightSide))
            {
                continue;
            }

            float acrossFraction = isRightSide ? 1f : 0f;
            Vector2 outwardXZ = isRightSide ? right : -right;
            int wallBase = surfaceVertexCount + emittedWallSides * VerticesPerWallRow * wallRowCount;
            emittedWallSides++;
            float[] wallHeights = new float[wallRowCount];
            for (int row = 0; row < wallRowCount; row++)
            {
                float alongFraction = (float)row / wallCellsAlong;
                Vector2 worldXZ = PointXZ(ramp, forward, right, alongFraction, acrossFraction);
                float topHeight = AdjustedHeightAt(ramp, alongFraction, worldXZ, baseHeightAtWorldXZ, baseAtLowerEnd, baseAtUpperEnd);
                float wallHeight = topHeight - ramp.lowerHeight;
                wallHeights[row] = wallHeight;
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

        if (endWallRowCount > 0)
        {
            int endWallCells = endWallRowCount - 1;
            int endWallBase = surfaceVertexCount + sideWallVertexCount;
            float endWallHeight = ramp.upperHeight - ramp.lowerHeight;
            float topRound = Mathf.Min(topRoundMetres, 0.3f * endWallHeight);
            float footRound = Mathf.Min(footRoundMetres, 0.3f * endWallHeight);
            float batter = 0.08f * endWallHeight;
            float faceRun = Mathf.Max(0f, endWallHeight - footRound - topRound);
            float topGrassEndArc = topRound * Mathf.PI * 0.5f;
            float faceLength = Mathf.Sqrt(batter * batter + faceRun * faceRun);
            float footGrassStartArc = topGrassEndArc + faceLength;
            float faceNormalOut = faceLength > 1e-6f ? faceRun / faceLength : 1f;
            float faceNormalUp = faceLength > 1e-6f ? batter / faceLength : 0f;
            for (int row = 0; row < endWallRowCount; row++)
            {
                float acrossFraction = (float)row / endWallCells;
                Vector2 worldXZ = PointXZ(ramp, forward, right, 1f, acrossFraction);
                float endTopHeight = AdjustedHeightAt(ramp, 1f, worldXZ, baseHeightAtWorldXZ, baseAtLowerEnd, baseAtUpperEnd);
                float acrossMetres = acrossFraction * ramp.widthMetres;
                int rowBase = endWallBase + row * VerticesPerWallRow;
                for (int step = 0; step <= FilletSteps; step++)
                {
                    float angle = (float)step / FilletSteps * Mathf.PI * 0.5f;
                    WriteWallVertex(positions, normals, uvs, rowBase + step, worldXZ, endTopHeight, tileOrigin, forward,
                        topRound * angle, topRound * Mathf.Sin(angle), topRound * (1f - Mathf.Cos(angle)),
                        Mathf.Sin(angle), Mathf.Cos(angle), acrossMetres, topGrassEndArc, footGrassStartArc);
                }

                WriteWallVertex(positions, normals, uvs, rowBase + FilletSteps + 1, worldXZ, endTopHeight, tileOrigin, forward,
                    topGrassEndArc, topRound, topRound, faceNormalOut, faceNormalUp, acrossMetres, topGrassEndArc, footGrassStartArc);
                WriteWallVertex(positions, normals, uvs, rowBase + FilletSteps + 2, worldXZ, endTopHeight, tileOrigin, forward,
                    footGrassStartArc, topRound + batter, endWallHeight - footRound, faceNormalOut, faceNormalUp,
                    acrossMetres, topGrassEndArc, footGrassStartArc);
                for (int step = 0; step <= FilletSteps; step++)
                {
                    float angle = (float)step / FilletSteps * Mathf.PI * 0.5f;
                    WriteWallVertex(positions, normals, uvs, rowBase + FilletSteps + 3 + step, worldXZ, endTopHeight, tileOrigin, forward,
                        footGrassStartArc + footRound * angle,
                        topRound + batter + footRound * (1f - Mathf.Cos(angle)),
                        endWallHeight - footRound + footRound * Mathf.Sin(angle),
                        Mathf.Cos(angle), Mathf.Sin(angle), acrossMetres, topGrassEndArc, footGrassStartArc);
                }
            }

            if (endWallHeight >= 0.01f)
            {
                for (int row = 0; row < endWallCells; row++)
                {
                    for (int group = 0; group < 3; group++)
                    {
                        for (int step = 0; step < groupRowCounts[group] - 1; step++)
                        {
                            int nearUpper = endWallBase + row * VerticesPerWallRow + groupStarts[group] + step;
                            int farUpper = nearUpper + VerticesPerWallRow;
                            AddOrientedTriangle(wallTriangles, positions, normals, nearUpper, farUpper, nearUpper + 1);
                            AddOrientedTriangle(wallTriangles, positions, normals, farUpper, farUpper + 1, nearUpper + 1);
                        }
                    }
                }
            }
        }

        if (baseHeightAtWorldXZ != null)
        {
            RecomputeSurfaceNormals(positions, normals, rowCount, columnCount);
        }

        if (groundHeightAtWorldXZ != null)
        {
            List<Vector3> skirtPositions = new List<Vector3>();
            List<Vector3> skirtNormals = new List<Vector3>();
            List<int> skirtTriangles = new List<int>();
            for (int edge = 0; edge < 4; edge++)
            {
                // 0 left, 1 right, 2 lower end, 3 upper end.
                bool isSideEdge = edge < 2;
                bool edgeHasWall = isSideEdge ? HasSideWall(ramp, edge == 1) : (edge == 3 && HasEndWall(ramp));
                Vector2 outwardXZ = edge == 0 ? -right : edge == 1 ? right : edge == 2 ? -forward : forward;
                float edgeLengthMetres = isSideEdge ? ramp.runMetres : ramp.widthMetres;
                int segmentCount = Mathf.Max(1, Mathf.CeilToInt(edgeLengthMetres / SkirtSampleSpacingMetres));
                float[] sampleTops = new float[segmentCount + 1];
                float[] sampleBottoms = new float[segmentCount + 1];
                bool[] sampleEmits = new bool[segmentCount + 1];
                Vector2[] samplePoints = new Vector2[segmentCount + 1];
                for (int sample = 0; sample <= segmentCount; sample++)
                {
                    float fraction = (float)sample / segmentCount;
                    float alongFraction = edge == 2 ? 0f : edge == 3 ? 1f : fraction;
                    float acrossFraction = edge == 0 ? 0f : edge == 1 ? 1f : fraction;
                    Vector2 edgeXZ = PointXZ(ramp, forward, right, alongFraction, acrossFraction);
                    float rampEdgeHeight = AdjustedHeightAt(ramp, alongFraction, edgeXZ, baseHeightAtWorldXZ, baseAtLowerEnd, baseAtUpperEnd);
                    float groundHeight = groundHeightAtWorldXZ(edgeXZ + outwardXZ * SkirtOutwardProbeMetres);
                    samplePoints[sample] = edgeXZ;
                    sampleTops[sample] = Mathf.Max(rampEdgeHeight, groundHeight) + 0.02f;
                    sampleBottoms[sample] = Mathf.Min(rampEdgeHeight, groundHeight) - 0.3f;
                    sampleEmits[sample] = !edgeHasWall || groundHeight > rampEdgeHeight;
                }

                Vector3 outwardNormal = new Vector3(outwardXZ.x, 0f, outwardXZ.y);
                for (int segment = 0; segment < segmentCount; segment++)
                {
                    if (!sampleEmits[segment] && !sampleEmits[segment + 1])
                    {
                        continue;
                    }

                    Vector3 topNear = ToTileLocal(samplePoints[segment], sampleTops[segment], tileOrigin);
                    Vector3 bottomNear = ToTileLocal(samplePoints[segment], sampleBottoms[segment], tileOrigin);
                    Vector3 topFar = ToTileLocal(samplePoints[segment + 1], sampleTops[segment + 1], tileOrigin);
                    Vector3 bottomFar = ToTileLocal(samplePoints[segment + 1], sampleBottoms[segment + 1], tileOrigin);
                    bool frontIsCounterClockwise = Vector3.Dot(Vector3.Cross(bottomNear - topNear, topFar - topNear), outwardNormal) >= 0f;
                    for (int faceSide = 0; faceSide < 2; faceSide++)
                    {
                        int firstIndex = vertexCount + skirtPositions.Count;
                        Vector3 faceNormal = faceSide == 0 ? outwardNormal : -outwardNormal;
                        skirtPositions.Add(topNear);
                        skirtPositions.Add(bottomNear);
                        skirtPositions.Add(topFar);
                        skirtPositions.Add(bottomFar);
                        for (int corner = 0; corner < 4; corner++)
                        {
                            skirtNormals.Add(faceNormal);
                        }

                        bool windCounterClockwise = frontIsCounterClockwise == (faceSide == 0);
                        skirtTriangles.Add(firstIndex);
                        skirtTriangles.Add(windCounterClockwise ? firstIndex + 1 : firstIndex + 2);
                        skirtTriangles.Add(windCounterClockwise ? firstIndex + 2 : firstIndex + 1);
                        skirtTriangles.Add(firstIndex + 2);
                        skirtTriangles.Add(windCounterClockwise ? firstIndex + 1 : firstIndex + 3);
                        skirtTriangles.Add(windCounterClockwise ? firstIndex + 3 : firstIndex + 1);
                    }
                }
            }

            if (skirtPositions.Count > 0)
            {
                int skirtVertexCount = skirtPositions.Count;
                Vector3[] allPositions = new Vector3[vertexCount + skirtVertexCount];
                Vector3[] allNormals = new Vector3[vertexCount + skirtVertexCount];
                Vector4[] allUvs = new Vector4[vertexCount + skirtVertexCount];
                positions.CopyTo(allPositions, 0);
                normals.CopyTo(allNormals, 0);
                uvs.CopyTo(allUvs, 0);
                for (int skirtVertex = 0; skirtVertex < skirtVertexCount; skirtVertex++)
                {
                    allPositions[vertexCount + skirtVertex] = skirtPositions[skirtVertex];
                    allNormals[vertexCount + skirtVertex] = skirtNormals[skirtVertex];
                    allUvs[vertexCount + skirtVertex] = new Vector4(0f, allPositions[vertexCount + skirtVertex].y, 0f, 0f);
                }

                positions = allPositions;
                normals = allNormals;
                uvs = allUvs;
                wallTriangles.AddRange(skirtTriangles);
            }
        }

        Mesh mesh = new Mesh();
        mesh.name = "TerraceRamp";
        mesh.indexFormat = positions.Length <= 65535 ? IndexFormat.UInt16 : IndexFormat.UInt32;
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
            if (!HasSideWall(ramp, isRightSide))
            {
                continue;
            }

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

        if (HasEndWall(ramp))
        {
            int endRowCount = EndWallCellsAcross(ramp) + 1;
            float endWallHeight = ramp.upperHeight - ramp.lowerHeight;
            float topRound = Mathf.Min(topRoundMetres, 0.3f * endWallHeight);
            float footRound = Mathf.Min(footRoundMetres, 0.3f * endWallHeight);
            float batter = 0.08f * endWallHeight;
            float bottomHeight = ramp.lowerHeight - tileOrigin.y;
            TerraceOutlineChain endChain = new TerraceOutlineChain();
            endChain.points = new Vector3[endRowCount];
            endChain.bottomHeights = new float[endRowCount];
            endChain.startArcPhase = 0f;
            endChain.outwardNormals = new Vector2[endRowCount];
            endChain.topRoundMetres = new float[endRowCount];
            endChain.footOffsetMetres = new float[endRowCount];
            endChain.footHeights = new float[endRowCount];
            for (int row = 0; row < endRowCount; row++)
            {
                float acrossFraction = (float)row / (endRowCount - 1);
                Vector2 worldXZ = PointXZ(ramp, forward, right, 1f, acrossFraction);
                endChain.points[row] = ToTileLocal(worldXZ, ramp.upperHeight, tileOrigin);
                endChain.bottomHeights[row] = bottomHeight;
                endChain.outwardNormals[row] = forward;
                endChain.topRoundMetres[row] = endWallHeight < 0.05f ? 0f : topRound;
                endChain.footOffsetMetres[row] = topRound + batter + footRound;
                endChain.footHeights[row] = bottomHeight + 0.03f;
            }

            chains.Add(endChain);
        }

        return chains;
    }

    private static bool HasEndWall(in TerraceRampFootprint ramp)
    {
        return ramp.kind == TerraceRampKind.AlongCliff;
    }

    private static int EndWallCellsAcross(in TerraceRampFootprint ramp)
    {
        return Mathf.Max(1, Mathf.CeilToInt(ramp.widthMetres * WallRowsPerMetre));
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
        float slopeRunMetres = SlopeRunMetres(ramp);
        if (slopeRunMetres >= ramp.runMetres)
        {
            return Mathf.Lerp(ramp.lowerHeight, ramp.upperHeight, alongFraction);
        }

        float slopeFraction = Mathf.Clamp01(alongFraction * ramp.runMetres / slopeRunMetres);
        return Mathf.Lerp(ramp.lowerHeight, ramp.upperHeight, slopeFraction);
    }

    private static float AdjustedHeightAt(
        in TerraceRampFootprint ramp,
        float alongFraction,
        Vector2 worldXZ,
        System.Func<Vector2, float> baseHeightAtWorldXZ,
        float baseAtLowerEnd,
        float baseAtUpperEnd)
    {
        float height = HeightAt(ramp, alongFraction);
        if (baseHeightAtWorldXZ == null)
        {
            return height;
        }

        return height + baseHeightAtWorldXZ(worldXZ) - Mathf.Lerp(baseAtLowerEnd, baseAtUpperEnd, alongFraction);
    }

    private static void RecomputeSurfaceNormals(Vector3[] positions, Vector3[] normals, int rowCount, int columnCount)
    {
        for (int row = 0; row < rowCount; row++)
        {
            int previousRow = Mathf.Max(row - 1, 0);
            int nextRow = Mathf.Min(row + 1, rowCount - 1);
            for (int column = 0; column < columnCount; column++)
            {
                int previousColumn = Mathf.Max(column - 1, 0);
                int nextColumn = Mathf.Min(column + 1, columnCount - 1);
                Vector3 alongTangent = positions[nextRow * columnCount + column] - positions[previousRow * columnCount + column];
                Vector3 acrossTangent = positions[row * columnCount + nextColumn] - positions[row * columnCount + previousColumn];
                Vector3 normal = Vector3.Cross(alongTangent, acrossTangent);
                if (normal.y < 0f)
                {
                    normal = -normal;
                }

                if (normal.sqrMagnitude > 1e-12f)
                {
                    normals[row * columnCount + column] = normal.normalized;
                }
            }
        }
    }

    private static float LandingMetres(in TerraceRampFootprint ramp)
    {
        if (ramp.kind != TerraceRampKind.AlongCliff)
        {
            return 0f;
        }

        return Mathf.Min(AlongCliffLandingMetres, ramp.runMetres * 0.5f);
    }

    private static float SlopeRunMetres(in TerraceRampFootprint ramp)
    {
        return Mathf.Max(ramp.runMetres - LandingMetres(ramp), 0.01f);
    }

    private static Vector3 ToTileLocal(Vector2 worldXZ, float height, Vector3 tileOrigin)
    {
        return new Vector3(worldXZ.x - tileOrigin.x, height - tileOrigin.y, worldXZ.y - tileOrigin.z);
    }
}
#endif
