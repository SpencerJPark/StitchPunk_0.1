// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// The seconds-tick strip above a cutscene's lanes, and the scrub surface for its playhead.
    /// </summary>
    /// <remarks>See <see cref="CutsceneTimelineGeometry"/>'s remarks for why this is a fresh, seconds-based sibling of <see cref="TimeRulerElement"/> rather than a reuse of it (decision G-D2).</remarks>
    public sealed class CutsceneTimelineRulerElement : VisualElement
    {
        private static readonly Color RulerBackground = new Color(0.15f, 0.15f, 0.16f);
        private static readonly Color MajorTick = new Color(0.70f, 0.70f, 0.72f);
        private static readonly Color MinorTick = new Color(0.38f, 0.38f, 0.40f);

        private const float MinimumLabelSpacingPixels = 46f;
        private const float MinimumMinorSpacingPixels = 6f;

        public const string UssClassName = "cutscene-editor__ruler";

        /// <summary>Pixels per second, pushed in by the panel so every lane and the ruler agree.</summary>
        public float pixelsPerSecond = 40f;

        /// <summary>How far past the last authored time the ruler still draws, so there is always room to drop a new item.</summary>
        public float trailingSeconds = 5f;

        /// <summary>The furthest time anything on the timeline currently reaches.</summary>
        public float contentEndSeconds = 10f;

        /// <summary>Raised when the pointer scrubs the ruler, with the raw seconds it landed on.</summary>
        public event Action<float> Scrubbed;

        public CutsceneTimelineRulerElement()
        {
            AddToClassList(UssClassName);
            generateVisualContent += OnGenerateVisualContent;
            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
        }

        /// <summary>Total content width the ruler (and every lane beside it) should reserve, in pixels.</summary>
        public float ContentWidth
        {
            get
            {
                CutsceneTimelineGeometry geometry = CutsceneTimelineGeometry.Create(pixelsPerSecond);
                return geometry.TimeToX(contentEndSeconds + trailingSeconds);
            }
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
            CutsceneTimelineGeometry geometry = CutsceneTimelineGeometry.Create(pixelsPerSecond);
            Scrubbed?.Invoke(geometry.XToTime(localX));
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

            CutsceneTimelineGeometry geometry = CutsceneTimelineGeometry.Create(pixelsPerSecond);
            float labelStep = CutsceneTimelineGeometry.ChooseSecondsStep(
                geometry.pixelsPerSecond, MinimumLabelSpacingPixels);
            float minorStep = ChooseMinorStep(labelStep, geometry.pixelsPerSecond);

            float lastVisibleSeconds = geometry.XToTime(rect.width);
            float second = FloorToStep(0f, minorStep);

            const int MaximumTicks = 2048;
            int drawn = 0;
            painter.lineWidth = 1f;
            while (second <= lastVisibleSeconds && drawn < MaximumTicks)
            {
                bool isMajor = IsApproximateMultiple(second, labelStep);
                float x = Mathf.Round(geometry.TimeToX(second)) + 0.5f;
                painter.strokeColor = isMajor ? MajorTick : MinorTick;
                painter.BeginPath();
                painter.MoveTo(new Vector2(x, isMajor ? rect.height * 0.35f : rect.height * 0.65f));
                painter.LineTo(new Vector2(x, rect.height));
                painter.Stroke();

                second += minorStep;
                drawn++;
            }

            RefreshLabels(geometry, labelStep, rect.width);
        }

        private void RefreshLabels(CutsceneTimelineGeometry geometry, float labelStep, float width)
        {
            // Rebuilding the label children from a repaint callback mutates the visual tree from
            // inside generateVisualContent, which TimeRulerElement's own history already flagged as
            // unsafe (labels smear on rapid zoom). Deferred to the next editor tick instead.
            schedule.Execute(() => RebuildLabelChildren(geometry, labelStep, width));
        }

        private void RebuildLabelChildren(CutsceneTimelineGeometry geometry, float labelStep, float width)
        {
            Clear();
            float lastVisibleSeconds = geometry.XToTime(width);
            float second = FloorToStep(0f, labelStep);

            const int MaximumLabels = 256;
            int created = 0;
            while (second <= lastVisibleSeconds && created < MaximumLabels)
            {
                Label marker = new Label(second.ToString("0.##") + "s");
                marker.pickingMode = PickingMode.Ignore;
                marker.style.position = Position.Absolute;
                marker.style.left = geometry.TimeToX(second) + 2f;
                marker.style.top = 0f;
                marker.style.fontSize = 9f;
                marker.style.color = MajorTick;
                Add(marker);

                second += labelStep;
                created++;
            }
        }

        private static float ChooseMinorStep(float labelStep, float pixelsPerSecond)
        {
            float fifth = labelStep / 5f;
            if (fifth * pixelsPerSecond >= MinimumMinorSpacingPixels)
            {
                return fifth;
            }
            float half = labelStep / 2f;
            if (half * pixelsPerSecond >= MinimumMinorSpacingPixels)
            {
                return half;
            }
            return labelStep;
        }

        private static float FloorToStep(float value, float step)
        {
            if (step <= 0f)
            {
                return value;
            }
            return Mathf.Floor(value / step) * step;
        }

        private static bool IsApproximateMultiple(float value, float step)
        {
            if (step <= 0f)
            {
                return true;
            }
            float remainder = value / step - Mathf.Round(value / step);
            return Mathf.Abs(remainder) < 0.001f;
        }
    }
}
