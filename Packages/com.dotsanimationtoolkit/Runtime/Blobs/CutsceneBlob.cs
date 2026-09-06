// Copyright (c) 2026 Spencer Park. All rights reserved.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace DotsAnimationToolkit
{
    /// <summary>
    /// Baked form of a <c>CutsceneAsset</c>: slot clip blocks/keys, a camera lane, and an event
    /// lane, split into <see cref="segments"/> at hold points so the clock is (segmentIndex,
    /// timeInSegment). Carries no clip registry of its own — clips resolve against the bound actor's own <see cref="ClipRegistryBlob"/>.
    /// </summary>
    public struct CutsceneBlob
    {
        public int schemaVersion; // bumped on any layout change, stamped at bake

        public ulong cutsceneKey; // diagnostic only; nothing resolves a cutscene by it

        public BlobArray<CutsceneSlotMetaBlob> slots; // authored order; what a host's binding buffer must cover

        public BlobArray<CutsceneSegmentBlob> segments; // chronological order; always at least one element
    }

    /// <summary>One slot's identity as the runtime sees it: who it is and its kind, never what it's bound to (the host's job via <c>CutsceneActorBinding</c>).</summary>
    public struct CutsceneSlotMetaBlob
    {
        public uint slotId; // names a host's CutsceneActorBinding entry

        public CutsceneSlotKind kind; // rig-driven clip player vs bare transform target
    }

    /// <summary>
    /// One elastic-time segment: the clock runs for <see cref="duration"/> seconds, then — unless
    /// this is the final segment — pauses at <see cref="holdId"/> until the host releases it. Every
    /// per-slot/camera/event time inside is already rebased relative to the segment's own start.
    /// </summary>
    public struct CutsceneSegmentBlob
    {
        public float duration; // seconds before pausing at holdId, or (final segment) ending

        public FixedString64Bytes holdId; // empty for the final segment

        public BlobArray<CutsceneSlotSegmentBlob> slotTracks; // parallel to CutsceneBlob.slots

        public BlobArray<CutsceneCameraKeyBlob> cameraKeys;

        public BlobArray<float> cameraCutTimes; // segment-relative

        public BlobArray<CutsceneEventMarkerBlob> events; // segment-relative

        public bool autoReleaseWhenMarksReached; // always false for the final segment
    }

    /// <summary>One slot's baked timeline for one segment.</summary>
    public struct CutsceneSlotSegmentBlob
    {
        public BlobArray<CutsceneClipBlockBlob> clipBlocks; // empty for a Prop slot

        public BlobArray<CutsceneTransformKeyBlob> transformKeys; // root motion (Actor) or full authored motion (Prop)

        public BlobArray<CutsceneFacingKeyBlob> facingKeys; // empty for a Prop slot

        public BlobArray<CutscenePartTrackBlob> partTracks; // empty for a Prop slot

        public BlobArray<CutsceneAttachMarkerBlob> attachMarkers;

        public BlobArray<CutsceneMarkKeyBlob> markKeys;
    }

    /// <summary>One baked move-to mark, bucketed by the instant its order is issued.</summary>
    public struct CutsceneMarkKeyBlob
    {
        public float time; // segment-relative seconds

        public float3 position;

        public float facingRadians;

        public float toleranceMeters; // XZ distance that counts as arrived

        public float timeoutSeconds; // 0 waits forever; otherwise resolves by teleport after this long
    }

    /// <summary>One baked attach/detach moment, bucketed by its own instant like an event.</summary>
    public struct CutsceneAttachMarkerBlob
    {
        public float time; // segment-relative seconds

        public CutsceneAttachKind kind;

        public int hostSlotIndex; // index into CutsceneBlob.slots; -1 = host slot id didn't resolve, warned at bake and skipped at play

        public uint socketId; // 0 = the host's root

        public float3 localOffset; // socket space, or host-root space for a root attach

        public quaternion localRotation; // root-attach only

        public bool hideWhileAttached;

        public float3 detachImpulse; // detach only; host-space impulse handed on via CutsceneDetachSignal
    }

    /// <summary>One baked clip block: overlap with the previous block on the slot's flat lane is the crossfade window; blocks that merely touch are a hard cut.</summary>
    public struct CutsceneClipBlockBlob
    {
        public ulong clipId; // resolved against whichever ClipRegistryBlob the bound actor carries

        public float start; // segment-relative seconds

        public float duration;

        public bool loop;

        // Crossfade window from the previous block on the slot's flat (pre-segment-split) lane,
        // baked rather than derived at play time: a hold can split two overlapping blocks across
        // segments, and the incoming one is still its segment's first block despite having a real
        // predecessor to blend from. 0 for the slot's first block, or a touching/gapped predecessor.
        public float blendDuration;

        public float speed; // multiplied by the cutscene's own speed at Play; 0 = pre-schema-5 bake, see CutsceneBlockTiming.EffectiveBlockSpeed

        public float clipStartOffset; // seconds into the clip, issued as a SetTime after the Play

        public CutsceneDirectionVariantsBlob directionVariants; // hasVariants false when the block's clip isn't in the slot's direction set
    }

    /// <summary>One clip block's turn table: the five east-side clips its direction set authors, plus the direction counts the resolve chain needs.</summary>
    public struct CutsceneDirectionVariantsBlob
    {
        public bool hasVariants; // false when the block's clip isn't a member of the slot's direction set, or the slot has none

        public ulong south; // clip id for that facing; 0 = the set leaves it empty

        public ulong southEast; // see south

        public ulong east; // see south

        public ulong northEast; // see south

        public ulong north; // see south

        public AnimationDirections targetDirections; // the actor's own turn granularity, before the set's coverage folds it

        public AnimationDirections effectiveDirections; // what the set's filled slots actually cover
    }

    /// <summary>One baked transform key. Rotation is stored in radians (authoring is degrees, matching <c>TransformKeyBlob</c>'s convention).</summary>
    public struct CutsceneTransformKeyBlob
    {
        public float time; // segment-relative seconds

        public float3 position;

        public float3 rotation; // radians, Euler ZXY

        public float3 scale;

        public Interpolation interpolation;

        public float2 bezierStartHandle; // (time, weight); read only for Interpolation.Bezier

        public float2 bezierEndHandle; // see bezierStartHandle
    }

    /// <summary>One baked facing override key. The angle is stored in radians (authoring is degrees, 0-360).</summary>
    public struct CutsceneFacingKeyBlob
    {
        public float time; // segment-relative seconds

        public float angleRadians;
    }

    /// <summary>One baked per-part override track, addressed by tag at authoring time.</summary>
    public struct CutscenePartTrackBlob
    {
        public uint tagId; // diagnostics only; the runtime never looks it up again

        // -1 = the tag didn't resolve against the slot's rig at bake (skipped at play, never an
        // error). Resolved once at cutscene bake time; recasting a slot's rig at play time does
        // NOT re-resolve tags — that needs a rebake.
        public int targetIndex;

        public AnimatedChannels channels; // channels outside the mask fall through to the composited clip beneath

        public BlobArray<CutsceneTransformKeyBlob> keys; // sorted by time
    }

    /// <summary>One baked camera pose key.</summary>
    public struct CutsceneCameraKeyBlob
    {
        public float time; // segment-relative seconds

        public float3 position; // world space

        public float3 rotation; // world space, radians, Euler ZXY

        public float fieldOfView; // degrees, vertical

        public Interpolation interpolation;

        public float2 bezierStartHandle; // (time, weight); read only for Interpolation.Bezier

        public float2 bezierEndHandle; // see bezierStartHandle
    }

    /// <summary>One baked event marker, same vocabulary and payload shape as a clip's own <see cref="EventMarkerBlob"/>.</summary>
    public struct CutsceneEventMarkerBlob
    {
        public float time; // segment-relative seconds

        public uint eventKey; // same vocabulary as EventMarkerBlob.eventKey

        public int intParam;

        public float floatParam;

        public bool fireOnSkip; // authored on by default
    }
}
