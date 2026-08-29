using DotsAnimationToolkit;
using UnityEngine;

// Shared by every DirectionSetSO baking site (UnitLibraryBakingSystem's idle/moving/stance/action
// mappings) so the fill-pattern warning and effective-count derivation live in exactly one place and
// can never disagree with what the Direction Set Editor window shows.
public static class DirectionSetBakeUtil
{
    public static DirectionSetBlob Bake(DirectionSetSO directionSet, string context)
    {
        if (directionSet == null) return default;

        bool isValidFill = directionSet.TryGetEffectiveDirections(out AnimationDirections effectiveDirections);
        if (!isValidFill)
        {
            Debug.LogWarning(
                $"[DirectionSetBaking] '{directionSet.name}' ({context}) has an invalid direction-slot " +
                $"fill pattern — rounding down to {effectiveDirections}. Fill exactly one of: SouthEast " +
                "only (Two), +NorthEast (Four), +South+North (Six), all five (Eight), or South only (One).",
                directionSet);
        }

        return new DirectionSetBlob
        {
            southEast = directionSet.southEast != null ? directionSet.southEast.Id : default,
            northEast = directionSet.northEast != null ? directionSet.northEast.Id : default,
            south     = directionSet.south != null ? directionSet.south.Id : default,
            north     = directionSet.north != null ? directionSet.north.Id : default,
            east      = directionSet.east != null ? directionSet.east.Id : default,
            effectiveDirections = effectiveDirections,
        };
    }
}
