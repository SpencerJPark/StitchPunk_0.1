// Copyright (c) 2026 Spencer Park. All rights reserved.

using UnityEngine;

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>
    /// Marks a GameObject as an animated actor root. Bake builds the registry and profile blobs
    /// from <see cref="profile"/>, seeds each playback layer from its <c>startingAnimationKey</c>s,
    /// and measures the actor-space rest bounds over its child <see cref="RigTargetAuthoring"/> parts.
    /// </summary>
    [AddComponentMenu("DOTS Animation Toolkit/Actor")]
    [DisallowMultipleComponent]
    public sealed class ActorAuthoring : MonoBehaviour
    {
        // Required: without a profile the actor bakes to nothing and the baker logs an error naming
        // this GameObject. Likewise a profile with no rig.
        [Tooltip("The profile that names this actor's rig, clip sets and layers.")]
        public ActorProfileAsset profile;

        // Only rateHz is read at bake; 0 falls back to AnimationToolkitConfig.defaultSampleRateHz.
        // phase01 is not authored here — it is a per-instance value derived at bake, and re-derived
        // at spawn, which is what spreads a crowd's sampling across frames.
        [Tooltip("Sampling override. Only rateHz is used; phase01 is derived per instance at bake.")]
        public SampleSettings sampleOverride;

        [Tooltip("Give this actor an AnimLod component so distance LOD (and host LOD writes) apply to it.")]
        public bool addDistanceLod;

        // Rotates the whole root as one unit — the right choice for a layered cutout character. Do
        // not also billboard in the material; two rotations are no rotation.
        [Tooltip("Rotate the whole actor to face the camera. Do NOT also billboard in the material.")]
        public BillboardMode billboardMode = BillboardMode.Off;

        [Tooltip("Yaw held by Frozen Yaw mode, in degrees.")]
        public float frozenYawDegrees;
    }
}
