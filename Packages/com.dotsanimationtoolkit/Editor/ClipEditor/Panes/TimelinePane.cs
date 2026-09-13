// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>The Timeline pane: the ruler, the playhead, the track lanes and their headers, key drags, box select, the keyboard map, events and sorting; the view (zoom and pan) lives in its View partial, and every clip write goes back through the window's undo and commit delegates.</summary>
    public sealed partial class TimelinePane : VisualElement, System.IDisposable
    {
        /// <summary>
        /// Where a dragged track-name column width is remembered. Alongside the split positions and
        /// keyed the same way, for the same reason: it is a habit of the person, not of the project.
        /// </summary>
        private const string TrackHeaderWidthPrefsKey =
            "DotsAnimationToolkit.ClipEditor.TrackHeaderWidth";

        /// <summary>Narrow enough to be a deliberate choice, wide enough to still name a row.</summary>
        private const float MinimumTrackHeaderWidth = 90f;

        // The ceiling on the name column, and the width the lanes are never dragged below. The
        // second is the one that matters: without a floor for the lanes, a narrow window would
        // leave the keys with no room at all.
        private const float MaximumTrackHeaderWidth = 480f;
        private const float MinimumLaneWidth = 160f;

        private const string TrackHeaderUssClassName = "clip-editor__track-header";
        private const string TrackHeaderLabelUssClassName = "clip-editor__track-header-label";
        private const string TrackHeaderPartUssClassName = "clip-editor__track-header-part";
        private const string TrackHeaderPartGroupUssClassName = "clip-editor__track-header-part-group";
        private const string TrackHeaderBindingUssClassName = "clip-editor__track-header-binding";
        private const string TrackHeaderArrowUssClassName = "clip-editor__track-header-arrow";

        /// <summary>
        /// The pair that keeps a two-line header and its lane the same height. Never applied one
        /// without the other — see <see cref="SyncTrackHeaderWrap"/>.
        /// </summary>
        private const string TrackHeaderWrappedUssClassName = "clip-editor__track-header--wrapped";
        private const string LaneWrappedUssClassName = "clip-editor__lane--wrapped";
        private const string TrackFoldoutUssClassName = "clip-editor__track-foldout";
        private const string ChannelHeaderUssClassName = "clip-editor__channel-header";

        // Box selection. Armed on a press in empty lane space and only becomes a band once the
        // pointer has travelled, so a plain click still just moves the playhead.
        private const float BoxSelectStartToleranceSquared = 16f;
        private BoxSelectElement boxSelectElement;
        private VisualElement boxSelectLane;
        private Vector2 boxSelectOriginInStack;
        private bool isBoxSelectArmed;
        private bool isBoxSelectActive;
        private bool isBoxSelectAdditive;

        /// <summary>Which tracks show their per-channel rows, keyed by kind and index.</summary>
        private readonly HashSet<long> expandedTrackKeys = new HashSet<long>();

        private VisualElement trackHeaderColumn;
        private VisualElement laneColumn;
        private VisualElement laneStack;
        private GhostLaneStripElement ghostLanes;

        // The name column, its drag strip, and the width the user last asked that column to be.
        // Kept unclamped by the window's own size, so narrowing and widening the window again
        // returns the column to where it was left. Zero means nobody has ever dragged it.
        private VisualElement trackHeaderStack;
        private VisualElement trackHeaderResizer;
        private float requestedTrackHeaderWidth;
        private float appliedTrackHeaderWidth;
        private float trackHeaderDragStartWidth;
        private float trackHeaderDragStartPointerX;

        // Rows the last rebuild put in the lane column (tracks + channel rows), so the ghost rows
        // below can carry on the stripe alternation without re-deriving it from childCount.
        private int timelineRowCount;
        private TimeRulerElement ruler;
        private PlayheadElement playhead;
        private Label statusLabel;

        /// <summary>Rebuilt per paste, which is once per keystroke and not per frame.</summary>
        private readonly List<ClipObjectRef> pasteDestinations = new List<ClipObjectRef>();

        // Reused per timeline rebuild for the same reason: the Events lane is rebuilt whenever any
        // track is, which is on every structural edit.
        private readonly List<float> eventWindowLengths = new List<float>();

        private bool isDraggingKeys;
        private float dragPreviousTime;
        private TimelineTrackKind dragTrackKind;
        private int dragTrackIndex;

        private ActiveAssetSelection selection;
        private ClipEditorSession session;
        private ClipPreviewController previewController;

        // The window's root, for popups that must overlay the whole window.
        internal VisualElement WindowRoot { get; set; }

        // The keyboard map drives playback through the window, which is the transport target.
        internal ITransportTarget TransportTarget { get; set; }

        // Window facts read as if they were still local, so the moved bodies read unchanged.
        internal System.Func<int> SnapFrameCountProvider { get; set; }
        internal System.Func<int> TransportFrameCountProvider { get; set; }
        internal System.Func<int> LargeStepFramesProvider { get; set; }
        internal System.Func<bool> IsTransformActiveProvider { get; set; }

        private int SnapFrameCount { get { return SnapFrameCountProvider != null ? SnapFrameCountProvider() : 0; } }
        private int TransportFrameCount { get { return TransportFrameCountProvider != null ? TransportFrameCountProvider() : 30; } }
        private int LargeStepFrames { get { return LargeStepFramesProvider != null ? LargeStepFramesProvider() : 1; } }
        private bool IsTransformActive { get { return IsTransformActiveProvider != null && IsTransformActiveProvider(); } }

        // Window operations the timeline calls, handed in once at bind time and named after the
        // members they stand in for.
        internal System.Action<float> SetPlayheadTime { get; set; }
        internal System.Action<string> RecordClipEdit { get; set; }
        internal System.Action CommitClipEdit { get; set; }
        internal System.Action MarkPreviewDirty { get; set; }
        internal System.Action<string> BeginUndoGesture { get; set; }
        internal System.Action RecordUndoGestureStep { get; set; }
        internal System.Action EndUndoGesture { get; set; }
        internal System.Action<string> EnsureClipTrackTagsAssigned { get; set; }
        internal System.Action<RigAsset, string> RecordSocketEdit { get; set; }
        internal System.Action<bool> CommitSocketEdit { get; set; }
        internal System.Action<GUIContent> ShowNotification { get; set; }
        internal System.Func<HierarchyItem, ClipObjectRef> BuildObjectRef { get; set; }
        internal System.Func<uint, uint, ClipEditorWindow.TrackBindingLabel> DescribeTrackBinding { get; set; }
        internal System.Action<TimelineTrackKind, int, VisualElement> OpenTimelineTrackTagPicker { get; set; }
        internal System.Action<TimelineTrackKind, int, VisualElement> OpenTimelinePartPicker { get; set; }
        internal System.Func<uint, bool> IsTargetSelected { get; set; }
        internal System.Func<string, bool> IsBoneSelected { get; set; }
        internal System.Func<string> DescribeSelection { get; set; }
        internal System.Action RefreshHierarchyRows { get; set; }
        internal System.Action<int> SelectHierarchyItem { get; set; }
        internal System.Action<int> SelectItemByIdWithoutNotify { get; set; }
        internal System.Action ClearTreeSelectionWithoutNotify { get; set; }
        internal System.Action RebuildHierarchy { get; set; }
        internal System.Action RebuildInspector { get; set; }
        internal System.Func<int, KeyAddress> ResolveEventKeyAddressForFlatIndex { get; set; }

        // Elements and view state the window's transport and key-transform partials still read.
        internal TimeRulerElement Ruler { get { return ruler; } }
        internal PlayheadElement Playhead { get { return playhead; } }
        internal VisualElement LaneColumn { get { return laneColumn; } }
        internal VisualElement LaneStack { get { return laneStack; } }
        internal Label StatusLabel { get { return statusLabel; } }
        internal float ViewZoom { get { return viewZoom; } }
        internal float ViewPan { get { return viewPan; } }
        internal int[] LastSortIndexMap { get { return lastSortIndexMap; } }

        public void Bind(
            VisualElement paneRoot,
            ActiveAssetSelection sharedSelection,
            ClipEditorSession editorSession,
            ClipPreviewController preview)
        {
            selection = sharedSelection;
            session = editorSession;
            previewController = preview;
            if (paneRoot != null)
            {
                BindTimeline(paneRoot);
                BindTimelineView(paneRoot);
            }
        }

        public void Dispose()
        {
            if (dragAutoScroll != null)
            {
                dragAutoScroll.Pause();
                dragAutoScroll = null;
            }
        }

        private RigAsset ActiveRig
        {
            get { return selection != null ? selection.Rig : null; }
        }

        private void BindTimeline(VisualElement paneRoot)
        {
            statusLabel = paneRoot.Q<Label>("timeline-status");
            trackHeaderColumn = paneRoot.Q<VisualElement>("track-header-column");
            laneColumn = paneRoot.Q<VisualElement>("lane-column");
            BindTrackHeaderResizer(paneRoot);

            // The lane stack owns keyboard focus: shortcuts registered here cannot swallow
            // keystrokes meant for the inspector's own text fields.
            laneStack = paneRoot.Q<VisualElement>("lane-stack");
            if (laneStack == null)
            {
                return;
            }
            laneStack.RegisterCallback<KeyDownEvent>(OnTimelineKeyDown);

            // Ruler above the lanes, playhead over both. Inserted rather than declared in UXML
            // because neither has a UXML factory, and giving them one would buy nothing: this window
            // is the only thing in the package that instantiates them.
            ruler = new TimeRulerElement();
            ruler.scrubbed += SetPlayheadTime;
            laneStack.Insert(0, ruler);

            // Straight after the lane column, so the empty rows begin where the tracks stop. Added
            // before the two overlays below: they are drawn in tree order, and a band or a playhead
            // painted under the ghost rows would vanish the moment it left the last track.
            ghostLanes = new GhostLaneStripElement();
            ghostLanes.ghostPointerDown += OnGhostLanePointerDown;
            laneStack.Add(ghostLanes);

            // Under the playhead so the current-time line stays readable over a band.
            boxSelectElement = new BoxSelectElement();
            laneStack.Add(boxSelectElement);

            playhead = new PlayheadElement();
            laneStack.Add(playhead);
        }

        // A drag strip, not a TwoPaneSplitView: this row lives inside the timeline's scroll view,
        // whose height is whatever the tracks add up to, so there is nothing definite for a split
        // to divide.
        /// <summary>Makes the track-name column draggable, and remembers where it was left.</summary>
        private void BindTrackHeaderResizer(VisualElement paneRoot)
        {
            trackHeaderStack = paneRoot.Q<VisualElement>("track-header-stack");
            trackHeaderResizer = paneRoot.Q<VisualElement>("track-header-resizer");
            if (trackHeaderStack == null || trackHeaderResizer == null)
            {
                return;
            }

            trackHeaderResizer.tooltip =
                "Drag to widen the track name column. A name too wide for the column wraps onto a "
                + "second line rather than being cut off.";

            if (EditorPrefs.HasKey(TrackHeaderWidthPrefsKey))
            {
                SetTrackHeaderWidth(
                    EditorPrefs.GetFloat(TrackHeaderWidthPrefsKey, MinimumTrackHeaderWidth));
            }

            trackHeaderResizer.RegisterCallback<PointerDownEvent>(pointerEvent =>
            {
                if (pointerEvent.button != 0)
                {
                    return;
                }
                trackHeaderDragStartWidth = trackHeaderStack.resolvedStyle.width;
                if (float.IsNaN(trackHeaderDragStartWidth))
                {
                    return;
                }

                // Panel coordinates, not the strip's own: the strip travels with the column it is
                // resizing, so a local x would be measured against a moving origin and the drag
                // would fight itself.
                trackHeaderDragStartPointerX = pointerEvent.position.x;
                trackHeaderResizer.CapturePointer(pointerEvent.pointerId);
                pointerEvent.StopPropagation();
            });

            trackHeaderResizer.RegisterCallback<PointerMoveEvent>(pointerEvent =>
            {
                if (!trackHeaderResizer.HasPointerCapture(pointerEvent.pointerId))
                {
                    return;
                }
                SetTrackHeaderWidth(
                    trackHeaderDragStartWidth
                        + (pointerEvent.position.x - trackHeaderDragStartPointerX));
                pointerEvent.StopPropagation();
            });

            trackHeaderResizer.RegisterCallback<PointerUpEvent>(pointerEvent =>
            {
                if (!trackHeaderResizer.HasPointerCapture(pointerEvent.pointerId))
                {
                    return;
                }
                trackHeaderResizer.ReleasePointer(pointerEvent.pointerId);
                EditorPrefs.SetFloat(TrackHeaderWidthPrefsKey, requestedTrackHeaderWidth);
                pointerEvent.StopPropagation();
            });

            // A narrowed window has to take width back off the column, or the lanes are squeezed to
            // nothing by a column that does not shrink. Widening hands it back, which is why the
            // requested width is kept rather than being overwritten by each clamp.
            VisualElement timelineRow = paneRoot.Q<VisualElement>("timeline-row");
            if (timelineRow != null)
            {
                timelineRow.RegisterCallback<GeometryChangedEvent>(
                    geometryEvent => ApplyTrackHeaderWidth());
            }
        }

        /// <summary>Records the width the user is asking for, then fits it to the room available.</summary>
        private void SetTrackHeaderWidth(float requestedWidth)
        {
            requestedTrackHeaderWidth = Mathf.Clamp(
                requestedWidth, MinimumTrackHeaderWidth, MaximumTrackHeaderWidth);
            ApplyTrackHeaderWidth();
        }

        private void ApplyTrackHeaderWidth()
        {
            if (trackHeaderStack == null || requestedTrackHeaderWidth <= 0f)
            {
                return;
            }

            float fittedWidth = requestedTrackHeaderWidth;
            VisualElement timelineRow = trackHeaderStack.parent;
            if (timelineRow != null && timelineRow.contentRect.width > 1f)
            {
                fittedWidth = Mathf.Min(
                    fittedWidth,
                    Mathf.Max(
                        MinimumTrackHeaderWidth,
                        timelineRow.contentRect.width - MinimumLaneWidth));
            }

            // Guarded because this also runs from a geometry callback, and writing an unchanged
            // width would dirty the layout that called it.
            if (Mathf.Abs(appliedTrackHeaderWidth - fittedWidth) < 0.5f)
            {
                return;
            }
            appliedTrackHeaderWidth = fittedWidth;
            trackHeaderStack.style.width = fittedWidth;
        }

        // -------------------------------------------------------------------------------------
        // Timeline construction
        // -------------------------------------------------------------------------------------

        internal void RebuildTimeline()
        {
            if (trackHeaderColumn == null || laneColumn == null)
            {
                return;
            }

            trackHeaderColumn.Clear();
            laneColumn.Clear();

            // The hierarchy's bold marks track which clip is selected, so it is refreshed with the
            // timeline rather than only when the rig changes.
            RefreshHierarchyRows();

            if (session.SelectedClip == null)
            {
                statusLabel.text = selection.ClipSet == null ? "Assign a clip set." : "Select a clip.";
                timelineRowCount = 0;
                SyncGhostLanes();
                RebuildInspector();
                return;
            }

            // Focus mode: with a selection, the timeline shows only that selection's tracks. It is
            // what makes a busy clip readable — but a row that has silently vanished is worse than a
            // busy timeline, so the status line always says what is being hidden and how to undo it.
            bool isFocused = session.SelectedHierarchyItems.Count > 0;
            int hiddenTrackCount = 0;

            // A track with no keys writes nothing at any time, so it is not a curve yet — it is a
            // component waiting for its first key, and the place to make that key is the part's own
            // Key button in the inspector. Counted, because "I added a Transform and no row
            // appeared" needs an answer on screen rather than in the source.
            int keylessTrackCount = 0;

            statusLabel.text = session.SelectedClip.name
                + "   duration " + session.SelectedClip.duration.ToString("0.###") + "s"
                + "   loop " + session.SelectedClip.defaultLoop.ToString()
                + "   selected " + session.SelectedKeys.Count.ToString();

            ruler.durationSeconds = session.SelectedClip.duration;
            ruler.frameCount = TransportFrameCount;
            ruler.RefreshSecondLabels();
            ruler.MarkDirtyRepaint();

            int rowIndex = 0;
            List<float> times = new List<float>();

            List<TransformTrack> transformTracks = session.SelectedClip.transformTracks;
            for (int trackIndex = 0; transformTracks != null && trackIndex < transformTracks.Count; trackIndex++)
            {
                TransformTrack track = transformTracks[trackIndex];
                if (track == null)
                {
                    continue;
                }
                if (track.keys == null || track.keys.Count == 0)
                {
                    keylessTrackCount++;
                    continue;
                }
                ClipEditorWindow.TrackBindingLabel binding = DescribeTrackBinding(track.targetId, track.tagId);
                if (isFocused && !IsTargetSelected(binding.resolvedTargetId))
                {
                    hiddenTrackCount++;
                    continue;
                }
                times.Clear();
                for (int keyIndex = 0; keyIndex < track.keys.Count; keyIndex++)
                {
                    times.Add(track.keys[keyIndex].normalizedTime);
                }
                AddTrackRow(
                    binding.tagText, binding.partText, "Transform · " + track.channels.ToString(),
                    TimelineTrackKind.Transform, trackIndex, times, true, ref rowIndex);
            }

            List<SpriteTrack> spriteTracks = session.SelectedClip.spriteTracks;
            for (int trackIndex = 0; spriteTracks != null && trackIndex < spriteTracks.Count; trackIndex++)
            {
                SpriteTrack track = spriteTracks[trackIndex];
                if (track == null)
                {
                    continue;
                }
                if (track.keys == null || track.keys.Count == 0)
                {
                    keylessTrackCount++;
                    continue;
                }
                ClipEditorWindow.TrackBindingLabel binding = DescribeTrackBinding(track.targetId, track.tagId);
                if (isFocused && !IsTargetSelected(binding.resolvedTargetId))
                {
                    hiddenTrackCount++;
                    continue;
                }
                times.Clear();
                for (int keyIndex = 0; keyIndex < track.keys.Count; keyIndex++)
                {
                    times.Add(track.keys[keyIndex].normalizedTime);
                }
                AddTrackRow(
                    binding.tagText, binding.partText, "Flipbook · " + track.mode.ToString(),
                    TimelineTrackKind.Sprite, trackIndex, times, true, ref rowIndex);
            }

            // Bone rows sit between the part rows and the events, so a character's skeleton and its
            // cutout parts read as one stack.
            List<BoneTrack> boneTracks = session.SelectedClip.boneTracks;
            for (int trackIndex = 0; boneTracks != null && trackIndex < boneTracks.Count; trackIndex++)
            {
                BoneTrack track = boneTracks[trackIndex];
                if (track == null)
                {
                    continue;
                }
                if (track.keys == null || track.keys.Count == 0)
                {
                    keylessTrackCount++;
                    continue;
                }
                if (isFocused && !IsBoneSelected(track.boneName))
                {
                    hiddenTrackCount++;
                    continue;
                }
                times.Clear();
                for (int keyIndex = 0; keyIndex < track.keys.Count; keyIndex++)
                {
                    times.Add(track.keys[keyIndex].normalizedTime);
                }

                // A bone binds by name, not by tag — there is no tag half to show, so the row keeps
                // the single-label shape the other kinds have grown out of.
                AddTrackRow(
                    string.IsNullOrEmpty(track.boneName) ? "<unnamed bone>" : track.boneName,
                    null, "Bone", TimelineTrackKind.Bone, trackIndex, times, false, ref rowIndex);
            }

            if (session.SelectedClip.events != null && session.SelectedClip.events.Count > 0)
            {
                // One lane per event name (E6 Task 2), not one shared lane with stacking: three
                // events on one frame land on three rows rather than piling under one. Events stay
                // visible while focused — they belong to the clip rather than to any one part, so
                // hiding them would make event authoring impossible the moment anything was selected.
                AnimEventKeyRegistry eventRegistry = ClipInspectorPane.ResolveEventKeyRegistry();
                List<uint> eventLaneKeys = EventLaneAddressing.ComputeLaneKeys(session.SelectedClip.events);
                for (int laneIndex = 0; laneIndex < eventLaneKeys.Count; laneIndex++)
                {
                    List<int> laneFlatIndices = EventLaneAddressing.ResolveLaneFlatIndices(
                        session.SelectedClip.events, laneIndex);
                    times.Clear();
                    for (int position = 0; position < laneFlatIndices.Count; position++)
                    {
                        times.Add(session.SelectedClip.events[laneFlatIndices[position]].normalizedTime);
                    }
                    AddTrackRow(
                        ClipInspectorPane.DescribeEventName(eventLaneKeys[laneIndex], eventRegistry),
                        null, null, TimelineTrackKind.Event, laneIndex, times, false, ref rowIndex,
                        laneAccent: ToolkitPalette.ColorForEventKey(eventLaneKeys[laneIndex]));
                }
            }

            if (isFocused)
            {
                statusLabel.text += "   ·   focused on " + DescribeSelection()
                    + (hiddenTrackCount > 0
                        ? " (" + hiddenTrackCount.ToString() + " track(s) hidden — deselect to show all)"
                        : string.Empty);
            }

            if (keylessTrackCount > 0)
            {
                statusLabel.text += "   ·   " + keylessTrackCount.ToString()
                    + " track(s) with no keys — select the part and press Key to start one";
            }

            timelineRowCount = rowIndex;
            SyncGhostLanes();

            SetPlayheadTime(session.PlayheadNormalized);
            RebuildInspector();
        }

        // Channel rows show the same keys as their track, not keys of their own: one TransformKey
        // carries position, rotation and scale together, so dragging a key on any channel retimes
        // the one underlying key.
        /// <summary>Adds a track's row and, when it is expanded, one row per animated channel.</summary>
        private void AddTrackRow(
            string headerText, string partText, string detailText, TimelineTrackKind trackKind,
            int trackIndex, List<float> times, bool hasBindingControls, ref int rowIndex,
            Color? laneAccent = null)
        {
            long trackKey = MakeTrackKey(trackKind, trackIndex);
            string[] channelNames = GetChannelNames(trackKind);
            bool canExpand = channelNames.Length > 0;
            bool isExpanded = canExpand && expandedTrackKeys.Contains(trackKey);

            VisualElement headerRow = new VisualElement();
            headerRow.AddToClassList(TrackHeaderUssClassName);

            if (canExpand)
            {
                Button foldoutButton = new Button(() => ToggleTrackExpanded(trackKey))
                {
                    text = isExpanded ? "▾" : "▸"
                };
                foldoutButton.AddToClassList(TrackFoldoutUssClassName);
                headerRow.Add(foldoutButton);
            }

            string rowTooltip = headerText
                + (string.IsNullOrEmpty(partText) ? string.Empty : "   →   " + partText)
                + (string.IsNullOrEmpty(detailText) ? string.Empty : "\n" + detailText)
                + (hasBindingControls
                    ? "\nClick the tag to move this row's keys to another tag; click the part to "
                      + "choose which rig part wears the tag.\nClick the row background to select "
                      + "every key on this track; shift-click adds them to the selection."
                    : "\nClick to select every key on this track; "
                      + "shift-click to add them to the selection.")
                + (trackKind == TimelineTrackKind.Event ? "\nRight-click for lane actions." : string.Empty);

            Label headerLabel = new Label(headerText);
            headerLabel.AddToClassList(TrackHeaderLabelUssClassName);
            headerLabel.tooltip = rowTooltip;
            headerRow.Add(headerLabel);

            VisualElement partGroup = null;
            Label partLabel = null;
            if (!string.IsNullOrEmpty(partText))
            {
                // Grouped so a column too narrow for both halves moves the arrow and the part down
                // together and the second line reads "→ Part". Ignored by picking so a press on the
                // gap between the two halves still reaches the row background, which is what selects
                // the track's keys.
                partGroup = new VisualElement();
                partGroup.AddToClassList(TrackHeaderPartGroupUssClassName);
                partGroup.pickingMode = PickingMode.Ignore;

                if (hasBindingControls)
                {
                    // The arrow is the row's grammar made visible: keys belong to the tag, the tag
                    // lands on the part. It is not a click target.
                    Label arrowLabel = new Label("→");
                    arrowLabel.AddToClassList(TrackHeaderArrowUssClassName);
                    partGroup.Add(arrowLabel);
                }
                partLabel = new Label(partText);
                partLabel.AddToClassList(TrackHeaderPartUssClassName);
                partLabel.tooltip = rowTooltip;
                partGroup.Add(partLabel);
                headerRow.Add(partGroup);
            }

            TimelineTrackKind headerTrackKind = trackKind;
            int headerTrackIndex = trackIndex;
            EventCallback<PointerDownEvent> selectTrackKeys = pointerEvent =>
            {
                bool additive = pointerEvent.shiftKey
                    || pointerEvent.ctrlKey || pointerEvent.commandKey;
                SelectAllKeysOnTrack(headerTrackKind, headerTrackIndex, additive);
                pointerEvent.StopPropagation();
            };

            if (hasBindingControls)
            {
                headerLabel.AddToClassList(TrackHeaderBindingUssClassName);
                headerLabel.RegisterCallback<PointerDownEvent>(pointerEvent =>
                {
                    pointerEvent.StopPropagation();
                    OpenTimelineTrackTagPicker(headerTrackKind, headerTrackIndex, headerLabel);
                });
                if (partLabel != null)
                {
                    partLabel.AddToClassList(TrackHeaderBindingUssClassName);
                    partLabel.RegisterCallback<PointerDownEvent>(pointerEvent =>
                    {
                        pointerEvent.StopPropagation();
                        OpenTimelinePartPicker(headerTrackKind, headerTrackIndex, partLabel);
                    });
                }

                // Only a press on the row's own background selects — a press on either picker half
                // stopped propagating above, and the foldout button owns its own click.
                headerRow.RegisterCallback<PointerDownEvent>(pointerEvent =>
                {
                    VisualElement pressed = pointerEvent.target as VisualElement;
                    if (pressed == headerRow)
                    {
                        selectTrackKeys(pointerEvent);
                    }
                });
            }
            else
            {
                headerLabel.RegisterCallback(selectTrackKeys);
                if (partLabel != null)
                {
                    // The part name is half the same row, so clicking it selects the same keys. A
                    // dead strip beside a live one reads as the row having stopped working.
                    partLabel.RegisterCallback(selectTrackKeys);
                }
            }

            // An event lane's header is its own authoring surface, not just a label: the other track
            // kinds have no equivalent menu because none names a project-wide vocabulary entry.
            if (trackKind == TimelineTrackKind.Event)
            {
                headerLabel.AddManipulator(new ContextualMenuManipulator(
                    menuEvent => BuildEventLaneContextMenu(menuEvent, headerTrackIndex, headerLabel)));
            }

            trackHeaderColumn.Add(headerRow);

            TrackLaneElement lane = AddLane(trackKind, trackIndex, times, rowIndex, false);
            if (laneAccent.HasValue)
            {
                // The accent is data-driven per lane, so it is set here rather than through a USS class.
                lane.eventColor = laneAccent.Value;
                headerLabel.style.borderLeftWidth = 3f;
                headerLabel.style.borderLeftColor = laneAccent.Value;
            }
            BindTrackHeaderWrap(headerRow, partGroup, lane);
            rowIndex++;

            if (!isExpanded)
            {
                return;
            }

            for (int channelIndex = 0; channelIndex < channelNames.Length; channelIndex++)
            {
                Label channelHeader = new Label(channelNames[channelIndex]);
                channelHeader.AddToClassList(TrackHeaderUssClassName);
                channelHeader.AddToClassList(ChannelHeaderUssClassName);
                trackHeaderColumn.Add(channelHeader);

                AddLane(trackKind, trackIndex, times, rowIndex, true);
                rowIndex++;
            }
        }

        // Normalized here, not in the lane: the lane draws in normalized time and has no idea what
        // the clip's duration is, and the window is authored in seconds.
        /// <summary>One event lane's window lengths as a fraction of the clip, parallel to its filtered key times.</summary>
        private List<float> CollectEventWindowLengths(int laneIndex)
        {
            eventWindowLengths.Clear();
            if (session.SelectedClip == null || session.SelectedClip.events == null)
            {
                return eventWindowLengths;
            }

            List<int> laneFlatIndices =
                EventLaneAddressing.ResolveLaneFlatIndices(session.SelectedClip.events, laneIndex);
            float duration = Mathf.Max(session.SelectedClip.duration, ClipAsset.MinimumDuration);
            for (int position = 0; position < laneFlatIndices.Count; position++)
            {
                eventWindowLengths.Add(
                    session.SelectedClip.events[laneFlatIndices[position]].windowSeconds / duration);
            }
            return eventWindowLengths;
        }

        /// <summary>Adds one lane, and hands it back so the header beside it can be paired with it.</summary>
        private TrackLaneElement AddLane(
            TimelineTrackKind trackKind, int trackIndex, List<float> times, int rowIndex,
            bool isChannelRow)
        {
            TrackLaneElement lane = new TrackLaneElement
            {
                trackKind = trackKind,
                trackIndex = trackIndex,
                isAlternateRow = (rowIndex & 1) == 1,
                isChannelRow = isChannelRow,
                isKeySelected = session.SelectedKeys.Contains,

                // Born with the current view. A lane created without it renders unzoomed under a
                // ruler that is not, until something happens to push the view down again.
                viewLaneWidth = LaneWidth,
                viewZoom = viewZoom,
                viewPan = viewPan
            };
            lane.SetKeyTimes(times);
            if (trackKind == TimelineTrackKind.Event)
            {
                lane.SetKeyWindows(CollectEventWindowLengths(trackIndex));
            }
            lane.keyPointerDown += OnKeyPointerDown;
            lane.lanePointerDown += OnLanePointerDown;
            laneColumn.Add(lane);
            return lane;
        }

        // Watched on the part group, not the row: the row's height is this callback's own output,
        // and watching it would be watching itself. The group's y is zero beside the tag, a lane's
        // height once it has wrapped below it.
        /// <summary>Keeps a header that has wrapped onto a second line exactly as tall as its own lane.</summary>
        private static void BindTrackHeaderWrap(
            VisualElement headerRow, VisualElement partGroup, TrackLaneElement lane)
        {
            if (headerRow == null || partGroup == null || lane == null)
            {
                return;
            }

            partGroup.RegisterCallback<GeometryChangedEvent>(geometryEvent =>
            {
                bool isWrapped = partGroup.layout.y > 1f;
                headerRow.EnableInClassList(TrackHeaderWrappedUssClassName, isWrapped);
                lane.EnableInClassList(LaneWrappedUssClassName, isWrapped);
            });
        }

        private static long MakeTrackKey(TimelineTrackKind trackKind, int trackIndex)
        {
            return ((long)trackKind << 32) | (uint)trackIndex;
        }

        private void ToggleTrackExpanded(long trackKey)
        {
            if (!expandedTrackKeys.Remove(trackKey))
            {
                expandedTrackKeys.Add(trackKey);
            }
            RebuildTimeline();
        }

        /// <summary>The channels a track kind animates, or an empty array when it has none to show.</summary>
        private static string[] GetChannelNames(TimelineTrackKind trackKind)
        {
            switch (trackKind)
            {
                case TimelineTrackKind.Transform:
                    return new string[]
                    {
                        "Position X", "Position Y", "Position Z", "Rotation Z", "Scale X", "Scale Y"
                    };
                case TimelineTrackKind.Bone:
                    return new string[] { "Position", "Rotation", "Scale" };
                case TimelineTrackKind.Sprite:
                    return new string[] { "Index" };
                default:
                    return new string[0];
            }
        }

        internal void RepaintLanes()
        {
            for (int childIndex = 0; childIndex < laneColumn.childCount; childIndex++)
            {
                laneColumn[childIndex].MarkDirtyRepaint();
            }
        }

        // Use this, not RepaintLanes, whenever key times have changed: a lane holds the times it
        // was built with, so a plain repaint redraws stale positions during a drag. Rebuilding the
        // timeline instead would destroy the element holding the pointer capture mid-gesture.
        /// <summary>Re-reads every lane key time from the clip, then repaints.</summary>
        internal void RefreshLaneKeys()
        {
            if (laneColumn == null || session.SelectedClip == null)
            {
                RepaintLanes();
                return;
            }

            List<float> times = new List<float>();
            for (int childIndex = 0; childIndex < laneColumn.childCount; childIndex++)
            {
                TrackLaneElement lane = laneColumn[childIndex] as TrackLaneElement;
                if (lane == null)
                {
                    laneColumn[childIndex].MarkDirtyRepaint();
                    continue;
                }

                times.Clear();
                int keyCount = CountKeysOnTrack(lane.trackKind, lane.trackIndex);
                for (int keyIndex = 0; keyIndex < keyCount; keyIndex++)
                {
                    times.Add(GetKeyTime(new KeyAddress(lane.trackKind, lane.trackIndex, keyIndex)));
                }
                lane.SetKeyTimes(times);
                lane.MarkDirtyRepaint();
            }
        }

        // -------------------------------------------------------------------------------------
        // Gestures. One undo step per gesture.
        // -------------------------------------------------------------------------------------

        private void OnKeyPointerDown(KeyAddress address, PointerDownEvent pointerEvent)
        {
            laneStack.Focus();

            bool additive = pointerEvent.shiftKey || pointerEvent.ctrlKey || pointerEvent.commandKey;
            if (!additive && !session.SelectedKeys.Contains(address))
            {
                session.SelectedKeys.Clear();
                session.HasActiveKey = false;
            }
            if (additive && session.SelectedKeys.Contains(address))
            {
                session.SelectedKeys.Remove(address);
                // Deselecting the active key hands the panel back to whatever remains, rather than
                // leaving it editing a key that is no longer selected.
                session.HasActiveKey = false;
            }
            else
            {
                session.SelectedKeys.Add(address);
                session.ActiveKey = address;
                session.HasActiveKey = true;
            }

            SyncBoneSelectionToKey(address);

            if (session.SelectedClip == null)
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
            dragTrackKind = address.trackKind;
            dragTrackIndex = address.trackIndex;

            VisualElement lane = pointerEvent.currentTarget as VisualElement;
            if (lane != null)
            {
                lane.CapturePointer(pointerEvent.pointerId);
                lane.RegisterCallback<PointerMoveEvent>(OnDragMove);
                lane.RegisterCallback<PointerUpEvent>(OnDragEnd);

                dragPointerLaneX = pointerEvent.localPosition.x;
            }

            // Measured from where the pointer is, not from where the key is. Seeding with the key
            // time meant grabbing a key slightly off its centre jumped it by that offset on the
            // first move; from here the key follows the cursor exactly.
            dragPreviousTime = TimelineGeometry.Snap(
                TimelineGeometry.Create(LaneWidth, viewZoom, viewPan).XToTime(dragPointerLaneX),
                SnapFrameCount);

            dragAutoScroll = laneStack.schedule
                .Execute(TickDragAutoScroll).Every(16);
            RepaintLanes();
            RebuildInspector();
        }

        // The tree's selection is set without notifying: the notification clears the key selection,
        // so the click would deselect the very key that caused it.
        /// <summary>Moves the viewport outline and the tree onto the bone whose key was grabbed.</summary>
        private void SyncBoneSelectionToKey(KeyAddress address)
        {
            string boneName = null;
            if (address.trackKind == TimelineTrackKind.Bone
                && session.SelectedClip != null
                && session.SelectedClip.boneTracks != null
                && address.trackIndex < session.SelectedClip.boneTracks.Count)
            {
                BoneTrack track = session.SelectedClip.boneTracks[address.trackIndex];
                boneName = track != null ? track.boneName : null;
            }

            int itemId = RigHierarchyPane.NothingSelectedItemId;
            if (!string.IsNullOrEmpty(boneName) && previewController != null)
            {
                int previewIndex = previewController.FindHierarchyIndexByName(boneName);
                if (previewIndex >= 0)
                {
                    itemId = previewIndex;
                }
            }
            SelectHierarchyItem(itemId);

            if (itemId != RigHierarchyPane.NothingSelectedItemId)
            {
                SelectItemByIdWithoutNotify(itemId);
            }
            else
            {
                ClearTreeSelectionWithoutNotify();
            }
        }

        private void OnDragMove(PointerMoveEvent moveEvent)
        {
            if (!isDraggingKeys || session.SelectedClip == null)
            {
                return;
            }

            TrackLaneElement lane = moveEvent.currentTarget as TrackLaneElement;
            if (lane == null)
            {
                return;
            }

            dragPointerLaneX = moveEvent.localPosition.x;
            UpdateKeyDrag();
        }

        /// <summary>Moves the selection to follow the pointer, using the view as it is right now.</summary>
        private void UpdateKeyDrag()
        {
            if (!isDraggingKeys || session.SelectedClip == null)
            {
                return;
            }

            // Read live, never cached: this is the same width the lanes and the ruler are drawn
            // with this frame, so the key lands under the cursor rather than near it.
            TimelineGeometry geometry = TimelineGeometry.Create(LaneWidth, viewZoom, viewPan);
            float pointerTime = TimelineGeometry.Snap(
                geometry.XToTime(dragPointerLaneX), SnapFrameCount);
            float delta = pointerTime - dragPreviousTime;
            if (Mathf.Abs(delta) < 1e-6f)
            {
                return;
            }

            RecordUndoGestureStep();

            // The whole selection moves by the grabbed key's delta, so relative spacing survives a
            // multi-key drag. Moving every key to the pointer instead would collapse them together.
            foreach (KeyAddress address in session.SelectedKeys)
            {
                SetKeyTime(address, GetKeyTime(address) + delta);
            }
            dragPreviousTime = pointerTime;

            EditorUtility.SetDirty(session.SelectedClip);
            SetPlayheadTime(pointerTime);
            MarkPreviewDirty();
            RefreshLaneKeys();
            ShowDragReadout(pointerTime);
        }

        /// <summary>Says which frame the drag is landing on, in the status line.</summary>
        private void ShowDragReadout(float normalizedTime)
        {
            if (statusLabel == null)
            {
                return;
            }
            int frameCount = Mathf.Max(1, TransportFrameCount);
            float frame = normalizedTime * frameCount;
            string range = normalizedTime < 0f || normalizedTime > 1f ? "   (outside clip)" : string.Empty;
            statusLabel.text = "Frame " + frame.ToString("0.##")
                + "   " + (normalizedTime * session.SelectedClip.duration).ToString("0.###") + "s"
                + "   " + session.SelectedKeys.Count.ToString() + " key(s)" + range;
        }

        // Driven by a scheduler, not pointer movement: the case that matters is the pointer held
        // still against the edge, which a movement-only trigger would not scroll for.
        /// <summary>Scrolls the view when a drag reaches the edge of the lane, so the gesture can continue.</summary>
        private void TickDragAutoScroll()
        {
            if (!isDraggingKeys)
            {
                return;
            }

            float laneWidth = LaneWidth;
            const float EdgeMarginPixels = 28f;
            const float MaximumScrollPixelsPerTick = 14f;

            float overshoot = 0f;
            if (dragPointerLaneX < EdgeMarginPixels)
            {
                overshoot = dragPointerLaneX - EdgeMarginPixels;
            }
            else if (dragPointerLaneX > laneWidth - EdgeMarginPixels)
            {
                overshoot = dragPointerLaneX - (laneWidth - EdgeMarginPixels);
            }
            if (Mathf.Abs(overshoot) < 0.5f)
            {
                return;
            }

            // Proportional to how far past the margin the pointer is, so easing into the edge
            // scrolls gently and shoving past it scrolls fast.
            float scrollPixels = Mathf.Clamp(
                overshoot, -MaximumScrollPixelsPerTick, MaximumScrollPixelsPerTick);
            TimelineGeometry geometry = TimelineGeometry.Create(laneWidth, viewZoom, viewPan);
            viewPan += scrollPixels / geometry.PixelsPerNormalizedUnit;
            ApplyTimelineView();

            // The view moved under a stationary pointer, so the time under the pointer changed and
            // the keys have to follow it. Without this the selection would sit still while the
            // world slid past.
            UpdateKeyDrag();
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

            if (dragAutoScroll != null)
            {
                dragAutoScroll.Pause();
                dragAutoScroll = null;
            }

            if (!isDraggingKeys)
            {
                return;
            }
            isDraggingKeys = false;

            EndUndoGesture();
            if (session.SelectedClip != null)
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

            if (pointerEvent.clickCount < 2 || session.SelectedClip == null)
            {
                bool additive = pointerEvent.shiftKey || pointerEvent.ctrlKey || pointerEvent.commandKey;
                if (!additive)
                {
                    session.SelectedKeys.Clear();
                    session.HasActiveKey = false;
                    RepaintLanes();
                    RebuildInspector();
                }

                // The same press can still become a box select, so the playhead is held rather than
                // moved. Moving it here dragged it along behind every band the user drew, which
                // reads as the two gestures fighting each other. It is applied on release, and only
                // if the press turned out to be a click.
                pendingPlayheadTime = normalizedTime;
                BeginBoxSelect(pointerEvent, additive);
                return;
            }

            float insertTime = TimelineGeometry.Snap(normalizedTime, SnapFrameCount);
            BeginUndoGesture("Add Animation Key");
            InsertKey(trackKind, trackIndex, insertTime);
            EndUndoGesture();

            EditorUtility.SetDirty(session.SelectedClip);
            session.SelectedKeys.Clear();
            session.HasActiveKey = false;
            SortTrackKeys(trackKind, trackIndex);
            SetPlayheadTime(insertTime);
            RebuildTimeline();
        }

        /// <summary>Handles a press on the empty rows below the last track: click-clears-and-scrubs, drag-selects.</summary>
        private void OnGhostLanePointerDown(float normalizedTime, PointerDownEvent pointerEvent)
        {
            laneStack.Focus();

            bool additive = pointerEvent.shiftKey || pointerEvent.ctrlKey || pointerEvent.commandKey;
            if (!additive)
            {
                session.SelectedKeys.Clear();
                session.HasActiveKey = false;
                RepaintLanes();
                RebuildInspector();
            }

            // Held rather than moved, for the reason spelled out in OnLanePointerDown: the playhead
            // dragging along behind every band reads as the two gestures fighting each other.
            pendingPlayheadTime = normalizedTime;
            BeginBoxSelect(pointerEvent, additive);
        }

        // -------------------------------------------------------------------------------------
        // Box selection
        // -------------------------------------------------------------------------------------

        /// <summary>
        /// Where a press on empty lane space would put the playhead, applied only if it stays a click.
        /// </summary>
        private float pendingPlayheadTime;

        private void BeginBoxSelect(PointerDownEvent pointerEvent, bool additive)
        {
            VisualElement lane = pointerEvent.currentTarget as VisualElement;
            if (lane == null || laneStack == null || boxSelectElement == null)
            {
                return;
            }

            boxSelectOriginInStack = lane.ChangeCoordinatesTo(laneStack, pointerEvent.localPosition);
            boxSelectLane = lane;
            isBoxSelectArmed = true;
            isBoxSelectActive = false;
            isBoxSelectAdditive = additive;

            lane.CapturePointer(pointerEvent.pointerId);
            lane.RegisterCallback<PointerMoveEvent>(OnBoxSelectMove);
            lane.RegisterCallback<PointerUpEvent>(OnBoxSelectEnd);
        }

        private void OnBoxSelectMove(PointerMoveEvent moveEvent)
        {
            if (!isBoxSelectArmed)
            {
                return;
            }

            VisualElement lane = moveEvent.currentTarget as VisualElement;
            if (lane == null)
            {
                return;
            }

            Vector2 currentInStack = lane.ChangeCoordinatesTo(laneStack, moveEvent.localPosition);
            Vector2 travel = currentInStack - boxSelectOriginInStack;
            if (!isBoxSelectActive && travel.sqrMagnitude < BoxSelectStartToleranceSquared)
            {
                return;
            }

            isBoxSelectActive = true;
            boxSelectElement.SetBand(Rect.MinMaxRect(
                Mathf.Min(boxSelectOriginInStack.x, currentInStack.x),
                Mathf.Min(boxSelectOriginInStack.y, currentInStack.y),
                Mathf.Max(boxSelectOriginInStack.x, currentInStack.x),
                Mathf.Max(boxSelectOriginInStack.y, currentInStack.y)));
        }

        private void OnBoxSelectEnd(PointerUpEvent upEvent)
        {
            VisualElement lane = upEvent.currentTarget as VisualElement;
            if (lane != null)
            {
                lane.ReleasePointer(upEvent.pointerId);
                lane.UnregisterCallback<PointerMoveEvent>(OnBoxSelectMove);
                lane.UnregisterCallback<PointerUpEvent>(OnBoxSelectEnd);
            }

            if (!isBoxSelectArmed)
            {
                return;
            }
            isBoxSelectArmed = false;
            boxSelectLane = null;

            if (!isBoxSelectActive)
            {
                // It was a click after all, so now the playhead moves.
                SetPlayheadTime(pendingPlayheadTime);
                return;
            }
            isBoxSelectActive = false;

            Vector2 endInStack = lane != null
                ? lane.ChangeCoordinatesTo(laneStack, upEvent.localPosition)
                : boxSelectOriginInStack;
            Rect bandRect = Rect.MinMaxRect(
                Mathf.Min(boxSelectOriginInStack.x, endInStack.x),
                Mathf.Min(boxSelectOriginInStack.y, endInStack.y),
                Mathf.Max(boxSelectOriginInStack.x, endInStack.x),
                Mathf.Max(boxSelectOriginInStack.y, endInStack.y));

            SelectKeysInsideBand(bandRect);
            boxSelectElement.HideBand();

            RepaintLanes();
            RebuildInspector();
        }

        /// <summary>Adds every key whose lane row and time fall inside the band.</summary>
        private void SelectKeysInsideBand(Rect bandRect)
        {
            if (!isBoxSelectAdditive)
            {
                session.SelectedKeys.Clear();
                session.HasActiveKey = false;
            }

            for (int childIndex = 0; childIndex < laneColumn.childCount; childIndex++)
            {
                TrackLaneElement lane = laneColumn[childIndex] as TrackLaneElement;
                if (lane == null)
                {
                    continue;
                }

                Rect laneRectInStack = lane.ChangeCoordinatesTo(laneStack, lane.contentRect);
                if (laneRectInStack.yMax < bandRect.yMin || laneRectInStack.yMin > bandRect.yMax)
                {
                    continue;
                }

                // The live view, not Create(width) -- that one-argument overload means zoom 1 and
                // pan 0, so the band was tested against where the keys would be on an unzoomed,
                // unscrolled timeline rather than where they are. At the default view the two agree,
                // which is why this looked like it worked; anywhere else it selected whatever
                // happened to line up under the wrong mapping.
                TimelineGeometry geometry =
                    TimelineGeometry.Create(LaneWidth, viewZoom, viewPan);
                IReadOnlyList<float> keyTimes = lane.KeyTimes;
                for (int keyIndex = 0; keyIndex < keyTimes.Count; keyIndex++)
                {
                    float keyXInLane = geometry.TimeToX(keyTimes[keyIndex]);
                    float keyXInStack =
                        lane.ChangeCoordinatesTo(laneStack, new Vector2(keyXInLane, 0f)).x;
                    if (keyXInStack >= bandRect.xMin && keyXInStack <= bandRect.xMax)
                    {
                        session.SelectedKeys.Add(
                            new KeyAddress(lane.trackKind, lane.trackIndex, keyIndex));
                    }
                }
            }
        }

        // -------------------------------------------------------------------------------------
        // Keyboard map
        // -------------------------------------------------------------------------------------

        private void OnTimelineKeyDown(KeyDownEvent keyEvent)
        {
            // A running grab or scale owns the keyboard. This handler sits on the lane stack, which
            // is inside the root the modal handler listens on, so it sees every key first — and it
            // reads Backspace as "delete the selected keys" while the gesture reads it as "rub out
            // the last digit I typed". Bowing out here lets the event bubble to the gesture.
            if (IsTransformActive)
            {
                return;
            }

            if (session.SelectedClip == null)
            {
                return;
            }

            bool commandModifier = keyEvent.ctrlKey || keyEvent.commandKey;

            switch (keyEvent.keyCode)
            {
                case KeyCode.Space:
                    TransportTarget.TogglePlay();
                    break;
                case KeyCode.Delete:
                case KeyCode.Backspace:
                    DeleteSelectedKeys();
                    break;
                case KeyCode.Home:
                    TransportTarget.JumpToStart();
                    break;
                case KeyCode.End:
                    TransportTarget.JumpToEnd();
                    break;
                case KeyCode.LeftArrow:
                    TransportTarget.Step(keyEvent.shiftKey ? -Mathf.Max(1, LargeStepFrames) : -1);
                    break;
                case KeyCode.RightArrow:
                    TransportTarget.Step(keyEvent.shiftKey ? Mathf.Max(1, LargeStepFrames) : 1);
                    break;
                case KeyCode.C:
                    if (!commandModifier)
                    {
                        return;
                    }
                    CopySelectedKeys();
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
                    CopySelectedKeys();
                    PasteKeysAtPlayhead();
                    break;
                default:
                    return;
            }

            keyEvent.StopPropagation();
        }

        /// <summary>Puts the selected keys on the clipboard, and says what was taken.</summary>
        internal void CopySelectedKeys()
        {
            ClipKeyClipboard.Copy(session.SelectedClip, session.SelectedKeys);
            if (!ClipKeyClipboard.HasContent)
            {
                return;
            }

            // Said out loud because copy is the one half of the pair with nothing on screen to show
            // for it. Silence after Ctrl+C is indistinguishable from a shortcut that did not fire.
            ShowNotification(new GUIContent(
                "Copied " + ClipKeyClipboard.KeyCount.ToString() + " key(s) from "
                + ClipKeyClipboard.ObjectCount.ToString() + " object(s)"));
        }

        /// <summary>Pastes the clipboard onto the selected objects, anchored at the playhead.</summary>
        internal void PasteKeysAtPlayhead()
        {
            if (!ClipKeyClipboard.HasContent || session.SelectedClip == null)
            {
                return;
            }

            pasteDestinations.Clear();
            for (int itemIndex = 0; itemIndex < session.SelectedHierarchyItems.Count; itemIndex++)
            {
                pasteDestinations.Add(BuildObjectRef(session.SelectedHierarchyItems[itemIndex]));
            }

            // Recorded whether or not the paste turns out to write the rig: a flipbook pasted onto
            // an untagged node can declare a new part, and that can't be known before it runs.
            RigAsset rig = ActiveRig;
            if (rig != null)
            {
                RecordSocketEdit(rig, "Paste Animation Keys");
            }

            BeginUndoGesture("Paste Animation Keys");
            ClipKeyPasteResult pasteResult =
                ClipKeyClipboard.Paste(session.SelectedClip, rig, pasteDestinations, session.PlayheadNormalized);

            // A paste can mint a track on an untagged part; A56 D4 says no keyed track goes
            // tagless, and inside the gesture so one Ctrl+Z undoes the paste and the tagging.
            EnsureClipTrackTagsAssigned("Paste Animation Keys");
            EndUndoGesture();

            if (pasteResult.touchedRig && rig != null)
            {
                AssetDatabase.SaveAssetIfDirty(rig);
                CommitSocketEdit(true);
            }

            if (pasteResult.keyCount > 0)
            {
                EditorUtility.SetDirty(session.SelectedClip);
                SortAllTracks();
            }

            // A promoted node has just become a part, so its row stands for one and the timeline has
            // a lane it did not have. Both are rebuilt even when nothing was pasted, because a
            // component may still have been added.
            if (pasteResult.keyCount > 0 || pasteResult.addedComponentCount > 0)
            {
                RebuildTimeline();
                RebuildHierarchy();
                RebuildInspector();
            }

            ShowNotification(new GUIContent(DescribePasteResult(pasteResult)));
        }

        /// <summary>One line saying what the paste did, including the parts of it that did nothing.</summary>
        private static string DescribePasteResult(ClipKeyPasteResult pasteResult)
        {
            if (pasteResult.keyCount == 0 && pasteResult.addedComponentCount == 0)
            {
                return "Nothing pasted — the clipboard's components could not be placed here.";
            }

            string described = "Pasted " + pasteResult.keyCount.ToString() + " key(s)";
            if (pasteResult.addedComponentCount > 0)
            {
                described += ", added " + pasteResult.addedComponentCount.ToString()
                    + " component(s)";
            }
            if (pasteResult.droppedKeyCount > 0)
            {
                described += ", dropped " + pasteResult.droppedKeyCount.ToString();
            }
            return described;
        }

        // Removed in descending index order: ascending would shift not-yet-deleted indices down by
        // one each time. Event addresses sort by flat storage index, not lane-local, since every
        // event lane shares one underlying events list and a removal can shift a different lane's
        // not-yet-processed marker.
        /// <summary>Removes every selected key.</summary>
        internal void DeleteSelectedKeys()
        {
            if (session.SelectedKeys.Count == 0)
            {
                return;
            }

            KeyAddress[] ordered = new List<KeyAddress>(session.SelectedKeys).ToArray();
            int[] removalIndex = new int[ordered.Length];
            for (int index = 0; index < ordered.Length; index++)
            {
                removalIndex[index] = ordered[index].trackKind == TimelineTrackKind.Event
                    ? EventLaneAddressing.ResolveFlatIndex(
                        session.SelectedClip.events, ordered[index].trackIndex, ordered[index].keyIndex)
                    : ordered[index].keyIndex;
            }
            System.Array.Sort(removalIndex, ordered);
            System.Array.Reverse(removalIndex);
            System.Array.Reverse(ordered);

            BeginUndoGesture("Delete Animation Keys");
            for (int addressIndex = 0; addressIndex < ordered.Length; addressIndex++)
            {
                KeyAddress address = ordered[addressIndex];
                int flatOrLocalIndex = removalIndex[addressIndex];
                switch (address.trackKind)
                {
                    case TimelineTrackKind.Transform:
                        if (address.trackIndex < session.SelectedClip.transformTracks.Count
                            && flatOrLocalIndex < session.SelectedClip.transformTracks[address.trackIndex].keys.Count)
                        {
                            session.SelectedClip.transformTracks[address.trackIndex].keys.RemoveAt(flatOrLocalIndex);
                        }
                        break;
                    case TimelineTrackKind.Sprite:
                        if (address.trackIndex < session.SelectedClip.spriteTracks.Count
                            && flatOrLocalIndex < session.SelectedClip.spriteTracks[address.trackIndex].keys.Count)
                        {
                            session.SelectedClip.spriteTracks[address.trackIndex].keys.RemoveAt(flatOrLocalIndex);
                        }
                        break;
                    case TimelineTrackKind.Bone:
                        if (session.SelectedClip.boneTracks != null
                            && address.trackIndex < session.SelectedClip.boneTracks.Count
                            && flatOrLocalIndex < session.SelectedClip.boneTracks[address.trackIndex].keys.Count)
                        {
                            session.SelectedClip.boneTracks[address.trackIndex].keys.RemoveAt(flatOrLocalIndex);
                        }
                        break;
                    default:
                        if (flatOrLocalIndex >= 0 && flatOrLocalIndex < session.SelectedClip.events.Count)
                        {
                            session.SelectedClip.events.RemoveAt(flatOrLocalIndex);
                        }
                        break;
                }
            }
            EndUndoGesture();

            EditorUtility.SetDirty(session.SelectedClip);
            session.SelectedKeys.Clear();
            session.HasActiveKey = false;
            RebuildTimeline();
        }

        // -------------------------------------------------------------------------------------
        // Key access. The lists hold structs, so every edit is a read-modify-write.
        // -------------------------------------------------------------------------------------

        internal float GetKeyTime(KeyAddress address)
        {
            if (session.SelectedClip == null)
            {
                return 0f;
            }
            switch (address.trackKind)
            {
                case TimelineTrackKind.Transform:
                    return session.SelectedClip.transformTracks[address.trackIndex].keys[address.keyIndex].normalizedTime;
                case TimelineTrackKind.Sprite:
                    return session.SelectedClip.spriteTracks[address.trackIndex].keys[address.keyIndex].normalizedTime;
                case TimelineTrackKind.Bone:
                    return session.SelectedClip.boneTracks[address.trackIndex].keys[address.keyIndex].normalizedTime;
                default:
                {
                    int flatIndex = ResolveEventFlatIndex(address);
                    return flatIndex >= 0 ? session.SelectedClip.events[flatIndex].normalizedTime : 0f;
                }
            }
        }

        internal void SetKeyTime(KeyAddress address, float normalizedTime)
        {
            if (session.SelectedClip == null)
            {
                return;
            }
            switch (address.trackKind)
            {
                case TimelineTrackKind.Transform:
                {
                    TransformTrack track = session.SelectedClip.transformTracks[address.trackIndex];
                    TransformKey key = track.keys[address.keyIndex];
                    key.normalizedTime = normalizedTime;
                    track.keys[address.keyIndex] = key;
                    break;
                }
                case TimelineTrackKind.Sprite:
                {
                    SpriteTrack track = session.SelectedClip.spriteTracks[address.trackIndex];
                    SpriteKey key = track.keys[address.keyIndex];
                    key.normalizedTime = normalizedTime;
                    track.keys[address.keyIndex] = key;
                    break;
                }
                case TimelineTrackKind.Bone:
                {
                    BoneTrack track = session.SelectedClip.boneTracks[address.trackIndex];
                    BoneKey key = track.keys[address.keyIndex];
                    key.normalizedTime = normalizedTime;
                    track.keys[address.keyIndex] = key;
                    break;
                }
                default:
                {
                    int flatIndex = ResolveEventFlatIndex(address);
                    if (flatIndex < 0)
                    {
                        break;
                    }
                    EventMarker marker = session.SelectedClip.events[flatIndex];
                    marker.normalizedTime = normalizedTime;
                    session.SelectedClip.events[flatIndex] = marker;
                    break;
                }
            }
        }

        /// <summary>The flat <see cref="session.SelectedClip"/>.events position one event address points to.</summary>
        internal int ResolveEventFlatIndex(KeyAddress address)
        {
            return EventLaneAddressing.ResolveFlatIndex(
                session.SelectedClip.events, address.trackIndex, address.keyIndex);
        }

        // Copies the preceding key rather than type defaults, so adding a key does not change the
        // pose the clip produces — a key at zero scale would snap the part to the origin on insert.
        /// <summary>Adds a key at <paramref name="normalizedTime"/>, copying the key at or before it.</summary>
        private void InsertKey(
            TimelineTrackKind trackKind, int trackIndex, float normalizedTime, uint explicitEventKey = 0u)
        {
            switch (trackKind)
            {
                case TimelineTrackKind.Transform:
                {
                    List<TransformKey> keys = session.SelectedClip.transformTracks[trackIndex].keys;
                    TransformKey inserted = new TransformKey
                    {
                        position = float3.zero,
                        scale = new float3(1f, 1f, 1f),
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
                    List<SpriteKey> keys = session.SelectedClip.spriteTracks[trackIndex].keys;
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
                case TimelineTrackKind.Bone:
                {
                    List<BoneKey> keys = session.SelectedClip.boneTracks[trackIndex].keys;

                    // Identity rotation and unit scale, so an inserted key on an empty track is the
                    // bone's rest pose rather than a degenerate zero-scale quaternion. A default
                    // quaternion is all zeros, which is not a rotation at all and collapses the
                    // skin the moment it is sampled.
                    BoneKey inserted = new BoneKey
                    {
                        localPosition = float3.zero,
                        localRotation = quaternion.identity,
                        localScale = new float3(1f, 1f, 1f),
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
                default:
                {
                    // trackIndex addresses an existing event lane — double-clicking the "Footstep"
                    // lane adds another Footstep. A negative trackIndex (the transport bar's Add
                    // Event button) has no lane to read, so it carries the key the picker already
                    // chose instead of guessing one. Never key 0: that struct default is the
                    // reserved "invalid" key, which used to fail validation at bake time.
                    List<uint> laneKeys = EventLaneAddressing.ComputeLaneKeys(session.SelectedClip.events);
                    uint eventKey = trackIndex >= 0 && trackIndex < laneKeys.Count
                        ? laneKeys[trackIndex]
                        : explicitEventKey;
                    session.SelectedClip.events.Add(new EventMarker
                    {
                        normalizedTime = normalizedTime,
                        eventKey = eventKey,
                        windowSeconds = ResolveDefaultWindowSecondsForKey(eventKey)
                    });
                    break;
                }
            }
        }

        // Selects the new marker rather than clearing selection, unlike a double-click add: a
        // toolbar button gives the author no on-screen cue to where the marker landed.
        /// <summary>Places a marker for <paramref name="eventKey"/> at the playhead and selects it.</summary>
        internal void AddEventAtPlayhead(uint eventKey)
        {
            if (session.SelectedClip == null)
            {
                return;
            }

            float insertTime = TimelineGeometry.Snap(session.PlayheadNormalized, SnapFrameCount);
            BeginUndoGesture("Add Event");

            // -1: this button targets no particular lane, unlike a double-click inside one, so
            // InsertKey carries the key the picker already chose instead of reading laneKeys[-1].
            InsertKey(TimelineTrackKind.Event, -1, insertTime, eventKey);
            EndUndoGesture();

            EditorUtility.SetDirty(session.SelectedClip);

            // Select the marker just added, before the sort below can move it — SortTrackKeys
            // remaps whatever is selected through the sort's index map, so selecting first and
            // sorting after is what lets the selection follow the marker to wherever it lands
            // rather than pointing at whatever key ends up in its old slot.
            int newFlatIndex = session.SelectedClip.events.Count - 1;
            KeyAddress newAddress = ResolveEventKeyAddressForFlatIndex(newFlatIndex);
            session.SelectedKeys.Clear();
            session.SelectedKeys.Add(newAddress);
            session.ActiveKey = newAddress;
            session.HasActiveKey = true;

            SortTrackKeys(TimelineTrackKind.Event, newAddress.trackIndex);
            SetPlayheadTime(insertTime);
            RebuildTimeline();
        }

        /// <summary>That event's default window, if it has one.</summary>
        private float ResolveDefaultWindowSecondsForKey(uint eventKey)
        {
            AnimEventKeyRegistry registry = ClipInspectorPane.ResolveEventKeyRegistry();
            AnimEventKeyEntry entry = ClipInspectorPane.FindRegistryEntryByKey(registry, eventKey);
            if (entry == null || entry.defaultWindowFrames <= 0)
            {
                return 0f;
            }
            return entry.defaultWindowFrames / ClipInspectorPane.ResolveReferenceFrameRate(registry);
        }

        // -------------------------------------------------------------------------------------
        // Event lane header menu.
        // -------------------------------------------------------------------------------------

        /// <summary>
        /// Right-click menu for an event lane's header: add another marker to this lane without
        /// hunting for empty space in it, select every marker on it, re-point the whole lane to a
        /// different event, or delete it outright.
        /// </summary>
        private void BuildEventLaneContextMenu(
            ContextualMenuPopulateEvent menuEvent, int laneIndex, VisualElement anchor)
        {
            if (session.SelectedClip == null)
            {
                return;
            }
            List<uint> laneKeys = EventLaneAddressing.ComputeLaneKeys(session.SelectedClip.events);
            if (laneIndex < 0 || laneIndex >= laneKeys.Count)
            {
                return;
            }
            uint laneKey = laneKeys[laneIndex];

            menuEvent.menu.AppendAction(
                "Add marker at playhead", action => AddEventAtPlayhead(laneKey));
            menuEvent.menu.AppendAction(
                "Select all markers",
                action => SelectAllKeysOnTrack(TimelineTrackKind.Event, laneIndex, false));
            menuEvent.menu.AppendAction(
                "Change event…", action => OpenChangeLaneEventPicker(laneIndex, anchor));
            menuEvent.menu.AppendAction(
                "Delete lane", action => DeleteEventLane(laneIndex));
        }

        /// <summary>
        /// Opens the event picker anchored to a lane header. Unlike the picker a marker's own
        /// inspector opens (<see cref="OpenEventKeyPicker"/>), the choice here repoints every marker
        /// on the lane at once — see <see cref="ApplyLaneEventChoice"/>.
        /// </summary>
        private void OpenChangeLaneEventPicker(int laneIndex, VisualElement anchor)
        {
            if (session.SelectedClip == null)
            {
                return;
            }
            AnimEventKeyRegistry registry = ClipInspectorPane.ResolveEventKeyRegistry();
            VocabularyPicker.Open(
                WindowRoot,
                anchor,
                registry,
                registry,
                VocabularyPickerConfig.ForEventKeys(registry),
                chosenEventKey => ApplyLaneEventChoice(laneIndex, chosenEventKey),
                RebuildTimeline);
        }

        /// <summary>
        /// Re-points every marker in one lane to <paramref name="chosenEventKey"/> under one undo
        /// gesture — distinct from renaming the registry row, which changes what a key is called
        /// rather than which key a marker carries.
        /// </summary>
        private void ApplyLaneEventChoice(int laneIndex, uint chosenEventKey)
        {
            if (session.SelectedClip == null || session.SelectedClip.events == null)
            {
                return;
            }
            List<int> flatIndices = EventLaneAddressing.ResolveLaneFlatIndices(session.SelectedClip.events, laneIndex);
            if (flatIndices.Count == 0)
            {
                return;
            }

            RecordClipEdit("Change Event");
            for (int position = 0; position < flatIndices.Count; position++)
            {
                EventMarker marker = session.SelectedClip.events[flatIndices[position]];
                marker.eventKey = chosenEventKey;
                session.SelectedClip.events[flatIndices[position]] = marker;
            }
            CommitClipEdit();
            RebuildTimeline();
        }

        /// <summary>
        /// Removes every marker in one lane, behind a confirmation naming how many — the same
        /// courtesy a registry-row delete gives (<c>TargetTagRegistryEditor.RemoveEntry</c>,
        /// <c>AnimEventKeyRegistryEditor.RemoveEntry</c>), but this deletes markers, not a
        /// vocabulary row.
        /// </summary>
        private void DeleteEventLane(int laneIndex)
        {
            if (session.SelectedClip == null || session.SelectedClip.events == null)
            {
                return;
            }
            List<int> flatIndices = EventLaneAddressing.ResolveLaneFlatIndices(session.SelectedClip.events, laneIndex);
            if (flatIndices.Count == 0)
            {
                return;
            }

            List<uint> laneKeys = EventLaneAddressing.ComputeLaneKeys(session.SelectedClip.events);
            string laneLabel = laneIndex < laneKeys.Count
                ? ClipInspectorPane.DescribeEventName(laneKeys[laneIndex], ClipInspectorPane.ResolveEventKeyRegistry())
                : "this lane";

            bool confirmed = EditorUtility.DisplayDialog(
                "Delete Event Lane",
                "Delete lane '" + laneLabel + "'?\n\n" + flatIndices.Count
                    + " marker(s) on it will be removed.",
                "Delete", "Cancel");
            if (!confirmed)
            {
                return;
            }

            RecordClipEdit("Delete Event Lane");
            // flatIndices is in ascending flat order (EventLaneAddressing's documented contract);
            // removing from the back is what keeps the not-yet-removed indices still ahead of it
            // valid as the list shrinks.
            for (int position = flatIndices.Count - 1; position >= 0; position--)
            {
                session.SelectedClip.events.RemoveAt(flatIndices[position]);
            }
            CommitClipEdit();

            session.SelectedKeys.Clear();
            session.HasActiveKey = false;
            RebuildTimeline();
        }

        // The sampler's segment search assumes ascending times; an out-of-order key does not throw,
        // it silently makes a segment unreachable. Dragging a key past a neighbour reorders rather
        // than clamps, so indices change here — the selection is remapped, not cleared, below.
        /// <summary>Restores ascending key order after an edit, and moves the selection with the keys.</summary>
        internal void SortTrackKeys(TimelineTrackKind trackKind, int trackIndex)
        {
            int[] newIndexOfOldIndex;
            switch (trackKind)
            {
                case TimelineTrackKind.Transform:
                    newIndexOfOldIndex = SortKeysTrackingIndices(
                        session.SelectedClip.transformTracks[trackIndex].keys, TransformKeyTime);
                    break;
                case TimelineTrackKind.Sprite:
                    newIndexOfOldIndex = SortKeysTrackingIndices(
                        session.SelectedClip.spriteTracks[trackIndex].keys, SpriteKeyTime);
                    break;
                case TimelineTrackKind.Bone:
                    newIndexOfOldIndex = SortKeysTrackingIndices(
                        session.SelectedClip.boneTracks[trackIndex].keys, BoneKeyTime);
                    break;
                default:
                    newIndexOfOldIndex = SortEventLaneKeys(trackIndex);
                    break;
            }

            lastSortIndexMap = newIndexOfOldIndex;
            RemapSelectionAfterSort(trackKind, trackIndex, newIndexOfOldIndex);
        }

        // Writes sorted markers back into the exact flat slots this lane's markers already occupied,
        // rather than sorting the whole list, since every lane shares ClipAsset.events.
        /// <summary>Sorts one event lane's markers by time in place, without disturbing any other lane's markers.</summary>
        private int[] SortEventLaneKeys(int laneIndex)
        {
            List<int> flatIndices = EventLaneAddressing.ResolveLaneFlatIndices(
                session.SelectedClip.events, laneIndex);
            List<EventMarker> laneMarkers = new List<EventMarker>(flatIndices.Count);
            for (int position = 0; position < flatIndices.Count; position++)
            {
                laneMarkers.Add(session.SelectedClip.events[flatIndices[position]]);
            }

            int[] newIndexOfOldIndex = SortKeysTrackingIndices(laneMarkers, EventMarkerTime);

            for (int position = 0; position < flatIndices.Count; position++)
            {
                session.SelectedClip.events[flatIndices[position]] = laneMarkers[position];
            }
            return newIndexOfOldIndex;
        }

        // Read by the modal grab/scale gesture, which tracks keys by their index at gesture start
        // and must follow them through every re-sort a mirroring scale causes.
        /// <summary>The index map produced by the most recent sort.</summary>
        private int[] lastSortIndexMap;

        // The width is deliberately not cached alongside it: a cached width is how the cursor and
        // the key came apart.
        /// <summary>Pointer x within the dragged lane, as of the last move.</summary>
        private float dragPointerLaneX;
        private IVisualElementScheduledItem dragAutoScroll;

        // Ties break on the original index, making the sort stable: two keys stacked on the same
        // frame keep their order rather than swapping on every re-sort.
        /// <summary>Sorts a key list by time and reports where each key ended up.</summary>
        private static int[] SortKeysTrackingIndices<TKey>(List<TKey> keys, System.Func<TKey, float> timeOf)
        {
            int keyCount = keys.Count;
            int[] sortedOrder = new int[keyCount];
            for (int index = 0; index < keyCount; index++)
            {
                sortedOrder[index] = index;
            }

            TKey[] originalKeys = keys.ToArray();
            System.Array.Sort(sortedOrder, delegate (int leftIndex, int rightIndex)
            {
                int comparison = timeOf(originalKeys[leftIndex])
                    .CompareTo(timeOf(originalKeys[rightIndex]));
                return comparison != 0 ? comparison : leftIndex.CompareTo(rightIndex);
            });

            int[] newIndexOfOldIndex = new int[keyCount];
            for (int position = 0; position < keyCount; position++)
            {
                keys[position] = originalKeys[sortedOrder[position]];
                newIndexOfOldIndex[sortedOrder[position]] = position;
            }
            return newIndexOfOldIndex;
        }

        /// <summary>Rewrites the addresses of one track's selected keys through a sort's index map.</summary>
        private void RemapSelectionAfterSort(
            TimelineTrackKind trackKind, int trackIndex, int[] newIndexOfOldIndex)
        {
            if (session.SelectedKeys.Count == 0)
            {
                return;
            }

            List<KeyAddress> remapped = new List<KeyAddress>(session.SelectedKeys.Count);
            bool changed = false;
            foreach (KeyAddress address in session.SelectedKeys)
            {
                // Other tracks did not move, so their addresses are still correct.
                if (address.trackKind != trackKind || address.trackIndex != trackIndex)
                {
                    remapped.Add(address);
                    continue;
                }
                if (address.keyIndex < 0 || address.keyIndex >= newIndexOfOldIndex.Length)
                {
                    // The key is gone rather than moved; dropping it is the only honest answer.
                    changed = true;
                    continue;
                }
                remapped.Add(new KeyAddress(
                    trackKind, trackIndex, newIndexOfOldIndex[address.keyIndex]));
                changed = true;
            }

            if (!changed)
            {
                return;
            }

            session.SelectedKeys.Clear();
            for (int index = 0; index < remapped.Count; index++)
            {
                session.SelectedKeys.Add(remapped[index]);
            }

            if (session.HasActiveKey
                && session.ActiveKey.trackKind == trackKind
                && session.ActiveKey.trackIndex == trackIndex)
            {
                if (session.ActiveKey.keyIndex >= 0 && session.ActiveKey.keyIndex < newIndexOfOldIndex.Length)
                {
                    session.ActiveKey = new KeyAddress(
                        trackKind, trackIndex, newIndexOfOldIndex[session.ActiveKey.keyIndex]);
                }
                else
                {
                    session.HasActiveKey = false;
                }
            }
        }

        /// <summary>Selects every key in the clip, across every track.</summary>
        internal void SelectAllKeys()
        {
            if (session.SelectedClip == null)
            {
                return;
            }
            session.SelectedKeys.Clear();
            session.HasActiveKey = false;

            AddTrackKeysToSelection(TimelineTrackKind.Transform, session.SelectedClip.transformTracks.Count);
            AddTrackKeysToSelection(TimelineTrackKind.Sprite, session.SelectedClip.spriteTracks.Count);
            AddTrackKeysToSelection(
                TimelineTrackKind.Bone,
                session.SelectedClip.boneTracks != null ? session.SelectedClip.boneTracks.Count : 0);
            AddTrackKeysToSelection(
                TimelineTrackKind.Event,
                EventLaneAddressing.ComputeLaneKeys(session.SelectedClip.events).Count);

            RepaintLanes();
            RebuildInspector();
            RebuildTimeline();
        }

        private void AddTrackKeysToSelection(TimelineTrackKind trackKind, int trackCount)
        {
            for (int trackIndex = 0; trackIndex < trackCount; trackIndex++)
            {
                AddKeysOnTrackToSelection(trackKind, trackIndex);
            }
        }

        private void AddKeysOnTrackToSelection(TimelineTrackKind trackKind, int trackIndex)
        {
            int keyCount = CountKeysOnTrack(trackKind, trackIndex);
            for (int keyIndex = 0; keyIndex < keyCount; keyIndex++)
            {
                session.SelectedKeys.Add(new KeyAddress(trackKind, trackIndex, keyIndex));
            }
        }

        /// <summary>Clears the key selection without touching the hierarchy selection.</summary>
        internal void DeselectAllKeys()
        {
            if (session.SelectedKeys.Count == 0)
            {
                return;
            }
            session.SelectedKeys.Clear();
            session.HasActiveKey = false;
            RepaintLanes();
            RebuildInspector();
            RebuildTimeline();
        }

        /// <summary>Selects every key on one track, replacing the selection unless adding to it.</summary>
        private void SelectAllKeysOnTrack(
            TimelineTrackKind trackKind, int trackIndex, bool additive)
        {
            if (session.SelectedClip == null)
            {
                return;
            }
            if (!additive)
            {
                session.SelectedKeys.Clear();
                session.HasActiveKey = false;
            }
            AddKeysOnTrackToSelection(trackKind, trackIndex);
            RepaintLanes();
            RebuildInspector();
            RebuildTimeline();
        }

        private static float TransformKeyTime(TransformKey key)
        {
            return key.normalizedTime;
        }

        private static float SpriteKeyTime(SpriteKey key)
        {
            return key.normalizedTime;
        }

        private static float BoneKeyTime(BoneKey key)
        {
            return key.normalizedTime;
        }

        private static float EventMarkerTime(EventMarker marker)
        {
            return marker.normalizedTime;
        }

        internal void SortAllTracks()
        {
            for (int trackIndex = 0; trackIndex < session.SelectedClip.transformTracks.Count; trackIndex++)
            {
                session.SelectedClip.transformTracks[trackIndex].keys.Sort(CompareTransformKeys);
            }
            for (int trackIndex = 0; trackIndex < session.SelectedClip.spriteTracks.Count; trackIndex++)
            {
                session.SelectedClip.spriteTracks[trackIndex].keys.Sort(CompareSpriteKeys);
            }
            for (int trackIndex = 0;
                session.SelectedClip.boneTracks != null && trackIndex < session.SelectedClip.boneTracks.Count;
                trackIndex++)
            {
                session.SelectedClip.boneTracks[trackIndex].keys.Sort(CompareBoneKeys);
            }
            session.SelectedClip.events.Sort(CompareEventMarkers);
            session.SelectedKeys.Clear();
            session.HasActiveKey = false;
        }

        private static int CompareTransformKeys(TransformKey first, TransformKey second)
        {
            return first.normalizedTime.CompareTo(second.normalizedTime);
        }

        private static int CompareSpriteKeys(SpriteKey first, SpriteKey second)
        {
            return first.normalizedTime.CompareTo(second.normalizedTime);
        }

        private static int CompareBoneKeys(BoneKey first, BoneKey second)
        {
            return first.normalizedTime.CompareTo(second.normalizedTime);
        }

        private static int CompareEventMarkers(EventMarker first, EventMarker second)
        {
            return first.normalizedTime.CompareTo(second.normalizedTime);
        }

        /// <summary>
        /// A merge deleted a track, so every stored track index after it points one row off.
        /// Selection and expansion are addressed by those indices; cheaper to drop than remap.
        /// </summary>
        internal void OnTrackListChanged()
        {
            session.SelectedKeys.Clear();
            session.HasActiveKey = false;
            expandedTrackKeys.Clear();
        }

        internal int CountKeysOnTrack(TimelineTrackKind trackKind, int trackIndex)
        {
            if (session.SelectedClip == null)
            {
                return 0;
            }
            switch (trackKind)
            {
                case TimelineTrackKind.Transform:
                    return trackIndex >= 0 && trackIndex < session.SelectedClip.transformTracks.Count
                        ? session.SelectedClip.transformTracks[trackIndex].keys.Count : 0;
                case TimelineTrackKind.Sprite:
                    return trackIndex >= 0 && trackIndex < session.SelectedClip.spriteTracks.Count
                        ? session.SelectedClip.spriteTracks[trackIndex].keys.Count : 0;
                case TimelineTrackKind.Bone:
                    return session.SelectedClip.boneTracks != null
                        && trackIndex >= 0 && trackIndex < session.SelectedClip.boneTracks.Count
                        ? session.SelectedClip.boneTracks[trackIndex].keys.Count : 0;
                default:
                    return EventLaneAddressing.ResolveLaneFlatIndices(
                        session.SelectedClip.events, trackIndex).Count;
            }
        }
    }
}
