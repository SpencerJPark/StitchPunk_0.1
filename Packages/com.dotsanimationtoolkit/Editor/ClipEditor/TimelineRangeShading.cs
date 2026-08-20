// Copyright (c) 2026 Spencer Park. All rights reserved.

using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Shades the timeline outside the clip and marks where the clip begins and ends.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Shared by the ruler and every lane on purpose.</strong> Two elements drawing "where
    /// the clip ends" from their own arithmetic is the same class of bug <c>TimelineGeometry</c>
    /// exists to prevent — a boundary line one pixel from the shading it bounds looks like a
    /// rendering fault and hides a real disagreement about the clip extent.
    /// </para>
    /// <para>
    /// Keys outside the clip still paint, select and drag normally. The shading says "this time is
    /// past the end", not "nothing here is real" — which matters because scaling keys past the end
    /// is a legitimate thing to do and then undo.
    /// </para>
    /// </remarks>
    internal static class TimelineRangeShading
    {
        /// <summary>Darker than any lane background, so it reads as outside rather than as a stripe.</summary>
        private static readonly Color OutOfRangeFill = new Color(0f, 0f, 0f, 0.30f);

        /// <summary>The clip boundary. Warm, so it cannot be mistaken for the blue playhead.</summary>
        private static readonly Color BoundaryLine = new Color(0.85f, 0.62f, 0.28f, 0.9f);

        /// <summary>
        /// Fills everything before normalized time 0 and after 1, then rules both boundaries.
        /// </summary>
        internal static void Paint(Painter2D painter, TimelineGeometry geometry, Rect rect)
        {
            float clipStartX = geometry.TimeToX(0f);
            float clipEndX = geometry.TimeToX(1f);

            painter.fillColor = OutOfRangeFill;
            FillSpan(painter, 0f, Mathf.Min(clipStartX, rect.width), rect.height);
            FillSpan(painter, Mathf.Max(0f, clipEndX), rect.width, rect.height);

            painter.strokeColor = BoundaryLine;
            painter.lineWidth = 1f;
            StrokeBoundary(painter, clipStartX, rect);
            StrokeBoundary(painter, clipEndX, rect);
        }

        private static void FillSpan(Painter2D painter, float fromX, float toX, float height)
        {
            if (toX - fromX <= 0.5f)
            {
                return;
            }
            painter.BeginPath();
            painter.MoveTo(new Vector2(fromX, 0f));
            painter.LineTo(new Vector2(toX, 0f));
            painter.LineTo(new Vector2(toX, height));
            painter.LineTo(new Vector2(fromX, height));
            painter.ClosePath();
            painter.Fill();
        }

        private static void StrokeBoundary(Painter2D painter, float x, Rect rect)
        {
            // Skipped when off screen: a line clamped to the edge would claim the clip ends at the
            // edge of the view, which is exactly the lie the unclamped converter was fixed to stop.
            if (x < -1f || x > rect.width + 1f)
            {
                return;
            }
            float pixelCentre = Mathf.Round(x) + 0.5f;
            painter.BeginPath();
            painter.MoveTo(new Vector2(pixelCentre, 0f));
            painter.LineTo(new Vector2(pixelCentre, rect.height));
            painter.Stroke();
        }
    }
}
