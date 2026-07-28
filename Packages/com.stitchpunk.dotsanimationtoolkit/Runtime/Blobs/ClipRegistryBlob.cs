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
        /// contract. Its element order is independent of <see cref="clips"/>; the two are joined by
        /// <see cref="clipIndexById"/>.
        /// </summary>
        public BlobArray<ulong> sortedClipIds;

        /// <summary>
        /// Parallel to <see cref="sortedClipIds"/>: for the id at sorted position <c>i</c>, the
        /// dense index of that clip in <see cref="clips"/>. This array is the whole point of the
        /// indirection — the search runs over the sorted ids, the result addresses the dense array.
        /// </summary>
        public BlobArray<int> clipIndexById;

        /// <summary>
        /// The baked clips in authored/dense order — a clip's dense index is simply its position in
        /// this array, and that index is what every runtime field caches
        /// (<see cref="PlaybackLayer.clipIndex"/>, <see cref="PlaybackLayer.previousClipIndex"/>).
        /// Readers must not assume this array is ordered by <see cref="ClipBlob.clipId"/>:
        /// <see cref="sortedClipIds"/> plus <see cref="clipIndexById"/> are what provide the
        /// id → dense-index mapping (<c>ClipRegistryUtil.TryResolveClip</c>, section 4.3), and if
        /// the dense order were the id order that indirection would be an identity map and the
        /// binary search pointless.
        /// </summary>
        public BlobArray<ClipBlob> clips;

        /// <summary>
        /// All target ids in ascending order. A target's dense index is its position in this
        /// array — resolved once at bind/bake time, never per frame (section 4.3).
        /// </summary>
        public BlobArray<uint> sortedTargetIds;

        /// <summary>Per dense target index: the authored conservative local half-extents from <c>RigTargetDefinition</c>.</summary>
        public BlobArray<float3> targetBoundsExtents;

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

        /// <summary>First global frame index of this clip's VAT range, or −1 when the clip has no VAT range.</summary>
        public int vatFrameStart;

        /// <summary>Number of VAT frames baked for this clip (includes the duplicated loop-safe frame).</summary>
        public int vatFrameCount;

        /// <summary>Sample rate the clip's VAT frames were baked at.</summary>
        public float vatFps;

        /// <summary>
        /// Conservative actor-space bounds for this clip (section 4.6), as an
        /// <see cref="AABB"/> — <c>Center</c> plus half-extents in <c>Extents</c>. Consumed by
        /// <c>RenderBoundsUpdateSystem</c>, which unions these into the actor's
        /// <c>RenderBounds.Value</c> (section 5.8).
        /// </summary>
        public AABB localBounds;
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

        /// <summary>Rotation about z in radians.</summary>
        public float rotationZ;

        /// <summary>Non-uniform x/y scale; negative values flip (applied via <c>PostTransformMatrix</c>, section 5.6).</summary>
        public float2 scale;

        /// <summary>Easing from this key to the next one.</summary>
        public Interpolation interpolation;
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

        /// <summary>Keys sorted by time.</summary>
        public BlobArray<SpriteKeyBlob> keys;
    }

    /// <summary>One baked sprite key (architecture section 4.2).</summary>
    public struct SpriteKeyBlob
    {
        /// <summary>Key time normalized to the clip's duration, in [0, 1].</summary>
        public float normalizedTime;

        /// <summary>Slice-mode frame index; −1 = no change (keep the current frame).</summary>
        public int sliceIndex;

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
}
