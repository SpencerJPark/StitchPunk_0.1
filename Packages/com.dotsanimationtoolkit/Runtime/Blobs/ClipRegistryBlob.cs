// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Dense clip index = position in <see cref="clips"/> = position in <see cref="sortedClipIds"/>; both are id-sorted.
    /// </summary>
    public struct ClipRegistryBlob
    {
        public int schemaVersion; // bumped on any layout change, stamped at bake

        public ulong setKey; // dedup/diagnostic key only; nothing looks up a clip by it

        public ulong vatSetKey; // 0 = the set has no VAT clips

        public BlobArray<ulong> sortedClipIds; // ascending order; binary-search key array

        public BlobArray<ClipBlob> clips; // id-sorted; index cached by PlaybackLayer.clipIndex/previousClipIndex

        public BlobArray<uint> sortedTargetIds; // ascending order; dense index = position here, resolved once at bind time

        public BlobArray<float3> targetBoundsExtents; // per dense target index; authored conservative local half-extents

        public BlobArray<int> targetFramesPerVariant; // per dense target index; 1 = no variant blocks

        public VatTextureInfoBlob vatInfo; // mirrored from the linked texture set
    }

    /// <summary>
    /// One baked clip: identity, timing/blend defaults, track and event data, VAT frame range, and bounds.
    /// </summary>
    public struct ClipBlob
    {
        public ulong clipId; // ClipId raw value

        public FixedString64Bytes debugName; // logs/editor display only, never used for lookup

        public float duration; // seconds; guaranteed >= 0.001 by validation

        public LoopMode defaultLoop; // always resolved; never LoopMode.UseClipDefault

        public float defaultBlendIn; // seconds; clamped <= duration at bake; 0 = pop

        public float defaultBlendOut; // seconds; clamped <= duration at bake

        public BlobArray<TransformTrackBlob> transformTracks; // sorted by dense target index

        public BlobArray<SpriteTrackBlob> spriteTracks; // sorted by dense target index

        public BlobArray<EventMarkerBlob> events; // sorted by normalizedTime, stable tie-break

        public int vatFrameStart; // first global frame of the untargeted VAT range; -1 = no VAT range

        public int vatFrameCount; // frame count for the untargeted range; see vatFrameStart

        public float vatFps; // sample rate for the untargeted range

        // Checked first, by dense target index; falls back to vatFrameStart/vatFrameCount/vatFps
        // when no entry matches. Empty for clips baked before multi-source VAT tracks.
        public BlobArray<VatTrackRangeBlob> vatTargetRanges;

        public BlobArray<BillboardTrackBlob> billboardTracks; // sorted by ascending rootId; empty if the clip doesn't animate billboarding

        // Offset-space bounds, NOT actor space: origin-centered, built from rest-pose-relative
        // offsets. Actor-space bounds are assembled at bake/update by combining this with each
        // target's rest pose (see RenderBoundsUpdateSystem).
        public AABB offsetBounds;
    }

    /// <summary>One baked transform track: keyed TRS curves bound to a single dense target.</summary>
    public struct TransformTrackBlob
    {
        public int targetIndex; // position in ClipRegistryBlob.sortedTargetIds

        public TrackBlendOp blendOp;

        public AnimatedChannels channels;

        public BlobArray<TransformKeyBlob> keys; // sorted by time
    }

    /// <summary>One baked transform key. Rotation is stored in radians (authoring is degrees).</summary>
    public struct TransformKeyBlob
    {
        public float normalizedTime; // [0, 1]

        public float3 position; // local x/y offset; z is the 2.5D draw-layer order

        public float3 rotation; // radians, Euler angles in Unity's ZXY order

        public float3 scale; // negative components flip, applied via PostTransformMatrix

        public Interpolation interpolation;

        public float2 bezierStartHandle; // (time, weight); read only for Interpolation.Bezier

        public float2 bezierEndHandle; // (time, weight); read only for Interpolation.Bezier
    }

    /// <summary>One baked sprite track: keyed frame selection bound to a single dense target.</summary>
    public struct SpriteTrackBlob
    {
        public int targetIndex; // dense target index

        public SpriteFrameMode mode;

        public SpriteSliceSpace sliceSpace;

        // Kept as the authored base rather than folded into the keys, so the blob agrees with the
        // authored asset about what a key means.
        public int baseIndex;

        public BlobArray<SpriteKeyBlob> keys; // sorted by time
    }

    public struct SpriteKeyBlob
    {
        public float normalizedTime; // [0, 1]

        public int sliceIndex; // -1 = no change (Absolute mode); offset from baseIndex (RelativeToBase mode)

        public SpriteIndexMode indexMode; // how sliceIndex resolves; see SpriteIndexResolver

        public float4 atlasRect; // atlas-mode rect: scale.xy, offset.zw
    }

    /// <summary>One baked billboard track: keyed billboard channels bound to a single billboard root.</summary>
    public struct BillboardTrackBlob
    {
        // Keyed by the root's stable id, not a dense index: billboard roots have no dense array
        // the way rig targets do.
        public uint rootId;

        public BlobArray<BillboardKeyBlob> keys; // sorted by time
    }

    /// <summary>One baked billboard key. The angle is stored in radians (authoring is degrees).</summary>
    public struct BillboardKeyBlob
    {
        public float normalizedTime; // [0, 1]

        public float angleOffsetRadians; // rotation off resolved facing, about the frame's up axis

        public float blendWeight; // [0, 1]

        public bool enabled; // held from this key onward, never eased (discrete channel)

        public Interpolation interpolation; // continuous channels only

        public float2 bezierStartHandle; // (time, weight); read only for Interpolation.Bezier

        public float2 bezierEndHandle; // (time, weight); read only for Interpolation.Bezier
    }

    public struct EventMarkerBlob
    {
        public float normalizedTime; // [0, 1]

        public uint eventKey; // user keys >= 16; 0-15 reserved, see ReservedEventKeys

        public int intParam; // passed through to AnimEventOutput.intParam

        public float floatParam; // passed through to AnimEventOutput.floatParam

        public float windowSeconds; // seconds the event mask bit holds open; 0 = pulse-only
    }

    /// <summary>VAT texel-addressing parameters mirrored from the texture set. Textures are bound at the material level.</summary>
    public struct VatTextureInfoBlob
    {
        public VatFlavor flavor;

        public int textureWidth; // texels

        public int rowsPerFrame; // 1 for the bone flavor

        public int boneOrVertexCount;
    }

    /// <summary>
    /// One target-scoped VAT frame range, mirroring <c>VatClipRange</c> minus the clip id (already
    /// known from whichever <see cref="ClipBlob.vatTargetRanges"/> array this sits in).
    /// </summary>
    public struct VatTrackRangeBlob
    {
        public int targetIndex; // position in ClipRegistryBlob.sortedTargetIds

        public int frameStart; // first frame in the texture's global frame numbering

        public int frameCount; // includes the duplicated loop-safe frame

        public float fps;
    }
}
