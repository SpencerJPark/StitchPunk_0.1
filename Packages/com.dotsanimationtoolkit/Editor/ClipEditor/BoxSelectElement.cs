// Copyright (c) 2026 Spencer Park. All rights reserved.

using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// The rubber band drawn while dragging a selection box across the timeline's lanes.
    /// </summary>
    /// <remarks>
    /// An overlay spanning the whole lane stack, like the playhead, so a band can start in one lane
    /// and end in another — a box that could only cover the row it began on would not be a box
    /// select. Pointer events pass straight through: the lanes underneath own the drag, and this
    /// only draws it.
    /// </remarks>
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

        /// <summary>Hides the band. Idempotent.</summary>
        /// <remarks>
        /// Named for the band rather than called <c>Clear</c>, which on a <c>VisualElement</c> already
        /// means "remove every child" — two very different operations behind one name.
        /// </remarks>
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
