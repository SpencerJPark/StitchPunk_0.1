// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>
    /// Marks a GameObject as an animated actor root. Bake builds the registry blob from
    /// <see cref="rig"/> and <see cref="clipSets"/>, seeds each playback layer from
    /// <see cref="startingLayers"/>, and measures the actor-space rest bounds over its child
    /// <see cref="RigTargetAuthoring"/> parts.
    /// </summary>
    [AddComponentMenu("DOTS Animation Toolkit/Actor")]
    [DisallowMultipleComponent]
    public sealed class ActorAuthoring : MonoBehaviour
    {
        // Required: without a rig the actor bakes to nothing and the baker logs an error naming
        // this GameObject.
        [Tooltip("The rig this actor animates. Defines its layers, targets and target tags.")]
        public RigAsset rig;

        // Required: at least one entry, or the actor bakes to nothing. A track whose tag or target
        // id this actor's rig does not carry is skipped with a warning rather than failing the
        // bake, which is what lets one set serve a roster of differing rigs.
        [Tooltip("The clip sets this actor plays from. Their clips are merged into one registry.")]
        public List<ClipSetAsset> clipSets = new List<ClipSetAsset>();

        [Tooltip("Which clip each layer is seeded with. The rig's layer decides whether it starts playing.")]
        public List<StartingLayerState> startingLayers = new List<StartingLayerState>();

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

    /// <summary>One layer's starting clip on an <see cref="ActorAuthoring"/>.</summary>
    // The baked clipIndex is only meaningful against the same blob it was baked with — appending a
    // clip whose id sorts below an existing one renumbers every dense index above it — so it is
    // baked in the same pass that builds the blob and never carried across bakes.
    [Serializable]
    public sealed class StartingLayerState
    {
        [Tooltip("Which playback layer this entry seeds. Must be a valid layer index of the actor's rig.")]
        public int layerIndex;

        [Tooltip("The clip this layer starts on. Must be a member of one of the actor's clip sets.")]
        public ClipAsset clip;

        [Tooltip("Starting playback speed. Negative plays in reverse.")]
        public float speed = 1f;

        [Tooltip("Starting loop mode. UseClipDefault defers to the clip's own authored default.")]
        public LoopMode loop = LoopMode.UseClipDefault;
    }
}
