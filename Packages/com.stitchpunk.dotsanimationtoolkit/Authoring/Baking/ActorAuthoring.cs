// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>
    /// Marks a GameObject as an animated actor root (architecture sections 4.1, 5.2). Bake produces
    /// the actor archetype of section 5.2 from this component: the registry blob built from
    /// <see cref="clipSet"/>, one <see cref="PlaybackLayer"/> element per rig layer seeded from
    /// <see cref="startingLayers"/>, the command and event buffers, the binding/visibility/bounds
    /// enableables, and the actor-space <see cref="ActorRestBounds"/> measured over the child
    /// <see cref="RigTargetAuthoring"/> parts.
    /// </summary>
    /// <remarks>
    /// One actor references exactly one <see cref="ClipSetAsset"/>, and every actor referencing the
    /// same set shares one baked blob through the <c>BlobAssetStore</c>'s content-hash dedup
    /// (architecture section 4.5). The rig comes from the set, never from this component, so an
    /// actor cannot disagree with its own clips about which rig it animates.
    /// </remarks>
    [AddComponentMenu("DOTS Animation Toolkit/Actor")]
    [DisallowMultipleComponent]
    public sealed class ActorAuthoring : MonoBehaviour
    {
        /// <summary>
        /// The clip set this actor plays from. Required: without it the actor bakes to nothing and
        /// the baker logs an error naming this GameObject.
        /// </summary>
        [Tooltip("The clip set this actor plays from. Its rig defines the layers and targets.")]
        public ClipSetAsset clipSet;

        /// <summary>
        /// What each playback layer starts holding. An entry names a layer index and the clip that
        /// layer is seeded with; whether the layer starts <em>playing</em> is the rig's
        /// <see cref="LayerDefinition.defaultActive"/> decision, because that is a property of the
        /// rig's layer contract rather than of one actor's starting pose.
        /// </summary>
        [Tooltip("Which clip each layer is seeded with. The rig's layer decides whether it starts playing.")]
        public List<StartingLayerState> startingLayers = new List<StartingLayerState>();

        /// <summary>
        /// Per-actor sampling override. <strong>Only <see cref="SampleSettings.rateHz"/> is read at
        /// bake</strong> — 0 means "fall back to <see cref="AnimationToolkitConfig.defaultSampleRateHz"/>".
        /// <see cref="SampleSettings.phase01"/> is <em>not</em> authored: architecture section 5.6
        /// makes it a per-instance value derived at bake and, once build step C4 adds
        /// <c>RigBindingSystem</c>, re-derived at spawn — which is what spreads a crowd's sampling
        /// across frames.
        /// </summary>
        [Tooltip("Sampling override. Only rateHz is used; phase01 is derived per instance at bake (section 5.6).")]
        public SampleSettings sampleOverride;

        /// <summary>
        /// Opts this actor into animation level of detail by giving it the <see cref="AnimLod"/>
        /// component (architecture section 5.10). The optional <c>AnimLodDistanceSystem</c> — itself
        /// switched on world-wide by <see cref="AnimationToolkitConfig.distanceLodEnabled"/> — writes
        /// <see cref="AnimLod.level"/> only on actors that carry the component, so this checkbox is
        /// the per-actor half of that opt-in. An actor without it always animates at full quality.
        /// </summary>
        [Tooltip("Give this actor an AnimLod component so distance LOD (and host LOD writes) apply to it.")]
        public bool addDistanceLod;

        /// <summary>
        /// Billboard this actor by rotating its root, so the whole rig turns as one unit
        /// (amendment A41). <c>Off</c> adds no component and costs nothing.
        /// </summary>
        /// <remarks>
        /// This is the transform path, which is the right one for a layered cutout character. Do
        /// not also enable a per-vertex billboard in the material — two rotations are no rotation.
        /// </remarks>
        [Tooltip("Rotate the whole actor to face the camera. Do NOT also billboard in the material.")]
        public BillboardMode billboardMode = BillboardMode.Off;

        /// <summary>Yaw held by FrozenYaw mode, in degrees. Authoring is degrees; the bake converts.</summary>
        [Tooltip("Yaw held by Frozen Yaw mode, in degrees.")]
        public float frozenYawDegrees;
    }

    /// <summary>
    /// One layer's starting clip on an <see cref="ActorAuthoring"/> (architecture sections 4.1, 5.2).
    /// The baker resolves <see cref="clip"/> against the registry blob it just built and writes the
    /// resulting dense clip index into the layer's <see cref="PlaybackLayer"/> element.
    /// </summary>
    /// <remarks>
    /// The baked <c>clipIndex</c> is only meaningful against that same blob — appending a clip whose
    /// id sorts below an existing one renumbers every dense index above it (architecture section 4.2,
    /// amendment A11) — which is precisely why the index is baked in the same pass that builds the
    /// blob and never carried across bakes.
    /// </remarks>
    [Serializable]
    public sealed class StartingLayerState
    {
        /// <summary>Index of the playback layer to seed; must be a valid layer of the set's rig.</summary>
        [Tooltip("Which playback layer this entry seeds. Must be a valid layer index of the set's rig.")]
        public int layerIndex;

        /// <summary>The clip this layer starts on. Must be a member of the actor's clip set.</summary>
        [Tooltip("The clip this layer starts on. Must be a member of the actor's clip set.")]
        public ClipAsset clip;

        /// <summary>Starting playback speed; negative values play the clip in reverse.</summary>
        [Tooltip("Starting playback speed. Negative plays in reverse.")]
        public float speed = 1f;

        /// <summary>
        /// Starting loop mode. <see cref="LoopMode.UseClipDefault"/> defers to the clip's authored
        /// default, which is the usual choice.
        /// </summary>
        [Tooltip("Starting loop mode. UseClipDefault defers to the clip's own authored default.")]
        public LoopMode loop = LoopMode.UseClipDefault;
    }
}
