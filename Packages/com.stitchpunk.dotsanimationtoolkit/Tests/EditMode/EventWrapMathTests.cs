// Copyright (c) 2026 Stitch Punk. All rights reserved.

using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;

namespace StitchPunk.AnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// EditMode coverage of wrap-correct event crossing (architecture sections 5.5, 8 M3, 11):
    /// single wrap, multi-wrap on large deltas, reverse playback, markers exactly at 0 and at 1,
    /// Once clamping, and PingPong legs. The standard fixture clip is 1 second long with markers
    /// at normalized 0 (index 0), 0.5 (index 1), and 1 (index 2).
    /// </summary>
    public sealed class EventWrapMathTests
    {
        private BlobAssetReference<ClipBlob> standardClipReference;
        private NativeList<int> crossedEventIndices;

        [SetUp]
        public void CreateFixture()
        {
            TestBlobFactory.ClipSpec clipSpec = new TestBlobFactory.ClipSpec
            {
                clipId = 1,
                duration = 1f,
                events = new TestBlobFactory.EventSpec[]
                {
                    new TestBlobFactory.EventSpec { normalizedTime = 0f, eventKey = 16 },
                    new TestBlobFactory.EventSpec { normalizedTime = 0.5f, eventKey = 17 },
                    new TestBlobFactory.EventSpec { normalizedTime = 1f, eventKey = 18 }
                }
            };
            standardClipReference = TestBlobFactory.BuildClip(clipSpec);
            crossedEventIndices = new NativeList<int>(16, Allocator.Persistent);
        }

        [TearDown]
        public void DisposeFixture()
        {
            if (standardClipReference.IsCreated)
            {
                standardClipReference.Dispose();
            }
            standardClipReference = default;
            if (crossedEventIndices.IsCreated)
            {
                crossedEventIndices.Dispose();
            }
        }

        private int Collect(float previousTime, float currentTime, LoopMode loopMode)
        {
            return EventWrapMath.CollectCrossings(
                ref standardClipReference.Value.events,
                previousTime,
                currentTime,
                1f,
                loopMode,
                ref crossedEventIndices);
        }

        private int[] CollectedIndices()
        {
            int[] collected = new int[crossedEventIndices.Length];
            for (int collectedIndex = 0; collectedIndex < crossedEventIndices.Length; collectedIndex++)
            {
                collected[collectedIndex] = crossedEventIndices[collectedIndex];
            }
            return collected;
        }

        [Test]
        public void CollectCrossings_ForwardInsideClip_CollectsOnlyMarkersInTheWindow()
        {
            int appendedCount = Collect(0.3f, 0.6f, LoopMode.Loop);
            Assert.AreEqual(1, appendedCount);
            CollectionAssert.AreEqual(new int[] { 1 }, CollectedIndices());
        }

        [Test]
        public void CollectCrossings_ForwardWindow_IsHalfOpenExcludingPreviousIncludingCurrent()
        {
            Collect(0.5f, 0.7f, LoopMode.Loop);
            Assert.AreEqual(0, crossedEventIndices.Length,
                "A marker exactly at the previous time must not fire again.");
            Collect(0.3f, 0.5f, LoopMode.Loop);
            CollectionAssert.AreEqual(new int[] { 1 }, CollectedIndices(),
                "A marker exactly at the current time must fire.");
        }

        [Test]
        public void CollectCrossings_SingleLoopWrap_CollectsEndMarkerThenStartMarkerChronologically()
        {
            int appendedCount = Collect(0.9f, 1.2f, LoopMode.Loop);
            Assert.AreEqual(2, appendedCount);
            CollectionAssert.AreEqual(new int[] { 2, 0 }, CollectedIndices());
        }

        [Test]
        public void CollectCrossings_MultiWrapLargeDelta_CollectsEveryPassInOrder()
        {
            int appendedCount = Collect(0f, 2.5f, LoopMode.Loop);
            Assert.AreEqual(7, appendedCount);
            CollectionAssert.AreEqual(new int[] { 1, 2, 0, 1, 2, 0, 1 }, CollectedIndices());
        }

        [Test]
        public void CollectCrossings_ReverseInsideClip_CollectsMarkersInReverseChronologicalOrder()
        {
            int appendedCount = Collect(0.6f, 0.2f, LoopMode.Loop);
            Assert.AreEqual(1, appendedCount);
            CollectionAssert.AreEqual(new int[] { 1 }, CollectedIndices());
        }

        [Test]
        public void CollectCrossings_ReverseAcrossLoopBoundary_CollectsBoundaryMarkers()
        {
            int appendedCount = Collect(0.2f, -0.1f, LoopMode.Loop);
            Assert.AreEqual(2, appendedCount);
            CollectionAssert.AreEqual(new int[] { 0, 2 }, CollectedIndices());
        }

        [Test]
        public void CollectCrossings_MarkerExactlyAtZero_FiresOnWrapButNotAtPlayStart()
        {
            Collect(0f, 0.4f, LoopMode.Loop);
            Assert.AreEqual(0, crossedEventIndices.Length,
                "A marker at normalized 0 must not fire when playback starts at 0.");
            int wrapCount = Collect(0.9f, 1.05f, LoopMode.Loop);
            Assert.AreEqual(2, wrapCount);
            CollectionAssert.Contains(CollectedIndices(), 0,
                "A marker at normalized 0 must fire on each loop wrap.");
        }

        [Test]
        public void CollectCrossings_MarkerExactlyAtOne_FiresWhenOnceClipReachesItsEnd()
        {
            int appendedCount = Collect(0.8f, 1f, LoopMode.Once);
            Assert.AreEqual(1, appendedCount);
            CollectionAssert.AreEqual(new int[] { 2 }, CollectedIndices());
        }

        [Test]
        public void CollectCrossings_OnceOvershoot_ClampsAndNeverWraps()
        {
            int appendedCount = Collect(0.8f, 1.7f, LoopMode.Once);
            Assert.AreEqual(1, appendedCount);
            CollectionAssert.AreEqual(new int[] { 2 }, CollectedIndices(),
                "Once must clamp at the end: the end marker fires once and nothing wraps.");
        }

        [Test]
        public void CollectCrossings_OnceReverse_FiresStartMarkerOnArrivalAtZero()
        {
            int appendedCount = Collect(0.3f, -0.2f, LoopMode.Once);
            Assert.AreEqual(1, appendedCount);
            CollectionAssert.AreEqual(new int[] { 0 }, CollectedIndices());
        }

        [Test]
        public void CollectCrossings_PingPongForward_FiresOnBothLegs()
        {
            BlobAssetReference<ClipBlob> pingPongClipReference = TestBlobFactory.BuildClip(
                new TestBlobFactory.ClipSpec
                {
                    clipId = 2,
                    duration = 1f,
                    defaultLoop = LoopMode.PingPong,
                    events = new TestBlobFactory.EventSpec[]
                    {
                        new TestBlobFactory.EventSpec { normalizedTime = 0.75f, eventKey = 16 }
                    }
                });
            try
            {
                int appendedCount = EventWrapMath.CollectCrossings(
                    ref pingPongClipReference.Value.events, 0.5f, 1.5f, 1f, LoopMode.PingPong, ref crossedEventIndices);
                Assert.AreEqual(2, appendedCount,
                    "The marker must fire on the forward leg (t = 0.75) and the reflected leg (t = 1.25).");
                CollectionAssert.AreEqual(new int[] { 0, 0 }, CollectedIndices());
            }
            finally
            {
                pingPongClipReference.Dispose();
            }
        }

        [Test]
        public void CollectCrossings_PingPongReverse_FiresOnBothLegsInReverseOrder()
        {
            BlobAssetReference<ClipBlob> pingPongClipReference = TestBlobFactory.BuildClip(
                new TestBlobFactory.ClipSpec
                {
                    clipId = 2,
                    duration = 1f,
                    defaultLoop = LoopMode.PingPong,
                    events = new TestBlobFactory.EventSpec[]
                    {
                        new TestBlobFactory.EventSpec { normalizedTime = 0.75f, eventKey = 16 }
                    }
                });
            try
            {
                int appendedCount = EventWrapMath.CollectCrossings(
                    ref pingPongClipReference.Value.events, 1.5f, 0.5f, 1f, LoopMode.PingPong, ref crossedEventIndices);
                Assert.AreEqual(2, appendedCount);
                CollectionAssert.AreEqual(new int[] { 0, 0 }, CollectedIndices());
            }
            finally
            {
                pingPongClipReference.Dispose();
            }
        }

        [Test]
        public void CollectCrossings_PingPongEndpointMarkers_FireOncePerReflectionNeverTwice()
        {
            BlobAssetReference<ClipBlob> endpointClipReference = TestBlobFactory.BuildClip(
                new TestBlobFactory.ClipSpec
                {
                    clipId = 2,
                    duration = 1f,
                    defaultLoop = LoopMode.PingPong,
                    events = new TestBlobFactory.EventSpec[]
                    {
                        new TestBlobFactory.EventSpec { normalizedTime = 0f, eventKey = 16 },
                        new TestBlobFactory.EventSpec { normalizedTime = 1f, eventKey = 17 }
                    }
                });
            try
            {
                int appendedCount = EventWrapMath.CollectCrossings(
                    ref endpointClipReference.Value.events, 0.5f, 2.5f, 1f, LoopMode.PingPong, ref crossedEventIndices);
                Assert.AreEqual(2, appendedCount,
                    "The end marker reflects at t = 1 and the start marker at t = 2 — one crossing each.");
                CollectionAssert.AreEqual(new int[] { 1, 0 }, CollectedIndices());
            }
            finally
            {
                endpointClipReference.Dispose();
            }
        }

        [Test]
        public void CollectCrossings_ZeroWindowOrNoEvents_CollectsNothing()
        {
            Assert.AreEqual(0, Collect(0.4f, 0.4f, LoopMode.Loop));

            BlobAssetReference<ClipBlob> eventlessClipReference = TestBlobFactory.BuildClip(
                new TestBlobFactory.ClipSpec { clipId = 3, duration = 1f });
            try
            {
                int appendedCount = EventWrapMath.CollectCrossings(
                    ref eventlessClipReference.Value.events, 0f, 0.9f, 1f, LoopMode.Loop, ref crossedEventIndices);
                Assert.AreEqual(0, appendedCount);
            }
            finally
            {
                eventlessClipReference.Dispose();
            }
        }

        [Test]
        public void CollectCrossings_UnresolvedUseClipDefault_CollectsNothing()
        {
            Assert.AreEqual(0, Collect(0f, 0.9f, LoopMode.UseClipDefault),
                "Callers must resolve UseClipDefault before collecting; unresolved input is inert.");
        }
    }
}
