// Copyright (c) 2026 Stitch Punk. All rights reserved.

using Unity.Entities;
using Unity.Mathematics;

namespace StitchPunk.AnimationToolkit
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
    }
}
