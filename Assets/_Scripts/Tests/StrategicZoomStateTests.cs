using NUnit.Framework;
using UnityEngine;

namespace StitchPunk.Tests
{
    [TestFixture]
    public sealed class StrategicZoomStateTests
    {
        private const float Tolerance = 0.00001f;
        private const float TransitionSeconds = 1.5f;
        private const float FrameSeconds = 0.1f;

        [Test]
        public void StepMix_ReachesTargetExactlyAfterTransitionSeconds()
        {
            float mix = 0f;
            for (int step = 0; step < 15; step++)
                mix = StrategicZoomState.StepMix(mix, 1f, FrameSeconds, TransitionSeconds);
            Assert.AreEqual(1f, mix);

            mix = StrategicZoomState.StepMix(mix, 1f, 100f, TransitionSeconds);
            Assert.AreEqual(1f, mix);
        }

        [Test]
        public void StepMix_ReversingMidTransitionNeverJumpsOrOvershoots()
        {
            float mix = 0f;
            float maximumStep = FrameSeconds / TransitionSeconds + Tolerance;
            for (int step = 0; step < 6; step++)
                mix = StrategicZoomState.StepMix(mix, 1f, FrameSeconds, TransitionSeconds);

            for (int step = 0; step < 30; step++)
            {
                float next = StrategicZoomState.StepMix(mix, 0f, FrameSeconds, TransitionSeconds);
                Assert.LessOrEqual(Mathf.Abs(next - mix), maximumStep, "mix jumped on step " + step);
                Assert.GreaterOrEqual(next, 0f);
                Assert.LessOrEqual(next, 1f);
                mix = next;
            }
            Assert.AreEqual(0f, mix);
        }

        [Test]
        public void ApplyEase_WeightsAlwaysSumToOneOnLinearCurve()
        {
            AnimationCurve linear = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            for (int step = 0; step <= 20; step++)
            {
                float easedMix = StrategicZoomState.ApplyEase(step / 20f, linear);
                Assert.AreEqual(1f, (1f - easedMix) + easedMix, Tolerance);
                Assert.AreEqual(step / 20f, easedMix, Tolerance);
            }
        }

        [Test]
        public void StepMix_ZeroOrNegativeTransitionSecondsReachesTargetInOneStep()
        {
            Assert.AreEqual(1f, StrategicZoomState.StepMix(0.2f, 1f, FrameSeconds, 0f));
            Assert.AreEqual(0f, StrategicZoomState.StepMix(0.8f, 0f, FrameSeconds, -2f));
        }

        [Test]
        public void StepMix_IsMonotonicTowardTarget()
        {
            float mix = 0f;
            for (int step = 0; step < 20; step++)
            {
                float next = StrategicZoomState.StepMix(mix, 1f, FrameSeconds, TransitionSeconds);
                Assert.GreaterOrEqual(next, mix);
                mix = next;
            }
            for (int step = 0; step < 20; step++)
            {
                float next = StrategicZoomState.StepMix(mix, 0f, FrameSeconds, TransitionSeconds);
                Assert.LessOrEqual(next, mix);
                mix = next;
            }
        }
    }
}
