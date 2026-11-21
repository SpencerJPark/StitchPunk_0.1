using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Look spike only: terraced plateau geometry (outline tracing, rounding, wall/cap/lip/foot meshes).
public static class TerraceSpikeGeometry
{
    public static List<List<Vector2>> TraceLevelOutlines(int[,] cellLevels, int threshold, float cellSizeMetres)
    {
        int sizeX = cellLevels.GetLength(0);
        int sizeZ = cellLevels.GetLength(1);
        Dictionary<Vector2Int, List<Vector2Int>> outgoingEdges = new Dictionary<Vector2Int, List<Vector2Int>>();
        List<Vector2Int> edgeStartOrder = new List<Vector2Int>();

        for (int cellX = 0; cellX < sizeX; cellX++)
        {
            for (int cellZ = 0; cellZ < sizeZ; cellZ++)
            {
                if (cellLevels[cellX, cellZ] < threshold) continue;
                if (!IsInside(cellLevels, cellX, cellZ - 1, threshold))
                    AddEdge(outgoingEdges, edgeStartOrder, new Vector2Int(cellX, cellZ), new Vector2Int(cellX + 1, cellZ));
                if (!IsInside(cellLevels, cellX + 1, cellZ, threshold))
                    AddEdge(outgoingEdges, edgeStartOrder, new Vector2Int(cellX + 1, cellZ), new Vector2Int(cellX + 1, cellZ + 1));
                if (!IsInside(cellLevels, cellX, cellZ + 1, threshold))
                    AddEdge(outgoingEdges, edgeStartOrder, new Vector2Int(cellX + 1, cellZ + 1), new Vector2Int(cellX, cellZ + 1));
                if (!IsInside(cellLevels, cellX - 1, cellZ, threshold))
                    AddEdge(outgoingEdges, edgeStartOrder, new Vector2Int(cellX, cellZ + 1), new Vector2Int(cellX, cellZ));
            }
        }

        List<List<Vector2>> loops = new List<List<Vector2>>();
        foreach (Vector2Int startVertex in edgeStartOrder)
        {
            if (outgoingEdges[startVertex].Count == 0) continue;
            List<Vector2> loop = new List<Vector2>();
            Vector2Int current = startVertex;
            do
            {
                loop.Add(new Vector2(current.x, current.y) * cellSizeMetres);
                List<Vector2Int> candidates = outgoingEdges[current];
                if (candidates.Count == 0) break;
                Vector2Int next = candidates[candidates.Count - 1];
                candidates.RemoveAt(candidates.Count - 1);
                current = next;
            }
            while (current != startVertex);
            if (loop.Count >= 3) loops.Add(loop);
        }
        return loops;
    }

    static bool IsInside(int[,] cellLevels, int cellX, int cellZ, int threshold)
    {
        if (cellX < 0 || cellZ < 0 || cellX >= cellLevels.GetLength(0) || cellZ >= cellLevels.GetLength(1)) return false;
        return cellLevels[cellX, cellZ] >= threshold;
    }

    static void AddEdge(Dictionary<Vector2Int, List<Vector2Int>> outgoingEdges, List<Vector2Int> edgeStartOrder, Vector2Int start, Vector2Int end)
    {
        if (!outgoingEdges.TryGetValue(start, out List<Vector2Int> endList))
        {
            endList = new List<Vector2Int>();
            outgoingEdges[start] = endList;
            edgeStartOrder.Add(start);
        }
        endList.Add(end);
    }

    public static List<Vector2> RoundOutline(List<Vector2> loop, float simplifyToleranceMetres, int chaikinIterations)
    {
        List<Vector2> simplified = SimplifyClosedLoop(loop, simplifyToleranceMetres);
        for (int iteration = 0; iteration < chaikinIterations; iteration++)
        {
            List<Vector2> cut = new List<Vector2>(simplified.Count * 2);
            for (int pointIndex = 0; pointIndex < simplified.Count; pointIndex++)
            {
                Vector2 current = simplified[pointIndex];
                Vector2 next = simplified[(pointIndex + 1) % simplified.Count];
                cut.Add(Vector2.Lerp(current, next, 0.25f));
                cut.Add(Vector2.Lerp(current, next, 0.75f));
            }
            simplified = cut;
        }
        return simplified;
    }

    static List<Vector2> SimplifyClosedLoop(List<Vector2> loop, float toleranceMetres)
    {
        if (loop.Count < 4) return new List<Vector2>(loop);
        int farthestIndex = 0;
        float farthestDistance = -1f;
        for (int pointIndex = 1; pointIndex < loop.Count; pointIndex++)
        {
            float distance = (loop[pointIndex] - loop[0]).sqrMagnitude;
            if (distance > farthestDistance) { farthestDistance = distance; farthestIndex = pointIndex; }
        }
        List<Vector2> firstHalf = loop.GetRange(0, farthestIndex + 1);
        List<Vector2> secondHalf = loop.GetRange(farthestIndex, loop.Count - farthestIndex);
        secondHalf.Add(loop[0]);
        List<Vector2> result = SimplifyOpen(firstHalf, toleranceMetres);
        List<Vector2> second = SimplifyOpen(secondHalf, toleranceMetres);
        result.RemoveAt(result.Count - 1);
        second.RemoveAt(second.Count - 1);
        result.AddRange(second);
        return result;
    }

    static List<Vector2> SimplifyOpen(List<Vector2> points, float toleranceMetres)
    {
        bool[] keep = new bool[points.Count];
        keep[0] = true;
        keep[points.Count - 1] = true;
        MarkKeptPoints(points, 0, points.Count - 1, toleranceMetres, keep);
        List<Vector2> result = new List<Vector2>();
        for (int pointIndex = 0; pointIndex < points.Count; pointIndex++)
            if (keep[pointIndex]) result.Add(points[pointIndex]);
        return result;
    }

    static void MarkKeptPoints(List<Vector2> points, int startIndex, int endIndex, float toleranceMetres, bool[] keep)
    {
        if (endIndex <= startIndex + 1) return;
        Vector2 lineStart = points[startIndex];
        Vector2 lineDirection = points[endIndex] - lineStart;
        float lineLength = lineDirection.magnitude;
        int worstIndex = -1;
        float worstDistance = toleranceMetres;
        for (int pointIndex = startIndex + 1; pointIndex < endIndex; pointIndex++)
        {
            Vector2 offset = points[pointIndex] - lineStart;
            float distance = lineLength < 1e-5f
                ? offset.magnitude
                : Mathf.Abs(offset.x * lineDirection.y - offset.y * lineDirection.x) / lineLength;
            if (distance > worstDistance) { worstDistance = distance; worstIndex = pointIndex; }
        }
        if (worstIndex < 0) return;
        keep[worstIndex] = true;
        MarkKeptPoints(points, startIndex, worstIndex, toleranceMetres, keep);
        MarkKeptPoints(points, worstIndex, endIndex, toleranceMetres, keep);
    }

    // Loop is counter-clockwise, so outward is the right-hand side of travel.
    static Vector2[] ComputeOutwardNormals(List<Vector2> loop)
    {
        Vector2[] normals = new Vector2[loop.Count];
        for (int pointIndex = 0; pointIndex < loop.Count; pointIndex++)
        {
            Vector2 previous = loop[(pointIndex + loop.Count - 1) % loop.Count];
            Vector2 next = loop[(pointIndex + 1) % loop.Count];
            Vector2 travel = (next - previous).normalized;
            normals[pointIndex] = new Vector2(travel.y, -travel.x);
        }
        return normals;
    }

    // Length n + 1: the last entry is the full perimeter so the seam vertex does not wrap.
    static float[] ComputeArcLengths(List<Vector2> loop)
    {
        float[] arcLengths = new float[loop.Count + 1];
        for (int pointIndex = 1; pointIndex <= loop.Count; pointIndex++)
            arcLengths[pointIndex] = arcLengths[pointIndex - 1] + Vector2.Distance(loop[pointIndex - 1], loop[pointIndex % loop.Count]);
        return arcLengths;
    }

    static Mesh FinishMesh(string meshName, List<Vector3> positions, List<Vector3> normals, List<Vector2> uvs, List<Color> colors, List<int> indices)
    {
        Mesh mesh = new Mesh { name = meshName, indexFormat = IndexFormat.UInt32 };
        mesh.SetVertices(positions);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetColors(colors);
        mesh.SetTriangles(indices, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    public static Mesh BuildWallMesh(List<Vector2> loop, float bottomY, float topY, float wallJitterMetres, int seed)
    {
        const int ringCount = 4;
        int columnCount = loop.Count + 1;
        Vector2[] outward = ComputeOutwardNormals(loop);
        float[] arcLengths = ComputeArcLengths(loop);
        List<Vector3> positions = new List<Vector3>();
        List<Vector3> normals = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<Color> colors = new List<Color>();
        List<int> indices = new List<int>();

        for (int ringIndex = 0; ringIndex < ringCount; ringIndex++)
        {
            float ringFraction = ringIndex / (float)(ringCount - 1);
            float height = Mathf.Lerp(bottomY, topY, ringFraction);
            for (int columnIndex = 0; columnIndex < columnCount; columnIndex++)
            {
                int pointIndex = columnIndex % loop.Count;
                float noise = Mathf.PerlinNoise(arcLengths[pointIndex] * 0.35f + seed * 7.1f, seed * 0.37f + ringIndex * 0.9f) - 0.5f;
                Vector2 pushed = loop[pointIndex] + outward[pointIndex] * (noise * 2f * wallJitterMetres);
                positions.Add(new Vector3(pushed.x, height, pushed.y));
                normals.Add(new Vector3(outward[pointIndex].x, 0f, outward[pointIndex].y));
                uvs.Add(new Vector2(topY - height, arcLengths[columnIndex]));
                colors.Add(new Color(0f, 0f, 0f, 1f));
            }
        }

        for (int ringIndex = 0; ringIndex < ringCount - 1; ringIndex++)
        {
            for (int columnIndex = 0; columnIndex < columnCount - 1; columnIndex++)
            {
                int bottomLeft = ringIndex * columnCount + columnIndex;
                int bottomRight = bottomLeft + 1;
                int topLeft = bottomLeft + columnCount;
                int topRight = topLeft + 1;
                indices.AddRange(new[] { bottomLeft, topLeft, topRight, bottomLeft, topRight, bottomRight });
            }
        }
        return FinishMesh("TerraceWall", positions, normals, uvs, colors, indices);
    }

    public static Mesh BuildCapMesh(List<Vector2> loop, float heightY)
    {
        List<Vector3> positions = new List<Vector3>();
        List<Vector3> normals = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<Color> colors = new List<Color>();
        foreach (Vector2 point in loop)
        {
            positions.Add(new Vector3(point.x, heightY, point.y));
            normals.Add(Vector3.up);
            uvs.Add(point);
            colors.Add(Color.white);
        }

        List<int> remaining = new List<int>();
        for (int pointIndex = 0; pointIndex < loop.Count; pointIndex++) remaining.Add(pointIndex);
        List<int> indices = new List<int>();
        int guard = loop.Count * loop.Count;
        while (remaining.Count > 3 && guard-- > 0)
        {
            int earPosition = FindEarPosition(loop, remaining);
            int previous = remaining[(earPosition + remaining.Count - 1) % remaining.Count];
            int current = remaining[earPosition];
            int next = remaining[(earPosition + 1) % remaining.Count];
            // Counter-clockwise (x,z) triangle; reversed so it faces up in Unity's clockwise convention.
            indices.AddRange(new[] { previous, next, current });
            remaining.RemoveAt(earPosition);
        }
        if (remaining.Count == 3) indices.AddRange(new[] { remaining[0], remaining[2], remaining[1] });
        return FinishMesh("TerraceCap", positions, normals, uvs, colors, indices);
    }

    static int FindEarPosition(List<Vector2> loop, List<int> remaining)
    {
        for (int earPosition = 0; earPosition < remaining.Count; earPosition++)
        {
            Vector2 previous = loop[remaining[(earPosition + remaining.Count - 1) % remaining.Count]];
            Vector2 current = loop[remaining[earPosition]];
            Vector2 next = loop[remaining[(earPosition + 1) % remaining.Count]];
            if (Cross(current - previous, next - current) <= 1e-6f) continue;
            bool containsOtherPoint = false;
            for (int otherPosition = 0; otherPosition < remaining.Count && !containsOtherPoint; otherPosition++)
            {
                if (otherPosition == earPosition || otherPosition == (earPosition + 1) % remaining.Count
                    || otherPosition == (earPosition + remaining.Count - 1) % remaining.Count) continue;
                containsOtherPoint = IsInsideTriangle(loop[remaining[otherPosition]], previous, current, next);
            }
            if (!containsOtherPoint) return earPosition;
        }
        return 0;
    }

    static float Cross(Vector2 first, Vector2 second)
    {
        return first.x * second.y - first.y * second.x;
    }

    static bool IsInsideTriangle(Vector2 point, Vector2 cornerA, Vector2 cornerB, Vector2 cornerC)
    {
        return Cross(cornerB - cornerA, point - cornerA) >= 0f
            && Cross(cornerC - cornerB, point - cornerB) >= 0f
            && Cross(cornerA - cornerC, point - cornerC) >= 0f;
    }

    public static Mesh BuildLipMesh(List<Vector2> loop, float heightY, float outwardMetres, float inwardMetres, float fringeSizeMetres)
    {
        float[] arcLengths = ComputeArcLengths(loop);
        return BuildEdgeStrip("TerraceLip", loop, arcLengths,
            heightY + 0.02f, -inwardMetres, heightY + 0.02f - 0.15f, outwardMetres,
            columnIndex => new Vector2(arcLengths[columnIndex] / fringeSizeMetres, 0.25f),
            columnIndex => new Vector2(arcLengths[columnIndex] / fringeSizeMetres, 0f),
            Color.white, Color.white);
    }

    public static Mesh BuildFootMesh(List<Vector2> loop, float groundY, float outwardMetres)
    {
        float[] arcLengths = ComputeArcLengths(loop);
        return BuildEdgeStrip("TerraceFoot", loop, arcLengths,
            groundY + 0.02f, 0f, groundY + 0.02f, outwardMetres,
            columnIndex => Vector2.zero, columnIndex => Vector2.one,
            new Color(0f, 0f, 0f, 0.55f), new Color(0f, 0f, 0f, 0f));
    }

    // Inner/outer offsets are signed metres along the outward normal; strip faces up.
    static Mesh BuildEdgeStrip(string meshName, List<Vector2> loop, float[] arcLengths,
        float innerY, float innerOffset, float outerY, float outerOffset,
        System.Func<int, Vector2> innerUv, System.Func<int, Vector2> outerUv, Color innerColor, Color outerColor)
    {
        Vector2[] outward = ComputeOutwardNormals(loop);
        int columnCount = loop.Count + 1;
        List<Vector3> positions = new List<Vector3>();
        List<Vector3> normals = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<Color> colors = new List<Color>();
        List<int> indices = new List<int>();
        for (int columnIndex = 0; columnIndex < columnCount; columnIndex++)
        {
            int pointIndex = columnIndex % loop.Count;
            Vector2 inner = loop[pointIndex] + outward[pointIndex] * innerOffset;
            Vector2 outer = loop[pointIndex] + outward[pointIndex] * outerOffset;
            positions.Add(new Vector3(inner.x, innerY, inner.y));
            positions.Add(new Vector3(outer.x, outerY, outer.y));
            normals.Add(Vector3.up);
            normals.Add(Vector3.up);
            uvs.Add(innerUv(columnIndex));
            uvs.Add(outerUv(columnIndex));
            colors.Add(innerColor);
            colors.Add(outerColor);
        }
        for (int columnIndex = 0; columnIndex < columnCount - 1; columnIndex++)
        {
            int innerCurrent = columnIndex * 2;
            int outerCurrent = innerCurrent + 1;
            int innerNext = innerCurrent + 2;
            int outerNext = innerCurrent + 3;
            indices.AddRange(new[] { innerCurrent, innerNext, outerNext, innerCurrent, outerNext, outerCurrent });
        }
        return FinishMesh(meshName, positions, normals, uvs, colors, indices);
    }
}
