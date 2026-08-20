// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// The timeline's view transform: zoom, pan, and the ways a user changes them.
    /// </summary>
    /// <remarks>
    /// <strong>The window owns the view and pushes it into every element.</strong> Nothing derives
    /// its own — a lane that zoomed its painting but not its hit-testing is the exact drift
    /// <c>TimelineGeometry</c> was written to make unrepresentable.
    /// </remarks>
    public sealed partial class ClipEditorWindow
    {
        private const string ZoomPrefKey = "DotsAnimationToolkit.ClipEditor.Zoom";
        private const string PanPrefKey = "DotsAnimationToolkit.ClipEditor.Pan";

        private float viewZoom = 1f;
        private float viewPan;

        private ScrollView timelineScroll;
        private Slider zoomSlider;
        private bool isSyncingZoomSlider;
        private bool isPanningView;
        private float panPointerStartX;
        private float panStartValue;

        private void BindTimelineView()
        {
            viewZoom = Mathf.Clamp(
                EditorPrefs.GetFloat(ZoomPrefKey, 1f),
                TimelineGeometry.MinimumZoom, TimelineGeometry.MaximumZoom);
            viewPan = EditorPrefs.GetFloat(PanPrefKey, 0f);

            timelineScroll = rootVisualElement.Q<ScrollView>("timeline-scroll");
            zoomSlider = rootVisualElement.Q<Slider>("zoom-slider");
            if (zoomSlider != null)
            {
                zoomSlider.tooltip =
                    "Timeline zoom. Ctrl+scroll over the timeline zooms toward the cursor; "
                    + "middle-drag pans. Panning past either end keeps out-of-range keys reachable.";
                zoomSlider.SetValueWithoutNotify(viewZoom);
                zoomSlider.RegisterValueChangedCallback(changeEvent =>
                {
                    if (isSyncingZoomSlider)
                    {
                        return;
                    }
                    // The slider has no cursor to anchor on, so it holds the view CENTRE — the
                    // thing a user is looking at when they drag a zoom slider.
                    SetZoomAnchored(changeEvent.newValue, 0.5f);
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

            if (timelineScroll != null)
            {
                timelineScroll.RegisterCallback<WheelEvent>(OnTimelineWheel);
                timelineScroll.RegisterCallback<PointerDownEvent>(OnTimelinePanDown);
                timelineScroll.RegisterCallback<PointerMoveEvent>(OnTimelinePanMove);
                timelineScroll.RegisterCallback<PointerUpEvent>(OnTimelinePanUp);
            }
        }

        /// <summary>Applies a new zoom while holding one normalized position under one screen point.</summary>
        /// <param name="anchorFraction">Where in the track area to hold, 0 = left edge, 1 = right.</param>
        private void SetZoomAnchored(float newZoom, float anchorFraction)
        {
            float laneWidth = LaneWidth;
            TimelineGeometry before = TimelineGeometry.Create(laneWidth, viewZoom, viewPan);
            float anchorX = before.leftPadding + before.TrackPixelWidth * Mathf.Clamp01(anchorFraction);
            float timeUnderAnchor = before.XToTime(anchorX);

            viewZoom = Mathf.Clamp(newZoom, TimelineGeometry.MinimumZoom, TimelineGeometry.MaximumZoom);

            TimelineGeometry after = TimelineGeometry.Create(laneWidth, viewZoom, viewPan);
            viewPan = after.PanToAnchor(timeUnderAnchor, anchorX);
            ApplyTimelineView();
        }

        private float LaneWidth
        {
            get
            {
                if (laneColumn != null && laneColumn.contentRect.width > 1f)
                {
                    return laneColumn.contentRect.width;
                }
                return 600f;
            }
        }

        /// <summary>Pushes the view into every element that paints or hit-tests against it.</summary>
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

            if (ruler != null)
            {
                ruler.viewZoom = viewZoom;
                ruler.viewPan = viewPan;
                // Labels are children, so they are rebuilt here rather than during the repaint the
                // line below schedules.
                ruler.RefreshSecondLabels();
                ruler.MarkDirtyRepaint();
            }
            if (playhead != null)
            {
                playhead.viewZoom = viewZoom;
                playhead.viewPan = viewPan;
                playhead.MarkDirtyRepaint();
            }
            if (laneColumn != null)
            {
                laneColumn.Query<TrackLaneElement>().ForEach(lane =>
                {
                    lane.viewZoom = viewZoom;
                    lane.viewPan = viewPan;
                    lane.MarkDirtyRepaint();
                });
            }
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
            if (!wheelEvent.ctrlKey && !wheelEvent.commandKey)
            {
                return;
            }

            float laneWidth = LaneWidth;
            TimelineGeometry geometry = TimelineGeometry.Create(laneWidth, viewZoom, viewPan);
            float localX = laneColumn != null
                ? wheelEvent.mousePosition.x - laneColumn.worldBound.xMin
                : geometry.leftPadding + geometry.TrackPixelWidth * 0.5f;
            float anchorFraction = (localX - geometry.leftPadding) / geometry.TrackPixelWidth;

            // Multiplicative, so a step feels the same at every zoom — an additive step crawls when
            // far in and jumps when far out.
            float factor = wheelEvent.delta.y > 0f ? 1f / 1.15f : 1.15f;
            SetZoomAnchored(viewZoom * factor, anchorFraction);
            wheelEvent.StopPropagation();
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
            if (selectedClip == null || selectedKeys.Count == 0)
            {
                FrameAll();
                return;
            }

            float earliest = float.MaxValue;
            float latest = float.MinValue;
            int resolved = 0;
            foreach (KeyAddress address in selectedKeys)
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

        /// <summary>
        /// The authored time of one selected key, or false when the address no longer resolves.
        /// </summary>
        /// <remarks>
        /// Addresses are positions, not references, so one can outlive the key it pointed at — a
        /// delete or an undo renumbers the list. Every index is bounds-checked rather than trusted,
        /// because a stale address here would throw during a repaint.
        /// </remarks>
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
                    if (markers == null || address.keyIndex < 0 || address.keyIndex >= markers.Count)
                    {
                        return false;
                    }
                    normalizedTime = markers[address.keyIndex].normalizedTime;
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
                1f / padded, TimelineGeometry.MinimumZoom, TimelineGeometry.MaximumZoom);
            viewPan = fromTime - span * MarginFraction;
            ApplyTimelineView();
        }
    }
}
