using System;
using System.Collections.Generic;
using UnityEngine;

public struct TerrainPlacementResult
{
    public uint placementId;
    public Vector2 localXZ;
    public float yawDegrees;
    public float scale;
    public int placeableIndex;
    public bool isSegment;
    public Vector2 segmentEndLocalXZ;
}

public static class TerrainFeaturePlacement
{
    private const uint PostKind = 1u;
    private const uint SegmentKind = 2u;
    private const uint RowKind = 3u;
    private const float MinimumSpacingMetres = 0.01f;

    private struct PlannedPost
    {
        public Vector2 position;
        public int edgeIndex;
        public int stepIndex;
    }

    public static List<TerrainPlacementResult> PlanAlongPath(IReadOnlyList<Vector2> polylineLocalXZ, bool closedLoop, IReadOnlyList<int> gapSegmentIndices, TerrainAlongPathPlacer placer, int placeableCount, int seed)
    {
        List<TerrainPlacementResult> results = new List<TerrainPlacementResult>();
        if (polylineLocalXZ == null || polylineLocalXZ.Count < 2 || placer == null || placer.spacingMetres < MinimumSpacingMetres)
        {
            return results;
        }

        System.Random random = new System.Random(seed);
        int edgeCount = closedLoop ? polylineLocalXZ.Count : polylineLocalXZ.Count - 1;
        List<PlannedPost> posts = new List<PlannedPost>();
        float edgeStartDistance = 0f;
        float nextPostDistance = 0f;

        for (int edgeIndex = 0; edgeIndex < edgeCount; edgeIndex++)
        {
            Vector2 edgeStart = polylineLocalXZ[edgeIndex];
            Vector2 edgeEnd = polylineLocalXZ[(edgeIndex + 1) % polylineLocalXZ.Count];
            float edgeLength = (edgeEnd - edgeStart).magnitude;
            if (edgeLength < 1e-6f)
            {
                continue;
            }

            Vector2 tangent = (edgeEnd - edgeStart) / edgeLength;
            Vector2 leftNormal = new Vector2(-tangent.y, tangent.x);
            bool isLastEdge = edgeIndex == edgeCount - 1;
            float edgeEndDistance = edgeStartDistance + edgeLength;
            int stepIndex = 0;
            // An open path keeps a post that lands exactly on its final knot; a closed loop would duplicate the first post.
            float limit = isLastEdge && !closedLoop ? edgeEndDistance + 1e-4f : edgeEndDistance;
            while (nextPostDistance < limit)
            {
                float distanceOnEdge = Mathf.Clamp(nextPostDistance - edgeStartDistance, 0f, edgeLength);
                float jitterAlong = placer.jitterMetres > 0f ? (float)(random.NextDouble() * 2.0 - 1.0) * placer.jitterMetres : 0f;
                Vector2 position = edgeStart + tangent * (distanceOnEdge + jitterAlong) + leftNormal * placer.sideOffsetMetres;
                PlannedPost post = new PlannedPost();
                post.position = position;
                post.edgeIndex = edgeIndex;
                post.stepIndex = stepIndex;
                posts.Add(post);

                float yaw = placer.alignToPath ? YawDegreesOf(tangent) : (float)(random.NextDouble() * 360.0);
                TerrainPlacementResult result = new TerrainPlacementResult();
                result.placementId = HashId(edgeIndex, stepIndex, PostKind);
                result.localXZ = position;
                result.yawDegrees = yaw;
                result.scale = 1f;
                result.placeableIndex = PickPlaceableIndex(random, placeableCount);
                results.Add(result);

                stepIndex++;
                nextPostDistance += placer.spacingMetres;
            }

            edgeStartDistance = edgeEndDistance;
        }

        if (placer.mode != TerrainAlongPathMode.Segments)
        {
            return results;
        }

        int segmentCount = closedLoop ? posts.Count : posts.Count - 1;
        for (int postIndex = 0; postIndex < segmentCount; postIndex++)
        {
            PlannedPost startPost = posts[postIndex];
            PlannedPost endPost = posts[(postIndex + 1) % posts.Count];
            if (ContainsIndex(gapSegmentIndices, startPost.edgeIndex))
            {
                continue;
            }

            Vector2 chord = endPost.position - startPost.position;
            TerrainPlacementResult segment = new TerrainPlacementResult();
            segment.placementId = HashId(startPost.edgeIndex, startPost.stepIndex, SegmentKind);
            segment.localXZ = startPost.position;
            segment.segmentEndLocalXZ = endPost.position;
            segment.yawDegrees = chord.sqrMagnitude > 1e-10f ? YawDegreesOf(chord.normalized) : 0f;
            segment.scale = 1f;
            segment.placeableIndex = -1;
            segment.isSegment = true;
            results.Add(segment);
        }

        return results;
    }

    public static List<TerrainPlacementResult> PlanRows(IReadOnlyList<Vector2> closedOutlineLocalXZ, TerrainRowsPlacer placer, int placeableCount, int seed)
    {
        List<TerrainPlacementResult> results = new List<TerrainPlacementResult>();
        if (closedOutlineLocalXZ == null || closedOutlineLocalXZ.Count < 3 || placer == null
            || placer.rowSpacingMetres < MinimumSpacingMetres || placer.alongRowSpacingMetres < MinimumSpacingMetres)
        {
            return results;
        }

        System.Random random = new System.Random(seed);
        float directionDegrees = float.IsNaN(placer.rowDirectionDegrees) ? LongestEdgeDirectionDegrees(closedOutlineLocalXZ) : placer.rowDirectionDegrees;
        float directionRadians = directionDegrees * Mathf.Deg2Rad;
        Vector2 rowDirection = new Vector2(Mathf.Cos(directionRadians), Mathf.Sin(directionRadians));
        Vector2 rowPerpendicular = new Vector2(-rowDirection.y, rowDirection.x);

        float minAlong = float.MaxValue;
        float maxAlong = float.MinValue;
        float minAcross = float.MaxValue;
        float maxAcross = float.MinValue;
        for (int vertexIndex = 0; vertexIndex < closedOutlineLocalXZ.Count; vertexIndex++)
        {
            float along = Vector2.Dot(closedOutlineLocalXZ[vertexIndex], rowDirection);
            float across = Vector2.Dot(closedOutlineLocalXZ[vertexIndex], rowPerpendicular);
            minAlong = Mathf.Min(minAlong, along);
            maxAlong = Mathf.Max(maxAlong, along);
            minAcross = Mathf.Min(minAcross, across);
            maxAcross = Mathf.Max(maxAcross, across);
        }

        int firstRow = Mathf.FloorToInt(minAcross / placer.rowSpacingMetres);
        int lastRow = Mathf.CeilToInt(maxAcross / placer.rowSpacingMetres);
        int firstColumn = Mathf.FloorToInt(minAlong / placer.alongRowSpacingMetres);
        int lastColumn = Mathf.CeilToInt(maxAlong / placer.alongRowSpacingMetres);
        for (int rowIndex = firstRow; rowIndex <= lastRow; rowIndex++)
        {
            for (int columnIndex = firstColumn; columnIndex <= lastColumn; columnIndex++)
            {
                Vector2 gridPoint = rowDirection * (columnIndex * placer.alongRowSpacingMetres) + rowPerpendicular * (rowIndex * placer.rowSpacingMetres);
                if (!IsInsidePolygon(closedOutlineLocalXZ, gridPoint))
                {
                    continue;
                }

                Vector2 jitter = Vector2.zero;
                if (placer.jitterMetres > 0f)
                {
                    jitter = new Vector2((float)(random.NextDouble() * 2.0 - 1.0), (float)(random.NextDouble() * 2.0 - 1.0)) * placer.jitterMetres;
                }

                TerrainPlacementResult result = new TerrainPlacementResult();
                result.placementId = HashId(rowIndex, columnIndex, RowKind);
                result.localXZ = gridPoint + jitter;
                result.yawDegrees = directionDegrees;
                result.scale = 1f;
                result.placeableIndex = PickPlaceableIndex(random, placeableCount);
                results.Add(result);
            }
        }

        return results;
    }

    public static bool IsOwnedByTile(Vector2 worldXZ, Rect tileRectXZ)
    {
        return worldXZ.x >= tileRectXZ.xMin && worldXZ.x < tileRectXZ.xMax
            && worldXZ.y >= tileRectXZ.yMin && worldXZ.y < tileRectXZ.yMax;
    }

    // Unity yaw: rotation about +Y with 0 facing +Z, so a tangent (x, z) becomes atan2(x, z).
    private static float YawDegreesOf(Vector2 directionXZ)
    {
        return Mathf.Atan2(directionXZ.x, directionXZ.y) * Mathf.Rad2Deg;
    }

    private static int PickPlaceableIndex(System.Random random, int placeableCount)
    {
        return placeableCount <= 1 ? 0 : random.Next(placeableCount);
    }

    private static bool ContainsIndex(IReadOnlyList<int> indices, int wanted)
    {
        if (indices == null)
        {
            return false;
        }

        for (int listIndex = 0; listIndex < indices.Count; listIndex++)
        {
            if (indices[listIndex] == wanted)
            {
                return true;
            }
        }

        return false;
    }

    private static float LongestEdgeDirectionDegrees(IReadOnlyList<Vector2> outline)
    {
        float longestSquared = -1f;
        float directionDegrees = 0f;
        for (int vertexIndex = 0; vertexIndex < outline.Count; vertexIndex++)
        {
            Vector2 edge = outline[(vertexIndex + 1) % outline.Count] - outline[vertexIndex];
            if (edge.sqrMagnitude > longestSquared)
            {
                longestSquared = edge.sqrMagnitude;
                directionDegrees = Mathf.Atan2(edge.y, edge.x) * Mathf.Rad2Deg;
            }
        }

        return directionDegrees;
    }

    private static bool IsInsidePolygon(IReadOnlyList<Vector2> outline, Vector2 point)
    {
        bool inside = false;
        int previousIndex = outline.Count - 1;
        for (int vertexIndex = 0; vertexIndex < outline.Count; vertexIndex++)
        {
            Vector2 current = outline[vertexIndex];
            Vector2 previous = outline[previousIndex];
            bool straddles = (current.y > point.y) != (previous.y > point.y);
            if (straddles && point.x < (previous.x - current.x) * (point.y - current.y) / (previous.y - current.y) + current.x)
            {
                inside = !inside;
            }

            previousIndex = vertexIndex;
        }

        return inside;
    }

    private static uint HashId(int first, int second, uint kind)
    {
        unchecked
        {
            uint hash = 2166136261u;
            hash = (hash ^ (uint)first) * 16777619u;
            hash = (hash ^ (uint)second) * 16777619u;
            hash = (hash ^ kind) * 16777619u;
            hash ^= hash >> 15;
            hash *= 2246822519u;
            hash ^= hash >> 13;
            return hash;
        }
    }
}
