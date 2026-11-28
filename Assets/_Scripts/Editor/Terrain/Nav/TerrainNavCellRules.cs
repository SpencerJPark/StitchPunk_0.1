#if UNITY_EDITOR
using System;

public struct TerrainNavCellInput
{
    public float slopeDegrees;
    public bool deepWater;
    public bool shallowWater;
    public bool crossesTerraceWall;
    public bool crossesLowLedge;
    public bool onRoad;
    public byte groundCost;
    public byte roadCost;
}

public struct TerrainNavCell
{
    public byte cost;
    public TerrainSurface surface;
}

// Pure per-cell nav classifier for the terrain nav bake; no Unity API so it is testable without a scene.
public static class TerrainNavCellRules
{
    public const float WallSlopeDegrees = 45f;
    public const float HeavySlopeDegrees = 25f;
    public const byte WallCost = 255;
    public const byte HeavyCost = 50;
    public const byte LeaveGroundCost = 255;

    public static TerrainNavCell Classify(in TerrainNavCellInput input)
    {
        TerrainNavCell cell = new TerrainNavCell();
        bool isHeavySlope = input.slopeDegrees >= HeavySlopeDegrees;

        if (input.deepWater)
        {
            cell.cost = WallCost;
            cell.surface = TerrainSurface.Deep;
            return cell;
        }
        // A terrace wall is a vertical cliff: it beats water, road and slope.
        if (input.crossesTerraceWall)
        {
            cell.cost = WallCost;
            cell.surface = TerrainSurface.Normal;
            return cell;
        }
        // NC-D6: a 0.7 m ledge is walkable but heavy; it must not hit the 45-degree slope wall test below.
        if (input.crossesLowLedge)
        {
            cell.cost = Math.Max(input.groundCost, HeavyCost);
            cell.surface = TerrainSurface.Normal;
            return cell;
        }
        if (input.slopeDegrees >= WallSlopeDegrees)
        {
            cell.cost = WallCost;
            cell.surface = TerrainSurface.Normal;
            return cell;
        }
        // Water beats road: bridges and fords repair these cells later via ApplyCrossing.
        if (input.shallowWater)
        {
            cell.cost = Math.Max(input.groundCost, HeavyCost);
            cell.surface = TerrainSurface.Shallow;
            return cell;
        }
        if (input.onRoad)
        {
            byte roadCellCost = input.roadCost == LeaveGroundCost ? input.groundCost : input.roadCost;
            if (isHeavySlope)
            {
                roadCellCost = Math.Max(roadCellCost, HeavyCost);
            }
            cell.cost = roadCellCost;
            cell.surface = TerrainSurface.Road;
            return cell;
        }
        cell.cost = isHeavySlope ? Math.Max(input.groundCost, HeavyCost) : input.groundCost;
        cell.surface = TerrainSurface.Normal;
        return cell;
    }

    public static TerrainNavCell ApplyCrossing(TerrainNavCell cell, bool isBridge, byte groundCost)
    {
        if (isBridge)
        {
            cell.surface = TerrainSurface.Road;
            cell.cost = groundCost;
            return cell;
        }
        if (cell.surface == TerrainSurface.Deep)
        {
            cell.surface = TerrainSurface.Shallow;
            cell.cost = HeavyCost;
        }
        return cell;
    }
}
#endif
