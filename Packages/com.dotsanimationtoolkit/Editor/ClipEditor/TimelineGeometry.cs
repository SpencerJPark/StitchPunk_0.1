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

        /// <summary>
        /// Fully zoomed out: the clip spans 15% of the track, so roughly six clip lengths of empty
        /// timeline sit around it.
        /// </summary>
        /// <remarks>
        /// Pulling this far back is not for reading keys, it is for finding them — a key dragged
        /// well past either end of the clip is still authored data, and this is the view that has
        /// it on screen. Fitting the clip is what Frame All is for, and it lands near the middle of
        /// the slider's travel rather than at its end.
        /// </remarks>
        public const float MinimumZoom = 0.15f;

        /// <summary>
        /// The structural ceiling, not the one a user meets. Zoom divides the track width, so a
        /// runaway value is a division by something near zero; this is the clamp that keeps the
        /// geometry finite. What the slider and Ctrl+scroll actually stop at is the per-clip
        /// ceiling from <see cref="MaximumZoomForFrameCount"/>, which is always smaller.
        /// </summary>
        public const float MaximumZoom = 200f;

        /// <summary>How many frames fill the track when zoomed all the way in.</summary>
        /// <remarks>
        /// The zoom-in limit is a count of frames rather than a fixed multiplier because that is
        /// what the limit is for: keeping keys far enough apart to grab individually. A fixed 20x
        /// meant a 30-frame clip zoomed to a frame and a half — a view of one key and no context —
        /// while a 600-frame clip could not get close enough to separate its keys at all.
        /// </remarks>
        public const float VisibleFramesAtMaximumZoom = 20f;

        /// <summary>
        /// The zoom that fits <see cref="VisibleFramesAtMaximumZoom"/> frames across the track.
        /// </summary>
        /// <remarks>
        /// Floored at 1 so a clip shorter than that count still zooms in to fill the view rather
        /// than being pinned below it, and capped at <see cref="MaximumZoom"/> so an absurd frame
        /// count cannot walk past the structural clamp.
        /// </remarks>
        public static float MaximumZoomForFrameCount(int frameCount)
        {
            return Mathf.Clamp(frameCount / VisibleFramesAtMaximumZoom, 1f, MaximumZoom);
        }

        /// <summary>
        /// Builds a converter for one width and one view.
        /// </summary>
        /// <remarks>
        /// <strong>There is deliberately no overload that defaults the view.</strong> There was one,
        /// and it read as a convenience while quietly meaning "zoom 1, pan 0". Two separate bugs
        /// came from calling it by mistake — key dragging converted the cursor against an unzoomed
        /// timeline, and box selection tested the band against where keys would be if the view had
        /// never moved. Both looked correct at the default view, which is exactly what made them
        /// survive. Requiring the view at every call site turns that mistake into a compile error.
        /// </remarks>
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

        /// <summary>
        /// The largest frame step from the 1-2-5 ladder whose spacing is still at least
        /// <paramref name="minimumSpacingPixels"/> wide.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>Derived from zoom, never assumed.</strong> The ruler used to step ticks by a
        /// fixed fraction of the clip and thin them by doubling, which meant the interval had no
        /// relationship to what a frame was worth in pixels: zoomed out the labels overlapped into
        /// a smear, and past 240 frames the ticks stopped landing on real frames at all.
        /// </para>
        /// <para>
        /// The ladder is 1, 2, 5, 10, 20, 50, 100 and so on, because those are the intervals the eye
        /// reads without arithmetic. Doubling gives 8s and 16s, which are legible as marks but not
        /// as numbers.
        /// </para>
        /// </remarks>
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

        /// <summary>
        /// The first multiple of <paramref name="step"/> at or before <paramref name="frame"/>.
        /// </summary>
        /// <remarks>
        /// Floor division rather than truncation, so the grid stays aligned through frame zero
        /// instead of mirroring around it — truncating would put ticks at -5 and -2 rather than -5
        /// and -10, and the negative half of the ruler is exactly what this now has to draw.
        /// </remarks>
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

        /// <summary>
        /// Whether a pointer at <paramref name="x"/> is grabbing a key at that time, against a
        /// caller-supplied grab box rather than the default.
        /// </summary>
        /// <remarks>
        /// A bounding-box test in x alone, not a shape test: the lane's height already bounds y,
        /// so nothing upstream ever hands this a y to check. That is exactly what lets one shape's
        /// grab box widen — a pentagon marker's, say — without the two-dimensional hit-testing a
        /// true polygon test would need.
        /// </remarks>
        public bool HitsKey(float x, float normalizedTime, float hitRadius)
        {
            return Mathf.Abs(x - TimeToX(normalizedTime)) <= hitRadius;
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
