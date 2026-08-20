// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;
using Unity.Mathematics;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Turns "which way is this character facing" into "which clip do I play, mirrored or not"
    /// (architecture amendment A38, section 10 answer 7).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Every direction set is closed under mirroring, and the whole scheme rests on that.</strong>
    /// <see cref="Direction.South"/> and <see cref="Direction.North"/> are their own mirrors;
    /// everything else pairs east↔west. So a facing is served by an <em>east-side</em> clip plus a
    /// mirror flag, and a four-direction character costs two locomotion clips per state rather than
    /// four. That is a far bigger saving than "mirroring halves the work", and it is why
    /// <c>PartFacing.mirrorX</c> is runtime state rather than a second set of baked clips.
    /// </para>
    /// <para>
    /// <strong>Never combine this with a mirrored clip authored by the Mirror Clip utility.</strong>
    /// Baked mirrored keys plus a runtime mirror is a double reflection, which is no reflection at
    /// all — and it fails silently, looking merely "wrong-footed" rather than broken. A facing uses
    /// one route or the other: the runtime mirror this returns, or its own authored clip played
    /// with <c>mirrorX</c> false.
    /// </para>
    /// <para>
    /// <strong>The sets are owner-specified, not derived from compass geometry.</strong> They are
    /// chosen by what reads well on screen: <see cref="AnimationDirections.Two"/> is the classic
    /// side-scroller three-quarter view, tilted toward the camera so the face stays visible — not a
    /// pure profile. <see cref="AnimationDirections.Four"/> is diagonals only, with no head-on view
    /// at all. Reasoning from angles rather than from this table produces sets that look plausible
    /// and are wrong.
    /// </para>
    /// </remarks>
    [BurstCompile]
    public static class FacingResolver
    {
        /// <summary>
        /// Resolves a desired facing into the clip facing to play and whether to mirror it.
        /// </summary>
        /// <param name="desiredFacing">The direction the character wants to face.</param>
        /// <param name="availableDirections">How many directions this character has art for.</param>
        /// <param name="clipFacing">The east-side facing whose clip should be played.</param>
        /// <param name="mirrorX">True when that clip must be mirrored to serve the facing.</param>
        [BurstCompile]
        public static void ResolveClipFacing(
            Direction desiredFacing,
            AnimationDirections availableDirections,
            out Direction clipFacing,
            out bool mirrorX)
        {
            Direction snappedFacing = Snap(desiredFacing, availableDirections);
            ToAuthoredSide(snappedFacing, out clipFacing, out mirrorX);
        }

        /// <summary>
        /// Snaps an arbitrary facing onto a member of <paramref name="availableDirections"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Snapping is by <em>side</em> then by <em>row</em>, never by nearest angle. Nearest-angle
        /// leaves a four-direction character walking straight at the camera undefined between
        /// south-east and south-west, and it flickers when the facing sits on a boundary. Under
        /// this scheme those two are the same clip with the mirror on or off, so there is nothing
        /// to tie and nothing to flicker.
        /// </para>
        /// <para>
        /// Facings with no horizontal component resolve to the east side, and pure profiles in sets
        /// that lack them resolve to the front row. Both are arbitrary but must be <em>fixed</em>:
        /// a character that picked a different answer frame to frame would jitter between two
        /// mirrored poses.
        /// </para>
        /// </remarks>
        [BurstCompile]
        public static Direction Snap(Direction desiredFacing, AnimationDirections availableDirections)
        {
            if (availableDirections == AnimationDirections.One)
            {
                // One means "this does not turn" — a boss at the top of the screen, a stationary
                // effect. Deliberately outside the nesting, and head-on rather than three-quarter.
                return Direction.South;
            }

            bool facesWest = IsWestSide(desiredFacing);

            if (availableDirections == AnimationDirections.Eight)
            {
                return desiredFacing;
            }

            if (availableDirections == AnimationDirections.Six)
            {
                // Six has head-on and head-away but no true profile, so east and west fold into the
                // nearest three-quarter on the same side.
                if (desiredFacing == Direction.East)
                {
                    return Direction.SouthEast;
                }
                if (desiredFacing == Direction.West)
                {
                    return Direction.SouthWest;
                }
                return desiredFacing;
            }

            if (availableDirections == AnimationDirections.Four)
            {
                // Diagonals only. South and North have no row of their own here, so they fall to
                // the front and back three-quarters respectively, on the east side by convention.
                if (desiredFacing == Direction.South)
                {
                    return Direction.SouthEast;
                }
                if (desiredFacing == Direction.North)
                {
                    return Direction.NorthEast;
                }
                if (desiredFacing == Direction.East)
                {
                    return Direction.SouthEast;
                }
                if (desiredFacing == Direction.West)
                {
                    return Direction.SouthWest;
                }
                return desiredFacing;
            }

            // Two: the front three-quarter pair only. Everything collapses onto the row that keeps
            // the face toward the camera, which is the entire point of the side-scroller view.
            return facesWest ? Direction.SouthWest : Direction.SouthEast;
        }

        /// <summary>
        /// Maps a facing onto its authored east-side counterpart plus a mirror flag.
        /// </summary>
        [BurstCompile]
        public static void ToAuthoredSide(Direction facing, out Direction clipFacing, out bool mirrorX)
        {
            switch (facing)
            {
                case Direction.SouthWest:
                    clipFacing = Direction.SouthEast;
                    mirrorX = true;
                    return;
                case Direction.NorthWest:
                    clipFacing = Direction.NorthEast;
                    mirrorX = true;
                    return;
                case Direction.West:
                    clipFacing = Direction.East;
                    mirrorX = true;
                    return;
                default:
                    // South and North are their own mirrors; the east side is already authored.
                    clipFacing = facing;
                    mirrorX = false;
                    return;
            }
        }

        /// <summary>
        /// Quantizes a movement vector to a facing within <paramref name="availableDirections"/>.
        /// </summary>
        /// <param name="movementXY">
        /// Horizontal movement in screen-ish space: +x is east, +y is north (away from the camera).
        /// <strong>Passed by <c>in</c>, and it must stay that way.</strong> A <c>[BurstCompile]</c>
        /// static method is an <em>external entry point</em>, and Burst cannot pass a struct — least
        /// of all a vector type — across one by value (BC1064/BC1067). Every other Burst-compiled
        /// static in this package takes primitives, enums, or by-ref structs for exactly this
        /// reason; this parameter was by value once and broke the whole Runtime assembly's Burst
        /// compilation, not just this method.
        /// </param>
        /// <param name="availableDirections">How many directions this character has art for.</param>
        /// <param name="currentFacing">
        /// Held when movement is too small to read, so a character coming to rest keeps facing the
        /// way it was going instead of snapping to a default.
        /// </param>
        [BurstCompile]
        public static Direction FromMovement(
            in float2 movementXY,
            AnimationDirections availableDirections,
            Direction currentFacing)
        {
            // Squared magnitude, so a near-stationary actor does not spin through facings on
            // floating-point noise. The threshold is small enough that any real step reads.
            if (math.lengthsq(movementXY) < 1e-6f)
            {
                return currentFacing;
            }

            bool movingWest = movementXY.x < 0f;
            bool movingNorth = movementXY.y > 0f;

            // Decided by sign, not by angle — see Snap's remarks for why nearest-angle is the wrong
            // instrument here.
            bool horizontalDominates = math.abs(movementXY.x) > math.abs(movementXY.y);

            Direction rawFacing;
            if (availableDirections == AnimationDirections.Eight && horizontalDominates
                && math.abs(movementXY.y) < math.abs(movementXY.x) * 0.4142f)
            {
                // Inside ~22.5 degrees of horizontal: a true profile, which only Eight can show.
                rawFacing = movingWest ? Direction.West : Direction.East;
            }
            else if (!horizontalDominates
                     && math.abs(movementXY.x) < math.abs(movementXY.y) * 0.4142f
                     && (availableDirections == AnimationDirections.Eight
                         || availableDirections == AnimationDirections.Six))
            {
                // Inside ~22.5 degrees of vertical, and the set has a head-on or head-away view.
                rawFacing = movingNorth ? Direction.North : Direction.South;
            }
            else if (movingNorth)
            {
                rawFacing = movingWest ? Direction.NorthWest : Direction.NorthEast;
            }
            else
            {
                rawFacing = movingWest ? Direction.SouthWest : Direction.SouthEast;
            }

            return Snap(rawFacing, availableDirections);
        }

        private static bool IsWestSide(Direction facing)
        {
            return facing == Direction.SouthWest
                || facing == Direction.West
                || facing == Direction.NorthWest;
        }
    }
}
