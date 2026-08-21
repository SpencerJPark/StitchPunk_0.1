// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// The transport bar: everything in the Clip Editor that answers <em>when</em>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>One place for time.</strong> Play, the playhead readout, the two fields that define
    /// the frame grid, loop and speed used to be scattered across the top toolbar next to controls
    /// about what an edit <em>writes</em> (Auto Key, Rig Edit, Snap). Two unrelated concerns sharing
    /// a strip made both harder to find. The bar is docked immediately above the timeline because
    /// that is the thing every control in it acts on.
    /// </para>
    /// <para>
    /// <strong>A separate file, not a bigger window class.</strong> <c>ClipEditorWindow</c> is
    /// already the largest file in the package; adding a transport to it would make the next reader's
    /// job worse for no benefit. This is a partial of the same class, so it shares the window's
    /// private state without exposing any of it.
    /// </para>
    /// </remarks>
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

        private Button playButton;
        private Button jumpStartButton;
        private Button stepBackButton;
        private Button stepForwardButton;
        private Button jumpEndButton;
        private IntegerField currentFrameField;
        private FloatField currentSecondsField;
        private FloatField clipLengthField;
        private FloatField frameRateField;
        private Label frameCountLabel;
        private Button loopButton;
        private FloatField playbackSpeedField;
        private Button quantizeKeysButton;

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
        /// because the control is now a plain button and has no value of its own to read.
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

        /// <summary>
        /// The frame a normalized time lands on.
        /// </summary>
        /// <remarks>
        /// Frames are numbered 0..FrameCount, so the last frame is the clip's end rather than one
        /// short of it. An author scrubbing to the end expects to see the final pose, not the one
        /// before it.
        /// </remarks>
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
            playButton = rootVisualElement.Q<Button>("play-toggle");
            if (playButton != null)
            {
                playButton.clicked += () => SetPlaying(!isPlaying);
                playButton.tooltip = "Play or pause. Shortcut: Space.";
            }

            jumpStartButton = rootVisualElement.Q<Button>("jump-start-button");
            if (jumpStartButton != null)
            {
                jumpStartButton.clicked += () => SetPlayheadTime(0f);
                jumpStartButton.tooltip = "Jump to the first frame. Shortcut: Home.";
            }

            jumpEndButton = rootVisualElement.Q<Button>("jump-end-button");
            if (jumpEndButton != null)
            {
                jumpEndButton.clicked += () => SetPlayheadTime(1f);
                jumpEndButton.tooltip = "Jump to the last frame. Shortcut: End.";
            }

            stepBackButton = rootVisualElement.Q<Button>("step-back-button");
            if (stepBackButton != null)
            {
                stepBackButton.clicked += () => StepFrames(-1);
                stepBackButton.tooltip = "Step back one frame. Shortcut: Left arrow (Shift for "
                    + LargeStepFrames + ").";
            }

            stepForwardButton = rootVisualElement.Q<Button>("step-forward-button");
            if (stepForwardButton != null)
            {
                stepForwardButton.clicked += () => StepFrames(1);
                stepForwardButton.tooltip = "Step forward one frame. Shortcut: Right arrow (Shift "
                    + "for " + LargeStepFrames + ").";
            }

            currentFrameField = rootVisualElement.Q<IntegerField>("current-frame-field");
            if (currentFrameField != null)
            {
                currentFrameField.tooltip = "The playhead, as a frame number. Editable.";
                currentFrameField.RegisterValueChangedCallback(changeEvent =>
                {
                    if (isSyncingTransport)
                    {
                        return;
                    }
                    SetPlayheadTime(FrameToNormalized(changeEvent.newValue));
                });
            }

            currentSecondsField = rootVisualElement.Q<FloatField>("current-seconds-field");
            if (currentSecondsField != null)
            {
                currentSecondsField.tooltip = "The playhead, in seconds. Editable.";
                currentSecondsField.RegisterValueChangedCallback(changeEvent =>
                {
                    if (isSyncingTransport)
                    {
                        return;
                    }
                    SetPlayheadTime(changeEvent.newValue / TransportDuration);
                });
            }

            clipLengthField = rootVisualElement.Q<FloatField>("clip-length-field");
            if (clipLengthField != null)
            {
                // Commit on blur or Enter, not per keystroke: "0.5" arrives as "0" first, which the
                // minimum-duration clamp turns into a value the rest of the number is typed on top of.
                clipLengthField.isDelayed = true;
                clipLengthField.tooltip =
                    "Clip length in seconds. With the frame rate this defines the frame count.";
                clipLengthField.RegisterValueChangedCallback(changeEvent =>
                {
                    if (isSyncingTransport || selectedClip == null)
                    {
                        return;
                    }
                    Undo.RecordObject(selectedClip, "Set Clip Length");
                    selectedClip.duration = Mathf.Max(ClipAsset.MinimumDuration, changeEvent.newValue);
                    EditorUtility.SetDirty(selectedClip);
                    OnClipTimingChanged();
                });
            }

            frameRateField = rootVisualElement.Q<FloatField>("frame-rate-field");
            if (frameRateField != null)
            {
                frameRateField.isDelayed = true;
                frameRateField.tooltip =
                    "Frames per second the timeline divides the clip into. A grid for authoring and "
                    + "snapping — it does not change how often the runtime samples the pose, which "
                    + "is the actor's own sample rate. Changing it never moves a key.";
                frameRateField.RegisterValueChangedCallback(changeEvent =>
                {
                    if (isSyncingTransport || selectedClip == null)
                    {
                        return;
                    }
                    Undo.RecordObject(selectedClip, "Set Frame Rate");
                    selectedClip.frameRate = Mathf.Max(1f, changeEvent.newValue);
                    EditorUtility.SetDirty(selectedClip);
                    OnClipTimingChanged();
                });
            }

            frameCountLabel = rootVisualElement.Q<Label>("frame-count-label");
            if (frameCountLabel != null)
            {
                frameCountLabel.tooltip = "Length times frame rate. Derived, so it cannot disagree "
                    + "with the two fields beside it.";
            }

            loopButton = rootVisualElement.Q<Button>("loop-button");
            if (loopButton != null)
            {
                // The built-in loop glyph, so the button reads at transport size without a word in
                // it. Carried on a child Image rather than the button's own background, because the
                // editor theme draws the button's chrome there. Falls back to text rather than
                // shipping a blank button if the icon ever leaves the editor's icon set.
                GUIContent loopIconContent = EditorGUIUtility.IconContent("preAudioLoopOff");
                Texture2D loopIcon = loopIconContent != null ? loopIconContent.image as Texture2D : null;
                Image loopIconImage = loopButton.Q<Image>("loop-icon");
                if (loopIcon != null && loopIconImage != null)
                {
                    loopIconImage.image = loopIcon;
                }
                else
                {
                    if (loopIconImage != null)
                    {
                        loopIconImage.RemoveFromHierarchy();
                    }
                    loopButton.text = "Loop";
                }

                loopButton.clicked += () => SetLooping(!isLoopEnabled);
                loopButton.tooltip = "Wrap at the end during preview playback. Preview only — the "
                    + "clip's own loop mode is authored on the asset.";
            }
            isLoopEnabled = EditorPrefs.GetBool(LoopPrefKey, true);
            RefreshLoopButtonState();

            playbackSpeedField = rootVisualElement.Q<FloatField>("playback-speed-field");
            if (playbackSpeedField != null)
            {
                playbackSpeedField.value = 1f;
                playbackSpeedField.tooltip =
                    "Preview playback multiplier. Negative plays backwards.";
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

        /// <summary>Pushes the window's current state into the bar's readouts.</summary>
        /// <remarks>
        /// Guarded by <see cref="isSyncingTransport"/>: writing a field notifies its callback, and a
        /// callback that moved the playhead would fight the value being written into it.
        /// </remarks>
        private void SyncTransportFromClip()
        {
            isSyncingTransport = true;
            try
            {
                bool hasClip = selectedClip != null;

                // Enabled state is always pushed; the value is not pushed into a field the user is
                // typing in. This sync runs from OnClipTimingChanged, which the field's own callback
                // raises, so writing back unconditionally means the field fights its own edit.
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
                        frameRateField.SetValueWithoutNotify(hasClip ? selectedClip.frameRate : 30f);
                    }
                }
                if (frameCountLabel != null)
                {
                    frameCountLabel.text = TransportFrameCount.ToString() + " frames";
                }

                SyncTransportPlayhead();
                RefreshQuantizeButton();
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
                if (currentFrameField != null)
                {
                    currentFrameField.SetValueWithoutNotify(NormalizedToFrame(playheadTime));
                }
                if (currentSecondsField != null)
                {
                    currentSecondsField.SetValueWithoutNotify(playheadTime * TransportDuration);
                }
            }
            finally
            {
                isSyncingTransport = wasSyncing;
            }
        }

        private void RefreshPlayButtonState()
        {
            if (playButton == null)
            {
                return;
            }
            playButton.text = isPlaying ? "Pause" : "Play";
            playButton.EnableInClassList("clip-editor__transport-play--playing", isPlaying);
        }

        /// <summary>Turns preview looping on or off and remembers the choice.</summary>
        private void SetLooping(bool looping)
        {
            isLoopEnabled = looping;
            EditorPrefs.SetBool(LoopPrefKey, looping);
            RefreshLoopButtonState();
        }

        /// <summary>
        /// A button has no checked state of its own, so the lit class is the only thing telling the
        /// author whether playback will wrap.
        /// </summary>
        private void RefreshLoopButtonState()
        {
            if (loopButton == null)
            {
                return;
            }
            loopButton.EnableInClassList("clip-editor__transport-loop--on", isLoopEnabled);
        }

        /// <summary>
        /// Re-rules the timeline after a length or rate edit.
        /// </summary>
        /// <remarks>
        /// The playhead is held at the same <em>normalized</em> position rather than the same frame
        /// number, because the clip it indexes into just changed length: staying at frame 12 of a
        /// clip that is now half as long would move the viewport to a different pose than the one
        /// being looked at.
        /// </remarks>
        private void OnClipTimingChanged()
        {
            if (ruler != null)
            {
                ruler.frameCount = TransportFrameCount;
                ruler.MarkDirtyRepaint();
            }
            SyncTransportFromClip();
            MarkPreviewDirty();
            RebuildTimeline();
        }

        // -----------------------------------------------------------------------------------
        // Off-grid keys.
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Shows the quantize action only when some key does not land on a frame of the current
        /// grid.
        /// </summary>
        /// <remarks>
        /// A permanently visible button that usually does nothing teaches people to ignore it. This
        /// one appearing IS the report that a rate change left keys between frames — which the spec
        /// asks to be surfaced rather than silently corrected.
        /// </remarks>
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

        /// <summary>
        /// Visits every authored key time in a clip, replacing it with whatever the visitor returns.
        /// </summary>
        /// <remarks>
        /// One traversal for read and write, so "which keys are off the grid" and "move the keys
        /// onto it" cannot disagree about what a key is. Every track kind is listed explicitly:
        /// a new kind that forgets to appear here is a kind the quantize action silently skips, and
        /// an explicit list is at least greppable.
        /// </remarks>
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
        // Keyboard.
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Registers the transport shortcuts on the window's own root.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>Scoped to this window, and inert while typing.</strong> Registered on
        /// <c>rootVisualElement</c> rather than through Unity's global shortcut system, so Space and
        /// the arrows keep their meaning everywhere else in the Editor. And a key pressed while a
        /// field has focus is left alone: Space must type a space and the arrows must move the caret
        /// when someone is editing the frame number, which is a field this very bar puts under their
        /// cursor.
        /// </para>
        /// <para>
        /// <c>TrickleDown</c> is deliberately NOT used — the event is handled on the bubble phase so
        /// a focused field sees it first and can consume it.
        /// </para>
        /// </remarks>
        private void RegisterTransportShortcuts()
        {
            rootVisualElement.focusable = true;
            Button undoButton = rootVisualElement.Q<Button>("undo-button");
            if (undoButton != null)
            {
                undoButton.tooltip = "Undo the last change (Ctrl+Z).";
                undoButton.clicked += PerformUndo;
            }

            Button redoButton = rootVisualElement.Q<Button>("redo-button");
            if (redoButton != null)
            {
                redoButton.tooltip = "Redo (Ctrl+Y or Ctrl+Shift+Z).";
                redoButton.clicked += PerformRedo;
            }

            rootVisualElement.RegisterCallback<KeyDownEvent>(OnTransportKeyDown);
        }

        private void OnTransportKeyDown(KeyDownEvent keyEvent)
        {
            if (IsTextEntryFocused())
            {
                return;
            }

            // First refusal, and it has to be first: while a grab or scale is running every key
            // belongs to it, including the ones the transport would otherwise claim. Space starting
            // playback in the middle of a retime is the exact confusion a modal gesture avoids.
            if (HandleTransformKeyDown(keyEvent))
            {
                keyEvent.StopPropagation();
                return;
            }

            int largeStep = Mathf.Max(1, LargeStepFrames);
            bool commandKey = keyEvent.ctrlKey || keyEvent.commandKey;
            bool handled = true;
            switch (keyEvent.keyCode)
            {
                // Undo has to be handled here rather than left to the Editor. Every edit already
                // records through Undo.RecordObject and the window already refreshes on
                // undoRedoPerformed — what was missing is that a focused UI Toolkit window keeps
                // the keystroke, so Ctrl+Z never reached Unity to begin with.
                case KeyCode.Z:
                    if (!commandKey)
                    {
                        handled = false;
                    }
                    else if (keyEvent.shiftKey)
                    {
                        PerformRedo();
                    }
                    else
                    {
                        PerformUndo();
                    }
                    break;
                case KeyCode.Y:
                    if (commandKey)
                    {
                        PerformRedo();
                    }
                    else
                    {
                        handled = false;
                    }
                    break;
                case KeyCode.Space:
                    SetPlaying(!isPlaying);
                    break;
                case KeyCode.LeftArrow:
                    StepFrames(keyEvent.shiftKey ? -largeStep : -1);
                    break;
                case KeyCode.RightArrow:
                    StepFrames(keyEvent.shiftKey ? largeStep : 1);
                    break;
                case KeyCode.Home:
                    SetPlayheadTime(0f);
                    break;
                case KeyCode.End:
                    SetPlayheadTime(1f);
                    break;
                // Framing does NOT take Home. The transport spec already gave Home to
                // jump-to-start, and a key that means two things depending on which pane you
                // imagine yourself in is worse than a second key. F is what Unity users already
                // press to frame a selection; numpad period is what Blender users press.
                case KeyCode.F:
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
                    FrameSelection();
                    break;
                case KeyCode.A:
                    if (keyEvent.altKey)
                    {
                        DeselectAllKeys();
                    }
                    else
                    {
                        SelectAllKeys();
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

        /// <summary>
        /// Undo one step and let the window catch up.
        /// </summary>
        /// <remarks>
        /// The refresh is not done here: <c>Undo.PerformUndo</c> raises
        /// <c>Undo.undoRedoPerformed</c>, which the window is already subscribed to. Refreshing in
        /// both places would rebuild the timeline twice per keystroke and, worse, would leave the
        /// button path and the shortcut path with different behaviour.
        /// </remarks>
        private void PerformUndo()
        {
            Undo.PerformUndo();
        }

        private void PerformRedo()
        {
            Undo.PerformRedo();
        }

        /// <summary>
        /// Whether a control that consumes typing currently has focus.
        /// </summary>
        /// <remarks>
        /// Checked by capability rather than by type name: any element that takes text input is one
        /// where Space and the arrows already mean something to the user. Unity's numeric fields are
        /// <c>TextField</c>s underneath, so this catches the frame and seconds fields this bar owns
        /// as well as any field added later.
        /// </remarks>
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
