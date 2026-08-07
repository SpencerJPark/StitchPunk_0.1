// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System.Collections.Generic;
using StitchPunk.AnimationToolkit.Authoring;
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
    /// Replaces the host's IMGUI window, whose verified feature list is the parity target. UI
    /// Toolkit throughout — no <c>OnGUI</c>, <c>GUILayout</c> or <c>Handles</c> anywhere in package
    /// Editor code, which the packaging conformance scan enforces.
    /// </para>
    /// <para>
    /// <strong>Undo is per gesture, not per mutation</strong> (section 7.4). A key drag records the
    /// clip once on pointer-down and collapses everything up to pointer-up into a single step, so
    /// one drag is one Ctrl+Z. The audit found the host's timeline was <c>SetDirty</c>-only — drags
    /// and inspector edits simply were not undoable — which is the gap this closes.
    /// </para>
    /// </remarks>
    public sealed class ClipEditorWindow : EditorWindow
    {
        private ObjectField clipSetField;
        private ListView clipListView;
        private VisualElement trackHeaderColumn;
        private VisualElement laneColumn;
        private Label statusLabel;

        private ClipSetAsset clipSet;
        private ClipAsset selectedClip;

        private readonly HashSet<KeyAddress> selectedKeys = new HashSet<KeyAddress>();

        // Drag state. undoGroup is captured on pointer-down so every move inside the gesture
        // collapses into it on release.
        private bool isDraggingKeys;
        private int dragUndoGroup;
        private float dragStartTime;
        private KeyAddress dragAnchor;

        [MenuItem("Window/DOTS Animation Toolkit/Clip Editor")]
        public static void ShowWindow()
        {
            ClipEditorWindow window = GetWindow<ClipEditorWindow>();
            window.titleContent = new GUIContent("Clip Editor");
            window.minSize = new Vector2(720f, 420f);
        }

        private void OnEnable()
        {
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
        }

        /// <summary>
        /// Undo replaces the key lists wholesale, so held addresses may now point past the end.
        /// Clearing the selection is the honest response — keeping it would leave the UI showing a
        /// selection of keys that no longer exist.
        /// </summary>
        private void OnUndoRedo()
        {
            selectedKeys.Clear();
            RebuildTimeline();
        }

        private void CreateGUI()
        {
            VisualElement root = rootVisualElement;

            Toolbar toolbar = new Toolbar();
            clipSetField = new ObjectField
            {
                objectType = typeof(ClipSetAsset),
                allowSceneObjects = false
            };
            clipSetField.style.width = 260f;
            clipSetField.RegisterValueChangedCallback(OnClipSetChanged);
            toolbar.Add(new Label("Clip Set") { style = { unityTextAlign = TextAnchor.MiddleLeft, marginLeft = 4f, marginRight = 4f } });
            toolbar.Add(clipSetField);
            root.Add(toolbar);

            TwoPaneSplitView split = new TwoPaneSplitView(0, 220f, TwoPaneSplitViewOrientation.Horizontal);
            root.Add(split);

            clipListView = new ListView
            {
                fixedItemHeight = 20f,
                selectionType = SelectionType.Single
            };
            clipListView.makeItem = () => new Label { style = { unityTextAlign = TextAnchor.MiddleLeft, marginLeft = 6f } };
            clipListView.bindItem = BindClipRow;
            clipListView.selectionChanged += OnClipSelectionChanged;
            split.Add(clipListView);

            VisualElement timelinePane = new VisualElement();
            timelinePane.style.flexDirection = FlexDirection.Column;

            statusLabel = new Label("Assign a clip set.");
            statusLabel.style.marginLeft = 6f;
            statusLabel.style.marginTop = 4f;
            statusLabel.style.marginBottom = 4f;
            timelinePane.Add(statusLabel);

            ScrollView timelineScroll = new ScrollView();
            timelineScroll.style.flexGrow = 1f;

            VisualElement timelineRow = new VisualElement();
            timelineRow.style.flexDirection = FlexDirection.Row;

            trackHeaderColumn = new VisualElement();
            trackHeaderColumn.style.width = 170f;
            trackHeaderColumn.style.flexShrink = 0f;
            timelineRow.Add(trackHeaderColumn);

            laneColumn = new VisualElement();
            laneColumn.style.flexGrow = 1f;
            timelineRow.Add(laneColumn);

            timelineScroll.Add(timelineRow);
            timelinePane.Add(timelineScroll);
            split.Add(timelinePane);
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

        private void OnClipSetChanged(ChangeEvent<Object> changeEvent)
        {
            clipSet = changeEvent.newValue as ClipSetAsset;
            selectedClip = null;
            selectedKeys.Clear();

            clipListView.itemsSource = clipSet != null && clipSet.clips != null
                ? (System.Collections.IList)clipSet.clips
                : new List<ClipAsset>();
            clipListView.Rebuild();
            RebuildTimeline();
        }

        private void OnClipSelectionChanged(IEnumerable<object> selection)
        {
            selectedClip = null;
            foreach (object item in selection)
            {
                selectedClip = item as ClipAsset;
                break;
            }
            selectedKeys.Clear();
            RebuildTimeline();
        }

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
                return;
            }

            statusLabel.text = selectedClip.name
                + "   duration " + selectedClip.duration.ToString("0.###") + "s"
                + "   loop " + selectedClip.defaultLoop.ToString();

            int rowIndex = 0;

            List<TransformTrack> transformTracks = selectedClip.transformTracks;
            for (int trackIndex = 0; transformTracks != null && trackIndex < transformTracks.Count; trackIndex++)
            {
                TransformTrack track = transformTracks[trackIndex];
                if (track == null)
                {
                    continue;
                }
                List<float> times = new List<float>();
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
                List<float> times = new List<float>();
                for (int keyIndex = 0; keyIndex < track.keys.Count; keyIndex++)
                {
                    times.Add(track.keys[keyIndex].normalizedTime);
                }
                AddTrackRow(
                    "S " + track.targetId.ToString() + "  " + track.mode.ToString(),
                    TimelineTrackKind.Sprite, trackIndex, times, rowIndex++);
            }

            if (selectedClip.events != null && selectedClip.events.Count > 0)
            {
                List<float> times = new List<float>();
                for (int eventIndex = 0; eventIndex < selectedClip.events.Count; eventIndex++)
                {
                    times.Add(selectedClip.events[eventIndex].normalizedTime);
                }
                AddTrackRow("Events", TimelineTrackKind.Event, 0, times, rowIndex++);
            }

            if (rowIndex == 0)
            {
                statusLabel.text += "   (no tracks)";
            }
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
            laneColumn.Add(lane);
        }

        // -------------------------------------------------------------------------------------
        // Gesture handling. One undo step per drag (section 7.4).
        // -------------------------------------------------------------------------------------

        private void OnKeyPointerDown(KeyAddress address, PointerDownEvent pointerEvent)
        {
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

            // Everything from here to pointer-up becomes one undo step. Recording BEFORE the first
            // mutation is what makes the undo restore the pre-drag state rather than some
            // intermediate frame of it.
            Undo.IncrementCurrentGroup();
            dragUndoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Move Animation Keys");
            Undo.RecordObject(selectedClip, "Move Animation Keys");

            isDraggingKeys = true;
            dragAnchor = address;
            dragStartTime = GetKeyTime(address);

            VisualElement lane = pointerEvent.target as VisualElement;
            if (lane != null)
            {
                lane.CapturePointer(pointerEvent.pointerId);
                lane.RegisterCallback<PointerMoveEvent>(OnDragMove);
                lane.RegisterCallback<PointerUpEvent>(OnDragEnd);
            }
            RepaintLanes();
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
            float pointerTime = geometry.XToTime(moveEvent.localPosition.x);
            float delta = pointerTime - dragStartTime;
            if (Mathf.Abs(delta) < 1e-5f)
            {
                return;
            }

            // The whole selection moves by the anchor's delta, so relative spacing survives a
            // multi-key drag. Moving each key to the pointer instead would collapse them together.
            foreach (KeyAddress address in selectedKeys)
            {
                SetKeyTime(address, Mathf.Clamp01(GetKeyTime(address) + delta));
            }
            dragStartTime = pointerTime;

            EditorUtility.SetDirty(selectedClip);
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

            if (isDraggingKeys)
            {
                isDraggingKeys = false;

                // Collapsing on release is what turns a drag of dozens of move events into a single
                // Ctrl+Z rather than dozens of them.
                Undo.CollapseUndoOperations(dragUndoGroup);

                if (selectedClip != null)
                {
                    SortTrackKeys(dragAnchor);
                    RebuildTimeline();
                }
            }
        }

        private void RepaintLanes()
        {
            for (int childIndex = 0; childIndex < laneColumn.childCount; childIndex++)
            {
                laneColumn[childIndex].MarkDirtyRepaint();
            }
        }

        // -------------------------------------------------------------------------------------
        // Key access. The lists hold structs, so a read-modify-write is required per edit.
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
        /// Restores ascending key order after a drag.
        /// </summary>
        /// <remarks>
        /// Validation rule V03 requires strictly ascending times, and the sampler's segment search
        /// assumes it — an out-of-order key does not throw, it silently makes a segment
        /// unreachable. Sorting on release rather than per move keeps indices stable for the
        /// duration of the gesture, which is what lets the selection survive the drag.
        /// </remarks>
        private void SortTrackKeys(KeyAddress address)
        {
            switch (address.trackKind)
            {
                case TimelineTrackKind.Transform:
                    selectedClip.transformTracks[address.trackIndex].keys.Sort(
                        (first, second) => first.normalizedTime.CompareTo(second.normalizedTime));
                    break;
                case TimelineTrackKind.Sprite:
                    selectedClip.spriteTracks[address.trackIndex].keys.Sort(
                        (first, second) => first.normalizedTime.CompareTo(second.normalizedTime));
                    break;
                default:
                    selectedClip.events.Sort(
                        (first, second) => first.normalizedTime.CompareTo(second.normalizedTime));
                    break;
            }

            // Indices moved, so held addresses no longer mean what they meant.
            selectedKeys.Clear();
        }
    }
}
