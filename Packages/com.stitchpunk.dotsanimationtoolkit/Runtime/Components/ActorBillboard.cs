// Copyright (c) 2026 Stitch Punk. All rights reserved.

using Unity.Entities;

namespace StitchPunk.AnimationToolkit
{
    /// <summary>
    /// Turns a whole actor to face the viewer by rotating its root transform
    /// (architecture section 6.3, amendment A41).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Why this exists alongside the shader billboard.</strong> The per-vertex path
    /// (<c>BillboardTransform</c> in <c>ToolkitBillboard.hlsl</c>) rotates <em>each quad about its
    /// own pivot</em>. That is right for a single-quad imposter and wrong for a layered cutout
    /// character: a dozen quads at different offsets would each turn independently and the authored
    /// arrangement would fan apart the moment the camera moved off axis. Rotating the root turns the
    /// rig as one rigid unit, which is the only thing that preserves a layered composition.
    /// </para>
    /// <para>
    /// <strong>An actor uses one path or the other, never both.</strong> A root rotated on the CPU
    /// and quads rotated in the shader is a double rotation — the same trap amendment A38 records
    /// for <c>mirrorX</c> against baked mirrored clips.
    /// </para>
    /// <para>
    /// Opt-in, like <c>AnimLod</c> and <c>PartFacing</c> (amendment A23's precedent): an actor
    /// without this component is not billboarded and pays nothing.
    /// </para>
    /// </remarks>
    public struct ActorBillboard : IComponentData
    {
        /// <summary>Which billboard rule to apply. Values match <c>ToolkitBillboard.hlsl</c>.</summary>
        public BillboardMode mode;

        /// <summary>
        /// Yaw in radians held by <see cref="BillboardMode.FrozenYaw"/>.
        /// </summary>
        /// <remarks>
        /// The host writes this at the moment it freezes — on death, in the case this was absorbed
        /// from. Capturing the yaw at that instant is what stops a corpse snapping to a new heading
        /// on the frame it dies.
        /// </remarks>
        public float frozenYaw;
    }

    /// <summary>
    /// How a billboard faces the viewer (architecture section 6.1). The numeric values are shared
    /// with <c>_BillboardParams.x</c> so the CPU and shader paths cannot drift apart.
    /// </summary>
    public enum BillboardMode : byte
    {
        /// <summary>No billboarding; the transform is left alone.</summary>
        Off = 0,

        /// <summary>Face the camera on every axis.</summary>
        Full = 1,

        /// <summary>Face the camera but stay upright — rotation about world Y only.</summary>
        Upright = 2,

        /// <summary>
        /// Hold an authored yaw while pitch still follows the camera. The corpse case: a dead body
        /// should not pirouette to follow the viewer, but it should still be seen rather than
        /// edge-on.
        /// </summary>
        FrozenYaw = 3,

        /// <summary>
        /// Align to the camera's forward vector rather than to its position, so every actor takes
        /// the same rotation (amendment A39). The classic 2.5D look.
        /// </summary>
        ScreenAligned = 4
    }
}
