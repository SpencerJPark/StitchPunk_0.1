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
        /// <summary>Default for the shift-modified step, in frames.</summary>
        private const int DefaultLargeStepFrames = 10;

        /// <summary>
        /// A key closer than this to a frame boundary counts as on-grid. In normalized units it is
        /// well below one frame at any sane rate, and it exists because a key authored at exactly a
        /// frame still arrives back as a float that has been through a division.
        /// </summary>
        private const float FrameSnapEpsilon = 1e-4f;

        private TransportCoreElement transportCore;
        private IntegerField currentFrameField;
        private FloatField currentSecondsField;
        private FloatField clipLengthField;
        private IntegerField frameRateField;
        private Label frameCountLabel;
        private FloatField playbackSpeedField;
        private Button quantizeKeysButton;
        private Button addEventButton;

        /// <summary>Guards the readout fields against re-notifying while being written.</summary>
        private bool isSyncingTransport;

        /// <summary>How many frames a shift-modified step moves. Persisted per user, not per clip.</summary>
        private int LargeStepFrames
        {
            get { return EditorPrefs.GetInt("DotsAnimationToolkit.ClipEditor.LargeStep", DefaultLargeStepFrames); }
        }

        /// <summary>Where the preview loop mode is remembered between sessions.</summary>
        private const string LoopPrefKey = "DotsAnimationToolkit.ClipEditor.Loop";

        /// <summary>
        /// Whether preview playback wraps at the end. Held here rather than read off the control,
        /// because the transport core exposes it through <see cref="ITransportTarget"/> rather than
        /// owning a value of its own.
        /// </summary>
        private bool isLoopEnabled = true;

        // -----------------------------------------------------------------------------------
        // Frame <-> normalized. Every conversion in the window goes through these two, so the
        // grid can never mean one thing to the ruler and another to the transport.
        // -----------------------------------------------------------------------------------

        /// <summary>The selected clip's frame count, or a usable default when nothing is selected.</summary>
        private int TransportFrameCount
        {
            get { return selectedClip != null ? selectedClip.FrameCount : 30; }
        }

        private float TransportDuration
        {
            get
            {
                return selectedClip != null
                    ? Mathf.Max(ClipAsset.MinimumDuration, selectedClip.duration)
                    : 1f;
            }
        }

        // Frames are numbered 0..FrameCount, so the last frame is the clip's end, not one short of it.
        private int NormalizedToFrame(float normalizedTime)
        {
            return Mathf.Clamp(
                Mathf.RoundToInt(normalizedTime * TransportFrameCount), 0, TransportFrameCount);
        }

        private float FrameToNormalized(int frame)
        {
            int frameCount = Mathf.Max(1, TransportFrameCount);
            return Mathf.Clamp01((float)Mathf.Clamp(frame, 0, frameCount) / frameCount);
        }

        // -----------------------------------------------------------------------------------
        // Binding.
        // -----------------------------------------------------------------------------------

        private void BindTransportBar()
        {
            VisualElement transportCoreSlot = rootVisualElement.Q<VisualElement>("transport-core-slot");
            if (transportCoreSlot != null)
            {
                transportCore = new TransportCoreElement { largeStepFrames = Mathf.Max(1, LargeStepFrames) };
                // Cleared first: CreateGUI re-runs after a domain reload and the slot must not stack cores.
                transportCoreSlot.Clear();
                transportCoreSlot.Add(transportCore);
                transportCore.Bind(this);
            }

            currentFrameField = rootVisualElement.Q<IntegerField>("current-frame-field");
            if (currentFrameField != null)
            {
                currentFrameField.tooltip =
                    "The playhead, as a frame number. Editable, and the Frame caption scrubs it a "
                    + "frame at a time.";
                currentFrameField.RegisterValueChangedCallback(changeEvent =>
                {
                    if (isSyncingTransport)
                    {
                        return;
                    }
                    SetPlayheadTime(FrameToNormalized(changeEvent.newValue));
                });
                CaptionDragHandle.Attach(
                    rootVisualElement.Q<Label>("frame-caption"), currentFrameField);
            }

            currentSecondsField = rootVisualElement.Q<FloatField>("current-seconds-field");
            if (currentSecondsField != null)
            {
                currentSecondsField.tooltip =
                    "The playhead, in seconds. Editable, and the Time caption scrubs it.";
                currentSecondsField.RegisterValueChangedCallback(changeEvent =>
                {
                    if (isSyncingTransport)
                    {
                        return;
                    }
                    SetPlayheadTime(changeEvent.newValue / TransportDuration);
                });
                CaptionDragHandle.Attach(
                    rootVisualElement.Q<Label>("seconds-caption"), currentSecondsField);
            }

            clipLengthField = rootVisualElement.Q<FloatField>("clip-length-field");
            if (clipLengthField != null)
            {
                // Commits on blur/Enter, not per keystroke: "0.5" arrives as "0" first, and the
                // minimum-duration clamp would collapse the clip mid-keystroke otherwise.
                clipLengthField.isDelayed = true;
                clipLengthField.tooltip =
                    "Clip length in seconds. With the frame rate this defines the frame count. "
                    + "Drag the Length caption to scrub it.";
                clipLengthField.RegisterValueChangedCallback(changeEvent =>
                {
                    if (isSyncingTransport || selectedClip == null)
                    {
                        return;
                    }
                    // Through the clip's own gesture undo rather than a bare RecordObject, so a
                    // drag collapses into one step instead of one per mouse move.
                    RecordClipEdit("Set Clip Length");
                    selectedClip.duration = Mathf.Max(ClipAsset.MinimumDuration, changeEvent.newValue);
                    CommitClipEdit();
                    OnClipTimingChanged();
                });
                CaptionDragHandle.Attach(
                    rootVisualElement.Q<Label>("length-caption"), clipLengthField);
            }

            // Integer, not float: the frame count it defines rounds to a whole number anyway, and a
            // clip carrying a fractional rate reads back rounded.
            frameRateField = rootVisualElement.Q<IntegerField>("frame-rate-field");
            if (frameRateField != null)
            {
                frameRateField.isDelayed = true;
                frameRateField.tooltip =
                    "Frames per second: how many poses the clip holds per second, and the rate a "
                    + "VAT bake samples it at — a clip is duration x FPS rows of texture, played "
                    + "back at this rate. Length sets how long the clip lasts; this sets how "
                    + "finely it is cut. Changing it never moves a key, but it does mean re-baking "
                    + "the VAT set. Drag the FPS caption to scrub it.";
                frameRateField.RegisterValueChangedCallback(changeEvent =>
                {
                    if (isSyncingTransport || selectedClip == null)
                    {
                        return;
                    }
                    RecordClipEdit("Set Frame Rate");
                    selectedClip.frameRate = Mathf.Max(1, changeEvent.newValue);
                    CommitClipEdit();
                    OnClipTimingChanged();
                });
                CaptionDragHandle.Attach(
                    rootVisualElement.Q<Label>("frame-rate-caption"), frameRateField);
            }

            frameCountLabel = rootVisualElement.Q<Label>("frame-count-label");
            if (frameCountLabel != null)
            {
                frameCountLabel.tooltip = "Length times frame rate. Derived, so it cannot disagree "
                    + "with the two fields beside it.";
            }

            isLoopEnabled = EditorPrefs.GetBool(LoopPrefKey, true);
            RefreshTransportCoreState();

            playbackSpeedField = rootVisualElement.Q<FloatField>("playback-speed-field");
            if (playbackSpeedField != null)
            {
                playbackSpeedField.value = 1f;
                playbackSpeedField.tooltip =
                    "Preview playback multiplier. Negative plays backwards. Drag the Speed caption "
                    + "to scrub it.";

                // No value-changed callback: the tick reads this field directly, so there is nothing
                // for a drag to notify. It still needs the handle, being a number like the rest.
                CaptionDragHandle.Attach(
                    rootVisualElement.Q<Label>("speed-caption"), playbackSpeedField);
            }

            quantizeKeysButton = rootVisualElement.Q<Button>("quantize-keys-button");
            if (quantizeKeysButton != null)
            {
                quantizeKeysButton.clicked += QuantizeKeysToFrameRate;
                quantizeKeysButton.tooltip =
                    "Move every key onto the nearest frame of the current grid. Offered rather than "
                    + "applied automatically: a key between frames is authored data, and a display "
                    + "setting must not rewrite it without being asked.";
            }

            // Lives in the status row over the key area, not the transport bar — it edits keys
            // rather than answering "when" — but is bound here since this method resolves every
            // control by name regardless of which row it sits in.
            addEventButton = rootVisualElement.Q<Button>("add-event-button");
            if (addEventButton != null)
            {
                // Kept as a field rather than captured locally: the event picker needs it as its anchor.
                addEventButton.clicked += OpenAddEventPicker;
                ToolkitIcons.SetButtonIcon(addEventButton, ToolkitIcons.AddEvent, "Add Event");
                // The word stays: the picker it opens is the affordance, the icon only says which family.
                addEventButton.text = "Add Event";
                addEventButton.AddToClassList("toolkit-icon-button--with-text");
            }

            RegisterTransportShortcuts();
            SyncTransportFromClip();
        }

        // -----------------------------------------------------------------------------------
        // Stepping and playback state.
        // -----------------------------------------------------------------------------------

        private void StepFrames(int frameDelta)
        {
            SetPlayheadTime(FrameToNormalized(NormalizedToFrame(playheadTime) + frameDelta));
        }

        // Guarded by isSyncingTransport: writing a field notifies its callback, and a callback that
        // moved the playhead would fight the value being written into it.
        private void SyncTransportFromClip()
        {
            isSyncingTransport = true;
            try
            {
                bool hasClip = selectedClip != null;

                // Enabled state is always pushed; the value is not, into a field the user is typing in.
                if (clipLengthField != null)
                {
                    clipLengthField.SetEnabled(hasClip);
                    if (!IsBeingEdited(clipLengthField))
                    {
                        clipLengthField.SetValueWithoutNotify(hasClip ? selectedClip.duration : 0f);
                    }
                }
                if (frameRateField != null)
                {
                    frameRateField.SetEnabled(hasClip);
                    if (!IsBeingEdited(frameRateField))
                    {
                        frameRateField.SetValueWithoutNotify(
                            hasClip ? Mathf.Max(1, Mathf.RoundToInt(selectedClip.frameRate)) : 30);
                    }
                }
                if (frameCountLabel != null)
                {
                    frameCountLabel.text = TransportFrameCount.ToString() + " frames";
                }
                if (addEventButton != null)
                {
                    addEventButton.SetEnabled(hasClip);
                    addEventButton.tooltip = hasClip
                        ? "Choose an event and place a marker at the playhead."
                        : "Select a clip to add an event marker.";
                }

                SyncTransportPlayhead();
                RefreshQuantizeButton();

                // The zoom-in limit is a frame count, and the frame count is what just changed.
                RefreshZoomRange();
            }
            finally
            {
                isSyncingTransport = false;
            }
        }

        /// <summary>Updates only the two playhead readouts. Called every time the playhead moves.</summary>
        private void SyncTransportPlayhead()
        {
            bool wasSyncing = isSyncingTransport;
            isSyncingTransport = true;
            try
            {
                // Not written into a field being typed in — an unconditional write would make the
                // caret jump on every keystroke. A caption drag is not excluded: the write-back is
                // what clamps the readout at the clip's ends.
                if (currentFrameField != null && !IsBeingEdited(currentFrameField))
                {
                    currentFrameField.SetValueWithoutNotify(NormalizedToFrame(playheadTime));
                }
                if (currentSecondsField != null && !IsBeingEdited(currentSecondsField))
                {
                    currentSecondsField.SetValueWithoutNotify(playheadTime * TransportDuration);
                }
            }
            finally
            {
                isSyncingTransport = wasSyncing;
            }
        }

        /// <summary>Turns preview looping on or off and remembers the choice.</summary>
        private void SetLooping(bool looping)
        {
            isLoopEnabled = looping;
            EditorPrefs.SetBool(LoopPrefKey, looping);
            RefreshTransportCoreState();
        }

        private void RefreshTransportCoreState()
        {
            transportCore?.RefreshState();
        }

        // Re-rules the timeline after a length or rate edit. The playhead holds its normalized
        // position rather than its frame number, since the frame it indexed into just changed length.
        private void OnClipTimingChanged()
        {
            if (ruler != null)
            {
                ruler.frameCount = TransportFrameCount;
                ruler.MarkDirtyRepaint();
            }
            SyncTransportFromClip();
            MarkPreviewDirty();

            // Requested: Length and FPS are dragged now, and a full timeline rebuild on every mouse
            // move of that drag is wasted work at 30 a second.
            RequestTimelineRebuild();
        }

        // -----------------------------------------------------------------------------------
        // Off-grid keys.
        // -----------------------------------------------------------------------------------

        // Shows the quantize action only when some key does not land on the current grid — a
        // permanently visible button that usually does nothing teaches people to ignore it.
        private void RefreshQuantizeButton()
        {
            if (quantizeKeysButton == null)
            {
                return;
            }
            int offGridCount = CountOffGridKeys();
            quantizeKeysButton.style.display =
                offGridCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            quantizeKeysButton.text = "Quantize " + offGridCount + " Key"
                + (offGridCount == 1 ? "" : "s");
        }

        private bool IsOnFrameGrid(float normalizedTime)
        {
            float frames = normalizedTime * TransportFrameCount;
            return Mathf.Abs(frames - Mathf.Round(frames)) <= FrameSnapEpsilon * TransportFrameCount;
        }

        private int CountOffGridKeys()
        {
            if (selectedClip == null)
            {
                return 0;
            }
            int count = 0;
            ForEachKeyTime(selectedClip, normalizedTime =>
            {
                if (!IsOnFrameGrid(normalizedTime))
                {
                    count++;
                }
                return normalizedTime;
            });
            return count;
        }

        private void QuantizeKeysToFrameRate()
        {
            if (selectedClip == null)
            {
                return;
            }
            Undo.RecordObject(selectedClip, "Quantize Keys To Frame Rate");
            ForEachKeyTime(selectedClip, normalizedTime =>
                FrameToNormalized(NormalizedToFrame(normalizedTime)));
            EditorUtility.SetDirty(selectedClip);
            MarkPreviewDirty();
            RebuildTimeline();
            RefreshQuantizeButton();
        }

        /// <summary>Visits every authored key time in a clip, replacing it with whatever the visitor returns.</summary>
        // Every track kind is listed explicitly: a new kind that forgets to appear here is one the
        // quantize action silently skips.
        private static void ForEachKeyTime(ClipAsset clip, System.Func<float, float> visitor)
        {
            if (clip.transformTracks != null)
            {
                for (int t = 0; t < clip.transformTracks.Count; t++)
                {
                    TransformTrack track = clip.transformTracks[t];
                    if (track == null || track.keys == null) { continue; }
                    for (int k = 0; k < track.keys.Count; k++)
                    {
                        TransformKey key = track.keys[k];
                        key.normalizedTime = visitor(key.normalizedTime);
                        track.keys[k] = key;
                    }
                }
            }
            if (clip.spriteTracks != null)
            {
                for (int t = 0; t < clip.spriteTracks.Count; t++)
                {
                    SpriteTrack track = clip.spriteTracks[t];
                    if (track == null || track.keys == null) { continue; }
                    for (int k = 0; k < track.keys.Count; k++)
                    {
                        SpriteKey key = track.keys[k];
                        key.normalizedTime = visitor(key.normalizedTime);
                        track.keys[k] = key;
                    }
                }
            }
            if (clip.billboardTracks != null)
            {
                for (int t = 0; t < clip.billboardTracks.Count; t++)
                {
                    BillboardTrack track = clip.billboardTracks[t];
                    if (track == null || track.keys == null) { continue; }
                    for (int k = 0; k < track.keys.Count; k++)
                    {
                        BillboardKey key = track.keys[k];
                        key.normalizedTime = visitor(key.normalizedTime);
                        track.keys[k] = key;
                    }
                }
            }
            if (clip.boneTracks != null)
            {
                for (int t = 0; t < clip.boneTracks.Count; t++)
                {
                    BoneTrack track = clip.boneTracks[t];
                    if (track == null || track.keys == null) { continue; }
                    for (int k = 0; k < track.keys.Count; k++)
                    {
                        BoneKey key = track.keys[k];
                        key.normalizedTime = visitor(key.normalizedTime);
                        track.keys[k] = key;
                    }
                }
            }
            if (clip.events != null)
            {
                for (int e = 0; e < clip.events.Count; e++)
                {
                    EventMarker marker = clip.events[e];
                    marker.normalizedTime = visitor(marker.normalizedTime);
                    clip.events[e] = marker;
                }
            }
        }

        // -----------------------------------------------------------------------------------
        // ITransportTarget.
        // -----------------------------------------------------------------------------------

        // Explicit so the window's own surface stays free of transport verbs; the core and the key
        // router are the only callers.
        bool ITransportTarget.IsPlaying { get { return isPlaying; } }
        bool ITransportTarget.IsLooping { get { return isLoopEnabled; } set { SetLooping(value); } }
        TransportCapabilities ITransportTarget.Capabilities
        {
            get
            {
                return TransportCapabilities.StepBack | TransportCapabilities.StepForward
                    | TransportCapabilities.Stop | TransportCapabilities.JumpToEnd | TransportCapabilities.Loop;
            }
        }
        void ITransportTarget.TogglePlay() { SetPlaying(!isPlaying); }
        void ITransportTarget.Stop() { SetPlaying(false); SetPlayheadTime(prePlayPlayheadTime); }
        void ITransportTarget.JumpToStart() { SetPlayheadTime(0f); }
        void ITransportTarget.JumpToEnd() { SetPlayheadTime(1f); }
        void ITransportTarget.Step(int frameDelta) { StepFrames(frameDelta); }

        // -----------------------------------------------------------------------------------
        // Keyboard.
        // -----------------------------------------------------------------------------------

        // Registered on rootVisualElement, not Unity's global shortcut system, so Space and the
        // arrows keep their meaning elsewhere; bubble phase so a focused field sees the key first.
        private void RegisterTransportShortcuts()
        {
            rootVisualElement.focusable = true;

            // Undo and redo are keyboard-only. They had buttons in the bar, but a transport is
            // about time and they are not: every editor in Unity undoes with Ctrl+Z, and two
            // buttons restating that were paying for themselves in bar width.
            rootVisualElement.RegisterCallback<KeyDownEvent>(OnTransportKeyDown);
        }

        private void OnTransportKeyDown(KeyDownEvent keyEvent)
        {
            bool commandKey = keyEvent.ctrlKey || keyEvent.commandKey;

            // Answered before the text-entry guard below: a focused UI Toolkit window keeps the
            // keystroke, so a Ctrl+Z declined here reaches nothing at all, not even the Editor.
            bool isRedoKey = commandKey
                && (keyEvent.keyCode == KeyCode.Y
                    || (keyEvent.keyCode == KeyCode.Z && keyEvent.shiftKey));
            if (isRedoKey || (commandKey && keyEvent.keyCode == KeyCode.Z))
            {
                // The gesture gets first refusal: mid-grab Ctrl+Z means "get me out of this", not a real undo.
                // Only the Clip Editor tab runs a transform gesture; undo/redo itself still runs on every tab.
                if (!(activeTab == ClipEditorTab.ClipEditor && HandleTransformKeyDown(keyEvent)))
                {
                    if (isRedoKey)
                    {
                        PerformRedo();
                    }
                    else
                    {
                        PerformUndo();
                    }
                }
                keyEvent.StopPropagation();
                return;
            }

            if (IsTextEntryFocused())
            {
                return;
            }

            // First refusal: while a grab or scale is running, every key belongs to it. Only the
            // Clip Editor tab runs a transform gesture at all.
            if (activeTab == ClipEditorTab.ClipEditor && HandleTransformKeyDown(keyEvent))
            {
                keyEvent.StopPropagation();
                return;
            }

            ITransportTarget activeTransportTarget = ResolveActiveTransportTarget();
            bool isClipEditorTab = activeTab == ClipEditorTab.ClipEditor;
            int largeStep = Mathf.Max(1, LargeStepFrames);
            bool handled = true;
            switch (keyEvent.keyCode)
            {
                case KeyCode.Space:
                    if (activeTransportTarget == null)
                    {
                        handled = false;
                    }
                    else
                    {
                        activeTransportTarget.TogglePlay();
                    }
                    break;
                case KeyCode.LeftArrow:
                    if (activeTransportTarget == null)
                    {
                        handled = false;
                    }
                    else
                    {
                        activeTransportTarget.Step(keyEvent.shiftKey ? -largeStep : -1);
                    }
                    break;
                case KeyCode.RightArrow:
                    if (activeTransportTarget == null)
                    {
                        handled = false;
                    }
                    else
                    {
                        activeTransportTarget.Step(keyEvent.shiftKey ? largeStep : 1);
                    }
                    break;
                case KeyCode.Home:
                    if (activeTransportTarget == null)
                    {
                        handled = false;
                    }
                    else
                    {
                        activeTransportTarget.JumpToStart();
                    }
                    break;
                case KeyCode.End:
                    if (activeTransportTarget == null)
                    {
                        handled = false;
                    }
                    else
                    {
                        activeTransportTarget.JumpToEnd();
                    }
                    break;
                // Framing does NOT take Home. The transport spec already gave Home to
                // jump-to-start, and a key that means two things depending on which pane you
                // imagine yourself in is worse than a second key. F is what Unity users already
                // press to frame a selection; numpad period is what Blender users press.
                case KeyCode.F:
                    if (!isClipEditorTab)
                    {
                        handled = false;
                        break;
                    }
                    // Shift widens the frame from the selection to the whole clip. A took select-all
                    // instead, which is the meaning every animator already has for it.
                    if (keyEvent.shiftKey)
                    {
                        FrameAll();
                    }
                    else
                    {
                        FrameSelection();
                    }
                    break;
                case KeyCode.KeypadPeriod:
                    if (!isClipEditorTab)
                    {
                        handled = false;
                        break;
                    }
                    FrameSelection();
                    break;
                case KeyCode.A:
                    if (!isClipEditorTab)
                    {
                        handled = false;
                        break;
                    }
                    if (keyEvent.altKey)
                    {
                        DeselectAllKeys();
                    }
                    else
                    {
                        SelectAllKeys();
                    }
                    break;

                // Also handled window-wide: pasting onto a different object means selecting it
                // first, which puts focus in the hierarchy where the lane stack's handler never runs.
                case KeyCode.C:
                    if (!isClipEditorTab)
                    {
                        handled = false;
                        break;
                    }
                    if (commandKey && selectedClip != null)
                    {
                        CopySelectedKeys();
                    }
                    else
                    {
                        handled = false;
                    }
                    break;
                case KeyCode.V:
                    if (!isClipEditorTab)
                    {
                        handled = false;
                        break;
                    }
                    if (commandKey && selectedClip != null)
                    {
                        PasteKeysAtPlayhead();
                    }
                    else
                    {
                        handled = false;
                    }
                    break;
                case KeyCode.D:
                    if (!isClipEditorTab)
                    {
                        handled = false;
                        break;
                    }
                    if (commandKey && selectedClip != null)
                    {
                        CopySelectedKeys();
                        PasteKeysAtPlayhead();
                    }
                    else
                    {
                        handled = false;
                    }
                    break;
                default:
                    handled = false;
                    break;
            }

            if (handled)
            {
                // Both, and for different reasons: StopPropagation keeps the key inside this
                // window, PreventDefault stops the Editor's own Space/arrow handling running too.
                keyEvent.StopPropagation();
            }
        }

        // No refresh here: Undo.PerformUndo raises undoRedoPerformed, which the window already subscribes to.
        private void PerformUndo()
        {
            Undo.PerformUndo();
        }

        private void PerformRedo()
        {
            Undo.PerformRedo();
        }

        // Checked by capability (TextField / ITextEdition) rather than by type name, so this also
        // catches Unity's numeric fields, which are TextFields underneath.
        private bool IsTextEntryFocused()
        {
            VisualElement focused = rootVisualElement.focusController != null
                ? rootVisualElement.focusController.focusedElement as VisualElement
                : null;
            while (focused != null)
            {
                if (focused is TextField || focused is ITextEdition)
                {
                    return true;
                }
                focused = focused.parent;
            }
            return false;
        }
    }
}
