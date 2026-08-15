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
    /// <strong>The layout is a persistent dock, declared in ClipEditorWindow.uxml.</strong> Three
    /// zones: hierarchy, viewport and inspector across the top, timeline along the bottom, nested
    /// <c>TwoPaneSplitView</c>s all the way down so every boundary is draggable. This file builds no
    /// layout of its own — it resolves the named slots and fills them — and it sets no sizes: those
    /// live in ClipEditorWindow.uss, because an inline style beats every rule in a stylesheet and so
    /// one stray <c>style.height</c> is a value nobody can override.
    /// </para>
    /// <para>
    /// <strong>The viewport is independent of selection.</strong> It initialises and renders from
    /// the moment the window opens, showing the reference grid when there is nothing else to show.
    /// Selection moves the marker and changes what the inspector displays; it decides nothing about
    /// whether the viewport draws. It used to decide exactly that, and an empty viewport was
    /// indistinguishable from a preview that had failed to start.
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

        private const string LayoutAssetPath =
            "Packages/com.stitchpunk.dotsanimationtoolkit/Editor/ClipEditor/ClipEditorWindow.uxml";

        /// <summary>
        /// Prefix for the persisted split positions. Keyed by window rather than by project on
        /// purpose: a dock layout is a habit of the person, and following them between projects is
        /// the behaviour every other editor window has.
        /// </summary>
        private const string SplitPrefsPrefix = "StitchPunk.AnimationToolkit.ClipEditor.Split.";

        /// <summary>Below this a pane is a sliver with nothing readable in it, so it is never stored.</summary>
        private const float MinimumSplitDimension = 60f;

        /// <summary>
        /// How far the pointer may travel between press and release and still count as a click.
        /// </summary>
        /// <remarks>
        /// A drag in the viewport orbits the camera. Without this, every orbit would also change the
        /// selection, because an orbit begins with exactly the same press a selection does.
        /// </remarks>
        private const float ClickMovementToleranceSquared = 9f;

        private const string HiddenUssClassName = "clip-editor--hidden";
        private const string ClipRowUssClassName = "clip-editor__clip-row";
        private const string HierarchyRowUssClassName = "clip-editor__hierarchy-row";
        private const string AnimatedBoneUssClassName = "clip-editor__hierarchy-row--animated";
        private const string TrackHeaderUssClassName = "clip-editor__track-header";
        private const string HeadingUssClassName = "clip-editor__heading";
        private const string HintUssClassName = "clip-editor__hint";

        private ObjectField clipSetField;
        private ListView clipListView;
        private Button newClipButton;
        private TreeView hierarchyTreeView;
        private Label hierarchyEmptyLabel;
        private ToolbarToggle playToggle;
        private ToolbarToggle snapToggle;
        private IntegerField frameCountField;
        private Label timeLabel;
        private VisualElement trackHeaderColumn;
        private VisualElement laneColumn;
        private VisualElement laneStack;
        private TimeRulerElement ruler;
        private PlayheadElement playhead;
        private ScrollView inspectorPane;
        private Label statusLabel;
        private Image previewImage;
        private Label previewStatusLabel;
        private ValidationBadgeElement validationBadge;
        private ObjectField skinnedSourceField;

        private ClipPreviewController previewController;
        private bool previewRegistryDirty;
        private double previewDirtiedAt;

        private ClipSetAsset clipSet;
        private ClipAsset selectedClip;
        private SerializedObject clipSerializedObject;

        private readonly HashSet<KeyAddress> selectedKeys = new HashSet<KeyAddress>();

        /// <summary>
        /// What is selected in the hierarchy, as the tree item id — which is also the preview's
        /// index for the same transform. -1 is nothing.
        /// </summary>
        /// <remarks>
        /// An index rather than a name because names repeat: a rig with two bones called
        /// <c>Hand</c> needs the tree, the outline and the inspector to agree on <em>which</em> one,
        /// and a name cannot say. An index rather than a <c>Transform</c> because the preview
        /// skeleton is a throwaway instance rebuilt whenever the rig changes, so a held reference
        /// would be a destroyed object more often than not.
        /// </remarks>
        private int selectedHierarchyItemId = -1;

        /// <summary>
        /// The selected transform's name, which is the identity bone <em>tracks</em> bind by.
        /// Carried alongside the index rather than derived from it so the bake's contract and the
        /// window's selection stay separate things.
        /// </summary>
        private string selectedBoneName;

        // Viewport picking. Hits are gathered on pointer-down, from the press position, and applied
        // on release — see OnPreviewPointerUp for why that is not the same as selecting on press.
        private readonly List<PreviewPickHit> pickCandidates = new List<PreviewPickHit>();
        private readonly List<Transform> previousPickCandidates = new List<Transform>();
        private Vector2 pickPressPosition;
        private bool isPickPending;
        private bool isPickCycleRequested;
        private int pickCycleIndex;

        private readonly Dictionary<string, float> persistedSplitDimensions = new Dictionary<string, float>();
        private readonly HashSet<string> pendingProportionalSplitKeys = new HashSet<string>();

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
            previewController = new ClipPreviewController();
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorApplication.update -= OnEditorTick;

            // The preview owns a Persistent-allocator blob and a PreviewRenderUtility, neither of
            // which the GC reclaims. Leaking them survives domain reloads as a growing native
            // allocation, so disposal here is load-bearing rather than tidy.
            if (previewController != null)
            {
                previewController.Dispose();
                previewController = null;
            }
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
            MarkPreviewDirty();
            RebuildTimeline();
        }

        // -------------------------------------------------------------------------------------
        // Layout. The tree comes from UXML; everything below resolves slots and wires behaviour.
        // -------------------------------------------------------------------------------------

        private void CreateGUI()
        {
            VisualTreeAsset layoutAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(LayoutAssetPath);
            if (layoutAsset == null)
            {
                // Loud rather than blank: an empty window with no explanation reads as a crash, and
                // the cause here is always the same — the .uxml was moved, renamed or not imported.
                Label missingLayoutLabel = new Label(
                    "The clip editor layout could not be loaded from " + LayoutAssetPath + ".");
                missingLayoutLabel.AddToClassList(HintUssClassName);
                rootVisualElement.Add(missingLayoutLabel);
                return;
            }

            layoutAsset.CloneTree(rootVisualElement);

            BindToolbar();
            BindClipList();
            BindHierarchy();
            BindViewport();
            BindInspector();
            BindTimeline();
            BindSplits();

            // Sync the preview with the state the window opened in, so the viewport reports "no clip
            // set" from the first frame instead of an empty status line.
            if (previewController != null)
            {
                previewController.SetClipSet(clipSet);
            }
            if (validationBadge != null)
            {
                validationBadge.Refresh(clipSet);
            }

            RefreshNewClipButton();
            RebuildHierarchy();
            RebuildTimeline();
        }

        private void BindToolbar()
        {
            clipSetField = rootVisualElement.Q<ObjectField>("clip-set-field");
            if (clipSetField != null)
            {
                clipSetField.objectType = typeof(ClipSetAsset);
                clipSetField.allowSceneObjects = false;
                clipSetField.RegisterValueChangedCallback(OnClipSetChanged);
            }

            playToggle = rootVisualElement.Q<ToolbarToggle>("play-toggle");
            if (playToggle != null)
            {
                playToggle.RegisterValueChangedCallback(changeEvent => SetPlaying(changeEvent.newValue));
            }

            ToolbarButton rewindButton = rootVisualElement.Q<ToolbarButton>("rewind-button");
            if (rewindButton != null)
            {
                rewindButton.clicked += () => SetPlayheadTime(0f);
            }

            timeLabel = rootVisualElement.Q<Label>("time-label");
            snapToggle = rootVisualElement.Q<ToolbarToggle>("snap-toggle");

            frameCountField = rootVisualElement.Q<IntegerField>("frame-count-field");
            if (frameCountField != null)
            {
                frameCountField.RegisterValueChangedCallback(changeEvent =>
                {
                    if (ruler != null)
                    {
                        ruler.frameCount = Mathf.Max(1, changeEvent.newValue);
                        ruler.MarkDirtyRepaint();
                    }
                });
            }

            // The rigged prefab authored bone tracks pose against (amendment A42, B4). Left empty
            // for cutout clip sets, which then behave exactly as before — with an empty hierarchy
            // pane rather than a missing one.
            skinnedSourceField = rootVisualElement.Q<ObjectField>("skinned-source-field");
            if (skinnedSourceField != null)
            {
                skinnedSourceField.objectType = typeof(GameObject);
                skinnedSourceField.allowSceneObjects = false;
                skinnedSourceField.tooltip =
                    "Rigged prefab for bone tracks. Use the same one the VAT bake samples — "
                    + "a different skeleton would preview motion the bake never sees.";
                skinnedSourceField.RegisterValueChangedCallback(OnSkinnedSourceChanged);
            }

            VisualElement badgeSlot = rootVisualElement.Q<VisualElement>("validation-badge-slot");
            if (badgeSlot != null)
            {
                validationBadge = new ValidationBadgeElement();
                badgeSlot.Add(validationBadge);
            }
        }

        private void BindClipList()
        {
            newClipButton = rootVisualElement.Q<Button>("new-clip-button");
            if (newClipButton != null)
            {
                newClipButton.clicked += CreateClip;
                newClipButton.tooltip =
                    "Create a clip beside the clip set on disk, using the set's rig, and add it to "
                    + "the set.";
            }

            clipListView = rootVisualElement.Q<ListView>("clip-list");
            if (clipListView == null)
            {
                return;
            }
            clipListView.fixedItemHeight = 20f;
            clipListView.selectionType = SelectionType.Single;
            clipListView.makeItem = MakeClipRow;
            clipListView.bindItem = BindClipRow;
            clipListView.selectionChanged += OnClipSelectionChanged;
            clipListView.itemsSource = new List<ClipAsset>();
        }

        /// <summary>
        /// Creates a clip in the assigned set and selects it, ready to author.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The creation is <see cref="ClipCreationUtility"/>'s, shared with the clip set's own
        /// inspector, so a clip made here is indistinguishable from one made there — same folder,
        /// same inherited rig, same id minting, same undo entry.
        /// </para>
        /// <para>
        /// Selecting the new clip immediately is the point of having the button here at all: the
        /// alternative is creating it in the Project window and coming back to find it in the list.
        /// It is pinged as well, because it is written to disk without asking where, and a file
        /// appearing somewhere the user was not told about is worse than a moment's flicker in the
        /// Project window.
        /// </para>
        /// </remarks>
        private void CreateClip()
        {
            if (clipSet == null)
            {
                return;
            }

            ClipAsset newClip = ClipCreationUtility.CreateClipInSet(clipSet);
            if (newClip == null)
            {
                return;
            }

            RefreshClipList();
            EditorGUIUtility.PingObject(newClip);

            int newClipIndex = clipSet.clips != null ? clipSet.clips.IndexOf(newClip) : -1;
            if (newClipIndex >= 0)
            {
                // Through the list's own selection, so creating a clip lands in exactly the state
                // clicking one would — SelectClip, the timeline rebuild and the inspector all follow
                // from the one notification.
                clipListView.SetSelection(newClipIndex);
                clipListView.ScrollToItem(newClipIndex);
            }

            MarkPreviewDirty();
        }

        /// <summary>Re-points the list at the set's clips and repaints its rows.</summary>
        private void RefreshClipList()
        {
            if (clipListView == null)
            {
                return;
            }
            clipListView.itemsSource = clipSet != null && clipSet.clips != null
                ? (System.Collections.IList)clipSet.clips
                : new List<ClipAsset>();
            clipListView.Rebuild();
        }

        /// <summary>
        /// Enables the New button only when there is a set for a clip to be created in.
        /// </summary>
        /// <remarks>
        /// A clip is only meaningful inside a set — it inherits the set's rig, and validation rule
        /// V06 refuses a clip whose rig is anything else. So "no set assigned" is not a case to
        /// invent a home for; it is a case to disable, with the reason on the button.
        /// </remarks>
        private void RefreshNewClipButton()
        {
            if (newClipButton == null)
            {
                return;
            }
            newClipButton.SetEnabled(clipSet != null);
        }

        private void BindHierarchy()
        {
            hierarchyEmptyLabel = rootVisualElement.Q<Label>("hierarchy-empty-label");

            hierarchyTreeView = rootVisualElement.Q<TreeView>("hierarchy-tree");
            if (hierarchyTreeView == null)
            {
                return;
            }
            hierarchyTreeView.fixedItemHeight = 20f;
            hierarchyTreeView.selectionType = SelectionType.Single;
            hierarchyTreeView.makeItem = MakeHierarchyRow;
            hierarchyTreeView.bindItem = BindHierarchyRow;
            hierarchyTreeView.selectionChanged += OnHierarchySelectionChanged;
        }

        private void BindViewport()
        {
            previewStatusLabel = rootVisualElement.Q<Label>("viewport-status");

            previewImage = rootVisualElement.Q<Image>("viewport-image");
            if (previewImage == null)
            {
                return;
            }
            previewImage.scaleMode = ScaleMode.ScaleToFit;
            previewImage.RegisterCallback<PointerDownEvent>(OnPreviewPointerDown);
            previewImage.RegisterCallback<PointerMoveEvent>(OnPreviewPointerMove);
            previewImage.RegisterCallback<PointerUpEvent>(OnPreviewPointerUp);
            previewImage.RegisterCallback<WheelEvent>(OnPreviewWheel);
        }

        private void BindInspector()
        {
            inspectorPane = rootVisualElement.Q<ScrollView>("inspector-content");
        }

        private void BindTimeline()
        {
            statusLabel = rootVisualElement.Q<Label>("timeline-status");
            trackHeaderColumn = rootVisualElement.Q<VisualElement>("track-header-column");
            laneColumn = rootVisualElement.Q<VisualElement>("lane-column");

            // The lane stack owns keyboard focus: shortcuts registered here cannot swallow
            // keystrokes meant for the inspector's own text fields.
            laneStack = rootVisualElement.Q<VisualElement>("lane-stack");
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

            playhead = new PlayheadElement();
            laneStack.Add(playhead);
        }

        // -------------------------------------------------------------------------------------
        // Split persistence
        // -------------------------------------------------------------------------------------

        /// <summary>
        /// Restores each split's stored position and keeps storing it as the user drags.
        /// </summary>
        /// <remarks>
        /// The fixed pane of each split is resolved by name rather than through
        /// <c>TwoPaneSplitView.fixedPane</c>, which is only populated once the split has laid itself
        /// out — and this runs before first layout, which is the only time the initial dimension can
        /// still be set.
        /// </remarks>
        private void BindSplits()
        {
            // The timeline's default is a proportion, not a pixel count: "about a quarter" only
            // lands on a quarter at one window height, and a window opened for the first time has
            // not been laid out yet, so its height is not even knowable here.
            BindSplit("Vertical", "dock-vertical", "timeline-pane", 220f, 0.25f);
            BindSplit("Columns", "dock-columns", "dock-left", 240f, 0f);
            BindSplit("LeftColumn", "dock-left", "clip-list-pane", 150f, 0f);
            BindSplit("Inspector", "dock-right", "inspector-pane", 280f, 0f);
        }

        private void BindSplit(
            string prefsKeySuffix, string splitName, string fixedPaneName,
            float fallbackDimension, float firstRunProportion)
        {
            TwoPaneSplitView splitView = rootVisualElement.Q<TwoPaneSplitView>(splitName);
            VisualElement fixedPane = rootVisualElement.Q<VisualElement>(fixedPaneName);
            if (splitView == null || fixedPane == null)
            {
                return;
            }

            string prefsKey = SplitPrefsPrefix + prefsKeySuffix;
            bool isHorizontal = splitView.orientation == TwoPaneSplitViewOrientation.Horizontal;
            bool hasStoredDimension = EditorPrefs.HasKey(prefsKey);
            float initialDimension = Mathf.Max(
                MinimumSplitDimension, EditorPrefs.GetFloat(prefsKey, fallbackDimension));

            // Set before first layout, which is when the split reads it. Restoring through the
            // control's own property rather than by writing the pane's style is what keeps the two
            // from fighting: the split re-applies its initial dimension during init, so a style
            // written first is simply overwritten a frame later.
            splitView.fixedPaneInitialDimension = initialDimension;
            persistedSplitDimensions[prefsKey] = initialDimension;

            if (!hasStoredDimension && firstRunProportion > 0f)
            {
                pendingProportionalSplitKeys.Add(prefsKey);
                ScheduleFirstRunProportion(prefsKey, splitView, isHorizontal, firstRunProportion);
            }

            // On the fixed pane, not on the split: dragging the divider resizes the pane, and the
            // split's own rect does not change at all.
            fixedPane.RegisterCallback<GeometryChangedEvent>(
                geometryEvent => OnSplitPaneGeometryChanged(prefsKey, fixedPane, isHorizontal));
        }

        /// <summary>
        /// Sizes a never-before-opened split to a fraction of itself, once it knows how big it is.
        /// </summary>
        /// <remarks>
        /// Deferred to the split's first layout because that is the earliest moment its dimension
        /// exists — <c>position</c> in <see cref="CreateGUI"/> is whatever the window last was, which
        /// for a first open is the default rect and not what the user ends up looking at. Applied
        /// through <c>fixedPaneInitialDimension</c> for the reason above, and then stored, so the
        /// proportion decides exactly once and every open after that restores a remembered position.
        /// </remarks>
        private void ScheduleFirstRunProportion(
            string prefsKey, TwoPaneSplitView splitView, bool isHorizontal, float proportion)
        {
            EventCallback<GeometryChangedEvent> firstLayoutCallback = null;
            firstLayoutCallback = geometryEvent =>
            {
                float splitDimension = isHorizontal
                    ? splitView.resolvedStyle.width
                    : splitView.resolvedStyle.height;
                if (float.IsNaN(splitDimension) || splitDimension < 1f)
                {
                    // Not laid out yet. Staying registered is the point — giving up here is what
                    // would leave the pane on the pixel fallback forever.
                    return;
                }

                splitView.UnregisterCallback<GeometryChangedEvent>(firstLayoutCallback);

                float proportionalDimension = Mathf.Max(
                    MinimumSplitDimension, splitDimension * proportion);
                splitView.fixedPaneInitialDimension = proportionalDimension;

                persistedSplitDimensions[prefsKey] = proportionalDimension;
                EditorPrefs.SetFloat(prefsKey, proportionalDimension);
                pendingProportionalSplitKeys.Remove(prefsKey);
            };
            splitView.RegisterCallback<GeometryChangedEvent>(firstLayoutCallback);
        }

        private void OnSplitPaneGeometryChanged(string prefsKey, VisualElement fixedPane, bool isHorizontal)
        {
            // A split still waiting on its first-run proportion is laid out at the pixel fallback,
            // which is not a position the user chose and must not be stored as one.
            if (pendingProportionalSplitKeys.Contains(prefsKey))
            {
                return;
            }

            float currentDimension = isHorizontal
                ? fixedPane.resolvedStyle.width
                : fixedPane.resolvedStyle.height;
            if (float.IsNaN(currentDimension) || currentDimension < MinimumSplitDimension)
            {
                return;
            }

            // Deduplicated because geometry events also fire on every window resize, and writing an
            // unchanged value to EditorPrefs on each one is a registry write per frame of a drag.
            float lastPersistedDimension;
            if (persistedSplitDimensions.TryGetValue(prefsKey, out lastPersistedDimension)
                && Mathf.Abs(lastPersistedDimension - currentDimension) < 0.5f)
            {
                return;
            }

            persistedSplitDimensions[prefsKey] = currentDimension;
            EditorPrefs.SetFloat(prefsKey, currentDimension);
        }

        // -------------------------------------------------------------------------------------
        // Viewport gestures
        // -------------------------------------------------------------------------------------

        /// <summary>
        /// Starts an orbit and casts the pick ray, from the exact position of the press.
        /// </summary>
        /// <remarks>
        /// The ray is cast here, not on release, so it uses the position the user aimed at rather
        /// than wherever the pointer drifted to before the button came up. What it finds is only
        /// <em>applied</em> on release, and only if this turned out to be a click rather than an
        /// orbit — see <see cref="OnPreviewPointerUp"/>.
        /// </remarks>
        private void OnPreviewPointerDown(PointerDownEvent pointerEvent)
        {
            isPickPending = false;

            // Double click reframes. With the camera persisting across every selection change, an
            // orbit that wandered off the rig would otherwise have no way back.
            if (pointerEvent.clickCount >= 2 && previewController != null)
            {
                previewController.ResetView();
                return;
            }
            previewImage.CapturePointer(pointerEvent.pointerId);

            if (previewController == null)
            {
                return;
            }

            pickPressPosition = pointerEvent.localPosition;
            isPickCycleRequested = pointerEvent.altKey || pointerEvent.shiftKey;
            isPickPending = true;

            Rect viewportRect = previewImage.contentRect;
            if (viewportRect.width < 1f || viewportRect.height < 1f)
            {
                pickCandidates.Clear();
                return;
            }

            // UI Toolkit's y runs down from the top; a viewport's runs up from the bottom. The
            // rendered texture is created at exactly this rect's size, so ScaleToFit neither crops
            // nor letterboxes and no further mapping is needed.
            Vector2 viewportPoint = new Vector2(
                pickPressPosition.x / viewportRect.width,
                1f - pickPressPosition.y / viewportRect.height);

            previewController.CollectPickHits(
                viewportPoint, viewportRect.width / viewportRect.height, pickCandidates);
        }

        private void OnPreviewPointerMove(PointerMoveEvent moveEvent)
        {
            if (!previewImage.HasPointerCapture(moveEvent.pointerId) || previewController == null)
            {
                return;
            }
            previewController.Orbit(moveEvent.deltaPosition);
        }

        /// <summary>
        /// Ends the orbit and, if the pointer never really moved, applies the pick.
        /// </summary>
        /// <remarks>
        /// Selecting on press would mean every orbit also reselected whatever the camera happened to
        /// start over. Committing on release, only within a few pixels of the press, is what lets
        /// one button both orbit and select without the two fighting.
        /// </remarks>
        private void OnPreviewPointerUp(PointerUpEvent upEvent)
        {
            previewImage.ReleasePointer(upEvent.pointerId);

            if (!isPickPending)
            {
                return;
            }
            isPickPending = false;

            Vector2 travel = (Vector2)upEvent.localPosition - pickPressPosition;
            if (travel.sqrMagnitude > ClickMovementToleranceSquared)
            {
                return;
            }

            ApplyViewportPick();
        }

        /// <summary>
        /// Selects whichever of the press's hits is current, cycling on a modified click.
        /// </summary>
        /// <remarks>
        /// The cycle advances only when the same click lands on the same set of candidates again;
        /// anything else resets to the nearest. Otherwise a modified click somewhere new would open
        /// on whatever ordinal the last one left behind, which reads as the viewport selecting at
        /// random.
        /// </remarks>
        private void ApplyViewportPick()
        {
            if (pickCandidates.Count == 0)
            {
                previousPickCandidates.Clear();
                ClearHierarchySelection();
                return;
            }

            if (isPickCycleRequested && CandidatesMatchPreviousPick())
            {
                pickCycleIndex = (pickCycleIndex + 1) % pickCandidates.Count;
            }
            else
            {
                pickCycleIndex = 0;
            }

            previousPickCandidates.Clear();
            for (int hitIndex = 0; hitIndex < pickCandidates.Count; hitIndex++)
            {
                previousPickCandidates.Add(pickCandidates[hitIndex].pickedTransform);
            }

            SelectHierarchyTransform(pickCandidates[pickCycleIndex].pickedTransform);
        }

        private bool CandidatesMatchPreviousPick()
        {
            if (previousPickCandidates.Count != pickCandidates.Count)
            {
                return false;
            }
            for (int hitIndex = 0; hitIndex < pickCandidates.Count; hitIndex++)
            {
                if (previousPickCandidates[hitIndex] != pickCandidates[hitIndex].pickedTransform)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Selects a transform of the previewed rig by driving the tree, not by bypassing it.
        /// </summary>
        /// <remarks>
        /// Routing a viewport click through <c>SetSelectionById</c> makes it fire the tree's own
        /// selection-changed handler, so clicking in the viewport and clicking in the tree run the
        /// same code and cannot end up meaning different things. That is the whole of the
        /// bidirectional sync: the tree is the one place selection is decided.
        /// </remarks>
        private void SelectHierarchyTransform(Transform pickedTransform)
        {
            if (hierarchyTreeView == null || previewController == null)
            {
                return;
            }

            int itemId = previewController.GetHierarchyIndex(pickedTransform);
            if (itemId < 0)
            {
                return;
            }

            hierarchyTreeView.SetSelectionById(itemId);

            // Every item is expanded when the tree is built, so the row exists to be scrolled to.
            hierarchyTreeView.ScrollToItemById(itemId);
        }

        private void OnPreviewWheel(WheelEvent wheelEvent)
        {
            if (previewController == null)
            {
                return;
            }
            previewController.Zoom(wheelEvent.delta.y * 0.3f);
            wheelEvent.StopPropagation();
        }

        private void OnSkinnedSourceChanged(ChangeEvent<Object> changeEvent)
        {
            if (previewController != null)
            {
                previewController.SetSkinnedSource(changeEvent.newValue as GameObject);
            }
            // Cleared before the tree is rebuilt: the old instance's transforms are gone, so the
            // held index now points into a hierarchy that no longer exists.
            SelectHierarchyItem(-1, null);
            previousPickCandidates.Clear();
            pickCandidates.Clear();
            RebuildHierarchy();
            RebuildInspector();
        }

        // -------------------------------------------------------------------------------------
        // Clip list
        // -------------------------------------------------------------------------------------

        private static VisualElement MakeClipRow()
        {
            Label label = new Label();
            label.AddToClassList(ClipRowUssClassName);
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
        // Prefab hierarchy. The rig's transforms, as the pick list for bone tracks.
        // -------------------------------------------------------------------------------------

        /// <summary>
        /// Rebuilds the hierarchy from the assigned rigged prefab.
        /// </summary>
        /// <remarks>
        /// A tree rather than the sorted name list this replaced, because a skeleton read as a flat
        /// alphabetical list tells you nothing about which bone you are picking — two bones named
        /// <c>Hand</c> and <c>Hand.001</c> are distinguishable only by where they sit. Tracks still
        /// bind by name, so the tree is a picker, not a new binding model.
        /// </remarks>
        private void RebuildHierarchy()
        {
            if (hierarchyTreeView == null)
            {
                return;
            }

            List<TreeViewItemData<string>> rootItems = new List<TreeViewItemData<string>>();

            // Built from the preview's live instance, not from the prefab asset. The viewport picks
            // transforms out of that instance, so sourcing the tree from it means a picked object is
            // literally a node of the tree's own source — no mapping between two hierarchies that
            // have to be kept in agreement.
            Transform hierarchyRoot = previewController != null ? previewController.HierarchyRoot : null;
            if (hierarchyRoot != null)
            {
                rootItems.Add(BuildHierarchyItem(hierarchyRoot));
            }

            hierarchyTreeView.SetRootItems(rootItems);
            hierarchyTreeView.Rebuild();
            if (rootItems.Count > 0)
            {
                // Expanded up front so any id the viewport picks has a visible row to select and
                // scroll to, without the window having to walk up and expand ancestors first.
                hierarchyTreeView.ExpandAll();
            }

            if (hierarchyEmptyLabel != null)
            {
                hierarchyEmptyLabel.EnableInClassList(HiddenUssClassName, rootItems.Count > 0);
            }
        }

        /// <summary>
        /// Builds one tree item, taking its id from the preview rather than from a counter here.
        /// </summary>
        /// <remarks>
        /// The id <em>is</em> the preview's index for that transform. Numbering them here instead
        /// would mean two independent walks that agree only as long as nobody changes one of them.
        /// </remarks>
        private TreeViewItemData<string> BuildHierarchyItem(Transform transformNode)
        {
            List<TreeViewItemData<string>> childItems = new List<TreeViewItemData<string>>();
            for (int childIndex = 0; childIndex < transformNode.childCount; childIndex++)
            {
                childItems.Add(BuildHierarchyItem(transformNode.GetChild(childIndex)));
            }

            return new TreeViewItemData<string>(
                previewController.GetHierarchyIndex(transformNode), transformNode.name, childItems);
        }

        private static VisualElement MakeHierarchyRow()
        {
            Label label = new Label();
            label.AddToClassList(HierarchyRowUssClassName);
            return label;
        }

        private void BindHierarchyRow(VisualElement element, int index)
        {
            Label label = element as Label;
            if (label == null)
            {
                return;
            }
            string boneName = hierarchyTreeView.GetItemDataForIndex<string>(index);
            label.text = boneName;

            // Bold marks a bone the selected clip already animates, so the tree doubles as the
            // answer to "what does this clip actually touch?".
            label.EnableInClassList(AnimatedBoneUssClassName, FindBoneTrackIndex(boneName) >= 0);
        }

        /// <summary>
        /// The single place a hierarchy selection takes effect, whichever surface caused it.
        /// </summary>
        /// <remarks>
        /// A viewport click reaches this by setting the tree's selection rather than by doing its
        /// own thing, so "clicked in the tree" and "clicked in the viewport" cannot drift into
        /// meaning two different things.
        /// </remarks>
        private void OnHierarchySelectionChanged(IEnumerable<object> selection)
        {
            string boneName = null;
            foreach (object item in selection)
            {
                boneName = item as string;
                break;
            }

            int itemId = -1;
            foreach (int selectedIndex in hierarchyTreeView.selectedIndices)
            {
                itemId = hierarchyTreeView.GetIdForIndex(selectedIndex);
                break;
            }

            // Key selection and hierarchy selection are one selection with two sources: showing a
            // key's values under a heading naming a different bone would be a lie about what the
            // fields edit.
            selectedKeys.Clear();
            SelectHierarchyItem(itemId, boneName);
            RepaintLanes();
            RebuildInspector();
        }

        /// <summary>Points the viewport outline and the inspector at one transform, or at nothing.</summary>
        private void SelectHierarchyItem(int itemId, string boneName)
        {
            selectedHierarchyItemId = itemId;
            selectedBoneName = boneName;
            if (previewController != null)
            {
                previewController.SetSelectedHierarchyIndex(itemId);
            }
        }

        /// <summary>Deselects everywhere at once — tree, viewport outline and inspector.</summary>
        private void ClearHierarchySelection()
        {
            if (selectedHierarchyItemId < 0)
            {
                return;
            }

            // Without notifying, because the clearing this would trigger is exactly what the rest of
            // this method does — and re-entering it would clear a key selection that a viewport
            // click on empty space has no business touching.
            if (hierarchyTreeView != null)
            {
                hierarchyTreeView.SetSelectionWithoutNotify(new int[0]);
            }
            SelectHierarchyItem(-1, null);
            RebuildInspector();
        }

        private int FindBoneTrackIndex(string boneName)
        {
            if (selectedClip == null || selectedClip.boneTracks == null || string.IsNullOrEmpty(boneName))
            {
                return -1;
            }
            for (int trackIndex = 0; trackIndex < selectedClip.boneTracks.Count; trackIndex++)
            {
                BoneTrack track = selectedClip.boneTracks[trackIndex];
                if (track != null && track.boneName == boneName)
                {
                    return trackIndex;
                }
            }
            return -1;
        }

        // -------------------------------------------------------------------------------------
        // Selection plumbing
        // -------------------------------------------------------------------------------------

        private void OnClipSetChanged(ChangeEvent<Object> changeEvent)
        {
            clipSet = changeEvent.newValue as ClipSetAsset;
            SelectClip(null);

            RefreshClipList();
            RefreshNewClipButton();

            if (previewController != null)
            {
                previewController.SetClipSet(clipSet);
            }
            if (validationBadge != null)
            {
                validationBadge.Refresh(clipSet);
            }
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
            double now = EditorApplication.timeSinceStartup;
            double elapsed = now - lastTickTime;
            if (elapsed < 1.0 / PlaybackHertz)
            {
                return;
            }
            lastTickTime = now;

            if (isPlaying && selectedClip != null)
            {
                // Advance in seconds then convert, so a clip's duration sets playback speed exactly
                // the way it does at runtime rather than every clip taking the same wall time.
                float duration = Mathf.Max(ClipAsset.MinimumDuration, selectedClip.duration);
                float advanced = playheadTime + (float)elapsed / duration;
                SetPlayheadTime(advanced - Mathf.Floor(advanced));
            }

            // The preview updates every tick, not only while playing — scrubbing a paused clip is
            // the authoring loop this window exists for.
            UpdatePreview(now);
        }

        /// <summary>Marks the preview's registry stale; the tick rebuilds it after a short delay.</summary>
        /// <remarks>
        /// Debounced rather than immediate because a drag mutates the clip dozens of times a second
        /// and each rebuild re-canonicalises the whole set. Collapsing a gesture into one rebuild is
        /// the difference between a live preview and a stuttering one.
        /// </remarks>
        private void MarkPreviewDirty()
        {
            previewRegistryDirty = true;
            previewDirtiedAt = EditorApplication.timeSinceStartup;
        }

        /// <summary>
        /// Advances the viewport one frame.
        /// </summary>
        /// <remarks>
        /// <strong>Every early exit here is about the pose, not about the picture.</strong> With no
        /// clip, no registry or a clip the registry does not contain, the mirror simply is not
        /// re-posed — the render still runs and still returns the scene, which at worst is the
        /// reference grid. The old version returned early and blanked <c>previewImage.image</c> in
        /// each of those cases, which is why the window looked dead until something was selected.
        /// </remarks>
        private void UpdatePreview(double now)
        {
            if (previewController == null || previewImage == null)
            {
                return;
            }

            if (previewRegistryDirty && now - previewDirtiedAt > 0.25)
            {
                previewRegistryDirty = false;
                previewController.Refresh();

                // Revalidated on the same debounced beat as the preview rebuild, for the same
                // reason: a full set validation walks every key of every clip, so running it per
                // repaint would make a large set's window crawl.
                if (validationBadge != null)
                {
                    validationBadge.Refresh(clipSet);
                }
            }

            string viewportStatus = previewController.StatusMessage;
            if (selectedClip != null && previewController.HasRegistry
                && !previewController.SamplePose(selectedClip.Id.Value, playheadTime))
            {
                viewportStatus = "Clip is not in the built registry — is it listed in the set?";
            }
            else if (selectedClip == null && clipSet != null && string.IsNullOrEmpty(viewportStatus))
            {
                // Only when the controller has nothing of its own to say: a rig with no targets or a
                // set that failed to build is the more useful message, and this must not bury it.
                viewportStatus = "Select a clip to pose the rig.";
            }
            if (previewStatusLabel != null)
            {
                previewStatusLabel.text = viewportStatus;
            }

            Rect previewRect = previewImage.contentRect;
            if (float.IsNaN(previewRect.width) || previewRect.width < 1f || previewRect.height < 1f)
            {
                // Layout has not run yet; rendering into a zero rect throws inside the utility.
                return;
            }

            Texture renderedTexture = previewController.Render(
                Mathf.RoundToInt(previewRect.width), Mathf.RoundToInt(previewRect.height));
            if (renderedTexture != null)
            {
                previewImage.image = renderedTexture;
                previewImage.MarkDirtyRepaint();
            }
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

            // The hierarchy's bold marks track which clip is selected, so it is refreshed with the
            // timeline rather than only when the rig changes.
            if (hierarchyTreeView != null)
            {
                hierarchyTreeView.RefreshItems();
            }

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

            // Bone rows sit between the part rows and the events, so a character's skeleton and its
            // cutout parts read as one stack — which is the entire point of authoring both here
            // rather than in two applications (amendment A42).
            List<BoneTrack> boneTracks = selectedClip.boneTracks;
            for (int trackIndex = 0; boneTracks != null && trackIndex < boneTracks.Count; trackIndex++)
            {
                BoneTrack track = boneTracks[trackIndex];
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
                    "B " + (string.IsNullOrEmpty(track.boneName) ? "<unnamed bone>" : track.boneName),
                    TimelineTrackKind.Bone, trackIndex, times, rowIndex++);
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
            header.AddToClassList(TrackHeaderUssClassName);
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

            SyncBoneSelectionToKey(address);

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

        /// <summary>
        /// Moves the viewport outline and the tree onto the bone whose key was grabbed.
        /// </summary>
        /// <remarks>
        /// The third direction of the same sync: the timeline is as much a selection surface as the
        /// tree and the viewport. The tree's selection is set <em>without</em> notifying here,
        /// because the notification clears the key selection — the click would deselect the very key
        /// that caused it.
        /// </remarks>
        private void SyncBoneSelectionToKey(KeyAddress address)
        {
            string boneName = null;
            if (address.trackKind == TimelineTrackKind.Bone
                && selectedClip != null
                && selectedClip.boneTracks != null
                && address.trackIndex < selectedClip.boneTracks.Count)
            {
                BoneTrack track = selectedClip.boneTracks[address.trackIndex];
                boneName = track != null ? track.boneName : null;
            }

            int itemId = -1;
            if (!string.IsNullOrEmpty(boneName) && previewController != null)
            {
                itemId = previewController.FindHierarchyIndexByName(boneName);
            }
            SelectHierarchyItem(itemId, boneName);

            if (hierarchyTreeView == null)
            {
                return;
            }
            if (itemId >= 0)
            {
                hierarchyTreeView.SetSelectionByIdWithoutNotify(new int[] { itemId });
                hierarchyTreeView.ScrollToItemById(itemId);
            }
            else
            {
                hierarchyTreeView.SetSelectionWithoutNotify(new int[0]);
            }
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
            MarkPreviewDirty();
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
                    case TimelineTrackKind.Bone:
                        if (selectedClip.boneTracks != null
                            && address.trackIndex < selectedClip.boneTracks.Count
                            && address.keyIndex < selectedClip.boneTracks[address.trackIndex].keys.Count)
                        {
                            selectedClip.boneTracks[address.trackIndex].keys.RemoveAt(address.keyIndex);
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
                case TimelineTrackKind.Bone:
                    return selectedClip.boneTracks[address.trackIndex].keys[address.keyIndex].normalizedTime;
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
                case TimelineTrackKind.Bone:
                {
                    BoneTrack track = selectedClip.boneTracks[address.trackIndex];
                    BoneKey key = track.keys[address.keyIndex];
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
                case TimelineTrackKind.Bone:
                {
                    List<BoneKey> keys = selectedClip.boneTracks[trackIndex].keys;

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
                case TimelineTrackKind.Bone:
                    selectedClip.boneTracks[trackIndex].keys.Sort(CompareBoneKeys);
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
            for (int trackIndex = 0;
                selectedClip.boneTracks != null && trackIndex < selectedClip.boneTracks.Count;
                trackIndex++)
            {
                selectedClip.boneTracks[trackIndex].keys.Sort(CompareBoneKeys);
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

        private static int CompareBoneKeys(BoneKey first, BoneKey second)
        {
            return first.normalizedTime.CompareTo(second.normalizedTime);
        }

        private static int CompareEventMarkers(EventMarker first, EventMarker second)
        {
            return first.normalizedTime.CompareTo(second.normalizedTime);
        }

        // -------------------------------------------------------------------------------------
        // Inspector. Bound fields get undo, dirtying and prefab overrides for free (section 7.4),
        // so nothing here hand-rolls an edit path.
        // -------------------------------------------------------------------------------------

        /// <summary>
        /// Fills the inspector for whatever is selected: a key, a bone, or the clip itself.
        /// </summary>
        /// <remarks>
        /// The three cases are ordered by how specific they are, and the last of them is the
        /// fallback — the pane is never empty, because "nothing here" and "the window is broken"
        /// look identical to someone who has just opened it.
        /// </remarks>
        private void RebuildInspector()
        {
            if (inspectorPane == null)
            {
                return;
            }
            inspectorPane.Clear();

            if (selectedKeys.Count > 0 && BuildKeyInspector())
            {
                return;
            }
            if (!string.IsNullOrEmpty(selectedBoneName))
            {
                BuildBoneInspector();
                return;
            }
            BuildClipInspector();
        }

        /// <summary>Returns false when the addressed key has gone, so the caller can fall through.</summary>
        private bool BuildKeyInspector()
        {
            if (selectedClip == null || clipSerializedObject == null)
            {
                return false;
            }
            clipSerializedObject.Update();

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
                return false;
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
            return true;
        }

        /// <summary>
        /// The inspector for a transform picked in the hierarchy.
        /// </summary>
        /// <remarks>
        /// Adding the track happens here rather than in the clip inspector because this is where the
        /// bone is named. A name typed by hand that resolves to nothing bakes the bone at rest,
        /// which reads as an animation that simply does not play; picking it from the rig's own
        /// hierarchy removes that failure outright.
        /// </remarks>
        private void BuildBoneInspector()
        {
            inspectorPane.Add(MakeHeading(selectedBoneName));

            // The hierarchy lists every transform, not only bones, so the inspector says which kind
            // this one is. What you can usefully do with it depends on the answer: only a skinned
            // bone moves the mesh when a bone track drives it.
            if (previewController != null)
            {
                string description = previewController.DescribeHierarchyItem(selectedHierarchyItemId);
                if (!string.IsNullOrEmpty(description))
                {
                    inspectorPane.Add(MakeHint(description));
                }
            }

            if (selectedClip == null)
            {
                inspectorPane.Add(MakeHint("Select a clip to animate this object."));
                return;
            }

            int boneTrackIndex = FindBoneTrackIndex(selectedBoneName);
            if (boneTrackIndex < 0)
            {
                inspectorPane.Add(MakeHint("No track on this clip animates this bone."));
                inspectorPane.Add(new Button(() => AddBoneTrack(selectedBoneName))
                {
                    text = "Add Bone Track"
                });
                return;
            }

            if (clipSerializedObject == null)
            {
                return;
            }
            clipSerializedObject.Update();

            SerializedProperty tracksProperty = clipSerializedObject.FindProperty("boneTracks");
            if (tracksProperty == null || boneTrackIndex >= tracksProperty.arraySize)
            {
                return;
            }
            inspectorPane.Add(new PropertyField(tracksProperty.GetArrayElementAtIndex(boneTrackIndex)));
            inspectorPane.Bind(clipSerializedObject);
        }

        private void BuildClipInspector()
        {
            if (selectedClip == null || clipSerializedObject == null)
            {
                inspectorPane.Add(MakeHint(clipSet == null
                    ? "Assign a clip set in the toolbar."
                    : "Select a clip to edit its properties."));
                return;
            }
            clipSerializedObject.Update();

            inspectorPane.Add(MakeHeading("Clip"));
            inspectorPane.Add(MakeClipNameField());
            AddBoundField("duration");
            AddBoundField("defaultLoop");
            AddBoundField("rig");
            AddBoneTrackControls();
            inspectorPane.Bind(clipSerializedObject);
        }

        /// <summary>
        /// Clip-level bone-track summary, plus the by-name fallback for a set with no rig assigned.
        /// </summary>
        /// <remarks>
        /// With a rig assigned, the hierarchy pane is the picker and this only points at it — a
        /// second dropdown listing the same bones would be one more thing to keep in sync with the
        /// tree for no gain. Without one there is nothing to pick from, so the typed field remains
        /// the only way to author a bone track, exactly as before.
        /// </remarks>
        private void AddBoneTrackControls()
        {
            inspectorPane.Add(MakeHeading("Bone Tracks"));

            int boneTrackCount = selectedClip.boneTracks != null ? selectedClip.boneTracks.Count : 0;
            inspectorPane.Add(new Label(boneTrackCount.ToString() + " track(s)"));

            bool hasHierarchy = skinnedSourceField != null && skinnedSourceField.value != null;
            if (hasHierarchy)
            {
                inspectorPane.Add(MakeHint("Pick a bone in the Hierarchy pane to add or edit its track."));
                return;
            }

            TextField boneNameField = new TextField("Bone Name");
            boneNameField.tooltip =
                "Assign a rigged prefab in the toolbar to pick from the hierarchy instead. "
                + "Case sensitive — the bake reports a name it cannot resolve.";
            inspectorPane.Add(boneNameField);
            inspectorPane.Add(new Button(() => AddBoneTrack(boneNameField.value))
            {
                text = "Add Bone Track"
            });
        }

        private void AddBoneTrack(string boneName)
        {
            if (selectedClip == null || string.IsNullOrWhiteSpace(boneName))
            {
                return;
            }

            if (selectedClip.boneTracks == null)
            {
                selectedClip.boneTracks = new List<BoneTrack>();
            }

            // One track per bone. Two tracks naming the same bone is a validation error, and the
            // second one would silently lose to whichever the bake applied last — better to refuse
            // it here, where the user can see why. Reported on the timeline's status line rather
            // than the viewport's, which the preview tick overwrites thirty times a second.
            if (FindBoneTrackIndex(boneName) >= 0)
            {
                statusLabel.text = "A bone track for '" + boneName + "' already exists.";
                return;
            }

            BeginUndoGesture("Add Bone Track");
            selectedClip.boneTracks.Add(new BoneTrack
            {
                boneName = boneName,
                keys = new List<BoneKey>()
            });
            EndUndoGesture();

            EditorUtility.SetDirty(selectedClip);
            RebuildTimeline();
        }

        /// <summary>
        /// The clip's asset name, editable in place.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Here rather than only in the Project window because a clip's name is not cosmetic: the
        /// clip set's id-constant generator turns it into a C# identifier, so a set full of
        /// "NewClip 3" produces constants nobody can read. Creating a clip from this window and
        /// having to leave it to give the clip a name is the flow this closes.
        /// </para>
        /// <para>
        /// <c>isDelayed</c> is load-bearing: without it the field commits on every keystroke, and
        /// each commit is a file rename on disk. Typing "Walk" would rename the asset four times and
        /// leave three stale <c>.meta</c> shuffles behind it.
        /// </para>
        /// </remarks>
        private TextField MakeClipNameField()
        {
            TextField nameField = new TextField("Name");
            nameField.isDelayed = true;
            nameField.SetValueWithoutNotify(selectedClip.name);
            nameField.RegisterValueChangedCallback(changeEvent =>
            {
                if (!ClipCreationUtility.RenameClip(selectedClip, changeEvent.newValue))
                {
                    // Refused — an illegal or duplicate name. Put the field back to the truth rather
                    // than leaving it showing a name the asset does not have.
                    nameField.SetValueWithoutNotify(selectedClip != null ? selectedClip.name : string.Empty);
                    return;
                }
                RefreshClipList();
                RebuildTimeline();
            });
            return nameField;
        }

        private static Label MakeHeading(string text)
        {
            Label label = new Label(text);
            label.AddToClassList(HeadingUssClassName);
            return label;
        }

        private static Label MakeHint(string text)
        {
            Label label = new Label(text);
            label.AddToClassList(HintUssClassName);
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
                case TimelineTrackKind.Bone:
                    return FindTrackKeyProperty("boneTracks", address);
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
