// Copyright (c) 2026 Stitch Punk. All rights reserved.

using NUnit.Framework;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// EditMode coverage of event-window containment (architecture section 5.5, amendment A45):
    /// the half-open window convention, Once clamping, loop wrap-around, PingPong reflection,
    /// reverse playback, and the pulse-only cases that must never open a window.
    /// </summary>
    /// <remarks>
    /// These are the cases that make a damage window silently wrong rather than obviously broken —
    /// a window that fails to wrap simply never fires on the second loop, which reads as "the
    /// animation is fine, the hit detection is flaky" for as long as nobody tests it directly.
    /// </remarks>
    public sealed class EventWindowMathTests
    {
        private const float Duration = 1f;
        private const float MarkerAtHalf = 0.5f;
        private const float QuarterSecondWindow = 0.25f;

        // ---------------------------------------------------------------------------------
        // The half-open convention: open at the crossing, closed at the far edge.
        // ---------------------------------------------------------------------------------

        [Test]
        public void WindowOpens_OnTheExactFrameTheMarkerIsCrossed()
        {
            Assert.IsTrue(IsOpenAt(0.5f, LoopMode.Once));
        }

        [Test]
        public void WindowStaysOpen_PartWayThrough()
        {
            Assert.IsTrue(IsOpenAt(0.6f, LoopMode.Once));
        }

        [Test]
        public void WindowCloses_AtItsFarEdge()
        {
            // Exactly one window length past the marker: closed, so a window and the next one
            // butted against it never both report open on the same frame.
            Assert.IsFalse(IsOpenAt(0.75f, LoopMode.Once));
        }

        [Test]
        public void WindowIsClosed_BeforeTheMarkerIsReached()
        {
            Assert.IsFalse(IsOpenAt(0.4f, LoopMode.Once));
        }

        // ---------------------------------------------------------------------------------
        // Pulse-only markers.
        // ---------------------------------------------------------------------------------

        [Test]
        public void ZeroLengthWindow_NeverOpens()
        {
            Assert.IsFalse(EventWindowMath.IsWindowOpen(
                MarkerAtHalf, 0f, 0.5f, Duration, LoopMode.Once, false));
        }

        [Test]
        public void NegativeWindow_NeverOpens()
        {
            Assert.IsFalse(EventWindowMath.IsWindowOpen(
                MarkerAtHalf, -0.25f, 0.5f, Duration, LoopMode.Once, false));
        }

        [Test]
        public void ZeroDurationClip_NeverOpens()
        {
            Assert.IsFalse(EventWindowMath.IsWindowOpen(
                MarkerAtHalf, QuarterSecondWindow, 0f, 0f, LoopMode.Once, false));
        }

        [Test]
        public void UnresolvedLoopMode_NeverOpens()
        {
            // ResolveLoopMode is the caller's job; an unresolved mode must not guess.
            Assert.IsFalse(EventWindowMath.IsWindowOpen(
                MarkerAtHalf, QuarterSecondWindow, 0.5f, Duration,
                LoopMode.UseClipDefault, false));
        }

        // ---------------------------------------------------------------------------------
        // Once: clamped, never wrapped.
        // ---------------------------------------------------------------------------------

        [Test]
        public void OnceClip_DoesNotReopenTheWindowByWrapping()
        {
            // Well past the marker on a clip that never loops.
            Assert.IsFalse(IsOpenAt(0.95f, LoopMode.Once));
        }

        [Test]
        public void OnceClip_HoldsAWindowOpenThatOverrunsTheClipEnd()
        {
            // Marker at 0.9 with a 0.25s window on a 1s Once clip: time parks at the end, and the
            // window is still open there rather than being cut short by the clamp.
            Assert.IsTrue(EventWindowMath.IsWindowOpen(
                0.9f, QuarterSecondWindow, Duration, Duration, LoopMode.Once, false));
        }

        [Test]
        public void OnceClip_ClampsOvershootRatherThanLettingElapsedGrow()
        {
            // A layer parked past the end must report the same thing as one exactly at the end.
            Assert.IsTrue(EventWindowMath.IsWindowOpen(
                0.9f, QuarterSecondWindow, Duration * 10f, Duration, LoopMode.Once, false));
        }

        // ---------------------------------------------------------------------------------
        // Loop: the window reopens every revolution and wraps across the boundary.
        // ---------------------------------------------------------------------------------

        [Test]
        public void LoopingClip_ReopensTheWindowOnTheSecondRevolution()
        {
            Assert.IsTrue(IsOpenAt(Duration + 0.6f, LoopMode.Loop));
        }

        [Test]
        public void LoopingClip_ReopensTheWindowManyRevolutionsLater()
        {
            Assert.IsTrue(IsOpenAt(Duration * 97f + 0.6f, LoopMode.Loop));
        }

        [Test]
        public void LoopingClip_ClosesTheWindowBetweenRevolutions()
        {
            Assert.IsFalse(IsOpenAt(Duration + 0.1f, LoopMode.Loop));
        }

        [Test]
        public void LoopingClip_CarriesAWindowAcrossTheLoopBoundary()
        {
            // Marker at 0.9 with a 0.25s window: the last 0.15s of it belong to the next
            // revolution, which is the case a naive "time >= marker" test gets wrong.
            Assert.IsTrue(EventWindowMath.IsWindowOpen(
                0.9f, QuarterSecondWindow, Duration + 0.1f, Duration, LoopMode.Loop, false));
        }

        [Test]
        public void LoopingClip_MarkerAtZeroIsOpenAtPlayStart()
        {
            // Documented divergence from the pulse convention: a marker at 0 does not *fire* at
            // play start, but its window is open there, because the playhead is on the marker.
            Assert.IsTrue(EventWindowMath.IsWindowOpen(
                0f, QuarterSecondWindow, 0f, Duration, LoopMode.Loop, false));
        }

        // ---------------------------------------------------------------------------------
        // Reverse playback: the window trails the direction of travel.
        // ---------------------------------------------------------------------------------

        [Test]
        public void ReversePlayback_OpensTheWindowBehindTheMarker()
        {
            Assert.IsTrue(EventWindowMath.IsWindowOpen(
                MarkerAtHalf, QuarterSecondWindow, 0.4f, Duration, LoopMode.Once, true));
        }

        [Test]
        public void ReversePlayback_LeavesTheForwardSideClosed()
        {
            Assert.IsFalse(EventWindowMath.IsWindowOpen(
                MarkerAtHalf, QuarterSecondWindow, 0.6f, Duration, LoopMode.Once, true));
        }

        [Test]
        public void ReversePlayback_WrapsOnALoopingClip()
        {
            // Travelling backwards from 0.1 crosses the 0.9 marker's mirror of the boundary.
            Assert.IsTrue(EventWindowMath.IsWindowOpen(
                0.05f, QuarterSecondWindow, Duration - 0.1f, Duration, LoopMode.Loop, true));
        }

        // ---------------------------------------------------------------------------------
        // PingPong: two crossings per period, endpoints counted once.
        // ---------------------------------------------------------------------------------

        [Test]
        public void PingPong_OpensOnTheForwardLeg()
        {
            Assert.IsTrue(IsOpenAt(0.6f, LoopMode.PingPong));
        }

        [Test]
        public void PingPong_OpensAgainOnTheReflectedLeg()
        {
            // Period is 2s; the mid-clip marker's mirror sits at 1.5s, so 1.6s is 0.1s past it.
            Assert.IsTrue(IsOpenAt(1.6f, LoopMode.PingPong));
        }

        [Test]
        public void PingPong_ClosesBetweenTheTwoLegs()
        {
            Assert.IsFalse(IsOpenAt(1.2f, LoopMode.PingPong));
        }

        [Test]
        public void PingPong_ReopensOnTheNextPeriod()
        {
            Assert.IsTrue(IsOpenAt(2f * Duration + 0.6f, LoopMode.PingPong));
        }

        [Test]
        public void PingPong_EndpointMarkerFoldsItsTwoCrossingsOntoOne()
        {
            // A marker at 1 has its forward crossing at 1s and its mirror at 2×1 − 1 = 1s: the
            // same instant. It must open once there, not read as two overlapping windows.
            Assert.IsTrue(EventWindowMath.IsWindowOpen(
                1f, QuarterSecondWindow, Duration, Duration, LoopMode.PingPong, false));
            Assert.IsFalse(EventWindowMath.IsWindowOpen(
                1f, QuarterSecondWindow, Duration + 0.5f, Duration, LoopMode.PingPong, false));
        }

        // ---------------------------------------------------------------------------------
        // ElapsedSinceCrossing, tested directly — the arithmetic the boolean hides.
        // ---------------------------------------------------------------------------------

        [Test]
        public void Elapsed_IsZeroAtTheCrossing()
        {
            Assert.AreEqual(0f, EventWindowMath.ElapsedSinceCrossing(
                MarkerAtHalf, 0.5f, Duration, LoopMode.Once, false), 1e-5f);
        }

        [Test]
        public void Elapsed_IsNegativeBeforeAOnceMarkerIsReached()
        {
            Assert.Less(EventWindowMath.ElapsedSinceCrossing(
                MarkerAtHalf, 0.25f, Duration, LoopMode.Once, false), 0f);
        }

        [Test]
        public void Elapsed_WrapsIntoRangeOnALoopingClip()
        {
            // 0.1s into the second revolution is 0.6s past a marker at 0.5.
            Assert.AreEqual(0.6f, EventWindowMath.ElapsedSinceCrossing(
                MarkerAtHalf, Duration + 0.1f, Duration, LoopMode.Loop, false), 1e-5f);
        }

        [Test]
        public void Elapsed_IsNeverNegativeOnALoopingClip()
        {
            // Modular arithmetic that keeps the dividend's sign would return −0.4 here, and every
            // window test downstream would read that as "not yet crossed".
            Assert.AreEqual(0.6f, EventWindowMath.ElapsedSinceCrossing(
                0.9f, 0.5f, Duration, LoopMode.Loop, false), 1e-5f);
        }

        /// <summary>The standard case: a marker at the clip's midpoint with a quarter-second window.</summary>
        private static bool IsOpenAt(float currentTime, LoopMode loopMode)
        {
            return EventWindowMath.IsWindowOpen(
                MarkerAtHalf, QuarterSecondWindow, currentTime, Duration, loopMode, false);
        }
    }
}
