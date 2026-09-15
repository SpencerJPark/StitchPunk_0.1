// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>One read-only imported-clip row: hollow, dimmed diamonds at an imported clip's key times. Never selectable, never dragged.</summary>
    public sealed class ImportedClipLaneElement : VisualElement
    {
        public const string UssClassName = "clip-editor__imported-lane";

        /// <summary>Dimmed so a glance tells authored keys from imported ones without reading the row label.</summary>
        private static readonly Color KeyOutline = new Color(
            ToolkitPalette.Accent.r, ToolkitPalette.Accent.g, ToolkitPalette.Accent.b, 0.45f);

        private readonly List<float> keyTimes = new List<float>();

        public float viewZoom = 1f;
        public float viewPan;
        public float viewLaneWidth;

        public ImportedClipLaneElement()
        {
            // Carries the ordinary lane class too, so it shares TrackLaneElement's row height and
            // stays exactly one row tall and aligned with its header.
            AddToClassList(TrackLaneElement.UssClassName);
            AddToClassList(UssClassName);
            pickingMode = PickingMode.Ignore;
            generateVisualContent += OnGenerateVisualContent;
        }

        private float ResolvedWidth
        {
            get { return viewLaneWidth > 1f ? viewLaneWidth : contentRect.width; }
        }

        private TimelineGeometry Geometry
        {
            get { return TimelineGeometry.Create(ResolvedWidth, viewZoom, viewPan); }
        }

        /// <summary>Pushes the view into the lane and repaints it.</summary>
        public void PushView(float laneWidth, float zoom, float pan)
        {
            viewLaneWidth = laneWidth;
            viewZoom = zoom;
            viewPan = pan;
            MarkDirtyRepaint();
        }

        public void SetKeyTimes(IReadOnlyList<float> normalizedKeyTimes)
        {
            keyTimes.Clear();
            if (normalizedKeyTimes != null)
            {
                keyTimes.AddRange(normalizedKeyTimes);
            }
            MarkDirtyRepaint();
        }

        private void OnGenerateVisualContent(MeshGenerationContext context)
        {
            Rect rect = contentRect;
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            Painter2D painter = context.painter2D;
            TimelineGeometry geometry = Geometry;
            float centreY = rect.height * 0.5f;

            painter.strokeColor = KeyOutline;
            painter.lineWidth = 1f;

            for (int keyIndex = 0; keyIndex < keyTimes.Count; keyIndex++)
            {
                float x = geometry.TimeToX(keyTimes[keyIndex]);
                float radius = TimelineGeometry.KeyDrawRadius;

                painter.BeginPath();
                painter.MoveTo(new Vector2(x, centreY - radius));
                painter.LineTo(new Vector2(x + radius, centreY));
                painter.LineTo(new Vector2(x, centreY + radius));
                painter.LineTo(new Vector2(x - radius, centreY));
                painter.ClosePath();
                painter.Stroke();
            }
        }
    }
}
