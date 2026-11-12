#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

public static class TerrainFeatureEditing
{
    public static List<Vector2> BoxOutline(Vector2 cornerA, Vector2 cornerB)
    {
        float minimumX = Mathf.Min(cornerA.x, cornerB.x);
        float maximumX = Mathf.Max(cornerA.x, cornerB.x);
        float minimumY = Mathf.Min(cornerA.y, cornerB.y);
        float maximumY = Mathf.Max(cornerA.y, cornerB.y);
        return new List<Vector2>
        {
            new Vector2(minimumX, minimumY),
            new Vector2(maximumX, minimumY),
            new Vector2(maximumX, maximumY),
            new Vector2(minimumX, maximumY),
        };
    }

    public static bool IsSelfIntersecting(IReadOnlyList<Vector2> closedOutline)
    {
        int pointCount = closedOutline.Count;
        if (pointCount < 4)
        {
            return false;
        }

        for (int firstEdge = 0; firstEdge < pointCount; firstEdge++)
        {
            for (int secondEdge = firstEdge + 2; secondEdge < pointCount; secondEdge++)
            {
                bool closingEdgeNeighboursFirstEdge = firstEdge == 0 && secondEdge == pointCount - 1;
                if (closingEdgeNeighboursFirstEdge)
                {
                    continue;
                }

                if (SegmentsIntersect(
                        closedOutline[firstEdge], closedOutline[(firstEdge + 1) % pointCount],
                        closedOutline[secondEdge], closedOutline[(secondEdge + 1) % pointCount]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static int NearestSegmentIndex(IReadOnlyList<Vector2> closedOutline, Vector2 point, out Vector2 closestPoint)
    {
        closestPoint = Vector2.zero;
        int pointCount = closedOutline.Count;
        if (pointCount == 0)
        {
            return -1;
        }

        int bestSegmentIndex = -1;
        float bestSquaredDistance = float.MaxValue;
        for (int segmentIndex = 0; segmentIndex < pointCount; segmentIndex++)
        {
            Vector2 segmentStart = closedOutline[segmentIndex];
            Vector2 segmentEnd = closedOutline[(segmentIndex + 1) % pointCount];
            Vector2 candidate = ClosestPointOnSegment(segmentStart, segmentEnd, point);
            float squaredDistance = (candidate - point).sqrMagnitude;
            if (squaredDistance < bestSquaredDistance)
            {
                bestSquaredDistance = squaredDistance;
                bestSegmentIndex = segmentIndex;
                closestPoint = candidate;
            }
        }

        return bestSegmentIndex;
    }

    public static float AreaSquareMetres(IReadOnlyList<Vector2> closedOutline)
    {
        int pointCount = closedOutline.Count;
        float doubledArea = 0f;
        for (int pointIndex = 0; pointIndex < pointCount; pointIndex++)
        {
            Vector2 current = closedOutline[pointIndex];
            Vector2 next = closedOutline[(pointIndex + 1) % pointCount];
            doubledArea += current.x * next.y - next.x * current.y;
        }

        return Mathf.Abs(doubledArea) * 0.5f;
    }

    public static bool ContainsPoint(IReadOnlyList<Vector2> closedOutline, Vector2 point)
    {
        int pointCount = closedOutline.Count;
        bool inside = false;
        for (int pointIndex = 0, previousIndex = pointCount - 1; pointIndex < pointCount; previousIndex = pointIndex++)
        {
            Vector2 current = closedOutline[pointIndex];
            Vector2 previous = closedOutline[previousIndex];
            bool straddlesRay = (current.y > point.y) != (previous.y > point.y);
            if (!straddlesRay)
            {
                continue;
            }

            float crossingX = (previous.x - current.x) * (point.y - current.y) / (previous.y - current.y) + current.x;
            if (point.x < crossingX)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private static Vector2 ClosestPointOnSegment(Vector2 segmentStart, Vector2 segmentEnd, Vector2 point)
    {
        Vector2 direction = segmentEnd - segmentStart;
        float squaredLength = direction.sqrMagnitude;
        if (squaredLength <= 0f)
        {
            return segmentStart;
        }

        float fraction = Mathf.Clamp01(Vector2.Dot(point - segmentStart, direction) / squaredLength);
        return segmentStart + direction * fraction;
    }

    private static float Orientation(Vector2 origin, Vector2 first, Vector2 second)
    {
        return (first.x - origin.x) * (second.y - origin.y) - (first.y - origin.y) * (second.x - origin.x);
    }

    private static bool IsWithinSegmentBounds(Vector2 segmentStart, Vector2 segmentEnd, Vector2 point)
    {
        return point.x >= Mathf.Min(segmentStart.x, segmentEnd.x) && point.x <= Mathf.Max(segmentStart.x, segmentEnd.x)
            && point.y >= Mathf.Min(segmentStart.y, segmentEnd.y) && point.y <= Mathf.Max(segmentStart.y, segmentEnd.y);
    }

    // Touching and collinear overlap count as intersecting: a vertex resting on another edge is still an invalid outline.
    private static bool SegmentsIntersect(Vector2 startA, Vector2 endA, Vector2 startB, Vector2 endB)
    {
        float orientationB1 = Orientation(startA, endA, startB);
        float orientationB2 = Orientation(startA, endA, endB);
        float orientationA1 = Orientation(startB, endB, startA);
        float orientationA2 = Orientation(startB, endB, endA);

        bool crossesProperly = ((orientationB1 > 0f && orientationB2 < 0f) || (orientationB1 < 0f && orientationB2 > 0f))
            && ((orientationA1 > 0f && orientationA2 < 0f) || (orientationA1 < 0f && orientationA2 > 0f));
        if (crossesProperly)
        {
            return true;
        }

        if (orientationB1 == 0f && IsWithinSegmentBounds(startA, endA, startB)) { return true; }
        if (orientationB2 == 0f && IsWithinSegmentBounds(startA, endA, endB)) { return true; }
        if (orientationA1 == 0f && IsWithinSegmentBounds(startB, endB, startA)) { return true; }
        if (orientationA2 == 0f && IsWithinSegmentBounds(startB, endB, endA)) { return true; }
        return false;
    }
}
#endif
