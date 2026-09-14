// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Entities;
using Unity.Mathematics;

namespace DotsAnimationToolkit
{
    /// <summary>World configuration singleton. Auto-created with defaults by <c>ConfigBootstrapSystem</c> when absent, so the package needs zero setup.</summary>
    public struct AnimationToolkitConfig : IComponentData
    {
        public float defaultSampleRateHz; // Hz, for actors whose SampleSettings.rateHz is 0; 0 = every frame

        public bool distanceLodEnabled; // enables the optional AnimLodDistanceSystem; default false

        public float4 lodDistancesSq; // squared camera-distance thresholds; x/y/z promote AnimLod.level to 1/2/3, w reserved
    }

    /// <summary>Camera singleton that <c>BillboardResolveSystem</c> and <c>AnimLodDistanceSystem</c> both wait for. Written by the host every frame, or by the Camera Sync sample's <c>ToolkitCameraSync</c>.</summary>
    public struct AnimationToolkitCameraData : IComponentData
    {
        public float3 position; // world space

        // World-space forward, host-written like position. Must be pass-invariant (the same value
        // under the ShadowCaster pass's light-view matrix) so a screen-aligned billboard doesn't
        // face one way on screen and another in its own shadow. Zero falls back to spherical mode.
        public float3 forward;
    }
}
