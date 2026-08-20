// Copyright (c) 2026 Spencer Park. All rights reserved.

using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// The tick strip above the lanes (architecture section 7.2).
    /// </summary>
    /// <remarks>
    /// Ticks come from <see cref="TimelineGeometry"/>, the same converter the lanes and playhead
    /// use, so a tick labelled 0.5s sits exactly where a key at 0.5s is drawn. A ruler with its own
    /// maths is the classic way for a timeline to start lying about where things are.
    /// </remarks>
    public sealed class TimeRulerElement : VisualElement
    {
        private static readonly Color RulerBackground = new Color(0.15f, 0.15f, 0.16f);
        private static readonly Color MajorTick = new Color(0.70f, 0.70f, 0.72f);
        private static readonly Color MinorTick = new Color(0.38f, 0.38f, 0.40f);

        /// <summary>Clip length in seconds, used only for the labels.</summary>
        public float durationSeconds = 1f;

        /// <summary>Number of frames spanning the clip; also the minor tick count.</summary>
        public int frameCount = 30;

        /// <summary>
        /// The timeline view, pushed in by the window. Never derived here: a lane that computed its
        /// own zoom would drift from the ruler's, which is the bug TimelineGeometry exists to stop.
        /// </summary>
        public float viewZoom = 1f;
        public float viewPan;


        /// <summary>Raised when the pointer scrubs the ruler, with the normalized time.</summary>
        public event System.Action<float> scrubbed;

        /// <summary>
        /// Height and shrink come from ClipEditorWindow.uss, which pairs this element's height with
        /// the spacer above the track headers. Inline styles would win over that rule and put the
        /// two stacks one row out of step with no way to correct it from the stylesheet.
        /// </summary>
        public const string UssClassName = "clip-editor__ruler";

        public TimeRulerElement()
        {
            AddToClassList(UssClassName);
            generateVisualContent += OnGenerateVisualContent;
            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
        }

        private void OnPointerDown(PointerDownEvent pointerEvent)
        {
            this.CapturePointer(pointerEvent.pointerId);
            RaiseScrub(pointerEvent.localPosition.x, pointerEvent.altKey);
            pointerEvent.StopPropagation();
        }

        private void OnPointerMove(PointerMoveEvent moveEvent)
        {
            if (!this.HasPointerCapture(moveEvent.pointerId))
            {
                return;
            }
            RaiseScrub(moveEvent.localPosition.x, moveEvent.altKey);
        }

        private void OnPointerUp(PointerUpEvent upEvent)
        {
            this.ReleasePointer(upEvent.pointerId);
        }

        /// <summary>
        /// Reports a scrub, snapped to the frame grid unless the caller asks for a free scrub.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>Snapped by default.</strong> A frame is the unit the clip is actually evaluated
        /// at, so an unsnapped playhead shows a pose between two frames — a pose the game will never
        /// display. Landing on frames by default means what the viewport shows is a frame that
        /// exists.
        /// </para>
        /// <para>
        /// <strong>Alt scrubs freely</strong>, not Shift: Shift already means "larger step" on the
        /// arrow keys, and one modifier meaning two things in the same window is how a shortcut
        /// stops being learnable. Alt is Unity's usual "ignore the grid" modifier.
        /// </para>
        /// </remarks>
        private void RaiseScrub(float localX, bool freeScrub)
        {
            if (scrubbed == null)
            {
                return;
            }

            float normalizedTime = TimelineGeometry.Create(contentRect.width, viewZoom, viewPan).XToTime(localX);
            if (!freeScrub)
            {
                int frames = Mathf.Max(1, frameCount);
                normalizedTime = Mathf.Clamp01(Mathf.Round(normalizedTime * frames) / frames);
            }
            scrubbed(normalizedTime);
        }

        /// <summary>
        /// Rebuilds the whole-second labels along the ruler.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Child labels rather than painted text: <c>Painter2D</c> draws geometry, and a ruler that
        /// had to rasterise its own glyphs would be a font renderer with a timeline attached.
        /// </para>
        /// <para>
        /// Labels are thinned so they never collide — a ten-second clip in a narrow pane gets a
        /// label every two or five seconds instead of an unreadable smear. The ticks stay dense;
        /// only the numbering thins, so the grid is still legible where the text is not.
        /// </para>
        /// </remarks>
        private void RebuildSecondLabels()
        {
            Clear();

            float width = contentRect.width;
            if (width <= 0f || durationSeconds <= 0f)
            {
                return;
            }

            int wholeSeconds = Mathf.FloorToInt(durationSeconds);
            if (wholeSeconds < 1)
            {
                return;
            }

            const float MinimumLabelSpacingPixels = 44f;
            int secondStride = 1;
            TimelineGeometry geometry = TimelineGeometry.Create(width, viewZoom, viewPan);
            while (geometry.TrackPixelWidth / (durationSeconds / secondStride) < MinimumLabelSpacingPixels)
            {
                secondStride = secondStride == 1 ? 2 : secondStride + (secondStride == 2 ? 3 : 5);
                if (secondStride > 600)
                {
                    return;
                }
            }

            for (int second = 0; second <= wholeSeconds; second += secondStride)
            {
                Label marker = new Label(second.ToString() + "s");
                marker.pickingMode = PickingMode.Ignore;
                marker.style.position = Position.Absolute;
                marker.style.left = geometry.TimeToX(second / durationSeconds) + 2f;
                marker.style.top = 0f;
                marker.style.fontSize = 9f;
                marker.style.color = MajorTick;
                Add(marker);
            }
        }

        private void OnGenerateVisualContent(MeshGenerationContext context)
        {
            RebuildSecondLabels();

            Rect rect = contentRect;
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            Painter2D painter = context.painter2D;
            painter.fillColor = RulerBackground;
            painter.BeginPath();
            painter.MoveTo(new Vector2(0f, 0f));
            painter.LineTo(new Vector2(rect.width, 0f));
            painter.LineTo(new Vector2(rect.width, rect.height));
            painter.LineTo(new Vector2(0f, rect.height));
            painter.ClosePath();
            painter.Fill();

            TimelineGeometry geometry = TimelineGeometry.Create(rect.width, viewZoom, viewPan);
            int ticks = Mathf.Clamp(frameCount, 1, 240);

            // Thin out the minor ticks when frames would fall closer together than they can be
            // told apart, so a long clip degrades to a readable ruler instead of a grey bar.
            int stride = 1;
            while (geometry.TrackPixelWidth / (ticks / (float)stride) < 4f)
            {
                stride *= 2;
            }

            painter.lineWidth = 1f;
            for (int tickIndex = 0; tickIndex <= ticks; tickIndex += stride)
            {
                bool major = tickIndex == 0 || tickIndex == ticks || (tickIndex % 10) == 0;
                float x = Mathf.Round(geometry.TimeToX(tickIndex / (float)ticks)) + 0.5f;
                painter.strokeColor = major ? MajorTick : MinorTick;
                painter.BeginPath();
                painter.MoveTo(new Vector2(x, major ? rect.height * 0.35f : rect.height * 0.65f));
                painter.LineTo(new Vector2(x, rect.height));
                painter.Stroke();
            }
        }
    }
}
