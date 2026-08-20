// Copyright (c) 2026 Stitch Punk. All rights reserved.

using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// The one place that converts between clip time and timeline pixels.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>This exists to make one specific bug unrepresentable.</strong> The audit found the
    /// host's IMGUI timeline drew keys with one rect calculation and hit-tested them with another,
    /// so they drifted apart under zoom and scroll: keys that could be seen but not grabbed, and
    /// grab targets floating beside the diamond they belonged to. Painting and hit-testing both go
    /// through here, so a change to one is a change to both by construction.
    /// </para>
    /// <para>
    /// Times are <em>normalized</em> (0..1 across the clip), matching how keys are authored, so
    /// zoom and clip duration are independent — re-timing a clip does not move any key.
    /// </para>
    /// </remarks>
    public struct TimelineGeometry
    {
        /// <summary>Width of the lane in pixels.</summary>
        public float laneWidth;

        /// <summary>Left inset before normalized time 0, in pixels.</summary>
        public float leftPadding;

        /// <summary>Right inset after normalized time 1, in pixels.</summary>
        public float rightPadding;

        /// <summary>Half-width of a key's grab box, in pixels.</summary>
        public const float KeyHitRadius = 7f;

        /// <summary>Half-width of the drawn key diamond, in pixels.</summary>
        public const float KeyDrawRadius = 5f;

        public static TimelineGeometry Create(float laneWidth)
        {
            return new TimelineGeometry
            {
                laneWidth = laneWidth,
                leftPadding = 12f,
                rightPadding = 12f
            };
        }

        /// <summary>Pixels spanned by the full 0..1 range.</summary>
        public float TrackPixelWidth
        {
            get { return Mathf.Max(1f, laneWidth - leftPadding - rightPadding); }
        }

        /// <summary>Normalized time to local x.</summary>
        public float TimeToX(float normalizedTime)
        {
            return leftPadding + Mathf.Clamp01(normalizedTime) * TrackPixelWidth;
        }

        /// <summary>Local x to normalized time, clamped into the clip.</summary>
        public float XToTime(float x)
        {
            return Mathf.Clamp01((x - leftPadding) / TrackPixelWidth);
        }

        /// <summary>Whether a pointer at <paramref name="x"/> is grabbing a key at that time.</summary>
        public bool HitsKey(float x, float normalizedTime)
        {
            return Mathf.Abs(x - TimeToX(normalizedTime)) <= KeyHitRadius;
        }

        /// <summary>
        /// Snaps a normalized time to a frame grid.
        /// </summary>
        /// <remarks>
        /// Snapping in normalized space rather than seconds keeps a key on the same frame when a
        /// clip's duration changes, which is what an animator expects from a re-time.
        /// </remarks>
        public static float Snap(float normalizedTime, int frameCount)
        {
            if (frameCount <= 0)
            {
                return Mathf.Clamp01(normalizedTime);
            }
            return Mathf.Clamp01(Mathf.Round(normalizedTime * frameCount) / frameCount);
        }
    }
}
