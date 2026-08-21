// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Editor;
using NUnit.Framework;
using Unity.Mathematics;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>
    /// EditMode coverage of <see cref="EasingPresets"/>, the clip inspector's easing shapes.
    /// </summary>
    /// <remarks>
    /// The table makes two promises the inspector is built on and neither is checkable by looking at
    /// it: that a key always resolves back to the preset it was set from, and that a fixed mode's
    /// handles draw the curve <c>ClipSampler</c> actually plays. Break the second and picking a
    /// preset would look right until the author dragged a handle, at which point the curve would
    /// jump — the class of bug that gets blamed on the drag rather than on the table.
    /// </remarks>
    public sealed class EasingPresetTests
    {
        private const float ExactTolerance = 1e-3f;
        private const float FittedTolerance = 0.01f;

        private static readonly Interpolation[] fixedModes =
        {
            Interpolation.Linear,
            Interpolation.Step,
            Interpolation.EaseIn,
            Interpolation.EaseOut,
            Interpolation.EaseInOut
        };

        [Test]
        public void Linear_IsTheFirstPreset_AndTheEnumDefault()
        {
            Assert.AreEqual(
                Interpolation.Linear, default(Interpolation),
                "A key that has never been touched must sit on the preset the list opens on.");
            Assert.AreEqual(
                Interpolation.Linear, EasingPresets.At(0).interpolation,
                "Linear is the default the inspector offers first.");
            Assert.AreEqual(
                0,
                EasingPresets.IndexOf(
                    default(Interpolation), float2.zero, float2.zero),
                "A default key must report the first preset, not a custom shape.");
        }

        [Test]
        public void EveryFixedMode_ResolvesToItsOwnPreset_WhateverHandlesTheKeyHolds()
        {
            // Handles left over from an earlier drag are unread in a fixed mode, so they must not
            // change which preset the key reports.
            float2 staleStartHandle = new float2(0.8f, 0.1f);
            float2 staleEndHandle = new float2(0.9f, 0.4f);

            foreach (Interpolation mode in fixedModes)
            {
                int presetIndex = EasingPresets.IndexOf(mode, staleStartHandle, staleEndHandle);
                Assert.IsFalse(
                    EasingPresets.IsCustomIndex(presetIndex),
                    mode + " is a preset, so it must never resolve to Custom.");
                Assert.AreEqual(
                    mode, EasingPresets.At(presetIndex).interpolation,
                    "The resolved preset must be the mode's own.");
            }
        }

        [Test]
        public void BezierPresets_ResolveBackToThemselves()
        {
            for (int presetIndex = 0; presetIndex < EasingPresets.CustomIndex; presetIndex++)
            {
                EasingPreset preset = EasingPresets.At(presetIndex);
                if (preset.interpolation != Interpolation.Bezier)
                {
                    continue;
                }
                Assert.AreEqual(
                    presetIndex,
                    EasingPresets.IndexOf(
                        preset.interpolation, preset.startHandle, preset.endHandle),
                    "Preset '" + preset.displayName
                    + "' must be recognised from the handles it writes.");
            }
        }

        [Test]
        public void HandShapedBezier_ReportsCustom()
        {
            float2 startHandle = new float2(0.9f, 0.02f);
            float2 endHandle = new float2(0.11f, 0.97f);

            Assert.IsTrue(
                EasingPresets.IsCustomIndex(
                    EasingPresets.IndexOf(Interpolation.Bezier, startHandle, endHandle)),
                "Handles matching no preset are a custom curve.");
        }

        [Test]
        public void UnsetBezierHandles_ReportLinear_BecauseThatIsWhatTheSamplerPlays()
        {
            Assert.AreEqual(
                0,
                EasingPresets.IndexOf(Interpolation.Bezier, float2.zero, float2.zero),
                "ClipSampler reads an all-zero handle pair as linear, so the inspector must name "
                + "the shape being played rather than call it custom.");
        }

        [Test]
        public void EaseInAndEaseOutHandles_ReproduceTheSamplersOwnCurve()
        {
            // Exact, not approximate: a quadratic Bézier is t² outright, and degree-elevating it to
            // a cubic is what produces these thirds.
            AssertHandlesMatchMode(Interpolation.EaseIn, ExactTolerance);
            AssertHandlesMatchMode(Interpolation.EaseOut, ExactTolerance);
        }

        [Test]
        public void LinearHandles_ReproduceTheStraightLine()
        {
            AssertHandlesMatchMode(Interpolation.Linear, ExactTolerance);
        }

        [Test]
        public void EaseInOutHandles_TrackTheSamplersPiecewiseCurve_WithinTheFittingError()
        {
            // A single cubic cannot be two quadratics, so this one is a fit. It is held to a
            // tolerance a person could not see rather than to an identity it cannot satisfy.
            AssertHandlesMatchMode(Interpolation.EaseInOut, FittedTolerance);
        }

        [Test]
        public void EveryPresetHandle_StaysInsideTheUnitSquare()
        {
            for (int presetIndex = 0; presetIndex < EasingPresets.CustomIndex; presetIndex++)
            {
                EasingPreset preset = EasingPresets.At(presetIndex);
                AssertInsideUnitSquare(preset.startHandle, preset.displayName);
                AssertInsideUnitSquare(preset.endHandle, preset.displayName);
            }
        }

        [Test]
        public void EveryDisplayName_IsUnique_AndResolvesBackToItsIndex()
        {
            IReadOnlyList<string> displayNames = EasingPresets.DisplayNames;
            Assert.AreEqual(
                EasingPresets.CustomIndex + 1, displayNames.Count,
                "The dropdown lists every preset plus the custom entry.");
            Assert.AreEqual(
                EasingPresets.CustomDisplayName, displayNames[EasingPresets.CustomIndex],
                "Custom is always the last entry.");

            HashSet<string> seenNames = new HashSet<string>();
            for (int nameIndex = 0; nameIndex < displayNames.Count; nameIndex++)
            {
                Assert.IsTrue(
                    seenNames.Add(displayNames[nameIndex]),
                    "Duplicate label '" + displayNames[nameIndex]
                    + "' would make the dropdown's selection ambiguous.");
                Assert.AreEqual(
                    nameIndex, EasingPresets.IndexOfDisplayName(displayNames[nameIndex]),
                    "A label must resolve to the entry it came from.");
            }

            Assert.AreEqual(
                -1, EasingPresets.IndexOfDisplayName("Not A Preset"),
                "An unknown label is rejected rather than silently treated as the first entry.");
        }

        private static void AssertHandlesMatchMode(Interpolation mode, float tolerance)
        {
            float2 startHandle;
            float2 endHandle;
            EasingPresets.HandlesFor(mode, out startHandle, out endHandle);

            for (int step = 0; step <= 20; step++)
            {
                float linearTime = step / 20f;
                Assert.AreEqual(
                    ClipSampler.Ease(linearTime, mode),
                    ClipSampler.EaseBezier(linearTime, in startHandle, in endHandle),
                    tolerance,
                    mode + "'s preset handles must draw the curve the sampler plays, at t="
                    + linearTime + ".");
            }
        }

        private static void AssertInsideUnitSquare(float2 handle, string presetName)
        {
            Assert.IsTrue(
                math.all(handle >= 0f) && math.all(handle <= 1f),
                "Preset '" + presetName + "' would author handles validation rule V17 rejects.");
        }
    }
}
