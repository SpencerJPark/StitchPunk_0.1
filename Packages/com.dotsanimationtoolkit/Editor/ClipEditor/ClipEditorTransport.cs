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

        /// <summary>Marks a caption that doubles as its field's drag handle.</summary>
        private const string DraggableTransportLabelUssClassName =
            "clip-editor__transport-label--draggable";

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
        private IntegerField frameRateField;
        private Label frameCountLabel;
        private Button loopButton;
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

        /// <summary>
        /// Makes a transport caption the drag handle for the field beside it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The bar's captions are standalone labels rather than the fields' own, because a
        /// <c>BaseField</c>'s label carries an inspector's width and this is a compact strip. The
        /// cost was that none of these numbers had a drag zone at all — a field's dragger lives on
        /// its own label, and these fields have none, so there was nothing to take hold of. This
        /// hands the caption to a real <see cref="FieldMouseDragger{T}"/>, the same object Unity's
        /// own fields use, so the sensitivity, the acceleration and the shift/alt modifiers are
        /// Unity's rather than an imitation of them.
        /// </para>
        /// <para>
        /// <strong><c>isDelayed</c> is lifted for the length of the drag, and put back after.</strong>
        /// A delayed field's drag writes only the displayed text and commits on release — which is
        /// exactly "the number moves but nothing happens until I let go". It has to come back
        /// afterwards because typing still needs it, for the reason the fields' own comments give.
        /// </para>
        /// </remarks>
        private static void MakeCaptionDragHandle<TValue>(
            VisualElement caption, TextValueField<TValue> field)
        {
            if (caption == null || field == null)
            {
                return;
            }

            new FieldMouseDragger<TValue>(field).SetDragZone(caption);
            caption.AddToClassList(DraggableTransportLabelUssClassName);

            if (!field.isDelayed)
            {
                return;
            }
            caption.RegisterCallback<PointerDownEvent>(downEvent => field.isDelayed = false);

            // PointerCaptureOut rather than PointerUp: the dragger captures the caption, and a
            // capture lost any other way still has to put the field back, or typing quietly loses
            // its guard for the rest of the session.
            caption.RegisterCallback<PointerCaptureOutEvent>(
                captureOutEvent => field.isDelayed = true);
        }

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
                MakeCaptionDragHandle(
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
                MakeCaptionDragHandle(
                    rootVisualElement.Q<Label>("seconds-caption"), currentSecondsField);
            }

            clipLengthField = rootVisualElement.Q<FloatField>("clip-length-field");
            if (clipLengthField != null)
            {
                // Typing commits on blur or Enter, not per keystroke: "0.5" arrives as "0" first,
                // and the minimum-duration clamp would collapse the clip to a millisecond and
                // rebuild the timeline around it between two keystrokes. MakeCaptionDragHandle
                // lifts this for the length of a drag, where every value it passes through is one
                // the author is deliberately looking at.
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
                MakeCaptionDragHandle(
                    rootVisualElement.Q<Label>("length-caption"), clipLengthField);
            }

            // An integer field rather than a float one: the rate is whole frames per second here.
            // The frame count it defines is rounded to an integer anyway (ClipAsset.FrameCount) and
            // a VAT bake turns it into a whole number of texture rows, so dragging it steps one
            // frame at a time — the unit the number is actually read in. A clip carrying a
            // fractional rate from elsewhere reads back rounded, and is rounded the first time this
            // field writes it.
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
                MakeCaptionDragHandle(
                    rootVisualElement.Q<Label>("frame-rate-caption"), frameRateField);
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
                    "Preview playback multiplier. Negative plays backwards. Drag the Speed caption "
                    + "to scrub it.";

                // No value-changed callback: the tick reads this field directly, so there is nothing
                // for a drag to notify. It still needs the handle, being a number like the rest.
                MakeCaptionDragHandle(
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

            // Lives in the status row over the key area, not the transport bar (D14) — same reason
            // Quantize Keys does, above: it edits keys rather than answering "when". Bound here
            // anyway, same as that one, because BindTransportBar already resolves every control by
            // name and one place for that beats a binding split across files by which row a control
            // happens to sit in.
            addEventButton = rootVisualElement.Q<Button>("add-event-button");
            if (addEventButton != null)
            {
                // Amendment A55: opens the event picker rather than guessing which event to place.
                // addEventButton is kept as a field (rather than captured locally) because the
                // picker needs it as its anchor.
                addEventButton.clicked += OpenAddEventPicker;
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
                // Not written into a field being typed in, for the reason SyncTransportFromClip
                // gives: this runs from the field's own callback, so an unconditional write means
                // the field fights its own edit and the caret jumps on every keystroke. A caption
                // drag is deliberately not excluded — the capture is on the caption, not inside the
                // field — because the write-back is what clamps the readout at the clip's ends.
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

            // Requested: Length and FPS are dragged now, and a full timeline rebuild on every mouse
            // move of that drag is wasted work at 30 a second.
            RequestTimelineRebuild();
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

            // Undo and redo are keyboard-only. They had buttons in the bar, but a transport is
            // about time and they are not: every editor in Unity undoes with Ctrl+Z, and two
            // buttons restating that were paying for themselves in bar width.
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

                // Copy, paste and duplicate are handled window-wide as well as on the lane stack,
                // because pasting keys onto a different object means selecting that object first —
                // and selecting it puts the focus in the hierarchy, where the timeline's own
                // handler never runs. The lane stack still gets first refusal and stops the event
                // when it acts, so a paste is never made twice.
                case KeyCode.C:
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
