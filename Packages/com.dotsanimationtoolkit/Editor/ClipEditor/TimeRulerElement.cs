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
            RaiseScrub(pointerEvent.localPosition.x);
            pointerEvent.StopPropagation();
        }

        private void OnPointerMove(PointerMoveEvent moveEvent)
        {
            if (!this.HasPointerCapture(moveEvent.pointerId))
            {
                return;
            }
            RaiseScrub(moveEvent.localPosition.x);
        }

        private void OnPointerUp(PointerUpEvent upEvent)
        {
            this.ReleasePointer(upEvent.pointerId);
        }

        private void RaiseScrub(float localX)
        {
            if (scrubbed == null)
            {
                return;
            }
            scrubbed(TimelineGeometry.Create(contentRect.width).XToTime(localX));
        }

        private void OnGenerateVisualContent(MeshGenerationContext context)
        {
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

            TimelineGeometry geometry = TimelineGeometry.Create(rect.width);
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
