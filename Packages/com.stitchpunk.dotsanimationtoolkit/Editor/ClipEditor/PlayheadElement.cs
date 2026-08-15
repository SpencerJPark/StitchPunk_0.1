// Copyright (c) 2026 Stitch Punk. All rights reserved.

using UnityEngine;
using UnityEngine.UIElements;

namespace StitchPunk.AnimationToolkit.Editor
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

        private void OnGenerateVisualContent(MeshGenerationContext context)
        {
            Rect rect = contentRect;
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            // Rounded to a pixel centre so the 1px line renders crisp rather than as two half-lit
            // columns.
            float x = Mathf.Round(TimelineGeometry.Create(rect.width).TimeToX(currentTime)) + 0.5f;

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
