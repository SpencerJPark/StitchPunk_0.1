using DotsAnimationToolkit;
using DotsAnimationToolkit.Authoring;
using UnityEngine;

// Shared by every DirectionSetAsset baking site (UnitLibraryBakingSystem's idle/moving/stance/action
// mappings) so the fill-pattern warning and effective-count derivation live in exactly one place and
// can never disagree with what the 2D Direction Sets panel shows.
public static class DirectionSetBakeUtil
{
    public static DirectionSetBlob Bake(DirectionSetAsset directionSet, string context)
    {
        if (directionSet == null) return default;

        bool isValidFill = directionSet.slots.TryGetEffectiveDirections(out AnimationDirections effectiveDirections);
        if (!isValidFill)
        {
            Debug.LogWarning(
                $"[DirectionSetBaking] '{directionSet.name}' ({context}) has an invalid direction-slot " +
                $"fill pattern — rounding down to {effectiveDirections}. Fill exactly one of: SouthEast " +
                "only (Two), +NorthEast (Four), +South+North (Six), all five (Eight), or South only (One).",
                directionSet);
        }
        else if (effectiveDirections != directionSet.slots.targetDirections)
        {
            // A valid but unfinished set. Distinct from the warning above: nothing is wrong with the
            // pattern, it just does not reach the coverage the author said they were aiming for, and
            // the unit will quietly turn through fewer facings than intended.
            Debug.LogWarning(
                $"[DirectionSetBaking] '{directionSet.name}' ({context}) is authored below its target: " +
                $"covers {effectiveDirections}, targets {directionSet.slots.targetDirections}.",
                directionSet);
        }

        return new DirectionSetBlob
        {
            southEast = directionSet.slots.southEast != null ? directionSet.slots.southEast.Id : default,
            northEast = directionSet.slots.northEast != null ? directionSet.slots.northEast.Id : default,
            south     = directionSet.slots.south != null ? directionSet.slots.south.Id : default,
            north     = directionSet.slots.north != null ? directionSet.slots.north.Id : default,
            east      = directionSet.slots.east != null ? directionSet.slots.east.Id : default,
            effectiveDirections = effectiveDirections,
        };
    }
}
