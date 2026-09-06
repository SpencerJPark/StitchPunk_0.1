// Copyright (c) 2026 Spencer Park. All rights reserved.

using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// The rubber band drawn while dragging a selection box across the timeline's lanes. Spans the
    /// whole lane stack so a band can start in one lane and end in another; pointer events pass
    /// straight through to the lanes underneath, which own the drag.
    /// </summary>
    public sealed class BoxSelectElement : VisualElement
    {
        public const string UssClassName = "clip-editor__box-select";

        private static readonly Color FillColor = new Color(0.30f, 0.62f, 0.95f, 0.18f);
        private static readonly Color OutlineColor = new Color(0.45f, 0.72f, 1f, 0.9f);

        private Rect selectionRect;
        private bool isActive;

        public BoxSelectElement()
        {
            AddToClassList(UssClassName);
            pickingMode = PickingMode.Ignore;
            generateVisualContent += OnGenerateVisualContent;
        }

        /// <summary>Shows the band across a rect in this element's own space.</summary>
        public void SetBand(Rect bandRect)
        {
            selectionRect = bandRect;
            isActive = true;
            MarkDirtyRepaint();
        }

        // Named HideBand rather than Clear, which on a VisualElement already means "remove every child".
        public void HideBand()
        {
            if (!isActive)
            {
                return;
            }
            isActive = false;
            MarkDirtyRepaint();
        }

        private void OnGenerateVisualContent(MeshGenerationContext context)
        {
            if (!isActive || selectionRect.width <= 0f || selectionRect.height <= 0f)
            {
                return;
            }

            Painter2D painter = context.painter2D;

            painter.fillColor = FillColor;
            painter.BeginPath();
            painter.MoveTo(new Vector2(selectionRect.xMin, selectionRect.yMin));
            painter.LineTo(new Vector2(selectionRect.xMax, selectionRect.yMin));
            painter.LineTo(new Vector2(selectionRect.xMax, selectionRect.yMax));
            painter.LineTo(new Vector2(selectionRect.xMin, selectionRect.yMax));
            painter.ClosePath();
            painter.Fill();

            painter.strokeColor = OutlineColor;
            painter.lineWidth = 1f;
            painter.BeginPath();
            painter.MoveTo(new Vector2(selectionRect.xMin, selectionRect.yMin));
            painter.LineTo(new Vector2(selectionRect.xMax, selectionRect.yMin));
            painter.LineTo(new Vector2(selectionRect.xMax, selectionRect.yMax));
            painter.LineTo(new Vector2(selectionRect.xMin, selectionRect.yMax));
            painter.ClosePath();
            painter.Stroke();
        }
    }
}
