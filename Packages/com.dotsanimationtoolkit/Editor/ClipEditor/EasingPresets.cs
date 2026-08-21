// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using Unity.Mathematics;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// One named starting shape for a key's easing curve.
    /// </summary>
    /// <remarks>
    /// A preset carries handles even when its <see cref="interpolation"/> is one of the fixed modes
    /// that ignores them. The handles are what the curve widget draws its grab dots on and what the
    /// key inherits the moment the author drags one, so a preset without them would jump to an
    /// unrelated shape on the first drag.
    /// </remarks>
    public readonly struct EasingPreset
    {
        public readonly string displayName;
        public readonly Interpolation interpolation;
        public readonly float2 startHandle;
        public readonly float2 endHandle;

        public EasingPreset(
            string displayName, Interpolation interpolation, float2 startHandle, float2 endHandle)
        {
            this.displayName = displayName;
            this.interpolation = interpolation;
            this.startHandle = startHandle;
            this.endHandle = endHandle;
        }
    }

    /// <summary>
    /// The easing shapes the clip inspector offers, and the matching that decides which one a key is
    /// already on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>A preset is a starting point, not a mode.</strong> Picking one writes the cheapest
    /// representation of that shape — a fixed <see cref="Interpolation"/> where one exists, a Bézier
    /// where it does not — and dragging a handle afterwards turns whatever was picked into
    /// <see cref="Interpolation.Bezier"/> seeded from that shape. That is why the fixed modes carry
    /// handle values here: they are the cubic that matches the curve the sampler draws, so the
    /// switch to Bézier is invisible rather than a jump.
    /// </para>
    /// <para>
    /// The ease-in and ease-out handles are exact. A quadratic Bézier reproduces the sampler's
    /// <c>t²</c> and <c>1 − (1 − t)²</c> outright, and degree-elevating it to a cubic gives thirds:
    /// (⅓, 0) / (⅔, ⅓) and its mirror. The ease-in-out handles are a fit rather than an identity,
    /// because the sampler's piecewise quadratic is two curves and a single cubic is one.
    /// </para>
    /// </remarks>
    public static class EasingPresets
    {
        /// <summary>The handles that describe a straight line — the cubic form of no easing.</summary>
        public static readonly float2 LinearStartHandle = new float2(1f / 3f, 1f / 3f);

        /// <summary>See <see cref="LinearStartHandle"/>.</summary>
        public static readonly float2 LinearEndHandle = new float2(2f / 3f, 2f / 3f);

        /// <summary>The label for a Bézier whose handles match no preset.</summary>
        public const string CustomDisplayName = "Custom";

        private const float MatchTolerance = 0.001f;

        private static readonly EasingPreset[] presets =
        {
            new EasingPreset(
                "Linear", Interpolation.Linear, LinearStartHandle, LinearEndHandle),
            new EasingPreset(
                "Hold (Step)", Interpolation.Step, LinearStartHandle, LinearEndHandle),
            new EasingPreset(
                "Ease In", Interpolation.EaseIn,
                new float2(1f / 3f, 0f), new float2(2f / 3f, 1f / 3f)),
            new EasingPreset(
                "Ease Out", Interpolation.EaseOut,
                new float2(1f / 3f, 2f / 3f), new float2(2f / 3f, 1f)),
            new EasingPreset(
                "Ease In Out", Interpolation.EaseInOut,
                new float2(0.45f, 0f), new float2(0.55f, 1f)),
            new EasingPreset(
                "Smooth", Interpolation.Bezier,
                new float2(0.25f, 0.1f), new float2(0.25f, 1f)),
            new EasingPreset(
                "Snap", Interpolation.Bezier,
                new float2(0.05f, 0.7f), new float2(0.1f, 1f))
        };

        private static readonly List<string> displayNames = BuildDisplayNames();

        /// <summary>
        /// Every preset label plus <see cref="CustomDisplayName"/>, in dropdown order.
        /// </summary>
        public static IReadOnlyList<string> DisplayNames
        {
            get { return displayNames; }
        }

        /// <summary>The dropdown index of the custom entry, which is always last.</summary>
        public static int CustomIndex
        {
            get { return presets.Length; }
        }

        /// <summary>The preset the inspector starts a fresh key on.</summary>
        public static EasingPreset Linear
        {
            get { return presets[0]; }
        }

        public static bool IsCustomIndex(int index)
        {
            return index == CustomIndex;
        }

        /// <summary>
        /// The preset at a dropdown index. Out-of-range indices resolve to
        /// <see cref="Linear"/> rather than throwing, because the caller is a UI control whose value
        /// can be stale by a rebuild.
        /// </summary>
        public static EasingPreset At(int index)
        {
            if (index < 0 || index >= presets.Length)
            {
                return presets[0];
            }
            return presets[index];
        }

        /// <summary>The dropdown index for a label, or −1 when the label is not one of ours.</summary>
        public static int IndexOfDisplayName(string displayName)
        {
            return displayNames.IndexOf(displayName);
        }

        /// <summary>
        /// The dropdown index a key sits on: the preset matching its mode and handles, or
        /// <see cref="CustomIndex"/> when its Bézier handles are shaped by hand.
        /// </summary>
        /// <remarks>
        /// Handles are only compared for Bézier keys. A fixed mode's stored handles are never read
        /// by the sampler, so a key left holding an old drag's handles is still exactly that mode —
        /// reporting it as custom would show a shape it does not play.
        /// </remarks>
        public static int IndexOf(Interpolation interpolation, float2 startHandle, float2 endHandle)
        {
            // An uninitialised handle pair is what the sampler reads as linear, so that is the
            // preset it is on. Calling it custom would name a shape nothing plays.
            if (interpolation == Interpolation.Bezier
                && math.all(startHandle == float2.zero) && math.all(endHandle == float2.zero))
            {
                return 0;
            }

            for (int presetIndex = 0; presetIndex < presets.Length; presetIndex++)
            {
                EasingPreset preset = presets[presetIndex];
                if (preset.interpolation != interpolation)
                {
                    continue;
                }
                if (interpolation != Interpolation.Bezier)
                {
                    return presetIndex;
                }
                if (Matches(preset.startHandle, startHandle) && Matches(preset.endHandle, endHandle))
                {
                    return presetIndex;
                }
            }
            return CustomIndex;
        }

        /// <summary>The label <see cref="IndexOf"/> resolves to, for a field that shows text.</summary>
        public static string DisplayNameOf(
            Interpolation interpolation, float2 startHandle, float2 endHandle)
        {
            return displayNames[IndexOf(interpolation, startHandle, endHandle)];
        }

        /// <summary>
        /// The cubic handles that draw a given fixed mode, for widgets that plot every mode on one
        /// pair of grab dots. Bézier keeps whatever handles it was authored with, so it is the
        /// caller's own values that come back.
        /// </summary>
        public static void HandlesFor(
            Interpolation interpolation, out float2 startHandle, out float2 endHandle)
        {
            for (int presetIndex = 0; presetIndex < presets.Length; presetIndex++)
            {
                if (presets[presetIndex].interpolation != interpolation)
                {
                    continue;
                }
                startHandle = presets[presetIndex].startHandle;
                endHandle = presets[presetIndex].endHandle;
                return;
            }
            startHandle = LinearStartHandle;
            endHandle = LinearEndHandle;
        }

        private static bool Matches(float2 expected, float2 actual)
        {
            return math.all(math.abs(expected - actual) <= MatchTolerance);
        }

        private static List<string> BuildDisplayNames()
        {
            List<string> names = new List<string>(presets.Length + 1);
            for (int presetIndex = 0; presetIndex < presets.Length; presetIndex++)
            {
                names.Add(presets[presetIndex].displayName);
            }
            names.Add(CustomDisplayName);
            return names;
        }
    }
}
