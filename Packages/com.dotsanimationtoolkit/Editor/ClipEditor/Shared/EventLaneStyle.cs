// Copyright (c) 2026 Spencer Park. All rights reserved.

using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Shared pin and window drawing for an event lane, lifted out of TrackLaneElement so the cutscene moment lane can render the same shape.</summary>
    public static class EventLaneStyle
    {
        public const float PinHalfWidth = 5f;
        public const float PinHalfHeight = TimelineGeometry.KeyDrawRadius * 1.35f;
        public const float PinShoulderFraction = 0.2f;
        public const float PinHitHalfWidth = PinHalfWidth + 2f;

        public static readonly Color PinOutline = new Color(0.08f, 0.08f, 0.09f);

        public static void DrawPin(Painter2D painter, float centreX, float centreY, Color fill, bool selected)
        {
            if (selected)
            {
                DrawPin(painter, centreX, centreY, fill, ToolkitPalette.Selected, 2f);
            }
            else
            {
                DrawPin(painter, centreX, centreY, fill, PinOutline, 1f);
            }
        }

        // Flat shoulders tapering to a single point at the exact key time, so an event reads as
        // obviously not-a-pose-key.
        public static void DrawPin(Painter2D painter, float centreX, float centreY, Color fill, Color outline, float outlineWidth)
        {
            float shoulderY = centreY - PinHalfHeight;
            float taperStartY = centreY + PinHalfHeight * PinShoulderFraction;
            float tipY = centreY + PinHalfHeight;
            painter.strokeColor = outline;
            painter.lineWidth = outlineWidth;
            painter.fillColor = fill;
            painter.BeginPath();
            painter.MoveTo(new Vector2(centreX - PinHalfWidth, shoulderY));
            painter.LineTo(new Vector2(centreX + PinHalfWidth, shoulderY));
            painter.LineTo(new Vector2(centreX + PinHalfWidth, taperStartY));
            painter.LineTo(new Vector2(centreX, tipY));
            painter.LineTo(new Vector2(centreX - PinHalfWidth, taperStartY));
            painter.ClosePath();
            painter.Fill();
            painter.Stroke();
        }

        public static void DrawWindow(Painter2D painter, float startX, float endX, float centreY, float halfHeight, Color eventColor)
        {
            if (endX <= startX || halfHeight <= 0f)
            {
                return;
            }
            painter.fillColor = new Color(eventColor.r, eventColor.g, eventColor.b, 0.30f);
            painter.BeginPath();
            painter.MoveTo(new Vector2(startX, centreY - halfHeight));
            painter.LineTo(new Vector2(endX, centreY - halfHeight));
            painter.LineTo(new Vector2(endX, centreY + halfHeight));
            painter.LineTo(new Vector2(startX, centreY + halfHeight));
            painter.ClosePath();
            painter.Fill();
        }
    }
}
