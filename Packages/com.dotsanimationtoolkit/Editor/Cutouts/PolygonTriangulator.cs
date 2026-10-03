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
