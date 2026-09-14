using UnityEngine;
using UnityEngine.UIElements;

namespace WorktreeToolkit.Editor
{
    public enum WorktreeGlyph
    {
        None,
        BranchFork,
        Check,
        Cross,
        Spinner,
        Padlock,
        DashedRing
    }

    /// <summary>Vector glyph drawing for the toolkit window's Painter2D icons, replacing stretched built-in icons.</summary>
    public static class WorktreeGlyphs
    {
        private const int SpinnerSegmentCount = 28;
        private const float SpinnerSweepRadians = 1.5f * Mathf.PI;
        private const int PadlockShackleSegmentCount = 16;
        private const int DashedRingDashCount = 8;
        private const int DashedRingSegmentsPerDash = 5;
        private const float DashedRingDashSweepRadians = 25f * Mathf.Deg2Rad;

        public static void Draw(Painter2D painter, WorktreeGlyph glyph, Rect bounds, Color color, float strokeWidth, float spinnerAngleRadians)
        {
            if (glyph == WorktreeGlyph.None || bounds.width <= 0f || bounds.height <= 0f)
            {
                return;
            }

            float smallerSide = Mathf.Min(bounds.width, bounds.height);
            float centerX = bounds.x + bounds.width * 0.5f;
            float centerY = bounds.y + bounds.height * 0.5f;

            Vector2 ToPoint(float normalizedX, float normalizedY)
            {
                return new Vector2(bounds.x + normalizedX * bounds.width, bounds.y + normalizedY * bounds.height);
            }

            painter.strokeColor = color;
            painter.lineWidth = strokeWidth;
            painter.lineCap = LineCap.Round;
            painter.lineJoin = LineJoin.Round;

            switch (glyph)
            {
                case WorktreeGlyph.BranchFork:
                    DrawBranchFork(painter, ToPoint, smallerSide);
                    break;
                case WorktreeGlyph.Check:
                    DrawCheck(painter, ToPoint);
                    break;
                case WorktreeGlyph.Cross:
                    DrawCross(painter, ToPoint);
                    break;
                case WorktreeGlyph.Spinner:
                    DrawSpinner(painter, centerX, centerY, smallerSide, spinnerAngleRadians);
                    break;
                case WorktreeGlyph.Padlock:
                    DrawPadlock(painter, ToPoint, smallerSide);
                    break;
                case WorktreeGlyph.DashedRing:
                    DrawDashedRing(painter, centerX, centerY, smallerSide);
                    break;
            }
        }

        private static void DrawCircle(Painter2D painter, Vector2 center, float radius)
        {
            painter.BeginPath();
            painter.MoveTo(center + new Vector2(radius, 0f));
            const int circleSegmentCount = 16;
            for (int segmentIndex = 1; segmentIndex <= circleSegmentCount; segmentIndex++)
            {
                float angleRadians = segmentIndex / (float)circleSegmentCount * 2f * Mathf.PI;
                painter.LineTo(center + new Vector2(Mathf.Cos(angleRadians) * radius, Mathf.Sin(angleRadians) * radius));
            }
            painter.ClosePath();
            painter.Stroke();
        }

        private static void DrawBranchFork(Painter2D painter, System.Func<float, float, Vector2> toPoint, float smallerSide)
        {
            float circleRadius = 0.09f * smallerSide;
            DrawCircle(painter, toPoint(0.32f, 0.2f), circleRadius);
            DrawCircle(painter, toPoint(0.32f, 0.8f), circleRadius);
            DrawCircle(painter, toPoint(0.72f, 0.34f), circleRadius);

            painter.BeginPath();
            painter.MoveTo(toPoint(0.32f, 0.29f));
            painter.LineTo(toPoint(0.32f, 0.71f));
            painter.Stroke();

            painter.BeginPath();
            painter.MoveTo(toPoint(0.72f, 0.43f));
            painter.BezierCurveTo(toPoint(0.72f, 0.56f), toPoint(0.32f, 0.5f), toPoint(0.32f, 0.62f));
            painter.Stroke();
        }

        private static void DrawCheck(Painter2D painter, System.Func<float, float, Vector2> toPoint)
        {
            painter.BeginPath();
            painter.MoveTo(toPoint(0.2f, 0.53f));
            painter.LineTo(toPoint(0.42f, 0.74f));
            painter.LineTo(toPoint(0.8f, 0.3f));
            painter.Stroke();
        }

        private static void DrawCross(Painter2D painter, System.Func<float, float, Vector2> toPoint)
        {
            painter.BeginPath();
            painter.MoveTo(toPoint(0.26f, 0.26f));
            painter.LineTo(toPoint(0.74f, 0.74f));
            painter.Stroke();

            painter.BeginPath();
            painter.MoveTo(toPoint(0.74f, 0.26f));
            painter.LineTo(toPoint(0.26f, 0.74f));
            painter.Stroke();
        }

        private static void DrawSpinner(Painter2D painter, float centerX, float centerY, float smallerSide, float spinnerAngleRadians)
        {
            float radius = 0.32f * smallerSide;
            painter.BeginPath();
            for (int segmentIndex = 0; segmentIndex <= SpinnerSegmentCount; segmentIndex++)
            {
                float angleRadians = spinnerAngleRadians + SpinnerSweepRadians * (segmentIndex / (float)SpinnerSegmentCount);
                Vector2 point = new Vector2(centerX + Mathf.Cos(angleRadians) * radius, centerY + Mathf.Sin(angleRadians) * radius);
                if (segmentIndex == 0)
                {
                    painter.MoveTo(point);
                }
                else
                {
                    painter.LineTo(point);
                }
            }
            painter.Stroke();
        }

        private static void DrawPadlock(Painter2D painter, System.Func<float, float, Vector2> toPoint, float smallerSide)
        {
            float shackleRadius = 0.17f * smallerSide;
            Vector2 shackleCenter = toPoint(0.5f, 0.46f);

            painter.BeginPath();
            Vector2 shackleLeftTop = shackleCenter + new Vector2(-shackleRadius, 0f);
            painter.MoveTo(shackleLeftTop);
            for (int segmentIndex = 1; segmentIndex <= PadlockShackleSegmentCount; segmentIndex++)
            {
                float angleRadians = Mathf.PI + Mathf.PI * (segmentIndex / (float)PadlockShackleSegmentCount);
                Vector2 point = shackleCenter + new Vector2(Mathf.Cos(angleRadians) * shackleRadius, Mathf.Sin(angleRadians) * shackleRadius);
                painter.LineTo(point);
            }
            Vector2 shackleRightTop = shackleCenter + new Vector2(shackleRadius, 0f);
            painter.LineTo(new Vector2(shackleRightTop.x, toPoint(0.5f, 0.5f).y));
            painter.MoveTo(new Vector2(shackleLeftTop.x, toPoint(0.5f, 0.5f).y));
            painter.LineTo(shackleLeftTop);
            painter.Stroke();

            painter.BeginPath();
            painter.MoveTo(toPoint(0.26f, 0.5f));
            painter.LineTo(toPoint(0.74f, 0.5f));
            painter.LineTo(toPoint(0.74f, 0.86f));
            painter.LineTo(toPoint(0.26f, 0.86f));
            painter.ClosePath();
            painter.Stroke();

            painter.BeginPath();
            painter.MoveTo(toPoint(0.5f, 0.62f));
            painter.LineTo(toPoint(0.5f, 0.74f));
            painter.Stroke();
        }

        private static void DrawDashedRing(Painter2D painter, float centerX, float centerY, float smallerSide)
        {
            float radius = 0.32f * smallerSide;
            float fullCircleRadians = 2f * Mathf.PI;
            float dashSpacingRadians = fullCircleRadians / DashedRingDashCount;

            for (int dashIndex = 0; dashIndex < DashedRingDashCount; dashIndex++)
            {
                float dashStartAngleRadians = dashIndex * dashSpacingRadians;
                painter.BeginPath();
                for (int segmentIndex = 0; segmentIndex <= DashedRingSegmentsPerDash; segmentIndex++)
                {
                    float angleRadians = dashStartAngleRadians + DashedRingDashSweepRadians * (segmentIndex / (float)DashedRingSegmentsPerDash);
                    Vector2 point = new Vector2(centerX + Mathf.Cos(angleRadians) * radius, centerY + Mathf.Sin(angleRadians) * radius);
                    if (segmentIndex == 0)
                    {
                        painter.MoveTo(point);
                    }
                    else
                    {
                        painter.LineTo(point);
                    }
                }
                painter.Stroke();
            }
        }
    }

    public sealed class WorktreeGlyphElement : VisualElement
    {
        private WorktreeGlyph currentGlyph = WorktreeGlyph.None;
        private Color currentGlyphColor = Color.white;
        private float currentStrokeWidth = 3f;
        private float currentSpinnerAngleRadians;

        public WorktreeGlyphElement()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += meshGenerationContext =>
            {
                WorktreeGlyphs.Draw(meshGenerationContext.painter2D, currentGlyph, contentRect, currentGlyphColor, currentStrokeWidth, currentSpinnerAngleRadians);
            };
        }

        public WorktreeGlyph Glyph
        {
            get => currentGlyph;
            set
            {
                if (currentGlyph == value)
                {
                    return;
                }
                currentGlyph = value;
                MarkDirtyRepaint();
            }
        }

        public Color GlyphColor
        {
            get => currentGlyphColor;
            set
            {
                if (currentGlyphColor == value)
                {
                    return;
                }
                currentGlyphColor = value;
                MarkDirtyRepaint();
            }
        }

        public float StrokeWidth
        {
            get => currentStrokeWidth;
            set
            {
                if (Mathf.Approximately(currentStrokeWidth, value))
                {
                    return;
                }
                currentStrokeWidth = value;
                MarkDirtyRepaint();
            }
        }

        public float SpinnerAngleRadians
        {
            get => currentSpinnerAngleRadians;
            set
            {
                if (Mathf.Approximately(currentSpinnerAngleRadians, value))
                {
                    return;
                }
                currentSpinnerAngleRadians = value;
                MarkDirtyRepaint();
            }
        }
    }
}
