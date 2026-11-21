// Copyright (c) 2026 Spencer Park. All rights reserved.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Paints the minor, major and axis grid lines of a pan/zoom world canvas.</summary>
    public static class CutoutCanvasGrid
    {
        public const float MinorGridWorldUnits = 0.1f;
        public const float MajorGridWorldUnits = 1f;
        private const float MinimumGridLineSpacingPoints = 6f;

        public static void Paint(
            MeshGenerationContext context,
            Vector2 elementSizePoints,
            float zoomPointsPerWorldUnit,
            Func<Vector2, Vector2> worldToElement,
            Func<Vector2, Vector2> elementToWorld)
        {
            Painter2D painter = context.painter2D;
            Vector2 worldMin = elementToWorld(new Vector2(0f, elementSizePoints.y));
            Vector2 worldMax = elementToWorld(new Vector2(elementSizePoints.x, 0f));

            List<Vector2> minorSegments = new List<Vector2>();
            List<Vector2> majorSegments = new List<Vector2>();
            bool drawMinor = MinorGridWorldUnits * zoomPointsPerWorldUnit >= MinimumGridLineSpacingPoints;

            if (drawMinor)
            {
                int firstMinorX = Mathf.FloorToInt(worldMin.x / MinorGridWorldUnits);
                int lastMinorX = Mathf.CeilToInt(worldMax.x / MinorGridWorldUnits);
                for (int index = firstMinorX; index <= lastMinorX; index++)
                {
                    if (index % 10 != 0)
                    {
                        AddVerticalLine(minorSegments, worldToElement, elementSizePoints, index * MinorGridWorldUnits);
                    }
                }

                int firstMinorY = Mathf.FloorToInt(worldMin.y / MinorGridWorldUnits);
                int lastMinorY = Mathf.CeilToInt(worldMax.y / MinorGridWorldUnits);
                for (int index = firstMinorY; index <= lastMinorY; index++)
                {
                    if (index % 10 != 0)
                    {
                        AddHorizontalLine(minorSegments, worldToElement, elementSizePoints, index * MinorGridWorldUnits);
                    }
                }
            }

            int firstMajorX = Mathf.FloorToInt(worldMin.x / MajorGridWorldUnits);
            int lastMajorX = Mathf.CeilToInt(worldMax.x / MajorGridWorldUnits);
            for (int index = firstMajorX; index <= lastMajorX; index++)
            {
                if (index != 0)
                {
                    AddVerticalLine(majorSegments, worldToElement, elementSizePoints, index * MajorGridWorldUnits);
                }
            }

            int firstMajorY = Mathf.FloorToInt(worldMin.y / MajorGridWorldUnits);
            int lastMajorY = Mathf.CeilToInt(worldMax.y / MajorGridWorldUnits);
            for (int index = firstMajorY; index <= lastMajorY; index++)
            {
                if (index != 0)
                {
                    AddHorizontalLine(majorSegments, worldToElement, elementSizePoints, index * MajorGridWorldUnits);
                }
            }

            List<Vector2> axisSegments = new List<Vector2>();
            AddVerticalLine(axisSegments, worldToElement, elementSizePoints, 0f);
            AddHorizontalLine(axisSegments, worldToElement, elementSizePoints, 0f);

            StrokeSegments(painter, minorSegments, new Color(1f, 1f, 1f, 0.04f), 1f);
            StrokeSegments(painter, majorSegments, new Color(1f, 1f, 1f, 0.09f), 1f);
            StrokeSegments(painter, axisSegments, new Color(1f, 1f, 1f, 0.22f), 1f);
        }

        private static void AddVerticalLine(List<Vector2> segmentEndpoints, Func<Vector2, Vector2> worldToElement, Vector2 elementSizePoints, float worldX)
        {
            float elementX = worldToElement(new Vector2(worldX, 0f)).x;
            segmentEndpoints.Add(new Vector2(elementX, 0f));
            segmentEndpoints.Add(new Vector2(elementX, elementSizePoints.y));
        }

        private static void AddHorizontalLine(List<Vector2> segmentEndpoints, Func<Vector2, Vector2> worldToElement, Vector2 elementSizePoints, float worldY)
        {
            float elementY = worldToElement(new Vector2(0f, worldY)).y;
            segmentEndpoints.Add(new Vector2(0f, elementY));
            segmentEndpoints.Add(new Vector2(elementSizePoints.x, elementY));
        }

        private static void StrokeSegments(Painter2D painter, List<Vector2> segmentEndpoints, Color color, float width)
        {
            if (segmentEndpoints.Count == 0)
            {
                return;
            }

            painter.strokeColor = color;
            painter.lineWidth = width;
            painter.BeginPath();
            for (int index = 0; index + 1 < segmentEndpoints.Count; index += 2)
            {
                painter.MoveTo(segmentEndpoints[index]);
                painter.LineTo(segmentEndpoints[index + 1]);
            }

            painter.Stroke();
        }
    }
}
