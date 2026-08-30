// Copyright (c) 2026 Spencer Park. All rights reserved.

using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>The current-time line drawn over a cutscene's ruler and lanes.</summary>
    /// <remarks>Seconds-based sibling of <see cref="PlayheadElement"/> — see <see cref="CutsceneTimelineGeometry"/>'s remarks (decision G-D2).</remarks>
    public sealed class CutsceneTimelinePlayheadElement : VisualElement
    {
        private static readonly Color PlayheadColor = new Color(0.95f, 0.36f, 0.30f);

        public const string UssClassName = "cutscene-editor__playhead";

        private float timeSeconds;

        public float TimeSeconds
        {
            get { return timeSeconds; }
            set
            {
                float clamped = Mathf.Max(0f, value);
                if (Mathf.Approximately(clamped, timeSeconds))
                {
                    return;
                }
                timeSeconds = clamped;
                MarkDirtyRepaint();
            }
        }

        /// <summary>Pixels per second, pushed in by the panel so this line agrees with the ruler and lanes.</summary>
        public float pixelsPerSecond = 40f;

        public CutsceneTimelinePlayheadElement()
        {
            AddToClassList(UssClassName);
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

            float x = Mathf.Round(
                CutsceneTimelineGeometry.Create(pixelsPerSecond).TimeToX(timeSeconds)) + 0.5f;

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
