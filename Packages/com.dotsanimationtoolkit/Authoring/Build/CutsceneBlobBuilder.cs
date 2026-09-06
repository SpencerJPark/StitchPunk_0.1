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
        public const int SchemaVersion = 5;

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

            // Marks fold into the root lane BEFORE anything else looks at it (decision A64-D2), so
            // the boundary pass, the content end and the bucketing all see the same lane the editor
            // preview walks.
            List<CutsceneTransformKey>[] effectiveRootKeysBySlot = BuildEffectiveRootKeysBySlot(cutscene);
            List<SegmentBoundary> boundaries = ComputeSegmentBoundaries(cutscene, effectiveRootKeysBySlot, warnings);
            WarnOnMarksWalkingThroughARendezvousHold(cutscene, warnings);

            BlobBuilder builder = new BlobBuilder(Allocator.Temp);
            try
            {
                ref CutsceneBlob root = ref builder.ConstructRoot<CutsceneBlob>();
                root.schemaVersion = SchemaVersion;
                root.cutsceneKey = cutscene.StableId;

                FillSlotMeta(ref builder, ref root, cutscene);
                FillSegments(ref builder, ref root, cutscene, boundaries, effectiveRootKeysBySlot, warnings);

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

            /// <summary>Whether the hold at this boundary is a rendezvous (amendment A64 §3.2). Meaningless on boundary 0 and on the final end-of-content boundary, neither of which is a hold.</summary>
            public bool autoReleaseWhenMarksReached;
        }

        private static List<CutsceneTransformKey>[] BuildEffectiveRootKeysBySlot(CutsceneAsset cutscene)
        {
            int slotCount = cutscene.slots?.Count ?? 0;
            List<CutsceneTransformKey>[] effectiveRootKeysBySlot = new List<CutsceneTransformKey>[slotCount];
            for (int slotIndex = 0; slotIndex < slotCount; slotIndex++)
            {
                effectiveRootKeysBySlot[slotIndex] = CutsceneMarkMerge.BuildEffectiveRootKeys(cutscene.slots[slotIndex]);
            }
            return effectiveRootKeysBySlot;
        }

        /// <summary>
        /// Reports a mark whose rehearsed walk straddles a rendezvous hold (§3.2): in the editor the
        /// hold would release mid-walk, because rehearsal arrival IS timeline time. Not fatal — the
        /// runtime, where arrival is a real distance test, plays it correctly either way.
        /// </summary>
        private static void WarnOnMarksWalkingThroughARendezvousHold(CutsceneAsset cutscene, List<string> warnings)
        {
            if (cutscene.slots == null || cutscene.holdMarkers == null)
            {
                return;
            }
            for (int slotIndex = 0; slotIndex < cutscene.slots.Count; slotIndex++)
            {
                CutsceneSlot slot = cutscene.slots[slotIndex];
                if (slot == null || slot.markKeys == null)
                {
                    continue;
                }
                for (int markIndex = 0; markIndex < slot.markKeys.Count; markIndex++)
                {
                    CutsceneMarkKey mark = slot.markKeys[markIndex];
                    float arrivalTime = CutsceneMarkMerge.ArrivalTime(mark);
                    for (int holdIndex = 0; holdIndex < cutscene.holdMarkers.Count; holdIndex++)
                    {
                        CutsceneHoldMarker hold = cutscene.holdMarkers[holdIndex];
                        if (hold == null || !hold.autoReleaseWhenMarksReached)
                        {
                            continue;
                        }
                        if (hold.time > mark.time + BoundaryEpsilon && hold.time < arrivalTime - BoundaryEpsilon)
                        {
                            warnings.Add(
                                "Cutscene mark " + markIndex + " on slot '" + slot.name + "' is issued at "
                                + mark.time.ToString("0.###") + "s and arrives at " + arrivalTime.ToString("0.###")
                                + "s, walking through rendezvous hold '" + hold.holdId + "' at "
                                + hold.time.ToString("0.###") + "s. The editor rehearsal releases that hold "
                                + "mid-walk; at run time arrival is a real distance test and plays correctly.");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Boundary <c>i</c> opens segment <c>i</c>; boundary <c>i + 1</c>'s <c>holdId</c> is what
        /// segment <c>i</c> pauses on (empty for the final, synthetic end-of-content boundary).
        /// </summary>
        private static List<SegmentBoundary> ComputeSegmentBoundaries(
            CutsceneAsset cutscene, List<CutsceneTransformKey>[] effectiveRootKeysBySlot, List<string> warnings)
        {
            List<SegmentBoundary> boundaries = new List<SegmentBoundary> { new SegmentBoundary { time = 0f, holdId = null } };

            // Hold boundaries are collected apart from the opening one and only then appended: a
            // hold authored at 0 shares its time with the opening boundary, and a sort over the
            // whole list could put it first, where its id would be read as "the timeline opens" and
            // silently lost.
            List<SegmentBoundary> holdBoundaries = new List<SegmentBoundary>();
            if (cutscene.holdMarkers != null)
            {
                List<CutsceneHoldMarker> sortedHolds = new List<CutsceneHoldMarker>(cutscene.holdMarkers);
                sortedHolds.Sort((left, right) => left.time.CompareTo(right.time));
                for (int i = 0; i < sortedHolds.Count; i++)
                {
                    holdBoundaries.Add(new SegmentBoundary
                    {
                        time = Mathf.Max(0f, sortedHolds[i].time),
                        holdId = sortedHolds[i].holdId ?? string.Empty,
                        autoReleaseWhenMarksReached = sortedHolds[i].autoReleaseWhenMarksReached
                    });
                }
            }

            AddDerivedHoldBoundaries(cutscene, holdBoundaries, warnings);
            SortByTimeKeepingOrder(holdBoundaries);
            boundaries.AddRange(holdBoundaries);

            float naturalEnd = ComputeContentEndSeconds(cutscene, effectiveRootKeysBySlot);
            float lastBoundaryTime = boundaries[boundaries.Count - 1].time;
            if (naturalEnd > lastBoundaryTime + BoundaryEpsilon || boundaries.Count == 1)
            {
                boundaries.Add(new SegmentBoundary { time = Mathf.Max(naturalEnd, lastBoundaryTime), holdId = null });
            }

            return boundaries;
        }

        /// <summary>
        /// Adds one boundary per holding event (amendment A65 3.1). A derived hold never
        /// auto-releases: marks resolve a rendezvous, a cue is resolved by whoever the cue started.
        /// </summary>
        /// <remarks>
        /// Two holding events at one instant share a boundary and both fire; an authored hold at
        /// that instant keeps its own id and the event pauses on that, because a host waiting on the
        /// authored name would otherwise never see the hold it was told about.
        /// </remarks>
        private static void AddDerivedHoldBoundaries(
            CutsceneAsset cutscene, List<SegmentBoundary> holdBoundaries, List<string> warnings)
        {
            List<CutsceneDerivedHolds.DerivedHold> derivedHolds = CutsceneDerivedHolds.Collect(cutscene);
            for (int derivedIndex = 0; derivedIndex < derivedHolds.Count; derivedIndex++)
            {
                CutsceneDerivedHolds.DerivedHold derivedHold = derivedHolds[derivedIndex];
                float boundaryTime = Mathf.Max(0f, derivedHold.time);

                int existingIndex = FindHoldBoundaryAt(holdBoundaries, boundaryTime);
                if (existingIndex >= 0)
                {
                    warnings.Add(
                        "Cutscene event " + derivedHold.eventIndex + " holds at "
                        + boundaryTime.ToString("0.###") + "s, where hold '" + holdBoundaries[existingIndex].holdId
                        + "' already pauses the clock. One hold is baked and the first id wins - "
                        + "release '" + holdBoundaries[existingIndex].holdId + "', not '" + derivedHold.holdId + "'.");
                    continue;
                }

                if (!derivedHold.nameResolved)
                {
                    warnings.Add(
                        "Cutscene event " + derivedHold.eventIndex + " holds the clock but its key is not "
                        + "in the project event vocabulary. Baked as hold '" + derivedHold.holdId
                        + "' - the host must release that exact id.");
                }

                holdBoundaries.Add(new SegmentBoundary { time = boundaryTime, holdId = derivedHold.holdId });
            }
        }

        /// <summary>
        /// Insertion sort, so boundaries sharing an instant keep the order they were collected in —
        /// authored holds before derived ones, and each group in its own authored order.
        /// <c>List.Sort</c> is unstable and would make "the first hold at this time" a coin flip.
        /// </summary>
        private static void SortByTimeKeepingOrder(List<SegmentBoundary> boundaries)
        {
            for (int index = 1; index < boundaries.Count; index++)
            {
                SegmentBoundary boundary = boundaries[index];
                int insertAt = index - 1;
                while (insertAt >= 0 && boundaries[insertAt].time > boundary.time)
                {
                    boundaries[insertAt + 1] = boundaries[insertAt];
                    insertAt--;
                }
                boundaries[insertAt + 1] = boundary;
            }
        }

        /// <summary>The hold boundary already sitting at <paramref name="time"/>, or -1 for none.</summary>
        private static int FindHoldBoundaryAt(List<SegmentBoundary> boundaries, float time)
        {
            for (int boundaryIndex = 0; boundaryIndex < boundaries.Count; boundaryIndex++)
            {
                if (Mathf.Abs(boundaries[boundaryIndex].time - time) <= BoundaryEpsilon)
                {
                    return boundaryIndex;
                }
            }
            return -1;
        }

        private static float ComputeContentEndSeconds(
            CutsceneAsset cutscene, List<CutsceneTransformKey>[] effectiveRootKeysBySlot)
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
                    latest = Mathf.Max(latest, LatestTime(effectiveRootKeysBySlot[slotIndex]));
                    latest = Mathf.Max(latest, LatestFacingTime(slot.facingKeys));
                    if (slot.partTracks != null)
                    {
                        for (int i = 0; i < slot.partTracks.Count; i++)
                        {
                            latest = Mathf.Max(latest, LatestTime(slot.partTracks[i].keys));
                        }
                    }
                    if (slot.attachMarkers != null)
                    {
                        for (int i = 0; i < slot.attachMarkers.Count; i++)
                        {
                            latest = Mathf.Max(latest, slot.attachMarkers[i].time);
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
            return AssignToSegment(boundaries, time, false, out segmentStart);
        }

        /// <param name="inclusiveEnd">
        /// Assigns a moment sitting exactly on a boundary to the segment that <em>ends</em> there
        /// rather than the one that starts (amendment A65 3.1). Used only by a holding event, which
        /// must fire on the frame its own hold engages - the host sees the cue, starts its thing,
        /// and the clock is already waiting for the release.
        /// </param>
        private static int AssignToSegment(
            List<SegmentBoundary> boundaries, float time, bool inclusiveEnd, out float segmentStart)
        {
            int segmentCount = boundaries.Count - 1;
            if (inclusiveEnd)
            {
                for (int segmentIndex = 0; segmentIndex < segmentCount; segmentIndex++)
                {
                    if (Mathf.Abs(boundaries[segmentIndex + 1].time - time) <= BoundaryEpsilon)
                    {
                        segmentStart = boundaries[segmentIndex].time;
                        return segmentIndex;
                    }
                }
            }

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
            List<SegmentBoundary> boundaries, List<CutsceneTransformKey>[] effectiveRootKeysBySlot,
            List<string> warnings)
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
            List<CutsceneAttachMarkerBlob>[,] attachMarkersBySlotSegment = new List<CutsceneAttachMarkerBlob>[cutscene.slots?.Count ?? 0, segmentCount];
            List<CutsceneMarkKeyBlob>[,] markKeysBySlotSegment = new List<CutsceneMarkKeyBlob>[cutscene.slots?.Count ?? 0, segmentCount];

            for (int slotIndex = 0; slotIndex < (cutscene.slots?.Count ?? 0); slotIndex++)
            {
                for (int segmentIndex = 0; segmentIndex < segmentCount; segmentIndex++)
                {
                    clipBlocksBySlotSegment[slotIndex, segmentIndex] = new List<CutsceneClipBlockBlob>();
                    transformKeysBySlotSegment[slotIndex, segmentIndex] = new List<CutsceneTransformKeyBlob>();
                    facingKeysBySlotSegment[slotIndex, segmentIndex] = new List<CutsceneFacingKeyBlob>();
                    partTracksBySlotSegment[slotIndex, segmentIndex] = new List<PartTrackBucket>();
                    attachMarkersBySlotSegment[slotIndex, segmentIndex] = new List<CutsceneAttachMarkerBlob>();
                    markKeysBySlotSegment[slotIndex, segmentIndex] = new List<CutsceneMarkKeyBlob>();
                }

                CutsceneSlot slot = cutscene.slots[slotIndex];
                BucketClipBlocks(slot, boundaries, warnings, clipBlocksBySlotSegment, slotIndex);
                BucketTransformKeys(effectiveRootKeysBySlot[slotIndex], boundaries, transformKeysBySlotSegment, slotIndex);
                BucketFacingKeys(slot.facingKeys, boundaries, facingKeysBySlotSegment, slotIndex);
                BucketPartTracks(slot, boundaries, warnings, partTracksBySlotSegment, slotIndex);
                BucketAttachMarkers(cutscene, slot, boundaries, warnings, attachMarkersBySlotSegment, slotIndex);
                BucketMarkKeys(slot.markKeys, boundaries, markKeysBySlotSegment, slotIndex);
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
                cutscene, boundaries, effectiveRootKeysBySlot, transformKeysBySlotSegment,
                facingKeysBySlotSegment, partTracksBySlotSegment, cameraKeysBySegment);

            for (int segmentIndex = 0; segmentIndex < segmentCount; segmentIndex++)
            {
                ref CutsceneSegmentBlob segmentBlob = ref segmentArray[segmentIndex];
                segmentBlob.duration = boundaries[segmentIndex + 1].time - boundaries[segmentIndex].time;
                FixedString64Bytes holdId = default;
                holdId.CopyFromTruncated(boundaries[segmentIndex + 1].holdId ?? string.Empty);
                segmentBlob.holdId = holdId;
                segmentBlob.autoReleaseWhenMarksReached = boundaries[segmentIndex + 1].autoReleaseWhenMarksReached;

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
                        partTracksBySlotSegment[slotIndex, segmentIndex],
                        attachMarkersBySlotSegment[slotIndex, segmentIndex],
                        markKeysBySlotSegment[slotIndex, segmentIndex]);
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
            List<CutsceneFacingKeyBlob> facingKeys, List<PartTrackBucket> partTracks,
            List<CutsceneAttachMarkerBlob> attachMarkers, List<CutsceneMarkKeyBlob> markKeys)
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

            BlobBuilderArray<CutsceneAttachMarkerBlob> attachMarkerArray =
                builder.Allocate(ref slotSegmentBlob.attachMarkers, attachMarkers.Count);
            for (int i = 0; i < attachMarkers.Count; i++)
            {
                attachMarkerArray[i] = attachMarkers[i];
            }

            BlobBuilderArray<CutsceneMarkKeyBlob> markKeyArray =
                builder.Allocate(ref slotSegmentBlob.markKeys, markKeys.Count);
            for (int i = 0; i < markKeys.Count; i++)
            {
                markKeyArray[i] = markKeys[i];
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

            // Sorted by start on the flat (pre-segment-split) lane, so each block's predecessor
            // here is its true seam partner (amendment A62 defect 3) — never merely the previous
            // entry in authoring order, and never reset at a segment boundary.
            List<int> sortedIndices = new List<int>(slot.clipBlocks.Count);
            for (int i = 0; i < slot.clipBlocks.Count; i++)
            {
                sortedIndices.Add(i);
            }
            sortedIndices.Sort((left, right) => slot.clipBlocks[left].start.CompareTo(slot.clipBlocks[right].start));

            CutsceneClipBlock previousBlock = null;
            for (int sortedPosition = 0; sortedPosition < sortedIndices.Count; sortedPosition++)
            {
                int originalIndex = sortedIndices[sortedPosition];
                CutsceneClipBlock block = slot.clipBlocks[originalIndex];
                if (block.clipId != 0UL && !ClipExistsInSlot(slot, block.clipId))
                {
                    warnings.Add(
                        "Cutscene clip block " + originalIndex + " on slot '" + slot.name + "' names clip id 0x"
                        + block.clipId.ToString("X16") + ", which is not in any of the slot's clip "
                        + "sets. Baked anyway — the bound actor's own registry may still resolve it.");
                }

                float blendDuration = previousBlock != null
                    ? CutsceneBlockTiming.SeamBlendDuration(previousBlock.start, previousBlock.duration, block.start)
                    : 0f;

                float segmentStart;
                int segmentIndex = AssignToSegment(boundaries, block.start, out segmentStart);
                bucket[slotIndex, segmentIndex].Add(new CutsceneClipBlockBlob
                {
                    clipId = block.clipId,
                    start = block.start - segmentStart,
                    duration = block.duration,
                    loop = block.loop,
                    blendDuration = blendDuration,
                    speed = CutsceneBlockTiming.EffectiveBlockSpeed(block.speed),
                    clipStartOffset = Mathf.Max(0f, block.clipStartOffsetSeconds),
                    directionVariants = CutsceneDirectionVariants.Build(slot, block.clipId)
                });

                previousBlock = block;
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

        /// <summary>
        /// Buckets one slot's marks by the instant their order is issued (§3.2). The arrival key
        /// they merge into the root lane is bucketed separately, by its own later time — the two
        /// routinely land in different segments, which is exactly what a rendezvous hold is.
        /// </summary>
        private static void BucketMarkKeys(
            List<CutsceneMarkKey> markKeys, List<SegmentBoundary> boundaries,
            List<CutsceneMarkKeyBlob>[,] bucket, int slotIndex)
        {
            if (markKeys == null)
            {
                return;
            }

            List<CutsceneMarkKey> sortedMarks = new List<CutsceneMarkKey>(markKeys);
            sortedMarks.Sort((left, right) => left.time.CompareTo(right.time));

            for (int i = 0; i < sortedMarks.Count; i++)
            {
                float segmentStart;
                int segmentIndex = AssignToSegment(boundaries, sortedMarks[i].time, out segmentStart);
                bucket[slotIndex, segmentIndex].Add(new CutsceneMarkKeyBlob
                {
                    time = sortedMarks[i].time - segmentStart,
                    position = sortedMarks[i].position,
                    facingRadians = math.radians(sortedMarks[i].facingDegrees),
                    toleranceMeters = sortedMarks[i].toleranceMeters,
                    timeoutSeconds = sortedMarks[i].timeoutSeconds
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
                int segmentIndex = AssignToSegment(
                    boundaries, events[i].time, events[i].holdUntilReleased, out segmentStart);
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

        /// <summary>
        /// Buckets one slot's attach lane (amendment A63 §3.2), resolving each Attach marker's host
        /// slot id to a dense index once, here, the same way a part track's tag is resolved (G-D9) —
        /// the runtime carries no slot-id map to look one up against.
        /// </summary>
        private static void BucketAttachMarkers(
            CutsceneAsset cutscene, CutsceneSlot slot, List<SegmentBoundary> boundaries,
            List<string> warnings, List<CutsceneAttachMarkerBlob>[,] bucket, int slotIndex)
        {
            if (slot.attachMarkers == null)
            {
                return;
            }

            // Sorted by time on the flat lane so the runtime's cursor walk sees a hand-over's two
            // markers in authored order rather than in list order.
            List<CutsceneAttachMarker> sortedMarkers = new List<CutsceneAttachMarker>(slot.attachMarkers);
            sortedMarkers.Sort((left, right) => left.time.CompareTo(right.time));

            for (int markerIndex = 0; markerIndex < sortedMarkers.Count; markerIndex++)
            {
                CutsceneAttachMarker marker = sortedMarkers[markerIndex];
                if (marker == null)
                {
                    continue;
                }

                int hostSlotIndex = -1;
                if (marker.kind == CutsceneAttachKind.Attach)
                {
                    hostSlotIndex = ResolveSlotIndexById(cutscene, marker.hostSlotId);
                    if (hostSlotIndex < 0)
                    {
                        warnings.Add(
                            "Cutscene attach marker " + markerIndex + " on slot '" + slot.name +
                            "' names host slot id 0x" + marker.hostSlotId.ToString("X8") +
                            ", which this cutscene does not declare. Baked anyway — skipped at play time.");
                    }
                    else if (hostSlotIndex == slotIndex)
                    {
                        warnings.Add(
                            "Cutscene attach marker " + markerIndex + " on slot '" + slot.name +
                            "' names itself as its host. Baked as unresolved — skipped at play time.");
                        hostSlotIndex = -1;
                    }
                    else if (marker.socketId != 0u)
                    {
                        WarnOnUnknownSocket(cutscene.slots[hostSlotIndex], slot, markerIndex, marker.socketId, warnings);
                    }
                }

                float segmentStart;
                int segmentIndex = AssignToSegment(boundaries, marker.time, out segmentStart);
                bucket[slotIndex, segmentIndex].Add(new CutsceneAttachMarkerBlob
                {
                    time = marker.time - segmentStart,
                    kind = marker.kind,
                    hostSlotIndex = hostSlotIndex,
                    socketId = marker.socketId,
                    localOffset = marker.localOffset,
                    localRotation = quaternion.Euler(math.radians(marker.localEulerDegrees)),
                    hideWhileAttached = marker.hideWhileAttached,
                    detachImpulse = marker.detachImpulse
                });
            }
        }

        private static int ResolveSlotIndexById(CutsceneAsset cutscene, uint slotId)
        {
            if (slotId == 0u || cutscene.slots == null)
            {
                return -1;
            }
            for (int slotIndex = 0; slotIndex < cutscene.slots.Count; slotIndex++)
            {
                if (cutscene.slots[slotIndex] != null && cutscene.slots[slotIndex].SlotId == slotId)
                {
                    return slotIndex;
                }
            }
            return -1;
        }

        /// <summary>
        /// Warns when a socket attach names a socket the host cannot offer. Not fatal: the runtime
        /// falls back to a root attach, so a mistyped socket parks the prop at the host's origin
        /// rather than leaving it behind with nothing said.
        /// </summary>
        private static void WarnOnUnknownSocket(
            CutsceneSlot hostSlot, CutsceneSlot ridingSlot, int markerIndex, uint socketId, List<string> warnings)
        {
            if (hostSlot.rig == null)
            {
                warnings.Add(
                    "Cutscene attach marker " + markerIndex + " on slot '" + ridingSlot.name +
                    "' names socket 0x" + socketId.ToString("X8") + " on host slot '" + hostSlot.name +
                    "', which has no rig. Baked anyway — attaches to the host root at play time.");
                return;
            }

            if (hostSlot.rig.sockets != null)
            {
                for (int socketIndex = 0; socketIndex < hostSlot.rig.sockets.Count; socketIndex++)
                {
                    SocketDefinition socket = hostSlot.rig.sockets[socketIndex];
                    if (socket != null && socket.Id.Value == socketId)
                    {
                        return;
                    }
                }
            }

            warnings.Add(
                "Cutscene attach marker " + markerIndex + " on slot '" + ridingSlot.name +
                "' names socket 0x" + socketId.ToString("X8") + ", which rig '" + hostSlot.rig.name +
                "' does not declare. Baked anyway — attaches to the host root at play time.");
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
            List<CutsceneTransformKey>[] effectiveRootKeysBySlot,
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
                        effectiveRootKeysBySlot[slotIndex], boundaryTime, endingSegmentDuration,
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
