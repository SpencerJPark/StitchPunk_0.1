// Copyright (c) 2026 Spencer Park. All rights reserved.

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
    /// <para>
    /// The view transform (<see cref="zoom"/>, <see cref="panNormalized"/>) lives here rather than
    /// being applied by each element, for the same reason the conversions do: a lane that zoomed
    /// its painting and not its hit-testing would reintroduce exactly the drift this type exists to
    /// prevent. Elements are handed the view by the window; nothing derives its own.
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

        /// <summary>
        /// How much of the clip the track area spans. 1 fits the whole clip; 2 shows half of it.
        /// </summary>
        public float zoom;

        /// <summary>
        /// The normalized time sitting at the left edge of the track area. Negative scrolls before
        /// the clip start, which is how out-of-range keys stay reachable.
        /// </summary>
        public float panNormalized;

        /// <summary>The smallest and largest zoom the view will accept.</summary>
        public const float MinimumZoom = 0.25f;
        public const float MaximumZoom = 200f;

        public static TimelineGeometry Create(float laneWidth)
        {
            return Create(laneWidth, 1f, 0f);
        }

        public static TimelineGeometry Create(float laneWidth, float zoom, float panNormalized)
        {
            return new TimelineGeometry
            {
                laneWidth = laneWidth,
                leftPadding = 12f,
                rightPadding = 12f,
                zoom = Mathf.Clamp(zoom <= 0f ? 1f : zoom, MinimumZoom, MaximumZoom),
                panNormalized = panNormalized
            };
        }

        /// <summary>Pixels spanned by the full 0..1 range.</summary>
        public float TrackPixelWidth
        {
            get { return Mathf.Max(1f, laneWidth - leftPadding - rightPadding); }
        }

        /// <summary>Pixels spanned by one unit of normalized time at the current zoom.</summary>
        public float PixelsPerNormalizedUnit
        {
            get { return TrackPixelWidth * zoom; }
        }

        /// <summary>
        /// Normalized time to local x.
        /// </summary>
        /// <remarks>
        /// <strong>Deliberately unclamped.</strong> It used to clamp to [0, 1], which quietly piled
        /// every key past the clip end onto the same pixel as the last in-range one — they could be
        /// seen only as one diamond, and grabbing it got whichever the hit test found first. A key
        /// outside the clip is real authored data and has a real position; the view is what decides
        /// whether that position is currently on screen.
        /// </remarks>
        public float TimeToX(float normalizedTime)
        {
            return leftPadding + (normalizedTime - panNormalized) * PixelsPerNormalizedUnit;
        }

        /// <summary>
        /// Local x to normalized time. Unclamped, so dragging past either end of the clip reads as
        /// the time it actually points at rather than sticking at the boundary.
        /// </summary>
        public float XToTime(float x)
        {
            return (x - leftPadding) / PixelsPerNormalizedUnit + panNormalized;
        }

        /// <summary>
        /// The pan that puts <paramref name="normalizedTime"/> under <paramref name="anchorX"/>.
        /// </summary>
        /// <remarks>
        /// Zooming toward the cursor is this, applied after the zoom changes: work out what time was
        /// under the pointer, then choose the pan that leaves it there. Zooming toward the view
        /// centre instead makes the thing being examined drift away as it grows, which is why every
        /// tool that gets this right anchors on the cursor.
        /// </remarks>
        public float PanToAnchor(float normalizedTime, float anchorX)
        {
            return normalizedTime - (anchorX - leftPadding) / PixelsPerNormalizedUnit;
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
                return normalizedTime;
            }
            // Unclamped for the same reason TimeToX is: an out-of-range key snaps to the frame grid
            // extended past the clip, not back to the clip's last frame.
            return Mathf.Round(normalizedTime * frameCount) / frameCount;
        }
    }
}
