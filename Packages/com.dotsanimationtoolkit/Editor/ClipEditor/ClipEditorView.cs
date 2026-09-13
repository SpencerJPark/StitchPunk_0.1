// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    public sealed partial class ClipEditorWindow
    {
        private const string ZoomPrefKey = "DotsAnimationToolkit.ClipEditor.Zoom";
        private const string PanPrefKey = "DotsAnimationToolkit.ClipEditor.Pan";

        private float viewZoom = 1f;
        private float viewPan;

        /// <summary>
        /// How far in this clip can be zoomed: the zoom that leaves
        /// <see cref="TimelineGeometry.VisibleFramesAtMaximumZoom"/> frames on screen. It depends on
        /// the clip's frame count, so it is read live rather than cached — length and frame rate are
        /// both editable from the bar right above the timeline.
        /// </summary>
        private float MaximumViewZoom
        {
            get { return TimelineGeometry.MaximumZoomForFrameCount(TransportFrameCount); }
        }

        private ScrollView timelineScroll;
        private Slider zoomSlider;
        private Scroller horizontalScroller;
        private bool isSyncingZoomSlider;
        private bool isSyncingHorizontalScroller;
        private bool isPanningView;
        private float panPointerStartX;
        private float panStartValue;

        private void BindTimelineView()
        {
            viewZoom = Mathf.Clamp(
                EditorPrefs.GetFloat(ZoomPrefKey, 1f),
                TimelineGeometry.MinimumZoom, MaximumViewZoom);
            viewPan = EditorPrefs.GetFloat(PanPrefKey, 0f);

            timelineScroll = rootVisualElement.Q<ScrollView>("timeline-scroll");
            zoomSlider = rootVisualElement.Q<Slider>("zoom-slider");
            if (zoomSlider != null)
            {
                zoomSlider.tooltip =
                    "Timeline zoom, centred on the playhead. All the way left pulls back to about "
                    + "seven clip lengths, so keys dragged outside the clip are still on screen; "
                    + "all the way right leaves "
                    + TimelineGeometry.VisibleFramesAtMaximumZoom.ToString("0") + " frames on "
                    + "screen, so the right-hand end moves with the clip's frame count. Ctrl+scroll "
                    + "zooms the same way (hold Alt to zoom toward the cursor instead). "
                    + "Shift+scroll or a sideways scroll pans, as does a middle-drag.";
                RefreshZoomRange();
                zoomSlider.RegisterValueChangedCallback(changeEvent =>
                {
                    if (isSyncingZoomSlider)
                    {
                        return;
                    }
                    SetZoomAtTime(changeEvent.newValue, playheadTime);
                });
            }

            Button frameAllButton = rootVisualElement.Q<Button>("frame-all-button");
            if (frameAllButton != null)
            {
                frameAllButton.clicked += FrameAll;
                frameAllButton.tooltip = "Fit the whole clip in the timeline.";
            }

            Button frameSelectionButton = rootVisualElement.Q<Button>("frame-selection-button");
            if (frameSelectionButton != null)
            {
                frameSelectionButton.clicked += FrameSelection;
                frameSelectionButton.tooltip =
                    "Fit the selected keys in the timeline. Falls back to framing the clip when "
                    + "nothing is selected.";
            }

            // The ghost scrollbar. ScrollView.mode="Vertical" only swaps USS variant classes; it
            // leaves horizontalScrollerVisibility at Auto, so the view still grows its own
            // horizontal scrollbar whenever its content happens to measure wider than the viewport.
            // It flickers in and out as content changes and does nothing when dragged, because
            // there is nothing to scroll: the lanes are one element wide that draws a zoomed
            // window. Turning it off leaves the scroller below as the only one.
            if (timelineScroll != null)
            {
                timelineScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            }

            // Built here rather than declared in UXML. Horizontal scrolling is the view transform,
            // not a ScrollView: the lanes are one element wide drawing a zoomed window, so there is
            // no oversized content for a ScrollView to scroll. Constructing it in code also keeps a
            // UXML factory mismatch — a load-time failure that takes the whole window with it —
            // off the table, which is why the ruler and playhead are built this way too.
            horizontalScroller = new Scroller(
                0f, 1f, newValue => { }, SliderDirection.Horizontal);
            horizontalScroller.name = "timeline-h-scroller";
            horizontalScroller.AddToClassList("clip-editor__h-scroller");
            if (timelineScroll != null && timelineScroll.parent != null)
            {
                timelineScroll.parent.Insert(
                    timelineScroll.parent.IndexOf(timelineScroll) + 1, horizontalScroller);
            }

            if (horizontalScroller != null)
            {
                horizontalScroller.tooltip =
                    "Scroll the timeline. The range grows as you zoom in, and extends past both "
                    + "ends of the clip so out-of-range keys stay reachable.";
                horizontalScroller.valueChanged += newValue =>
                {
                    if (isSyncingHorizontalScroller)
                    {
                        return;
                    }
                    viewPan = newValue;
                    ApplyTimelineView();
                };
            }

            // A resize changes what every normalized time is worth in pixels, and it is the one
            // trigger the window cannot see for itself.
            if (laneStack != null)
            {
                laneStack.RegisterCallback<GeometryChangedEvent>(
                    geometryEvent => ApplyTimelineView());
            }

            // Two more resizes matter to the ghost rows and to nothing else, which is why the stack
            // alone was enough until now. The lane column changes height whenever tracks are added,
            // removed or expanded — and the strip shrinks by however much the column grew, so the
            // stack it sits in can end up exactly as tall as before and never report a thing.
            // Dragging the timeline pane taller changes the viewport without touching either.
            if (laneColumn != null)
            {
                laneColumn.RegisterCallback<GeometryChangedEvent>(
                    geometryEvent => ApplyTimelineView());
            }
            if (timelineScroll != null && timelineScroll.contentViewport != null)
            {
                timelineScroll.contentViewport.RegisterCallback<GeometryChangedEvent>(
                    geometryEvent => ApplyTimelineView());
            }

            if (timelineScroll != null)
            {
                timelineScroll.RegisterCallback<WheelEvent>(OnTimelineWheel);
                timelineScroll.RegisterCallback<PointerDownEvent>(OnTimelinePanDown);
                timelineScroll.RegisterCallback<PointerMoveEvent>(OnTimelinePanMove);
                timelineScroll.RegisterCallback<PointerUpEvent>(OnTimelinePanUp);
            }

            // Without this the restored zoom and pan lived only in the slider: the elements kept
            // their defaults of zoom 1 and pan 0, so the window opened showing an unzoomed timeline
            // under a slider that said otherwise, and the first nudge of the slider "fixed" it by
            // finally pushing the view down.
            ApplyTimelineView();
        }

        /// <summary>Points the slider's ends at the current zoom bounds, and pulls the view inside them.</summary>
        private void RefreshZoomRange()
        {
            float ceiling = MaximumViewZoom;
            float clampedZoom = Mathf.Clamp(viewZoom, TimelineGeometry.MinimumZoom, ceiling);

            if (zoomSlider != null)
            {
                isSyncingZoomSlider = true;
                try
                {
                    // The zoomed-in end moves with the clip's frame count, so the slider's right-hand
                    // stop always matches what the view will actually accept.
                    zoomSlider.lowValue = TimelineGeometry.MinimumZoom;
                    zoomSlider.highValue = ceiling;
                    zoomSlider.SetValueWithoutNotify(clampedZoom);
                }
                finally
                {
                    isSyncingZoomSlider = false;
                }
            }

            // A shortened clip can drop the ceiling under where the view already is. That is a move
            // of the view, so everything that paints or hit-tests against it has to be told.
            if (!Mathf.Approximately(clampedZoom, viewZoom))
            {
                viewZoom = clampedZoom;
                ApplyTimelineView();
            }
        }

        /// <summary>Zooms while holding <paramref name="anchorTime"/> where it is on screen.</summary>
        private void SetZoomAtTime(float newZoom, float anchorTime)
        {
            float laneWidth = LaneWidth;
            TimelineGeometry before = TimelineGeometry.Create(laneWidth, viewZoom, viewPan);

            float anchorX = before.TimeToX(anchorTime);
            // Off screen anchors move to the middle instead: zooming in is always a closer look at
            // the playhead, never a way of losing it.
            if (anchorX < before.leftPadding || anchorX > laneWidth - before.rightPadding)
            {
                anchorX = before.leftPadding + before.TrackPixelWidth * 0.5f;
            }

            viewZoom = Mathf.Clamp(newZoom, TimelineGeometry.MinimumZoom, MaximumViewZoom);

            TimelineGeometry after = TimelineGeometry.Create(laneWidth, viewZoom, viewPan);
            viewPan = after.PanToAnchor(anchorTime, anchorX);
            ApplyTimelineView();
        }

        /// <summary>Applies a new zoom while holding one normalized position under one screen point.</summary>
        /// <param name="anchorFraction">Where in the track area to hold, 0 = left edge, 1 = right.</param>
        private void SetZoomAnchored(float newZoom, float anchorFraction)
        {
            float laneWidth = LaneWidth;
            TimelineGeometry before = TimelineGeometry.Create(laneWidth, viewZoom, viewPan);
            float anchorX = before.leftPadding + before.TrackPixelWidth * Mathf.Clamp01(anchorFraction);
            float timeUnderAnchor = before.XToTime(anchorX);

            viewZoom = Mathf.Clamp(newZoom, TimelineGeometry.MinimumZoom, MaximumViewZoom);

            TimelineGeometry after = TimelineGeometry.Create(laneWidth, viewZoom, viewPan);
            viewPan = after.PanToAnchor(timeUnderAnchor, anchorX);
            ApplyTimelineView();
        }

        /// <summary>The one width every part of the timeline converts against.</summary>
        private float LaneWidth
        {
            get
            {
                // Measured on the lane stack, the common ancestor of ruler and playhead — taking the
                // lane column instead let the ruler and lanes build from different widths.
                if (laneStack != null && laneStack.contentRect.width > 1f)
                {
                    return laneStack.contentRect.width;
                }
                if (laneColumn != null && laneColumn.contentRect.width > 1f)
                {
                    return laneColumn.contentRect.width;
                }
                return 600f;
            }
        }

        // The window owns the view and pushes it into every element below; nothing derives its own,
        // since a lane that zoomed its painting but not its hit-testing would drift from the rest.
        private void ApplyTimelineView()
        {
            EditorPrefs.SetFloat(ZoomPrefKey, viewZoom);
            EditorPrefs.SetFloat(PanPrefKey, viewPan);

            if (zoomSlider != null)
            {
                isSyncingZoomSlider = true;
                zoomSlider.SetValueWithoutNotify(viewZoom);
                isSyncingZoomSlider = false;
            }

            SyncHorizontalScroller();

            float laneWidth = LaneWidth;

            if (ruler != null)
            {
                ruler.viewLaneWidth = laneWidth;
                ruler.viewZoom = viewZoom;
                ruler.viewPan = viewPan;
                // Labels are children, so they are rebuilt here rather than during the repaint the
                // line below schedules.
                ruler.RefreshSecondLabels();
                ruler.MarkDirtyRepaint();
            }
            if (playhead != null)
            {
                playhead.viewLaneWidth = laneWidth;
                playhead.viewZoom = viewZoom;
                playhead.viewPan = viewPan;
                playhead.MarkDirtyRepaint();
            }
            if (laneColumn != null)
            {
                laneColumn.Query<TrackLaneElement>().ForEach(lane =>
                {
                    lane.viewLaneWidth = laneWidth;
                    lane.viewZoom = viewZoom;
                    lane.viewPan = viewPan;
                    lane.MarkDirtyRepaint();
                });
            }

            // After the lanes, because how many ghost rows fit is decided by how tall the lane
            // column ended up.
            SyncGhostLanes();
            if (ghostLanes != null)
            {
                ghostLanes.PushView(laneWidth, viewZoom, viewPan);
            }
        }

        /// <summary>Resizes the ghost rows to whatever the timeline has left under its last track.</summary>
        private void SyncGhostLanes()
        {
            if (ghostLanes == null || timelineScroll == null)
            {
                return;
            }

            // Measured against the viewport, never the lane stack — the stack is as tall as its own
            // contents, so sizing from it would make each pass grow forever.
            VisualElement viewport = timelineScroll.contentViewport;
            float viewportHeight = viewport != null ? viewport.contentRect.height : 0f;

            // Negated rather than "<= 1f" so an unlaid-out NaN stops here too. NaN loses every
            // comparison, so written the other way round it would sail past and be subtracted from
            // into a NaN height, which is not a size any layout can recover from.
            if (!(viewportHeight > 1f))
            {
                return;
            }

            // The parity carries on from the tracks so the stripes do not restart, which would put
            // two rows of one shade against each other exactly where the eye looks for the join.
            ghostLanes.SyncRows(
                viewportHeight - ResolvedHeightOrZero(ruler) - ResolvedHeightOrZero(laneColumn),
                (timelineRowCount & 1) == 1);
        }

        /// <summary>An element's laid-out height, treating "has never been laid out" as no height at all.</summary>
        private static float ResolvedHeightOrZero(VisualElement element)
        {
            if (element == null)
            {
                return 0f;
            }
            float resolvedHeight = element.resolvedStyle.height;
            return float.IsNaN(resolvedHeight) ? 0f : resolvedHeight;
        }

        // -----------------------------------------------------------------------------------
        // Input.
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Scroll zooms toward the cursor; plain scroll without ctrl is left to the scroll view so
        /// a deep clip can still be scrolled vertically.
        /// </summary>
        private void OnTimelineWheel(WheelEvent wheelEvent)
        {
            if (wheelEvent.ctrlKey || wheelEvent.commandKey)
            {
                // Multiplicative, so a step feels the same at every zoom — an additive step crawls
                // when far in and jumps when far out.
                float factor = wheelEvent.delta.y > 0f ? 1f / 1.15f : 1.15f;

                if (wheelEvent.altKey)
                {
                    // The escape hatch: zoom toward the cursor, for when the point of interest is
                    // not where the playhead is.
                    TimelineGeometry geometry =
                        TimelineGeometry.Create(LaneWidth, viewZoom, viewPan);
                    float localX = laneStack != null
                        ? wheelEvent.mousePosition.x - laneStack.worldBound.xMin
                        : geometry.leftPadding + geometry.TrackPixelWidth * 0.5f;
                    SetZoomAnchored(
                        viewZoom * factor,
                        (localX - geometry.leftPadding) / geometry.TrackPixelWidth);
                }
                else
                {
                    SetZoomAtTime(viewZoom * factor, playheadTime);
                }
                wheelEvent.StopPropagation();
                return;
            }

            // Sideways scroll, or shift+scroll, pans. Converted through the live geometry so one
            // notch always covers the same number of pixels — at high zoom that is a smaller slice
            // of the clip, which is exactly what "scroll the whole timeline" needs it to be.
            float sidewaysDelta = Mathf.Abs(wheelEvent.delta.x) > 0.01f
                ? wheelEvent.delta.x
                : (wheelEvent.shiftKey ? wheelEvent.delta.y : 0f);
            if (Mathf.Abs(sidewaysDelta) < 0.01f)
            {
                // Plain vertical scroll belongs to the scroll view, so a deep clip still scrolls.
                return;
            }

            const float PixelsPerWheelUnit = 18f;
            TimelineGeometry panGeometry = TimelineGeometry.Create(LaneWidth, viewZoom, viewPan);
            viewPan += sidewaysDelta * PixelsPerWheelUnit / panGeometry.PixelsPerNormalizedUnit;
            ApplyTimelineView();
            wheelEvent.StopPropagation();
        }

        /// <summary>
        /// Rebuilds the horizontal scroller range from the current zoom and the keys on screen, so
        /// the thumb always reflects the current zoom and can reach any out-of-range key.
        /// </summary>
        private void SyncHorizontalScroller()
        {
            if (horizontalScroller == null)
            {
                return;
            }

            float visibleSpan = 1f / Mathf.Max(TimelineGeometry.MinimumZoom, viewZoom);

            float contentStart = Mathf.Min(0f, viewPan);
            float contentEnd = Mathf.Max(1f, viewPan + visibleSpan);
            if (laneColumn != null)
            {
                for (int childIndex = 0; childIndex < laneColumn.childCount; childIndex++)
                {
                    TrackLaneElement lane = laneColumn[childIndex] as TrackLaneElement;
                    if (lane == null)
                    {
                        continue;
                    }
                    IReadOnlyList<float> times = lane.KeyTimes;
                    for (int keyIndex = 0; keyIndex < times.Count; keyIndex++)
                    {
                        contentStart = Mathf.Min(contentStart, times[keyIndex]);
                        contentEnd = Mathf.Max(contentEnd, times[keyIndex]);
                    }
                }
            }

            // A margin of one screenful either side, so there is always somewhere to scroll to and
            // a key sitting exactly on the boundary is not pinned against the end of the bar.
            contentStart -= visibleSpan * 0.5f;
            contentEnd += visibleSpan * 0.5f;

            float scrollableSpan = Mathf.Max(0f, (contentEnd - contentStart) - visibleSpan);

            isSyncingHorizontalScroller = true;
            try
            {
                horizontalScroller.lowValue = contentStart;
                horizontalScroller.highValue = contentStart + scrollableSpan;
                horizontalScroller.value = Mathf.Clamp(
                    viewPan, contentStart, contentStart + scrollableSpan);

                // Thumb size as a fraction of the whole bar, which is what makes the scrollbar read
                // as "how much of the timeline you are looking at".
                float totalSpan = Mathf.Max(1e-4f, contentEnd - contentStart);
                horizontalScroller.Adjust(Mathf.Clamp01(visibleSpan / totalSpan));
            }
            finally
            {
                isSyncingHorizontalScroller = false;
            }
        }

        private void OnTimelinePanDown(PointerDownEvent pointerEvent)
        {
            // Middle mouse, the button no other timeline gesture claims.
            if (pointerEvent.button != 2)
            {
                return;
            }
            isPanningView = true;
            panPointerStartX = pointerEvent.position.x;
            panStartValue = viewPan;
            timelineScroll.CapturePointer(pointerEvent.pointerId);
            pointerEvent.StopPropagation();
        }

        private void OnTimelinePanMove(PointerMoveEvent moveEvent)
        {
            if (!isPanningView)
            {
                return;
            }
            TimelineGeometry geometry = TimelineGeometry.Create(LaneWidth, viewZoom, viewPan);
            float deltaPixels = moveEvent.position.x - panPointerStartX;
            viewPan = panStartValue - deltaPixels / geometry.PixelsPerNormalizedUnit;
            ApplyTimelineView();
            moveEvent.StopPropagation();
        }

        private void OnTimelinePanUp(PointerUpEvent upEvent)
        {
            if (!isPanningView)
            {
                return;
            }
            isPanningView = false;
            timelineScroll.ReleasePointer(upEvent.pointerId);
        }

        // -----------------------------------------------------------------------------------
        // Framing.
        // -----------------------------------------------------------------------------------

        private void FrameAll()
        {
            FrameRange(0f, 1f);
        }

        private void FrameSelection()
        {
            if (selectedClip == null || session.SelectedKeys.Count == 0)
            {
                FrameAll();
                return;
            }

            float earliest = float.MaxValue;
            float latest = float.MinValue;
            int resolved = 0;
            foreach (KeyAddress address in session.SelectedKeys)
            {
                float keyTime;
                if (!TryGetSelectedKeyTime(address, out keyTime))
                {
                    continue;
                }
                earliest = Mathf.Min(earliest, keyTime);
                latest = Mathf.Max(latest, keyTime);
                resolved++;
            }
            if (resolved == 0)
            {
                FrameAll();
                return;
            }

            // A single key has no extent, so frame a window around it rather than dividing by zero.
            if (Mathf.Approximately(earliest, latest))
            {
                earliest -= 0.05f;
                latest += 0.05f;
            }
            FrameRange(earliest, latest);
        }

        /// <summary>The authored time of one selected key, or false when the address no longer resolves.</summary>
        // Every index is bounds-checked rather than trusted: an address can outlive the key it
        // pointed at (a delete or undo renumbers the list), and a stale one here would throw during repaint.
        private bool TryGetSelectedKeyTime(KeyAddress address, out float normalizedTime)
        {
            normalizedTime = 0f;
            if (selectedClip == null)
            {
                return false;
            }

            switch (address.trackKind)
            {
                case TimelineTrackKind.Transform:
                {
                    List<TransformTrack> tracks = selectedClip.transformTracks;
                    if (tracks == null || address.trackIndex < 0 || address.trackIndex >= tracks.Count)
                    {
                        return false;
                    }
                    TransformTrack track = tracks[address.trackIndex];
                    if (track == null || track.keys == null
                        || address.keyIndex < 0 || address.keyIndex >= track.keys.Count)
                    {
                        return false;
                    }
                    normalizedTime = track.keys[address.keyIndex].normalizedTime;
                    return true;
                }
                case TimelineTrackKind.Sprite:
                {
                    List<SpriteTrack> tracks = selectedClip.spriteTracks;
                    if (tracks == null || address.trackIndex < 0 || address.trackIndex >= tracks.Count)
                    {
                        return false;
                    }
                    SpriteTrack track = tracks[address.trackIndex];
                    if (track == null || track.keys == null
                        || address.keyIndex < 0 || address.keyIndex >= track.keys.Count)
                    {
                        return false;
                    }
                    normalizedTime = track.keys[address.keyIndex].normalizedTime;
                    return true;
                }
                case TimelineTrackKind.Bone:
                {
                    List<BoneTrack> tracks = selectedClip.boneTracks;
                    if (tracks == null || address.trackIndex < 0 || address.trackIndex >= tracks.Count)
                    {
                        return false;
                    }
                    BoneTrack track = tracks[address.trackIndex];
                    if (track == null || track.keys == null
                        || address.keyIndex < 0 || address.keyIndex >= track.keys.Count)
                    {
                        return false;
                    }
                    normalizedTime = track.keys[address.keyIndex].normalizedTime;
                    return true;
                }
                default:
                {
                    List<EventMarker> markers = selectedClip.events;
                    int flatIndex = EventLaneAddressing.ResolveFlatIndex(
                        markers, address.trackIndex, address.keyIndex);
                    if (markers == null || flatIndex < 0)
                    {
                        return false;
                    }
                    normalizedTime = markers[flatIndex].normalizedTime;
                    return true;
                }
            }
        }

        /// <summary>Fits a normalized range, with a margin so the outermost keys are not on the edge.</summary>
        private void FrameRange(float fromTime, float toTime)
        {
            float span = Mathf.Max(1e-4f, toTime - fromTime);
            const float MarginFraction = 0.06f;
            float padded = span * (1f + MarginFraction * 2f);

            viewZoom = Mathf.Clamp(
                1f / padded, TimelineGeometry.MinimumZoom, MaximumViewZoom);
            viewPan = fromTime - span * MarginFraction;
            ApplyTimelineView();
        }
    }
}
