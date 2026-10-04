// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Ear-clipping triangulation for simple 2D polygons, concave allowed.</summary>
    public static class PolygonTriangulator
    {
        const float MinimumAbsoluteArea = 1e-6f;
        const float CollinearEpsilon = 1e-6f;

        // Positive = counter-clockwise with +X right, +Y up.
        public static float SignedArea(IReadOnlyList<Vector2> polygon)
        {
            float doubledArea = 0f;
            for (int vertexIndex = 0; vertexIndex < polygon.Count; vertexIndex++)
            {
                Vector2 current = polygon[vertexIndex];
                Vector2 next = polygon[(vertexIndex + 1) % polygon.Count];
                doubledArea += current.x * next.y - next.x * current.y;
            }
            return doubledArea * 0.5f;
        }

        public static bool IsSelfIntersecting(IReadOnlyList<Vector2> polygon)
        {
            int vertexCount = polygon.Count;
            if (vertexCount < 3)
                return false;

            for (int vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
            {
                if (polygon[vertexIndex] == polygon[(vertexIndex + 1) % vertexCount])
                    return true;
            }

            for (int firstEdge = 0; firstEdge < vertexCount; firstEdge++)
            {
                for (int secondEdge = firstEdge + 2; secondEdge < vertexCount; secondEdge++)
                {
                    bool edgesShareLastAndFirstVertex = firstEdge == 0 && secondEdge == vertexCount - 1;
                    if (edgesShareLastAndFirstVertex)
                        continue;

                    if (SegmentsIntersectOrTouch(
                            polygon[firstEdge], polygon[(firstEdge + 1) % vertexCount],
                            polygon[secondEdge], polygon[(secondEdge + 1) % vertexCount]))
                        return true;
                }
            }
            return false;
        }

        // Clears `triangles`, then fills it with CCW index triples into `polygon`.
        public static bool TryEarClip(IReadOnlyList<Vector2> polygon, List<int> triangles)
        {
            triangles.Clear();
            int vertexCount = polygon.Count;
            if (vertexCount < 3)
                return false;

            float signedArea = SignedArea(polygon);
            if (Mathf.Abs(signedArea) < MinimumAbsoluteArea || IsSelfIntersecting(polygon))
                return false;

            List<int> remaining = new List<int>(vertexCount);
            for (int vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
                remaining.Add(signedArea > 0f ? vertexIndex : vertexCount - 1 - vertexIndex);

            while (remaining.Count > 3)
            {
                bool removedVertex = false;
                for (int remainingIndex = 0; remainingIndex < remaining.Count; remainingIndex++)
                {
                    int previous = remaining[(remainingIndex + remaining.Count - 1) % remaining.Count];
                    int current = remaining[remainingIndex];
                    int next = remaining[(remainingIndex + 1) % remaining.Count];
                    float turn = Cross(polygon[previous], polygon[current], polygon[next]);

                    if (Mathf.Abs(turn) <= CollinearEpsilon)
                    {
                        remaining.RemoveAt(remainingIndex);
                        removedVertex = true;
                        break;
                    }
                    if (turn < 0f || AnyOtherVertexInsideTriangle(polygon, remaining, previous, current, next))
                        continue;

                    triangles.Add(previous);
                    triangles.Add(current);
                    triangles.Add(next);
                    remaining.RemoveAt(remainingIndex);
                    removedVertex = true;
                    break;
                }

                if (!removedVertex)
                {
                    triangles.Clear();
                    return false;
                }
            }

            if (remaining.Count == 3 && Cross(polygon[remaining[0]], polygon[remaining[1]], polygon[remaining[2]]) > CollinearEpsilon)
            {
                triangles.Add(remaining[0]);
                triangles.Add(remaining[1]);
                triangles.Add(remaining[2]);
            }
            return true;
        }

        public static bool IsValidInnerEdge(IReadOnlyList<Vector2> polygon, IReadOnlyList<Vector2Int> keptEdges, int firstVertex, int secondVertex, out string reason)
        {
            reason = string.Empty;
            int vertexCount = polygon.Count;
            if (firstVertex < 0 || secondVertex < 0 || firstVertex >= vertexCount || secondVertex >= vertexCount || firstVertex == secondVertex)
            {
                reason = "That edge leaves the shape.";
                return false;
            }

            int lowVertex = Mathf.Min(firstVertex, secondVertex);
            int highVertex = Mathf.Max(firstVertex, secondVertex);
            bool adjacentOnOutline = highVertex - lowVertex == 1 || (lowVertex == 0 && highVertex == vertexCount - 1);
            if (adjacentOnOutline)
            {
                reason = "Those vertices are already joined by the outline.";
                return false;
            }

            if (keptEdges != null)
            {
                for (int keptIndex = 0; keptIndex < keptEdges.Count; keptIndex++)
                {
                    Vector2Int kept = keptEdges[keptIndex];
                    bool sameEdge = (kept.x == lowVertex && kept.y == highVertex) || (kept.x == highVertex && kept.y == lowVertex);
                    if (sameEdge)
                    {
                        reason = "That edge is already drawn.";
                        return false;
                    }
                }
            }

            Vector2 start = polygon[lowVertex];
            Vector2 end = polygon[highVertex];
            if (!IsPointStrictlyInsidePolygon(polygon, (start + end) * 0.5f))
            {
                reason = "That edge leaves the shape.";
                return false;
            }

            for (int outlineStart = 0; outlineStart < vertexCount; outlineStart++)
            {
                int outlineEnd = (outlineStart + 1) % vertexCount;
                bool touchesFirst = outlineStart == lowVertex || outlineEnd == lowVertex;
                bool touchesSecond = outlineStart == highVertex || outlineEnd == highVertex;
                if (!touchesFirst && !touchesSecond)
                {
                    if (SegmentsIntersectOrTouch(start, end, polygon[outlineStart], polygon[outlineEnd]))
                    {
                        reason = "That edge leaves the shape.";
                        return false;
                    }
                    continue;
                }

                // Shares one endpoint with the outline edge: it may only overlap it by lying along it.
                int outlineOther = touchesFirst ? (outlineStart == lowVertex ? outlineEnd : outlineStart) : (outlineStart == highVertex ? outlineEnd : outlineStart);
                Vector2 sharedPoint = touchesFirst ? start : end;
                Vector2 farPoint = touchesFirst ? end : start;
                bool outlineEndLiesOnEdge = Mathf.Abs(Cross(sharedPoint, farPoint, polygon[outlineOther])) <= CollinearEpsilon
                    && IsWithinBoundingBox(start, end, polygon[outlineOther]);
                bool edgeEndLiesOnOutline = Mathf.Abs(Cross(sharedPoint, polygon[outlineOther], farPoint)) <= CollinearEpsilon
                    && IsWithinBoundingBox(sharedPoint, polygon[outlineOther], farPoint);
                if (outlineEndLiesOnEdge || edgeEndLiesOnOutline)
                {
                    reason = "That edge leaves the shape.";
                    return false;
                }
            }

            if (keptEdges != null)
            {
                for (int keptIndex = 0; keptIndex < keptEdges.Count; keptIndex++)
                {
                    Vector2Int kept = keptEdges[keptIndex];
                    bool sharesEndpoint = kept.x == lowVertex || kept.x == highVertex || kept.y == lowVertex || kept.y == highVertex;
                    if (sharesEndpoint)
                        continue;
                    if (SegmentsIntersectOrTouch(start, end, polygon[kept.x], polygon[kept.y]))
                    {
                        reason = "That edge crosses another edge.";
                        return false;
                    }
                }
            }
            return true;
        }

        public static bool TryTriangulateWithEdges(IReadOnlyList<Vector2> polygon, IReadOnlyList<Vector2Int> innerEdges, List<int> triangles, List<int> skippedEdgeIndices)
        {
            skippedEdgeIndices.Clear();
            if (innerEdges == null || innerEdges.Count == 0)
                return TryEarClip(polygon, triangles);

            triangles.Clear();
            int vertexCount = polygon.Count;
            float signedArea = SignedArea(polygon);
            if (vertexCount < 3 || Mathf.Abs(signedArea) < MinimumAbsoluteArea || IsSelfIntersecting(polygon))
                return false;

            List<int> wholeOutline = new List<int>(vertexCount);
            for (int vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
                wholeOutline.Add(signedArea > 0f ? vertexIndex : vertexCount - 1 - vertexIndex);
            List<List<int>> pieces = new List<List<int>> { wholeOutline };

            List<Vector2Int> keptEdges = new List<Vector2Int>();
            for (int edgeIndex = 0; edgeIndex < innerEdges.Count; edgeIndex++)
            {
                int lowVertex = Mathf.Min(innerEdges[edgeIndex].x, innerEdges[edgeIndex].y);
                int highVertex = Mathf.Max(innerEdges[edgeIndex].x, innerEdges[edgeIndex].y);
                if (!IsValidInnerEdge(polygon, keptEdges, lowVertex, highVertex, out string reason))
                {
                    skippedEdgeIndices.Add(edgeIndex);
                    continue;
                }

                bool split = false;
                for (int pieceIndex = 0; pieceIndex < pieces.Count && !split; pieceIndex++)
                {
                    List<int> piece = pieces[pieceIndex];
                    int lowPosition = piece.IndexOf(lowVertex);
                    int highPosition = piece.IndexOf(highVertex);
                    if (lowPosition < 0 || highPosition < 0)
                        continue;

                    pieces[pieceIndex] = CopyRunInclusive(piece, lowPosition, highPosition);
                    pieces.Add(CopyRunInclusive(piece, highPosition, lowPosition));
                    split = true;
                }

                if (split)
                    keptEdges.Add(new Vector2Int(lowVertex, highVertex));
                else
                    skippedEdgeIndices.Add(edgeIndex);
            }

            List<Vector2> pieceCorners = new List<Vector2>();
            List<int> pieceTriangles = new List<int>();
            for (int pieceIndex = 0; pieceIndex < pieces.Count; pieceIndex++)
            {
                List<int> piece = pieces[pieceIndex];
                pieceCorners.Clear();
                for (int position = 0; position < piece.Count; position++)
                    pieceCorners.Add(polygon[piece[position]]);

                if (!TryEarClip(pieceCorners, pieceTriangles))
                {
                    triangles.Clear();
                    return false;
                }
                for (int triangleIndex = 0; triangleIndex < pieceTriangles.Count; triangleIndex++)
                    triangles.Add(piece[pieceTriangles[triangleIndex]]);
            }
            return true;
        }

        public static void ShiftEdgesForInsertedVertex(List<Vector2Int> innerEdges, int insertedIndex)
        {
            for (int edgeIndex = 0; edgeIndex < innerEdges.Count; edgeIndex++)
            {
                Vector2Int edge = innerEdges[edgeIndex];
                if (edge.x >= insertedIndex)
                    edge.x++;
                if (edge.y >= insertedIndex)
                    edge.y++;
                innerEdges[edgeIndex] = NormalizeEdge(edge);
            }
        }

        public static void RemoveEdgesForDeletedVertex(List<Vector2Int> innerEdges, int deletedIndex)
        {
            for (int edgeIndex = innerEdges.Count - 1; edgeIndex >= 0; edgeIndex--)
            {
                Vector2Int edge = innerEdges[edgeIndex];
                if (edge.x == deletedIndex || edge.y == deletedIndex)
                {
                    innerEdges.RemoveAt(edgeIndex);
                    continue;
                }
                if (edge.x > deletedIndex)
                    edge.x--;
                if (edge.y > deletedIndex)
                    edge.y--;
                innerEdges[edgeIndex] = NormalizeEdge(edge);
            }
        }

        static Vector2Int NormalizeEdge(Vector2Int edge)
        {
            return edge.x <= edge.y ? edge : new Vector2Int(edge.y, edge.x);
        }

        // Cyclic run of `piece` from fromPosition forward to toPosition, both inclusive.
        static List<int> CopyRunInclusive(List<int> piece, int fromPosition, int toPosition)
        {
            List<int> run = new List<int>();
            int position = fromPosition;
            while (true)
            {
                run.Add(piece[position]);
                if (position == toPosition)
                    break;
                position = (position + 1) % piece.Count;
            }
            return run;
        }

        static bool IsPointStrictlyInsidePolygon(IReadOnlyList<Vector2> polygon, Vector2 point)
        {
            bool inside = false;
            int vertexCount = polygon.Count;
            for (int vertexIndex = 0, previous = vertexCount - 1; vertexIndex < vertexCount; previous = vertexIndex++)
            {
                Vector2 current = polygon[vertexIndex];
                Vector2 before = polygon[previous];
                if (Mathf.Abs(Cross(before, current, point)) <= CollinearEpsilon && IsWithinBoundingBox(before, current, point))
                    return false;
                bool straddles = (current.y > point.y) != (before.y > point.y);
                if (straddles && point.x < (before.x - current.x) * (point.y - current.y) / (before.y - current.y) + current.x)
                    inside = !inside;
            }
            return inside;
        }

        static float Cross(Vector2 origin, Vector2 first, Vector2 second)
        {
            return (first.x - origin.x) * (second.y - origin.y) - (first.y - origin.y) * (second.x - origin.x);
        }

        static bool AnyOtherVertexInsideTriangle(IReadOnlyList<Vector2> polygon, List<int> remaining, int cornerA, int cornerB, int cornerC)
        {
            for (int remainingIndex = 0; remainingIndex < remaining.Count; remainingIndex++)
            {
                int candidate = remaining[remainingIndex];
                if (candidate == cornerA || candidate == cornerB || candidate == cornerC)
                    continue;

                Vector2 point = polygon[candidate];
                bool insideOrOnTriangle = Cross(polygon[cornerA], polygon[cornerB], point) >= 0f
                    && Cross(polygon[cornerB], polygon[cornerC], point) >= 0f
                    && Cross(polygon[cornerC], polygon[cornerA], point) >= 0f;
                if (insideOrOnTriangle)
                    return true;
            }
            return false;
        }

        static bool SegmentsIntersectOrTouch(Vector2 firstStart, Vector2 firstEnd, Vector2 secondStart, Vector2 secondEnd)
        {
            float startSide = Cross(firstStart, firstEnd, secondStart);
            float endSide = Cross(firstStart, firstEnd, secondEnd);
            float otherStartSide = Cross(secondStart, secondEnd, firstStart);
            float otherEndSide = Cross(secondStart, secondEnd, firstEnd);

            if (((startSide > 0f && endSide < 0f) || (startSide < 0f && endSide > 0f))
                && ((otherStartSide > 0f && otherEndSide < 0f) || (otherStartSide < 0f && otherEndSide > 0f)))
                return true;

            return (startSide == 0f && IsWithinBoundingBox(firstStart, firstEnd, secondStart))
                || (endSide == 0f && IsWithinBoundingBox(firstStart, firstEnd, secondEnd))
                || (otherStartSide == 0f && IsWithinBoundingBox(secondStart, secondEnd, firstStart))
                || (otherEndSide == 0f && IsWithinBoundingBox(secondStart, secondEnd, firstEnd));
        }

        static bool IsWithinBoundingBox(Vector2 segmentStart, Vector2 segmentEnd, Vector2 point)
        {
            return point.x >= Mathf.Min(segmentStart.x, segmentEnd.x) && point.x <= Mathf.Max(segmentStart.x, segmentEnd.x)
                && point.y >= Mathf.Min(segmentStart.y, segmentEnd.y) && point.y <= Mathf.Max(segmentStart.y, segmentEnd.y);
        }
    }
}
