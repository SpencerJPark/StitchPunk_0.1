// Copyright (c) 2026 Spencer Park. All rights reserved.

using UnityEngine;

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>
    /// A "logical animation" that turns: five east-side clip slots, mirrors always free.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Its effective <see cref="AnimationDirections"/> is derived, never declared.</strong>
    /// <see cref="TryGetEffectiveDirections"/> reads it off which slots are filled, so
    /// <see cref="FacingResolver"/> snaps into whatever the fill pattern actually supports rather
    /// than into a number somebody typed. West-side facings are never stored — every one of them is
    /// an east-side slot plus <c>PartFacing.mirrorX</c>, which is why a six-direction character costs
    /// four clips rather than six.
    /// </para>
    /// <para>
    /// An asset rather than an inline struct so one set is shareable across every unit built on the
    /// same rig. What a set means — which action or stance plays it — is host-owned; this package
    /// owns only the five slots and the derivation.
    /// </para>
    /// </remarks>
    [CreateAssetMenu(
        fileName = "NewDirectionSet",
        menuName = "DOTS Animation Toolkit/Direction Set Asset",
        order = 3)]
    public class DirectionSetAsset : ScriptableObject
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

        /// <summary>
        /// How many directions this set is <em>meant</em> to end up covering.
        /// </summary>
        /// <remarks>
        /// Authoring intent, not runtime data: nothing resolves a facing through it. It scaffolds the
        /// 2D Direction Sets panel's queue with the slots still to fill, and lets a bake say "authored
        /// below target" instead of silently shipping a half-drawn set. Coverage itself stays derived
        /// — see <see cref="TryGetEffectiveDirections"/>.
        /// </remarks>
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

        /// <summary>
        /// Writes <paramref name="clip"/> into the slot for an east-side facing. No-ops for a
        /// west-side one, which is served by its mirror and has no slot of its own.
        /// </summary>
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

        /// <summary>
        /// Derives the mirror-closed <see cref="AnimationDirections"/> this fill pattern actually
        /// covers.
        /// </summary>
        /// <returns>
        /// False for anything other than the five valid patterns (SouthEast only / +NorthEast /
        /// +South+North / all five / South only), in which case the out value is rounded down to the
        /// largest set whose required slots are all present.
        /// </returns>
        /// <remarks>
        /// Shared by every consumer — the bake's fill-pattern warning and the panel's live coverage
        /// readout both call this, so the two can never disagree about what a set covers.
        /// </remarks>
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

        /// <summary>
        /// The east-side slots a target coverage requires, in the order the fill pattern promotes
        /// through them.
        /// </summary>
        /// <remarks>
        /// The inverse of <see cref="TryGetEffectiveDirections"/>: that says what a fill pattern
        /// covers, this says what a coverage needs filled. Both must name the same five patterns, so
        /// they live beside each other rather than in a panel that could drift from the derivation.
        /// </remarks>
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

        /// <summary>
        /// The facings a coverage is made of, with south facing the camera — the mirror-closed member
        /// tables <see cref="AnimationDirections"/> documents.
        /// </summary>
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
