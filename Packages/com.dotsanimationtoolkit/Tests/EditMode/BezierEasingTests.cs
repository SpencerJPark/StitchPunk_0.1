// Copyright (c) 2026 Spencer Park. All rights reserved.

using NUnit.Framework;
using Unity.Mathematics;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// EditMode coverage of <see cref="Interpolation.Bezier"/>.
    /// </summary>
    /// <remarks>
    /// The curve is a parametric cubic solved for a weight at a time, so most of what can go wrong
    /// is in the solve rather than in the shape: a stalled Newton step, a non-terminating loop, or
    /// an unset handle pair read as a real curve. These pin the identities the solve must satisfy
    /// rather than a table of sampled values, because the identities are what the sampler relies on.
    /// </remarks>
    public sealed class BezierEasingTests
    {
        private const float Tolerance = 1e-4f;

        private static readonly float2 LinearStartHandle = new float2(1f / 3f, 1f / 3f);
        private static readonly float2 LinearEndHandle = new float2(2f / 3f, 2f / 3f);

        [Test]
        public void Bezier_WithHandlesOnTheDiagonal_IsIndistinguishableFromLinear()
        {
            for (int step = 0; step <= 10; step++)
            {
                float linearTime = step / 10f;
                Assert.AreEqual(
                    linearTime,
                    ClipSampler.EaseBezier(linearTime, in LinearStartHandle, in LinearEndHandle),
                    Tolerance,
                    "Handles on the diagonal describe the linear curve, so the solve must return " +
                    "the input at t=" + linearTime + ".");
            }
        }

        [Test]
        public void Bezier_PinsBothEndpoints()
        {
            float2 startHandle = new float2(0.9f, 0.1f);
            float2 endHandle = new float2(0.1f, 0.9f);

            Assert.AreEqual(0f, ClipSampler.EaseBezier(0f, in startHandle, in endHandle), Tolerance);
            Assert.AreEqual(1f, ClipSampler.EaseBezier(1f, in startHandle, in endHandle), Tolerance);
        }

        [Test]
        public void Bezier_WithUnsetHandles_ReadsAsLinearRatherThanCollapsing()
        {
            // The all-zero pair is what a key deserializes to when these fields did not exist. A
            // curve through (0,0),(0,0) would pin the weight near zero for most of the segment,
            // which reads as an animation that freezes and then snaps.
            float2 unsetHandle = float2.zero;

            Assert.AreEqual(0.25f, ClipSampler.EaseBezier(0.25f, in unsetHandle, in unsetHandle), Tolerance);
            Assert.AreEqual(0.5f, ClipSampler.EaseBezier(0.5f, in unsetHandle, in unsetHandle), Tolerance);
            Assert.AreEqual(0.8f, ClipSampler.EaseBezier(0.8f, in unsetHandle, in unsetHandle), Tolerance);
        }

        [Test]
        public void Bezier_IsMonotonicAcrossTheSegment_ForHandlesInTheUnitSquare()
        {
            // Validation rule V17 confines handles to the unit square precisely so this holds: the
            // bake's bounds union assumes a segment never travels past its own keys.
            float2 startHandle = new float2(0.85f, 0.05f);
            float2 endHandle = new float2(0.15f, 0.95f);

            float previousWeight = -1f;
            for (int step = 0; step <= 64; step++)
            {
                float weight = ClipSampler.EaseBezier(step / 64f, in startHandle, in endHandle);
                Assert.GreaterOrEqual(
                    weight, previousWeight - Tolerance,
                    "An ease inside the unit square must never move backwards.");
                Assert.GreaterOrEqual(weight, -Tolerance, "The weight must not leave the segment.");
                Assert.LessOrEqual(weight, 1f + Tolerance, "The weight must not leave the segment.");
                previousWeight = weight;
            }
        }

        [Test]
        public void Bezier_EaseInShapedHandles_LagBehindLinearInTheFirstHalf()
        {
            // A slow-out/fast-in curve: the classic ease-in handle placement. The identity worth
            // pinning is the direction of the deviation, not a sampled constant.
            float2 startHandle = new float2(0.75f, 0f);
            float2 endHandle = new float2(1f, 1f);

            float weightAtQuarter = ClipSampler.EaseBezier(0.25f, in startHandle, in endHandle);
            Assert.Less(
                weightAtQuarter, 0.25f,
                "Handles pulled toward the time axis must ease in, so the weight lags the input.");
        }

        [Test]
        public void Ease_RoutesBezierToTheCurve_AndLeavesOtherModesAlone()
        {
            float2 startHandle = new float2(0.75f, 0f);
            float2 endHandle = new float2(1f, 1f);

            Assert.AreEqual(
                ClipSampler.EaseBezier(0.4f, in startHandle, in endHandle),
                ClipSampler.Ease(0.4f, Interpolation.Bezier, in startHandle, in endHandle),
                Tolerance);

            // Handles must be ignored entirely by every other mode, or a key that once carried a
            // curve would keep bending after being switched back to linear.
            Assert.AreEqual(
                0.4f,
                ClipSampler.Ease(0.4f, Interpolation.Linear, in startHandle, in endHandle),
                Tolerance);
            Assert.AreEqual(
                0f,
                ClipSampler.Ease(0.4f, Interpolation.Step, in startHandle, in endHandle),
                Tolerance);
        }
    }
}
