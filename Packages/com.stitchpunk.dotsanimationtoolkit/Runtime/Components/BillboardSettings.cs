// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System.Runtime.InteropServices;
using Unity.Mathematics;

namespace StitchPunk.AnimationToolkit
{
    /// <summary>
    /// Everything <see cref="BillboardMath"/> needs to resolve one billboard root's orientation
    /// (amendment A44): the authored configuration, plus the two channels a clip can key.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>One parameter block, shared by the runtime job and the editor preview.</strong> The
    /// preview must show exactly what the game will show, and the only way to guarantee that is for
    /// both to call the same function with the same struct. A preview that re-derived any of this
    /// would drift from the runtime the first time either changed — the failure
    /// <c>SocketPreviewParityTests</c> exists to prevent for sockets.
    /// </para>
    /// <para>
    /// <strong>Angles are radians here and degrees in the authoring asset.</strong> The conversion
    /// happens once, at bake, for the same reason <c>TransformKey</c>'s rotation does: degrees are
    /// what an author types and radians are what trigonometry consumes, and converting per frame
    /// would be paying for the author's convenience on every actor of every frame.
    /// </para>
    /// <para>
    /// <strong>A default-constructed value is inert.</strong> <see cref="enabled"/> is false and
    /// <see cref="blendWeight"/> is 0, so a settings block nobody filled in leaves the node's pose
    /// alone rather than snapping it somewhere. That is the safe direction for a struct the bake is
    /// responsible for populating.
    /// </para>
    /// </remarks>
    public struct BillboardSettings
    {
        /// <summary>Which billboard rule to apply.</summary>
        public BillboardMode mode;

        /// <summary>
        /// The axis <see cref="BillboardMode.AxisConstrained"/> turns about, in world space.
        /// Normalised by the bake; ignored by every other mode.
        /// </summary>
        public float3 constraintAxis;

        /// <summary>Yaw in radians held by <see cref="BillboardMode.FrozenYaw"/>.</summary>
        public float frozenYaw;

        /// <summary>
        /// Rotation in radians applied about the resolved frame's own up axis, after the facing is
        /// resolved and before snapping. The authored rest offset plus whatever a clip's billboard
        /// track has keyed on top of it.
        /// </summary>
        public float angleOffsetRadians;

        /// <summary>
        /// How much of the billboard orientation to take, against the node's animated pose. 1 is
        /// fully billboarded, 0 leaves the animated pose untouched, and values between slerp.
        /// Clamped to [0, 1] when resolved.
        /// </summary>
        public float blendWeight;

        /// <summary>
        /// Whether this root billboards at all this frame. Keyable, so a clip can hand a node back
        /// to its animation for a few frames and take it again afterwards.
        /// </summary>
        /// <remarks>
        /// <strong>The <c>MarshalAs</c> is load-bearing, not decoration.</strong> C#'s <c>bool</c>
        /// has no fixed width, so a struct containing a bare one is not blittable — and this struct
        /// crosses a <c>[BurstCompile]</c> external entry point, where only blittable types are
        /// allowed (Burst error BC1063). Without the attribute the whole Runtime assembly fails
        /// Burst compilation, not merely this field.
        /// </remarks>
        [MarshalAs(UnmanagedType.U1)] public bool enabled;

        /// <summary>
        /// How many discrete facings a full turn snaps to. <strong>Below 2 means no snapping</strong>
        /// — a sentinel rather than a companion bool, following the package's existing convention
        /// (<c>sliceIndex</c> −1 means "no change"), and because a one-step wheel is not a snap but
        /// a fixed direction.
        /// </summary>
        public int snapSteps;

        /// <summary>
        /// Phase of the snap wheel in radians, so its steps can straddle the cardinal directions
        /// rather than land on them.
        /// </summary>
        public float snapPhaseRadians;

        /// <summary>
        /// Half the width of the arc the node may turn within, in radians, measured from its rest
        /// orientation. <strong>Negative means no clamping.</strong>
        /// </summary>
        /// <remarks>
        /// Half rather than full because every use of it is symmetric about the rest orientation,
        /// and halving once at bake is cheaper than halving per actor per frame. Zero is a
        /// meaningful value — it pins the node to its rest orientation — so the "off" sentinel has
        /// to be negative rather than zero.
        /// </remarks>
        public float clampHalfArcRadians;

        /// <summary>True when <see cref="snapSteps"/> asks for a snap wheel.</summary>
        public bool SnapEnabled
        {
            get { return snapSteps >= 2; }
        }

        /// <summary>True when <see cref="clampHalfArcRadians"/> asks for an arc limit.</summary>
        public bool ClampEnabled
        {
            get { return clampHalfArcRadians >= 0f; }
        }
    }
}
