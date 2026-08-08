// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System.Collections.Generic;
using StitchPunk.AnimationToolkit.Authoring;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace StitchPunk.AnimationToolkit.Editor
{
    /// <summary>
    /// The clip timeline editor (architecture section 7.1, 7.2).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Replaces the host's immediate-mode window, whose verified feature list is the parity target:
    /// clip selector, transport, timeline, draggable keys, double-click add, context inspector,
    /// keyboard map, copy/paste. Built entirely on UI Toolkit — the packaging conformance scan
    /// forbids immediate-mode drawing calls anywhere in package Editor code.
    /// </para>
    /// <para>
    /// <strong>Undo is per gesture, not per mutation</strong> (section 7.4). A key drag records the
    /// clip once on pointer-down and collapses everything up to pointer-up into one step, so one
    /// drag is one Ctrl+Z. The audit found the host's timeline was dirty-flag-only — drags and
    /// inspector edits simply were not undoable — which is the gap this closes.
    /// </para>
    /// </remarks>
    public sealed class ClipEditorWindow : EditorWindow
    {
        private const float PlaybackHertz = 30f;

        private ObjectField clipSetField;
        private ListView clipListView;
        private ToolbarToggle playToggle;
        private ToolbarToggle snapToggle;
        private IntegerField frameCountField;
        private Label timeLabel;
        private VisualElement trackHeaderColumn;
        private VisualElement laneColumn;
        private VisualElement laneStack;
        private TimeRulerElement ruler;
        private PlayheadElement playhead;
        private VisualElement inspectorPane;
        private Label statusLabel;

        private ClipSetAsset clipSet;
        private ClipAsset selectedClip;
        private SerializedObject clipSerializedObject;

        private readonly HashSet<KeyAddress> selectedKeys = new HashSet<KeyAddress>();

        private bool isPlaying;
        private double lastTickTime;
        private float playheadTime;

        // Drag state. The undo group is captured on pointer-down so every move inside the gesture
        // collapses into it on release.
        private bool isDraggingKeys;
        private int gestureUndoGroup;
        private float dragPreviousTime;
        private TimelineTrackKind dragTrackKind;
        private int dragTrackIndex;

        [MenuItem("Window/DOTS Animation Toolkit/Clip Editor")]
        public static void ShowWindow()
        {
            ClipEditorWindow window = GetWindow<ClipEditorWindow>();
            window.titleContent = new GUIContent("Clip Editor");
            window.minSize = new Vector2(820f, 460f);
        }

        private void OnEnable()
        {
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.update += OnEditorTick;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorApplication.update -= OnEditorTick;
        }

        /// <summary>
        /// Undo replaces the key lists wholesale, so held addresses may now point past the end.
        /// Clearing the selection is the honest response — keeping it would leave the window
        /// showing a selection of keys that no longer exist.
        /// </summary>
        private void OnUndoRedo()
        {
            selectedKeys.Clear();
            RefreshSerializedClip();
            RebuildTimeline();
        }

        // -------------------------------------------------------------------------------------
        // Layout
        // -------------------------------------------------------------------------------------

        private void CreateGUI()
        {
            VisualElement root = rootVisualElement;
            root.Add(BuildToolbar());

            TwoPaneSplitView outerSplit = new TwoPaneSplitView(0, 200f, TwoPaneSplitViewOrientation.Horizontal);
            outerSplit.style.flexGrow = 1f;
            root.Add(outerSplit);

            clipListView = new ListView
            {
                fixedItemHeight = 20f,
                selectionType = SelectionType.Single
            };
            clipListView.makeItem = MakeClipRow;
            clipListView.bindItem = BindClipRow;
            clipListView.selectionChanged += OnClipSelectionChanged;
            outerSplit.Add(clipListView);

            TwoPaneSplitView innerSplit = new TwoPaneSplitView(1, 260f, TwoPaneSplitViewOrientation.Horizontal);
            outerSplit.Add(innerSplit);
            innerSplit.Add(BuildTimelinePane());

            inspectorPane = new ScrollView();
            inspectorPane.style.paddingLeft = 6f;
            inspectorPane.style.paddingTop = 6f;
            innerSplit.Add(inspectorPane);

            RebuildTimeline();
        }

        private Toolbar BuildToolbar()
        {
            Toolbar toolbar = new Toolbar();

            clipSetField = new ObjectField
            {
                objectType = typeof(ClipSetAsset),
                allowSceneObjects = false
            };
            clipSetField.style.width = 240f;
            clipSetField.RegisterValueChangedCallback(OnClipSetChanged);
            toolbar.Add(MakeToolbarLabel("Clip Set"));
            toolbar.Add(clipSetField);

            playToggle = new ToolbarToggle { text = "Play" };
            playToggle.RegisterValueChangedCallback(changeEvent => SetPlaying(changeEvent.newValue));
            toolbar.Add(playToggle);

            toolbar.Add(new ToolbarButton(() => SetPlayheadTime(0f)) { text = "|<" });

            timeLabel = MakeToolbarLabel("0.000s");
            timeLabel.style.width = 66f;
            toolbar.Add(timeLabel);

            snapToggle = new ToolbarToggle { text = "Snap", value = true };
            toolbar.Add(snapToggle);

            frameCountField = new IntegerField { value = 30 };
            frameCountField.style.width = 44f;
            frameCountField.RegisterValueChangedCallback(changeEvent =>
            {
                if (ruler != null)
                {
                    ruler.frameCount = Mathf.Max(1, changeEvent.newValue);
                    ruler.MarkDirtyRepaint();
                }
            });
            toolbar.Add(MakeToolbarLabel("Frames"));
            toolbar.Add(frameCountField);

            return toolbar;
        }

        private VisualElement BuildTimelinePane()
        {
            VisualElement pane = new VisualElement();
            pane.style.flexDirection = FlexDirection.Column;
            pane.style.flexGrow = 1f;

            statusLabel = new Label("Assign a clip set.");
            statusLabel.style.marginLeft = 6f;
            statusLabel.style.marginTop = 4f;
            statusLabel.style.marginBottom = 4f;
            pane.Add(statusLabel);

            VisualElement timelineRow = new VisualElement();
            timelineRow.style.flexDirection = FlexDirection.Row;
            timelineRow.style.flexGrow = 1f;

            VisualElement headerStack = new VisualElement();
            headerStack.style.width = 170f;
            headerStack.style.flexShrink = 0f;
            // A spacer exactly the ruler's height, so header row N lines up with lane row N.
            VisualElement headerSpacer = new VisualElement();
            headerSpacer.style.height = 20f;
            headerSpacer.style.flexShrink = 0f;
            headerStack.Add(headerSpacer);
            trackHeaderColumn = new VisualElement();
            headerStack.Add(trackHeaderColumn);
            timelineRow.Add(headerStack);

            // The lane stack owns keyboard focus: shortcuts registered here cannot swallow
            // keystrokes meant for the inspector's own text fields.
            laneStack = new VisualElement();
            laneStack.style.flexGrow = 1f;
            laneStack.focusable = true;
            laneStack.RegisterCallback<KeyDownEvent>(OnTimelineKeyDown);

            ruler = new TimeRulerElement();
            ruler.scrubbed += SetPlayheadTime;
            laneStack.Add(ruler);

            laneColumn = new VisualElement();
            laneStack.Add(laneColumn);

            playhead = new PlayheadElement();
            laneStack.Add(playhead);

            timelineRow.Add(laneStack);
            pane.Add(timelineRow);
            return pane;
        }

        private static Label MakeToolbarLabel(string text)
        {
            Label label = new Label(text);
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
            label.style.marginLeft = 4f;
            label.style.marginRight = 4f;
            return label;
        }

        private static VisualElement MakeClipRow()
        {
            Label label = new Label();
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
            label.style.marginLeft = 6f;
            return label;
        }

        private void BindClipRow(VisualElement element, int index)
        {
            Label label = element as Label;
            if (label == null || clipSet == null || clipSet.clips == null || index >= clipSet.clips.Count)
            {
                return;
            }
            ClipAsset clip = clipSet.clips[index];
            label.text = clip != null ? clip.name : "<missing>";
        }

        // -------------------------------------------------------------------------------------
        // Selection plumbing
        // -------------------------------------------------------------------------------------

        private void OnClipSetChanged(ChangeEvent<Object> changeEvent)
        {
            clipSet = changeEvent.newValue as ClipSetAsset;
            SelectClip(null);

            clipListView.itemsSource = clipSet != null && clipSet.clips != null
                ? (System.Collections.IList)clipSet.clips
                : new List<ClipAsset>();
            clipListView.Rebuild();
        }

        private void OnClipSelectionChanged(IEnumerable<object> selection)
        {
            ClipAsset clip = null;
            foreach (object item in selection)
            {
                clip = item as ClipAsset;
                break;
            }
            SelectClip(clip);
        }

        private void SelectClip(ClipAsset clip)
        {
            selectedClip = clip;
            selectedKeys.Clear();
            SetPlaying(false);
            playheadTime = 0f;
            RefreshSerializedClip();
            RebuildTimeline();
        }

        private void RefreshSerializedClip()
        {
            clipSerializedObject = selectedClip != null ? new SerializedObject(selectedClip) : null;
        }

        // -------------------------------------------------------------------------------------
        // Transport
        // -------------------------------------------------------------------------------------

        private void SetPlaying(bool playing)
        {
            isPlaying = playing;
            lastTickTime = EditorApplication.timeSinceStartup;
            if (playToggle != null && playToggle.value != playing)
            {
                playToggle.SetValueWithoutNotify(playing);
            }
        }

        private void OnEditorTick()
        {
            if (!isPlaying || selectedClip == null)
            {
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            double elapsed = now - lastTickTime;
            if (elapsed < 1.0 / PlaybackHertz)
            {
                return;
            }
            lastTickTime = now;

            // Advance in seconds then convert, so a clip's duration sets playback speed exactly the
            // way it does at runtime rather than every clip taking the same wall time.
            float duration = Mathf.Max(ClipAsset.MinimumDuration, selectedClip.duration);
            float advanced = playheadTime + (float)elapsed / duration;
            SetPlayheadTime(advanced - Mathf.Floor(advanced));
        }

        private void SetPlayheadTime(float normalizedTime)
        {
            playheadTime = Mathf.Clamp01(normalizedTime);
            if (playhead != null)
            {
                playhead.NormalizedTime = playheadTime;
            }
            if (timeLabel != null)
            {
                float duration = selectedClip != null ? selectedClip.duration : 1f;
                timeLabel.text = (playheadTime * duration).ToString("0.000") + "s";
            }
        }

        /// <summary>Frames to snap to, or zero when snapping is off.</summary>
        private int SnapFrameCount
        {
            get
            {
                if (snapToggle == null || !snapToggle.value)
                {
                    return 0;
                }
                return frameCountField != null ? Mathf.Max(1, frameCountField.value) : 30;
            }
        }

        // -------------------------------------------------------------------------------------
        // Timeline construction
        // -------------------------------------------------------------------------------------

        private void RebuildTimeline()
        {
            if (trackHeaderColumn == null || laneColumn == null)
            {
                return;
            }

            trackHeaderColumn.Clear();
            laneColumn.Clear();

            if (selectedClip == null)
            {
                statusLabel.text = clipSet == null ? "Assign a clip set." : "Select a clip.";
                RebuildInspector();
                return;
            }

            statusLabel.text = selectedClip.name
                + "   duration " + selectedClip.duration.ToString("0.###") + "s"
                + "   loop " + selectedClip.defaultLoop.ToString()
                + "   selected " + selectedKeys.Count.ToString();

            ruler.durationSeconds = selectedClip.duration;
            ruler.frameCount = frameCountField != null ? Mathf.Max(1, frameCountField.value) : 30;
            ruler.MarkDirtyRepaint();

            int rowIndex = 0;
            List<float> times = new List<float>();

            List<TransformTrack> transformTracks = selectedClip.transformTracks;
            for (int trackIndex = 0; transformTracks != null && trackIndex < transformTracks.Count; trackIndex++)
            {
                TransformTrack track = transformTracks[trackIndex];
                if (track == null)
                {
                    continue;
                }
                times.Clear();
                for (int keyIndex = 0; keyIndex < track.keys.Count; keyIndex++)
                {
                    times.Add(track.keys[keyIndex].normalizedTime);
                }
                AddTrackRow(
                    "T " + track.targetId.ToString() + "  " + track.channels.ToString(),
                    TimelineTrackKind.Transform, trackIndex, times, rowIndex++);
            }

            List<SpriteTrack> spriteTracks = selectedClip.spriteTracks;
            for (int trackIndex = 0; spriteTracks != null && trackIndex < spriteTracks.Count; trackIndex++)
            {
                SpriteTrack track = spriteTracks[trackIndex];
                if (track == null)
                {
                    continue;
                }
                times.Clear();
                for (int keyIndex = 0; keyIndex < track.keys.Count; keyIndex++)
                {
                    times.Add(track.keys[keyIndex].normalizedTime);
                }
                AddTrackRow(
                    "S " + track.targetId.ToString() + "  " + track.mode.ToString(),
                    TimelineTrackKind.Sprite, trackIndex, times, rowIndex++);
            }

            if (selectedClip.events != null)
            {
                times.Clear();
                for (int eventIndex = 0; eventIndex < selectedClip.events.Count; eventIndex++)
                {
                    times.Add(selectedClip.events[eventIndex].normalizedTime);
                }
                AddTrackRow("Events", TimelineTrackKind.Event, 0, times, rowIndex++);
            }

            SetPlayheadTime(playheadTime);
            RebuildInspector();
        }

        private void AddTrackRow(
            string headerText, TimelineTrackKind trackKind, int trackIndex, List<float> times, int rowIndex)
        {
            Label header = new Label(headerText);
            header.style.height = 22f;
            header.style.unityTextAlign = TextAnchor.MiddleLeft;
            header.style.marginLeft = 6f;
            header.tooltip = headerText;
            trackHeaderColumn.Add(header);

            TrackLaneElement lane = new TrackLaneElement
            {
                trackKind = trackKind,
                trackIndex = trackIndex,
                isAlternateRow = (rowIndex & 1) == 1,
                isKeySelected = selectedKeys.Contains
            };
            lane.SetKeyTimes(times);
            lane.keyPointerDown += OnKeyPointerDown;
            lane.lanePointerDown += OnLanePointerDown;
            laneColumn.Add(lane);
        }

        private void RepaintLanes()
        {
            for (int childIndex = 0; childIndex < laneColumn.childCount; childIndex++)
            {
                laneColumn[childIndex].MarkDirtyRepaint();
            }
        }

        // -------------------------------------------------------------------------------------
        // Gestures. One undo step per gesture (section 7.4).
        // -------------------------------------------------------------------------------------

        private void OnKeyPointerDown(KeyAddress address, PointerDownEvent pointerEvent)
        {
            laneStack.Focus();

            bool additive = pointerEvent.shiftKey || pointerEvent.ctrlKey || pointerEvent.commandKey;
            if (!additive && !selectedKeys.Contains(address))
            {
                selectedKeys.Clear();
            }
            if (additive && selectedKeys.Contains(address))
            {
                selectedKeys.Remove(address);
            }
            else
            {
                selectedKeys.Add(address);
            }

            if (selectedClip == null)
            {
                RepaintLanes();
                return;
            }

            SetPlayheadTime(GetKeyTime(address));

            // Everything from here to pointer-up becomes one undo step. Recording BEFORE the first
            // mutation is what makes undo restore the pre-drag state rather than some intermediate
            // frame of it.
            BeginUndoGesture("Move Animation Keys");

            isDraggingKeys = true;
            dragPreviousTime = GetKeyTime(address);
            dragTrackKind = address.trackKind;
            dragTrackIndex = address.trackIndex;

            VisualElement lane = pointerEvent.currentTarget as VisualElement;
            if (lane != null)
            {
                lane.CapturePointer(pointerEvent.pointerId);
                lane.RegisterCallback<PointerMoveEvent>(OnDragMove);
                lane.RegisterCallback<PointerUpEvent>(OnDragEnd);
            }
            RepaintLanes();
            RebuildInspector();
        }

        private void OnDragMove(PointerMoveEvent moveEvent)
        {
            if (!isDraggingKeys || selectedClip == null)
            {
                return;
            }

            TrackLaneElement lane = moveEvent.currentTarget as TrackLaneElement;
            if (lane == null)
            {
                return;
            }

            TimelineGeometry geometry = TimelineGeometry.Create(lane.contentRect.width);
            float pointerTime = TimelineGeometry.Snap(
                geometry.XToTime(moveEvent.localPosition.x), SnapFrameCount);
            float delta = pointerTime - dragPreviousTime;
            if (Mathf.Abs(delta) < 1e-6f)
            {
                return;
            }

            // The whole selection moves by the grabbed key's delta, so relative spacing survives a
            // multi-key drag. Moving every key to the pointer instead would collapse them together.
            foreach (KeyAddress address in selectedKeys)
            {
                SetKeyTime(address, Mathf.Clamp01(GetKeyTime(address) + delta));
            }
            dragPreviousTime = pointerTime;

            EditorUtility.SetDirty(selectedClip);
            SetPlayheadTime(pointerTime);
            RebuildTimeline();
        }

        private void OnDragEnd(PointerUpEvent upEvent)
        {
            VisualElement lane = upEvent.currentTarget as VisualElement;
            if (lane != null)
            {
                lane.ReleasePointer(upEvent.pointerId);
                lane.UnregisterCallback<PointerMoveEvent>(OnDragMove);
                lane.UnregisterCallback<PointerUpEvent>(OnDragEnd);
            }

            if (!isDraggingKeys)
            {
                return;
            }
            isDraggingKeys = false;

            EndUndoGesture();
            if (selectedClip != null)
            {
                SortTrackKeys(dragTrackKind, dragTrackIndex);
                RebuildTimeline();
            }
        }

        /// <summary>
        /// Handles a press on empty lane space: single click moves the playhead and clears the
        /// selection, double click adds a key (parity item "double-click add").
        /// </summary>
        private void OnLanePointerDown(
            TimelineTrackKind trackKind, int trackIndex, float normalizedTime, PointerDownEvent pointerEvent)
        {
            laneStack.Focus();

            if (pointerEvent.clickCount < 2 || selectedClip == null)
            {
                if (!pointerEvent.shiftKey && !pointerEvent.ctrlKey && !pointerEvent.commandKey)
                {
                    selectedKeys.Clear();
                    RepaintLanes();
                    RebuildInspector();
                }
                SetPlayheadTime(normalizedTime);
                return;
            }

            float insertTime = TimelineGeometry.Snap(normalizedTime, SnapFrameCount);
            BeginUndoGesture("Add Animation Key");
            InsertKey(trackKind, trackIndex, insertTime);
            EndUndoGesture();

            EditorUtility.SetDirty(selectedClip);
            selectedKeys.Clear();
            SortTrackKeys(trackKind, trackIndex);
            SetPlayheadTime(insertTime);
            RebuildTimeline();
        }

        private void BeginUndoGesture(string actionName)
        {
            Undo.IncrementCurrentGroup();
            gestureUndoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(actionName);
            Undo.RecordObject(selectedClip, actionName);
        }

        private void EndUndoGesture()
        {
            // Collapsing on release is what turns a drag of dozens of move events into a single
            // Ctrl+Z rather than dozens of them.
            Undo.CollapseUndoOperations(gestureUndoGroup);
            RefreshSerializedClip();
        }

        // -------------------------------------------------------------------------------------
        // Keyboard map (parity item "full keyboard map")
        // -------------------------------------------------------------------------------------

        private void OnTimelineKeyDown(KeyDownEvent keyEvent)
        {
            if (selectedClip == null)
            {
                return;
            }

            bool commandModifier = keyEvent.ctrlKey || keyEvent.commandKey;
            float frameStep = 1f / Mathf.Max(1, frameCountField != null ? frameCountField.value : 30);

            switch (keyEvent.keyCode)
            {
                case KeyCode.Space:
                    SetPlaying(!isPlaying);
                    break;
                case KeyCode.Delete:
                case KeyCode.Backspace:
                    DeleteSelectedKeys();
                    break;
                case KeyCode.Home:
                    SetPlayheadTime(0f);
                    break;
                case KeyCode.End:
                    SetPlayheadTime(1f);
                    break;
                case KeyCode.LeftArrow:
                    SetPlayheadTime(playheadTime - frameStep);
                    break;
                case KeyCode.RightArrow:
                    SetPlayheadTime(playheadTime + frameStep);
                    break;
                case KeyCode.C:
                    if (!commandModifier)
                    {
                        return;
                    }
                    ClipKeyClipboard.Copy(selectedClip, selectedKeys);
                    break;
                case KeyCode.V:
                    if (!commandModifier)
                    {
                        return;
                    }
                    PasteKeysAtPlayhead();
                    break;
                case KeyCode.D:
                    // Duplicate is copy+paste at the playhead, so it cannot drift from paste's
                    // behaviour the way a second implementation would.
                    if (!commandModifier)
                    {
                        return;
                    }
                    ClipKeyClipboard.Copy(selectedClip, selectedKeys);
                    PasteKeysAtPlayhead();
                    break;
                default:
                    return;
            }

            keyEvent.StopPropagation();
        }

        private void PasteKeysAtPlayhead()
        {
            if (!ClipKeyClipboard.HasContent)
            {
                return;
            }

            BeginUndoGesture("Paste Animation Keys");
            int pastedCount = ClipKeyClipboard.Paste(selectedClip, playheadTime);
            EndUndoGesture();

            if (pastedCount == 0)
            {
                return;
            }
            EditorUtility.SetDirty(selectedClip);
            SortAllTracks();
            RebuildTimeline();
        }

        /// <summary>
        /// Removes every selected key.
        /// </summary>
        /// <remarks>
        /// Addresses are removed in <em>descending</em> index order. Deleting ascending would shift
        /// the indices of the not-yet-deleted addresses down by one each time, so the second
        /// deletion within any track would silently hit the wrong key.
        /// </remarks>
        private void DeleteSelectedKeys()
        {
            if (selectedKeys.Count == 0)
            {
                return;
            }

            List<KeyAddress> ordered = new List<KeyAddress>(selectedKeys);
            ordered.Sort((first, second) => second.keyIndex.CompareTo(first.keyIndex));

            BeginUndoGesture("Delete Animation Keys");
            for (int addressIndex = 0; addressIndex < ordered.Count; addressIndex++)
            {
                KeyAddress address = ordered[addressIndex];
                switch (address.trackKind)
                {
                    case TimelineTrackKind.Transform:
                        if (address.trackIndex < selectedClip.transformTracks.Count
                            && address.keyIndex < selectedClip.transformTracks[address.trackIndex].keys.Count)
                        {
                            selectedClip.transformTracks[address.trackIndex].keys.RemoveAt(address.keyIndex);
                        }
                        break;
                    case TimelineTrackKind.Sprite:
                        if (address.trackIndex < selectedClip.spriteTracks.Count
                            && address.keyIndex < selectedClip.spriteTracks[address.trackIndex].keys.Count)
                        {
                            selectedClip.spriteTracks[address.trackIndex].keys.RemoveAt(address.keyIndex);
                        }
                        break;
                    default:
                        if (address.keyIndex < selectedClip.events.Count)
                        {
                            selectedClip.events.RemoveAt(address.keyIndex);
                        }
                        break;
                }
            }
            EndUndoGesture();

            EditorUtility.SetDirty(selectedClip);
            selectedKeys.Clear();
            RebuildTimeline();
        }

        // -------------------------------------------------------------------------------------
        // Key access. The lists hold structs, so every edit is a read-modify-write.
        // -------------------------------------------------------------------------------------

        private float GetKeyTime(KeyAddress address)
        {
            if (selectedClip == null)
            {
                return 0f;
            }
            switch (address.trackKind)
            {
                case TimelineTrackKind.Transform:
                    return selectedClip.transformTracks[address.trackIndex].keys[address.keyIndex].normalizedTime;
                case TimelineTrackKind.Sprite:
                    return selectedClip.spriteTracks[address.trackIndex].keys[address.keyIndex].normalizedTime;
                default:
                    return selectedClip.events[address.keyIndex].normalizedTime;
            }
        }

        private void SetKeyTime(KeyAddress address, float normalizedTime)
        {
            if (selectedClip == null)
            {
                return;
            }
            switch (address.trackKind)
            {
                case TimelineTrackKind.Transform:
                {
                    TransformTrack track = selectedClip.transformTracks[address.trackIndex];
                    TransformKey key = track.keys[address.keyIndex];
                    key.normalizedTime = normalizedTime;
                    track.keys[address.keyIndex] = key;
                    break;
                }
                case TimelineTrackKind.Sprite:
                {
                    SpriteTrack track = selectedClip.spriteTracks[address.trackIndex];
                    SpriteKey key = track.keys[address.keyIndex];
                    key.normalizedTime = normalizedTime;
                    track.keys[address.keyIndex] = key;
                    break;
                }
                default:
                {
                    EventMarker marker = selectedClip.events[address.keyIndex];
                    marker.normalizedTime = normalizedTime;
                    selectedClip.events[address.keyIndex] = marker;
                    break;
                }
            }
        }

        /// <summary>
        /// Adds a key at <paramref name="normalizedTime"/>, copying the key at or before it.
        /// </summary>
        /// <remarks>
        /// Copying the preceding key means adding a key does not change the pose the clip produces
        /// — it only creates somewhere to edit. A key inserted at type defaults would snap the part
        /// to the origin at zero scale the moment it appeared, which reads as the editor having
        /// broken the animation.
        /// </remarks>
        private void InsertKey(TimelineTrackKind trackKind, int trackIndex, float normalizedTime)
        {
            switch (trackKind)
            {
                case TimelineTrackKind.Transform:
                {
                    List<TransformKey> keys = selectedClip.transformTracks[trackIndex].keys;
                    TransformKey inserted = new TransformKey
                    {
                        position = float3.zero,
                        scale = new float2(1f, 1f),
                        interpolation = Interpolation.Linear
                    };
                    for (int keyIndex = 0; keyIndex < keys.Count; keyIndex++)
                    {
                        if (keys[keyIndex].normalizedTime <= normalizedTime)
                        {
                            inserted = keys[keyIndex];
                        }
                    }
                    inserted.normalizedTime = normalizedTime;
                    keys.Add(inserted);
                    break;
                }
                case TimelineTrackKind.Sprite:
                {
                    List<SpriteKey> keys = selectedClip.spriteTracks[trackIndex].keys;
                    SpriteKey inserted = new SpriteKey();
                    for (int keyIndex = 0; keyIndex < keys.Count; keyIndex++)
                    {
                        if (keys[keyIndex].normalizedTime <= normalizedTime)
                        {
                            inserted = keys[keyIndex];
                        }
                    }
                    inserted.normalizedTime = normalizedTime;
                    keys.Add(inserted);
                    break;
                }
                default:
                {
                    selectedClip.events.Add(new EventMarker { normalizedTime = normalizedTime });
                    break;
                }
            }
        }

        /// <summary>
        /// Restores ascending key order after an edit.
        /// </summary>
        /// <remarks>
        /// Validation rule V03 requires ascending times and the sampler's segment search assumes
        /// it — an out-of-order key does not throw, it silently makes a segment unreachable.
        /// Sorting once at the end of a gesture rather than per move keeps indices stable for its
        /// duration, which is what lets a multi-key selection survive a drag.
        /// </remarks>
        private void SortTrackKeys(TimelineTrackKind trackKind, int trackIndex)
        {
            switch (trackKind)
            {
                case TimelineTrackKind.Transform:
                    selectedClip.transformTracks[trackIndex].keys.Sort(CompareTransformKeys);
                    break;
                case TimelineTrackKind.Sprite:
                    selectedClip.spriteTracks[trackIndex].keys.Sort(CompareSpriteKeys);
                    break;
                default:
                    selectedClip.events.Sort(CompareEventMarkers);
                    break;
            }

            // Indices moved, so held addresses no longer mean what they meant.
            selectedKeys.Clear();
        }

        private void SortAllTracks()
        {
            for (int trackIndex = 0; trackIndex < selectedClip.transformTracks.Count; trackIndex++)
            {
                selectedClip.transformTracks[trackIndex].keys.Sort(CompareTransformKeys);
            }
            for (int trackIndex = 0; trackIndex < selectedClip.spriteTracks.Count; trackIndex++)
            {
                selectedClip.spriteTracks[trackIndex].keys.Sort(CompareSpriteKeys);
            }
            selectedClip.events.Sort(CompareEventMarkers);
            selectedKeys.Clear();
        }

        private static int CompareTransformKeys(TransformKey first, TransformKey second)
        {
            return first.normalizedTime.CompareTo(second.normalizedTime);
        }

        private static int CompareSpriteKeys(SpriteKey first, SpriteKey second)
        {
            return first.normalizedTime.CompareTo(second.normalizedTime);
        }

        private static int CompareEventMarkers(EventMarker first, EventMarker second)
        {
            return first.normalizedTime.CompareTo(second.normalizedTime);
        }

        // -------------------------------------------------------------------------------------
        // Context inspector. Bound fields get undo, dirtying and prefab overrides for free
        // (section 7.4), so nothing here hand-rolls an edit path.
        // -------------------------------------------------------------------------------------

        private void RebuildInspector()
        {
            if (inspectorPane == null)
            {
                return;
            }
            inspectorPane.Clear();

            if (selectedClip == null || clipSerializedObject == null)
            {
                return;
            }
            clipSerializedObject.Update();

            if (selectedKeys.Count == 0)
            {
                inspectorPane.Add(MakeHeading("Clip"));
                AddBoundField("duration");
                AddBoundField("defaultLoop");
                AddBoundField("rig");
                inspectorPane.Bind(clipSerializedObject);
                return;
            }

            // Multi-select edits the last address only. Driving N keys from one field needs a
            // mixed-value story the property system does not hand us, so rather than pretend, the
            // inspector says plainly which key it is editing.
            KeyAddress shown = default(KeyAddress);
            foreach (KeyAddress address in selectedKeys)
            {
                shown = address;
            }

            SerializedProperty keyProperty = FindKeyProperty(shown);
            if (keyProperty == null)
            {
                return;
            }

            inspectorPane.Add(MakeHeading(
                shown.trackKind.ToString() + " key "
                + shown.trackIndex.ToString() + " / " + shown.keyIndex.ToString()));
            if (selectedKeys.Count > 1)
            {
                inspectorPane.Add(new Label(
                    selectedKeys.Count.ToString() + " selected — editing the last."));
            }

            inspectorPane.Add(new PropertyField(keyProperty));
            inspectorPane.Bind(clipSerializedObject);
        }

        private static Label MakeHeading(string text)
        {
            Label label = new Label(text);
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.marginBottom = 4f;
            return label;
        }

        private void AddBoundField(string propertyPath)
        {
            SerializedProperty property = clipSerializedObject.FindProperty(propertyPath);
            if (property != null)
            {
                inspectorPane.Add(new PropertyField(property));
            }
        }

        private SerializedProperty FindKeyProperty(KeyAddress address)
        {
            switch (address.trackKind)
            {
                case TimelineTrackKind.Transform:
                    return FindTrackKeyProperty("transformTracks", address);
                case TimelineTrackKind.Sprite:
                    return FindTrackKeyProperty("spriteTracks", address);
                default:
                {
                    SerializedProperty events = clipSerializedObject.FindProperty("events");
                    if (events == null || address.keyIndex >= events.arraySize)
                    {
                        return null;
                    }
                    return events.GetArrayElementAtIndex(address.keyIndex);
                }
            }
        }

        private SerializedProperty FindTrackKeyProperty(string tracksPath, KeyAddress address)
        {
            SerializedProperty tracks = clipSerializedObject.FindProperty(tracksPath);
            if (tracks == null || address.trackIndex >= tracks.arraySize)
            {
                return null;
            }
            SerializedProperty keys = tracks.GetArrayElementAtIndex(address.trackIndex)
                .FindPropertyRelative("keys");
            if (keys == null || address.keyIndex >= keys.arraySize)
            {
                return null;
            }
            return keys.GetArrayElementAtIndex(address.keyIndex);
        }
    }
}
