// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// The baked form of a <c>CutsceneAsset</c> (Phase G §5): every slot's clip blocks and keys, a
    /// camera lane, and an event lane, split into <see cref="segments"/> at hold points so the
    /// runtime clock is <c>(segmentIndex, timeInSegment)</c> rather than one elastic time value —
    /// the containment that keeps a hold from infecting every "time → what's playing" lookup (spec
    /// §5, §9 risk). Built by <c>CutsceneBlobBuilder</c>, beside <c>ClipRegistryBuilder</c>.
    /// </summary>
    /// <remarks>
    /// Carries no clip registry of its own: a clip block's <c>clipId</c> resolves at play time
    /// against whichever <see cref="ClipRegistryBlob"/> the bound actor entity already carries from
    /// its own actor bake (spec §5 — "a cutscene rides the same registry blobs the actors already
    /// use"). This is what keeps the player a consumer of the existing playback machinery rather
    /// than a second animation pipeline (spec §6).
    /// </remarks>
    public struct CutsceneBlob
    {
        /// <summary>Blob layout version; bumped on any layout change and stamped at bake.</summary>
        public int schemaVersion;

        /// <summary>The source <c>CutsceneAsset</c>'s stable id. Diagnostic only — nothing resolves a cutscene by it.</summary>
        public ulong cutsceneKey;

        /// <summary>Every slot this cutscene stages, in authored order — what a host's binding buffer (spec §6) must cover.</summary>
        public BlobArray<CutsceneSlotMetaBlob> slots;

        /// <summary>The timeline split at hold points, in chronological order. Always at least one element, even for a cutscene with no holds at all.</summary>
        public BlobArray<CutsceneSegmentBlob> segments;
    }

    /// <summary>One slot's identity as the runtime sees it (Phase G §3, §6): who it is and what kind of thing it is, never what it is bound to — that is the host's job via <c>CutsceneActorBinding</c>.</summary>
    public struct CutsceneSlotMetaBlob
    {
        /// <summary>Stable id a host's binding buffer entry names (<c>CutsceneSlot.SlotId</c>).</summary>
        public uint slotId;

        /// <summary>Whether this slot plays clips on a rig or is a bare transform target.</summary>
        public CutsceneSlotKind kind;
    }

    /// <summary>
    /// One elastic-time segment (Phase G §5): the clock runs for <see cref="duration"/> seconds,
    /// then — unless this is the final segment — pauses at <see cref="holdId"/> until the host
    /// releases it. Every per-slot/camera/event time inside a segment is already rebased to be
    /// relative to the segment's own start, so nothing downstream ever subtracts a hold boundary.
    /// </summary>
    public struct CutsceneSegmentBlob
    {
        /// <summary>How long this segment plays before it either pauses at <see cref="holdId"/> or (the final segment) simply ends.</summary>
        public float duration;

        /// <summary>The hold this segment ends on, or empty for the final segment — nothing pauses after the cutscene's own last moment.</summary>
        public FixedString64Bytes holdId;

        /// <summary>Per-slot clip blocks and keys, parallel to <see cref="CutsceneBlob.slots"/>.</summary>
        public BlobArray<CutsceneSlotSegmentBlob> slotTracks;

        /// <summary>The camera's keyed pose/FOV curve within this segment.</summary>
        public BlobArray<CutsceneCameraKeyBlob> cameraKeys;

        /// <summary>Camera hard-cut times within this segment, segment-relative.</summary>
        public BlobArray<float> cameraCutTimes;

        /// <summary>Event markers within this segment, segment-relative.</summary>
        public BlobArray<CutsceneEventMarkerBlob> events;
    }

    /// <summary>One slot's baked timeline for one segment (Phase G §2).</summary>
    public struct CutsceneSlotSegmentBlob
    {
        /// <summary>Which clip plays when, within this segment. Empty for a Prop slot.</summary>
        public BlobArray<CutsceneClipBlockBlob> clipBlocks;

        /// <summary>The slot's own transform through the segment: root motion for an Actor, or the whole authored motion for a Prop.</summary>
        public BlobArray<CutsceneTransformKeyBlob> transformKeys;

        /// <summary>Facing override keys within this segment. Empty for a Prop slot.</summary>
        public BlobArray<CutsceneFacingKeyBlob> facingKeys;

        /// <summary>Tag-addressed per-part override tracks within this segment. Empty for a Prop slot.</summary>
        public BlobArray<CutscenePartTrackBlob> partTracks;
    }

    /// <summary>One baked clip block (Phase G §2): overlap with the previous block on the slot's flat lane is the crossfade window; blocks that merely touch are a hard cut.</summary>
    public struct CutsceneClipBlockBlob
    {
        /// <summary>The clip's stable id, resolved against whichever <see cref="ClipRegistryBlob"/> the bound actor carries.</summary>
        public ulong clipId;

        /// <summary>Block start, segment-relative seconds.</summary>
        public float start;

        /// <summary>Block length in seconds.</summary>
        public float duration;

        /// <summary>Whether the clip loops for the block's duration rather than playing once.</summary>
        public bool loop;

        /// <summary>
        /// The crossfade window from the block immediately before this one on the slot's flat
        /// (pre-segment-split) clip lane, baked at bake time (amendment A62 defect 3, decision
        /// A62-D3) rather than derived at play time from "the previous block in this segment" — a
        /// hold can fall between two overlapping blocks, and the incoming one is still the first
        /// block of its own segment even though it has a real predecessor to blend from. 0 for the
        /// slot's first block on the flat lane, or when the previous block only touches or leaves a
        /// gap (a hard cut).
        /// </summary>
        public float blendDuration;
    }

    /// <summary>One baked transform key (Phase G §2). Rotation is stored in radians (converted at bake; authoring is degrees, matching <c>TransformKeyBlob</c>'s own convention).</summary>
    public struct CutsceneTransformKeyBlob
    {
        /// <summary>Key time, segment-relative seconds.</summary>
        public float time;

        /// <summary>Local offset.</summary>
        public float3 position;

        /// <summary>Local rotation in radians, Euler ZXY.</summary>
        public float3 rotation;

        /// <summary>Non-uniform x/y/z scale.</summary>
        public float3 scale;

        /// <summary>Easing from this key to the next one.</summary>
        public Interpolation interpolation;

        /// <summary>First Bézier handle (time, weight); read only for <see cref="Interpolation.Bezier"/>.</summary>
        public float2 bezierStartHandle;

        /// <summary>Second Bézier handle. See <see cref="bezierStartHandle"/>.</summary>
        public float2 bezierEndHandle;
    }

    /// <summary>One baked facing override key (Phase G §2, decision G-D3). The angle is stored in radians (converted at bake; authoring is degrees, 0–360).</summary>
    public struct CutsceneFacingKeyBlob
    {
        /// <summary>Key time, segment-relative seconds.</summary>
        public float time;

        /// <summary>The facing angle in radians.</summary>
        public float angleRadians;
    }

    /// <summary>One baked per-part override track (Phase G §2), addressed by tag at authoring time.</summary>
    /// <remarks>
    /// <strong>Decision G-D9: resolved to a dense target index at cutscene bake time, not looked up
    /// live at play time.</strong> The runtime has nothing to look a tag up against —
    /// <see cref="ClipRegistryBlob"/> resolves a tag-bound clip track down to a dense index during
    /// the clip's own bake and carries no tag map of its own, so there is no existing baked
    /// structure a player could query at play time. <see cref="targetIndex"/> is therefore resolved
    /// here, against the slot's rig, using the exact same canonical (ascending stable id) ordering
    /// <c>ClipRegistryBuilder</c> uses — the same rig always yields the same ordering regardless of
    /// which builder computes it, so this index agrees with whatever dense index the bound actor's
    /// own <see cref="RigPartRef"/> buffer resolves to. <strong>The cost:</strong> a slot recast to a
    /// different rig at play time (spec §3's "the same cutscene can be recast") does not re-resolve
    /// tags against the new rig; only the Scene-view editor preview (G3) does that live. Recasting a
    /// slot's rig for the runtime player needs a rebake until a follow-up amendment gives the
    /// runtime registry its own tag map.
    /// </remarks>
    public struct CutscenePartTrackBlob
    {
        /// <summary>The authored role, kept for diagnostics — the runtime never looks it up again.</summary>
        public uint tagId;

        /// <summary>
        /// Dense target index resolved at bake (matches <see cref="RigPartRef.targetIndex"/> for the
        /// bound actor's own rig), or −1 when the tag did not resolve against the slot's rig at bake
        /// time — skipped at play time, never an error (rule T2).
        /// </summary>
        public int targetIndex;

        /// <summary>Which pose channels this track owns; channels outside the mask are left to the composited clip beneath it.</summary>
        public AnimatedChannels channels;

        /// <summary>Keys sorted by <see cref="CutsceneTransformKeyBlob.time"/>.</summary>
        public BlobArray<CutsceneTransformKeyBlob> keys;
    }

    /// <summary>One baked camera pose key (Phase G §2, §4).</summary>
    public struct CutsceneCameraKeyBlob
    {
        /// <summary>Key time, segment-relative seconds.</summary>
        public float time;

        /// <summary>World-space position.</summary>
        public float3 position;

        /// <summary>World-space rotation in radians, Euler ZXY.</summary>
        public float3 rotation;

        /// <summary>Vertical field of view in degrees.</summary>
        public float fieldOfView;

        /// <summary>Easing from this key to the next one.</summary>
        public Interpolation interpolation;

        /// <summary>First Bézier handle (time, weight); read only for <see cref="Interpolation.Bezier"/>.</summary>
        public float2 bezierStartHandle;

        /// <summary>Second Bézier handle. See <see cref="bezierStartHandle"/>.</summary>
        public float2 bezierEndHandle;
    }

    /// <summary>One baked event marker (Phase G §2, decision G-D4), same vocabulary and payload shape as a clip's own <see cref="EventMarkerBlob"/>.</summary>
    public struct CutsceneEventMarkerBlob
    {
        /// <summary>Marker time, segment-relative seconds.</summary>
        public float time;

        /// <summary>User event key, same vocabulary as <see cref="EventMarkerBlob.eventKey"/>.</summary>
        public uint eventKey;

        /// <summary>User integer payload.</summary>
        public int intParam;

        /// <summary>User float payload.</summary>
        public float floatParam;

        /// <summary>Whether a skip still fires this event (decision G-D4; default authored on).</summary>
        public bool fireOnSkip;
    }
}
