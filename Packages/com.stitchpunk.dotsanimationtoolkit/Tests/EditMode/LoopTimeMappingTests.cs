// Copyright (c) 2026 Stitch Punk. All rights reserved.

using NUnit.Framework;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// EditMode coverage of loop-mode time mapping (architecture sections 5.4, 8 M3): Once
    /// clamping, Loop wrapping, PingPong reflection — including the negative times produced by
    /// reverse (negative-speed) playback.
    /// </summary>
    public sealed class LoopTimeMappingTests
    {
        private const float Tolerance = 1e-5f;

        [Test]
        public void MapTime_Once_ClampsBelowZeroAndAboveDuration()
        {
            Assert.AreEqual(0f, ClipSampler.MapTime(-0.5f, 2f, LoopMode.Once), Tolerance);
            Assert.AreEqual(2f, ClipSampler.MapTime(3.7f, 2f, LoopMode.Once), Tolerance);
        }

        [Test]
        public void MapTime_Once_PassesThroughTimesInsideTheClip()
        {
            Assert.AreEqual(0.75f, ClipSampler.MapTime(0.75f, 2f, LoopMode.Once), Tolerance);
        }

        [Test]
        public void MapTime_Loop_WrapsForwardTimeBeyondDuration()
        {
            Assert.AreEqual(0.3f, ClipSampler.MapTime(2.3f, 1f, LoopMode.Loop), Tolerance);
            Assert.AreEqual(0.25f, ClipSampler.MapTime(10.25f, 1f, LoopMode.Loop), Tolerance);
        }

        [Test]
        public void MapTime_Loop_WrapsNegativeReverseTimeIntoRange()
        {
            Assert.AreEqual(0.75f, ClipSampler.MapTime(-0.25f, 1f, LoopMode.Loop), Tolerance);
            Assert.AreEqual(0.5f, ClipSampler.MapTime(-3.5f, 1f, LoopMode.Loop), Tolerance);
        }

        [Test]
        public void MapTime_Loop_MapsExactDurationToZero()
        {
            Assert.AreEqual(0f, ClipSampler.MapTime(1f, 1f, LoopMode.Loop), Tolerance);
        }

        [Test]
        public void MapTime_PingPong_ReflectsOnTheSecondLeg()
        {
            Assert.AreEqual(0.25f, ClipSampler.MapTime(0.25f, 1f, LoopMode.PingPong), Tolerance);
            Assert.AreEqual(0.5f, ClipSampler.MapTime(1.5f, 1f, LoopMode.PingPong), Tolerance);
            Assert.AreEqual(0.1f, ClipSampler.MapTime(1.9f, 1f, LoopMode.PingPong), Tolerance);
        }

        [Test]
        public void MapTime_PingPong_RepeatsAfterAFullPeriod()
        {
            Assert.AreEqual(0.25f, ClipSampler.MapTime(2.25f, 1f, LoopMode.PingPong), Tolerance);
            Assert.AreEqual(1f, ClipSampler.MapTime(5f, 1f, LoopMode.PingPong), Tolerance);
        }

        [Test]
        public void MapTime_PingPong_ReflectsNegativeReverseTime()
        {
            Assert.AreEqual(0.5f, ClipSampler.MapTime(-0.5f, 1f, LoopMode.PingPong), Tolerance);
            Assert.AreEqual(0.75f, ClipSampler.MapTime(-1.25f, 1f, LoopMode.PingPong), Tolerance);
        }

        [Test]
        public void MapTime_NonPositiveDuration_ReturnsZero()
        {
            Assert.AreEqual(0f, ClipSampler.MapTime(0.5f, 0f, LoopMode.Loop), Tolerance);
            Assert.AreEqual(0f, ClipSampler.MapTimeNormalized(0.5f, 0f, LoopMode.Loop), Tolerance);
        }

        [Test]
        public void MapTimeNormalized_ReturnsTimeDividedByDuration()
        {
            Assert.AreEqual(0.5f, ClipSampler.MapTimeNormalized(1f, 2f, LoopMode.Once), Tolerance);
            Assert.AreEqual(0.15f, ClipSampler.MapTimeNormalized(2.3f, 2f, LoopMode.Loop), Tolerance);
        }

        [Test]
        public void ResolveLoopMode_UseClipDefault_ReturnsTheClipDefault()
        {
            Assert.AreEqual(LoopMode.PingPong, ClipSampler.ResolveLoopMode(LoopMode.UseClipDefault, LoopMode.PingPong));
            Assert.AreEqual(LoopMode.Once, ClipSampler.ResolveLoopMode(LoopMode.UseClipDefault, LoopMode.Once));
        }

        [Test]
        public void ResolveLoopMode_ExplicitRequest_WinsOverTheClipDefault()
        {
            Assert.AreEqual(LoopMode.Loop, ClipSampler.ResolveLoopMode(LoopMode.Loop, LoopMode.Once));
            Assert.AreEqual(LoopMode.Once, ClipSampler.ResolveLoopMode(LoopMode.Once, LoopMode.PingPong));
        }
    }
}
