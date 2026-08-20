// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System.Collections.Generic;
using NUnit.Framework;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// EditMode coverage of sample-rate quantization with per-entity phase
    /// (architecture sections 5.6, 8 M3): rate 0 = every frame, frame-index advancement gating,
    /// and the phase spread that puts two actors onto different sample frames.
    /// </summary>
    public sealed class SampleQuantizationTests
    {
        [Test]
        public void ShouldSample_ZeroOrNegativeRate_AlwaysSamples()
        {
            Assert.IsTrue(ClipSampler.ShouldSample(0.5f, 0.5001f, 0f, 0f));
            Assert.IsTrue(ClipSampler.ShouldSample(0.5f, 0.5001f, -1f, 0.5f));
        }

        [Test]
        public void ShouldSample_SamplesOnlyWhenThePhasedFrameIndexAdvances()
        {
            Assert.IsFalse(ClipSampler.ShouldSample(0.05f, 0.09f, 10f, 0f),
                "Both times sit inside frame 0 at 10 Hz.");
            Assert.IsTrue(ClipSampler.ShouldSample(0.09f, 0.11f, 10f, 0f),
                "The 0.1 s boundary was crossed at 10 Hz.");
            Assert.IsFalse(ClipSampler.ShouldSample(0.11f, 0.19f, 10f, 0f));
        }

        [Test]
        public void ShouldSample_PhaseOffset_ShiftsTheSampleBoundaries()
        {
            Assert.IsFalse(ClipSampler.ShouldSample(0.34f, 0.36f, 10f, 0f),
                "Without phase, 0.34→0.36 stays inside frame 3.");
            Assert.IsTrue(ClipSampler.ShouldSample(0.34f, 0.36f, 10f, 0.5f),
                "A 0.5 phase moves the boundary to 0.35, which this window crosses.");
        }

        [Test]
        public void ShouldSample_PhaseSpread_PutsTwoActorsOnDifferentSampleFrames()
        {
            const float rateHz = 10f;
            const float stepSeconds = 0.01f;
            const int stepCount = 40;

            List<int> zeroPhaseSampleSteps = new List<int>();
            List<int> halfPhaseSampleSteps = new List<int>();
            for (int stepIndex = 1; stepIndex <= stepCount; stepIndex++)
            {
                float previousElapsed = (stepIndex - 1) * stepSeconds;
                float currentElapsed = stepIndex * stepSeconds;
                if (ClipSampler.ShouldSample(previousElapsed, currentElapsed, rateHz, 0f))
                {
                    zeroPhaseSampleSteps.Add(stepIndex);
                }
                if (ClipSampler.ShouldSample(previousElapsed, currentElapsed, rateHz, 0.5f))
                {
                    halfPhaseSampleSteps.Add(stepIndex);
                }
            }

            Assert.IsNotEmpty(zeroPhaseSampleSteps);
            Assert.IsNotEmpty(halfPhaseSampleSteps);
            foreach (int sampleStep in zeroPhaseSampleSteps)
            {
                CollectionAssert.DoesNotContain(halfPhaseSampleSteps, sampleStep,
                    "Opposite phases must never sample on the same tick at a shared rate.");
            }
        }

        [Test]
        public void SampleFrameIndex_MatchesTheDocumentedFloorFormula()
        {
            Assert.AreEqual(0L, ClipSampler.SampleFrameIndex(0.05f, 10f, 0f));
            Assert.AreEqual(1L, ClipSampler.SampleFrameIndex(0.1f, 10f, 0f));
            Assert.AreEqual(3L, ClipSampler.SampleFrameIndex(0.34f, 10f, 0f));
            Assert.AreEqual(3L, ClipSampler.SampleFrameIndex(0.31f, 10f, 0.5f),
                "floor(3.1 + 0.5) = 3.");
            Assert.AreEqual(4L, ClipSampler.SampleFrameIndex(0.36f, 10f, 0.5f),
                "floor(3.6 + 0.5) = 4.");
        }
    }
}
