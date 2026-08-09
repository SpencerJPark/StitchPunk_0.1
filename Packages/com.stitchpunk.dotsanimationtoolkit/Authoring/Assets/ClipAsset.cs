// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace StitchPunk.AnimationToolkit.Authoring
{
    /// <summary>
    /// One authored animation (architecture section 3.2): duration, loop and blend defaults, the
    /// transform and sprite tracks bound to <see cref="rig"/>'s targets, the event markers on its
    /// timeline, and an optional VAT source consumed only by the editor's texture baker.
    /// Identified by <see cref="Id"/> (architecture section 3.4).
    /// </summary>
    /// <remarks>
    /// Tracks, keys, and markers are serialized inline on this asset — there is no sub-asset per
    /// keyframe. A clip may itself be a sub-asset of the <see cref="ClipSetAsset"/> that registers
    /// it (keeping a set self-contained for distribution) or free-standing; the registry builder
    /// only follows references and does not care which.
    /// </remarks>
    [CreateAssetMenu(
        fileName = "NewClip",
        menuName = "DOTS Animation Toolkit/Clip Asset",
        order = 1)]
    public sealed class ClipAsset : ScriptableObject, IStableIdMintReporter
    {
        /// <summary>The shortest legal clip duration in seconds (validation rule V01).</summary>
        public const float MinimumDuration = 0.001f;

        [SerializeField] internal ulong stableId;

        /// <summary>
        /// The rig this clip is authored against. Every track's <c>targetId</c> must name a target
        /// of this rig (validation rule V02), and a clip may only join a set whose rig is the same
        /// asset (validation rule V06).
        /// </summary>
        public RigAsset rig;

        /// <summary>Clip length in seconds. Values below <see cref="MinimumDuration"/> fail validation rule V01.</summary>
        [Min(MinimumDuration)] public float duration = 1f;

        /// <summary>
        /// The loop mode playback uses when a command does not override it. Authoring never means
        /// <see cref="LoopMode.UseClipDefault"/> here — that value is a command-side sentinel and is
        /// resolved to <see cref="LoopMode.Once"/> when the clip is baked.
        /// </summary>
        public LoopMode defaultLoop = LoopMode.Once;

        /// <summary>Default crossfade-in length in seconds; 0 means the clip pops in. Clamped to <see cref="duration"/> at bake (validation rule V12).</summary>
        [Min(0f)] public float defaultBlendIn;

        /// <summary>Default fade-out length in seconds. Clamped to <see cref="duration"/> at bake (validation rule V12).</summary>
        [Min(0f)] public float defaultBlendOut;

        /// <summary>Keyed TRS curves, each bound to one rig target.</summary>
        public List<TransformTrack> transformTracks = new List<TransformTrack>();

        /// <summary>Keyed sprite-frame curves, each bound to one rig target.</summary>
        public List<SpriteTrack> spriteTracks = new List<SpriteTrack>();

        /// <summary>Typed markers on this clip's timeline, emitted into the actor's event buffer at runtime.</summary>
        public List<EventMarker> events = new List<EventMarker>();

        /// <summary>
        /// Optional <em>untargeted</em> source for vertex-animation-texture baking. Consumed only
        /// in-editor by the VAT texture baker; a clip carrying one requires its set to reference a
        /// texture set holding a matching frame range (validation rule V07).
        /// </summary>
        /// <remarks>
        /// This is the single-source shape the toolkit shipped with before <see cref="vatTracks"/>
        /// (C10): it names no target, so every <c>VatDriven</c> part bound to this clip resolves the
        /// same baked range from it. It stays first-class rather than becoming sugar over a
        /// zero-target <see cref="VatTrack"/>, because every existing project's clips already carry
        /// a populated instance of this exact field (see the A36 note on <c>ClipValidation</c>) and
        /// a schema change here would be a silent migration for data this package cannot see. A
        /// clip that also carries <see cref="vatTracks"/> entries keeps this as the fallback range
        /// for any VAT part whose target is not named by one of them — so adding a targeted track to
        /// one part of an actor never disturbs the parts that never asked for one.
        /// </remarks>
        public VatClipSource vatSource;

        /// <summary>
        /// Additional VAT sources scoped to specific rig targets (architecture section 3.2, C10) —
        /// what lets one clip drive several VAT-mesh parts from independent source animations, e.g.
        /// a torso baked from <c>walk_torso.anim</c> and a cape baked from <c>walk_cape.anim</c>
        /// under one clip identity. A track's baked range overrides <see cref="vatSource"/>'s only
        /// for the part bound to its <see cref="VatTrack.targetId"/>; every other VAT part on the
        /// same clip keeps resolving <see cref="vatSource"/> exactly as it did before this list
        /// existed.
        /// </summary>
        /// <remarks>
        /// Unlike <see cref="vatSource"/>, this list carries none of the amendment A36
        /// serialization trap: it is a <c>List&lt;VatTrack&gt;</c>, and Unity round-trips an empty
        /// list as an empty list rather than manufacturing a phantom element the way it does for a
        /// lone <c>[Serializable]</c> class field. A clip that never used this feature therefore
        /// reads back with a genuinely empty list, and "does this clip have a track for target X"
        /// can be answered by iterating it directly — no null-vs-default disambiguation is needed
        /// the way it is for <see cref="vatSource"/>.
        /// </remarks>
        public List<VatTrack> vatTracks = new List<VatTrack>();

        /// <summary>
        /// This clip's stable 64-bit identity (architecture section 3.4) — the value games pass in
        /// <c>AnimationCommand</c> and the key the baked registry binary-searches.
        /// </summary>
        public ClipId Id
        {
            get { return new ClipId(stableId); }
        }

        /// <summary>
        /// Assigns a fresh stable id when this clip still carries the reserved 0 value. Idempotent,
        /// so an existing id survives every edit, rename, reorder, and re-serialization.
        /// </summary>
        internal void EnsureStableIds()
        {
            if (stableId == 0UL)
            {
                stableId = StableIdUtility.NewAssetStableId();
                hasUnpersistedStableId = true;
            }
        }


        // Not serialized: this describes an in-memory condition for the current session, and a
        // persisted "needs persisting" flag would contradict itself (amendment A14).
        [System.NonSerialized] private bool hasUnpersistedStableId;

        /// <inheritdoc />
        public bool HasUnpersistedStableId
        {
            get { return hasUnpersistedStableId; }
        }

        /// <inheritdoc />
        public void MarkStableIdPersisted()
        {
            hasUnpersistedStableId = false;
        }

        // Unity raises Awake when an instance is created and OnEnable both after creation and
        // after an asset is deserialized, so between them no asset can reach an inspector, a bake,
        // or a test without an id. Both funnel into the same idempotent assignment.
        private void Awake()
        {
            EnsureStableIds();
        }

        private void OnEnable()
        {
            EnsureStableIds();
        }

        private void OnValidate()
        {
            EnsureStableIds();
        }

        private void Reset()
        {
            EnsureStableIds();
        }
    }

    /// <summary>
    /// A keyed TRS curve bound to one rig target (architecture section 3.2). Several transform
    /// tracks may address the same target in one clip; the runtime applies all of them in canonical
    /// order (architecture section 5.6).
    /// </summary>
    [Serializable]
    public sealed class TransformTrack
    {
        /// <summary>Stable id of the <c>RigTargetDefinition</c> this track animates (validation rule V02).</summary>
        public uint targetId;

        /// <summary>Whether the track replaces its channels or adds onto the composited lower layers.</summary>
        public TrackBlendOp blendOp = TrackBlendOp.Override;

        /// <summary>The pose channels this track owns; channels outside the mask are left to lower layers.</summary>
        public AnimatedChannels channels = AnimatedChannels.PositionXY;

        /// <summary>
        /// Keys in strictly ascending <c>normalizedTime</c> order. The clip editor keeps them sorted
        /// on every edit; hand-edited assets are caught by validation rule V03.
        /// </summary>
        public List<TransformKey> keys = new List<TransformKey>();
    }

    /// <summary>One transform key (architecture section 3.2). Rotation is authored in degrees and converted to radians at bake.</summary>
    [Serializable]
    public struct TransformKey
    {
        /// <summary>Key time as a fraction of the clip's duration, in [0, 1] (validation rule V04).</summary>
        public float normalizedTime;

        /// <summary>Local offset: x/y in the plane, z as the 2.5D draw-layer order.</summary>
        public float3 position;

        /// <summary>Rotation about z in degrees.</summary>
        public float rotationZ;

        /// <summary>Non-uniform x/y scale; negative components flip the part.</summary>
        public float2 scale;

        /// <summary>Easing applied from this key to the next one.</summary>
        public Interpolation interpolation;
    }

    /// <summary>
    /// A keyed sprite-frame curve bound to one rig target (architecture section 3.2).
    /// </summary>
    [Serializable]
    public sealed class SpriteTrack
    {
        /// <summary>Stable id of the <c>RigTargetDefinition</c> this track animates (validation rule V02).</summary>
        public uint targetId;

        /// <summary>Whether the keys address Texture2DArray slices or atlas rects.</summary>
        public SpriteFrameMode mode = SpriteFrameMode.Slice;

        /// <summary>
        /// Whether slice keys are absolute frames or offsets from the part's rest slice
        /// (amendment A37). Ignored in <see cref="SpriteFrameMode.Atlas"/> mode, which has no rest
        /// value to be relative to.
        /// </summary>
        public SpriteSliceSpace sliceSpace = SpriteSliceSpace.Absolute;

        /// <summary>Keys in strictly ascending <c>normalizedTime</c> order (validation rule V03).</summary>
        public List<SpriteKey> keys = new List<SpriteKey>();
    }

    /// <summary>One sprite key (architecture section 3.2).</summary>
    [Serializable]
    public struct SpriteKey
    {
        /// <summary>Key time as a fraction of the clip's duration, in [0, 1] (validation rule V04).</summary>
        public float normalizedTime;

        /// <summary>Slice-mode frame index; −1 means "no change". Values below −1 fail validation rule V14.</summary>
        public int sliceIndex;

        /// <summary>Atlas-mode rect: scale in xy, offset in zw.</summary>
        public float4 atlasRect;
    }

    /// <summary>One typed marker on a clip's timeline (architecture sections 3.2, 5.5).</summary>
    [Serializable]
    public struct EventMarker
    {
        /// <summary>Marker time as a fraction of the clip's duration, in [0, 1] (validation rule V04).</summary>
        public float normalizedTime;

        /// <summary>
        /// User event key. Keys below <see cref="ReservedEventKeys.FirstUserKey"/> belong to the
        /// package and fail validation rule V09.
        /// </summary>
        public uint eventKey;

        /// <summary>User integer payload delivered with the emitted event.</summary>
        public int intParam;

        /// <summary>User float payload delivered with the emitted event.</summary>
        public float floatParam;
    }

    /// <summary>
    /// The vertex-animation-texture source of a clip (architecture sections 3.2, 4.7). Read only by
    /// the editor's texture baker; entity baking consumes the resulting texture set, never this.
    /// </summary>
    [Serializable]
    public sealed class VatClipSource
    {
        /// <summary>The Unity animation clip sampled at bake time.</summary>
        public AnimationClip sourceClip;

        /// <summary>Sampling rate in frames per second; overrides the bake settings' rate for this clip.</summary>
        [Min(1f)] public float sampleFps = 30f;

        /// <summary>
        /// When true the baker appends one extra frame duplicating frame 0, so the shader's
        /// two-row lerp never reads across the clip boundary at the loop seam.
        /// </summary>
        public bool loopSafe;

    }

    /// <summary>
    /// One target-scoped vertex-animation-texture source (architecture sections 3.2, 4.7; C10).
    /// Read only by the editor's texture baker, exactly like <see cref="VatClipSource"/> — entity
    /// baking never sees this type, only the frame ranges it produced.
    /// </summary>
    /// <remarks>
    /// Deliberately shaped as a sibling of <see cref="VatClipSource"/> rather than a reuse of it:
    /// the two differ only by the added <see cref="targetId"/>, but giving this its own type keeps
    /// <see cref="ClipAsset.vatSource"/>'s meaning ("the untargeted source") from becoming ambiguous
    /// with a <see cref="ClipAsset.vatTracks"/> entry whose <see cref="targetId"/> happens to be the
    /// reserved 0 value. A track naming 0 targets nothing (<c>TargetId.IsValid</c> is false for it)
    /// and fails validation rule V02, the same as any other track bound to a target the rig does not
    /// declare — the toolkit never treats 0 here as "same as vatSource".
    /// </remarks>
    [Serializable]
    public sealed class VatTrack
    {
        /// <summary>
        /// Stable id of the rig target this source drives (validation rule V02). Must name a target
        /// this clip's rig actually declares; the reserved value 0 always fails that check.
        /// </summary>
        public uint targetId;

        /// <summary>The Unity animation clip sampled at bake time for <see cref="targetId"/>.</summary>
        public AnimationClip sourceClip;

        /// <summary>Sampling rate in frames per second; overrides the bake settings' rate for this track.</summary>
        [Min(1f)] public float sampleFps = 30f;

        /// <summary>
        /// When true the baker appends one extra frame duplicating frame 0, so the shader's
        /// two-row lerp never reads across the clip boundary at the loop seam.
        /// </summary>
        public bool loopSafe;
    }
}
