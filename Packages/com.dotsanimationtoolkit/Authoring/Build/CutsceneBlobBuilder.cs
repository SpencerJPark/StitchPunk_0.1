// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace DotsAnimationToolkit.Authoring
{
    /// <summary>
    /// Turns a <see cref="CutsceneAsset"/> into the single <see cref="CutsceneBlob"/> the runtime
    /// player reads (Phase G §5), beside <see cref="ClipRegistryBuilder"/>. Splits the authored
    /// timeline into segments at hold points and validates clip/tag references the same
    /// lenient-warn way rules T2/T6 already do for clips (spec §5, decision G-D8 below).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>A clip block is assigned to exactly one segment and never clipped across a hold
    /// (decision G-D8).</strong> The segment split exists to make elastic time containable for
    /// lookups (spec §5, §9), not to describe playback itself: once the player issues a block's
    /// Play/Queue command at its start time, the clip keeps running through the existing
    /// <c>PlaybackLayer</c>/<c>ClipSampler</c> machinery exactly as any other clip does, whether or
    /// not a hold happens to land inside its span. Clipping a looping block at every segment
    /// boundary and re-describing the remainder would restart its loop phase at each hold release —
    /// exactly the "pop back to frame 0" spec §2 rules out with "looping clips keep cycling." Every
    /// other lane item (a key, a cut marker, an event) is a single instant and carries no such
    /// concern, so all of them — blocks included — are assigned to the one segment window
    /// containing their own defining time, by the same half-open-interval rule
    /// <see cref="AssignToSegment"/> applies uniformly.
    /// </para>
    /// <para>
    /// <strong>No BlobAssetStore dedup, unlike <see cref="ClipRegistryBuilder"/>.</strong> That
    /// type dedups because many actors can share one (rig, clip-sets) bind; a cutscene has no such
    /// multi-instance fan-out — one <see cref="CutsceneAsset"/> bakes to one blob, and a caller
    /// wanting to reuse it across concurrent plays of the same cutscene can cache the reference
    /// itself (G6's concern, not this builder's).
    /// </para>
    /// </remarks>
    public static class CutsceneBlobBuilder
    {
        /// <summary>Blob layout version; bumped on any layout change and stamped at bake.</summary>
        public const int SchemaVersion = 1;

        private const float BoundaryEpsilon = 1e-5f;

        /// <summary>
        /// Builds the blob for one cutscene.
        /// </summary>
        /// <param name="cutscene">The source asset. Must not be null.</param>
        /// <param name="blob">The built blob, allocated with <see cref="Allocator.Persistent"/>. Ownership passes to the caller.</param>
        /// <param name="validationWarnings">
        /// Appended with one message per unresolved clip id or tag id found while baking (rules
        /// T2/T6's lenient philosophy — reported, never a thrown error). Pass a fresh list to
        /// collect them, or null to discard.
        /// </param>
        public static void Build(
            CutsceneAsset cutscene,
            out BlobAssetReference<CutsceneBlob> blob,
            List<string> validationWarnings)
        {
            if (cutscene == null)
            {
                throw new ArgumentNullException(nameof(cutscene));
            }

            List<string> warnings = validationWarnings ?? new List<string>();
            List<SegmentBoundary> boundaries = ComputeSegmentBoundaries(cutscene);

            BlobBuilder builder = new BlobBuilder(Allocator.Temp);
            try
            {
                ref CutsceneBlob root = ref builder.ConstructRoot<CutsceneBlob>();
                root.schemaVersion = SchemaVersion;
                root.cutsceneKey = cutscene.StableId;

                FillSlotMeta(ref builder, ref root, cutscene);
                FillSegments(ref builder, ref root, cutscene, boundaries, warnings);

                blob = builder.CreateBlobAssetReference<CutsceneBlob>(Allocator.Persistent);
            }
            finally
            {
                builder.Dispose();
            }

            for (int warningIndex = 0; warningIndex < warnings.Count; warningIndex++)
            {
                Debug.LogWarning("[DOTS Animation Toolkit] " + warnings[warningIndex]);
            }
        }

        // -----------------------------------------------------------------------------------
        // Segment boundaries.
        // -----------------------------------------------------------------------------------

        private struct SegmentBoundary
        {
            public float time;
            public string holdId;
        }

        /// <summary>
        /// Boundary <c>i</c> opens segment <c>i</c>; boundary <c>i + 1</c>'s <c>holdId</c> is what
        /// segment <c>i</c> pauses on (empty for the final, synthetic end-of-content boundary).
        /// </summary>
        private static List<SegmentBoundary> ComputeSegmentBoundaries(CutsceneAsset cutscene)
        {
            List<SegmentBoundary> boundaries = new List<SegmentBoundary> { new SegmentBoundary { time = 0f, holdId = null } };

            if (cutscene.holdMarkers != null)
            {
                List<CutsceneHoldMarker> sortedHolds = new List<CutsceneHoldMarker>(cutscene.holdMarkers);
                sortedHolds.Sort((left, right) => left.time.CompareTo(right.time));
                for (int i = 0; i < sortedHolds.Count; i++)
                {
                    boundaries.Add(new SegmentBoundary
                    {
                        time = Mathf.Max(0f, sortedHolds[i].time),
                        holdId = sortedHolds[i].holdId ?? string.Empty
                    });
                }
            }

            float naturalEnd = ComputeContentEndSeconds(cutscene);
            float lastBoundaryTime = boundaries[boundaries.Count - 1].time;
            if (naturalEnd > lastBoundaryTime + BoundaryEpsilon || boundaries.Count == 1)
            {
                boundaries.Add(new SegmentBoundary { time = Mathf.Max(naturalEnd, lastBoundaryTime), holdId = null });
            }

            return boundaries;
        }

        private static float ComputeContentEndSeconds(CutsceneAsset cutscene)
        {
            float latest = 0f;
            if (cutscene.slots != null)
            {
                for (int slotIndex = 0; slotIndex < cutscene.slots.Count; slotIndex++)
                {
                    CutsceneSlot slot = cutscene.slots[slotIndex];
                    if (slot == null)
                    {
                        continue;
                    }
                    if (slot.clipBlocks != null)
                    {
                        for (int i = 0; i < slot.clipBlocks.Count; i++)
                        {
                            latest = Mathf.Max(latest, slot.clipBlocks[i].start + slot.clipBlocks[i].duration);
                        }
                    }
                    latest = Mathf.Max(latest, LatestTime(slot.transformKeys));
                    latest = Mathf.Max(latest, LatestFacingTime(slot.facingKeys));
                    if (slot.partTracks != null)
                    {
                        for (int i = 0; i < slot.partTracks.Count; i++)
                        {
                            latest = Mathf.Max(latest, LatestTime(slot.partTracks[i].keys));
                        }
                    }
                }
            }
            latest = Mathf.Max(latest, LatestCameraTime(cutscene.cameraLane?.keys));
            if (cutscene.cameraLane?.cutMarkers != null)
            {
                for (int i = 0; i < cutscene.cameraLane.cutMarkers.Count; i++)
                {
                    latest = Mathf.Max(latest, cutscene.cameraLane.cutMarkers[i].time);
                }
            }
            if (cutscene.events != null)
            {
                for (int i = 0; i < cutscene.events.Count; i++)
                {
                    latest = Mathf.Max(latest, cutscene.events[i].time);
                }
            }
            return latest;
        }

        private static float LatestTime(List<CutsceneTransformKey> keys)
        {
            float latest = 0f;
            if (keys != null)
            {
                for (int i = 0; i < keys.Count; i++)
                {
                    latest = Mathf.Max(latest, keys[i].time);
                }
            }
            return latest;
        }

        private static float LatestFacingTime(List<CutsceneFacingKey> keys)
        {
            float latest = 0f;
            if (keys != null)
            {
                for (int i = 0; i < keys.Count; i++)
                {
                    latest = Mathf.Max(latest, keys[i].time);
                }
            }
            return latest;
        }

        private static float LatestCameraTime(List<CutsceneCameraKey> keys)
        {
            float latest = 0f;
            if (keys != null)
            {
                for (int i = 0; i < keys.Count; i++)
                {
                    latest = Mathf.Max(latest, keys[i].time);
                }
            }
            return latest;
        }

        /// <summary>
        /// The index of the segment window <paramref name="time"/> falls into, and that window's
        /// own start — every segment is half-open <c>[start, end)</c> except the final one, which is
        /// closed at both ends so a moment authored exactly at the cutscene's own last instant still
        /// belongs somewhere.
        /// </summary>
        private static int AssignToSegment(List<SegmentBoundary> boundaries, float time, out float segmentStart)
        {
            int segmentCount = boundaries.Count - 1;
            for (int segmentIndex = 0; segmentIndex < segmentCount; segmentIndex++)
            {
                float windowStart = boundaries[segmentIndex].time;
                float windowEnd = boundaries[segmentIndex + 1].time;
                bool isLastSegment = segmentIndex == segmentCount - 1;
                if (time >= windowStart && (time < windowEnd || (isLastSegment && time <= windowEnd)))
                {
                    segmentStart = windowStart;
                    return segmentIndex;
                }
            }
            // Defensive fallback for a time before 0 or past a degenerate empty timeline: the last segment.
            segmentStart = boundaries[segmentCount - 1].time;
            return segmentCount - 1;
        }

        // -----------------------------------------------------------------------------------
        // Blob assembly.
        // -----------------------------------------------------------------------------------

        private static void FillSlotMeta(ref BlobBuilder builder, ref CutsceneBlob root, CutsceneAsset cutscene)
        {
            int slotCount = cutscene.slots?.Count ?? 0;
            BlobBuilderArray<CutsceneSlotMetaBlob> slotArray = builder.Allocate(ref root.slots, slotCount);
            for (int i = 0; i < slotCount; i++)
            {
                slotArray[i] = new CutsceneSlotMetaBlob
                {
                    slotId = cutscene.slots[i].SlotId,
                    kind = cutscene.slots[i].kind
                };
            }
        }

        private static void FillSegments(
            ref BlobBuilder builder, ref CutsceneBlob root, CutsceneAsset cutscene,
            List<SegmentBoundary> boundaries, List<string> warnings)
        {
            int segmentCount = boundaries.Count - 1;
            BlobBuilderArray<CutsceneSegmentBlob> segmentArray = builder.Allocate(ref root.segments, segmentCount);

            // Bucket every lane item into its segment once, up front, rather than re-scanning every
            // authoring list per segment — the same "compute once, index everywhere" shape the rest
            // of this package's bake code uses.
            List<CutsceneClipBlockBlob>[,] clipBlocksBySlotSegment = new List<CutsceneClipBlockBlob>[cutscene.slots?.Count ?? 0, segmentCount];
            List<CutsceneTransformKeyBlob>[,] transformKeysBySlotSegment = new List<CutsceneTransformKeyBlob>[cutscene.slots?.Count ?? 0, segmentCount];
            List<CutsceneFacingKeyBlob>[,] facingKeysBySlotSegment = new List<CutsceneFacingKeyBlob>[cutscene.slots?.Count ?? 0, segmentCount];
            List<PartTrackBucket>[,] partTracksBySlotSegment = new List<PartTrackBucket>[cutscene.slots?.Count ?? 0, segmentCount];

            for (int slotIndex = 0; slotIndex < (cutscene.slots?.Count ?? 0); slotIndex++)
            {
                for (int segmentIndex = 0; segmentIndex < segmentCount; segmentIndex++)
                {
                    clipBlocksBySlotSegment[slotIndex, segmentIndex] = new List<CutsceneClipBlockBlob>();
                    transformKeysBySlotSegment[slotIndex, segmentIndex] = new List<CutsceneTransformKeyBlob>();
                    facingKeysBySlotSegment[slotIndex, segmentIndex] = new List<CutsceneFacingKeyBlob>();
                    partTracksBySlotSegment[slotIndex, segmentIndex] = new List<PartTrackBucket>();
                }

                CutsceneSlot slot = cutscene.slots[slotIndex];
                BucketClipBlocks(slot, boundaries, warnings, clipBlocksBySlotSegment, slotIndex);
                BucketTransformKeys(slot.transformKeys, boundaries, transformKeysBySlotSegment, slotIndex);
                BucketFacingKeys(slot.facingKeys, boundaries, facingKeysBySlotSegment, slotIndex);
                BucketPartTracks(slot, boundaries, warnings, partTracksBySlotSegment, slotIndex);
            }

            List<CutsceneCameraKeyBlob>[] cameraKeysBySegment = new List<CutsceneCameraKeyBlob>[segmentCount];
            List<float>[] cameraCutsBySegment = new List<float>[segmentCount];
            List<CutsceneEventMarkerBlob>[] eventsBySegment = new List<CutsceneEventMarkerBlob>[segmentCount];
            for (int segmentIndex = 0; segmentIndex < segmentCount; segmentIndex++)
            {
                cameraKeysBySegment[segmentIndex] = new List<CutsceneCameraKeyBlob>();
                cameraCutsBySegment[segmentIndex] = new List<float>();
                eventsBySegment[segmentIndex] = new List<CutsceneEventMarkerBlob>();
            }
            BucketCameraKeys(cutscene.cameraLane?.keys, boundaries, cameraKeysBySegment);
            BucketCameraCuts(cutscene.cameraLane?.cutMarkers, boundaries, cameraCutsBySegment);
            BucketEvents(cutscene.events, boundaries, eventsBySegment);

            InsertBoundaryContinuityKeys(
                cutscene, boundaries, transformKeysBySlotSegment, facingKeysBySlotSegment,
                partTracksBySlotSegment, cameraKeysBySegment);

            for (int segmentIndex = 0; segmentIndex < segmentCount; segmentIndex++)
            {
                ref CutsceneSegmentBlob segmentBlob = ref segmentArray[segmentIndex];
                segmentBlob.duration = boundaries[segmentIndex + 1].time - boundaries[segmentIndex].time;
                FixedString64Bytes holdId = default;
                holdId.CopyFromTruncated(boundaries[segmentIndex + 1].holdId ?? string.Empty);
                segmentBlob.holdId = holdId;

                int slotCount = cutscene.slots?.Count ?? 0;
                BlobBuilderArray<CutsceneSlotSegmentBlob> slotTrackArray =
                    builder.Allocate(ref segmentBlob.slotTracks, slotCount);
                for (int slotIndex = 0; slotIndex < slotCount; slotIndex++)
                {
                    FillSlotSegment(
                        ref builder, ref slotTrackArray[slotIndex],
                        clipBlocksBySlotSegment[slotIndex, segmentIndex],
                        transformKeysBySlotSegment[slotIndex, segmentIndex],
                        facingKeysBySlotSegment[slotIndex, segmentIndex],
                        partTracksBySlotSegment[slotIndex, segmentIndex]);
                }

                BlobBuilderArray<CutsceneCameraKeyBlob> cameraKeyArray =
                    builder.Allocate(ref segmentBlob.cameraKeys, cameraKeysBySegment[segmentIndex].Count);
                for (int i = 0; i < cameraKeysBySegment[segmentIndex].Count; i++)
                {
                    cameraKeyArray[i] = cameraKeysBySegment[segmentIndex][i];
                }

                BlobBuilderArray<float> cutArray =
                    builder.Allocate(ref segmentBlob.cameraCutTimes, cameraCutsBySegment[segmentIndex].Count);
                for (int i = 0; i < cameraCutsBySegment[segmentIndex].Count; i++)
                {
                    cutArray[i] = cameraCutsBySegment[segmentIndex][i];
                }

                BlobBuilderArray<CutsceneEventMarkerBlob> eventArray =
                    builder.Allocate(ref segmentBlob.events, eventsBySegment[segmentIndex].Count);
                for (int i = 0; i < eventsBySegment[segmentIndex].Count; i++)
                {
                    eventArray[i] = eventsBySegment[segmentIndex][i];
                }
            }
        }

        private struct PartTrackBucket
        {
            public uint tagId;
            public int targetIndex;
            public AnimatedChannels channels;
            public List<CutsceneTransformKeyBlob> keys;
        }

        private static void FillSlotSegment(
            ref BlobBuilder builder, ref CutsceneSlotSegmentBlob slotSegmentBlob,
            List<CutsceneClipBlockBlob> clipBlocks, List<CutsceneTransformKeyBlob> transformKeys,
            List<CutsceneFacingKeyBlob> facingKeys, List<PartTrackBucket> partTracks)
        {
            BlobBuilderArray<CutsceneClipBlockBlob> clipBlockArray =
                builder.Allocate(ref slotSegmentBlob.clipBlocks, clipBlocks.Count);
            for (int i = 0; i < clipBlocks.Count; i++)
            {
                clipBlockArray[i] = clipBlocks[i];
            }

            BlobBuilderArray<CutsceneTransformKeyBlob> transformKeyArray =
                builder.Allocate(ref slotSegmentBlob.transformKeys, transformKeys.Count);
            for (int i = 0; i < transformKeys.Count; i++)
            {
                transformKeyArray[i] = transformKeys[i];
            }

            BlobBuilderArray<CutsceneFacingKeyBlob> facingKeyArray =
                builder.Allocate(ref slotSegmentBlob.facingKeys, facingKeys.Count);
            for (int i = 0; i < facingKeys.Count; i++)
            {
                facingKeyArray[i] = facingKeys[i];
            }

            BlobBuilderArray<CutscenePartTrackBlob> partTrackArray =
                builder.Allocate(ref slotSegmentBlob.partTracks, partTracks.Count);
            for (int i = 0; i < partTracks.Count; i++)
            {
                partTrackArray[i].tagId = partTracks[i].tagId;
                partTrackArray[i].targetIndex = partTracks[i].targetIndex;
                partTrackArray[i].channels = partTracks[i].channels;
                BlobBuilderArray<CutsceneTransformKeyBlob> keyArray =
                    builder.Allocate(ref partTrackArray[i].keys, partTracks[i].keys.Count);
                for (int keyIndex = 0; keyIndex < partTracks[i].keys.Count; keyIndex++)
                {
                    keyArray[keyIndex] = partTracks[i].keys[keyIndex];
                }
            }
        }

        // -----------------------------------------------------------------------------------
        // Bucketing (decision G-D8: a clip block by its start time, everything else by its own
        // instant — see class remarks for why a block is never clipped across a segment boundary).
        // -----------------------------------------------------------------------------------

        private static void BucketClipBlocks(
            CutsceneSlot slot, List<SegmentBoundary> boundaries, List<string> warnings,
            List<CutsceneClipBlockBlob>[,] bucket, int slotIndex)
        {
            if (slot.clipBlocks == null || slot.kind != CutsceneSlotKind.Actor)
            {
                return;
            }
            for (int i = 0; i < slot.clipBlocks.Count; i++)
            {
                CutsceneClipBlock block = slot.clipBlocks[i];
                if (block.clipId != 0UL && !ClipExistsInSlot(slot, block.clipId))
                {
                    warnings.Add(
                        "Cutscene clip block " + i + " on slot '" + slot.name + "' names clip id 0x"
                        + block.clipId.ToString("X16") + ", which is not in any of the slot's clip "
                        + "sets. Baked anyway — the bound actor's own registry may still resolve it.");
                }

                float segmentStart;
                int segmentIndex = AssignToSegment(boundaries, block.start, out segmentStart);
                bucket[slotIndex, segmentIndex].Add(new CutsceneClipBlockBlob
                {
                    clipId = block.clipId,
                    start = block.start - segmentStart,
                    duration = block.duration,
                    loop = block.loop
                });
            }
        }

        private static bool ClipExistsInSlot(CutsceneSlot slot, ulong clipId)
        {
            if (slot.clipSets == null)
            {
                return false;
            }
            for (int setIndex = 0; setIndex < slot.clipSets.Count; setIndex++)
            {
                ClipSetAsset clipSet = slot.clipSets[setIndex];
                if (clipSet == null || clipSet.clips == null)
                {
                    continue;
                }
                for (int clipIndex = 0; clipIndex < clipSet.clips.Count; clipIndex++)
                {
                    if (clipSet.clips[clipIndex] != null && clipSet.clips[clipIndex].stableId == clipId)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static void BucketTransformKeys(
            List<CutsceneTransformKey> keys, List<SegmentBoundary> boundaries,
            List<CutsceneTransformKeyBlob>[,] bucket, int slotIndex)
        {
            if (keys == null)
            {
                return;
            }
            for (int i = 0; i < keys.Count; i++)
            {
                float segmentStart;
                int segmentIndex = AssignToSegment(boundaries, keys[i].time, out segmentStart);
                bucket[slotIndex, segmentIndex].Add(ToBlob(keys[i], segmentStart));
            }
        }

        private static void BucketFacingKeys(
            List<CutsceneFacingKey> keys, List<SegmentBoundary> boundaries,
            List<CutsceneFacingKeyBlob>[,] bucket, int slotIndex)
        {
            if (keys == null)
            {
                return;
            }
            for (int i = 0; i < keys.Count; i++)
            {
                float segmentStart;
                int segmentIndex = AssignToSegment(boundaries, keys[i].time, out segmentStart);
                bucket[slotIndex, segmentIndex].Add(new CutsceneFacingKeyBlob
                {
                    time = keys[i].time - segmentStart,
                    angleRadians = math.radians(keys[i].angleDegrees)
                });
            }
        }

        private static void BucketPartTracks(
            CutsceneSlot slot, List<SegmentBoundary> boundaries, List<string> warnings,
            List<PartTrackBucket>[,] bucket, int slotIndex)
        {
            if (slot.partTracks == null || slot.kind != CutsceneSlotKind.Actor)
            {
                return;
            }

            int segmentCount = boundaries.Count - 1;
            for (int trackIndex = 0; trackIndex < slot.partTracks.Count; trackIndex++)
            {
                CutsceneKeyedTrack track = slot.partTracks[trackIndex];
                int resolvedTargetIndex = slot.rig != null
                    ? ResolveDenseTargetIndexForTag(slot.rig, track.tagId)
                    : -1;
                if (track.tagId == 0u || resolvedTargetIndex < 0)
                {
                    warnings.Add(
                        "Cutscene part track " + trackIndex + " on slot '" + slot.name + "' names tag id 0x"
                        + track.tagId.ToString("X8") + ", which the slot's rig does not declare. Baked "
                        + "anyway (rule T2) — skipped at play time (decision G-D9: resolved once here, "
                        + "against the rig assigned at bake time).");
                }

                // Every segment needs its own bucket entry for this track, even an empty one, so the
                // player can address "part track N" the same way across segments without the index
                // shifting because a middle segment happened to have no keys for it.
                List<CutsceneTransformKeyBlob>[] perSegmentKeys = new List<CutsceneTransformKeyBlob>[segmentCount];
                for (int segmentIndex = 0; segmentIndex < segmentCount; segmentIndex++)
                {
                    perSegmentKeys[segmentIndex] = new List<CutsceneTransformKeyBlob>();
                }
                if (track.keys != null)
                {
                    for (int keyIndex = 0; keyIndex < track.keys.Count; keyIndex++)
                    {
                        float segmentStart;
                        int segmentIndex = AssignToSegment(boundaries, track.keys[keyIndex].time, out segmentStart);
                        perSegmentKeys[segmentIndex].Add(ToBlob(track.keys[keyIndex], segmentStart));
                    }
                }
                for (int segmentIndex = 0; segmentIndex < segmentCount; segmentIndex++)
                {
                    bucket[slotIndex, segmentIndex].Add(new PartTrackBucket
                    {
                        tagId = track.tagId,
                        targetIndex = resolvedTargetIndex,
                        channels = track.channels,
                        keys = perSegmentKeys[segmentIndex]
                    });
                }
            }
        }

        /// <summary>
        /// The dense target index <paramref name="tagId"/> resolves to on <paramref name="rig"/>, in
        /// the exact canonical (ascending stable id) order <c>ClipRegistryBuilder.BuildCanonicalTargets</c>
        /// uses — see decision G-D9 on <see cref="CutscenePartTrackBlob.targetIndex"/> for why the two
        /// must agree. Returns −1 when the tag is 0 or no target on the rig carries it.
        /// </summary>
        private static int ResolveDenseTargetIndexForTag(RigAsset rig, uint tagId)
        {
            if (tagId == 0u || rig.targets == null)
            {
                return -1;
            }

            List<RigTargetDefinition> canonicalTargets = new List<RigTargetDefinition>();
            for (int i = 0; i < rig.targets.Count; i++)
            {
                if (rig.targets[i] != null)
                {
                    canonicalTargets.Add(rig.targets[i]);
                }
            }
            canonicalTargets.Sort((left, right) => left.stableId.CompareTo(right.stableId));

            for (int denseIndex = 0; denseIndex < canonicalTargets.Count; denseIndex++)
            {
                if (canonicalTargets[denseIndex].tagId == tagId)
                {
                    return denseIndex;
                }
            }
            return -1;
        }

        private static void BucketCameraKeys(
            List<CutsceneCameraKey> keys, List<SegmentBoundary> boundaries, List<CutsceneCameraKeyBlob>[] bucket)
        {
            if (keys == null)
            {
                return;
            }
            for (int i = 0; i < keys.Count; i++)
            {
                float segmentStart;
                int segmentIndex = AssignToSegment(boundaries, keys[i].time, out segmentStart);
                bucket[segmentIndex].Add(new CutsceneCameraKeyBlob
                {
                    time = keys[i].time - segmentStart,
                    position = keys[i].position,
                    rotation = math.radians(keys[i].rotation),
                    fieldOfView = keys[i].fieldOfView,
                    interpolation = keys[i].interpolation,
                    bezierStartHandle = keys[i].bezierStartHandle,
                    bezierEndHandle = keys[i].bezierEndHandle
                });
            }
        }

        private static void BucketCameraCuts(
            List<CutsceneCameraCutMarker> cuts, List<SegmentBoundary> boundaries, List<float>[] bucket)
        {
            if (cuts == null)
            {
                return;
            }
            for (int i = 0; i < cuts.Count; i++)
            {
                float segmentStart;
                int segmentIndex = AssignToSegment(boundaries, cuts[i].time, out segmentStart);
                bucket[segmentIndex].Add(cuts[i].time - segmentStart);
            }
        }

        private static void BucketEvents(
            List<CutsceneEventMarker> events, List<SegmentBoundary> boundaries, List<CutsceneEventMarkerBlob>[] bucket)
        {
            if (events == null)
            {
                return;
            }
            for (int i = 0; i < events.Count; i++)
            {
                float segmentStart;
                int segmentIndex = AssignToSegment(boundaries, events[i].time, out segmentStart);
                bucket[segmentIndex].Add(new CutsceneEventMarkerBlob
                {
                    time = events[i].time - segmentStart,
                    eventKey = events[i].eventKey,
                    intParam = events[i].intParam,
                    floatParam = events[i].floatParam,
                    fireOnSkip = events[i].fireOnSkip
                });
            }
        }

        // -----------------------------------------------------------------------------------
        // Boundary continuity (amendment A62 defect 1, decision A62-D1): a per-segment array walk
        // never reaches across a hold, so every keyed lane still playing across one needs its value
        // at the boundary baked into both the segment that ends there and the one that starts —
        // otherwise the ending segment holds its last authored key's stale value for the rest of its
        // own duration, and the starting segment has nothing until its own first authored key.
        // -----------------------------------------------------------------------------------

        private static void InsertBoundaryContinuityKeys(
            CutsceneAsset cutscene, List<SegmentBoundary> boundaries,
            List<CutsceneTransformKeyBlob>[,] transformKeysBySlotSegment,
            List<CutsceneFacingKeyBlob>[,] facingKeysBySlotSegment,
            List<PartTrackBucket>[,] partTracksBySlotSegment,
            List<CutsceneCameraKeyBlob>[] cameraKeysBySegment)
        {
            int segmentCount = boundaries.Count - 1;
            int slotCount = cutscene.slots?.Count ?? 0;

            // Boundary 0 is the timeline's own start and the last boundary is the cutscene's own
            // end — neither separates two segments that both need continuity, only a hold does.
            for (int boundaryIndex = 1; boundaryIndex < segmentCount; boundaryIndex++)
            {
                float boundaryTime = boundaries[boundaryIndex].time;
                float endingSegmentDuration = boundaries[boundaryIndex].time - boundaries[boundaryIndex - 1].time;
                int endingSegmentIndex = boundaryIndex - 1;
                int startingSegmentIndex = boundaryIndex;

                for (int slotIndex = 0; slotIndex < slotCount; slotIndex++)
                {
                    CutsceneSlot slot = cutscene.slots[slotIndex];
                    if (slot == null)
                    {
                        continue;
                    }

                    InsertTransformContinuity(
                        slot.transformKeys, boundaryTime, endingSegmentDuration,
                        transformKeysBySlotSegment[slotIndex, endingSegmentIndex],
                        transformKeysBySlotSegment[slotIndex, startingSegmentIndex]);

                    InsertFacingContinuity(
                        slot.facingKeys, boundaryTime, facingKeysBySlotSegment[slotIndex, startingSegmentIndex]);

                    if (slot.partTracks != null)
                    {
                        List<PartTrackBucket> endingBuckets = partTracksBySlotSegment[slotIndex, endingSegmentIndex];
                        List<PartTrackBucket> startingBuckets = partTracksBySlotSegment[slotIndex, startingSegmentIndex];
                        for (int trackIndex = 0; trackIndex < slot.partTracks.Count; trackIndex++)
                        {
                            InsertTransformContinuity(
                                slot.partTracks[trackIndex].keys, boundaryTime, endingSegmentDuration,
                                endingBuckets[trackIndex].keys, startingBuckets[trackIndex].keys);
                        }
                    }
                }

                InsertCameraContinuity(
                    cutscene.cameraLane, boundaryTime, endingSegmentDuration,
                    cameraKeysBySegment[endingSegmentIndex], cameraKeysBySegment[startingSegmentIndex]);
            }
        }

        /// <summary>
        /// Root/part-track continuity for one lane across one boundary. The ending segment always
        /// gets a synthetic copy of the boundary pose at its own last instant; the starting segment
        /// gets one at its own first instant too, unless an authored key already sits at the
        /// boundary (it lands there naturally via <see cref="AssignToSegment"/>'s half-open rule).
        /// </summary>
        private static void InsertTransformContinuity(
            List<CutsceneTransformKey> flatKeys, float boundaryTime, float endingSegmentDuration,
            List<CutsceneTransformKeyBlob> endingSegmentKeys, List<CutsceneTransformKeyBlob> startingSegmentKeys)
        {
            if (flatKeys == null || flatKeys.Count == 0)
            {
                return;
            }

            float3 sampledPosition;
            float3 sampledEulerDegrees;
            float3 sampledScale;
            CutsceneKeySampler.TrySampleTransform(
                flatKeys, boundaryTime, out sampledPosition, out sampledEulerDegrees, out sampledScale);

            int precedingIndex = FindPrecedingKeyIndex(flatKeys, boundaryTime);
            CutsceneTransformKeyBlob endingKey = new CutsceneTransformKeyBlob
            {
                time = endingSegmentDuration,
                position = sampledPosition,
                rotation = math.radians(sampledEulerDegrees),
                scale = sampledScale,
                interpolation = flatKeys[precedingIndex].interpolation,
                bezierStartHandle = flatKeys[precedingIndex].bezierStartHandle,
                bezierEndHandle = flatKeys[precedingIndex].bezierEndHandle
            };
            // Appended, not inserted: every key already bucketed into the ending segment has a
            // rebased time strictly less than its own duration, so this stays the sorted list's max.
            endingSegmentKeys.Add(endingKey);

            if (!HasKeyNear(flatKeys, boundaryTime))
            {
                CutsceneTransformKeyBlob startingKey = endingKey;
                startingKey.time = 0f;
                // Inserted at the front: this is time 0 of the starting segment, and nothing already
                // bucketed there can be earlier (no authored key sits within epsilon of the boundary
                // in this branch).
                startingSegmentKeys.Insert(0, startingKey);
            }
        }

        /// <summary>
        /// Facing continuity across one boundary (§3.2): unlike a transform/camera lane, a facing
        /// lane has no interpolated "current value" to hold — only the last override key at or
        /// before the playhead matters (<c>CutsceneKeySampler.TryResolveFacingAngle</c>'s own rule).
        /// So only the starting segment needs anything, and only when the override that was active
        /// going into the hold would otherwise vanish from the segment that resumes after it.
        /// </summary>
        private static void InsertFacingContinuity(
            List<CutsceneFacingKey> flatKeys, float boundaryTime, List<CutsceneFacingKeyBlob> startingSegmentKeys)
        {
            if (flatKeys == null || flatKeys.Count == 0)
            {
                return;
            }

            int bestIndex = -1;
            for (int i = 0; i < flatKeys.Count; i++)
            {
                if (flatKeys[i].time <= boundaryTime && (bestIndex < 0 || flatKeys[i].time > flatKeys[bestIndex].time))
                {
                    bestIndex = i;
                }
            }
            if (bestIndex < 0 || math.abs(flatKeys[bestIndex].time - boundaryTime) <= BoundaryEpsilon)
            {
                // No override yet, or one already sits at the boundary and lands in the starting
                // segment on its own via AssignToSegment.
                return;
            }

            startingSegmentKeys.Insert(0, new CutsceneFacingKeyBlob
            {
                time = 0f,
                angleRadians = math.radians(flatKeys[bestIndex].angleDegrees)
            });
        }

        /// <summary>Camera-lane counterpart of <see cref="InsertTransformContinuity"/>, sampled cut-aware (decision G-D7 stays authoritative even at a hold).</summary>
        private static void InsertCameraContinuity(
            CutsceneCameraLane cameraLane, float boundaryTime, float endingSegmentDuration,
            List<CutsceneCameraKeyBlob> endingSegmentKeys, List<CutsceneCameraKeyBlob> startingSegmentKeys)
        {
            List<CutsceneCameraKey> flatKeys = cameraLane?.keys;
            if (flatKeys == null || flatKeys.Count == 0)
            {
                return;
            }

            float3 sampledPosition;
            float3 sampledEulerDegrees;
            float fieldOfView;
            bool isCut;
            CutsceneKeySampler.SampleCameraWithCuts(
                flatKeys, cameraLane.cutMarkers, boundaryTime,
                out sampledPosition, out sampledEulerDegrees, out fieldOfView, out isCut);

            int precedingIndex = FindPrecedingCameraKeyIndex(flatKeys, boundaryTime);
            CutsceneCameraKeyBlob endingKey = new CutsceneCameraKeyBlob
            {
                time = endingSegmentDuration,
                position = sampledPosition,
                rotation = math.radians(sampledEulerDegrees),
                fieldOfView = fieldOfView,
                interpolation = flatKeys[precedingIndex].interpolation,
                bezierStartHandle = flatKeys[precedingIndex].bezierStartHandle,
                bezierEndHandle = flatKeys[precedingIndex].bezierEndHandle
            };
            endingSegmentKeys.Add(endingKey);

            bool hasAuthoredKeyAtBoundary = false;
            for (int i = 0; i < flatKeys.Count; i++)
            {
                if (math.abs(flatKeys[i].time - boundaryTime) <= BoundaryEpsilon)
                {
                    hasAuthoredKeyAtBoundary = true;
                    break;
                }
            }
            if (!hasAuthoredKeyAtBoundary)
            {
                CutsceneCameraKeyBlob startingKey = endingKey;
                startingKey.time = 0f;
                startingSegmentKeys.Insert(0, startingKey);
            }
        }

        private static bool HasKeyNear(List<CutsceneTransformKey> keys, float time)
        {
            for (int i = 0; i < keys.Count; i++)
            {
                if (math.abs(keys[i].time - time) <= BoundaryEpsilon)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>The last key at or before <paramref name="time"/> (assumes ascending-sorted keys, same as every other lane walk in this builder); falls back to the first key when <paramref name="time"/> precedes every key.</summary>
        private static int FindPrecedingKeyIndex(List<CutsceneTransformKey> keys, float time)
        {
            int precedingIndex = 0;
            for (int i = 0; i < keys.Count; i++)
            {
                if (keys[i].time <= time)
                {
                    precedingIndex = i;
                }
                else
                {
                    break;
                }
            }
            return precedingIndex;
        }

        private static int FindPrecedingCameraKeyIndex(List<CutsceneCameraKey> keys, float time)
        {
            int precedingIndex = 0;
            for (int i = 0; i < keys.Count; i++)
            {
                if (keys[i].time <= time)
                {
                    precedingIndex = i;
                }
                else
                {
                    break;
                }
            }
            return precedingIndex;
        }

        private static CutsceneTransformKeyBlob ToBlob(CutsceneTransformKey key, float segmentStart)
        {
            return new CutsceneTransformKeyBlob
            {
                time = key.time - segmentStart,
                position = key.position,
                rotation = math.radians(key.rotation),
                scale = key.scale,
                interpolation = key.interpolation,
                bezierStartHandle = key.bezierStartHandle,
                bezierEndHandle = key.bezierEndHandle
            };
        }
    }
}
