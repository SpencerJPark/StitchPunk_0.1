// Copyright (c) 2026 Spencer Park. All rights reserved.

using NUnit.Framework;
using DotsAnimationToolkit.Authoring;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// Covers <c>CutsceneBlobBuilder</c>'s boundary-continuity baking (amendment A62 defect 1): the
    /// runtime player only ever walks one segment's own key array, so a lane still in motion across
    /// a hold needs its value baked into both the segment that ends there and the one that starts.
    /// </summary>
    public sealed class CutsceneBlobBuilderTests
    {
        private CutsceneAsset cutscene;

        [TearDown]
        public void TearDown()
        {
            if (cutscene != null)
            {
                Object.DestroyImmediate(cutscene);
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
    }
}
