// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using DotsAnimationToolkit.Authoring;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// Covers <c>CutsceneBlobBuilder</c>'s boundary-continuity and seam-blend baking (amendment A62,
    /// defects 1 and 3): the runtime player only ever walks one segment's own key array, so a lane
    /// still in motion across a hold needs its value baked into both the segment that ends there and
    /// the one that starts, and a crossfade whose predecessor ends up in an earlier segment needs its
    /// blend duration baked rather than derived at play time from "the previous block in this segment".
    /// </summary>
    public sealed class CutsceneBlobBuilderTests
    {
        private CutsceneAsset cutscene;
        private Func<IVocabularyRegistry> previousEventNameRegistrySource;

        [SetUp]
        public void SetUp()
        {
            previousEventNameRegistrySource = CutsceneDerivedHolds.EventNameRegistrySource;
        }

        [TearDown]
        public void TearDown()
        {
            CutsceneDerivedHolds.EventNameRegistrySource = previousEventNameRegistrySource;
            if (cutscene != null)
            {
                UnityEngine.Object.DestroyImmediate(cutscene);
            }
        }

        [Test]
        public void HoldBoundary_BakesTheSampledPoseIntoBothSegments()
        {
            cutscene = ScriptableObject.CreateInstance<CutsceneAsset>();
            CutsceneSlot propSlot = new CutsceneSlot { name = "Prop", kind = CutsceneSlotKind.Prop };
            propSlot.transformKeys.Add(new CutsceneTransformKey
            {
                time = 0f,
                position = new float3(0f, 0f, 0f),
                scale = new float3(1f, 1f, 1f),
                interpolation = Interpolation.Linear
            });
            propSlot.transformKeys.Add(new CutsceneTransformKey
            {
                time = 4f,
                position = new float3(8f, 0f, 0f),
                scale = new float3(1f, 1f, 1f),
                interpolation = Interpolation.Linear
            });
            cutscene.slots.Add(propSlot);
            cutscene.holdMarkers.Add(new CutsceneHoldMarker { time = 2f, holdId = "H" });
            cutscene.EnsureStableIds();

            BlobAssetReference<CutsceneBlob> blob;
            CutsceneBlobBuilder.Build(cutscene, out blob, null);
            try
            {
                Assert.AreEqual(2, blob.Value.segments.Length, "one hold splits the timeline into two segments");

                ref CutsceneSlotSegmentBlob endingSlotSegment = ref blob.Value.segments[0].slotTracks[0];
                Assert.Greater(endingSlotSegment.transformKeys.Length, 0);
                CutsceneTransformKeyBlob lastEndingKey = endingSlotSegment.transformKeys[endingSlotSegment.transformKeys.Length - 1];
                Assert.AreEqual(2f, lastEndingKey.time, 1e-4f, "segment 0's last key must sit at its own duration");
                Assert.AreEqual(4f, lastEndingKey.position.x, 1e-4f, "the sampled pose at the hold is the midpoint of the 0->8 motion");

                ref CutsceneSlotSegmentBlob startingSlotSegment = ref blob.Value.segments[1].slotTracks[0];
                Assert.Greater(startingSlotSegment.transformKeys.Length, 0);
                CutsceneTransformKeyBlob firstStartingKey = startingSlotSegment.transformKeys[0];
                Assert.AreEqual(0f, firstStartingKey.time, 1e-4f, "segment 1's first key must sit at its own start");
                Assert.AreEqual(4f, firstStartingKey.position.x, 1e-4f, "segment 1 must resume from the same pose segment 0 ended on");
            }
            finally
            {
                blob.Dispose();
            }
        }

        /// <summary>
        /// Spec §5's own numbers for this test (blocks [0,3) and [2.5,5), hold at 2.7) put the
        /// second block's <em>start</em> (2.5) before the hold (2.7), so decision G-D8 assigns it
        /// whole to segment 0, not segment 1 — logged in the spec's §7. This uses a hold at 2.4
        /// instead, between the two starts, which reaches the same intent: a block whose blend
        /// partner ended up in the earlier segment must still bake its seam.
        /// </summary>
        [Test]
        public void SeamAcrossAHold_KeepsItsBlendDuration()
        {
            cutscene = ScriptableObject.CreateInstance<CutsceneAsset>();
            CutsceneSlot actorSlot = new CutsceneSlot { name = "Actor", kind = CutsceneSlotKind.Actor };
            actorSlot.clipBlocks.Add(new CutsceneClipBlock { clipId = 1UL, start = 0f, duration = 3f, loop = false });
            actorSlot.clipBlocks.Add(new CutsceneClipBlock { clipId = 2UL, start = 2.5f, duration = 2.5f, loop = false });
            cutscene.slots.Add(actorSlot);
            cutscene.holdMarkers.Add(new CutsceneHoldMarker { time = 2.4f, holdId = "H" });
            cutscene.EnsureStableIds();

            BlobAssetReference<CutsceneBlob> blob;
            CutsceneBlobBuilder.Build(cutscene, out blob, null);
            try
            {
                ref CutsceneSlotSegmentBlob startingSlotSegment = ref blob.Value.segments[1].slotTracks[0];
                Assert.AreEqual(1, startingSlotSegment.clipBlocks.Length, "the second block is assigned wholly to segment 1 by its own start time");
                Assert.AreEqual(0.5f, startingSlotSegment.clipBlocks[0].blendDuration, 1e-4f,
                    "the seam's overlap survives the hold even though the outgoing block is no longer in this segment");
            }
            finally
            {
                blob.Dispose();
            }
        }

        /// <summary>
        /// Amendment A63-T1: the runtime has no slot-id map, so an attach marker's host is resolved
        /// to a dense slot index at bake. An unresolvable host must warn once and bake −1 rather
        /// than throwing — rule T2's lenient shape, so one mis-typed host cannot fail a bake.
        /// </summary>
        [Test]
        public void AttachMarker_ResolvesHostSlotIndex_AndWarnsOnUnknownHost()
        {
            cutscene = ScriptableObject.CreateInstance<CutsceneAsset>();
            CutsceneSlot propSlot = new CutsceneSlot { name = "Prop", kind = CutsceneSlotKind.Prop };
            CutsceneSlot actorSlot = new CutsceneSlot { name = "Actor", kind = CutsceneSlotKind.Actor };
            cutscene.slots.Add(propSlot);
            cutscene.slots.Add(actorSlot);
            cutscene.EnsureStableIds();

            propSlot.attachMarkers.Add(new CutsceneAttachMarker
            {
                time = 0.5f,
                kind = CutsceneAttachKind.Attach,
                hostSlotId = actorSlot.SlotId,
                localOffset = new float3(0f, 1f, 0f)
            });
            propSlot.attachMarkers.Add(new CutsceneAttachMarker
            {
                time = 1f,
                kind = CutsceneAttachKind.Attach,
                hostSlotId = 0xFFFFu
            });

            List<string> warnings = new List<string>();
            BlobAssetReference<CutsceneBlob> blob;
            LogAssert.Expect(LogType.Warning, new Regex("host slot id 0x0000FFFF"));
            CutsceneBlobBuilder.Build(cutscene, out blob, warnings);
            try
            {
                ref CutsceneSlotSegmentBlob propSegment = ref blob.Value.segments[0].slotTracks[0];
                Assert.AreEqual(2, propSegment.attachMarkers.Length, "both markers bake");
                Assert.AreEqual(1, propSegment.attachMarkers[0].hostSlotIndex,
                    "the resolvable host bakes to its dense slot index");
                Assert.AreEqual(1f, propSegment.attachMarkers[0].localOffset.y, 1e-4f,
                    "sanity: the authored offset survives the bake");
                Assert.AreEqual(-1, propSegment.attachMarkers[1].hostSlotIndex,
                    "an unknown host slot id bakes as unresolved rather than throwing");
                Assert.AreEqual(1, warnings.Count, "exactly one warning, for the unresolvable host");
            }
            finally
            {
                blob.Dispose();
            }
        }

        /// <summary>
        /// Amendment A65-T1, decision A65-D1: a cue is one marker. An event marked
        /// <c>holdUntilReleased</c> has to bake into the segment that <em>ends</em> at its own time,
        /// so it fires on the very frame its hold engages — a host that never sees the cue cannot
        /// release the hold it starts, and the cutscene stops forever.
        /// </summary>
        [Test]
        public void HoldingEvent_BakesABoundaryNamedAfterTheEvent_AndFiresBeforeIt()
        {
            const uint DialogueEventKey = 0x0000ABCDu;
            CutsceneDerivedHolds.EventNameRegistrySource =
                () => new StubEventVocabulary(DialogueEventKey, "Dialogue");

            cutscene = ScriptableObject.CreateInstance<CutsceneAsset>();
            CutsceneSlot propSlot = new CutsceneSlot { name = "Prop", kind = CutsceneSlotKind.Prop };
            propSlot.transformKeys.Add(new CutsceneTransformKey
            {
                time = 0f,
                position = float3.zero,
                scale = new float3(1f, 1f, 1f),
                interpolation = Interpolation.Linear
            });
            propSlot.transformKeys.Add(new CutsceneTransformKey
            {
                time = 4f,
                position = new float3(8f, 0f, 0f),
                scale = new float3(1f, 1f, 1f),
                interpolation = Interpolation.Linear
            });
            cutscene.slots.Add(propSlot);
            cutscene.events.Add(new CutsceneEventMarker
            {
                time = 2f,
                eventKey = DialogueEventKey,
                holdUntilReleased = true
            });
            cutscene.EnsureStableIds();

            List<string> warnings = new List<string>();
            BlobAssetReference<CutsceneBlob> blob;
            CutsceneBlobBuilder.Build(cutscene, out blob, warnings);
            try
            {
                Assert.AreEqual(2, blob.Value.segments.Length, "the holding event splits the timeline");
                Assert.AreEqual("Dialogue", blob.Value.segments[0].holdId.ToString(),
                    "the derived hold carries the event's own registry name, which is the whole host contract");
                Assert.AreEqual(1, blob.Value.segments[0].events.Length,
                    "the event belongs to the segment that ends at its time, not the one that starts there");
                Assert.AreEqual(2f, blob.Value.segments[0].events[0].time, 1e-4f,
                    "and it sits at that segment's own duration, so it fires on the frame the hold engages");
                Assert.AreEqual(0, blob.Value.segments[1].events.Length);
                Assert.IsEmpty(warnings, "a named event needs no warning");
            }
            finally
            {
                blob.Dispose();
            }
        }

        /// <summary>
        /// Decision A64-D2: a mark is also a root key, at the instant the rehearsed walk arrives.
        /// Without the merge the editor shows no travel at all and A62's boundary pass has no
        /// arrival pose to bake, so the segment after a rendezvous hold starts wherever the actor
        /// was standing when the order was issued.
        /// </summary>
        [Test]
        public void Mark_IsMergedIntoTheRootLaneAtArrivalTime()
        {
            cutscene = ScriptableObject.CreateInstance<CutsceneAsset>();
            CutsceneSlot actorSlot = new CutsceneSlot { name = "Walker", kind = CutsceneSlotKind.Actor };
            actorSlot.markKeys.Add(new CutsceneMarkKey
            {
                time = 1f,
                position = new float3(5f, 0f, 0f),
                toleranceMeters = 0.5f,
                previewTravelSeconds = 2f
            });
            cutscene.slots.Add(actorSlot);
            cutscene.EnsureStableIds();

            List<string> warnings = new List<string>();
            BlobAssetReference<CutsceneBlob> blob;
            CutsceneBlobBuilder.Build(cutscene, out blob, warnings);
            try
            {
                ref CutsceneSlotSegmentBlob slotSegment = ref blob.Value.segments[0].slotTracks[0];
                Assert.AreEqual(1, slotSegment.transformKeys.Length,
                    "the mark bakes one merged root key even though the slot has no authored root lane");
                Assert.AreEqual(3f, slotSegment.transformKeys[0].time, 1e-4f,
                    "the merged key sits at time + previewTravelSeconds, where the walk arrives");
                Assert.AreEqual(5f, slotSegment.transformKeys[0].position.x, 1e-4f);
                Assert.AreEqual(1f, slotSegment.transformKeys[0].scale.x, 1e-4f,
                    "an empty authored lane gives the merged key unit scale, never zero");
                Assert.AreEqual(1, slotSegment.markKeys.Length,
                    "the mark itself still bakes, bucketed by the instant its order is issued");
                Assert.AreEqual(1f, slotSegment.markKeys[0].time, 1e-4f);
                Assert.IsEmpty(warnings, "nothing to warn about without a hold in the way");
            }
            finally
            {
                blob.Dispose();
            }

            UnityEngine.Object.DestroyImmediate(cutscene);
            cutscene = ScriptableObject.CreateInstance<CutsceneAsset>();
            CutsceneSlot heldSlot = new CutsceneSlot { name = "Walker", kind = CutsceneSlotKind.Actor };
            heldSlot.markKeys.Add(new CutsceneMarkKey
            {
                time = 1f,
                position = new float3(5f, 0f, 0f),
                toleranceMeters = 0.5f,
                previewTravelSeconds = 2f
            });
            cutscene.slots.Add(heldSlot);
            cutscene.holdMarkers.Add(new CutsceneHoldMarker
            {
                time = 2f,
                holdId = "Rendezvous",
                autoReleaseWhenMarksReached = true
            });
            cutscene.EnsureStableIds();

            List<string> holdWarnings = new List<string>();
            BlobAssetReference<CutsceneBlob> heldBlob;
            LogAssert.Expect(LogType.Warning, new Regex("walking through rendezvous hold"));
            CutsceneBlobBuilder.Build(cutscene, out heldBlob, holdWarnings);
            try
            {
                Assert.AreEqual(1, holdWarnings.Count,
                    "a rehearsal that walks through a rendezvous hold releases it mid-walk - warned, not fatal");
                Assert.IsTrue(heldBlob.Value.segments[0].autoReleaseWhenMarksReached,
                    "the hold's rendezvous flag bakes onto the segment it ends");
                Assert.IsFalse(heldBlob.Value.segments[1].autoReleaseWhenMarksReached,
                    "the final segment ends on nothing and can never auto-release");
            }
            finally
            {
                heldBlob.Dispose();
            }
        }

        /// <summary>
        /// A one-entry event vocabulary, so the derived hold's name is the fixture's rather than
        /// whatever this project happens to have in <c>ProjectSettings/</c>.
        /// </summary>
        private sealed class StubEventVocabulary : IVocabularyRegistry
        {
            private readonly uint entryId;
            private readonly string entryName;

            public StubEventVocabulary(uint id, string name)
            {
                entryId = id;
                entryName = name;
            }

            public int VocabularyEntryCount
            {
                get { return 1; }
            }

            public string VocabularyEntryName(int entryIndex)
            {
                return entryName;
            }

            public uint VocabularyEntryId(int entryIndex)
            {
                return entryId;
            }

            public string FindName(uint id)
            {
                return id == entryId ? entryName : null;
            }

            public bool ContainsId(uint id)
            {
                return id == entryId;
            }

            public uint CreateVocabularyEntry(string name)
            {
                throw new NotSupportedException("The fixture's vocabulary is read-only.");
            }

            public string GeneratedConstantsPath { get; set; }
        }
    }
}
