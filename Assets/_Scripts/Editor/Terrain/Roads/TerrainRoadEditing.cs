#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

public static class TerrainRoadEditing
{
    private const float WidthSnapStepMetres = 0.1f;
    private const float DegenerateLengthSquared = 1e-10f;

    public struct PolylineHit
    {
        public int segmentIndex;
        public float segmentFraction;
        public float distancePixels;
        public Vector2 pointOnLine;
    }

    public static bool TryNearestOnPolyline(IReadOnlyList<Vector2> screenPoints, Vector2 mouse, float maxDistancePixels, out PolylineHit hit)
    {
        hit = default;
        if (screenPoints == null || screenPoints.Count < 2)
        {
            return false;
        }

        float bestDistance = float.MaxValue;
        for (int segmentIndex = 0; segmentIndex < screenPoints.Count - 1; segmentIndex++)
        {
            Vector2 start = screenPoints[segmentIndex];
            Vector2 segment = screenPoints[segmentIndex + 1] - start;
            float lengthSquared = segment.sqrMagnitude;
            float fraction = lengthSquared < DegenerateLengthSquared
                ? 0f
                : Mathf.Clamp01(Vector2.Dot(mouse - start, segment) / lengthSquared);
            Vector2 closest = start + segment * fraction;
            float distance = Vector2.Distance(mouse, closest);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                hit = new PolylineHit
                {
                    segmentIndex = segmentIndex,
                    segmentFraction = fraction,
                    distancePixels = distance,
                    pointOnLine = closest
                };
            }
        }

        return bestDistance <= maxDistancePixels;
    }

    public static int NearestPointIndex(IReadOnlyList<Vector2> screenPoints, Vector2 mouse, float maxDistancePixels)
    {
        int bestIndex = -1;
        float bestDistance = maxDistancePixels;
        for (int pointIndex = 0; pointIndex < screenPoints.Count; pointIndex++)
        {
            float distance = Vector2.Distance(mouse, screenPoints[pointIndex]);
            if (distance <= bestDistance)
            {
                bestDistance = distance;
                bestIndex = pointIndex;
            }
        }
        return bestIndex;
    }

    public static float PolylineFraction(int pointCount, int segmentIndex, float segmentFraction)
    {
        if (pointCount < 2)
        {
            return 0f;
        }
        return Mathf.Clamp01((segmentIndex + segmentFraction) / (pointCount - 1));
    }

    // Left is travel direction rotated 90 degrees counter-clockwise seen from above: +x travel gives +z.
    // Corners use the unscaled averaged normal, so the offset length stays halfWidthMetres.
    public static void BuildEdgeLines(IReadOnlyList<Vector3> centre, float halfWidthMetres, List<Vector3> left, List<Vector3> right)
    {
        left.Clear();
        right.Clear();
        int pointCount = centre.Count;
        if (pointCount < 2)
        {
            return;
        }

        Vector2 previousNormal = Vector2.zero;
        for (int pointIndex = 0; pointIndex < pointCount; pointIndex++)
        {
            Vector2 incomingNormal = pointIndex > 0
                ? SegmentLeftNormal(centre[pointIndex - 1], centre[pointIndex], previousNormal)
                : Vector2.zero;
            Vector2 outgoingNormal = pointIndex < pointCount - 1
                ? SegmentLeftNormal(centre[pointIndex], centre[pointIndex + 1], previousNormal)
                : Vector2.zero;

            Vector2 normal = incomingNormal + outgoingNormal;
            normal = normal.sqrMagnitude < DegenerateLengthSquared ? (pointIndex == 0 ? outgoingNormal : incomingNormal) : normal.normalized;
            previousNormal = normal;

            Vector3 point = centre[pointIndex];
            Vector3 offset = new Vector3(normal.x, 0f, normal.y) * halfWidthMetres;
            left.Add(point + offset);
            right.Add(point - offset);
        }
    }

    public static float WidthFromHandleDrag(Vector3 centrePoint, Vector3 draggedPoint, float minimumWidthMetres, float maximumWidthMetres)
    {
        float deltaX = draggedPoint.x - centrePoint.x;
        float deltaZ = draggedPoint.z - centrePoint.z;
        float width = 2f * Mathf.Sqrt(deltaX * deltaX + deltaZ * deltaZ);
        width = Mathf.Clamp(width, minimumWidthMetres, maximumWidthMetres);
        return Mathf.Round(width / WidthSnapStepMetres) * WidthSnapStepMetres;
    }

    public static bool CanDeleteKnot(int knotCount, bool isClosed)
    {
        return knotCount > (isClosed ? 3 : 2);
    }

    public static int NearestEndKnotIndex(IReadOnlyList<Vector3> knotPositions, Vector3 point)
    {
        int lastIndex = knotPositions.Count - 1;
        float distanceToFirst = HorizontalDistanceSquared(knotPositions[0], point);
        float distanceToLast = HorizontalDistanceSquared(knotPositions[lastIndex], point);
        return distanceToFirst <= distanceToLast ? 0 : lastIndex;
    }

    private static Vector2 SegmentLeftNormal(Vector3 from, Vector3 to, Vector2 fallback)
    {
        Vector2 direction = new Vector2(to.x - from.x, to.z - from.z);
        if (direction.sqrMagnitude < DegenerateLengthSquared)
        {
            return fallback;
        }
        direction.Normalize();
        return new Vector2(-direction.y, direction.x);
    }

    private static float HorizontalDistanceSquared(Vector3 first, Vector3 second)
    {
        float deltaX = first.x - second.x;
        float deltaZ = first.z - second.z;
        return deltaX * deltaX + deltaZ * deltaZ;
    }
}
#endif
