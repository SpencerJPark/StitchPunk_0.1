// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>
    /// One authored animation: duration, loop and blend defaults, the transform and sprite tracks
    /// bound to a rig's targets, the event markers on its timeline, and optional VAT sources
    /// consumed only by the editor's texture baker. Identified by <see cref="Id"/>.
    /// </summary>
    [CreateAssetMenu(
        fileName = "NewClip",
        menuName = "DOTS Animation Toolkit/Clip Asset",
        order = 1)]
    public sealed class ClipAsset : ScriptableObject, IStableIdMintReporter, ISerializationCallbackReceiver
    {
        /// <summary>The shortest legal clip duration in seconds.</summary>
        public const float MinimumDuration = 0.001f;

        [SerializeField] internal ulong stableId;

        // A clip names no rig. Motion and skeleton are independent assets, paired only where an
        // ActorAuthoring states both — a track lines up by tag against whatever rig it plays on,
        // and one that does not line up is skipped.

        [Tooltip("Clip length in seconds.")]
        [Min(MinimumDuration)] public float duration = 1f;

        // UseClipDefault is a command-side sentinel only, never an authored value here — it is
        // resolved to Once when the clip is baked.
        [Tooltip("Loop mode playback uses when a command does not override it.")]
        public LoopMode defaultLoop = LoopMode.Once;

        [Tooltip("Default crossfade-in length in seconds; 0 means the clip pops in. Clamped to duration at bake.")]
        [Min(0f)] public float defaultBlendIn;

        [Tooltip("Default fade-out length in seconds. Clamped to duration at bake.")]
        [Min(0f)] public float defaultBlendOut;

        [Tooltip("Poses held per second, for both the timeline and the VAT bake. Changing this re-bakes; keys stay where authored, since they're stored as a fraction of duration.")]
        [Min(1f)] public float frameRate = 30f;

        /// <summary>The clip's total frame count at its current <see cref="frameRate"/>, rounded to the nearest whole frame and never below one.</summary>
        // Derived rather than stored, so it cannot disagree with the two fields that define it.
        public int FrameCount
        {
            get
            {
                float frames = Mathf.Max(MinimumDuration, duration) * Mathf.Max(1f, frameRate);
                return Mathf.Max(1, Mathf.RoundToInt(frames));
            }
        }

        /// <summary>Keyed TRS curves, each bound to one rig target.</summary>
        public List<TransformTrack> transformTracks = new List<TransformTrack>();

        /// <summary>Keyed sprite-frame curves, each bound to one rig target.</summary>
        public List<SpriteTrack> spriteTracks = new List<SpriteTrack>();

        /// <summary>
        /// Keyed local TRS curves applied directly to named bones of the rig's skinned hierarchy —
        /// a second source for the VAT bake alongside <see cref="vatSource"/>/<see cref="vatTracks"/>.
        /// </summary>
        public List<BoneTrack> boneTracks = new List<BoneTrack>();

        /// <summary>Typed markers on this clip's timeline, emitted into the actor's event buffer at runtime.</summary>
        public List<EventMarker> events = new List<EventMarker>();

        /// <summary>
        /// Optional untargeted source for vertex-animation-texture baking, consumed only in-editor.
        /// Acts as the fallback range for any VAT part whose target is not named in <see cref="vatTracks"/>.
        /// </summary>
        public VatClipSource vatSource;

        /// <summary>
        /// Additional VAT sources scoped to specific rig targets — lets one clip drive several
        /// VAT-mesh parts from independent source animations (e.g. a torso and a cape baked from
        /// different clips under one clip identity).
        /// </summary>
        public List<VatTrack> vatTracks = new List<VatTrack>();

        /// <summary>Keyed billboard channels, each bound to one billboard root of the clip's rig. Empty for a clip that does not animate billboarding.</summary>
        public List<BillboardTrack> billboardTracks = new List<BillboardTrack>();

        /// <summary>This clip's stable 64-bit identity — the value games pass in <c>AnimationCommand</c> and the key the baked registry binary-searches.</summary>
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
                stableId = StableIdMinting.NewAssetStableId();
                hasUnpersistedStableId = true;
            }
        }


        // Not serialized: this describes an in-memory condition for the current session, and a
        // persisted "needs persisting" flag would contradict itself.
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
        /// <summary>Nothing to do — migration is a read-side concern.</summary>
        public void OnBeforeSerialize()
        {
        }

        // Brings clips authored against the 2.5D transform schema up to the 3D one. Runs on every
        // load and is idempotent (pure struct arithmetic — Unity may raise this off the main thread,
        // so no engine call belongs here). Both signals below mean "never written", not "is zero":
        // a rotation is adopted from the legacy angle only while the new one is still all-zero, and
        // a scale is corrected only when z is exactly 0, which collapses geometry and no author chooses.
        public void OnAfterDeserialize()
        {
            if (transformTracks == null)
            {
                return;
            }

            for (int trackIndex = 0; trackIndex < transformTracks.Count; trackIndex++)
            {
                TransformTrack track = transformTracks[trackIndex];
                if (track == null || track.keys == null)
                {
                    continue;
                }

                for (int keyIndex = 0; keyIndex < track.keys.Count; keyIndex++)
                {
                    TransformKey key = track.keys[keyIndex];
                    bool changed = false;

                    if (key.rotationZ != 0f && math.all(key.rotation == float3.zero))
                    {
                        key.rotation = new float3(0f, 0f, key.rotationZ);
                        key.rotationZ = 0f;
                        changed = true;
                    }

                    if (key.scale.z == 0f)
                    {
                        key.scale.z = 1f;
                        changed = true;
                    }

                    if (changed)
                    {
                        track.keys[keyIndex] = key;
                    }
                }
            }
        }

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
    /// A keyed TRS curve bound to one rig target. Several transform tracks may address the same
    /// target in one clip; the runtime applies all of them in canonical order.
    /// </summary>
    [Serializable]
    public sealed class TransformTrack
    {
        /// <summary>Stable id of the target this track animates. Meaningful only when <see cref="tagId"/> is 0.</summary>
        public uint targetId;

        // Sentinel, not a companion bool: non-zero means "bind by tag" and targetId is ignored; 0
        // means "bind by target id", so every clip authored before this field existed still reads
        // back correctly. Resolved at bake against the clip's rig; reported and skipped (never an
        // error) if no target carries the tag, or the tag no longer exists in the registry.
        /// <summary>The role this track animates, or 0 to bind by <see cref="targetId"/> instead.</summary>
        public uint tagId;

        /// <summary>Whether the track replaces its channels or adds onto the composited lower layers.</summary>
        public TrackBlendOp blendOp = TrackBlendOp.Override;

        /// <summary>The pose channels this track owns; channels outside the mask are left to lower layers.</summary>
        public AnimatedChannels channels = AnimatedChannels.PositionXY;

        /// <summary>Keys in strictly ascending <c>normalizedTime</c> order. The clip editor keeps them sorted on every edit.</summary>
        public List<TransformKey> keys = new List<TransformKey>();
    }

    /// <summary>One transform key. Rotation is authored in degrees and converted to radians at bake.</summary>
    [Serializable]
    public struct TransformKey
    {
        /// <summary>Key time as a fraction of the clip's duration, in [0, 1].</summary>
        public float normalizedTime;

        /// <summary>Local offset. For a 3D rig all three axes are position; for a 2.5D one z doubles as the draw-layer order.</summary>
        public float3 position;

        // Euler, not a quaternion: these are numbers an author types and drags. Bone tracks keep
        // quaternions instead, since those arrive from a bake or a solver and nobody types them.
        /// <summary>Local rotation in degrees, as Euler angles applied in Unity's ZXY order.</summary>
        public float3 rotation;

        // Retained only so clips authored before rotation became 3D keep their motion; consumed by
        // ClipAsset.OnAfterDeserialize. Deleting it early would silently flatten unmigrated rotations to zero.
        /// <summary>Legacy single-axis rotation in degrees, migrated into <see cref="rotation"/> on load.</summary>
        public float rotationZ;

        // A z of exactly 0 is read as unmigrated 2D data and corrected to 1 on load — it collapses
        // geometry to nothing, so no author chooses it deliberately.
        /// <summary>Non-uniform x/y/z scale; negative components flip the part on that axis.</summary>
        public float3 scale;

        /// <summary>Easing applied from this key to the next one.</summary>
        public Interpolation interpolation;

        // All-zero is the value a struct deserializes to when these fields did not exist yet;
        // ClipSampler.Ease reads that exact case as linear rather than as a degenerate curve.
        /// <summary>
        /// The first Bezier handle, in segment space: x is time across the segment, y is the blend
        /// weight. Read only when <see cref="interpolation"/> is <see cref="Interpolation.Bezier"/>.
        /// </summary>
        public float2 bezierStartHandle;

        /// <summary>The second Bezier handle. See <see cref="bezierStartHandle"/>.</summary>
        public float2 bezierEndHandle;
    }

    /// <summary>A keyed sprite-frame curve bound to one rig target.</summary>
    [Serializable]
    public sealed class SpriteTrack
    {
        /// <summary>Stable id of the target this track animates. Meaningful only when <see cref="tagId"/> is 0.</summary>
        public uint targetId;

        /// <summary>The role this track animates, or 0 to bind by <see cref="targetId"/> instead. Same sentinel convention as <see cref="TransformTrack.tagId"/>.</summary>
        public uint tagId;

        /// <summary>Whether the keys address Texture2DArray slices or atlas rects.</summary>
        public SpriteFrameMode mode = SpriteFrameMode.Slice;

        /// <summary>Whether slice keys are absolute frames or offsets from the part's rest slice. Ignored in <see cref="SpriteFrameMode.Atlas"/> mode.</summary>
        public SpriteSliceSpace sliceSpace = SpriteSliceSpace.Absolute;

        // A retargeting handle, not a stored result: relative keys hold only their offsets, so
        // moving this one number slides the whole track onto a different span of the texture array
        // without touching a single key.
        /// <summary>The array index every <see cref="SpriteIndexMode.RelativeToBase"/> key on this track offsets from. Ignored by absolute keys.</summary>
        public int baseIndex;

        /// <summary>Keys in strictly ascending <c>normalizedTime</c> order.</summary>
        public List<SpriteKey> keys = new List<SpriteKey>();
    }

    /// <summary>One sprite key.</summary>
    [Serializable]
    public struct SpriteKey
    {
        /// <summary>Key time as a fraction of the clip's duration, in [0, 1].</summary>
        public float normalizedTime;

        /// <summary>
        /// In <see cref="SpriteIndexMode.Absolute"/> mode, the slice index itself — −1 means "no
        /// change" and anything below is invalid. In <see cref="SpriteIndexMode.RelativeToBase"/>
        /// mode, an offset from the track's <c>baseIndex</c> where −1 is not a sentinel.
        /// </summary>
        public int sliceIndex;

        /// <summary>Whether <see cref="sliceIndex"/> is an index or an offset from the track's base.</summary>
        public SpriteIndexMode indexMode;

        /// <summary>Atlas-mode rect: scale in xy, offset in zw.</summary>
        public float4 atlasRect;
    }

    /// <summary>One typed marker on a clip's timeline.</summary>
    [Serializable]
    public struct EventMarker
    {
        /// <summary>Marker time as a fraction of the clip's duration, in [0, 1].</summary>
        public float normalizedTime;

        /// <summary>User event key. Keys below <see cref="ReservedEventKeys.FirstUserKey"/> belong to the package and are invalid here.</summary>
        public uint eventKey;

        /// <summary>User integer payload delivered with the emitted event.</summary>
        public int intParam;

        /// <summary>User float payload delivered with the emitted event.</summary>
        public float floatParam;

        // Seconds, not frames, even though the Clip Editor displays a frame count — a frame count
        // would make the real duration depend on the machine's frame rate.
        /// <summary>How long this marker holds its <see cref="AnimEventMask"/> bit open, in seconds. 0 makes it pulse-only.</summary>
        [Min(0f)] public float windowSeconds;
    }

    /// <summary>
    /// One authored bone track: keyed local TRS curves posing a single named bone of the rig's
    /// skinned hierarchy. Parallel to <see cref="TransformTrack"/> and <see cref="SpriteTrack"/>,
    /// not an extension of either — a bone track drives an imported bone's local transform
    /// directly, rather than a rig target's offset from rest.
    /// </summary>
    [Serializable]
    public sealed class BoneTrack
    {
        // Bones are named, not id'd, because a bone lives in an imported hierarchy this package does
        // not own and cannot mint a stable id for. Renaming a bone in the DCC tool breaks the
        // binding; the bake reports the unresolved name rather than silently pinning it to rest.
        /// <summary>Name of the posed bone — this track's only identity. Must be non-empty and unique within the clip.</summary>
        public string boneName = string.Empty;

        /// <summary>Keys in strictly ascending <c>normalizedTime</c> order.</summary>
        public List<BoneKey> keys = new List<BoneKey>();
    }

    /// <summary>
    /// One bone key: a full local TRS sample for one bone at one point on a clip's timeline,
    /// applied during VAT baking exactly as an imported <c>AnimationClip</c>'s sampled pose would be.
    /// </summary>
    [Serializable]
    public struct BoneKey
    {
        /// <summary>Key time as a fraction of the clip's duration, in [0, 1].</summary>
        public float normalizedTime;

        /// <summary>Local position offset from the bone's bind pose.</summary>
        public float3 localPosition;

        /// <summary>Local rotation, authored directly as a quaternion — a joint orientation is not expressible on one axis.</summary>
        public quaternion localRotation;

        /// <summary>Local non-uniform scale.</summary>
        public float3 localScale;

        /// <summary>Easing applied from this key to the next one.</summary>
        public Interpolation interpolation;

        // All-zero is the value a struct deserializes to when these fields did not exist yet;
        // ClipSampler.Ease reads that exact case as linear rather than as a degenerate curve.
        /// <summary>
        /// The first Bezier handle, in segment space: x is time across the segment, y is the blend
        /// weight. Read only when <see cref="interpolation"/> is <see cref="Interpolation.Bezier"/>.
        /// </summary>
        public float2 bezierStartHandle;

        /// <summary>The second Bezier handle. See <see cref="bezierStartHandle"/>.</summary>
        public float2 bezierEndHandle;
    }

    /// <summary>The vertex-animation-texture source of a clip. Read only by the editor's texture baker; entity baking consumes the resulting texture set, never this.</summary>
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
    /// One target-scoped vertex-animation-texture source. Read only by the editor's texture baker,
    /// exactly like <see cref="VatClipSource"/> — entity baking never sees this type, only the
    /// frame ranges it produced.
    /// </summary>
    [Serializable]
    public sealed class VatTrack
    {
        /// <summary>Stable id of the rig target this source drives. 0 always fails — it does not fall back to <see cref="ClipAsset.vatSource"/>.</summary>
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

    /// <summary>
    /// Keyed billboard channels for one billboard root: how far the root turns off the camera, how
    /// much of the billboard applies at all, and whether it applies this frame.
    /// </summary>
    // Bound to the root's own id, not to the node it turns — a root's address is editable (the same
    // root may be re-pointed from the hips to the torso), and a node-bound track would orphan itself.
    [Serializable]
    public sealed class BillboardTrack
    {
        /// <summary>Stable id of the billboard root this track animates. Must name a root the clip's rig declares.</summary>
        public uint rootStableId;

        /// <summary>Keys in strictly ascending <c>normalizedTime</c> order.</summary>
        public List<BillboardKey> keys = new List<BillboardKey>();
    }

    /// <summary>One billboard key.</summary>
    [Serializable]
    public struct BillboardKey
    {
        /// <summary>Key time as a fraction of the clip's duration, in [0, 1].</summary>
        public float normalizedTime;

        /// <summary>
        /// Rotation off the resolved facing, about the billboard frame's own up axis, in degrees.
        /// Added to the root's authored rest offset rather than replacing it, so a rig that sits
        /// permanently three-quarters-on can still be animated off that rest.
        /// </summary>
        public float angleOffsetDegrees;

        /// <summary>
        /// How much of the billboard orientation applies, against the node's animated pose. 1 is
        /// fully billboarded; 0 hands the node back to its animation.
        /// </summary>
        [Range(0f, 1f)] public float blendWeight;

        // Always held from its key, never eased — same as flipbook indices: an enable flag is a
        // discrete instruction, not an approximation of anything between two moments.
        /// <summary>Whether the root billboards at all from this key onward.</summary>
        public bool enabled;

        /// <summary>Easing applied from this key to the next one, for the continuous channels.</summary>
        public Interpolation interpolation;

        /// <summary>First Bézier handle (time, weight); read only for <c>Interpolation.Bezier</c>.</summary>
        public float2 bezierStartHandle;

        /// <summary>Second Bézier handle (time, weight); read only for <c>Interpolation.Bezier</c>.</summary>
        public float2 bezierEndHandle;
    }
}
