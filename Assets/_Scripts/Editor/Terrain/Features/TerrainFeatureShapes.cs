#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

public static class TerrainFeatureShapes
{
    private const int CircleKnotCount = 8;

    public static List<Vector2> CircleKnots(Vector2 centreXZ, float radiusMetres)
    {
        List<Vector2> knots = new List<Vector2>(CircleKnotCount);
        for (int knotIndex = 0; knotIndex < CircleKnotCount; knotIndex++)
        {
            float angleRadians = knotIndex * Mathf.PI * 2f / CircleKnotCount;
            knots.Add(centreXZ + new Vector2(Mathf.Cos(angleRadians), Mathf.Sin(angleRadians)) * radiusMetres);
        }
        return knots;
    }

    public static List<Vector2> BoxKnots(Vector2 cornerA, Vector2 cornerB)
    {
        return TerrainFeatureEditing.BoxOutline(cornerA, cornerB);
    }

    public static bool KnotsAreSharp(TerrainFeatureShapeKind kind)
    {
        return kind == TerrainFeatureShapeKind.Straight
            || kind == TerrainFeatureShapeKind.Polygon
            || kind == TerrainFeatureShapeKind.Box;
    }

    public static bool IsAllowed(TerrainFeatureShapeKind kind, TerrainFeatureShapeSupport support, out bool closed)
    {
        bool supportsOpen = (support & TerrainFeatureShapeSupport.OpenPath) != 0;
        bool supportsClosed = (support & (TerrainFeatureShapeSupport.ClosedArea | TerrainFeatureShapeSupport.ClosedLoop)) != 0;

        bool canBeOpen = kind == TerrainFeatureShapeKind.Curve || kind == TerrainFeatureShapeKind.Straight;
        if (canBeOpen && supportsOpen)
        {
            closed = false;
            return true;
        }

        closed = supportsClosed;
        return supportsClosed;
    }
}
#endif
