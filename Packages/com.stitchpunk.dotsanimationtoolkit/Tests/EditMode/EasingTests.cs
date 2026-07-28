// Copyright (c) 2026 Stitch Punk. All rights reserved.

using NUnit.Framework;

namespace StitchPunk.AnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// EditMode coverage of the five easing curves (architecture sections 3.2, 8 M3): shapes
    /// absorbed verbatim from the audited host sampler.
    /// </summary>
    public sealed class EasingTests
    {
        private const float Tolerance = 1e-5f;

        [Test]
        public void Ease_Linear_ReturnsInputUnchanged()
        {
            Assert.AreEqual(0f, ClipSampler.Ease(0f, Interpolation.Linear), Tolerance);
            Assert.AreEqual(0.25f, ClipSampler.Ease(0.25f, Interpolation.Linear), Tolerance);
            Assert.AreEqual(0.7f, ClipSampler.Ease(0.7f, Interpolation.Linear), Tolerance);
            Assert.AreEqual(1f, ClipSampler.Ease(1f, Interpolation.Linear), Tolerance);
        }

        [Test]
        public void Ease_Step_ReturnsZeroWeightEverywhere()
        {
            Assert.AreEqual(0f, ClipSampler.Ease(0f, Interpolation.Step), Tolerance);
            Assert.AreEqual(0f, ClipSampler.Ease(0.5f, Interpolation.Step), Tolerance);
            Assert.AreEqual(0f, ClipSampler.Ease(0.999f, Interpolation.Step), Tolerance);
        }

        [Test]
        public void Ease_EaseIn_MatchesQuadraticCurve()
        {
            Assert.AreEqual(0.0625f, ClipSampler.Ease(0.25f, Interpolation.EaseIn), Tolerance);
            Assert.AreEqual(0.25f, ClipSampler.Ease(0.5f, Interpolation.EaseIn), Tolerance);
            Assert.AreEqual(0.5625f, ClipSampler.Ease(0.75f, Interpolation.EaseIn), Tolerance);
        }

        [Test]
        public void Ease_EaseOut_MatchesInverseQuadraticCurve()
        {
            Assert.AreEqual(0.4375f, ClipSampler.Ease(0.25f, Interpolation.EaseOut), Tolerance);
            Assert.AreEqual(0.75f, ClipSampler.Ease(0.5f, Interpolation.EaseOut), Tolerance);
            Assert.AreEqual(0.9375f, ClipSampler.Ease(0.75f, Interpolation.EaseOut), Tolerance);
        }

        [Test]
        public void Ease_EaseInOut_MatchesPiecewiseQuadraticCurve()
        {
            Assert.AreEqual(0.125f, ClipSampler.Ease(0.25f, Interpolation.EaseInOut), Tolerance);
            Assert.AreEqual(0.5f, ClipSampler.Ease(0.5f, Interpolation.EaseInOut), Tolerance);
            Assert.AreEqual(0.875f, ClipSampler.Ease(0.75f, Interpolation.EaseInOut), Tolerance);
        }

        [Test]
        public void Ease_ContinuousModes_PreserveSegmentEndpoints()
        {
            Interpolation[] continuousModes =
            {
                Interpolation.Linear,
                Interpolation.EaseIn,
                Interpolation.EaseOut,
                Interpolation.EaseInOut
            };
            foreach (Interpolation interpolationMode in continuousModes)
            {
                Assert.AreEqual(0f, ClipSampler.Ease(0f, interpolationMode), Tolerance,
                    "Easing " + interpolationMode + " must map 0 to 0.");
                Assert.AreEqual(1f, ClipSampler.Ease(1f, interpolationMode), Tolerance,
                    "Easing " + interpolationMode + " must map 1 to 1.");
            }
        }

        [Test]
        public void Ease_ContinuousModes_AreMonotonicAcrossTheSegment()
        {
            Interpolation[] continuousModes =
            {
                Interpolation.Linear,
                Interpolation.EaseIn,
                Interpolation.EaseOut,
                Interpolation.EaseInOut
            };
            const int sampleCount = 100;
            foreach (Interpolation interpolationMode in continuousModes)
            {
                float previousValue = ClipSampler.Ease(0f, interpolationMode);
                for (int sampleIndex = 1; sampleIndex <= sampleCount; sampleIndex++)
                {
                    float sampleTime = sampleIndex / (float)sampleCount;
                    float currentValue = ClipSampler.Ease(sampleTime, interpolationMode);
                    Assert.GreaterOrEqual(currentValue, previousValue,
                        "Easing " + interpolationMode + " must be monotonic (t = " + sampleTime + ").");
                    previousValue = currentValue;
                }
            }
        }
    }
}
