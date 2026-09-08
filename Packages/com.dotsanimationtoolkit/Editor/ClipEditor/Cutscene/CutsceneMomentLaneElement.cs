// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>One reusable lane of point-in-time markers, shared by root keys, facing overrides, camera keys, event markers, and hold markers.</summary>
    public sealed class CutsceneMomentLaneElement : VisualElement
    {
        public const string UssClassName = "cutscene-editor__moment-lane";
        private const string MarkerUssClassName = "cutscene-editor__moment-marker";
        private const string SelectedMarkerUssClassName = "cutscene-editor__moment-marker--selected";
        private const float MarkerSize = 10f;
        private const float DragThresholdPixels = 3f;

        private readonly List<VisualElement> markerElements = new List<VisualElement>();
        private readonly List<float> times = new List<float>();
        private readonly List<string> variantClasses = new List<string>();
        private readonly List<bool> readOnlyFlags = new List<bool>();
        private readonly List<float> dragStartTimes = new List<float>();
        // Null means every marker takes the lane colour.
        private IReadOnlyList<Color> perMarkerColors;
        private int selectedIndex = -1;
        private int draggingIndex = -1;
        private float dragStartPointerX;
        private float dragStartTime;
        private bool draggedPastThreshold;

        /// <summary>Pixels per second, pushed in by the panel so this lane agrees with the ruler.</summary>
        public float pixelsPerSecond = 40f;

        /// <summary>Marker fill color — distinguishes a facing lane from an event lane at a glance.</summary>
        public Color markerColor = new Color(0.55f, 0.75f, 0.95f);

        // Asked per marker on every rebuild and every in-place refresh, so a lane draws the panel's
        // whole selection set rather than the one index it was handed. Null means "only selectedIndex".
        /// <summary>Whether the item at an index is in the panel's selection.</summary>
        public Func<int, bool> isItemSelected;

        /// <summary>Raised when a marker is clicked (selected), or -1 when empty space is clicked.</summary>
        public event Action<int> MomentSelected;

        /// <summary>Raised on a marker press, before any drag, with (index, toggles the item, adds to the set).</summary>
        public event Action<int, bool, bool> MomentPointerDown;

        /// <summary>Raised on every pointer move while dragging, with the drag's total time delta.</summary>
        public event Action<float> SelectionDragMoved;

        /// <summary>Raised once on release, with the final time delta to write across the selection.</summary>
        public event Action<float> SelectionDragCommitted;

        /// <summary>Raised on a press in empty lane space, handing over the event so the panel can start a band drag.</summary>
        public event Action<PointerDownEvent> BackgroundPointerDown;

        /// <summary>Raised on every pointer move while dragging a marker — visual/live-preview only, never authored.</summary>
        public event Action<int, float> MomentMoved;

        // Drag is visual-only until release: the authored data is written once, here, so many
        // visual frames collapse into one Undo step.
        /// <summary>Raised once, on release, with the final time to actually write.</summary>
        public event Action<int, float> MomentMoveCommitted;

        /// <summary>Raised on an empty-space double-click, with the time under the cursor.</summary>
        public event Action<float> EmptySpaceDoubleClicked;

        /// <summary>Raised from a marker's "Delete" context menu entry.</summary>
        public event Action<int> MomentDeleteRequested;

        public CutsceneMomentLaneElement()
        {
            AddToClassList(UssClassName);
            RegisterCallback<PointerDownEvent>(OnBackgroundPointerDown);
        }

        /// <summary>Rebuilds every marker from a fresh snapshot of times. Call after any authored change.</summary>
        public void SetTimes(IReadOnlyList<float> newTimes, int newSelectedIndex)
        {
            SetTimes(newTimes, newSelectedIndex, null);
        }

        /// <summary>
        /// As <see cref="SetTimes(IReadOnlyList{float}, int)"/>, plus one extra USS class per marker
        /// so a lane whose moments are not all the same kind can say so by shape — the attach lane's
        /// Attach and Detach. Null entries are simply skipped.
        /// </summary>
        public void SetTimes(IReadOnlyList<float> newTimes, int newSelectedIndex, IReadOnlyList<string> markerVariantClasses)
        {
            SetTimes(newTimes, newSelectedIndex, markerVariantClasses, null);
        }

        /// <summary>
        /// As the variant-class overload, plus a per-marker read-only flag: a read-only marker is
        /// drawn and never picked, so a lane can show a moment it does not own — the ghost a holding
        /// event casts onto the Holds row, whose editable half is the event.
        /// </summary>
        public void SetTimes(
            IReadOnlyList<float> newTimes, int newSelectedIndex,
            IReadOnlyList<string> markerVariantClasses, IReadOnlyList<bool> markerReadOnlyFlags,
            IReadOnlyList<Color> markerColors = null)
        {
            perMarkerColors = markerColors;
            times.Clear();
            if (newTimes != null)
            {
                times.AddRange(newTimes);
            }
            variantClasses.Clear();
            if (markerVariantClasses != null)
            {
                for (int index = 0; index < markerVariantClasses.Count; index++)
                {
                    variantClasses.Add(markerVariantClasses[index]);
                }
            }
            readOnlyFlags.Clear();
            if (markerReadOnlyFlags != null)
            {
                for (int index = 0; index < markerReadOnlyFlags.Count; index++)
                {
                    readOnlyFlags.Add(markerReadOnlyFlags[index]);
                }
            }
            selectedIndex = newSelectedIndex;
            Rebuild();
        }

        private void Rebuild()
        {
            Clear();
            markerElements.Clear();

            for (int index = 0; index < times.Count; index++)
            {
                int capturedIndex = index;
                VisualElement marker = new VisualElement();
                marker.AddToClassList(MarkerUssClassName);
                marker.style.position = Position.Absolute;
                marker.style.width = MarkerSize;
                marker.style.height = MarkerSize;
                marker.style.top = 2f;
                marker.style.backgroundColor = perMarkerColors != null && capturedIndex < perMarkerColors.Count
                    ? perMarkerColors[capturedIndex]
                    : markerColor;
                // Shape lives in USS, not in an inline style: an inline rotate would outrank the
                // variant classes below, and a Detach marker must be able to stop being a diamond.
                if (capturedIndex < variantClasses.Count && !string.IsNullOrEmpty(variantClasses[capturedIndex]))
                {
                    marker.AddToClassList(variantClasses[capturedIndex]);
                }
                marker.EnableInClassList(SelectedMarkerUssClassName, IsSelected(capturedIndex));
                PositionMarker(marker, times[capturedIndex]);

                if (capturedIndex < readOnlyFlags.Count && readOnlyFlags[capturedIndex])
                {
                    // Ignored rather than merely callback-less, so a click falls through to the
                    // lane behind it and still selects (or double-click-adds) as if it were empty.
                    marker.pickingMode = PickingMode.Ignore;
                    Add(marker);
                    markerElements.Add(marker);
                    continue;
                }

                marker.RegisterCallback<PointerDownEvent>(
                    pointerEvent => OnMarkerPointerDown(pointerEvent, capturedIndex, marker));
                marker.RegisterCallback<PointerMoveEvent>(
                    pointerEvent => OnMarkerPointerMove(pointerEvent, capturedIndex, marker));
                marker.RegisterCallback<PointerUpEvent>(
                    pointerEvent => OnMarkerPointerUp(pointerEvent, capturedIndex, marker));
                marker.AddManipulator(new ContextualMenuManipulator(
                    menuEvent => menuEvent.menu.AppendAction(
                        "Delete", _ => MomentDeleteRequested?.Invoke(capturedIndex))));

                Add(marker);
                markerElements.Add(marker);
            }
        }

        private bool IsSelected(int index)
        {
            return isItemSelected != null ? isItemSelected(index) : index == selectedIndex;
        }

        /// <summary>Repaints which markers look selected, without tearing the lane down mid-gesture.</summary>
        public void RefreshSelectionVisuals()
        {
            for (int index = 0; index < markerElements.Count; index++)
            {
                markerElements[index].EnableInClassList(SelectedMarkerUssClassName, IsSelected(index));
            }
        }

        /// <summary>Draws every selected marker shifted by a delta, previewing a group drag before anything is written.</summary>
        public void PreviewOffsetForSelected(float deltaSeconds)
        {
            if (dragStartTimes.Count != times.Count)
            {
                CaptureDragStartTimes();
            }
            for (int index = 0; index < markerElements.Count && index < dragStartTimes.Count; index++)
            {
                if (!IsSelected(index))
                {
                    continue;
                }
                float previewTime = Mathf.Max(0f, dragStartTimes[index] + deltaSeconds);
                times[index] = previewTime;
                PositionMarker(markerElements[index], previewTime);
            }
        }

        /// <summary>Every item whose marker falls inside a band given in this lane's own space.</summary>
        public void CollectItemsInBand(Rect bandInLaneSpace, List<int> collected)
        {
            if (collected == null)
            {
                return;
            }
            CutsceneTimelineGeometry geometry = CutsceneTimelineGeometry.Create(pixelsPerSecond);
            for (int index = 0; index < times.Count; index++)
            {
                if (index < readOnlyFlags.Count && readOnlyFlags[index])
                {
                    continue;
                }
                float markerX = geometry.TimeToX(times[index]);
                if (markerX >= bandInLaneSpace.xMin && markerX <= bandInLaneSpace.xMax)
                {
                    collected.Add(index);
                }
            }
        }

        // The pre-drag times, so every preview frame offsets from where the group started rather
        // than from where the previous frame left it.
        private void CaptureDragStartTimes()
        {
            dragStartTimes.Clear();
            dragStartTimes.AddRange(times);
        }

        private void PositionMarker(VisualElement marker, float timeSeconds)
        {
            float x = CutsceneTimelineGeometry.Create(pixelsPerSecond).TimeToX(timeSeconds);
            marker.style.left = x - MarkerSize * 0.5f;
        }

        private void OnMarkerPointerDown(PointerDownEvent pointerEvent, int index, VisualElement marker)
        {
            marker.CapturePointer(pointerEvent.pointerId);
            draggingIndex = index;
            draggedPastThreshold = false;
            dragStartPointerX = pointerEvent.position.x;
            dragStartTime = times[index];
            CaptureDragStartTimes();

            // Selection resolves on press, not on release: a drag has to know what it is moving
            // before it starts moving it, and the panel answers isItemSelected out of that set.
            MomentPointerDown?.Invoke(
                index,
                pointerEvent.ctrlKey || pointerEvent.commandKey,
                pointerEvent.shiftKey);
            pointerEvent.StopPropagation();
        }

        private void OnMarkerPointerMove(PointerMoveEvent moveEvent, int index, VisualElement marker)
        {
            if (draggingIndex != index || !marker.HasPointerCapture(moveEvent.pointerId))
            {
                return;
            }

            float deltaPixels = moveEvent.position.x - dragStartPointerX;
            if (!draggedPastThreshold && Mathf.Abs(deltaPixels) < DragThresholdPixels)
            {
                return;
            }
            draggedPastThreshold = true;

            float deltaSeconds = deltaPixels
                / CutsceneTimelineGeometry.Create(pixelsPerSecond).pixelsPerSecond;

            // Clamped against the dragged marker alone; the commit re-clamps against whatever else
            // is selected, which may reach further left than this one does.
            deltaSeconds = Mathf.Max(deltaSeconds, -dragStartTime);
            SelectionDragMoved?.Invoke(deltaSeconds);
            MomentMoved?.Invoke(index, dragStartTime + deltaSeconds);
        }

        private void OnMarkerPointerUp(PointerUpEvent upEvent, int index, VisualElement marker)
        {
            if (draggingIndex != index)
            {
                return;
            }
            marker.ReleasePointer(upEvent.pointerId);
            draggingIndex = -1;

            if (draggedPastThreshold)
            {
                float deltaSeconds = Mathf.Max(
                    (upEvent.position.x - dragStartPointerX)
                        / CutsceneTimelineGeometry.Create(pixelsPerSecond).pixelsPerSecond,
                    -dragStartTime);
                SelectionDragCommitted?.Invoke(deltaSeconds);
                MomentMoveCommitted?.Invoke(index, dragStartTime + deltaSeconds);
            }
            else
            {
                MomentSelected?.Invoke(index);
            }
        }

        private void OnBackgroundPointerDown(PointerDownEvent pointerEvent)
        {
            if (pointerEvent.target != this)
            {
                // A marker's own handler already claimed this — background handling only applies to
                // clicks that hit empty lane space.
                return;
            }

            if (pointerEvent.clickCount >= 2)
            {
                float time = CutsceneTimelineGeometry.Create(pixelsPerSecond)
                    .XToTime(pointerEvent.localPosition.x);
                EmptySpaceDoubleClicked?.Invoke(time);
                return;
            }

            // Deliberately not raising the empty-space selection here: the panel cannot yet know
            // whether this press is a click that clears the set or the start of an additive band.
            BackgroundPointerDown?.Invoke(pointerEvent);
        }
    }
}
