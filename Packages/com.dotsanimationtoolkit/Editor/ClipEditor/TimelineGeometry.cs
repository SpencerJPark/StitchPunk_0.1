// Copyright (c) 2026 Spencer Park. All rights reserved.

using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// The one place that converts between clip time and timeline pixels, so painting and
    /// hit-testing can never drift apart. Times are normalized (0..1 across the clip), independent
    /// of zoom and clip duration; the view transform lives here so no element derives its own.
    /// </summary>
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

        // Fully zoomed out: the clip spans 15% of the track, so roughly six clip lengths of empty
        // timeline sit around it — for finding a key dragged well past either end, not for reading it.
        public const float MinimumZoom = 0.15f;

        // The structural ceiling, not the one a user meets: zoom divides the track width, so this
        // keeps the geometry finite. The slider and Ctrl+scroll actually stop at the smaller
        // per-clip ceiling from MaximumZoomForFrameCount.
        public const float MaximumZoom = 200f;

        // How many frames fill the track when zoomed all the way in. A count of frames rather than
        // a fixed multiplier, since a fixed 20x meant a 30-frame clip zoomed to a frame and a half.
        public const float VisibleFramesAtMaximumZoom = 20f;

        /// <summary>The zoom that fits <see cref="VisibleFramesAtMaximumZoom"/> frames across the track.</summary>
        public static float MaximumZoomForFrameCount(int frameCount)
        {
            return Mathf.Clamp(frameCount / VisibleFramesAtMaximumZoom, 1f, MaximumZoom);
        }

        // Builds a converter for one width and one view. Deliberately no overload that defaults the
        // view: one used to exist and quietly meant "zoom 1, pan 0", producing bugs that looked
        // correct only at the default view.
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

        // Normalized time to local x. Deliberately unclamped: clamping to [0, 1] used to pile every
        // key past the clip end onto the same pixel as the last in-range one.
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

        /// <summary>The pan that puts <paramref name="normalizedTime"/> under <paramref name="anchorX"/>.</summary>
        public float PanToAnchor(float normalizedTime, float anchorX)
        {
            return normalizedTime - (anchorX - leftPadding) / PixelsPerNormalizedUnit;
        }

        // The largest frame step from the 1-2-5 ladder whose spacing is still at least
        // minimumSpacingPixels wide — derived from zoom, never assumed, since those intervals are
        // the ones the eye reads without arithmetic.
        public static int ChooseFrameStep(float pixelsPerFrame, float minimumSpacingPixels)
        {
            if (pixelsPerFrame <= 0f || minimumSpacingPixels <= 0f)
            {
                return 1;
            }

            int mantissaIndex = 0;
            int decade = 1;
            int step = 1;
            while (step * pixelsPerFrame < minimumSpacingPixels)
            {
                mantissaIndex++;
                if (mantissaIndex >= 3)
                {
                    mantissaIndex = 0;
                    decade *= 10;
                }
                // A ruler zoomed this far out has no useful numbering left; stop rather than
                // overflow looking for a step that will never be wide enough.
                if (decade > 1000000)
                {
                    break;
                }
                step = MantissaAt(mantissaIndex) * decade;
            }
            return Mathf.Max(1, step);
        }

        private static int MantissaAt(int mantissaIndex)
        {
            switch (mantissaIndex)
            {
                case 1:
                    return 2;
                case 2:
                    return 5;
                default:
                    return 1;
            }
        }

        /// <summary>
        /// The largest whole division of <paramref name="labelStep"/> that still reads as separate
        /// ticks, or <paramref name="labelStep"/> itself when even halves would be too dense.
        /// </summary>
        public static int ChooseMinorFrameStep(
            int labelStep, float pixelsPerFrame, float minimumSpacingPixels)
        {
            int fifth = labelStep / 5;
            if (fifth >= 1 && fifth * pixelsPerFrame >= minimumSpacingPixels)
            {
                return fifth;
            }
            int half = labelStep / 2;
            if (half >= 1 && half * pixelsPerFrame >= minimumSpacingPixels)
            {
                return half;
            }
            return labelStep;
        }

        // The first multiple of step at or before frame. Floor division rather than truncation, so
        // the grid stays aligned through frame zero instead of mirroring around it.
        public static int FloorToStep(float frame, int step)
        {
            if (step <= 0)
            {
                return Mathf.FloorToInt(frame);
            }
            return Mathf.FloorToInt(frame / step) * step;
        }

        /// <summary>Whether a pointer at <paramref name="x"/> is grabbing a key at that time.</summary>
        public bool HitsKey(float x, float normalizedTime)
        {
            return HitsKey(x, normalizedTime, KeyHitRadius);
        }

        // Whether a pointer at x is grabbing a key at that time, against a caller-supplied grab box.
        // A bounding-box test in x alone: the lane's height already bounds y.
        public bool HitsKey(float x, float normalizedTime, float hitRadius)
        {
            return Mathf.Abs(x - TimeToX(normalizedTime)) <= hitRadius;
        }

        // Snaps a normalized time to a frame grid. Snapping in normalized space rather than seconds
        // keeps a key on the same frame when a clip's duration changes.
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
