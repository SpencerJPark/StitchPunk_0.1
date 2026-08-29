using DotsAnimationToolkit;

// Baked mirror of a DirectionSetSO: five east-side clip ids + the mirror-closed AnimationDirections
// derived from which slots were filled at bake (DirectionSetBakeUtil). West-side facings are never
// stored here — FacingResolver.ToAuthoredSide always resolves to one of these five east-side keys
// plus a runtime mirror flag.
public struct DirectionSetBlob
{
    public ClipId southEast;
    public ClipId northEast;
    public ClipId south;
    public ClipId north;
    public ClipId east;
    public AnimationDirections effectiveDirections;

    public ClipId GetSlot(Direction eastSideFacing)
    {
        switch (eastSideFacing)
        {
            case Direction.SouthEast: return southEast;
            case Direction.NorthEast: return northEast;
            case Direction.South: return south;
            case Direction.North: return north;
            case Direction.East: return east;
            default: return default;
        }
    }
}
