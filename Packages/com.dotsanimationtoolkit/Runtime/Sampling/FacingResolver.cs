// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Burst;
using Unity.Mathematics;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Turns "which way is this character facing" into "which clip do I play, mirrored or not".
    /// Never combine the returned mirror with a clip already mirrored by the Mirror Clip utility —
    /// a double reflection fails silently, looking merely wrong-footed rather than broken.
    /// </summary>
    [BurstCompile]
    public static class FacingResolver
    {
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

        // Snapping is by side then by row, never by nearest angle: nearest-angle leaves a
        // four-direction character walking straight at the camera undefined between south-east and
        // south-west, and it flickers on a boundary. Here those two are the same clip mirrored or
        // not, so there is nothing to tie and nothing to flicker.
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

        /// <param name="movementXY">
        /// +x is east, +y is north. Passed by <c>in</c> and must stay that way: a
        /// <c>[BurstCompile]</c> static is an external entry point, and Burst cannot pass a vector
        /// across one by value (BC1064/BC1067).
        /// </param>
        /// <param name="currentFacing">Held when movement is too small to read, so a character coming to rest keeps facing the way it was going.</param>
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

            // Decided by sign, not by angle — see Snap's comment for why nearest-angle is wrong here.
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
