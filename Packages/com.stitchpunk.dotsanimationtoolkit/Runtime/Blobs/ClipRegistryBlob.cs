// Copyright (c) 2026 Stitch Punk. All rights reserved.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace StitchPunk.AnimationToolkit
{
    /// <summary>
    /// The baked registry of one <c>ClipSetAsset</c> (architecture section 4.2): every clip, the
    /// rig's dense target order, and the VAT addressing metadata. Built once at bake time, owned by
    /// the BlobAssetStore, shared by every actor referencing the same set, and never manually
    /// disposed. Textures never live in blobs — only <see cref="vatSetKey"/> plus addressing
    /// metadata (section 4.4).
    /// </summary>
    public struct ClipRegistryBlob
    {
        /// <summary>Blob layout version; bumped on any layout change and stamped at bake.</summary>
        public int schemaVersion;

        /// <summary>Stable id of the source <c>ClipSetAsset</c>.</summary>
        public ulong setKey;

        /// <summary>Stable key of the linked <c>VatTextureSetAsset</c>, or 0 when the set has no VAT clips.</summary>
        public ulong vatSetKey;

        /// <summary>Number of playback layers the rig defines (1–8).</summary>
        public byte layerCount;

        /// <summary>
        /// All clip ids in ascending order — the binary-search key array of the section 4.3 lookup
        /// contract. Element <c>i</c> is the id of <see cref="clips"/>[<c>i</c>], because both
        /// arrays are written in the same ascending-clip-id order (section 4.5.1).
        /// </summary>
        public BlobArray<ulong> sortedClipIds;

        /// <summary>
        /// The baked clips, sorted by ascending <see cref="ClipBlob.clipId"/> — the canonical order
        /// of section 4.5.1. A clip's dense index is its position in this array, and because the
        /// array is id-sorted that position is also its position in <see cref="sortedClipIds"/>, so
        /// the binary search of <c>ClipRegistryUtil.TryResolveClip</c> (section 4.3) yields the
        /// dense index directly and no id → index indirection array is stored. The dense index is
        /// what every runtime field caches (<see cref="PlaybackLayer.clipIndex"/>,
        /// <see cref="PlaybackLayer.previousClipIndex"/>).
        /// </summary>
        public BlobArray<ClipBlob> clips;

        /// <summary>
        /// All target ids in ascending order. A target's dense index is its position in this
        /// array — resolved once at bind/bake time, never per frame (section 4.3).
        /// </summary>
        public BlobArray<uint> sortedTargetIds;

        /// <summary>Per dense target index: the authored conservative local half-extents from <c>RigTargetDefinition</c>.</summary>
        public BlobArray<float3> targetBoundsExtents;

        /// <summary>
        /// Per dense target index: how many consecutive frames one variant owns in that target's
        /// texture array (amendment A37). 1 = no variant blocks.
        /// </summary>
        public BlobArray<int> targetFramesPerVariant;

        /// <summary>VAT texel-addressing parameters mirrored from the texture set (section 4.4).</summary>
        public VatTextureInfoBlob vatInfo;
    }

    /// <summary>
    /// One baked clip (architecture section 4.2): identity, timing/blend defaults, canonical-order
    /// track and event data, VAT frame range, and conservative bounds.
    /// </summary>
    public struct ClipBlob
    {
        /// <summary>The clip's stable 64-bit id (<see cref="ClipId"/> raw value).</summary>
        public ulong clipId;

        /// <summary>Human-readable clip name for logs and editor display; never used for lookup.</summary>
        public FixedString64Bytes debugName;

        /// <summary>Clip length in seconds; validation guarantees ≥ 0.001 (rule V01).</summary>
        public float duration;

        /// <summary>The clip's authored default loop mode (always resolved — never <see cref="LoopMode.UseClipDefault"/>).</summary>
        public LoopMode defaultLoop;

        /// <summary>Default crossfade-in seconds; clamped ≤ <see cref="duration"/> at bake. 0 = pop.</summary>
        public float defaultBlendIn;

        /// <summary>Default fade-out seconds; clamped ≤ <see cref="duration"/> at bake.</summary>
        public float defaultBlendOut;

        /// <summary>Transform tracks, sorted by dense target index (canonical order, section 4.5).</summary>
        public BlobArray<TransformTrackBlob> transformTracks;

        /// <summary>Sprite tracks, sorted by dense target index (canonical order, section 4.5).</summary>
        public BlobArray<SpriteTrackBlob> spriteTracks;

        /// <summary>Event markers sorted by <c>normalizedTime</c> with stable original-order tie-break.</summary>
        public BlobArray<EventMarkerBlob> events;

        /// <summary>
        /// First global frame index of this clip's <em>untargeted</em> VAT range, or −1 when the
        /// clip has no VAT range at all. Baked from <c>ClipAsset.vatSource</c> — the range a VAT
        /// part resolves when <see cref="vatTargetRanges"/> holds no entry for its target index.
        /// </summary>
        public int vatFrameStart;

        /// <summary>
        /// Number of VAT frames baked for the untargeted range (includes the duplicated loop-safe
        /// frame). See <see cref="vatFrameStart"/>.
        /// </summary>
        public int vatFrameCount;

        /// <summary>Sample rate the untargeted VAT range was baked at. See <see cref="vatFrameStart"/>.</summary>
        public float vatFps;

        /// <summary>
        /// Per-target VAT frame range overrides, baked from <c>ClipAsset.vatTracks</c> (C10). Sorted
        /// by ascending <c>targetIndex</c> (canonical order, architecture section 4.5); empty for
        /// every clip that predates multi-source VAT tracks or simply does not use them.
        /// </summary>
        /// <remarks>
        /// A VAT part resolves its frame range by first scanning this array for its own dense target
        /// index and, only when no entry matches, falling back to
        /// <see cref="vatFrameStart"/>/<see cref="vatFrameCount"/>/<see cref="vatFps"/> — the same
        /// two-step rule <c>VatTextureSetAsset.TryGetTrackRange</c> uses at bake time, so a part with
        /// no dedicated track keeps resolving the clip's shared range exactly as it did before this
        /// array existed. See <c>VatMaterialSystem.WriteVatPropertiesJob.TryResolveGlobalFrame</c>.
        /// </remarks>
        public BlobArray<VatTrackRangeBlob> vatTargetRanges;

        /// <summary>
        /// Conservative bounds for this clip in <em>offset space</em> (section 4.6), as an
        /// <see cref="AABB"/> — <c>Center</c> plus half-extents in <c>Extents</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This box is <strong>not</strong> actor space. It is built from
        /// <see cref="TransformKeyBlob.position"/> values, which section 3.2 defines as local
        /// offsets <em>from a target's rest pose</em>, and from each target's authored half-extents
        /// centred on the origin. The rest poses themselves live on the actor prefab and are
        /// captured into <c>TargetRestPose</c> at entity bake time; <c>ClipRegistryBuilder</c> sees
        /// only a <c>ClipSetAsset</c> graph and can never read them, so every box it produces is
        /// origin-centred by construction.
        /// </para>
        /// <para>
        /// The actor-space box is therefore assembled at entity bake time, where the rest poses are
        /// available: the actor baker combines each part's rest pose with this offset box to produce
        /// the actor-level <c>ActorRestBounds</c> (section 5.2), and section 5.8's
        /// <c>RenderBoundsUpdateSystem</c> unions <c>ActorRestBounds</c> with the offset boxes of
        /// the clips currently referenced before writing <c>RenderBounds.Value</c>. Treating this
        /// field as actor space on its own under-reports the silhouette of any rig whose parts sit
        /// away from the actor origin.
        /// </para>
        /// </remarks>
        public AABB offsetBounds;
    }

    /// <summary>
    /// One baked transform track: keyed TRS curves bound to a single dense target
    /// (architecture section 4.2).
    /// </summary>
    public struct TransformTrackBlob
    {
        /// <summary>Dense target index the track animates (position in <see cref="ClipRegistryBlob.sortedTargetIds"/>).</summary>
        public int targetIndex;

        /// <summary>How the track combines with the pose composited so far.</summary>
        public TrackBlendOp blendOp;

        /// <summary>The pose channels this track animates.</summary>
        public AnimatedChannels channels;

        /// <summary>Keys sorted by time; interpolation resolved per key at bake.</summary>
        public BlobArray<TransformKeyBlob> keys;
    }

    /// <summary>
    /// One baked transform key (architecture section 4.2). Rotation is stored in radians
    /// (converted at bake; authoring is degrees).
    /// </summary>
    public struct TransformKeyBlob
    {
        /// <summary>Key time normalized to the clip's duration, in [0, 1].</summary>
        public float normalizedTime;

        /// <summary>Local x/y offset; z is the 2.5D draw-layer order.</summary>
        public float3 position;

        /// <summary>Local rotation in radians, as Euler angles in Unity's ZXY order.</summary>
        public float3 rotation;

        /// <summary>Non-uniform x/y/z scale; negative components flip (applied via <c>PostTransformMatrix</c>, section 5.6).</summary>
        public float3 scale;

        /// <summary>Easing from this key to the next one.</summary>
        public Interpolation interpolation;

        /// <summary>First Bézier handle (time, weight); read only for <see cref="Interpolation.Bezier"/>.</summary>
        public float2 bezierStartHandle;

        /// <summary>Second Bézier handle (time, weight); read only for <see cref="Interpolation.Bezier"/>.</summary>
        public float2 bezierEndHandle;
    }

    /// <summary>
    /// One baked sprite track: keyed frame selection bound to a single dense target
    /// (architecture section 4.2).
    /// </summary>
    public struct SpriteTrackBlob
    {
        /// <summary>Dense target index the track animates.</summary>
        public int targetIndex;

        /// <summary>Whether keys address Texture2DArray slices or atlas rects.</summary>
        public SpriteFrameMode mode;

        /// <summary>
        /// Whether slice keys are absolute frames or offsets from the rest slice (amendment A37).
        /// </summary>
        public SpriteSliceSpace sliceSpace;

        /// <summary>
        /// The index <see cref="SpriteIndexMode.RelativeToBase"/> keys offset from.
        /// </summary>
        /// <remarks>
        /// Baked as the authored base rather than folded into the keys, so the blob keeps the same
        /// offsets the asset holds. Folding would cost nothing at runtime and lose nothing that
        /// runtime reads — but it would make the baked data disagree with the authored data about
        /// what a key means, and every tool that reads a blob back would report resolved indices an
        /// author never typed.
        /// </remarks>
        public int baseIndex;

        /// <summary>Keys sorted by time.</summary>
        public BlobArray<SpriteKeyBlob> keys;
    }

    /// <summary>One baked sprite key (architecture section 4.2).</summary>
    public struct SpriteKeyBlob
    {
        /// <summary>Key time normalized to the clip's duration, in [0, 1].</summary>
        public float normalizedTime;

        /// <summary>
        /// The key's stored number: a slice index in <see cref="SpriteIndexMode.Absolute"/> (−1 =
        /// no change), or an offset from the track's <c>baseIndex</c> in
        /// <see cref="SpriteIndexMode.RelativeToBase"/>.
        /// </summary>
        public int sliceIndex;

        /// <summary>How <see cref="sliceIndex"/> resolves. See <c>SpriteIndexResolver</c>.</summary>
        public SpriteIndexMode indexMode;

        /// <summary>Atlas-mode rect: scale.xy, offset.zw.</summary>
        public float4 atlasRect;
    }

    /// <summary>One baked event marker (architecture section 4.2).</summary>
    public struct EventMarkerBlob
    {
        /// <summary>Marker time normalized to the clip's duration, in [0, 1].</summary>
        public float normalizedTime;

        /// <summary>Typed event key; user keys ≥ 16, 0–15 reserved (<see cref="ReservedEventKeys"/>, rule V09).</summary>
        public uint eventKey;

        /// <summary>User integer payload passed through to <see cref="AnimEventOutput.intParam"/>.</summary>
        public int intParam;

        /// <summary>User float payload passed through to <see cref="AnimEventOutput.floatParam"/>.</summary>
        public float floatParam;
    }

    /// <summary>
    /// VAT texel-addressing parameters mirrored from the texture set into the registry blob
    /// (architecture sections 4.2, 4.4). Textures themselves are bound at the material level.
    /// </summary>
    public struct VatTextureInfoBlob
    {
        /// <summary>Which VAT encoding the linked texture set carries.</summary>
        public VatFlavor flavor;

        /// <summary>Texture width in texels.</summary>
        public int textureWidth;

        /// <summary>Texture rows per animation frame; 1 for the bone flavor.</summary>
        public int rowsPerFrame;

        /// <summary>Bone count (bone flavor) or vertex count (vertex flavor).</summary>
        public int boneOrVertexCount;
    }

    /// <summary>
    /// One target-scoped VAT frame range baked from a <c>ClipAsset.vatTracks</c> entry (C10),
    /// mirroring <c>VatClipRange</c> minus the clip id — the clip is already known from whichever
    /// <see cref="ClipBlob.vatTargetRanges"/> array this element sits in.
    /// </summary>
    public struct VatTrackRangeBlob
    {
        /// <summary>
        /// Dense target index this range overrides (position in
        /// <see cref="ClipRegistryBlob.sortedTargetIds"/>), matching <see cref="RigPartBinding.targetIndex"/>
        /// of the VAT part it drives.
        /// </summary>
        public int targetIndex;

        /// <summary>Index of this range's first frame in the texture's global frame numbering.</summary>
        public int frameStart;

        /// <summary>Number of frames this range occupies, including the duplicated loop-safe frame.</summary>
        public int frameCount;

        /// <summary>The rate these frames were sampled at.</summary>
        public float fps;
    }
}
