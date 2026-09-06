// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>A key's easing, drawn as a curve with draggable handles, sampled through <c>ClipSampler.Ease</c> so the shape matches playback.</summary>
    public sealed class EasingCurveEditorElement : VisualElement
    {
        public const string UssClassName = "clip-editor__easing-curve";

        private const int CurveSampleCount = 48;
        private const float HandleRadius = 5f;
        private const float HandleGrabRadius = 11f;
        private const float PresetHandleAlpha = 0.45f;

        private static readonly Color BackgroundColor = new Color(0.16f, 0.16f, 0.17f, 1f);
        private static readonly Color GridColor = new Color(0.28f, 0.28f, 0.30f, 1f);
        private static readonly Color CurveColor = new Color(0.55f, 0.80f, 1f, 1f);
        private static readonly Color HandleLineColor = new Color(0.60f, 0.60f, 0.64f, 1f);
        private static readonly Color StartHandleColor = new Color(0.45f, 0.85f, 0.50f, 1f);
        private static readonly Color EndHandleColor = new Color(0.95f, 0.65f, 0.35f, 1f);

        private Interpolation interpolation = Interpolation.Linear;
        private float2 startHandle = EasingPresets.LinearStartHandle;
        private float2 endHandle = EasingPresets.LinearEndHandle;

        private int draggingHandleIndex = -1;

        // Raised while a handle is dragged, with both handles' current values. The key becomes
        // Bezier by the act of dragging, so the listener writes that mode too; the event does not
        // carry it because the answer is never anything else.
        public event Action<float2, float2> curveEdited;

        public EasingCurveEditorElement()
        {
            AddToClassList(UssClassName);
            focusable = true;
            generateVisualContent += OnGenerateVisualContent;
            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
        }

        /// <summary>The easing mode the widget is currently drawing.</summary>
        public Interpolation CurrentInterpolation
        {
            get { return interpolation; }
        }

        // Sets the curve shown, without raising curveEdited. A fixed mode's handles come from its
        // preset, since the key's stored handles are unread in that mode and may hold a stale drag.
        public void SetCurveWithoutNotify(
            Interpolation newInterpolation, float2 newStartHandle, float2 newEndHandle)
        {
            interpolation = newInterpolation;

            if (newInterpolation != Interpolation.Bezier)
            {
                EasingPresets.HandlesFor(newInterpolation, out newStartHandle, out newEndHandle);
            }
            else if (math.all(newStartHandle == float2.zero) && math.all(newEndHandle == float2.zero))
            {
                newStartHandle = EasingPresets.LinearStartHandle;
                newEndHandle = EasingPresets.LinearEndHandle;
            }

            // Clamped to the unit square: x outside it makes the curve non-functional, and y outside
            // it is overshoot the bake's bounds union cannot account for.
            startHandle = math.clamp(newStartHandle, new float2(0f, 0f), new float2(1f, 1f));
            endHandle = math.clamp(newEndHandle, new float2(0f, 0f), new float2(1f, 1f));
            MarkDirtyRepaint();
        }

        /// <summary>The handles on screen, which a fixed mode's key does not itself store.</summary>
        public void GetHandles(out float2 currentStartHandle, out float2 currentEndHandle)
        {
            currentStartHandle = startHandle;
            currentEndHandle = endHandle;
        }

        private bool HasDraggableHandles
        {
            get { return interpolation != Interpolation.Step; }
        }

        private Vector2 CurveToLocal(float2 curvePoint, Rect rect)
        {
            return new Vector2(curvePoint.x * rect.width, (1f - curvePoint.y) * rect.height);
        }

        private float2 LocalToCurve(Vector2 localPoint, Rect rect)
        {
            if (rect.width < 1f || rect.height < 1f)
            {
                return float2.zero;
            }
            return math.clamp(
                new float2(localPoint.x / rect.width, 1f - localPoint.y / rect.height),
                new float2(0f, 0f),
                new float2(1f, 1f));
        }

        private void OnPointerDown(PointerDownEvent pointerEvent)
        {
            if (!HasDraggableHandles)
            {
                return;
            }

            Rect rect = contentRect;
            Vector2 localPoint = pointerEvent.localPosition;

            float startDistance = Vector2.Distance(localPoint, CurveToLocal(startHandle, rect));
            float endDistance = Vector2.Distance(localPoint, CurveToLocal(endHandle, rect));

            if (startDistance > HandleGrabRadius && endDistance > HandleGrabRadius)
            {
                return;
            }

            draggingHandleIndex = startDistance <= endDistance ? 0 : 1;
            this.CapturePointer(pointerEvent.pointerId);
            pointerEvent.StopPropagation();
        }

        private void OnPointerMove(PointerMoveEvent moveEvent)
        {
            if (draggingHandleIndex < 0 || !this.HasPointerCapture(moveEvent.pointerId))
            {
                return;
            }

            float2 curvePoint = LocalToCurve(moveEvent.localPosition, contentRect);
            if (draggingHandleIndex == 0)
            {
                startHandle = curvePoint;
            }
            else
            {
                endHandle = curvePoint;
            }

            // Reshaping a preset is what makes the key a Bézier. Switching here, rather than waiting
            // for the listener to write it back, keeps the drawn curve following the dragged handles
            // for the rest of the gesture instead of snapping back to the preset's fixed shape.
            interpolation = Interpolation.Bezier;

            MarkDirtyRepaint();
            if (curveEdited != null)
            {
                curveEdited(startHandle, endHandle);
            }
            moveEvent.StopPropagation();
        }

        private void OnPointerUp(PointerUpEvent upEvent)
        {
            if (draggingHandleIndex < 0)
            {
                return;
            }
            draggingHandleIndex = -1;
            this.ReleasePointer(upEvent.pointerId);
            upEvent.StopPropagation();
        }

        private void OnGenerateVisualContent(MeshGenerationContext context)
        {
            Rect rect = contentRect;
            if (rect.width <= 1f || rect.height <= 1f)
            {
                return;
            }

            Painter2D painter = context.painter2D;

            painter.fillColor = BackgroundColor;
            painter.BeginPath();
            painter.MoveTo(new Vector2(0f, 0f));
            painter.LineTo(new Vector2(rect.width, 0f));
            painter.LineTo(new Vector2(rect.width, rect.height));
            painter.LineTo(new Vector2(0f, rect.height));
            painter.ClosePath();
            painter.Fill();

            painter.strokeColor = GridColor;
            painter.lineWidth = 1f;
            for (int gridStep = 1; gridStep < 4; gridStep++)
            {
                float fraction = gridStep / 4f;
                painter.BeginPath();
                painter.MoveTo(new Vector2(rect.width * fraction, 0f));
                painter.LineTo(new Vector2(rect.width * fraction, rect.height));
                painter.Stroke();

                painter.BeginPath();
                painter.MoveTo(new Vector2(0f, rect.height * fraction));
                painter.LineTo(new Vector2(rect.width, rect.height * fraction));
                painter.Stroke();
            }

            StrokeCurve(painter, rect);

            if (!HasDraggableHandles)
            {
                return;
            }

            // Dimmed while the key is on a fixed mode: the handles describe the shape being drawn,
            // but they are not values the key stores until one of them is dragged.
            float handleAlpha = interpolation == Interpolation.Bezier ? 1f : PresetHandleAlpha;

            painter.strokeColor = WithAlpha(HandleLineColor, handleAlpha);
            painter.lineWidth = 1f;
            painter.BeginPath();
            painter.MoveTo(CurveToLocal(float2.zero, rect));
            painter.LineTo(CurveToLocal(startHandle, rect));
            painter.Stroke();
            painter.BeginPath();
            painter.MoveTo(CurveToLocal(new float2(1f, 1f), rect));
            painter.LineTo(CurveToLocal(endHandle, rect));
            painter.Stroke();

            AppendHandleDot(
                painter, CurveToLocal(startHandle, rect), WithAlpha(StartHandleColor, handleAlpha));
            AppendHandleDot(
                painter, CurveToLocal(endHandle, rect), WithAlpha(EndHandleColor, handleAlpha));
        }

        // Draws the eased weight across the segment, sampled through the runtime's own solve. Step
        // is drawn rather than sampled: ClipSampler.Ease returns 0 for it everywhere, since track
        // sampling short-circuits Step before easing.
        private void StrokeCurve(Painter2D painter, Rect rect)
        {
            painter.strokeColor = CurveColor;
            painter.lineWidth = 2f;
            painter.BeginPath();
            painter.MoveTo(CurveToLocal(float2.zero, rect));

            if (interpolation == Interpolation.Step)
            {
                painter.LineTo(CurveToLocal(new float2(1f, 0f), rect));
                painter.LineTo(CurveToLocal(new float2(1f, 1f), rect));
                painter.Stroke();
                return;
            }

            for (int sampleIndex = 1; sampleIndex <= CurveSampleCount; sampleIndex++)
            {
                float sampleTime = sampleIndex / (float)CurveSampleCount;
                float weight = ClipSampler.Ease(
                    sampleTime, interpolation, in startHandle, in endHandle);
                painter.LineTo(CurveToLocal(new float2(sampleTime, weight), rect));
            }
            painter.Stroke();
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            return new Color(color.r, color.g, color.b, color.a * alpha);
        }

        private static void AppendHandleDot(Painter2D painter, Vector2 center, Color color)
        {
            painter.fillColor = color;
            painter.BeginPath();
            painter.Arc(center, HandleRadius, 0f, 360f);
            painter.ClosePath();
            painter.Fill();
        }
    }
}
