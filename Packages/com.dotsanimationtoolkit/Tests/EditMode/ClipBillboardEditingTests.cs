// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using DotsAnimationToolkit.Editor;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// EditMode coverage of <see cref="ClipBillboardEditing"/>, the billboard component's read and
    /// write path.
    /// </summary>
    /// <remarks>
    /// The one thing this has to get right and nothing else in the package checks is that a
    /// billboard key's two kinds of channel are treated differently: angle and weight are eased
    /// between keys, and <c>enabled</c> is held from its key. Sampling all three alike would either
    /// flicker an enable flag halfway through a segment or step the angle, and both look like a
    /// preview bug rather than a sampling rule.
    /// </remarks>
    public sealed class ClipBillboardEditingTests
    {
        private const float Tolerance = 1e-4f;

        private BillboardTrack track;

        [SetUp]
        public void SetUp()
        {
            track = new BillboardTrack();
            track.rootStableId = 0x77u;
            track.keys = new List<BillboardKey>();
        }

        private void AddKey(
            float normalizedTime, float angleOffsetDegrees, float blendWeight, bool enabled,
            Interpolation interpolation)
        {
            track.keys.Add(new BillboardKey
            {
                normalizedTime = normalizedTime,
                angleOffsetDegrees = angleOffsetDegrees,
                blendWeight = blendWeight,
                enabled = enabled,
                interpolation = interpolation
            });
        }

        [Test]
        public void AnEmptyTrackEvaluatesToRest_RatherThanFailingLoudly()
        {
            float angleOffsetDegrees;
            float blendWeight;
            bool enabled;

            Assert.IsFalse(ClipBillboardEditing.TryEvaluate(
                track, 0.5f, out angleOffsetDegrees, out blendWeight, out enabled));
            Assert.AreEqual(0f, angleOffsetDegrees, Tolerance);
            Assert.AreEqual(1f, blendWeight, Tolerance, "Rest is fully billboarded.");
            Assert.IsTrue(enabled);
        }

        [Test]
        public void ContinuousChannelsAreEasedBetweenKeys()
        {
            AddKey(0f, 0f, 0f, true, Interpolation.Linear);
            AddKey(1f, 90f, 1f, true, Interpolation.Linear);

            float angleOffsetDegrees;
            float blendWeight;
            bool enabled;
            ClipBillboardEditing.TryEvaluate(
                track, 0.5f, out angleOffsetDegrees, out blendWeight, out enabled);

            Assert.AreEqual(45f, angleOffsetDegrees, Tolerance);
            Assert.AreEqual(0.5f, blendWeight, Tolerance);
        }

        [Test]
        public void EnabledIsHeldFromItsKey_NeverBlended()
        {
            AddKey(0f, 0f, 1f, false, Interpolation.Linear);
            AddKey(1f, 0f, 1f, true, Interpolation.Linear);

            float angleOffsetDegrees;
            float blendWeight;
            bool enabled;

            ClipBillboardEditing.TryEvaluate(
                track, 0.99f, out angleOffsetDegrees, out blendWeight, out enabled);
            Assert.IsFalse(
                enabled,
                "An enable flag fires at its key. Held to the very edge of the segment, it must "
                + "still be the left key's value.");

            ClipBillboardEditing.TryEvaluate(
                track, 1f, out angleOffsetDegrees, out blendWeight, out enabled);
            Assert.IsTrue(enabled, "And it changes exactly on the key that says so.");
        }

        [Test]
        public void StepHoldsTheContinuousChannelsToo()
        {
            AddKey(0f, 0f, 0f, true, Interpolation.Step);
            AddKey(1f, 90f, 1f, true, Interpolation.Linear);

            float angleOffsetDegrees;
            float blendWeight;
            bool enabled;
            ClipBillboardEditing.TryEvaluate(
                track, 0.75f, out angleOffsetDegrees, out blendWeight, out enabled);

            Assert.AreEqual(0f, angleOffsetDegrees, Tolerance);
            Assert.AreEqual(0f, blendWeight, Tolerance);
        }

        [Test]
        public void SettingAValueAtAKeysTimeEditsThatKey_RatherThanAddingASecond()
        {
            AddKey(0.5f, 10f, 0.5f, true, Interpolation.Linear);

            int keyIndex = ClipBillboardEditing.SetKeyValues(track, 0.5f, 40f, 0.25f, false);

            Assert.AreEqual(0, keyIndex);
            Assert.AreEqual(1, track.keys.Count);
            Assert.AreEqual(40f, track.keys[0].angleOffsetDegrees, Tolerance);
            Assert.AreEqual(0.25f, track.keys[0].blendWeight, Tolerance);
            Assert.IsFalse(track.keys[0].enabled);
        }

        [Test]
        public void SettingAValueBetweenKeysInsertsInTimeOrder()
        {
            AddKey(0f, 0f, 1f, true, Interpolation.Linear);
            AddKey(1f, 90f, 1f, true, Interpolation.Linear);

            ClipBillboardEditing.SetKeyValues(track, 0.5f, 45f, 1f, true);

            Assert.AreEqual(3, track.keys.Count);
            Assert.AreEqual(0f, track.keys[0].normalizedTime, Tolerance);
            Assert.AreEqual(0.5f, track.keys[1].normalizedTime, Tolerance);
            Assert.AreEqual(1f, track.keys[2].normalizedTime, Tolerance);
        }

        [Test]
        public void ANewKeyInheritsTheEasingOfTheSegmentItLandsIn_HandlesIncluded()
        {
            AddKey(0f, 0f, 1f, true, Interpolation.Bezier);
            BillboardKey curvedKey = track.keys[0];
            curvedKey.bezierStartHandle = new float2(0.25f, 0.1f);
            curvedKey.bezierEndHandle = new float2(0.25f, 1f);
            track.keys[0] = curvedKey;
            AddKey(1f, 90f, 1f, true, Interpolation.Linear);

            int keyIndex = ClipBillboardEditing.SetKeyValues(track, 0.5f, 45f, 1f, true);

            Assert.AreEqual(Interpolation.Bezier, track.keys[keyIndex].interpolation);
            Assert.AreEqual(
                0.25f, track.keys[keyIndex].bezierStartHandle.x, Tolerance,
                "A Bezier with no handles reads as linear, so the handles have to travel with the "
                + "mode or keying inside a shaped curve flattens it.");
        }

        [Test]
        public void BlendWeightIsClampedToTheRangeTheRuntimeReads()
        {
            ClipBillboardEditing.SetKeyValues(track, 0.5f, 0f, 4f, true);
            Assert.AreEqual(1f, track.keys[0].blendWeight, Tolerance);

            ClipBillboardEditing.SetKeyValues(track, 0.5f, 0f, -2f, true);
            Assert.AreEqual(0f, track.keys[0].blendWeight, Tolerance);
        }

        [Test]
        public void CollectTracksForRootTakesOnlyThatRootsTracks()
        {
            ClipAsset clip = ScriptableObject.CreateInstance<ClipAsset>();
            try
            {
                clip.billboardTracks = new List<BillboardTrack>();
                clip.billboardTracks.Add(new BillboardTrack { rootStableId = 0x11u });
                clip.billboardTracks.Add(track);

                List<BillboardTrack> tracks = new List<BillboardTrack>();
                List<int> trackIndices = new List<int>();
                ClipBillboardEditing.CollectTracksForRoot(clip, 0x77u, tracks, trackIndices);

                Assert.AreEqual(1, tracks.Count);
                Assert.AreEqual(
                    1, trackIndices[0],
                    "The index must address the clip's list, which is what removal and rebuilding "
                    + "address.");
            }
            finally
            {
                Object.DestroyImmediate(clip);
            }
        }
    }
}
