// Copyright (c) 2026 Spencer Park. All rights reserved.

namespace DotsAnimationToolkit
{
    /// <summary>Which of eight ways an actor faces. South faces the camera, north faces away.</summary>
    public enum Direction : byte
    {
        /// <summary>Facing away from the camera.</summary>
        North = 0,

        /// <summary>Three-quarter view facing away, toward the right.</summary>
        NorthEast = 1,

        /// <summary>True profile, facing right.</summary>
        East = 2,

        /// <summary>Three-quarter view facing the camera, toward the right.</summary>
        SouthEast = 3,

        /// <summary>Facing the camera head-on.</summary>
        South = 4,

        /// <summary>Three-quarter view facing the camera, toward the left.</summary>
        SouthWest = 5,

        /// <summary>True profile, facing left.</summary>
        West = 6,

        /// <summary>Three-quarter view facing away, toward the left.</summary>
        NorthWest = 7
    }

    /// <summary>
    /// How many directional variants an actor has drawn for it — content, not rig. Each level from
    /// <see cref="Two"/> up nests the one below via mirroring (west clips = east clips with
    /// <c>PartFacing.mirrorX</c>); <see cref="One"/> needs no facing machinery at all.
    /// </summary>
    public enum AnimationDirections : byte
    {
        /// <summary>South only — an actor that does not turn.</summary>
        One = 0,

        /// <summary>The front three-quarter pair: SouthEast and SouthWest.</summary>
        Two = 1,

        /// <summary>Both three-quarter pairs: SouthEast, NorthEast, NorthWest, SouthWest.</summary>
        Four = 2,

        /// <summary>Both three-quarter pairs plus head-on and head-away.</summary>
        Six = 3,

        /// <summary>Every member of <see cref="Direction"/>, including true profile.</summary>
        Eight = 4
    }
}
