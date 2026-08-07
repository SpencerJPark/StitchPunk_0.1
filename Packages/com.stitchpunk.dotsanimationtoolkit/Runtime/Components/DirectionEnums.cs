// Copyright (c) 2026 Stitch Punk. All rights reserved.

namespace StitchPunk.AnimationToolkit
{
    /// <summary>
    /// Which of eight ways an actor faces (architecture section 10 answer 7, amendment A38).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Package-owned <em>data</em>, not selection logic. Deciding which direction an actor faces is
    /// driven by movement and AI state the package cannot know, so that stays game-side; what the
    /// package owns is the vocabulary both sides agree on, and the tables in
    /// <see cref="AnimationDirections"/> that say which members a given actor actually has.
    /// </para>
    /// <para>
    /// The names are compass points with <strong>south facing the camera and north facing away</strong>.
    /// </para>
    /// </remarks>
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
    /// How many directional variants an actor has drawn for it (amendment A38).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Per actor, not per rig.</strong> A citizen and a boss can share a rig and differ in
    /// how many directions were drawn for them, which is why this is a property of the content
    /// rather than of the skeleton.
    /// </para>
    /// <para>
    /// The member sets, with south facing the camera:
    /// </para>
    /// <list type="table">
    /// <listheader><term>Count</term><description>Members</description></listheader>
    /// <item><term><see cref="One"/></term><description>South</description></item>
    /// <item><term><see cref="Two"/></term><description>SouthEast, SouthWest</description></item>
    /// <item><term><see cref="Four"/></term><description>SouthEast, NorthEast, NorthWest, SouthWest</description></item>
    /// <item><term><see cref="Six"/></term><description>South, SouthEast, NorthEast, North, NorthWest, SouthWest</description></item>
    /// <item><term><see cref="Eight"/></term><description>all of <see cref="Direction"/></description></item>
    /// </list>
    /// <para>
    /// <strong>Every set from <see cref="Two"/> upward is closed under mirroring</strong>, and each
    /// level adds one mirror-symmetric pair to the one below: <see cref="Two"/> is the front
    /// three-quarter pair, <see cref="Four"/> adds the back pair, <see cref="Six"/> adds head-on and
    /// head-away, <see cref="Eight"/> adds true profile. Only South and North are their own mirrors.
    /// </para>
    /// <para>
    /// That is what makes a directional clip set cheap: the west-facing members are the east-facing
    /// clips with <c>PartFacing.mirrorX</c> set, so authoring cost is
    /// (self-symmetric members) + (mirror pairs) — <strong>1, 1, 2, 4 and 5 clips</strong> for the
    /// five counts. A four-direction character costs two locomotion clips per state, not four.
    /// </para>
    /// <para>
    /// <see cref="One"/> sits deliberately outside that nesting: it is not the first direction of a
    /// turning actor but the absence of turning — a boss at the top of the screen, or a stationary
    /// effect. It faces the camera head-on rather than taking <see cref="Two"/>'s three-quarter
    /// view, and it needs no facing machinery at all: no <c>PartFacing</c>, no quantization, no
    /// mirror.
    /// </para>
    /// </remarks>
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
