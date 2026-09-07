// Copyright (c) 2026 Spencer Park. All rights reserved.

using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    public sealed partial class CutsceneEditorPanel
    {
        /// <summary>Zoom change per wheel notch, as a multiplier.</summary>
        private const float TimelineZoomStepFactor = 1.15f;

        // Ctrl+wheel keeps the time under the cursor pinned there, which is what makes zooming feel
        // like it happens where you are looking rather than at the left edge.
        private void OnTimelineWheel(WheelEvent wheelEvent)
        {
            if (!wheelEvent.ctrlKey && !wheelEvent.commandKey)
            {
                return;
            }
            if (timelineLaneScroll == null || cutscene == null)
            {
                return;
            }

            Vector2 pointerInViewport =
                timelineLaneScroll.contentViewport.WorldToLocal(wheelEvent.mousePosition);
            float contentX = pointerInViewport.x + timelineLaneScroll.scrollOffset.x;
            float timeUnderCursor = CutsceneTimelineGeometry.Create(pixelsPerSecond).XToTime(contentX);

            float zoomFactor = wheelEvent.delta.y > 0f
                ? 1f / TimelineZoomStepFactor
                : TimelineZoomStepFactor;
            SetTimelineZoom(pixelsPerSecond * zoomFactor);

            float newContentX = CutsceneTimelineGeometry.Create(pixelsPerSecond).TimeToX(timeUnderCursor);
            ScrollTimelineTo(newContentX - pointerInViewport.x);
            wheelEvent.StopPropagation();
        }

        private void SetTimelineZoom(float newPixelsPerSecond)
        {
            pixelsPerSecond = Mathf.Clamp(
                newPixelsPerSecond,
                CutsceneTimelineGeometry.MinimumPixelsPerSecond,
                CutsceneTimelineGeometry.MaximumPixelsPerSecond);
            if (zoomSlider != null)
            {
                zoomSlider.SetValueWithoutNotify(pixelsPerSecond);
            }
            RebuildTimeline();
        }

        // Applied after the rebuild rather than during it: the lane column's content is replaced
        // wholesale, and a scroll offset set against the old content is discarded.
        private void ScrollTimelineTo(float contentX)
        {
            if (timelineLaneScroll == null)
            {
                return;
            }
            Vector2 scrollOffset = timelineLaneScroll.scrollOffset;
            scrollOffset.x = Mathf.Max(0f, contentX);
            timelineLaneScroll.scrollOffset = scrollOffset;
        }

        /// <summary>Zooms so the whole cutscene fits the visible lane width.</summary>
        private void FrameWholeTimeline()
        {
            if (cutscene == null || timelineLaneScroll == null)
            {
                return;
            }
            float visibleWidth = timelineLaneScroll.contentViewport.resolvedStyle.width;
            float contentEnd = ComputeContentEndSeconds() + TrailingSeconds;
            if (float.IsNaN(visibleWidth) || visibleWidth < 1f || contentEnd <= 0f)
            {
                return;
            }
            SetTimelineZoom((visibleWidth - 20f) / contentEnd);
            ScrollTimelineTo(0f);
        }

        /// <summary>Scrolls the playhead to the middle of the visible lane width, without moving it.</summary>
        private void CentreTimelineOnPlayhead()
        {
            if (timelineLaneScroll == null)
            {
                return;
            }
            float visibleWidth = timelineLaneScroll.contentViewport.resolvedStyle.width;
            if (float.IsNaN(visibleWidth) || visibleWidth < 1f)
            {
                return;
            }
            float playheadX = CutsceneTimelineGeometry.Create(pixelsPerSecond).TimeToX(playheadSeconds);
            ScrollTimelineTo(playheadX - visibleWidth * 0.5f);
        }
    }
}
