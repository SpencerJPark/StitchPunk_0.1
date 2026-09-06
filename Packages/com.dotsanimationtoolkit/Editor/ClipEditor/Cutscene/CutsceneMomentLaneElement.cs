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
        private int selectedIndex = -1;
        private int draggingIndex = -1;
        private float dragStartPointerX;
        private float dragStartTime;
        private bool draggedPastThreshold;

        /// <summary>Pixels per second, pushed in by the panel so this lane agrees with the ruler.</summary>
        public float pixelsPerSecond = 40f;

        /// <summary>Marker fill color — distinguishes a facing lane from an event lane at a glance.</summary>
        public Color markerColor = new Color(0.55f, 0.75f, 0.95f);

        /// <summary>Raised when a marker is clicked (selected), or -1 when empty space is clicked.</summary>
        public event Action<int> MomentSelected;

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
            IReadOnlyList<string> markerVariantClasses, IReadOnlyList<bool> markerReadOnlyFlags)
        {
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
                marker.style.backgroundColor = markerColor;
                // Shape lives in USS, not in an inline style: an inline rotate would outrank the
                // variant classes below, and a Detach marker must be able to stop being a diamond.
                if (capturedIndex < variantClasses.Count && !string.IsNullOrEmpty(variantClasses[capturedIndex]))
                {
                    marker.AddToClassList(variantClasses[capturedIndex]);
                }
                marker.EnableInClassList(SelectedMarkerUssClassName, capturedIndex == selectedIndex);
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

            CutsceneTimelineGeometry geometry = CutsceneTimelineGeometry.Create(pixelsPerSecond);
            float newTime = Mathf.Max(0f, dragStartTime + deltaPixels / geometry.pixelsPerSecond);
            times[index] = newTime;
            PositionMarker(marker, newTime);
            MomentMoved?.Invoke(index, newTime);
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
                MomentMoveCommitted?.Invoke(index, times[index]);
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
            }
            else
            {
                MomentSelected?.Invoke(-1);
            }
        }
    }
}
