using DotsAnimationToolkit;
using DotsAnimationToolkit.Authoring;
using UnityEngine;

// A "logical animation" that turns: five east-side clip slots, mirrors always free. Its effective
// AnimationDirections is DERIVED from which slots are filled (see TryGetEffectiveDirections), never
// declared — the resolver snaps into whatever the fill pattern actually supports. Shareable across
// units on the same rig (it's an asset, not an inline struct), and what the Direction Set Editor
// window opens. See DirectionFacing_System.md §4.
[CreateAssetMenu(fileName = "DirectionSet", menuName = "Units/Direction Set")]
public class DirectionSetSO : ScriptableObject
{
    [Tooltip("Front three-quarter, facing the camera toward the right. The only slot a Two/One-" +
             "coverage set needs.")]
    public ClipAsset southEast;

    [Tooltip("Back three-quarter, facing away toward the right. Adding this promotes the set to Four.")]
    public ClipAsset northEast;

    [Tooltip("Head-on, facing the camera. Filled alone (no other slot) this is a One-coverage set " +
             "that never turns and never mirrors. Filled alongside north it promotes a Four set to Six.")]
    public ClipAsset south;

    [Tooltip("Head-away, facing away from the camera. Filled alongside south it promotes a Four set to Six.")]
    public ClipAsset north;

    [Tooltip("True profile, facing right. Filling all five slots promotes the set to Eight.")]
    public ClipAsset east;

    public ClipAsset GetSlot(Direction eastSideFacing)
    {
        switch (eastSideFacing)
        {
            case Direction.SouthEast: return southEast;
            case Direction.NorthEast: return northEast;
            case Direction.South: return south;
            case Direction.North: return north;
            case Direction.East: return east;
            default: return null;
        }
    }

    // Derives the mirror-closed AnimationDirections this fill pattern actually covers. Returns false
    // for anything other than the five valid patterns (SouthEast only / +NorthEast / +South+North /
    // all five / South only) and rounds the out value down to the largest set whose required slots
    // are all present — shared by DirectionSetBakeUtil (the bake-time warning) and the Direction Set
    // Editor window (the live coverage readout), so the two can never disagree.
    public bool TryGetEffectiveDirections(out AnimationDirections effectiveDirections)
    {
        bool hasSouthEast = southEast != null;
        bool hasNorthEast = northEast != null;
        bool hasSouth = south != null;
        bool hasNorth = north != null;
        bool hasEast = east != null;

        if (hasSouthEast && hasNorthEast && hasSouth && hasNorth && hasEast)
        {
            effectiveDirections = AnimationDirections.Eight;
            return true;
        }
        if (hasSouthEast && hasNorthEast && hasSouth && hasNorth && !hasEast)
        {
            effectiveDirections = AnimationDirections.Six;
            return true;
        }
        if (hasSouthEast && hasNorthEast && !hasSouth && !hasNorth && !hasEast)
        {
            effectiveDirections = AnimationDirections.Four;
            return true;
        }
        if (hasSouthEast && !hasNorthEast && !hasSouth && !hasNorth && !hasEast)
        {
            effectiveDirections = AnimationDirections.Two;
            return true;
        }
        if (!hasSouthEast && !hasNorthEast && hasSouth && !hasNorth && !hasEast)
        {
            effectiveDirections = AnimationDirections.One;
            return true;
        }

        if (hasSouthEast && hasNorthEast && hasSouth && hasNorth)
        {
            effectiveDirections = AnimationDirections.Six;
        }
        else if (hasSouthEast && hasNorthEast)
        {
            effectiveDirections = AnimationDirections.Four;
        }
        else if (hasSouthEast)
        {
            effectiveDirections = AnimationDirections.Two;
        }
        else if (hasSouth)
        {
            effectiveDirections = AnimationDirections.One;
        }
        else
        {
            // No usable slots at all (e.g. only North or only East filled) — nothing plays.
            effectiveDirections = AnimationDirections.One;
        }
        return false;
    }
}
