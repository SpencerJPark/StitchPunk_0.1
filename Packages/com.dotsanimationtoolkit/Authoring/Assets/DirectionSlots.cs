// Copyright (c) 2026 Spencer Park. All rights reserved.

using UnityEngine;

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>
    /// Five east-side clip slots, mirrored to cover the west side for free. Effective coverage is
    /// derived from which slots are filled, never declared.
    /// </summary>
    [System.Serializable]
    public sealed class DirectionSlots
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

        [Tooltip("How many directions this set is meant to cover once finished. Authoring intent only " +
                 "— actual coverage is always derived from which slots are filled.")]
        public AnimationDirections targetDirections = AnimationDirections.Six;

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

        /// <summary>No-ops for a west-side facing, which is served by its mirror and has no slot of its own.</summary>
        public void SetSlot(Direction eastSideFacing, ClipAsset clip)
        {
            switch (eastSideFacing)
            {
                case Direction.SouthEast: southEast = clip; break;
                case Direction.NorthEast: northEast = clip; break;
                case Direction.South: south = clip; break;
                case Direction.North: north = clip; break;
                case Direction.East: east = clip; break;
            }
        }

        /// <summary>Derives the mirror-closed <see cref="AnimationDirections"/> this fill pattern actually covers.</summary>
        /// <returns>
        /// False for anything other than the five valid patterns, in which case the out value is
        /// rounded down to the largest set whose required slots are all present.
        /// </returns>
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

        /// <summary>The east-side slots a target coverage requires, in the order the fill pattern promotes through them.</summary>
        public static Direction[] GetRequiredSlots(AnimationDirections directions)
        {
            switch (directions)
            {
                case AnimationDirections.One:
                    return new[] { Direction.South };
                case AnimationDirections.Two:
                    return new[] { Direction.SouthEast };
                case AnimationDirections.Four:
                    return new[] { Direction.SouthEast, Direction.NorthEast };
                case AnimationDirections.Six:
                    return new[]
                    {
                        Direction.SouthEast, Direction.NorthEast, Direction.South, Direction.North
                    };
                default:
                    return new[]
                    {
                        Direction.SouthEast, Direction.NorthEast, Direction.South, Direction.North,
                        Direction.East
                    };
            }
        }

        /// <summary>The facings a coverage is made of, with south facing the camera.</summary>
        public static Direction[] GetMembers(AnimationDirections directions)
        {
            switch (directions)
            {
                case AnimationDirections.One:
                    return new[] { Direction.South };
                case AnimationDirections.Two:
                    return new[] { Direction.SouthEast, Direction.SouthWest };
                case AnimationDirections.Four:
                    return new[]
                    {
                        Direction.SouthEast, Direction.NorthEast, Direction.NorthWest, Direction.SouthWest
                    };
                case AnimationDirections.Six:
                    return new[]
                    {
                        Direction.South, Direction.SouthEast, Direction.NorthEast,
                        Direction.North, Direction.NorthWest, Direction.SouthWest
                    };
                default:
                    return new[]
                    {
                        Direction.North, Direction.NorthEast, Direction.East, Direction.SouthEast,
                        Direction.South, Direction.SouthWest, Direction.West, Direction.NorthWest
                    };
            }
        }
    }
}
