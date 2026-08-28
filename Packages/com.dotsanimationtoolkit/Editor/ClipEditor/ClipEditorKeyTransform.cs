// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Modal grab and scale for the selected keys — the Blender <c>G</c> and <c>S</c> gestures.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Modal, not drag-based, and that is the point.</strong> A drag can only express what
    /// the hand does between press and release; a modal operator can be steered with the mouse,
    /// corrected with a typed number, snapped or unsnapped mid-gesture, and abandoned with Escape
    /// leaving nothing behind. Retiming is exactly the kind of edit that wants all four.
    /// </para>
    /// <para>
    /// Every update recomputes each key's time from the time it had when the gesture began, never
    /// from the time it has now. Accumulating deltas instead would drift as the pointer moved back
    /// and forth, and — worse — would make snapping sticky, since a snapped value would become the
    /// base the next delta was measured from.
    /// </para>
    /// </remarks>
    public sealed partial class ClipEditorWindow
    {
        private const string PivotPrefKey = "DotsAnimationToolkit.ClipEditor.TransformPivot";

        /// <summary>Which gesture is running, if any.</summary>
        private enum KeyTransformKind
        {
            None,
            Grab,
            Scale
        }

        /// <summary>The fixed point a scale works about.</summary>
        private enum KeyTransformPivot
        {
            /// <summary>The playhead. The default: it is visible, and the user put it there.</summary>
            Playhead,

            /// <summary>Midway between the earliest and latest selected key.</summary>
            SelectionCenter,

            /// <summary>The earliest selected key, so the selection stretches forward from its start.</summary>
            SelectionStart
        }

        /// <summary>
        /// One track's key times as they were when the gesture began, plus where each has moved to.
        /// </summary>
        /// <remarks>
        /// <see cref="currentIndexOfOriginal"/> is what survives a re-sort. Scaling through a
        /// negative factor mirrors the selection, which reverses its keys, and after that a key's
        /// index is no longer the index it started at. Composing the sort's index map into this
        /// array each update keeps "original key 3" addressable however far it has moved.
        /// </remarks>
        private sealed class KeyTransformTrackSnapshot
        {
            public TimelineTrackKind trackKind;
            public int trackIndex;
            public float[] originalTimes;
            public bool[] isSelected;
            public int[] currentIndexOfOriginal;
        }

        private KeyTransformKind activeTransform = KeyTransformKind.None;
        private KeyTransformPivot transformPivotMode = KeyTransformPivot.Playhead;
        private readonly List<KeyTransformTrackSnapshot> transformSnapshots =
            new List<KeyTransformTrackSnapshot>();

        private float transformPivotTime;
        private float transformPointerStartX;
        private float transformPointerCurrentX;
        private string transformTypedValue = string.Empty;
        private bool transformSnapSuppressed;

        private Label transformReadout;
        private DropdownField pivotDropdown;

        private bool IsTransformActive
        {
            get { return activeTransform != KeyTransformKind.None; }
        }

        // -----------------------------------------------------------------------------------
        // Binding.
        // -----------------------------------------------------------------------------------

        private void BindKeyTransform()
        {
            transformPivotMode = (KeyTransformPivot)EditorPrefs.GetInt(
                PivotPrefKey, (int)KeyTransformPivot.Playhead);

            transformReadout = rootVisualElement.Q<Label>("transform-readout");
            if (transformReadout != null)
            {
                transformReadout.style.display = DisplayStyle.None;
            }

            pivotDropdown = rootVisualElement.Q<DropdownField>("pivot-dropdown");
            if (pivotDropdown != null)
            {
                // Each choice is a whole sentence about what S will do, because the "Pivot" label
                // that used to head this control is gone: a bare "Playhead" in a bar that already
                // has a playhead readout says nothing about scaling.
                pivotDropdown.choices = new List<string>
                {
                    "Scale From Playhead", "Scale From Center", "Scale From Start"
                };
                pivotDropdown.index = (int)transformPivotMode;
                pivotDropdown.tooltip =
                    "The fixed point S scales the selected keys about. G ignores it.";
                pivotDropdown.RegisterValueChangedCallback(changeEvent =>
                {
                    transformPivotMode = (KeyTransformPivot)Mathf.Max(0, pivotDropdown.index);
                    EditorPrefs.SetInt(PivotPrefKey, (int)transformPivotMode);
                });
            }

            // Pointer motion steers a running gesture, and any click confirms it. Registered on the
            // root in the trickle-down phase so the lanes cannot swallow the click as a fresh
            // selection before the gesture has seen it.
            rootVisualElement.RegisterCallback<PointerMoveEvent>(OnTransformPointerMove);
            rootVisualElement.RegisterCallback<PointerDownEvent>(
                OnTransformPointerDown, TrickleDown.TrickleDown);
        }

        // -----------------------------------------------------------------------------------
        // Lifecycle.
        // -----------------------------------------------------------------------------------

        /// <summary>Starts a gesture, or restarts as the other kind if one is already running.</summary>
        private void BeginKeyTransform(KeyTransformKind kind)
        {
            if (IsTransformActive)
            {
                // Pressing S during a G means "I meant scale". Cancelling first makes the second
                // gesture measure from the untouched times rather than the half-moved ones.
                CancelKeyTransform();
            }

            if (selectedClip == null || selectedKeys.Count == 0)
            {
                statusLabel.text = "Select keys first — G moves them, S scales them.";
                return;
            }

            if (!CaptureTransformSnapshots())
            {
                return;
            }

            activeTransform = kind;
            transformTypedValue = string.Empty;
            transformSnapSuppressed = false;
            transformPivotTime = ResolvePivotTime();
            transformPointerStartX = PlayheadLaneX();
            transformPointerCurrentX = transformPointerStartX;

            BeginUndoGesture(kind == KeyTransformKind.Grab
                ? "Move Animation Keys" : "Scale Animation Keys");

            ApplyKeyTransform();
        }

        /// <summary>
        /// Records every key on every track the selection touches, not only the selected ones.
        /// </summary>
        /// <remarks>
        /// The unselected keys are recorded because they take part in the sort. Their times never
        /// change, but their indices do, and rewriting the whole track from one snapshot each update
        /// is what keeps times and indices in step.
        /// </remarks>
        private bool CaptureTransformSnapshots()
        {
            transformSnapshots.Clear();

            foreach (KeyAddress address in selectedKeys)
            {
                KeyTransformTrackSnapshot snapshot =
                    FindSnapshot(address.trackKind, address.trackIndex);
                if (snapshot == null)
                {
                    snapshot = CreateSnapshot(address.trackKind, address.trackIndex);
                    if (snapshot == null)
                    {
                        continue;
                    }
                    transformSnapshots.Add(snapshot);
                }
                if (address.keyIndex >= 0 && address.keyIndex < snapshot.isSelected.Length)
                {
                    snapshot.isSelected[address.keyIndex] = true;
                }
            }

            return transformSnapshots.Count > 0;
        }

        private KeyTransformTrackSnapshot FindSnapshot(TimelineTrackKind trackKind, int trackIndex)
        {
            for (int index = 0; index < transformSnapshots.Count; index++)
            {
                KeyTransformTrackSnapshot candidate = transformSnapshots[index];
                if (candidate.trackKind == trackKind && candidate.trackIndex == trackIndex)
                {
                    return candidate;
                }
            }
            return null;
        }

        private KeyTransformTrackSnapshot CreateSnapshot(TimelineTrackKind trackKind, int trackIndex)
        {
            int keyCount = CountKeysOnTrack(trackKind, trackIndex);
            if (keyCount <= 0)
            {
                return null;
            }

            KeyTransformTrackSnapshot snapshot = new KeyTransformTrackSnapshot
            {
                trackKind = trackKind,
                trackIndex = trackIndex,
                originalTimes = new float[keyCount],
                isSelected = new bool[keyCount],
                currentIndexOfOriginal = new int[keyCount]
            };
            for (int keyIndex = 0; keyIndex < keyCount; keyIndex++)
            {
                snapshot.originalTimes[keyIndex] =
                    GetKeyTime(new KeyAddress(trackKind, trackIndex, keyIndex));
                snapshot.currentIndexOfOriginal[keyIndex] = keyIndex;
            }
            return snapshot;
        }

        private int CountKeysOnTrack(TimelineTrackKind trackKind, int trackIndex)
        {
            if (selectedClip == null)
            {
                return 0;
            }
            switch (trackKind)
            {
                case TimelineTrackKind.Transform:
                    return trackIndex >= 0 && trackIndex < selectedClip.transformTracks.Count
                        ? selectedClip.transformTracks[trackIndex].keys.Count : 0;
                case TimelineTrackKind.Sprite:
                    return trackIndex >= 0 && trackIndex < selectedClip.spriteTracks.Count
                        ? selectedClip.spriteTracks[trackIndex].keys.Count : 0;
                case TimelineTrackKind.Bone:
                    return selectedClip.boneTracks != null
                        && trackIndex >= 0 && trackIndex < selectedClip.boneTracks.Count
                        ? selectedClip.boneTracks[trackIndex].keys.Count : 0;
                default:
                    return EventLaneAddressing.ResolveLaneFlatIndices(
                        selectedClip.events, trackIndex).Count;
            }
        }

        /// <summary>Confirms the gesture, leaving one undo step behind.</summary>
        private void ConfirmKeyTransform()
        {
            if (!IsTransformActive)
            {
                return;
            }
            activeTransform = KeyTransformKind.None;
            transformSnapshots.Clear();
            HideTransformReadout();

            EditorUtility.SetDirty(selectedClip);
            EndUndoGesture();
            RebuildTimeline();
            RebuildInspector();
        }

        /// <summary>Abandons the gesture, leaving nothing behind — not even an undo entry.</summary>
        private void CancelKeyTransform()
        {
            if (!IsTransformActive)
            {
                return;
            }
            activeTransform = KeyTransformKind.None;

            RestoreOriginalTimes();
            transformSnapshots.Clear();
            HideTransformReadout();

            // The times are already back, so this is about the undo stack rather than the data: a
            // cancelled gesture that left a "Move Animation Keys" entry behind would make the next
            // Ctrl+Z appear to do nothing.
            Undo.RevertAllDownToGroup(gestureUndoGroup);
            RefreshSerializedClip();
            MarkPreviewDirty();
            RebuildTimeline();
            RebuildInspector();
        }

        /// <summary>
        /// Drops a running gesture without restoring anything and without touching the undo stack.
        /// </summary>
        /// <remarks>
        /// For the one case where cancelling would be wrong: an undo performed mid-gesture. The
        /// snapshot holds times recorded before the undo, so restoring them would write the undone
        /// state straight back, and reverting the gesture group from inside undoRedoPerformed would
        /// re-enter it. The gesture is simply abandoned; the undo has already decided what the data
        /// should be.
        /// </remarks>
        private void DiscardKeyTransform()
        {
            if (!IsTransformActive)
            {
                return;
            }
            activeTransform = KeyTransformKind.None;
            transformSnapshots.Clear();
            HideTransformReadout();
        }

        private void RestoreOriginalTimes()
        {
            for (int snapshotIndex = 0; snapshotIndex < transformSnapshots.Count; snapshotIndex++)
            {
                KeyTransformTrackSnapshot snapshot = transformSnapshots[snapshotIndex];
                for (int originalIndex = 0;
                    originalIndex < snapshot.originalTimes.Length;
                    originalIndex++)
                {
                    SetKeyTime(
                        new KeyAddress(
                            snapshot.trackKind,
                            snapshot.trackIndex,
                            snapshot.currentIndexOfOriginal[originalIndex]),
                        snapshot.originalTimes[originalIndex]);
                }
                SortTrackKeys(snapshot.trackKind, snapshot.trackIndex);
                ComposeSnapshotIndices(snapshot);
            }
        }

        // -----------------------------------------------------------------------------------
        // The transform itself.
        // -----------------------------------------------------------------------------------

        /// <summary>Recomputes every affected key from its recorded time and repaints.</summary>
        private void ApplyKeyTransform()
        {
            if (!IsTransformActive || selectedClip == null)
            {
                return;
            }

            // Every step, not just the first: the gesture spans frames, and an unrecorded frame is
            // a change Unity never sees.
            RecordUndoGestureStep();

            bool snapping = SnapFrameCount > 0 && !transformSnapSuppressed;
            float grabDelta = 0f;
            float scaleFactor = 1f;
            if (activeTransform == KeyTransformKind.Grab)
            {
                grabDelta = ResolveGrabDelta(snapping);
            }
            else
            {
                scaleFactor = ResolveScaleFactor();
            }

            for (int snapshotIndex = 0; snapshotIndex < transformSnapshots.Count; snapshotIndex++)
            {
                KeyTransformTrackSnapshot snapshot = transformSnapshots[snapshotIndex];
                for (int originalIndex = 0;
                    originalIndex < snapshot.originalTimes.Length;
                    originalIndex++)
                {
                    float time = snapshot.originalTimes[originalIndex];
                    if (snapshot.isSelected[originalIndex])
                    {
                        if (activeTransform == KeyTransformKind.Grab)
                        {
                            time += grabDelta;
                        }
                        else
                        {
                            time = transformPivotTime + (time - transformPivotTime) * scaleFactor;
                            if (snapping)
                            {
                                time = TimelineGeometry.Snap(time, SnapFrameCount);
                            }
                        }
                    }

                    // Deliberately unclamped: pushing keys past the clip end is a legitimate step on
                    // the way to a longer clip, and the out-of-range shading exists to show it.
                    SetKeyTime(
                        new KeyAddress(
                            snapshot.trackKind,
                            snapshot.trackIndex,
                            snapshot.currentIndexOfOriginal[originalIndex]),
                        time);
                }

                SortTrackKeys(snapshot.trackKind, snapshot.trackIndex);
                ComposeSnapshotIndices(snapshot);
            }

            EditorUtility.SetDirty(selectedClip);
            MarkPreviewDirty();
            // RefreshLaneKeys, not RepaintLanes: a lane draws the times it was built with, so a
            // plain repaint would leave every diamond where it started while the data moved.
            RefreshLaneKeys();
            ShowTransformReadout(grabDelta, scaleFactor, snapping);
        }

        /// <summary>
        /// Grab distance, in normalized time.
        /// </summary>
        /// <remarks>
        /// <strong>The distance is snapped, not the resulting times.</strong> Snapping each key
        /// individually would flatten the spacing inside a selection whose keys sit off the frame
        /// grid — a move would silently become a quantize. Snapping the distance moved keeps the
        /// shape being moved intact, and <c>Quantize Keys</c> is there for when flattening is
        /// actually what is wanted. Scale snaps per key instead, because a scale changes the spacing
        /// by definition and there is no shape left to preserve.
        /// </remarks>
        private float ResolveGrabDelta(bool snapping)
        {
            float typedFrames;
            if (TryParseTypedValue(out typedFrames))
            {
                return typedFrames / Mathf.Max(1, TransportFrameCount);
            }

            TimelineGeometry geometry = TimelineGeometry.Create(LaneWidth, viewZoom, viewPan);
            float delta = (transformPointerCurrentX - transformPointerStartX)
                / geometry.PixelsPerNormalizedUnit;
            if (snapping)
            {
                delta = TimelineGeometry.Snap(delta, SnapFrameCount);
            }
            return delta;
        }

        /// <summary>
        /// The scale factor, signed so that dragging through the pivot mirrors the selection.
        /// </summary>
        private float ResolveScaleFactor()
        {
            float typedFactor;
            if (TryParseTypedValue(out typedFactor))
            {
                return typedFactor;
            }

            TimelineGeometry geometry = TimelineGeometry.Create(LaneWidth, viewZoom, viewPan);
            float pivotX = geometry.TimeToX(transformPivotTime);
            float startOffset = transformPointerStartX - pivotX;

            // Starting on the pivot leaves no reference length to divide by. Rather than divide by
            // something near zero and jump to an enormous factor, fall back to a fixed pixel rate.
            const float MinimumReferencePixels = 8f;
            const float PixelsPerUnitFactor = 200f;
            if (Mathf.Abs(startOffset) < MinimumReferencePixels)
            {
                return 1f + (transformPointerCurrentX - transformPointerStartX) / PixelsPerUnitFactor;
            }
            return (transformPointerCurrentX - pivotX) / startOffset;
        }

        private float ResolvePivotTime()
        {
            if (transformPivotMode == KeyTransformPivot.Playhead)
            {
                return playheadTime;
            }

            float earliest = float.MaxValue;
            float latest = float.MinValue;
            foreach (KeyAddress address in selectedKeys)
            {
                float keyTime;
                if (!TryGetSelectedKeyTime(address, out keyTime))
                {
                    continue;
                }
                earliest = Mathf.Min(earliest, keyTime);
                latest = Mathf.Max(latest, keyTime);
            }
            if (earliest > latest)
            {
                return playheadTime;
            }
            return transformPivotMode == KeyTransformPivot.SelectionStart
                ? earliest
                : (earliest + latest) * 0.5f;
        }

        /// <summary>Folds a sort's index map into the snapshot's original-to-current mapping.</summary>
        private void ComposeSnapshotIndices(KeyTransformTrackSnapshot snapshot)
        {
            int[] sortMap = lastSortIndexMap;
            if (sortMap == null)
            {
                return;
            }
            for (int originalIndex = 0;
                originalIndex < snapshot.currentIndexOfOriginal.Length;
                originalIndex++)
            {
                int currentIndex = snapshot.currentIndexOfOriginal[originalIndex];
                if (currentIndex >= 0 && currentIndex < sortMap.Length)
                {
                    snapshot.currentIndexOfOriginal[originalIndex] = sortMap[currentIndex];
                }
            }
        }

        // -----------------------------------------------------------------------------------
        // Input while modal.
        // -----------------------------------------------------------------------------------

        /// <summary>Handles a key press for the gestures. Returns false when nothing was consumed.</summary>
        private bool HandleTransformKeyDown(KeyDownEvent keyEvent)
        {
            if (!IsTransformActive)
            {
                // Ctrl+S is save and Ctrl+Z is undo. Neither should start a gesture, and a reflex
                // Ctrl+S silently beginning a scale is a particularly bad way to learn that.
                if (keyEvent.ctrlKey || keyEvent.commandKey)
                {
                    return false;
                }

                // Not modal yet: G and S start a gesture, and nothing else here applies.
                if (keyEvent.keyCode == KeyCode.G)
                {
                    BeginKeyTransform(KeyTransformKind.Grab);
                    return true;
                }
                if (keyEvent.keyCode == KeyCode.S)
                {
                    BeginKeyTransform(KeyTransformKind.Scale);
                    return true;
                }
                return false;
            }

            // Ctrl+Z during a gesture means "get me out of this", which is what cancelling does —
            // and it is the right answer rather than a real undo, because the gesture has not been
            // committed yet. Undoing the step before it while it is still running would leave the
            // snapshot describing times that no longer exist.
            if ((keyEvent.ctrlKey || keyEvent.commandKey) && keyEvent.keyCode == KeyCode.Z)
            {
                CancelKeyTransform();
                return true;
            }

            switch (keyEvent.keyCode)
            {
                case KeyCode.Escape:
                    CancelKeyTransform();
                    return true;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    ConfirmKeyTransform();
                    return true;
                case KeyCode.G:
                    BeginKeyTransform(KeyTransformKind.Grab);
                    return true;
                case KeyCode.S:
                    BeginKeyTransform(KeyTransformKind.Scale);
                    return true;
                case KeyCode.Backspace:
                    if (transformTypedValue.Length > 0)
                    {
                        transformTypedValue =
                            transformTypedValue.Substring(0, transformTypedValue.Length - 1);
                        ApplyKeyTransform();
                    }
                    return true;
                default:
                    break;
            }

            // Typing a number takes over from the mouse: an exact retime is the whole reason to
            // offer numeric entry, so a stray pointer move must not overwrite it.
            char typed = keyEvent.character;
            if (char.IsDigit(typed) || typed == '.' || typed == '-')
            {
                transformTypedValue += typed;
                ApplyKeyTransform();
                return true;
            }

            // Anything else is swallowed rather than passed on. A gesture is modal: letting Space
            // start playback underneath a running retime is how you end up not knowing what state
            // you are in.
            return true;
        }

        /// <summary>Re-applies when the snap modifier changes, so the keys follow the modifier live.</summary>
        private void UpdateTransformModifiers(bool snapSuppressed)
        {
            if (!IsTransformActive || transformSnapSuppressed == snapSuppressed)
            {
                return;
            }
            transformSnapSuppressed = snapSuppressed;
        }

        private void OnTransformPointerMove(PointerMoveEvent moveEvent)
        {
            if (!IsTransformActive)
            {
                return;
            }
            transformPointerCurrentX = LaneXFromWorld(moveEvent.position.x);
            UpdateTransformModifiers(moveEvent.ctrlKey || moveEvent.commandKey);
            ApplyKeyTransform();
            moveEvent.StopPropagation();
        }

        private void OnTransformPointerDown(PointerDownEvent pointerEvent)
        {
            if (!IsTransformActive)
            {
                return;
            }
            // Left click confirms, right click cancels — the two answers the gesture has, on the
            // two buttons every modal tool puts them on.
            if (pointerEvent.button == 1)
            {
                CancelKeyTransform();
            }
            else
            {
                ConfirmKeyTransform();
            }
            pointerEvent.StopPropagation();
            pointerEvent.StopImmediatePropagation();
        }

        private bool TryParseTypedValue(out float value)
        {
            value = 0f;
            if (string.IsNullOrEmpty(transformTypedValue))
            {
                return false;
            }
            // "-" and "1." are what a half-typed number looks like. Neither parses, and neither
            // should reset the gesture, so an unparseable buffer simply falls back to the mouse.
            return float.TryParse(
                transformTypedValue,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out value);
        }

        private float LaneXFromWorld(float worldX)
        {
            return laneColumn != null ? worldX - laneColumn.worldBound.xMin : worldX;
        }

        /// <summary>
        /// Where the gesture starts measuring from.
        /// </summary>
        /// <remarks>
        /// The gesture starts from the keyboard, so there is no pointer event to read. Anchoring on
        /// the playhead makes the first pixel of mouse movement a small change rather than a jump
        /// from wherever the pointer happened to be resting.
        /// </remarks>
        private float PlayheadLaneX()
        {
            return TimelineGeometry.Create(LaneWidth, viewZoom, viewPan).TimeToX(playheadTime);
        }

        // -----------------------------------------------------------------------------------
        // Readout.
        // -----------------------------------------------------------------------------------

        private void ShowTransformReadout(float grabDelta, float scaleFactor, bool snapping)
        {
            if (transformReadout == null)
            {
                return;
            }
            transformReadout.style.display = DisplayStyle.Flex;

            string body;
            if (activeTransform == KeyTransformKind.Grab)
            {
                float frames = grabDelta * Mathf.Max(1, TransportFrameCount);
                body = "Move " + (frames >= 0f ? "+" : string.Empty)
                    + frames.ToString("0.##") + " frames";
            }
            else
            {
                body = "Scale x" + scaleFactor.ToString("0.###")
                    + (scaleFactor < 0f ? " (mirrored)" : string.Empty)
                    + " about " + PivotDescription();
            }

            if (transformTypedValue.Length > 0)
            {
                body += "   typed: " + transformTypedValue;
            }
            body += snapping ? "   [snap on, Ctrl to disable]" : "   [snap off]";
            transformReadout.text = body + "   Enter/click confirms, Esc cancels";
        }

        private string PivotDescription()
        {
            switch (transformPivotMode)
            {
                case KeyTransformPivot.SelectionCenter:
                    return "selection center";
                case KeyTransformPivot.SelectionStart:
                    return "selection start";
                default:
                    return "playhead";
            }
        }

        private void HideTransformReadout()
        {
            if (transformReadout != null)
            {
                transformReadout.style.display = DisplayStyle.None;
            }
        }
    }
}
