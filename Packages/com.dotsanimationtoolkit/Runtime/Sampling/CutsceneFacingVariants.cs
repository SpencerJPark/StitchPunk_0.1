// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;
using Unity.Mathematics;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Turns a cutscene facing angle into the clip a direction set serves it with (amendment A65
    /// §3.2): quantize to the actor's turn granularity, fold onto what the set actually covers, then
    /// take the east-side sibling.
    /// </summary>
    /// <remarks>
    /// <strong>One copy, called by the runtime player and by the Cutscene Editor's preview.</strong>
    /// The preview picked a variant this way first (A58); a second implementation for playback is
    /// exactly the preview-versus-play divergence the single-sampler rule exists to prevent, and
    /// facing is the one lane where a divergence looks like working art pointing the wrong way
    /// rather than like a bug.
    /// </remarks>
    [BurstCompile]
    public static class CutsceneFacingVariants
    {
        /// <summary>
        /// Resolves <paramref name="angleDegrees"/> (0 = east, 90 = north, the
        /// <see cref="CutsceneFacing"/> model) into the east-side clip facing that serves it.
        /// </summary>
        /// <param name="mirrorX">
        /// True when the resolved facing is a west-side one served by mirroring. The toolkit does
        /// not apply it (decision A65-D2) — the host's facing system does, from
        /// <see cref="CutsceneFacing"/> — but the preview mirrors on it, and a caller that wants to
        /// know whether a turn is a mirror or a different clip needs it.
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
            // so the same instant must resolve the same way however it was reached — scrubbed
            // backwards in the editor, or played forwards.
            Direction memberFacing =
                FacingResolver.FromMovement(in facingVector, targetDirections, Direction.SouthEast);
            Direction foldedFacing = FacingResolver.Snap(memberFacing, effectiveDirections);
            FacingResolver.ToAuthoredSide(foldedFacing, out clipFacing, out mirrorX);
        }

        /// <summary>The set's clip for an east-side facing, or 0 where the set leaves that slot empty.</summary>
        /// <remarks>
        /// Deliberately not a <c>[BurstCompile]</c> entry point: the blob it reads carries a
        /// <c>bool</c>, which is not blittable across one (BC1063), and both callers — the timeline
        /// player and the editor preview — are managed anyway.
        /// </remarks>
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

        /// <summary>
        /// The facing angle a travel vector implies, in the <see cref="CutsceneFacing"/> model.
        /// </summary>
        /// <remarks>
        /// <c>atan2(z, x)</c>, not <c>atan2(x, z)</c>: the y component of the vector
        /// <see cref="FacingResolver.FromMovement"/> reads is north, which is world +Z, and the x
        /// component is east. Measuring from +Z instead — the <c>LocalTransform</c> Y-euler
        /// convention — reflects every derived facing about the 45° line and quietly turns an actor
        /// walking east into one facing north.
        /// </remarks>
        [BurstCompile]
        public static float AngleDegreesFromTravel(in float3 travel)
        {
            float angleDegrees = math.degrees(math.atan2(travel.z, travel.x));
            return angleDegrees < 0f ? angleDegrees + 360f : angleDegrees;
        }
    }
}
