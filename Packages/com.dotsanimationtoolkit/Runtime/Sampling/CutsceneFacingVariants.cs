// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;
using Unity.Mathematics;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Turns a cutscene facing angle into the clip a direction set serves it with: quantize to the
    /// actor's turn granularity, fold onto what the set actually covers, then take the east-side sibling.
    /// </summary>
    [BurstCompile]
    public static class CutsceneFacingVariants
    {
        /// <summary>Resolves <paramref name="angleDegrees"/> (0 = east, 90 = north) into the east-side clip facing that serves it.</summary>
        /// <param name="mirrorX">
        /// True when the resolved facing is a west-side one served by mirroring. The toolkit does
        /// not apply the mirror itself — the host's facing system does, from
        /// <see cref="CutsceneFacing"/> — but a caller distinguishing a mirror from a different clip needs this.
        /// </param>
        [BurstCompile]
        public static void Resolve(
            float angleDegrees,
            AnimationDirections targetDirections,
            AnimationDirections effectiveDirections,
            out Direction clipFacing,
            out bool mirrorX)
        {
            float angleRadians = math.radians(angleDegrees);
            float2 facingVector = new float2(math.cos(angleRadians), math.sin(angleRadians));

            // No hysteresis seed: a cutscene's angle is authored or derived from an authored lane,
            // so the same instant must resolve the same way regardless of direction — scrubbed
            // backwards in the editor, or played forwards.
            Direction memberFacing =
                FacingResolver.FromMovement(in facingVector, targetDirections, Direction.SouthEast);
            Direction foldedFacing = FacingResolver.Snap(memberFacing, effectiveDirections);
            FacingResolver.ToAuthoredSide(foldedFacing, out clipFacing, out mirrorX);
        }

        // Deliberately not a [BurstCompile] entry point: the blob it reads carries a bool, which is
        // not blittable across one (BC1063), and both callers are managed anyway.
        /// <summary>The set's clip for an east-side facing, or 0 where the set leaves that slot empty.</summary>
        public static ulong SelectVariantClipId(
            in CutsceneDirectionVariantsBlob variants, Direction clipFacing)
        {
            switch (clipFacing)
            {
                case Direction.South: return variants.south;
                case Direction.SouthEast: return variants.southEast;
                case Direction.East: return variants.east;
                case Direction.NorthEast: return variants.northEast;
                case Direction.North: return variants.north;
                default: return 0UL;
            }
        }

        // atan2(z, x), not atan2(x, z): the vector's y component (read by FacingResolver.FromMovement)
        // is north (world +Z) and x is east. Measuring from +Z instead (the LocalTransform Y-euler
        // convention) reflects every derived facing about the 45-degree line.
        /// <summary>The facing angle a travel vector implies, in the <see cref="CutsceneFacing"/> model.</summary>
        [BurstCompile]
        public static float AngleDegreesFromTravel(in float3 travel)
        {
            float angleDegrees = math.degrees(math.atan2(travel.z, travel.x));
            return angleDegrees < 0f ? angleDegrees + 360f : angleDegrees;
        }
    }
}
