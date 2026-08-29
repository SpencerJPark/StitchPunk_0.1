using DotsAnimationToolkit;

// Baked mirror of a DirectionSetAsset: five east-side clip ids + the mirror-closed
// AnimationDirections derived from which slots were filled at bake (DirectionSetBakeUtil). West-side
// facings are never stored here — FacingResolver.ToAuthoredSide always resolves to one of these five
// east-side keys plus a runtime mirror flag.
public struct DirectionSetBlob
{
    public ClipId southEast;
    public ClipId northEast;
    public ClipId south;
    public ClipId north;
    public ClipId east;
    public AnimationDirections effectiveDirections;

    // The per-set fold (DirectionFacing_System.md §5): a set with fewer authored directions than the
    // actor turns through folds onto whatever it actually has, so a Two-coverage walk on a
    // Six-turning actor just mirrors left/right. Every clip pick goes through here rather than
    // GetSlot, because the raw slot for a facing the set never authored is an empty ClipId — which
    // reads on screen as the unit freezing whenever it faces that way.
    public ClipId ResolveSlot(Direction eastSideFacing)
    {
        FacingResolver.ResolveClipFacing(
            eastSideFacing, effectiveDirections, out Direction foldedFacing, out bool _);
        return GetSlot(foldedFacing);
    }

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
