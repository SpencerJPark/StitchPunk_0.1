// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Entities;
using Unity.Mathematics;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// World configuration singleton (architecture section 5.2). Auto-created with defaults by
    /// <c>ConfigBootstrapSystem</c> when absent, so the package needs zero setup.
    /// </summary>
    public struct AnimationToolkitConfig : IComponentData
    {
        /// <summary>World-default sample rate in Hz for actors whose <see cref="SampleSettings.rateHz"/> is 0; 0 = every frame (default).</summary>
        public float defaultSampleRateHz;

        /// <summary>Enables the optional <c>AnimLodDistanceSystem</c>; default false.</summary>
        public bool distanceLodEnabled;

        /// <summary>
        /// Squared camera-distance thresholds consumed by <c>AnimLodDistanceSystem</c>
        /// (architecture section 5.10): crossing x, y, and z promotes
        /// <see cref="AnimLod.level"/> to 1, 2, and 3 respectively; w is reserved.
        /// </summary>
        public float4 lodDistancesSq;
    }

    /// <summary>
    /// Camera position singleton consumed only by <c>AnimLodDistanceSystem</c>
    /// (architecture section 5.2). Written by the host, or by the <c>ToolkitCameraSync</c>
    /// sample MonoBehaviour.
    /// </summary>
    public struct AnimationToolkitCameraData : IComponentData
    {
        /// <summary>The rendering camera's world-space position.</summary>
        public float3 position;

        /// <summary>
        /// The rendering camera's world-space forward vector (amendment A39).
        /// </summary>
        /// <remarks>
        /// <para>
        /// Carried alongside <see cref="position"/> so a billboard can be <em>screen-aligned</em> —
        /// every quad taking the same rotation — as well as <em>spherical</em>, each quad turning to
        /// face the camera point. Those are visibly different looks, and §6.3's rule does not choose
        /// between them: it requires only that facing come from a value that is <strong>the same in
        /// every pass</strong>, because the ShadowCaster pass substitutes the light's view matrix and
        /// a quad that faced differently there would cast the shadow of a shape the camera never
        /// sees. A host-written forward vector is as pass-invariant as
        /// <c>_WorldSpaceCameraPos</c>, so it satisfies that requirement exactly.
        /// </para>
        /// <para>
        /// Host-written, like <see cref="position"/> — the package never reads a
        /// <c>Camera</c>. Leave it at zero and screen-aligned modes fall back to spherical, which is
        /// the safe degradation: a wrong-looking billboard rather than a degenerate one.
        /// </para>
        /// </remarks>
        public float3 forward;
    }
}
