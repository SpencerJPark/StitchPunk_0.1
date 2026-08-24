// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace DotsAnimationToolkit.Authoring
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
    public sealed class ClipAsset : ScriptableObject, IStableIdMintReporter, ISerializationCallbackReceiver
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

        /// <summary>
        /// The clip's frame rate: how many poses it holds per second. Total frames is
        /// <c>duration * frameRate</c>, and <see cref="duration"/> alone sets how long it lasts.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>The rate the animation is stored and played at.</strong> It rules the Clip
        /// Editor's timeline, and it is the rate a VAT bake samples the clip at: a clip's block of
        /// the texture is <c>duration * frameRate</c> rows, and the range's fps — what the shader
        /// steps through those rows with — is this number. Twelve here bakes twelve rows a second
        /// and reads on screen as twelve frames a second, which is why it is one number rather than
        /// an authoring grid sitting next to a separate bake setting that could disagree with it.
        /// </para>
        /// <para>
        /// It is not <c>SampleSettings.rateHz</c>. That is a per-actor throttle on how often the
        /// runtime re-evaluates a pose — a performance knob, tied to distance through the LOD
        /// policy — and it belongs to the actor rather than to the animation.
        /// </para>
        /// <para>
        /// <strong>Changing it means re-baking.</strong> The row count of a baked texture is fixed
        /// at bake time, so a clip whose rate moved afterwards is stale until the VAT set is baked
        /// again; the bake hash folds the rate in, so rule V08 sees the change.
        /// </para>
        /// <para>
        /// <strong>Changing it cannot destroy key data.</strong> Keys are stored as a fraction of
        /// the clip's duration, so the frame grid is a view over them rather than a container for
        /// them: raising or lowering the rate re-rules the timeline and moves no key. A key that
        /// does not land exactly on a frame of the new grid stays where it is and is reported
        /// rather than snapped — an authored key is data, and a display setting must not quietly
        /// rewrite it.
        /// </para>
        /// </remarks>
        [Min(1f)] public float frameRate = 30f;

        /// <summary>
        /// The clip's total frame count at its current <see cref="frameRate"/>, rounded to the
        /// nearest whole frame and never below one.
        /// </summary>
        /// <remarks>
        /// Derived rather than stored, so it cannot disagree with the two fields that define it.
        /// The transport bar shows it read-only for the same reason.
        /// </remarks>
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
        /// Authored bone tracks (Amendment A42): keyed local TRS curves applied directly to named
        /// bones of the rig's skinned hierarchy, as a second source for the VAT bake alongside the
        /// imported-<see cref="AnimationClip"/> path (<see cref="vatSource"/>, <see cref="vatTracks"/>).
        /// A track's own <see cref="BoneTrack.boneName"/> is its identity — bones are named rather
        /// than id'd, because they live in an imported hierarchy this package does not own and
        /// cannot assign a stable id to (the same asymmetry <c>RigAsset.SocketDefinition</c> already
        /// carries for bone-attached sockets).
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is a <c>List&lt;BoneTrack&gt;</c>, not a lone <c>[Serializable]</c> class field, so it
        /// does not repeat the amendment A36 serialization trap documented on
        /// <see cref="ClipValidation.ValidateVatCoverageInto"/> and on <see cref="vatTracks"/> above:
        /// Unity round-trips an empty <c>List&lt;T&gt;</c> as an empty list, never manufacturing a
        /// phantom element the way it does for a lone <c>[Serializable]</c> class field with no
        /// <c>[SerializeReference]</c>. A36's trap exists specifically because <see cref="vatSource"/>
        /// is a single class-typed field, so deserializing a clip that never set it still produces a
        /// non-null instance with null contents, which is indistinguishable from "opted in with
        /// nothing filled in" unless the predicate also checks the inner reference. A list carries no
        /// such ambiguity: after a disk round trip, "no bone tracks were authored" and "this list is
        /// empty" are exactly the same fact, so <c>boneTracks == null || boneTracks.Count == 0</c> (or
        /// simply iterating it) answers "does this clip author any bones" correctly with no
        /// null-vs-default disambiguation, the same way <see cref="vatTracks"/> already does.
        /// </para>
        /// <para>
        /// A clip that has never used bone tracks therefore reads back with a genuinely empty list,
        /// never carries any validation cost (rules V03/V04/V15/V16 all short-circuit on zero
        /// entries), and never contributes anything to the bake beyond a zero-length blob array —
        /// exactly like <see cref="vatTracks"/> for a clip that has never used multi-source VAT.
        /// </para>
        /// </remarks>
        public List<BoneTrack> boneTracks = new List<BoneTrack>();

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
        /// Keyed billboard channels, each bound to one billboard root of the clip's rig
        /// (amendment A44). Empty for every clip that does not animate billboarding.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A fifth track kind rather than three more channels on <see cref="TransformTrack"/>, for
        /// A42's reason exactly: extending <see cref="TransformKey"/> would grow <em>every cutout
        /// key</em> — the format most clips use — with fields they never set, and blob size is the
        /// one cost a crowd pays per clip.
        /// </para>
        /// <para>
        /// <strong>Unlike bone tracks, these are runtime data.</strong> A42's correction established
        /// that authored bone tracks never enter the blob, because nothing samples a bone at
        /// runtime — they are bake input, like <see cref="vatSource"/>. These are the opposite:
        /// <c>BillboardResolveSystem</c> samples them every frame, so they are baked, hashed, and
        /// carried in <c>ClipBlob</c>.
        /// </para>
        /// </remarks>
        public List<BillboardTrack> billboardTracks = new List<BillboardTrack>();

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
        /// <summary>Nothing to do — migration is a read-side concern.</summary>
        public void OnBeforeSerialize()
        {
        }

        /// <summary>
        /// Brings clips authored against the 2.5D transform schema up to the 3D one.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Runs on every load and is idempotent, so it does not depend on the asset being re-saved
        /// to stick — an unmigrated clip read a hundred times migrates the same way a hundred times.
        /// Pure struct arithmetic, because Unity may raise this off the main thread and any engine
        /// call here would be a race rather than an error anyone could read.
        /// </para>
        /// <para>
        /// <strong>Both signals are "the field was never written", not "the field is zero".</strong>
        /// A rotation is only adopted from the legacy angle when the new one is still all zeros, so
        /// a genuine 3D rotation is never overwritten by a stale value. A scale is only corrected
        /// when its z is exactly 0, which no author chooses — it collapses the part to nothing.
        /// </para>
        /// </remarks>
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
    /// A keyed TRS curve bound to one rig target (architecture section 3.2). Several transform
    /// tracks may address the same target in one clip; the runtime applies all of them in canonical
    /// order (architecture section 5.6).
    /// </summary>
    [Serializable]
    public sealed class TransformTrack
    {
        /// <summary>
        /// Stable id of the <c>RigTargetDefinition</c> this track animates (validation rule V02).
        /// Meaningful only when <see cref="tagId"/> is the reserved 0 — see that field's remarks for
        /// the sentinel convention the two share.
        /// </summary>
        public uint targetId;

        /// <summary>
        /// The role this track animates, or 0 to bind by <see cref="targetId"/> instead (Phase E
        /// target-tags spec §4.3).
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>Sentinel, not a companion bool.</strong> A non-zero value means "bind by tag" and
        /// <see cref="targetId"/> is ignored; 0 means "bind by target id", the track's behaviour
        /// before this field existed. This is the same shape <see cref="SpriteKey.sliceIndex"/>'s
        /// -1 and <c>SnapSettings.snapSteps</c>'s "&lt; 2" sentinels already use elsewhere in this
        /// package, rather than a second <c>bool bindsByTag</c> field: a sentinel cannot itself
        /// disagree with the value it gates, where a bool-plus-id pair could hold
        /// <c>bindsByTag == true</c> beside a stale <c>tagId == 0</c> and leave a resolver guessing
        /// which field is the lie. Every clip authored before this field existed deserializes it as
        /// 0, so it reads back exactly as "bind by target id" — no migration needed.
        /// </para>
        /// <para>
        /// Resolved at bake against the clip's rig (spec §5): the dense index of whichever rig
        /// target currently carries this tag, or the track is reported and skipped if none does
        /// (rule T2) or the tag no longer exists in the registry (rule T3). Both target-id and
        /// tag-bound tracks stay first-class — sharing is opt-in per track, never a migration, so a
        /// character-specific track with no role to name never has to invent a junk tag (spec §4.3).
        /// </para>
        /// </remarks>
        public uint tagId;

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

        /// <summary>
        /// Local offset. For a 3D rig all three axes are position; for a 2.5D one z doubles as the
        /// draw-layer order.
        /// </summary>
        public float3 position;

        /// <summary>
        /// Local rotation in degrees, as Euler angles applied in Unity's ZXY order.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Three angles rather than one, because a rig is not necessarily flat — a vehicle animated
        /// in this system needs pitch and yaw as much as roll. A 2.5D cutout simply leaves x and y
        /// at zero and pays nothing for them.
        /// </para>
        /// <para>
        /// Euler rather than a quaternion, because these are the numbers an author types and drags:
        /// three readable fields with a sign and a magnitude. Bone tracks keep quaternions
        /// (<see cref="BoneKey.localRotation"/>) because nobody types those — they arrive from a
        /// bake or a solver.
        /// </para>
        /// </remarks>
        public float3 rotation;

        /// <summary>
        /// Legacy single-axis rotation in degrees, migrated into <see cref="rotation"/> on load.
        /// </summary>
        /// <remarks>
        /// Retained only so clips authored before rotation became 3D keep their motion. It is
        /// consumed by <c>ClipAsset.OnAfterDeserialize</c> and can be deleted once no project in
        /// flight still has unmigrated assets — but deleting it early would silently flatten every
        /// existing clip's rotation to zero, which is the one outcome a migration exists to prevent.
        /// </remarks>
        public float rotationZ;

        /// <summary>
        /// Non-uniform x/y/z scale; negative components flip the part on that axis.
        /// </summary>
        /// <remarks>
        /// A z component of exactly 0 is read as unmigrated 2D data and corrected to 1 on load: it
        /// collapses geometry to nothing, so it is never a value anyone authored deliberately.
        /// </remarks>
        public float3 scale;

        /// <summary>Easing applied from this key to the next one.</summary>
        public Interpolation interpolation;

        /// <summary>
        /// The first Bézier handle, in segment space: x is time across the segment, y is the blend
        /// weight. Read only when <see cref="interpolation"/> is <see cref="Interpolation.Bezier"/>.
        /// </summary>
        /// <remarks>
        /// Both handles zero is the value a struct deserializes to when the fields did not exist,
        /// and a zero-length handle pair is not a curve. <c>ClipSampler.Ease</c> reads that exact
        /// case as linear rather than as a degenerate solve — the same defensive reading
        /// <see cref="BoneKey.localRotation"/> needs for an all-zero quaternion, and for the same
        /// reason: a struct's zero value is not always a meaningful value.
        /// </remarks>
        public float2 bezierStartHandle;

        /// <summary>The second Bézier handle. See <see cref="bezierStartHandle"/>.</summary>
        public float2 bezierEndHandle;
    }

    /// <summary>
    /// A keyed sprite-frame curve bound to one rig target (architecture section 3.2).
    /// </summary>
    [Serializable]
    public sealed class SpriteTrack
    {
        /// <summary>
        /// Stable id of the <c>RigTargetDefinition</c> this track animates (validation rule V02).
        /// Meaningful only when <see cref="tagId"/> is the reserved 0 — see that field's remarks for
        /// the sentinel convention the two share.
        /// </summary>
        public uint targetId;

        /// <summary>
        /// The role this track animates, or 0 to bind by <see cref="targetId"/> instead (Phase E
        /// target-tags spec §4.3). Same sentinel convention as <see cref="TransformTrack.tagId"/> —
        /// see its remarks for why a sentinel rather than a companion bool, and how it resolves at
        /// bake.
        /// </summary>
        public uint tagId;

        /// <summary>Whether the keys address Texture2DArray slices or atlas rects.</summary>
        public SpriteFrameMode mode = SpriteFrameMode.Slice;

        /// <summary>
        /// Whether slice keys are absolute frames or offsets from the part's rest slice
        /// (amendment A37). Ignored in <see cref="SpriteFrameMode.Atlas"/> mode, which has no rest
        /// value to be relative to.
        /// </summary>
        public SpriteSliceSpace sliceSpace = SpriteSliceSpace.Absolute;

        /// <summary>
        /// The array index every <see cref="SpriteIndexMode.RelativeToBase"/> key on this track
        /// offsets from. Ignored by absolute keys.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>This is a retargeting handle, not a stored result.</strong> Relative keys hold
        /// their offsets and nothing else, so moving this one number slides the whole track onto a
        /// different span of the texture array — the same mouth shapes on a different character's
        /// block — without touching a single key. Baking the sum into the keys instead would make
        /// this a one-time edit that quietly consumed itself.
        /// </para>
        /// <para>
        /// Several tracks may drive the same target with different bases, which is how one texture
        /// array holds independent feature sets: a mouth track based at 0 and an eye track based at
        /// 32 animate the same part without either knowing about the other.
        /// </para>
        /// </remarks>
        public int baseIndex;

        /// <summary>Keys in strictly ascending <c>normalizedTime</c> order (validation rule V03).</summary>
        public List<SpriteKey> keys = new List<SpriteKey>();
    }

    /// <summary>One sprite key (architecture section 3.2).</summary>
    [Serializable]
    public struct SpriteKey
    {
        /// <summary>Key time as a fraction of the clip's duration, in [0, 1] (validation rule V04).</summary>
        public float normalizedTime;

        /// <summary>
        /// The key's stored number. In <see cref="SpriteIndexMode.Absolute"/> it is the slice index
        /// itself, where −1 means "no change" and anything below −1 fails validation rule V14. In
        /// <see cref="SpriteIndexMode.RelativeToBase"/> it is an offset from the track's
        /// <c>baseIndex</c>, where negatives are ordinary steps backwards and −1 is not a sentinel.
        /// </summary>
        /// <remarks>
        /// Deliberately still one field rather than a stored offset beside a stored absolute. Two
        /// fields would let them disagree, and then "which one is true" becomes a question the
        /// asset cannot answer. One number plus <see cref="indexMode"/> always resolves the same
        /// way, and <c>SpriteIndexResolver</c> is the only thing that resolves it.
        /// </remarks>
        public int sliceIndex;

        /// <summary>
        /// Whether <see cref="sliceIndex"/> is an index or an offset from the track's base
        /// (validation rule V18). Defaults to <see cref="SpriteIndexMode.Absolute"/>, which is the
        /// zero value — so every clip authored before this field existed keeps its exact meaning.
        /// </summary>
        public SpriteIndexMode indexMode;

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

        /// <summary>
        /// How long this marker holds its <see cref="AnimEventMask"/> bit open, in seconds
        /// (validation rules V19, V20). 0 — the default, and what every marker authored before this
        /// field existed carries — makes the marker pulse-only: it still emits into the actor's
        /// event buffer with its payload, it just never opens a window.
        /// </summary>
        /// <remarks>
        /// Seconds, not frames, even though the Clip Editor presents it as a frame count. A frame
        /// count would make the window's real length depend on the machine's frame rate, so the same
        /// authored attack would connect on a fast machine and miss on a slow one. The frame count
        /// in the inspector is this value divided by the project's reference rate, computed for
        /// display only.
        /// </remarks>
        [Min(0f)] public float windowSeconds;
    }

    /// <summary>
    /// One authored bone track (Amendment A42): keyed local TRS curves posing a single named bone
    /// of the rig's skinned hierarchy. Parallel to <see cref="TransformTrack"/> and
    /// <see cref="SpriteTrack"/>, not an extension of either — a bone track drives an imported
    /// bone's local transform directly, rather than a rig target's offset from rest.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is deliberately a new track kind rather than <see cref="TransformKey"/> gaining a
    /// quaternion and a <c>float3</c> scale. <see cref="TransformKey"/> carries
    /// <c>float rotationZ</c> because a 2.5D cutout part only ever needs one rotation axis; growing
    /// it to a full joint orientation would add channels every cutout key carries and never sets,
    /// for a technique most clips never use. Blob size is the one cost a crowd actually pays per
    /// clip, so the toolkit's existing split by "what a track drives"
    /// (<see cref="ClipAsset.transformTracks"/>, <see cref="ClipAsset.spriteTracks"/>) grows a
    /// fourth member instead, and a cutout clip's on-disk layout is untouched by this type existing.
    /// </para>
    /// <para>
    /// <strong>Bones are named, targets are id'd.</strong> A rig target is a row this package owns
    /// and can mint a stable <c>targetId</c> for (architecture section 3.4); a bone lives in an
    /// imported hierarchy this package does not own, so <see cref="boneName"/> is the only handle
    /// Unity gives us — the identical asymmetry <c>RigAsset.SocketDefinition.boneName</c> already
    /// carries for bone-attached sockets. Renaming a bone in the DCC tool breaks the binding; the
    /// bake must report the unresolved name rather than silently baking a bone pinned to rest.
    /// </para>
    /// </remarks>
    [Serializable]
    public sealed class BoneTrack
    {
        /// <summary>
        /// Name of the posed bone in the rig's imported skinned hierarchy (validation rule V15 for
        /// emptiness, V16 for a duplicate within one clip). This is the track's identity — there is
        /// no stable id, because the bone is not a row this package owns (see remarks above).
        /// </summary>
        public string boneName = string.Empty;

        /// <summary>
        /// Keys in strictly ascending <c>normalizedTime</c> order. The clip editor keeps them sorted
        /// on every edit; hand-edited assets are caught by validation rule V03.
        /// </summary>
        public List<BoneKey> keys = new List<BoneKey>();
    }

    /// <summary>
    /// One bone key (Amendment A42): a full local TRS sample for one bone at one point on a clip's
    /// timeline, applied during VAT baking exactly as an imported <c>AnimationClip</c>'s sampled
    /// pose would be (architecture section 4.7 posing step; the bake path itself is phase B2).
    /// </summary>
    [Serializable]
    public struct BoneKey
    {
        /// <summary>Key time as a fraction of the clip's duration, in [0, 1] (validation rule V04).</summary>
        public float normalizedTime;

        /// <summary>Local position offset from the bone's bind pose.</summary>
        public float3 localPosition;

        /// <summary>
        /// Local rotation, authored directly as a quaternion — unlike <see cref="TransformKey.rotationZ"/>,
        /// a joint orientation is not expressible on one axis, which is the entire reason this is a
        /// separate track kind (see <see cref="BoneTrack"/> remarks).
        /// </summary>
        public quaternion localRotation;

        /// <summary>Local non-uniform scale.</summary>
        public float3 localScale;

        /// <summary>Easing applied from this key to the next one.</summary>
        public Interpolation interpolation;

        /// <summary>
        /// The first Bézier handle, in segment space: x is time across the segment, y is the blend
        /// weight. Read only when <see cref="interpolation"/> is <see cref="Interpolation.Bezier"/>.
        /// </summary>
        /// <remarks>
        /// Both handles zero is the value a struct deserializes to when the fields did not exist,
        /// and a zero-length handle pair is not a curve. <c>ClipSampler.Ease</c> reads that exact
        /// case as linear rather than as a degenerate solve — the same defensive reading
        /// <see cref="BoneKey.localRotation"/> needs for an all-zero quaternion, and for the same
        /// reason: a struct's zero value is not always a meaningful value.
        /// </remarks>
        public float2 bezierStartHandle;

        /// <summary>The second Bézier handle. See <see cref="bezierStartHandle"/>.</summary>
        public float2 bezierEndHandle;
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

    /// <summary>
    /// Keyed billboard channels for one billboard root (amendment A44): how far the root turns off
    /// the camera, how much of the billboard applies at all, and whether it applies this frame.
    /// </summary>
    /// <remarks>
    /// <strong>Bound to the root's own id, not to the node it turns.</strong> A billboard root's
    /// address is editable — the same root may be re-pointed from the hips to the torso — and a
    /// track bound to the addressed node would silently orphan itself the moment that happened.
    /// </remarks>
    [Serializable]
    public sealed class BillboardTrack
    {
        /// <summary>
        /// Stable id of the <c>BillboardRootDefinition</c> this track animates (validation rule
        /// V24). Must name a billboard root the clip's rig declares.
        /// </summary>
        public uint rootStableId;

        /// <summary>Keys in strictly ascending <c>normalizedTime</c> order (validation rule V03).</summary>
        public List<BillboardKey> keys = new List<BillboardKey>();
    }

    /// <summary>One billboard key (amendment A44).</summary>
    [Serializable]
    public struct BillboardKey
    {
        /// <summary>Key time as a fraction of the clip's duration, in [0, 1] (validation rule V04).</summary>
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

        /// <summary>
        /// Whether the root billboards at all from this key onward.
        /// </summary>
        /// <remarks>
        /// <strong>Always held from its key, never eased</strong> — the rule amendment A43
        /// established for flipbook indices, and for the same reason: an enable flag is a discrete
        /// instruction that fires at a moment, not an approximation of anything between two moments.
        /// <see cref="interpolation"/> governs the two continuous channels only.
        /// </remarks>
        public bool enabled;

        /// <summary>Easing applied from this key to the next one, for the continuous channels.</summary>
        public Interpolation interpolation;

        /// <summary>First Bézier handle (time, weight); read only for <c>Interpolation.Bezier</c>.</summary>
        public float2 bezierStartHandle;

        /// <summary>Second Bézier handle (time, weight); read only for <c>Interpolation.Bezier</c>.</summary>
        public float2 bezierEndHandle;
    }
}
