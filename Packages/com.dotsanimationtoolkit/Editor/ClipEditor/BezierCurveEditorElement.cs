// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// A draggable-handle editor for a key's Bézier ease.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>The curve drawn is sampled through <c>ClipSampler.EaseBezier</c></strong>, the same
    /// function the runtime evaluates. A widget that plotted the cubic with its own arithmetic would
    /// eventually disagree with playback, and the disagreement would be invisible — the shape would
    /// simply be a little wrong in a way nobody could point at.
    /// </para>
    /// <para>
    /// Handles are clamped to the unit square, which is validation rule V17's constraint rather than
    /// a drawing convenience: x outside it makes the curve non-functional, and y outside it is
    /// overshoot the bake's bounds union cannot account for. Clamping here means the editor cannot
    /// author a clip that fails that rule.
    /// </para>
    /// </remarks>
    public sealed class BezierCurveEditorElement : VisualElement
    {
        public const string UssClassName = "clip-editor__bezier-curve";

        private const int CurveSampleCount = 48;
        private const float HandleRadius = 5f;
        private const float HandleGrabRadius = 11f;

        private static readonly Color BackgroundColor = new Color(0.16f, 0.16f, 0.17f, 1f);
        private static readonly Color GridColor = new Color(0.28f, 0.28f, 0.30f, 1f);
        private static readonly Color CurveColor = new Color(0.55f, 0.80f, 1f, 1f);
        private static readonly Color HandleLineColor = new Color(0.60f, 0.60f, 0.64f, 1f);
        private static readonly Color StartHandleColor = new Color(0.45f, 0.85f, 0.50f, 1f);
        private static readonly Color EndHandleColor = new Color(0.95f, 0.65f, 0.35f, 1f);

        private float2 startHandle = new float2(1f / 3f, 1f / 3f);
        private float2 endHandle = new float2(2f / 3f, 2f / 3f);

        private int draggingHandleIndex = -1;

        /// <summary>Raised while a handle is dragged, with both handles' current values.</summary>
        public event Action<float2, float2> handlesChanged;

        public BezierCurveEditorElement()
        {
            AddToClassList(UssClassName);
            focusable = true;
            generateVisualContent += OnGenerateVisualContent;
            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
        }

        /// <summary>
        /// Sets the handles shown, without raising <see cref="handlesChanged"/>.
        /// </summary>
        /// <remarks>
        /// An all-zero pair is displayed as the linear handles, matching how
        /// <c>ClipSampler.EaseBezier</c> reads it. Drawing the literal zeros would show a curve
        /// pinned to the origin that does not describe what the key actually does.
        /// </remarks>
        public void SetHandlesWithoutNotify(float2 newStartHandle, float2 newEndHandle)
        {
            if (math.all(newStartHandle == float2.zero) && math.all(newEndHandle == float2.zero))
            {
                newStartHandle = new float2(1f / 3f, 1f / 3f);
                newEndHandle = new float2(2f / 3f, 2f / 3f);
            }
            startHandle = math.clamp(newStartHandle, new float2(0f, 0f), new float2(1f, 1f));
            endHandle = math.clamp(newEndHandle, new float2(0f, 0f), new float2(1f, 1f));
            MarkDirtyRepaint();
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

            MarkDirtyRepaint();
            if (handlesChanged != null)
            {
                handlesChanged(startHandle, endHandle);
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

            // Sampled through the runtime's own solve, so what is drawn is what will play.
            painter.strokeColor = CurveColor;
            painter.lineWidth = 2f;
            painter.BeginPath();
            painter.MoveTo(CurveToLocal(float2.zero, rect));
            for (int sampleIndex = 1; sampleIndex <= CurveSampleCount; sampleIndex++)
            {
                float sampleTime = sampleIndex / (float)CurveSampleCount;
                float weight = ClipSampler.EaseBezier(sampleTime, in startHandle, in endHandle);
                painter.LineTo(CurveToLocal(new float2(sampleTime, weight), rect));
            }
            painter.Stroke();

            painter.strokeColor = HandleLineColor;
            painter.lineWidth = 1f;
            painter.BeginPath();
            painter.MoveTo(CurveToLocal(float2.zero, rect));
            painter.LineTo(CurveToLocal(startHandle, rect));
            painter.Stroke();
            painter.BeginPath();
            painter.MoveTo(CurveToLocal(new float2(1f, 1f), rect));
            painter.LineTo(CurveToLocal(endHandle, rect));
            painter.Stroke();

            AppendHandleDot(painter, CurveToLocal(startHandle, rect), StartHandleColor);
            AppendHandleDot(painter, CurveToLocal(endHandle, rect), EndHandleColor);
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
