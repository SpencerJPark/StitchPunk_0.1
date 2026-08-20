// Copyright (c) 2026 Spencer Park. All rights reserved.

using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// The current-time line drawn over the ruler and lanes (architecture section 7.2).
    /// </summary>
    /// <remarks>
    /// An absolutely positioned overlay spanning the whole lane stack, so the line is continuous
    /// across every track instead of being redrawn per lane and stepping between them. Pointer
    /// events pass straight through — the playhead must never steal a click meant for a key
    /// underneath it.
    /// </remarks>
    public sealed class PlayheadElement : VisualElement
    {
        private static readonly Color PlayheadColor = new Color(0.95f, 0.36f, 0.30f);

        private float currentTime;

        /// <summary>Normalized playhead position, 0..1 across the clip.</summary>
        public float NormalizedTime
        {
            get { return currentTime; }
            set
            {
                float clamped = Mathf.Clamp01(value);
                if (Mathf.Approximately(clamped, currentTime))
                {
                    return;
                }
                currentTime = clamped;
                MarkDirtyRepaint();
            }
        }

        /// <summary>Absolute positioning and the full-stack inset come from ClipEditorWindow.uss.</summary>
        public const string UssClassName = "clip-editor__playhead";

        public PlayheadElement()
        {
            AddToClassList(UssClassName);

            // Not a style: this is behaviour. The playhead must never steal a click meant for a key
            // underneath it, and a stylesheet has no say in that.
            pickingMode = PickingMode.Ignore;
            generateVisualContent += OnGenerateVisualContent;
        }


        /// <summary>
        /// The timeline view, pushed in by the window. Never derived here: a lane that computed its
        /// own zoom would drift from the ruler's, which is the bug TimelineGeometry exists to stop.
        /// </summary>
        public float viewZoom = 1f;
        public float viewPan;


        /// <summary>
        /// The timeline width the window wants used, in pixels. Zero means "measure yourself".
        /// </summary>
        /// <remarks>
        /// <strong>Pushed in for the same reason zoom and pan are.</strong> The ruler and playhead
        /// sit in the lane stack while the lanes sit in a column inside it, so each element
        /// measuring its own <c>contentRect</c> gave three widths that agreed only once layout had
        /// settled. Any difference between them is multiplied by the zoom, so a few pixels of
        /// disagreement at 1x became a visible gap between the cursor and the key at 20x. One width
        /// for the whole timeline makes that gap unrepresentable.
        /// </remarks>
        public float viewLaneWidth;

        /// <summary>The width to build geometry from: the pushed one, or our own before layout.</summary>
        private float ResolvedWidth
        {
            get { return viewLaneWidth > 1f ? viewLaneWidth : contentRect.width; }
        }

        private void OnGenerateVisualContent(MeshGenerationContext context)
        {
            Rect rect = contentRect;
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            // Rounded to a pixel centre so the 1px line renders crisp rather than as two half-lit
            // columns.
            float x = Mathf.Round(
                TimelineGeometry.Create(ResolvedWidth, viewZoom, viewPan).TimeToX(currentTime)) + 0.5f;

            Painter2D painter = context.painter2D;
            painter.strokeColor = PlayheadColor;
            painter.lineWidth = 1f;
            painter.BeginPath();
            painter.MoveTo(new Vector2(x, 0f));
            painter.LineTo(new Vector2(x, rect.height));
            painter.Stroke();

            painter.fillColor = PlayheadColor;
            painter.BeginPath();
            painter.MoveTo(new Vector2(x - 5f, 0f));
            painter.LineTo(new Vector2(x + 5f, 0f));
            painter.LineTo(new Vector2(x, 8f));
            painter.ClosePath();
            painter.Fill();
        }
    }
}
