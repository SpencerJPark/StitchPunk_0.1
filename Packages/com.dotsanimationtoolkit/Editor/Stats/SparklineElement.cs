// Copyright (c) 2026 Spencer Park. All rights reserved.

using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// A fixed-length ring-buffer line chart drawn with Painter2D, oldest sample on the left.
    /// </summary>
    public sealed class SparklineElement : VisualElement
    {
        public const int Capacity = 60;

        private readonly int[] samples = new int[Capacity];
        private int sampleCount;
        private int writeIndex;
        private int latest;
        private int peak;

        public int Latest
        {
            get { return latest; }
        }

        public int Peak
        {
            get { return peak; }
        }

        public int SampleCount
        {
            get { return sampleCount; }
        }

        public SparklineElement()
        {
            name = "stats-sparkline";
            style.height = 32f;
            style.flexGrow = 1f;
            style.minWidth = 120f;
            generateVisualContent += DrawSparkline;
        }

        public void Push(int value)
        {
            samples[writeIndex] = value;
            writeIndex = (writeIndex + 1) % Capacity;
            if (sampleCount < Capacity)
            {
                sampleCount++;
            }

            latest = value;
            RecomputePeak();
            MarkDirtyRepaint();
        }

        public void Clear()
        {
            sampleCount = 0;
            writeIndex = 0;
            latest = 0;
            peak = 0;
            MarkDirtyRepaint();
        }

        private void RecomputePeak()
        {
            int highest = 0;
            int oldestIndex = sampleCount < Capacity ? 0 : writeIndex;
            for (int sampleOffset = 0; sampleOffset < sampleCount; sampleOffset++)
            {
                int ringIndex = (oldestIndex + sampleOffset) % Capacity;
                if (samples[ringIndex] > highest)
                {
                    highest = samples[ringIndex];
                }
            }

            peak = highest;
        }

        private void DrawSparkline(MeshGenerationContext context)
        {
            Rect rect = contentRect;
            if (sampleCount < 2 || float.IsNaN(rect.width) || float.IsNaN(rect.height)
                || rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            Painter2D painter = context.painter2D;

            painter.strokeColor = ToolkitPalette.BoxBorder;
            painter.lineWidth = 1f;
            painter.BeginPath();
            painter.MoveTo(new Vector2(0f, rect.height - 0.5f));
            painter.LineTo(new Vector2(rect.width, rect.height - 0.5f));
            painter.Stroke();

            float xStep = rect.width / (Capacity - 1);
            int oldestIndex = sampleCount < Capacity ? 0 : writeIndex;
            int highest = peak;

            painter.strokeColor = ToolkitPalette.LaneEvents;
            painter.lineWidth = 1.5f;
            painter.BeginPath();
            for (int sampleOffset = 0; sampleOffset < sampleCount; sampleOffset++)
            {
                int ringIndex = (oldestIndex + sampleOffset) % Capacity;
                int slotFromOldest = Capacity - sampleCount + sampleOffset;
                float x = slotFromOldest * xStep;
                float normalizedHeight = highest > 0 ? samples[ringIndex] / (float)highest : 0f;
                float y = highest > 0
                    ? 1f + ((1f - normalizedHeight) * (rect.height - 1f))
                    : rect.height;

                if (sampleOffset == 0)
                {
                    painter.MoveTo(new Vector2(x, y));
                }
                else
                {
                    painter.LineTo(new Vector2(x, y));
                }
            }

            painter.Stroke();
        }
    }
}
