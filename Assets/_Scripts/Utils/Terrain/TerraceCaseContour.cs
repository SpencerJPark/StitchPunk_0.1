using UnityEngine;

// Smooth marching-squares contour per 2 m cell: straight lines for edge cases, quarter circles around the odd corner
// for one- and three-corner cases, two arcs for saddles (inside connected). Positive = upper level.
public static class TerraceCaseContour
{
    public const float CornerArcRadiusMetres = 1f;      // half a 2 m cell: the only radius that meets both side midpoints at right angles

    private const float CellSideMetres = 2f;

    private static readonly Vector2 TopLeftCorner = new Vector2(0f, CellSideMetres);
    private static readonly Vector2 TopRightCorner = new Vector2(CellSideMetres, CellSideMetres);
    private static readonly Vector2 BottomRightCorner = new Vector2(CellSideMetres, 0f);
    private static readonly Vector2 BottomLeftCorner = new Vector2(0f, 0f);

    public static int CaseFor(int topLeftLevel, int topRightLevel, int bottomRightLevel, int bottomLeftLevel, int levelThreshold)
    {
        int caseIndex = 0;
        if (topLeftLevel >= levelThreshold) caseIndex |= 1;
        if (topRightLevel >= levelThreshold) caseIndex |= 2;
        if (bottomRightLevel >= levelThreshold) caseIndex |= 4;
        if (bottomLeftLevel >= levelThreshold) caseIndex |= 8;
        return caseIndex;
    }

    public static float SignedDistanceInCell(int caseIndex, Vector2 cellLocalMetres)
    {
        float radius = CornerArcRadiusMetres;
        float distance;
        switch (caseIndex)
        {
            case 0: distance = -radius; break;
            case 15: distance = radius; break;
            case 1: distance = radius - Vector2.Distance(cellLocalMetres, TopLeftCorner); break;
            case 2: distance = radius - Vector2.Distance(cellLocalMetres, TopRightCorner); break;
            case 4: distance = radius - Vector2.Distance(cellLocalMetres, BottomRightCorner); break;
            case 8: distance = radius - Vector2.Distance(cellLocalMetres, BottomLeftCorner); break;
            case 14: distance = Vector2.Distance(cellLocalMetres, TopLeftCorner) - radius; break;
            case 13: distance = Vector2.Distance(cellLocalMetres, TopRightCorner) - radius; break;
            case 11: distance = Vector2.Distance(cellLocalMetres, BottomRightCorner) - radius; break;
            case 7: distance = Vector2.Distance(cellLocalMetres, BottomLeftCorner) - radius; break;
            case 3: distance = cellLocalMetres.y - 1f; break;
            case 12: distance = 1f - cellLocalMetres.y; break;
            case 6: distance = cellLocalMetres.x - 1f; break;
            case 9: distance = 1f - cellLocalMetres.x; break;
            case 5:
                distance = Mathf.Min(
                    Vector2.Distance(cellLocalMetres, TopRightCorner),
                    Vector2.Distance(cellLocalMetres, BottomLeftCorner)) - radius;
                break;
            case 10:
                distance = Mathf.Min(
                    Vector2.Distance(cellLocalMetres, TopLeftCorner),
                    Vector2.Distance(cellLocalMetres, BottomRightCorner)) - radius;
                break;
            default: distance = -radius; break;
        }

        // With the clamp, on any cell side whose two corners agree the value is exactly +-R from both cells, and on a crossing side both cells give the same linear value, so the field is continuous across cell and tile seams.
        return Mathf.Clamp(distance, -radius, radius);
    }
}
