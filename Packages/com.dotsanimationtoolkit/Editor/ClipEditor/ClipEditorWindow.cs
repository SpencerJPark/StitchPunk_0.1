// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
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
    public sealed partial class ClipEditorWindow : EditorWindow
    {
        private const float PlaybackHertz = 30f;

        private const string LayoutAssetPath =
            "Packages/com.dotsanimationtoolkit/Editor/ClipEditor/ClipEditorWindow.uxml";

        /// <summary>
        /// Prefix for the persisted split positions. Keyed by window rather than by project on
        /// purpose: a dock layout is a habit of the person, and following them between projects is
        /// the behaviour every other editor window has.
        /// </summary>
        private const string SplitPrefsPrefix = "DotsAnimationToolkit.ClipEditor.Split.";

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
        private const string BillboardRootUssClassName = "clip-editor__hierarchy-row--billboard-root";
        private const string BillboardInheritedUssClassName =
            "clip-editor__hierarchy-row--billboard-inherited";

        /// <summary>
        /// Marks a declared billboard root in the tree. A glyph rather than a texture: the row is a
        /// <c>Label</c>, so a prefix costs no layout change, survives every theme, and cannot go
        /// missing the way a packaged icon can.
        /// </summary>
        private const string BillboardRootGlyph = "◈ ";

        /// <summary>Marks a node inheriting a billboard from an ancestor. Deliberately fainter.</summary>
        private const string BillboardInheritedGlyph = "· ";
        private const string TrackHeaderUssClassName = "clip-editor__track-header";
        private const string TrackHeaderLabelUssClassName = "clip-editor__track-header-label";
        private const string TrackFoldoutUssClassName = "clip-editor__track-foldout";
        private const string ChannelHeaderUssClassName = "clip-editor__channel-header";
        private const string HeadingUssClassName = "clip-editor__heading";
        private const string HintUssClassName = "clip-editor__hint";
        private const string FlipbookTrackUssClassName = "clip-editor__flipbook-track";
        private const string FlipbookKeyUssClassName = "clip-editor__flipbook-key";
        private const string FlipbookResolvedUssClassName = "clip-editor__flipbook-resolved";
        private const string FlipbookInvalidUssClassName = "clip-editor__flipbook-resolved--invalid";
        private const string TransformBlockUssClassName = "clip-editor__transform-block";
        private const string TransformOnKeyUssClassName = "clip-editor__transform-block--on-key";
        private const string TransformInterpolatedUssClassName = "clip-editor__transform-block--interpolated";
        private const string TransformModifiedUssClassName = "clip-editor__transform-block--modified";
        private const string TransformStateChipUssClassName = "clip-editor__transform-state";
        private const string ReconcileRowUssClassName = "clip-editor__reconcile-row";
        private const string ReconcileRowLabelUssClassName = "clip-editor__reconcile-row-label";
        private const string ReconcileRemapUssClassName = "clip-editor__reconcile-remap";
        private const string ViewportFrameRigEditUssClassName =
            "clip-editor__viewport-frame--rig-edit";
        private const string SelectionHeadingUssClassName = "clip-editor__selection-heading";
        private const string SelectionHeadingActiveUssClassName =
            "clip-editor__selection-heading--active";

        private ObjectField clipSetField;
        private ListView clipListView;
        private Button newClipButton;
        private Button deleteClipButton;
        private TreeView hierarchyTreeView;
        private Label hierarchyEmptyLabel;
        private ToolbarToggle snapToggle;
        private ToolbarToggle autoKeyToggle;

        // The held transform edit: a value the user has changed but not written to a key. Kept per
        // selection and dropped when the playhead or the selection moves, because it describes
        // "this part, at this instant" and neither survives the other changing.
        // Gizmo state. The drag records the value it started from and re-derives the whole result
        // each move, rather than accumulating deltas — accumulation drifts, and a drag that ends
        // somewhere the pointer is not is the symptom.
        private GizmoMode gizmoMode = GizmoMode.Move;
        private GizmoHandle activeGizmoHandle = GizmoHandle.None;
        private float3 gizmoDragStartPosition;
        private float3 gizmoDragStartRotation;
        private float3 gizmoDragStartScale;
        private float gizmoDragStartParameter;

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

        private bool hasPendingTransformEdit;
        private uint pendingTransformTargetId;
        private float3 pendingPosition;
        private float3 pendingRotationDegrees;
        private float3 pendingScale;
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
        /// The key the inspector edits: the one most recently clicked, not an arbitrary member of
        /// the selection.
        /// </summary>
        /// <remarks>
        /// The inspector used to take "the last" address by iterating <see cref="selectedKeys"/> and
        /// keeping the final value. A <c>HashSet</c> has no order, so that was whichever key the
        /// hash buckets happened to yield last — with one key selected it looked right, and the
        /// moment a second was added the panel showed a key the user had not clicked. Selection is a
        /// set; the ACTIVE element is not, and it has to be stored separately.
        /// </remarks>
        private KeyAddress activeKey;
        private bool hasActiveKey;

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
        private int selectedHierarchyItemId = NothingSelectedItemId;

        /// <summary>
        /// What the hierarchy pane lists: the rig's parts, and the previewed prefab's transforms.
        /// </summary>
        /// <remarks>
        /// Two genuinely different things share the tree because they are the two kinds of thing a
        /// clip animates — a rig target carries transform and flipbook tracks, a bone carries bone
        /// tracks — and an author picking "what am I keying" should not have to know which pane each
        /// lives in. The kind is carried on the item rather than inferred from the id, so adding a
        /// third kind later does not mean re-encoding the id space.
        /// </remarks>
        /// <summary>What a hierarchy row stands for.</summary>
        /// <remarks>
        /// An enum rather than a pair of booleans, because most of the code that cares asks "is this
        /// a prefab transform" by writing <c>!isRigTarget</c>. Adding sockets as a second flag would
        /// have made every one of those sites quietly wrong about the new kind, and wrong in the
        /// direction that offers bone-track buttons for a socket.
        /// </remarks>
        private enum HierarchyItemKind
        {
            /// <summary>A transform of the previewed prefab.</summary>
            PrefabTransform,

            /// <summary>A part the rig declares, which transform and flipbook tracks bind to.</summary>
            RigTarget,

            /// <summary>An attachment point the rig declares.</summary>
            Socket
        }

        private sealed class HierarchyItem
        {
            public HierarchyItemKind kind;
            public string displayName;

            /// <summary>Set for a rig target.</summary>
            public uint targetId;

            /// <summary>Set for a socket.</summary>
            public uint socketId;

            /// <summary>Set for a previewed transform: its index in the preview's hierarchy.</summary>
            public int previewIndex;
        }

        /// <summary>
        /// Tree ids for rig targets, which must not collide with the preview hierarchy indices that
        /// id the transform rows. Preview indices are always ≥ 0, so targets take the negatives —
        /// no threshold constant to outgrow, unlike an offset scheme.
        /// </summary>
        private const int RigTargetItemIdBase = -2;

        /// <summary>
        /// Tree ids for socket rows, far enough below the rig-target range never to meet it.
        /// </summary>
        /// <remarks>
        /// A million targets below zero is not a rig anyone will author, so the two negative ranges
        /// are disjoint in practice without either needing to know the other's size. The alternative
        /// — packing a kind into the high bits — would make every id unreadable in a debugger for
        /// the sake of a collision that cannot happen.
        /// </remarks>
        private const int SocketItemIdBase = -1000000;

        /// <summary>
        /// Not −1: that is a legitimate tree id under <see cref="RigTargetItemIdBase"/>'s scheme,
        /// and overloading it would make the first rig target indistinguishable from no selection.
        /// </summary>
        private const int NothingSelectedItemId = int.MinValue;

        private readonly Dictionary<int, HierarchyItem> hierarchyItemsById =
            new Dictionary<int, HierarchyItem>();

        /// <summary>
        /// Every selected row, in tree order. The timeline shows the tracks of all of them and the
        /// inspector gives each its own labelled block.
        /// </summary>
        private readonly List<HierarchyItem> selectedHierarchyItems = new List<HierarchyItem>();

        /// <summary>The row the gizmo and the viewport outline follow, of the several selected.</summary>
        private int activeHierarchyItemId = NothingSelectedItemId;

        /// <summary>
        /// The previous selection, so the next change can be diffed to find the row just added.
        /// </summary>
        private readonly HashSet<int> previouslySelectedItemIds = new HashSet<int>();

        /// <summary>Set while a selection change is being applied, to stop it re-entering itself.</summary>
        private bool isHandlingHierarchySelection;

        private Button editPrefabButton;
        private Button billboardRootButton;
        private ToolbarToggle rigEditToggle;
        private VisualElement reconcilePanel;
        private ScrollView reconcileList;
        private Label reconcileTitle;
        private VisualElement viewportFrame;
        private Label rigEditBanner;

        /// <summary>
        /// The selected transform's name, which is the identity bone <em>tracks</em> bind by.
        /// Carried alongside the index rather than derived from it so the bake's contract and the
        /// window's selection stay separate things.
        /// </summary>
        private string selectedBoneName;

        /// <summary>The selected rig target, or 0 when the selection is a bone or nothing.</summary>
        private uint selectedTargetId;

        /// <summary>The selected socket, or 0 when the selection is anything else.</summary>
        private uint selectedSocketId;

        // Reused per inspector rebuild rather than allocated, since a rebuild happens on every
        // scrub tick that changes the displayed value.
        private readonly List<SpriteTrack> flipbookTracks = new List<SpriteTrack>();
        private readonly List<int> flipbookTrackIndices = new List<int>();

        // Reused per timeline rebuild for the same reason: the Events lane is rebuilt whenever any
        // track is, which is on every structural edit.
        private readonly List<float> eventWindowLengths = new List<float>();

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

        /// <summary>
        /// Opens the Clip Editor, docked beside the Scene view when it is being created.
        /// </summary>
        /// <remarks>
        /// The dock neighbour is a request, not a command — Unity honours it only when the window
        /// is created, and an existing window keeps wherever the user put it. That is the right
        /// division: this decides the default, the user decides thereafter.
        /// </remarks>
        [MenuItem("Window/DOTS Animation Toolkit/Clip Editor")]
        public static void ShowWindow()
        {
            ClipEditorWindow window = GetWindow<ClipEditorWindow>(
                "Clip Editor", ClipEditorDocking.PreferredDockNeighbours());
            window.titleContent = new GUIContent("Clip Editor");
            window.minSize = new Vector2(820f, 460f);
        }

        /// <summary>
        /// Re-creates a floating window as a docked one, carrying what makes it the same window.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Unity has no API to dock a window that already exists, so the only route is to close it
        /// and reopen it asking for a dock neighbour. The close is deferred: this is called from
        /// inside the window's own event handling, and destroying the instance mid-callback is how
        /// a null reference gets thrown at a stack frame nobody will recognise.
        /// </para>
        /// <para>
        /// One-time, in practice. Once docked the window stays docked, so the cost is paid on the
        /// first trip into prefab mode and never again.
        /// </para>
        /// </remarks>
        private void RedockBesideSceneView(System.Action afterDocked)
        {
            ClipEditorDocking.CarriedState state = new ClipEditorDocking.CarriedState
            {
                clipSet = clipSet,
                selectedClip = selectedClip,
                skinnedSource = LoadedPrefab,
                playheadTime = playheadTime,
                rigEditMode = IsRigEditMode
            };
            for (int itemIndex = 0; itemIndex < selectedHierarchyItems.Count; itemIndex++)
            {
                state.selectedNames.Add(selectedHierarchyItems[itemIndex].displayName);
            }
            ClipEditorDocking.SetPendingState(state);

            // Close and reopen in one deferred step, never across two. Deferred so the instance is
            // not destroyed inside its own event handling; together so there is never a tick in
            // which the window is closed and its replacement merely queued — an editor that stopped
            // ticking in between would leave the user with no window at all.
            EditorApplication.delayCall += () =>
            {
                CloseExistingWindow();
                ShowWindow();
                if (afterDocked != null)
                {
                    afterDocked();
                }
            };
        }

        private static void CloseExistingWindow()
        {
            ClipEditorWindow[] openWindows = Resources.FindObjectsOfTypeAll<ClipEditorWindow>();
            for (int windowIndex = 0; windowIndex < openWindows.Length; windowIndex++)
            {
                if (openWindows[windowIndex] != null)
                {
                    openWindows[windowIndex].Close();
                }
            }
        }

        /// <summary>Adopts the state carried across a re-dock, if there is any waiting.</summary>
        private void AdoptCarriedState()
        {
            ClipEditorDocking.CarriedState state = ClipEditorDocking.ConsumePendingState();
            if (state == null)
            {
                return;
            }

            if (clipSetField != null)
            {
                clipSetField.value = state.clipSet;
            }
            if (skinnedSourceField != null)
            {
                skinnedSourceField.value = state.skinnedSource;
            }
            if (state.selectedClip is ClipAsset carriedClip)
            {
                SelectClip(carriedClip);
            }
            if (rigEditToggle != null)
            {
                rigEditToggle.SetValueWithoutNotify(state.rigEditMode);
                ApplyRigEditChrome();
            }

            // Reuses the round-trip restore, because it is the same problem: put the playhead and
            // the selection back on a tree that has just been rebuilt from scratch.
            roundTripPlayheadTime = state.playheadTime;
            roundTripSelectedNames.Clear();
            roundTripSelectedNames.AddRange(state.selectedNames);
            hasRoundTripState = true;
            RestoreRoundTripState();
        }

        private void OnEnable()
        {
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.update += OnEditorTick;

            // Both ends of the round trip. Saving is the interesting one -- a user can save several
            // times without leaving prefab mode, and each save is a structure this window may now
            // disagree with. Closing catches the case where they never saved but Unity reverted.
            PrefabStage.prefabSaved += OnPrefabStageSaved;
            PrefabStage.prefabStageClosing += OnPrefabStageClosing;

            previewController = new ClipPreviewController();
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorApplication.update -= OnEditorTick;
            PrefabStage.prefabSaved -= OnPrefabStageSaved;
            PrefabStage.prefabStageClosing -= OnPrefabStageClosing;

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
            hasActiveKey = false;
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

            RefreshClipActionButtons();
            RebuildHierarchy();
            RebuildTimeline();

            // Last, because it drives the fields and the tree this method has only just finished
            // building. Does nothing unless the window was reopened to be docked.
            AdoptCarriedState();
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

            ToolbarButton newClipSetButton = rootVisualElement.Q<ToolbarButton>("new-clip-set-button");
            if (newClipSetButton != null)
            {
                newClipSetButton.clicked += CreateClipSet;
                newClipSetButton.tooltip = "Create a new clip set asset and load it into this window.";
            }

            // Play, the playhead readout and the frame grid all moved to the transport bar above
            // the timeline. Snap stays here: it governs what a DRAG writes, not when anything is.
            snapToggle = rootVisualElement.Q<ToolbarToggle>("snap-toggle");
            BindTransportBar();
            BindTimelineView();

            ToolbarToggle billboardPreviewToggle =
                rootVisualElement.Q<ToolbarToggle>("billboard-preview-toggle");
            if (billboardPreviewToggle != null)
            {
                billboardPreviewToggle.tooltip =
                    "Show billboarding in the viewport, exactly as the game resolves it. "
                    + "Turn it off to inspect the authored pose from an angle a billboarded rig "
                    + "would never show you.";
                billboardPreviewToggle.RegisterValueChangedCallback(changeEvent =>
                {
                    if (previewController != null)
                    {
                        previewController.BillboardPreviewEnabled = changeEvent.newValue;
                    }
                    if (previewImage != null)
                    {
                        previewImage.MarkDirtyRepaint();
                    }
                    Repaint();
                });
            }

            rigEditToggle = rootVisualElement.Q<ToolbarToggle>("rig-edit-toggle");
            if (rigEditToggle != null)
            {
                rigEditToggle.tooltip =
                    "Off: gizmos and fields key the selected clip. "
                    + "On: gizmos write the prefab's base pose and the hierarchy accepts drag-to-"
                    + "reparent. No keyframes are created in Rig Edit.";
                rigEditToggle.RegisterValueChangedCallback(OnRigEditModeChanged);
            }
            ApplyRigEditChrome();

            autoKeyToggle = rootVisualElement.Q<ToolbarToggle>("auto-key-toggle");
            if (autoKeyToggle != null)
            {
                autoKeyToggle.tooltip =
                    "On: editing a transform value writes it into a key at the playhead. "
                    + "Off: the change is held and shown as modified until you press Key.";
                autoKeyToggle.RegisterValueChangedCallback(changeEvent =>
                {
                    // Turning auto-key on adopts whatever is currently held, rather than discarding
                    // it — the user has just said they want their edits kept.
                    if (changeEvent.newValue && hasPendingTransformEdit)
                    {
                        CommitPendingTransformEdit();
                    }
                    RebuildInspector();
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

            deleteClipButton = rootVisualElement.Q<Button>("delete-clip-button");
            if (deleteClipButton != null)
            {
                deleteClipButton.clicked += DeleteSelectedClip;
                deleteClipButton.tooltip =
                    "Remove the selected clip from the set, and optionally send its asset to the "
                    + "trash. Asks first.";
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
        /// The creation is <see cref="ClipAssetUtility"/>'s, shared with the clip set's own
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

            ClipAsset newClip = ClipAssetUtility.CreateClipInSet(clipSet);
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
        /// Enables the Clips pane's actions for the states in which they mean something.
        /// </summary>
        /// <remarks>
        /// A clip is only meaningful inside a set — it inherits the set's rig, and validation rule
        /// V06 refuses a clip whose rig is anything else. So "no set assigned" is not a case to
        /// invent a home for; it is a case to disable. Delete additionally needs a clip selected to
        /// be about.
        /// </remarks>
        private void RefreshClipActionButtons()
        {
            if (newClipButton != null)
            {
                newClipButton.SetEnabled(clipSet != null);
            }
            if (deleteClipButton != null)
            {
                deleteClipButton.SetEnabled(clipSet != null && selectedClip != null);
            }
        }

        /// <summary>
        /// Asks what to do with the selected clip, then un-registers it and optionally trashes it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>Three answers, because there are genuinely three.</strong> "Remove from the set"
        /// and "delete the file" are different intentions with very different consequences, and a
        /// two-button dialog would make the safe one unreachable from here — so someone who meant
        /// "take this out of the set" would confirm a deletion to get it. The dialog names both
        /// outcomes rather than making the user infer them from one word on a button.
        /// </para>
        /// <para>
        /// The dialog says plainly which outcome is undoable. Deleting the asset is not, on purpose:
        /// undo cannot bring a file back, so an undoable delete would restore a set entry pointing
        /// at something in the trash. The file going to the operating system's trash rather than
        /// being unlinked outright is the one recovery path a mis-click actually has.
        /// </para>
        /// </remarks>
        private void DeleteSelectedClip()
        {
            if (clipSet == null || selectedClip == null || clipSet.clips == null)
            {
                return;
            }

            int clipIndex = clipSet.clips.IndexOf(selectedClip);
            if (clipIndex < 0)
            {
                return;
            }

            ClipAsset clipToDelete = selectedClip;
            int choice = EditorUtility.DisplayDialogComplex(
                "Delete Clip",
                "Delete '" + clipToDelete.name + "'?\n\n"
                + "Delete Asset sends the clip file to the trash and removes it from '"
                + clipSet.name + "'. This cannot be undone.\n\n"
                + "Remove From Set un-registers it, leaves the asset on disk, and can be undone.",
                "Delete Asset",
                "Cancel",
                "Remove From Set");

            if (choice == 1)
            {
                return;
            }

            if (choice == 0)
            {
                ClipAssetUtility.DeleteClipFromSet(clipSet, clipIndex, clipToDelete);
            }
            else
            {
                ClipAssetUtility.RemoveClipFromSet(clipSet, clipIndex);
            }

            SelectClip(null);
            RefreshClipList();
            SelectClipNearIndex(clipIndex);

            MarkPreviewDirty();
            if (validationBadge != null)
            {
                validationBadge.Refresh(clipSet);
            }
        }

        /// <summary>
        /// Selects whatever now occupies <paramref name="removedIndex"/>, or the last clip.
        /// </summary>
        /// <remarks>
        /// Landing on the neighbour is what makes deleting several clips in a row workable — leaving
        /// nothing selected would mean re-selecting by hand between every deletion.
        /// </remarks>
        private void SelectClipNearIndex(int removedIndex)
        {
            if (clipListView == null || clipSet == null || clipSet.clips == null
                || clipSet.clips.Count == 0)
            {
                RefreshClipActionButtons();
                return;
            }

            int nextIndex = Mathf.Clamp(removedIndex, 0, clipSet.clips.Count - 1);
            clipListView.SetSelection(nextIndex);
            clipListView.ScrollToItem(nextIndex);
        }

        /// <summary>
        /// Creates a clip set wherever the user chooses, and loads it into the window.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The location is asked for rather than derived. A clip is created beside its set because
        /// the set is a natural anchor; a set is the root of the graph and has no anchor at all, so
        /// there is nothing to infer a home from — and a package guessing a folder is how projects
        /// end up with assets scattered wherever a tool felt like putting them.
        /// </para>
        /// <para>
        /// Assigned through the toolbar field rather than to <c>clipSet</c> directly, so loading a
        /// new set runs the same path as picking one by hand: the clip list, preview, validation
        /// badge and button states all follow from the one change notification.
        /// </para>
        /// </remarks>
        private void CreateClipSet()
        {
            string assetPath = EditorUtility.SaveFilePanelInProject(
                "Create Clip Set",
                "NewClipSet",
                "asset",
                "Choose where to save the new clip set.");
            if (string.IsNullOrEmpty(assetPath))
            {
                return;
            }

            ClipSetAsset newClipSet = ClipAssetUtility.CreateClipSet(assetPath);
            if (newClipSet == null)
            {
                return;
            }

            if (clipSetField != null)
            {
                clipSetField.value = newClipSet;
            }
            EditorGUIUtility.PingObject(newClipSet);
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

            // Multiple, so several parts can be focused on the timeline at once. Ctrl-click adds,
            // shift-click extends — the conventions every list in the editor already uses.
            hierarchyTreeView.selectionType = SelectionType.Multiple;
            hierarchyTreeView.makeItem = MakeHierarchyRow;
            hierarchyTreeView.bindItem = BindHierarchyRow;
            hierarchyTreeView.selectionChanged += OnHierarchySelectionChanged;

            Button addSocketButton = rootVisualElement.Q<Button>("add-socket-button");
            if (addSocketButton != null)
            {
                addSocketButton.tooltip =
                    "Add an attachment point, bound to whatever is selected in this tree.";
                addSocketButton.clicked += AddSocket;
            }

            billboardRootButton = rootVisualElement.Q<Button>("billboard-root-button");
            if (billboardRootButton != null)
            {
                billboardRootButton.clicked += ToggleBillboardRootOnSelection;
            }

            editPrefabButton = rootVisualElement.Q<Button>("edit-prefab-button");
            if (editPrefabButton != null)
            {
                editPrefabButton.clicked += OpenPrefabForSelection;
            }
            RefreshPrefabActionState();
            RefreshBillboardButtonState();
        }

        /// <summary>
        /// Enables the prefab entry points only when there is a prefab asset behind the rig field.
        /// </summary>
        /// <remarks>
        /// A scene object dropped into that field has no asset to open, and a button that reports
        /// its own failure after being pressed is worse than one that shows it cannot be pressed.
        /// </remarks>
        private void RefreshPrefabActionState()
        {
            if (editPrefabButton == null)
            {
                return;
            }
            bool canOpen = PrefabAuthoringBridge.CanOpen(LoadedPrefab);
            editPrefabButton.SetEnabled(canOpen);
            editPrefabButton.tooltip = canOpen
                ? "Open this prefab in Unity's prefab mode. Structural edits — parenting, adding "
                    + "parts, moving meshes — belong there, not here."
                : "Assign a prefab in the toolbar's rig field to edit it.";
        }

        /// <summary>The prefab assigned in the toolbar, which the preview instantiates.</summary>
        private GameObject LoadedPrefab
        {
            get
            {
                return skinnedSourceField != null ? skinnedSourceField.value as GameObject : null;
            }
        }

        /// <summary>
        /// The path of a hierarchy row's object below the prefab root, for addressing it in a stage.
        /// </summary>
        /// <remarks>
        /// A rig-target row has no transform of its own — it stands for an entry in the rig asset —
        /// so it resolves through the name it binds by. That is the same lookup the rest pose uses,
        /// which means "Open Prefab Here" lands on exactly the object the preview took its rest pose
        /// from, or on the root when there is none to land on.
        /// </remarks>
        private string ResolveHierarchyPath(HierarchyItem item)
        {
            if (item == null || previewController == null)
            {
                return string.Empty;
            }

            Transform root = previewController.HierarchyRoot;
            if (root == null)
            {
                return string.Empty;
            }

            // A socket has no transform of its own in the prefab, so it resolves through whatever
            // it follows — opening prefab mode on the hand is the useful answer to "edit this
            // socket's object", since the socket itself lives in the rig asset.
            Transform node;
            switch (item.kind)
            {
                case HierarchyItemKind.RigTarget:
                    node = PrefabAuthoringBridge.FindByName(root, item.displayName);
                    break;
                case HierarchyItemKind.Socket:
                    node = PrefabAuthoringBridge.FindByName(root, ResolveSocketFollowName(item.socketId));
                    break;
                default:
                    node = previewController.GetTransformByIndex(item.previewIndex);
                    break;
            }
            return node != null ? PrefabAuthoringBridge.GetHierarchyPath(node, root) : string.Empty;
        }

        // -------------------------------------------------------------------------------------
        // Round trip. Prefab mode owns the structure; this window owns the animation bound to it,
        // and has to be told when the first changes under the second.
        // -------------------------------------------------------------------------------------

        /// <summary>The playhead and selection to restore once the rebuilt tree is standing.</summary>
        private float roundTripPlayheadTime;
        private readonly List<string> roundTripSelectedNames = new List<string>();
        private bool hasRoundTripState;

        /// <summary>
        /// Captures what should survive a trip through prefab mode.
        /// </summary>
        /// <remarks>
        /// Selection is remembered <em>by name</em> rather than by tree id, because the ids are
        /// indices into a hierarchy walk that the prefab edit is about to invalidate. A name is the
        /// only handle that can still mean something on the other side — and when it cannot, that is
        /// itself the signal that the object was renamed or deleted, which is what the reconciler
        /// reports.
        /// </remarks>
        private void RememberRoundTripState()
        {
            roundTripPlayheadTime = playheadTime;
            roundTripSelectedNames.Clear();
            for (int itemIndex = 0; itemIndex < selectedHierarchyItems.Count; itemIndex++)
            {
                roundTripSelectedNames.Add(selectedHierarchyItems[itemIndex].displayName);
            }
            hasRoundTripState = true;
        }

        private void OnPrefabStageSaved(GameObject savedRoot)
        {
            if (!IsStageOurPrefab(PrefabStageUtility.GetCurrentPrefabStage()))
            {
                return;
            }
            ReloadAfterPrefabEdit();
        }

        private void OnPrefabStageClosing(PrefabStage closingStage)
        {
            if (!IsStageOurPrefab(closingStage))
            {
                return;
            }

            // Deferred, because the stage is still open at this moment: reinstantiating the prefab
            // now would copy the contents the stage is about to tear down.
            EditorApplication.delayCall += ReloadAfterPrefabEdit;

            // The other half of the swap. Exiting prefab mode is the user saying they are done
            // authoring structure, so the window they were animating in comes back on its own —
            // that is the "one click out" half of the requirement.
            EditorApplication.delayCall += FocusSelf;
        }

        /// <summary>Brings this window forward, guarding against the instance having gone.</summary>
        /// <remarks>
        /// Called from a deferred callback, which can outlive the window if the user closed it while
        /// prefab mode was open. <c>this == null</c> is the Unity-object null check that catches a
        /// destroyed window a plain reference comparison would miss.
        /// </remarks>
        private void FocusSelf()
        {
            if (this == null)
            {
                return;
            }
            Focus();
        }

        /// <summary>Whether a stage is editing the prefab this window has loaded.</summary>
        /// <remarks>
        /// Without this the window would rebuild itself every time anyone in the project saved any
        /// prefab, which is both wasteful and confusing — a reconciliation panel that appeared
        /// because of an unrelated edit would be noise of the worst kind.
        /// </remarks>
        private bool IsStageOurPrefab(PrefabStage stage)
        {
            if (stage == null)
            {
                return false;
            }
            string loadedPath = PrefabAuthoringBridge.ResolveAssetPath(LoadedPrefab);
            return !string.IsNullOrEmpty(loadedPath) && stage.assetPath == loadedPath;
        }

        /// <summary>
        /// Rebuilds everything downstream of the prefab, then reports what no longer binds.
        /// </summary>
        /// <remarks>
        /// The order matters. The preview is reinstantiated first so the hierarchy is the new one;
        /// the tree is rebuilt from it; selection and playhead are restored against that tree; and
        /// only then is reconciliation run, because it asks "which names are missing from the
        /// hierarchy" and needs the new hierarchy to ask it of.
        /// </remarks>
        private void ReloadAfterPrefabEdit()
        {
            if (previewController == null)
            {
                return;
            }

            GameObject prefab = LoadedPrefab;

            // Forced through null: SetSkinnedSource early-outs when handed the same reference, and
            // after a prefab save it is the same reference with different contents.
            previewController.SetSkinnedSource(null);
            previewController.SetSkinnedSource(prefab);

            RebuildHierarchy();
            RestoreRoundTripState();
            RebuildTimeline();
            RefreshPrefabActionState();
            MarkPreviewDirty();

            RunReconciliation();
        }

        private void RestoreRoundTripState()
        {
            if (!hasRoundTripState)
            {
                return;
            }
            hasRoundTripState = false;

            SetPlayheadTime(roundTripPlayheadTime);

            List<int> restoredIds = new List<int>();
            for (int nameIndex = 0; nameIndex < roundTripSelectedNames.Count; nameIndex++)
            {
                int itemId = FindItemIdByName(roundTripSelectedNames[nameIndex]);
                if (itemId != NothingSelectedItemId)
                {
                    restoredIds.Add(itemId);
                }
            }

            if (hierarchyTreeView == null || restoredIds.Count == 0)
            {
                return;
            }
            hierarchyTreeView.SetSelectionById(restoredIds);
        }

        private int FindItemIdByName(string displayName)
        {
            if (string.IsNullOrEmpty(displayName))
            {
                return NothingSelectedItemId;
            }
            foreach (KeyValuePair<int, HierarchyItem> pair in hierarchyItemsById)
            {
                if (pair.Value != null && pair.Value.displayName == displayName)
                {
                    return pair.Key;
                }
            }
            return NothingSelectedItemId;
        }

        // -------------------------------------------------------------------------------------
        // Rig Edit mode.
        // -------------------------------------------------------------------------------------

        /// <summary>
        /// Whether a gizmo drag edits the rig's base setup instead of keying the clip.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>The two modes must never be confusable, because their outputs are not
        /// interchangeable.</strong> A drag in Animate mode writes a key into a clip; the same drag
        /// in Rig Edit mode writes the prefab asset. Neither is recoverable by doing the other, and a
        /// user who mistook one for the other would find out only later — a pose silently baked into
        /// every clip, or a key nobody meant to create.
        /// </para>
        /// <para>
        /// So the mode is stated three times over: the toolbar toggle is tinted, the viewport frame
        /// is bordered in the same colour, and a banner across the top of the viewport says in words
        /// what a drag will do. Keying is also switched off outright rather than merely discouraged —
        /// Auto Key is disabled and the edit path refuses — so the ambiguity is removed in behaviour
        /// and not only in signage.
        /// </para>
        /// </remarks>
        private bool IsRigEditMode
        {
            get { return rigEditToggle != null && rigEditToggle.value; }
        }

        private void OnRigEditModeChanged(ChangeEvent<bool> changeEvent)
        {
            // A held, unkeyed pose belongs to the clip. Carrying it into a mode that writes the
            // prefab would make its eventual destination a coin toss.
            DiscardPendingTransformEdit();
            ApplyRigEditChrome();
            RebuildInspector();
            MarkPreviewDirty();
        }

        private void ApplyRigEditChrome()
        {
            bool isRigEdit = IsRigEditMode;

            if (viewportFrame != null)
            {
                viewportFrame.EnableInClassList(ViewportFrameRigEditUssClassName, isRigEdit);
            }

            if (rigEditBanner != null)
            {
                rigEditBanner.EnableInClassList(HiddenUssClassName, !isRigEdit);
                rigEditBanner.text = isRigEdit
                    ? "RIG EDIT — gizmo drags write the prefab's base pose. No keyframes are created."
                    : string.Empty;
            }

            // Auto Key is not merely ignored in Rig Edit, it is visibly unavailable: leaving a lit
            // "Auto Key" beside a mode that cannot key is the exact ambiguity this mode exists to
            // remove.
            if (autoKeyToggle != null)
            {
                autoKeyToggle.SetEnabled(!isRigEdit);
            }
        }

        /// <summary>
        /// Writes a gizmo drag into the prefab's base pose.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The values arriving here are the same ones the animate path would have keyed, but they
        /// mean something different: in a clip they are an offset from rest, and here they
        /// <em>are</em> the rest. So the composition is undone before writing — the drag's delta is
        /// added to the transform the prefab currently has, rather than replacing it with a number
        /// that was only ever meaningful relative to it.
        /// </para>
        /// <para>
        /// Nothing is written until the drag is released. A per-frame write would mean one asset
        /// save per pointer move.
        /// </para>
        /// </remarks>
        private void CommitRigBaseEdit(uint targetId, float3 position, float3 rotationDegrees, float3 scale)
        {
            HierarchyItem item = ActiveHierarchyItem;
            if (item == null)
            {
                ShowNotification(new GUIContent("Select a part to edit its base pose."));
                return;
            }

            string path = ResolveHierarchyPath(item);
            GameObject prefab = LoadedPrefab;
            if (prefab == null)
            {
                ShowNotification(new GUIContent("Assign a prefab in the rig field to edit its rig."));
                return;
            }

            string error;
            bool written = RigStructureEditor.TrySetLocalPose(
                prefab, path,
                new Vector3(position.x, position.y, position.z),
                new Vector3(rotationDegrees.x, rotationDegrees.y, rotationDegrees.z),
                new Vector3(scale.x, scale.y, scale.z),
                out error);

            if (!written)
            {
                ShowNotification(new GUIContent(error));
                return;
            }

            ReloadAfterPrefabEdit();
        }

        // -------------------------------------------------------------------------------------
        // Reconciliation.
        // -------------------------------------------------------------------------------------

        private readonly List<BrokenBinding> brokenBindings = new List<BrokenBinding>();
        private readonly HashSet<string> hierarchyNameCache = new HashSet<string>();

        /// <summary>
        /// Re-checks every name-based binding and shows the panel when any has broken.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>Nothing is dropped, and nothing is guessed.</strong> A binding whose name has
        /// gone could be a rename, a reparent that also renamed, or a deliberate deletion, and the
        /// difference is not recoverable from the data — only the person who made the edit knows.
        /// So the panel states the fact and offers the two honest answers: point it at a name that
        /// exists, or remove it.
        /// </para>
        /// <para>
        /// Transform and sprite tracks are absent from this panel on purpose. They bind to a rig
        /// target's stable id, which no prefab edit can touch, so listing them would be inventing a
        /// problem to make the panel look thorough.
        /// </para>
        /// </remarks>
        private void RunReconciliation()
        {
            if (previewController == null)
            {
                return;
            }
            previewController.CollectHierarchyNames(hierarchyNameCache);
            BindingReconciler.Collect(clipSet, hierarchyNameCache, brokenBindings);
            RebuildReconcilePanel();
        }

        private void RebuildReconcilePanel()
        {
            if (reconcilePanel == null || reconcileList == null)
            {
                return;
            }

            reconcileList.Clear();
            bool hasFindings = brokenBindings.Count > 0;
            reconcilePanel.EnableInClassList(HiddenUssClassName, !hasFindings);
            if (!hasFindings)
            {
                return;
            }

            if (reconcileTitle != null)
            {
                reconcileTitle.text = brokenBindings.Count.ToString()
                    + " binding(s) no longer match the prefab. Nothing has been changed — pick a "
                    + "new name or remove each one.";
            }

            // A snapshot, because remapping mutates the lists the findings index into. Rebuilding
            // from a stale index would edit the wrong track.
            List<string> availableNames = new List<string>(hierarchyNameCache);
            availableNames.Sort();

            for (int findingIndex = 0; findingIndex < brokenBindings.Count; findingIndex++)
            {
                reconcileList.Add(BuildReconcileRow(brokenBindings[findingIndex], availableNames));
            }
        }

        private VisualElement BuildReconcileRow(BrokenBinding binding, List<string> availableNames)
        {
            VisualElement row = new VisualElement();
            row.AddToClassList(ReconcileRowUssClassName);

            Label label = new Label(DescribeBrokenBinding(binding));
            label.AddToClassList(ReconcileRowLabelUssClassName);
            row.Add(label);

            // A dropdown of names that exist rather than a free text field: the failure this panel
            // exists to fix is a name that resolves to nothing, and typing is how you get one.
            PopupField<string> remapField = new PopupField<string>(
                availableNames, 0, FormatRemapChoice, FormatRemapChoice);
            remapField.AddToClassList(ReconcileRemapUssClassName);
            row.Add(remapField);

            row.Add(new Button(() => ApplyRemap(binding, remapField.value))
            {
                text = "Remap"
            });

            if (BindingReconciler.IsDeletable(binding.kind))
            {
                row.Add(new Button(() => ConfirmDeleteBinding(binding))
                {
                    text = "Delete"
                });
            }
            return row;
        }

        private static string FormatRemapChoice(string choice)
        {
            return string.IsNullOrEmpty(choice) ? "<none>" : choice;
        }

        private static string DescribeBrokenBinding(BrokenBinding binding)
        {
            switch (binding.kind)
            {
                case BrokenBindingKind.BoneTrack:
                    return binding.description + "  ·  \"" + binding.missingName
                        + "\" is not in the prefab. " + binding.keyCount.ToString()
                        + " key(s) will not bake.";
                case BrokenBindingKind.BoneSocket:
                    return binding.description + "  ·  \"" + binding.missingName
                        + "\" is not in the prefab. The attachment will bake at the origin.";
                default:
                    return binding.description + "  ·  \"" + binding.missingName
                        + "\" is not in the prefab. Tracks still play; the preview has no rest pose "
                        + "for this part.";
            }
        }

        private void ApplyRemap(BrokenBinding binding, string newName)
        {
            if (string.IsNullOrEmpty(newName))
            {
                return;
            }

            RigAsset rig = clipSet != null ? clipSet.rig : null;
            Object undoTarget = binding.kind == BrokenBindingKind.BoneTrack
                ? (Object)binding.clip
                : rig;
            if (undoTarget == null)
            {
                return;
            }

            Undo.RecordObject(undoTarget, "Remap Animation Binding");
            if (!BindingReconciler.Remap(binding, rig, newName))
            {
                return;
            }
            EditorUtility.SetDirty(undoTarget);
            AssetDatabase.SaveAssetIfDirty(undoTarget);

            AfterReconcileEdit();
        }

        /// <summary>
        /// Deletes a broken track, behind a confirmation naming what is lost.
        /// </summary>
        /// <remarks>
        /// Confirmed because this is the one action in the panel that destroys authored work, and
        /// the count of keys is in the prompt because "delete this track" and "delete these forty
        /// keys" are different decisions.
        /// </remarks>
        private void ConfirmDeleteBinding(BrokenBinding binding)
        {
            string question = binding.kind == BrokenBindingKind.BoneTrack
                ? "Delete the bone track for \"" + binding.missingName + "\"?\n\n"
                    + binding.keyCount.ToString() + " key(s) will be lost."
                : "Delete the socket bound to \"" + binding.missingName + "\"?";

            if (!EditorUtility.DisplayDialog("Delete Broken Binding", question, "Delete", "Cancel"))
            {
                return;
            }

            RigAsset rig = clipSet != null ? clipSet.rig : null;
            Object undoTarget = binding.kind == BrokenBindingKind.BoneTrack
                ? (Object)binding.clip
                : rig;
            if (undoTarget == null)
            {
                return;
            }

            Undo.RecordObject(undoTarget, "Delete Broken Binding");
            if (!BindingReconciler.Delete(binding, rig))
            {
                return;
            }
            EditorUtility.SetDirty(undoTarget);
            AssetDatabase.SaveAssetIfDirty(undoTarget);

            AfterReconcileEdit();
        }

        /// <summary>
        /// Re-runs the whole check after one fix.
        /// </summary>
        /// <remarks>
        /// Recollected rather than removing the fixed row, because a delete shifts every later index
        /// into the same list. Patching the remaining findings by hand is exactly the bookkeeping
        /// that goes wrong; asking the question again cannot.
        /// </remarks>
        private void AfterReconcileEdit()
        {
            RefreshSerializedClip();
            RunReconciliation();
            RebuildTimeline();
            MarkPreviewDirty();
        }

        /// <summary>Opens prefab mode on the active row, or on the prefab root when none is picked.</summary>
        private void OpenPrefabForSelection()
        {
            OpenPrefabAt(ActiveHierarchyItem);
        }

        private void OpenPrefabAt(HierarchyItem item)
        {
            GameObject prefab = LoadedPrefab;
            if (!PrefabAuthoringBridge.CanOpen(prefab))
            {
                ShowNotification(new GUIContent(
                    "Assign a prefab in the rig field before editing it."));
                return;
            }

            // Remembered before the stage opens, so returning can put the window back where it was
            // rather than at the top of a rebuilt tree at time zero.
            RememberRoundTripState();

            // A floating window cannot be sent behind anything — it sits above the main window
            // whatever has focus, which is what turns this into a drag-the-window-aside chore. So
            // the first trip into prefab mode docks it, and the swap works from then on.
            if (!docked)
            {
                // Docking first, opening second. Reopening the window is itself a focus grab, so
                // doing it after the stage had opened would snatch focus straight back off the
                // Scene view the user just asked to look at.
                string pathToOpen = ResolveHierarchyPath(item);
                GameObject prefabToOpen = prefab;
                RedockBesideSceneView(() =>
                {
                    PrefabAuthoringBridge.OpenPrefab(prefabToOpen, pathToOpen);
                    EditorApplication.delayCall += ClipEditorDocking.FocusPrefabAuthoring;
                });
                return;
            }

            PrefabAuthoringBridge.OpenPrefab(prefab, ResolveHierarchyPath(item));

            // Deferred by one tick: the stage's own scene view is still being brought up by the
            // call above, and focusing into the middle of that lands on the outgoing view.
            EditorApplication.delayCall += ClipEditorDocking.FocusPrefabAuthoring;
        }

        /// <summary>
        /// Builds the right-click menu for one hierarchy row.
        /// </summary>
        /// <remarks>
        /// Three entries, and the split between them is deliberate: "Open Prefab Here" is the one
        /// that changes what you are editing, while "Ping" and "Select" only move the cursor. Making
        /// a select silently open a stage would leave the user in prefab mode without having asked
        /// to be.
        /// </remarks>
        private void BuildHierarchyContextMenu(ContextualMenuPopulateEvent menuEvent, HierarchyItem item)
        {
            bool canOpen = PrefabAuthoringBridge.CanOpen(LoadedPrefab);

            menuEvent.menu.AppendAction(
                "Open Prefab Here",
                action => OpenPrefabAt(item),
                canOpen ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);

            menuEvent.menu.AppendAction(
                "Ping in Project",
                action => PrefabAuthoringBridge.PingInProject(LoadedPrefab),
                canOpen ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);

            menuEvent.menu.AppendAction(
                "Select in Scene",
                action =>
                {
                    if (!PrefabAuthoringBridge.SelectInOpenStageOrScene(
                            LoadedPrefab, ResolveHierarchyPath(item)))
                    {
                        ShowNotification(new GUIContent(
                            "No open prefab stage or scene instance holds that object."));
                    }
                },
                canOpen ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);

            AppendBillboardMenuActions(menuEvent, item);
        }

        /// <summary>
        /// Adds the make/clear billboard-root entries for one row (amendment A44).
        /// </summary>
        /// <remarks>
        /// <para>
        /// The entry writes the <em>rig asset</em>, not the prefab, because that is where billboard
        /// configuration lives — it has to travel with the rig and be shared by every actor
        /// instanced from it. So unlike the reparent drag, this is not gated on Rig Edit mode: it
        /// edits an asset the window already owns rather than restructuring a prefab.
        /// </para>
        /// <para>
        /// A rig-target row is addressed by stable id and anything else by path, which is the same
        /// split <c>BillboardNodeAddress</c> makes and for the same reason: only a target has an id
        /// to be addressed by.
        /// </para>
        /// </remarks>
        private void AppendBillboardMenuActions(
            ContextualMenuPopulateEvent menuEvent, HierarchyItem item)
        {
            RigAsset rig = clipSet != null ? clipSet.rig : null;
            if (rig == null || item == null)
            {
                return;
            }

            int existingIndex = FindBillboardRootIndexFor(rig, item);
            if (existingIndex >= 0)
            {
                menuEvent.menu.AppendAction(
                    "Billboard/Clear Billboard Root",
                    action => ClearBillboardRoot(rig, existingIndex));
                return;
            }

            menuEvent.menu.AppendAction(
                "Billboard/Make Billboard Root",
                action => MakeBillboardRoot(rig, item));
        }

        /// <summary>
        /// Makes the selected row a billboard root, or clears it if it already is one.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>One button that reads the selection, not an add/remove pair.</strong> A node
        /// either is a billboard root or is not; a pair would force the author to know which state
        /// they were in before choosing, which is exactly what the button is supposed to tell them.
        /// The label follows the selection for the same reason.
        /// </para>
        /// <para>
        /// The same operation lives on the row's right-click menu. This exists because the context
        /// menu is where you look once you know the feature is there, and the pane header is where
        /// you look when you do not.
        /// </para>
        /// </remarks>
        private void ToggleBillboardRootOnSelection()
        {
            RigAsset rig = clipSet != null ? clipSet.rig : null;
            HierarchyItem item = ActiveHierarchyItem;
            if (rig == null || item == null)
            {
                ShowNotification(new GUIContent(
                    "Select a node in the hierarchy first, and load a clip set whose rig can hold "
                    + "the billboard root."));
                return;
            }

            int existingIndex = FindBillboardRootIndexFor(rig, item);
            if (existingIndex >= 0)
            {
                ClearBillboardRoot(rig, existingIndex);
            }
            else
            {
                MakeBillboardRoot(rig, item);
            }
            RefreshBillboardButtonState();
        }

        /// <summary>
        /// Points the button at what it would do to the current selection.
        /// </summary>
        /// <remarks>
        /// Disabled rather than hidden when nothing is selected: a control that vanishes is a
        /// control nobody learns exists, which is the whole problem this button was added to solve.
        /// </remarks>
        private void RefreshBillboardButtonState()
        {
            if (billboardRootButton == null)
            {
                return;
            }

            RigAsset rig = clipSet != null ? clipSet.rig : null;
            HierarchyItem item = ActiveHierarchyItem;
            bool canAct = rig != null && item != null;
            billboardRootButton.SetEnabled(canAct);

            if (!canAct)
            {
                billboardRootButton.text = "+ Billboard";
                billboardRootButton.tooltip =
                    "Select a node in the hierarchy to make it a billboard root.";
                return;
            }

            bool alreadyRoot = FindBillboardRootIndexFor(rig, item) >= 0;
            billboardRootButton.text = alreadyRoot ? "- Billboard" : "+ Billboard";
            billboardRootButton.tooltip = alreadyRoot
                ? "Stop '" + item.displayName + "' being a billboard root. Nodes under it fall back "
                    + "to whichever root is above it, or to none."
                : "Make '" + item.displayName + "' a billboard root. It turns to face the viewer, "
                    + "and so does everything under it unless a child declares a root of its own.";
        }

        /// <summary>The rig's billboard root addressing this row, or −1.</summary>
        private int FindBillboardRootIndexFor(RigAsset rig, HierarchyItem item)
        {
            if (rig.billboardRoots == null)
            {
                return -1;
            }
            BillboardNodeAddress address = BuildBillboardAddressFor(item);
            for (int rootIndex = 0; rootIndex < rig.billboardRoots.Count; rootIndex++)
            {
                BillboardRootDefinition definition = rig.billboardRoots[rootIndex];
                if (definition == null || definition.address.kind != address.kind)
                {
                    continue;
                }
                if (address.kind == BillboardAddressKind.RigTarget)
                {
                    if (definition.address.targetId == address.targetId)
                    {
                        return rootIndex;
                    }
                    continue;
                }
                if (string.Equals(
                        definition.address.hierarchyPath,
                        address.hierarchyPath,
                        System.StringComparison.Ordinal))
                {
                    return rootIndex;
                }
            }
            return -1;
        }

        private BillboardNodeAddress BuildBillboardAddressFor(HierarchyItem item)
        {
            if (item.kind == HierarchyItemKind.RigTarget)
            {
                return new BillboardNodeAddress
                {
                    kind = BillboardAddressKind.RigTarget,
                    targetId = item.targetId
                };
            }
            return new BillboardNodeAddress
            {
                kind = BillboardAddressKind.HierarchyPath,
                hierarchyPath = ResolveHierarchyPath(item)
            };
        }

        private void MakeBillboardRoot(RigAsset rig, HierarchyItem item)
        {
            Undo.RecordObject(rig, "Make Billboard Root");
            if (rig.billboardRoots == null)
            {
                rig.billboardRoots = new List<BillboardRootDefinition>();
            }
            rig.billboardRoots.Add(new BillboardRootDefinition
            {
                displayName = item.displayName,
                address = BuildBillboardAddressFor(item)
            });
            // Mints the row's stable id, which clip billboard tracks bind to. Without this the row
            // saves with id 0 and no track could ever address it.
            rig.EnsureStableIds();
            EditorUtility.SetDirty(rig);
            RefreshAfterBillboardEdit();
        }

        private void ClearBillboardRoot(RigAsset rig, int rootIndex)
        {
            Undo.RecordObject(rig, "Clear Billboard Root");
            rig.billboardRoots.RemoveAt(rootIndex);
            EditorUtility.SetDirty(rig);
            RefreshAfterBillboardEdit();
        }

        /// <summary>
        /// Repaints the tree and the viewport after a billboard edit, so both agree with the rig.
        /// </summary>
        private void RefreshAfterBillboardEdit()
        {
            RefreshHierarchyRows();
            if (previewImage != null)
            {
                previewImage.MarkDirtyRepaint();
            }
            Repaint();
        }

        private void BindViewport()
        {
            previewStatusLabel = rootVisualElement.Q<Label>("viewport-status");
            viewportFrame = rootVisualElement.Q<VisualElement>("viewport-frame");
            rigEditBanner = rootVisualElement.Q<Label>("rig-edit-banner");

            reconcilePanel = rootVisualElement.Q<VisualElement>("reconcile-panel");
            reconcileList = rootVisualElement.Q<ScrollView>("reconcile-list");
            reconcileTitle = rootVisualElement.Q<Label>("reconcile-title");

            Button dismissButton = rootVisualElement.Q<Button>("reconcile-dismiss-button");
            if (dismissButton != null)
            {
                // Dismiss hides the panel without touching anything. The bindings stay broken and
                // the next prefab save reports them again, which is the honest behaviour: this is a
                // "not now" button, not a "resolved" one.
                dismissButton.clicked += () =>
                {
                    brokenBindings.Clear();
                    RebuildReconcilePanel();
                };
            }

            previewImage = rootVisualElement.Q<Image>("viewport-image");
            if (previewImage == null)
            {
                return;
            }
            previewImage.scaleMode = ScaleMode.ScaleToFit;

            // Focusable so W/E/R reach the viewport rather than the window's other shortcuts.
            previewImage.focusable = true;
            previewImage.RegisterCallback<KeyDownEvent>(OnViewportKeyDown);
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

            // Under the playhead so the current-time line stays readable over a band.
            boxSelectElement = new BoxSelectElement();
            laneStack.Add(boxSelectElement);

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
        /// <summary>W / E / R switch the gizmo mode, matching every other 3D tool.</summary>
        private void OnViewportKeyDown(KeyDownEvent keyEvent)
        {
            GizmoMode requestedMode;
            switch (keyEvent.keyCode)
            {
                case KeyCode.W:
                    requestedMode = GizmoMode.Move;
                    break;
                case KeyCode.E:
                    requestedMode = GizmoMode.Rotate;
                    break;
                case KeyCode.R:
                    requestedMode = GizmoMode.Scale;
                    break;
                default:
                    return;
            }

            gizmoMode = requestedMode;
            RefreshGizmo();
            keyEvent.StopPropagation();
        }

        /// <summary>
        /// Puts the gizmo on the selected part at the value currently displayed.
        /// </summary>
        /// <remarks>
        /// The pivot comes from the authored value rather than from the mirrored quad, because the
        /// quad follows the built registry and that is rebuilt on a debounce — a gizmo anchored to it
        /// would lag its own drag by a quarter of a second.
        /// </remarks>
        private void RefreshGizmo()
        {
            if (previewController == null)
            {
                return;
            }

            // A socket takes the gizmo wherever its marker currently sits, clip or no clip: its
            // offset is rig data, so it is placeable without a clip selected at all.
            if (selectedSocketId != 0u)
            {
                Transform marker = previewController.GetSocketMarker(selectedSocketId);
                previewController.SetGizmo(
                    marker != null, gizmoMode,
                    marker != null ? marker.localPosition : Vector3.zero,
                    activeGizmoHandle);
                return;
            }

            if (selectedTargetId == 0u || selectedClip == null)
            {
                previewController.SetGizmo(false, gizmoMode, Vector3.zero, GizmoHandle.None);
                return;
            }

            float3 position;
            float3 rotationDegrees;
            float3 scale;
            ResolveDisplayedTransform(
                selectedTargetId, out position, out rotationDegrees, out scale);

            previewController.SetGizmo(
                true, gizmoMode, new Vector3(position.x, position.y, position.z), activeGizmoHandle);
        }

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
            previewImage.Focus();

            if (previewController == null)
            {
                return;
            }

            // A press on a gizmo handle is a transform drag, not an orbit and not a selection. Tested
            // first for exactly that reason: the handle sits on top of the thing it edits, so any
            // other order would make it unusable.
            if (TryBeginGizmoDrag(pointerEvent.localPosition))
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

            if (activeGizmoHandle != GizmoHandle.None)
            {
                ContinueGizmoDrag(moveEvent.localPosition);
                return;
            }
            previewController.Orbit(moveEvent.deltaPosition);
        }

        /// <summary>Whether the press landed on a gizmo handle, and if so, starts the drag.</summary>
        private bool TryBeginGizmoDrag(Vector2 localPosition)
        {
            bool draggingSocket = selectedSocketId != 0u;
            if (!draggingSocket && (selectedTargetId == 0u || selectedClip == null))
            {
                return false;
            }

            Vector2 viewportPoint;
            float aspect;
            if (!TryGetViewportPoint(localPosition, out viewportPoint, out aspect))
            {
                return false;
            }

            GizmoHandle handle = previewController.PickGizmoHandle(viewportPoint, aspect);
            if (handle == GizmoHandle.None)
            {
                return false;
            }

            float3 position;
            float3 rotationDegrees;
            float3 scale;
            if (draggingSocket)
            {
                // The drag works in the marker's own space, which is where the gizmo is drawn.
                // The offset it writes back is in the followed part's space, and the conversion
                // between the two happens once, on release.
                Transform marker = previewController.GetSocketMarker(selectedSocketId);
                if (marker == null)
                {
                    return false;
                }
                Vector3 markerEuler = marker.localRotation.eulerAngles;
                position = new float3(
                    marker.localPosition.x, marker.localPosition.y, marker.localPosition.z);
                rotationDegrees = new float3(markerEuler.x, markerEuler.y, markerEuler.z);
                scale = new float3(1f, 1f, 1f);
            }
            else
            {
                ResolveDisplayedTransform(
                    selectedTargetId, out position, out rotationDegrees, out scale);
            }

            activeGizmoHandle = handle;
            gizmoDragStartPosition = position;
            gizmoDragStartRotation = rotationDegrees;
            gizmoDragStartScale = scale;

            Ray pressRay = previewController.BuildViewportRay(viewportPoint, aspect);
            Vector3 pivot = new Vector3(position.x, position.y, position.z);

            if (gizmoMode == GizmoMode.Rotate)
            {
                Vector3 planeHit;
                if (!PreviewGizmoMath.TryIntersectPlane(
                        pressRay, pivot, PreviewGizmoMath.GetRotationPlaneNormal(handle), out planeHit))
                {
                    activeGizmoHandle = GizmoHandle.None;
                    return false;
                }
                gizmoDragStartParameter =
                    PreviewGizmoMath.AngleAroundPivotDegrees(planeHit, pivot, handle);
            }
            else
            {
                Vector3 axis = handle == GizmoHandle.ScaleUniform
                    ? Vector3.right
                    : PreviewGizmoMath.GetHandleAxis(handle);
                if (!PreviewGizmoMath.TryGetClosestAxisParameter(
                        pressRay, pivot, axis, out gizmoDragStartParameter))
                {
                    activeGizmoHandle = GizmoHandle.None;
                    return false;
                }
            }

            RefreshGizmo();
            return true;
        }

        /// <summary>
        /// Turns pointer motion into a transform value and writes it through the shared path.
        /// </summary>
        /// <remarks>
        /// Every frame of the drag writes with <c>forceKey: false</c>, so with auto-key off the
        /// whole gesture stays a held edit and the clip gains nothing until release. With auto-key
        /// on the first write creates the key and the rest update it, because
        /// <c>SetKeyValues</c> finds the key already at the playhead.
        /// </remarks>
        private void ContinueGizmoDrag(Vector2 localPosition)
        {
            Vector2 viewportPoint;
            float aspect;
            if (!TryGetViewportPoint(localPosition, out viewportPoint, out aspect))
            {
                return;
            }

            Ray dragRay = previewController.BuildViewportRay(viewportPoint, aspect);
            Vector3 pivot = new Vector3(
                gizmoDragStartPosition.x, gizmoDragStartPosition.y, gizmoDragStartPosition.z);

            if (gizmoMode == GizmoMode.Rotate)
            {
                Vector3 planeHit;
                if (!PreviewGizmoMath.TryIntersectPlane(
                        dragRay, pivot,
                        PreviewGizmoMath.GetRotationPlaneNormal(activeGizmoHandle), out planeHit))
                {
                    return;
                }
                float currentAngle = PreviewGizmoMath.AngleAroundPivotDegrees(
                    planeHit, pivot, activeGizmoHandle);
                float angleDelta = Mathf.DeltaAngle(gizmoDragStartParameter, currentAngle);

                float3 rotatedValue = gizmoDragStartRotation;
                switch (activeGizmoHandle)
                {
                    case GizmoHandle.RotateX:
                        rotatedValue.x += angleDelta;
                        break;
                    case GizmoHandle.RotateY:
                        rotatedValue.y += angleDelta;
                        break;
                    default:
                        rotatedValue.z += angleDelta;
                        break;
                }
                ApplyGizmoDragValue(gizmoDragStartPosition, rotatedValue, gizmoDragStartScale);
                RebuildInspector();
                RefreshGizmo();
                return;
            }

            Vector3 dragAxis = activeGizmoHandle == GizmoHandle.ScaleUniform
                ? Vector3.right
                : PreviewGizmoMath.GetHandleAxis(activeGizmoHandle);
            float currentParameter;
            if (!PreviewGizmoMath.TryGetClosestAxisParameter(
                    dragRay, pivot, dragAxis, out currentParameter))
            {
                return;
            }
            float parameterDelta = currentParameter - gizmoDragStartParameter;

            if (gizmoMode == GizmoMode.Move)
            {
                float3 movedPosition = gizmoDragStartPosition;
                switch (activeGizmoHandle)
                {
                    case GizmoHandle.AxisX:
                        movedPosition.x += parameterDelta;
                        break;
                    case GizmoHandle.AxisY:
                        movedPosition.y += parameterDelta;
                        break;
                    default:
                        movedPosition.z += parameterDelta;
                        break;
                }
                ApplyGizmoDragValue(movedPosition, gizmoDragStartRotation, gizmoDragStartScale);
            }
            else
            {
                float3 scaledValue = gizmoDragStartScale;
                switch (activeGizmoHandle)
                {
                    case GizmoHandle.AxisX:
                        scaledValue.x += parameterDelta;
                        break;
                    case GizmoHandle.AxisY:
                        scaledValue.y += parameterDelta;
                        break;
                    case GizmoHandle.AxisZ:
                        scaledValue.z += parameterDelta;
                        break;
                    default:
                        scaledValue += parameterDelta;
                        break;
                }
                ApplyGizmoDragValue(gizmoDragStartPosition, gizmoDragStartRotation, scaledValue);
            }

            RebuildInspector();
            RefreshGizmo();
        }

        /// <summary>
        /// Sends a drag's value wherever the current selection says it belongs.
        /// </summary>
        /// <remarks>
        /// One dispatcher rather than a test at each of the three drag branches, so "what does a
        /// gizmo drag write" has a single answer in a single place. A socket is held live rather
        /// than written per frame for the same reason a clip edit is: one asset write per pointer
        /// move would be absurd, and the marker already shows the result.
        /// </remarks>
        private void ApplyGizmoDragValue(float3 position, float3 rotationDegrees, float3 scale)
        {
            if (selectedSocketId != 0u)
            {
                pendingSocketPosition = position;
                pendingSocketRotation = rotationDegrees;
                hasPendingSocketEdit = true;
                PreviewSocketDrag(position, rotationDegrees);
                return;
            }
            ApplyTransformEdit(selectedTargetId, position, rotationDegrees, scale, false);
        }

        private bool hasPendingSocketEdit;
        private float3 pendingSocketPosition;
        private float3 pendingSocketRotation;

        /// <summary>Moves the marker during the drag, without touching the asset.</summary>
        private void PreviewSocketDrag(float3 position, float3 rotationDegrees)
        {
            Transform marker = previewController.GetSocketMarker(selectedSocketId);
            if (marker == null)
            {
                return;
            }
            marker.localPosition = new Vector3(position.x, position.y, position.z);
            marker.localRotation = Quaternion.Euler(
                rotationDegrees.x, rotationDegrees.y, rotationDegrees.z);
        }

        /// <summary>
        /// Writes a finished socket drag back as an offset in the followed thing's space.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The gizmo works in the mirror root's space, because that is where it is drawn; a socket
        /// stores its offset in the space of the part or bone it follows. So the followed pose is
        /// divided back out here — the inverse of the composition
        /// <see cref="PreviewSocketMarkers"/> and <c>SocketResolveSystem</c> both perform. Writing
        /// the drag's raw numbers instead would look right until the rig rotated, and then put the
        /// sword somewhere else entirely.
        /// </para>
        /// <para>
        /// Undo goes on the rig, matching every other socket edit: the offset is rig structure that
        /// all clips share.
        /// </para>
        /// </remarks>
        private void CommitSocketDrag()
        {
            SocketDefinition socket = FindSocket(selectedSocketId);
            RigAsset rig = clipSet != null ? clipSet.rig : null;
            if (socket == null || rig == null || !hasPendingSocketEdit)
            {
                hasPendingSocketEdit = false;
                return;
            }
            hasPendingSocketEdit = false;

            Transform followed = previewController.GetSocketFollowedTransform(socket);
            Vector3 draggedPosition = new Vector3(
                pendingSocketPosition.x, pendingSocketPosition.y, pendingSocketPosition.z);
            Quaternion draggedRotation = Quaternion.Euler(
                pendingSocketRotation.x, pendingSocketRotation.y, pendingSocketRotation.z);

            Vector3 basePosition = Vector3.zero;
            Quaternion baseRotation = Quaternion.identity;
            if (followed != null)
            {
                basePosition = followed.localPosition;
                baseRotation = followed.localRotation;
            }

            Quaternion inverseBase = Quaternion.Inverse(baseRotation);
            Undo.RecordObject(rig, "Place Socket");
            socket.localPosition = inverseBase * (draggedPosition - basePosition);
            socket.localEulerAngles = (inverseBase * draggedRotation).eulerAngles;
            CommitSocketEdit(true);
            RebuildInspector();
        }

        /// <summary>
        /// Ends a gizmo drag, keying the result when auto-key asked for it.
        /// </summary>
        /// <remarks>
        /// The key is written on release rather than per frame so a drag is one key and one undo
        /// step, not one per pointer move.
        /// </remarks>
        private void EndGizmoDrag()
        {
            if (activeGizmoHandle == GizmoHandle.None)
            {
                return;
            }
            activeGizmoHandle = GizmoHandle.None;

            // The fork the whole mode exists for. The rule itself lives in GizmoDragRouting so it
            // can be read — and tested — as a table, rather than reconstructed from these branches.
            GizmoDragDestination destination = GizmoDragRouting.Resolve(
                selectedSocketId != 0u, IsRigEditMode, IsAutoKeyEnabled, hasPendingTransformEdit);

            switch (destination)
            {
                case GizmoDragDestination.SocketOffset:
                    CommitSocketDrag();
                    RefreshGizmo();
                    return;

                case GizmoDragDestination.RigBasePose:
                    CommitRigBaseEdit(
                        pendingTransformTargetId, pendingPosition, pendingRotationDegrees, pendingScale);
                    DiscardPendingTransformEdit();
                    break;

                case GizmoDragDestination.ClipKey:
                    CommitPendingTransformEdit();
                    break;

                // HeldClipEdit and Nothing both leave the value where ApplyTransformEdit put it:
                // held and drawn as modified, or absent. Neither writes anything on release.
            }

            RebuildInspector();
            RefreshGizmo();
        }

        /// <summary>Maps a pointer position in the image to a viewport point and the rect's aspect.</summary>
        private bool TryGetViewportPoint(
            Vector2 localPosition, out Vector2 viewportPoint, out float aspect)
        {
            viewportPoint = Vector2.zero;
            aspect = 1f;

            Rect viewportRect = previewImage.contentRect;
            if (viewportRect.width < 1f || viewportRect.height < 1f)
            {
                return false;
            }

            viewportPoint = new Vector2(
                localPosition.x / viewportRect.width,
                1f - localPosition.y / viewportRect.height);
            aspect = viewportRect.width / viewportRect.height;
            return true;
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

            if (activeGizmoHandle != GizmoHandle.None)
            {
                EndGizmoDrag();
                return;
            }

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

            int itemId;
            uint pickedSocketId;
            uint pickedTargetId;

            // Sockets are tested first because a click on one usually lands on its attachment — a
            // sword, not a cube — whose transform belongs to neither of the other two hierarchies.
            if (previewController.TryGetSocketIdForTransform(pickedTransform, out pickedSocketId))
            {
                if (!TryFindSocketItemId(pickedSocketId, out itemId))
                {
                    return;
                }
            }
            else if (previewController.TryGetTargetIdForTransform(pickedTransform, out pickedTargetId))
            {
                // A cutout part quad. Its row is a rig target, not a transform of the previewed
                // prefab, so the id comes from the target table rather than the preview hierarchy.
                if (!TryFindRigTargetItemId(pickedTargetId, out itemId))
                {
                    return;
                }
            }
            else
            {
                itemId = previewController.GetHierarchyIndex(pickedTransform);
                if (itemId < 0)
                {
                    return;
                }
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
            SelectHierarchyItem(NothingSelectedItemId);
            previousPickCandidates.Clear();
            pickCandidates.Clear();
            RebuildHierarchy();
            RebuildTimeline();
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

            hierarchyItemsById.Clear();
            List<TreeViewItemData<HierarchyItem>> rootItems = new List<TreeViewItemData<HierarchyItem>>();

            // The rig's parts come first: they are what a cutout clip animates, and they are what a
            // flipbook track binds to. Before this they appeared nowhere in the window, so a
            // flipbook track had no object to belong to.
            rootItems.AddRange(BuildRigTargetItems());

            // After the parts, before the prefab's transforms: a socket belongs to the rig, so it
            // reads with the rig's own rows rather than buried in the imported hierarchy.
            rootItems.AddRange(BuildSocketItems());

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
        private TreeViewItemData<HierarchyItem> BuildHierarchyItem(Transform transformNode)
        {
            List<TreeViewItemData<HierarchyItem>> childItems =
                new List<TreeViewItemData<HierarchyItem>>();
            for (int childIndex = 0; childIndex < transformNode.childCount; childIndex++)
            {
                childItems.Add(BuildHierarchyItem(transformNode.GetChild(childIndex)));
            }

            int itemId = previewController.GetHierarchyIndex(transformNode);
            HierarchyItem item = new HierarchyItem
            {
                kind = HierarchyItemKind.PrefabTransform,
                displayName = transformNode.name,
                previewIndex = itemId
            };
            hierarchyItemsById[itemId] = item;
            return new TreeViewItemData<HierarchyItem>(itemId, item, childItems);
        }

        /// <summary>
        /// One row per rig target, flat — a rig declares a list of parts, not a tree of them.
        /// </summary>
        private List<TreeViewItemData<HierarchyItem>> BuildRigTargetItems()
        {
            List<TreeViewItemData<HierarchyItem>> targetItems =
                new List<TreeViewItemData<HierarchyItem>>();
            if (clipSet == null || clipSet.rig == null || clipSet.rig.targets == null)
            {
                return targetItems;
            }

            for (int targetIndex = 0; targetIndex < clipSet.rig.targets.Count; targetIndex++)
            {
                RigTargetDefinition target = clipSet.rig.targets[targetIndex];
                if (target == null)
                {
                    continue;
                }

                int itemId = RigTargetItemIdBase - targetIndex;
                HierarchyItem item = new HierarchyItem
                {
                    kind = HierarchyItemKind.RigTarget,
                    displayName = string.IsNullOrEmpty(target.displayName)
                        ? "Target " + target.Id.Value.ToString()
                        : target.displayName,
                    targetId = target.Id.Value
                };
                hierarchyItemsById[itemId] = item;
                targetItems.Add(new TreeViewItemData<HierarchyItem>(itemId, item));
            }
            return targetItems;
        }

        /// <summary>
        /// One row per socket, flat, listed after the parts.
        /// </summary>
        /// <remarks>
        /// Their own rows rather than children of whatever they follow. A socket may follow a rig
        /// target or an imported bone, which live in two different trees, so nesting them would put
        /// half the sockets in one place and half in another — and a socket whose binding is broken
        /// would have nowhere at all to appear, which is exactly when the author needs to find it.
        /// </remarks>
        private List<TreeViewItemData<HierarchyItem>> BuildSocketItems()
        {
            List<TreeViewItemData<HierarchyItem>> socketItems =
                new List<TreeViewItemData<HierarchyItem>>();
            if (clipSet == null || clipSet.rig == null || clipSet.rig.sockets == null)
            {
                return socketItems;
            }

            for (int socketIndex = 0; socketIndex < clipSet.rig.sockets.Count; socketIndex++)
            {
                SocketDefinition socket = clipSet.rig.sockets[socketIndex];
                if (socket == null)
                {
                    continue;
                }

                int itemId = SocketItemIdBase - socketIndex;
                HierarchyItem item = new HierarchyItem
                {
                    kind = HierarchyItemKind.Socket,
                    displayName = DescribeSocketRow(socket),
                    socketId = socket.Id.Value
                };
                hierarchyItemsById[itemId] = item;
                socketItems.Add(new TreeViewItemData<HierarchyItem>(itemId, item));
            }
            return socketItems;
        }

        /// <summary>
        /// A socket's row label: its name, what it follows, and a mark when that resolves to nothing.
        /// </summary>
        /// <remarks>
        /// The binding is on the row rather than only in the inspector because an unresolved socket
        /// is the failure that otherwise surfaces at run time as a weapon pinned to the actor's
        /// feet. Seeing it in the list costs nothing and catches it before a bake.
        /// </remarks>
        private string DescribeSocketRow(SocketDefinition socket)
        {
            string name = string.IsNullOrEmpty(socket.displayName)
                ? "Socket " + socket.Id.Value.ToString()
                : socket.displayName;

            string follows = socket.mode == SocketAttachMode.RigTarget
                ? ResolveTargetDisplayName(socket.targetId)
                : (string.IsNullOrEmpty(socket.boneName) ? "<no bone>" : socket.boneName);

            bool resolved = previewController != null && previewController.IsSocketResolved(socket);
            return name + "  →  " + follows + (resolved ? string.Empty : "   (unresolved)");
        }

        /// <summary>The socket with this id on the loaded rig, or null.</summary>
        private SocketDefinition FindSocket(uint socketId)
        {
            if (clipSet == null || clipSet.rig == null || clipSet.rig.sockets == null)
            {
                return null;
            }
            for (int socketIndex = 0; socketIndex < clipSet.rig.sockets.Count; socketIndex++)
            {
                SocketDefinition socket = clipSet.rig.sockets[socketIndex];
                if (socket != null && socket.Id.Value == socketId)
                {
                    return socket;
                }
            }
            return null;
        }

        private int FindSocketIndex(uint socketId)
        {
            if (clipSet == null || clipSet.rig == null || clipSet.rig.sockets == null)
            {
                return -1;
            }
            for (int socketIndex = 0; socketIndex < clipSet.rig.sockets.Count; socketIndex++)
            {
                SocketDefinition socket = clipSet.rig.sockets[socketIndex];
                if (socket != null && socket.Id.Value == socketId)
                {
                    return socketIndex;
                }
            }
            return -1;
        }

        /// <summary>The name of whatever a socket follows, for path resolution.</summary>
        private string ResolveSocketFollowName(uint socketId)
        {
            SocketDefinition socket = FindSocket(socketId);
            if (socket == null)
            {
                return string.Empty;
            }
            return socket.mode == SocketAttachMode.RigTarget
                ? ResolveTargetDisplayName(socket.targetId)
                : socket.boneName;
        }

        /// <summary>
        /// One hierarchy row, wired for the two gestures that reach prefab mode.
        /// </summary>
        /// <remarks>
        /// The manipulator and the double-click callback are attached once, at construction, and
        /// read the row's <em>current</em> item through a field the bind step refreshes. Rows are
        /// recycled as the tree scrolls, so registering per bind would stack a new handler on the
        /// same element every time it came back into view.
        /// </remarks>
        private VisualElement MakeHierarchyRow()
        {
            HierarchyRowLabel label = new HierarchyRowLabel();
            label.AddToClassList(HierarchyRowUssClassName);

            label.AddManipulator(new ContextualMenuManipulator(
                menuEvent => BuildHierarchyContextMenu(menuEvent, label.item)));

            label.RegisterCallback<PointerDownEvent>(pointerEvent =>
            {
                if (pointerEvent.clickCount >= 2 && pointerEvent.button == 0)
                {
                    OpenPrefabAt(label.item);
                }
            });

            RegisterReparentDrag(label);
            return label;
        }

        /// <summary>
        /// Wires one row for drag-to-reparent, which only does anything in Rig Edit mode.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Gated on the mode for the same reason gizmos are: dragging a row is a natural way to
        /// scroll or to rearrange a selection, and a drag that silently rewrote the prefab asset
        /// would be the worst kind of surprise. Outside Rig Edit the drag never starts, so the
        /// gesture is simply not available rather than available and dangerous.
        /// </para>
        /// <para>
        /// Built on <c>DragAndDrop</c> and UI Toolkit's drag events rather than the TreeView's own
        /// drag hooks, which are not public in this Unity version. The alternative — the built-in
        /// <c>reorderable</c> flag — would reorder the <em>view</em> and leave the prefab untouched,
        /// which is precisely the parallel hierarchy this must not become.
        /// </para>
        /// </remarks>
        private void RegisterReparentDrag(HierarchyRowLabel label)
        {
            label.RegisterCallback<PointerMoveEvent>(pointerEvent =>
            {
                if (!IsRigEditMode
                    || pointerEvent.pressedButtons != 1
                    || label.item == null
                    || label.item.kind != HierarchyItemKind.PrefabTransform)
                {
                    return;
                }

                DragAndDrop.PrepareStartDrag();
                DragAndDrop.SetGenericData(ReparentDragKey, label.item);
                DragAndDrop.objectReferences = new Object[0];
                DragAndDrop.StartDrag("Reparent " + label.item.displayName);
                pointerEvent.StopPropagation();
            });

            label.RegisterCallback<DragUpdatedEvent>(dragEvent =>
            {
                DragAndDrop.visualMode = CanDropOn(label.item)
                    ? DragAndDropVisualMode.Move
                    : DragAndDropVisualMode.Rejected;
                dragEvent.StopPropagation();
            });

            label.RegisterCallback<DragPerformEvent>(dragEvent =>
            {
                HierarchyItem dragged = DragAndDrop.GetGenericData(ReparentDragKey) as HierarchyItem;
                if (CanDropOn(label.item) && dragged != null)
                {
                    DragAndDrop.AcceptDrag();
                    ReparentInPrefab(dragged, label.item);
                }
                dragEvent.StopPropagation();
            });
        }

        private const string ReparentDragKey = "DotsAnimationToolkit.ReparentItem";

        /// <summary>Whether the row under the cursor is a legal drop target for the current drag.</summary>
        private bool CanDropOn(HierarchyItem dropTarget)
        {
            if (!IsRigEditMode || dropTarget == null
                || dropTarget.kind != HierarchyItemKind.PrefabTransform)
            {
                return false;
            }

            HierarchyItem dragged = DragAndDrop.GetGenericData(ReparentDragKey) as HierarchyItem;
            if (dragged == null || dragged == dropTarget
                || dragged.kind != HierarchyItemKind.PrefabTransform)
            {
                return false;
            }

            // The deep check, asked of the preview's copy of the hierarchy. It answers the same
            // question the write would ask of the asset, so an illegal drop is a rejected cursor
            // rather than a notification after the fact.
            if (previewController == null)
            {
                return false;
            }
            Transform draggedNode = previewController.GetTransformByIndex(dragged.previewIndex);
            Transform targetNode = previewController.GetTransformByIndex(dropTarget.previewIndex);
            string ignoredError;
            return RigStructureEditor.ValidateReparent(draggedNode, targetNode, out ignoredError);
        }

        /// <summary>
        /// Moves a dragged object under the row it was dropped on, in the prefab asset.
        /// </summary>
        /// <remarks>
        /// The deep checks — parenting into your own subtree, already-a-child — live in
        /// <see cref="RigStructureEditor"/> rather than here, because they need the prefab's
        /// hierarchy and this one has only the preview's. Failure is reported rather than swallowed:
        /// a drag that appears to work and changes nothing is a bug report waiting to happen.
        /// </remarks>
        private void ReparentInPrefab(HierarchyItem dragged, HierarchyItem newParent)
        {
            GameObject prefab = LoadedPrefab;
            if (prefab == null)
            {
                ShowNotification(new GUIContent("Assign a prefab in the rig field to edit its rig."));
                return;
            }

            string childPath = ResolveHierarchyPath(dragged);
            string parentPath = ResolveHierarchyPath(newParent);

            string error;
            if (!RigStructureEditor.TryReparent(prefab, childPath, parentPath, out error))
            {
                ShowNotification(new GUIContent(error));
                return;
            }

            RememberRoundTripState();
            ReloadAfterPrefabEdit();
        }

        /// <summary>A row label that remembers which item it is currently showing.</summary>
        private sealed class HierarchyRowLabel : Label
        {
            public HierarchyItem item;
        }

        private void BindHierarchyRow(VisualElement element, int index)
        {
            HierarchyRowLabel label = element as HierarchyRowLabel;
            if (label == null)
            {
                return;
            }
            HierarchyItem item = hierarchyTreeView.GetItemDataForIndex<HierarchyItem>(index);
            label.item = item;
            if (item == null)
            {
                label.text = string.Empty;
                return;
            }
            label.text = item.displayName;

            // Bold marks something the selected clip already animates, so the tree doubles as the
            // answer to "what does this clip actually touch?".
            bool isAnimated;
            switch (item.kind)
            {
                case HierarchyItemKind.RigTarget:
                    isAnimated = CountTracksForTarget(item.targetId) > 0;
                    break;
                case HierarchyItemKind.Socket:
                    // A socket carries no keys, so bold means "this one has something pinned to it"
                    // — the reading that matters when scanning a rig for what will show up.
                    isAnimated = FindSocket(item.socketId) != null
                        && FindSocket(item.socketId).previewAttachment != null;
                    break;
                default:
                    isAnimated = FindBoneTrackIndex(item.displayName) >= 0;
                    break;
            }
            label.EnableInClassList(AnimatedBoneUssClassName, isAnimated);
            ApplyBillboardIndicator(label, item);
        }

        /// <summary>
        /// Marks a row as a billboard root, as inheriting one, or as neither (amendment A44).
        /// </summary>
        /// <remarks>
        /// <para>
        /// Three states rather than two, because "this node billboards" and "this node <em>decides</em>
        /// how it billboards" are different facts and an author acting on the wrong one edits the
        /// wrong rig row. The inherited marker names its source on hover for the same reason: knowing
        /// a node billboards is useless without knowing which root to go and change.
        /// </para>
        /// <para>
        /// This matters more than decoration. A fully billboarded node's animated rotation is
        /// replaced outright at resolve time, so keying rotation on one changes nothing visible —
        /// and without a marker in the tree that is discovered only after an afternoon of keying.
        /// </para>
        /// </remarks>
        private void ApplyBillboardIndicator(HierarchyRowLabel label, HierarchyItem item)
        {
            label.EnableInClassList(BillboardRootUssClassName, false);
            label.EnableInClassList(BillboardInheritedUssClassName, false);
            label.tooltip = string.Empty;

            RigAsset rig = clipSet != null ? clipSet.rig : null;
            if (rig == null || rig.billboardRoots == null || rig.billboardRoots.Count == 0)
            {
                return;
            }

            Transform node = ResolveHierarchyTransform(item);
            if (node == null)
            {
                return;
            }

            Transform previewRoot = previewController != null ? previewController.HierarchyRoot : null;
            List<ResolvedBillboardRoot> resolvedRoots =
                BillboardRootResolver.Resolve(rig, previewRoot, null);
            int rootIndex =
                BillboardRootResolver.FindNearestRootIndex(resolvedRoots, node, previewRoot);
            if (rootIndex < 0)
            {
                return;
            }

            BillboardRootDefinition definition = resolvedRoots[rootIndex].definition;
            string rootName = string.IsNullOrEmpty(definition.displayName)
                ? "(unnamed root)"
                : definition.displayName;

            if (resolvedRoots[rootIndex].node == node)
            {
                label.EnableInClassList(BillboardRootUssClassName, true);
                label.text = BillboardRootGlyph + label.text;
                label.tooltip = "Billboard root - " + ObjectNames.NicifyVariableName(
                    definition.mode.ToString());
                return;
            }

            label.EnableInClassList(BillboardInheritedUssClassName, true);
            label.text = BillboardInheritedGlyph + label.text;
            label.tooltip = "Billboards with «" + rootName + "»";
        }

        /// <summary>
        /// The preview transform a hierarchy row stands for, or null when it stands for none.
        /// </summary>
        /// <remarks>
        /// A rig-target row has no transform of its own — it stands for a row of the rig asset — so
        /// it resolves through the name it binds by, exactly as <see cref="ResolveHierarchyPath"/>
        /// does. A socket row resolves to whatever it follows.
        /// </remarks>
        private Transform ResolveHierarchyTransform(HierarchyItem item)
        {
            if (item == null || previewController == null)
            {
                return null;
            }
            Transform root = previewController.HierarchyRoot;
            if (root == null)
            {
                return null;
            }

            switch (item.kind)
            {
                case HierarchyItemKind.RigTarget:
                    return PrefabAuthoringBridge.FindByName(root, item.displayName);
                case HierarchyItemKind.Socket:
                    return PrefabAuthoringBridge.FindByName(root, ResolveSocketFollowName(item.socketId));
                default:
                    return previewController.GetTransformByIndex(item.previewIndex);
            }
        }

        private bool TryFindSocketItemId(uint socketId, out int itemId)
        {
            foreach (KeyValuePair<int, HierarchyItem> pair in hierarchyItemsById)
            {
                if (pair.Value.kind == HierarchyItemKind.Socket && pair.Value.socketId == socketId)
                {
                    itemId = pair.Key;
                    return true;
                }
            }
            itemId = NothingSelectedItemId;
            return false;
        }

        private bool TryFindRigTargetItemId(uint targetId, out int itemId)
        {
            foreach (KeyValuePair<int, HierarchyItem> pair in hierarchyItemsById)
            {
                if (pair.Value.kind == HierarchyItemKind.RigTarget && pair.Value.targetId == targetId)
                {
                    itemId = pair.Key;
                    return true;
                }
            }
            itemId = NothingSelectedItemId;
            return false;
        }

        /// <summary>How many transform and flipbook tracks the selected clip aims at a target.</summary>
        private int CountTracksForTarget(uint targetId)
        {
            if (selectedClip == null)
            {
                return 0;
            }

            int trackCount = 0;
            for (int trackIndex = 0;
                selectedClip.transformTracks != null && trackIndex < selectedClip.transformTracks.Count;
                trackIndex++)
            {
                TransformTrack track = selectedClip.transformTracks[trackIndex];
                if (track != null && track.targetId == targetId)
                {
                    trackCount++;
                }
            }
            for (int trackIndex = 0;
                selectedClip.spriteTracks != null && trackIndex < selectedClip.spriteTracks.Count;
                trackIndex++)
            {
                SpriteTrack track = selectedClip.spriteTracks[trackIndex];
                if (track != null && track.targetId == targetId)
                {
                    trackCount++;
                }
            }
            return trackCount;
        }

        /// <summary>
        /// The single place a hierarchy selection takes effect, whichever surface caused it.
        /// </summary>
        /// <remarks>
        /// A viewport click reaches this by setting the tree's selection rather than by doing its
        /// own thing, so "clicked in the tree" and "clicked in the viewport" cannot drift into
        /// meaning two different things.
        /// </remarks>
        /// <summary>
        /// Adopts the tree's whole selection and works out which row of it is active.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Every selected row gets its own block in the inspector and its own rows on the timeline.
        /// One of them is additionally the <em>active</em> row — the one the viewport outline and
        /// the gizmo follow, because those can only be in one place. The distinction is "which one
        /// is the gizmo on", not "which one am I editing".
        /// </para>
        /// <para>
        /// <strong>Active is the row just added, found by diffing against the previous selection
        /// rather than by taking the last of <c>selectedIndices</c>.</strong> That enumerable is
        /// ordered by row, not by when each row was clicked, so ctrl-clicking a row above an
        /// existing selection would otherwise put the gizmo on the row the user did not touch.
        /// </para>
        /// </remarks>
        private void OnHierarchySelectionChanged(IEnumerable<object> selection)
        {
            // Re-entry guard, and not an optional one. A selection change rebuilds the timeline,
            // which calls RefreshItems to redraw the tree's "animated" marks, and RefreshItems
            // re-resolves the tree's own selection — which can notify again. Without this the two
            // call each other until the stack runs out.
            if (isHandlingHierarchySelection)
            {
                return;
            }
            isHandlingHierarchySelection = true;
            try
            {
                ApplyHierarchySelectionChange();
            }
            finally
            {
                isHandlingHierarchySelection = false;
            }

            // The button names what it would do to the row now selected, so it has to follow the
            // selection rather than only the edits it makes itself.
            RefreshBillboardButtonState();
        }

        private void ApplyHierarchySelectionChange()
        {
            // An echo is not a click. RefreshItems re-resolves the tree's selection and notifies
            // with the set that is already applied; taking that for a user action cleared the key
            // selection a moment after the click that made it, so clicking a key showed the bone
            // panel instead of the key. Suppressing the notification at the RefreshItems call is
            // only half the fix — it cannot cover a notification the tree defers to a later frame.
            if (IsHierarchySelectionEcho())
            {
                return;
            }

            int previousActiveItemId = selectedHierarchyItemId;

            selectedHierarchyItems.Clear();
            int newlySelectedItemId = NothingSelectedItemId;
            bool previousActiveIsStillSelected = false;

            foreach (int selectedIndex in hierarchyTreeView.selectedIndices)
            {
                int itemId = hierarchyTreeView.GetIdForIndex(selectedIndex);
                HierarchyItem item;
                if (!hierarchyItemsById.TryGetValue(itemId, out item))
                {
                    continue;
                }
                selectedHierarchyItems.Add(item);

                if (itemId == previousActiveItemId)
                {
                    previousActiveIsStillSelected = true;
                }
                else if (!previouslySelectedItemIds.Contains(itemId))
                {
                    newlySelectedItemId = itemId;
                }
            }

            // A row was added: that is the one just clicked. Nothing was added (a row was removed
            // instead, or the whole range was replaced): keep the active row if it survived, else
            // fall back to the first of what is left.
            if (newlySelectedItemId != NothingSelectedItemId)
            {
                activeHierarchyItemId = newlySelectedItemId;
            }
            else if (!previousActiveIsStillSelected)
            {
                activeHierarchyItemId = selectedHierarchyItems.Count > 0
                    ? FindItemIdOf(selectedHierarchyItems[0])
                    : NothingSelectedItemId;
            }

            previouslySelectedItemIds.Clear();
            for (int itemIndex = 0; itemIndex < selectedHierarchyItems.Count; itemIndex++)
            {
                previouslySelectedItemIds.Add(FindItemIdOf(selectedHierarchyItems[itemIndex]));
            }

            // Key selection and hierarchy selection are one selection with two sources: showing a
            // key's values under a heading naming a different object would be a lie about what the
            // fields edit.
            selectedKeys.Clear();
            hasActiveKey = false;
            ApplyHierarchySelection();
            RebuildTimeline();
            RebuildInspector();
        }

        /// <summary>
        /// Whether the tree is reporting the selection that has already been applied.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Compares by resolved item, not only by id. A rebuilt hierarchy can hand out the same ids
        /// for freshly constructed items, and treating that as an echo would leave
        /// <see cref="selectedHierarchyItems"/> holding detached objects the inspector would then
        /// edit — a quieter bug than the one this fixes.
        /// </para>
        /// <para>
        /// A user re-clicking an already-selected row also lands here and is skipped. That is the
        /// behaviour worth having: it keeps a key selection alive across a redundant click.
        /// </para>
        /// </remarks>
        private bool IsHierarchySelectionEcho()
        {
            if (hierarchyTreeView == null)
            {
                return false;
            }

            int matchedCount = 0;
            foreach (int selectedIndex in hierarchyTreeView.selectedIndices)
            {
                int itemId = hierarchyTreeView.GetIdForIndex(selectedIndex);
                HierarchyItem item;
                if (!hierarchyItemsById.TryGetValue(itemId, out item))
                {
                    return false;
                }
                if (!previouslySelectedItemIds.Contains(itemId)
                    || !selectedHierarchyItems.Contains(item))
                {
                    return false;
                }
                matchedCount++;
            }

            // Counts as well as membership, so a selection that shrank is not mistaken for an echo
            // of the larger one it came from.
            return matchedCount == previouslySelectedItemIds.Count
                && matchedCount == selectedHierarchyItems.Count;
        }

        /// <summary>
        /// Repaints the tree's rows without letting the repaint pose as a selection change.
        /// </summary>
        /// <remarks>
        /// <c>RefreshItems</c> re-resolves the tree's selection as part of redrawing it, which
        /// raises <c>selectionChanged</c>. Every call here is a redraw — the marks for which rows
        /// are animated or billboarded — so none of them should reach the selection handler.
        /// </remarks>
        private void RefreshHierarchyRows()
        {
            if (hierarchyTreeView == null)
            {
                return;
            }
            bool wasHandlingSelection = isHandlingHierarchySelection;
            isHandlingHierarchySelection = true;
            try
            {
                hierarchyTreeView.RefreshItems();
            }
            finally
            {
                isHandlingHierarchySelection = wasHandlingSelection;
            }
        }

        /// <summary>
        /// Selects one row and nothing else — the viewport's click path and the timeline's.
        /// </summary>
        private void SelectHierarchyItem(int itemId)
        {
            selectedHierarchyItems.Clear();
            previouslySelectedItemIds.Clear();
            activeHierarchyItemId = NothingSelectedItemId;

            HierarchyItem item;
            if (hierarchyItemsById.TryGetValue(itemId, out item))
            {
                selectedHierarchyItems.Add(item);
                previouslySelectedItemIds.Add(itemId);
                activeHierarchyItemId = itemId;
            }
            ApplyHierarchySelection();
        }

        /// <summary>Points the viewport outline and the gizmo at the active row, or at nothing.</summary>
        private void ApplyHierarchySelection()
        {
            // A held edit belongs to the part it was made on; changing the selection ends it.
            DiscardPendingTransformEdit();

            HierarchyItem activeItem = ActiveHierarchyItem;
            if (activeItem == null)
            {
                selectedHierarchyItemId = NothingSelectedItemId;
                selectedBoneName = null;
                selectedTargetId = 0u;
                if (previewController != null)
                {
                    previewController.SetSelectedHierarchyIndex(-1);
                }
                return;
            }

            selectedHierarchyItemId = FindItemIdOf(activeItem);
            if (activeItem.kind == HierarchyItemKind.Socket)
            {
                selectedBoneName = null;
                selectedTargetId = 0u;
                selectedSocketId = activeItem.socketId;
                if (previewController != null)
                {
                    previewController.SetSelectedSocketId(activeItem.socketId);
                }
                return;
            }
            selectedSocketId = 0u;
            if (activeItem.kind == HierarchyItemKind.RigTarget)
            {
                selectedBoneName = null;
                selectedTargetId = activeItem.targetId;
                if (previewController != null)
                {
                    previewController.SetSelectedTargetId(activeItem.targetId);
                }
                return;
            }

            selectedBoneName = activeItem.displayName;
            selectedTargetId = 0u;
            if (previewController != null)
            {
                previewController.SetSelectedHierarchyIndex(activeItem.previewIndex);
            }
        }

        /// <summary>The row the gizmo and the outline follow: the one most recently added.</summary>
        private HierarchyItem ActiveHierarchyItem
        {
            get
            {
                HierarchyItem item;
                if (activeHierarchyItemId != NothingSelectedItemId
                    && hierarchyItemsById.TryGetValue(activeHierarchyItemId, out item)
                    && selectedHierarchyItems.Contains(item))
                {
                    return item;
                }
                return selectedHierarchyItems.Count > 0 ? selectedHierarchyItems[0] : null;
            }
        }

        private int FindItemIdOf(HierarchyItem item)
        {
            foreach (KeyValuePair<int, HierarchyItem> pair in hierarchyItemsById)
            {
                if (pair.Value == item)
                {
                    return pair.Key;
                }
            }
            return NothingSelectedItemId;
        }

        /// <summary>Whether a transform or flipbook track's target is in the current selection.</summary>
        private bool IsTargetSelected(uint targetId)
        {
            for (int itemIndex = 0; itemIndex < selectedHierarchyItems.Count; itemIndex++)
            {
                HierarchyItem item = selectedHierarchyItems[itemIndex];
                if (item.kind == HierarchyItemKind.RigTarget && item.targetId == targetId)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Whether a bone track's bone is in the current selection.</summary>
        private bool IsBoneSelected(string boneName)
        {
            if (string.IsNullOrEmpty(boneName))
            {
                return false;
            }
            for (int itemIndex = 0; itemIndex < selectedHierarchyItems.Count; itemIndex++)
            {
                HierarchyItem item = selectedHierarchyItems[itemIndex];
                if (item.kind == HierarchyItemKind.PrefabTransform && item.displayName == boneName)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Names what the timeline is focused on, for the status line.
        /// </summary>
        /// <remarks>
        /// Hiding rows without saying why would read as tracks having been lost. Past two names the
        /// list is replaced by a count, because a status line that wraps is worse than one that
        /// summarises.
        /// </remarks>
        private string DescribeSelection()
        {
            if (selectedHierarchyItems.Count == 0)
            {
                return "nothing";
            }
            if (selectedHierarchyItems.Count > 2)
            {
                return selectedHierarchyItems.Count.ToString() + " objects";
            }

            string described = string.Empty;
            for (int itemIndex = 0; itemIndex < selectedHierarchyItems.Count; itemIndex++)
            {
                if (itemIndex > 0)
                {
                    described += " + ";
                }
                described += DescribeHierarchyItemName(selectedHierarchyItems[itemIndex]);
            }
            return described;
        }

        private string DescribeHierarchyItemName(HierarchyItem item)
        {
            return item.kind == HierarchyItemKind.RigTarget
                ? ResolveTargetDisplayName(item.targetId)
                : item.displayName;
        }

        /// <summary>Deselects everywhere at once — tree, viewport outline and inspector.</summary>
        private void ClearHierarchySelection()
        {
            // Not "< 0": rig-target rows carry negative ids, so a less-than test would treat every
            // selected part as nothing selected and refuse to clear it.
            if (selectedHierarchyItemId == NothingSelectedItemId)
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
            SelectHierarchyItem(NothingSelectedItemId);
            // The timeline is rebuilt too: with nothing selected the focus filter lifts, and the
            // rows it was hiding have to come back or clearing the selection would look like it
            // deleted them.
            RebuildTimeline();
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
            RefreshClipActionButtons();

            // The hierarchy lists the set's rig targets, so it is stale the moment the set changes.
            // It used to be rebuilt only when the previewed prefab changed, which was enough while
            // the pane showed nothing but that prefab's transforms.
            SelectHierarchyItem(NothingSelectedItemId);
            RebuildHierarchy();

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
            hasActiveKey = false;
            SetPlaying(false);
            playheadTime = 0f;
            RefreshSerializedClip();
            RefreshClipActionButtons();
            RebuildTimeline();

            // The bar is bound once, while nothing is selected, so its fields start disabled and
            // showing zero. Without this they stay that way for the rest of the session and the
            // clip length simply cannot be typed into.
            SyncTransportFromClip();
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
            RefreshPlayButtonState();
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
                float speed = playbackSpeedField != null ? playbackSpeedField.value : 1f;
                float advanced = playheadTime + (float)elapsed * speed / duration;

                bool shouldLoop = loopToggle == null || loopToggle.value;
                if (shouldLoop)
                {
                    // Floor rather than a modulo so a negative speed wraps to the END, which is
                    // what "loop" means when playing backwards.
                    advanced -= Mathf.Floor(advanced);
                }
                else if (advanced >= 1f || advanced < 0f)
                {
                    // Stop AT the boundary rather than past it, and drop out of play, so the
                    // transport agrees with what the viewport is showing.
                    advanced = Mathf.Clamp01(advanced);
                    SetPlaying(false);
                }
                SetPlayheadTime(advanced);
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
            float clampedTime = Mathf.Clamp01(normalizedTime);

            // A held edit describes the part at one instant, so moving off that instant ends it.
            // Carrying it along would silently apply a value the user never keyed to a time they
            // never looked at.
            if (hasPendingTransformEdit && !Mathf.Approximately(clampedTime, playheadTime))
            {
                DiscardPendingTransformEdit();
                RebuildInspector();
            }

            playheadTime = clampedTime;
            if (playhead != null)
            {
                playhead.NormalizedTime = playheadTime;
            }
            SyncTransportPlayhead();

            // The inspector shows the value at the playhead, so it moves with it. In place rather
            // than by rebuilding: a rebuild would destroy the field being typed into.
            RefreshLiveInspectorValues();
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
                return TransportFrameCount;
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
            RefreshHierarchyRows();

            if (selectedClip == null)
            {
                statusLabel.text = clipSet == null ? "Assign a clip set." : "Select a clip.";
                RebuildInspector();
                return;
            }

            // Focus mode: with a selection, the timeline shows only that selection's tracks. It is
            // what makes a busy clip readable — but a row that has silently vanished is worse than a
            // busy timeline, so the status line always says what is being hidden and how to undo it.
            bool isFocused = selectedHierarchyItems.Count > 0;
            int hiddenTrackCount = 0;

            statusLabel.text = selectedClip.name
                + "   duration " + selectedClip.duration.ToString("0.###") + "s"
                + "   loop " + selectedClip.defaultLoop.ToString()
                + "   selected " + selectedKeys.Count.ToString();

            ruler.durationSeconds = selectedClip.duration;
            ruler.frameCount = TransportFrameCount;
            ruler.RefreshSecondLabels();
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
                if (isFocused && !IsTargetSelected(track.targetId))
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
                    "T " + track.targetId.ToString() + "  " + track.channels.ToString(),
                    TimelineTrackKind.Transform, trackIndex, times, ref rowIndex);
            }

            List<SpriteTrack> spriteTracks = selectedClip.spriteTracks;
            for (int trackIndex = 0; spriteTracks != null && trackIndex < spriteTracks.Count; trackIndex++)
            {
                SpriteTrack track = spriteTracks[trackIndex];
                if (track == null)
                {
                    continue;
                }
                if (isFocused && !IsTargetSelected(track.targetId))
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
                    "S " + track.targetId.ToString() + "  " + track.mode.ToString(),
                    TimelineTrackKind.Sprite, trackIndex, times, ref rowIndex);
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
                AddTrackRow(
                    "B " + (string.IsNullOrEmpty(track.boneName) ? "<unnamed bone>" : track.boneName),
                    TimelineTrackKind.Bone, trackIndex, times, ref rowIndex);
            }

            if (selectedClip.events != null)
            {
                times.Clear();
                for (int eventIndex = 0; eventIndex < selectedClip.events.Count; eventIndex++)
                {
                    times.Add(selectedClip.events[eventIndex].normalizedTime);
                }
                // Events stay visible while focused. They belong to the clip rather than to any one
                // part, so hiding them would make event authoring impossible the moment anything
                // was selected — and they are one row, not the clutter focus exists to remove.
                AddTrackRow("Events", TimelineTrackKind.Event, 0, times, ref rowIndex);
            }

            if (isFocused)
            {
                statusLabel.text += "   ·   focused on " + DescribeSelection()
                    + (hiddenTrackCount > 0
                        ? " (" + hiddenTrackCount.ToString() + " track(s) hidden — deselect to show all)"
                        : string.Empty);
            }

            SetPlayheadTime(playheadTime);
            RebuildInspector();
        }

        /// <summary>
        /// Adds a track's row and, when it is expanded, one row per animated channel.
        /// </summary>
        /// <remarks>
        /// <strong>Channel rows show the same keys as their track, not keys of their own.</strong>
        /// One <c>TransformKey</c> carries position, rotation and scale together, so the rows are a
        /// reading of one set of keys rather than independent curves — dragging a key on any of them
        /// retimes the one underlying key. Independent per-channel keying would mean splitting the
        /// key struct into per-channel curves, which changes the blob, the sampler and every baked
        /// clip; it is not something the dopesheet can decide on its own.
        /// </remarks>
        private void AddTrackRow(
            string headerText, TimelineTrackKind trackKind, int trackIndex, List<float> times,
            ref int rowIndex)
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

            Label headerLabel = new Label(headerText);
            headerLabel.AddToClassList(TrackHeaderLabelUssClassName);
            headerLabel.tooltip = headerText;
            headerRow.Add(headerLabel);
            trackHeaderColumn.Add(headerRow);

            AddLane(trackKind, trackIndex, times, rowIndex, false);
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

        /// <summary>
        /// Each event marker's window length as a fraction of the clip, for the lane to bar.
        /// </summary>
        /// <remarks>
        /// Normalized here rather than in the lane because the lane draws in normalized time and
        /// has no idea what the clip's duration is — and the window is authored in seconds, so
        /// something has to divide.
        /// </remarks>
        private List<float> CollectEventWindowLengths()
        {
            eventWindowLengths.Clear();
            if (selectedClip == null || selectedClip.events == null)
            {
                return eventWindowLengths;
            }

            float duration = Mathf.Max(selectedClip.duration, ClipAsset.MinimumDuration);
            for (int eventIndex = 0; eventIndex < selectedClip.events.Count; eventIndex++)
            {
                eventWindowLengths.Add(selectedClip.events[eventIndex].windowSeconds / duration);
            }
            return eventWindowLengths;
        }

        private void AddLane(
            TimelineTrackKind trackKind, int trackIndex, List<float> times, int rowIndex,
            bool isChannelRow)
        {
            TrackLaneElement lane = new TrackLaneElement
            {
                trackKind = trackKind,
                trackIndex = trackIndex,
                isAlternateRow = (rowIndex & 1) == 1,
                isChannelRow = isChannelRow,
                isKeySelected = selectedKeys.Contains
            };
            lane.SetKeyTimes(times);
            if (trackKind == TimelineTrackKind.Event)
            {
                lane.SetKeyWindows(CollectEventWindowLengths());
            }
            lane.keyPointerDown += OnKeyPointerDown;
            lane.lanePointerDown += OnLanePointerDown;
            laneColumn.Add(lane);
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
                hasActiveKey = false;
            }
            if (additive && selectedKeys.Contains(address))
            {
                selectedKeys.Remove(address);
                // Deselecting the active key hands the panel back to whatever remains, rather than
                // leaving it editing a key that is no longer selected.
                hasActiveKey = false;
            }
            else
            {
                selectedKeys.Add(address);
                activeKey = address;
                hasActiveKey = true;
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

            int itemId = NothingSelectedItemId;
            if (!string.IsNullOrEmpty(boneName) && previewController != null)
            {
                int previewIndex = previewController.FindHierarchyIndexByName(boneName);
                if (previewIndex >= 0)
                {
                    itemId = previewIndex;
                }
            }
            SelectHierarchyItem(itemId);

            if (hierarchyTreeView == null)
            {
                return;
            }
            if (itemId != NothingSelectedItemId)
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
                bool additive = pointerEvent.shiftKey || pointerEvent.ctrlKey || pointerEvent.commandKey;
                if (!additive)
                {
                    selectedKeys.Clear();
                    hasActiveKey = false;
                    RepaintLanes();
                    RebuildInspector();
                }
                SetPlayheadTime(normalizedTime);

                // The same press can still become a box select. It only becomes one once the pointer
                // has actually travelled, so a plain click keeps meaning "move the playhead here".
                BeginBoxSelect(pointerEvent, additive);
                return;
            }

            float insertTime = TimelineGeometry.Snap(normalizedTime, SnapFrameCount);
            BeginUndoGesture("Add Animation Key");
            InsertKey(trackKind, trackIndex, insertTime);
            EndUndoGesture();

            EditorUtility.SetDirty(selectedClip);
            selectedKeys.Clear();
            hasActiveKey = false;
            SortTrackKeys(trackKind, trackIndex);
            SetPlayheadTime(insertTime);
            RebuildTimeline();
        }

        // -------------------------------------------------------------------------------------
        // Box selection
        // -------------------------------------------------------------------------------------

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
                // It was a click after all; the playhead already moved on press.
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

        /// <summary>
        /// Adds every key whose lane row and time fall inside the band.
        /// </summary>
        /// <remarks>
        /// A channel row and its track row select the same key, so a band covering both simply
        /// yields that key once — <see cref="selectedKeys"/> is a set, and the duplicate collapses
        /// rather than needing a special case.
        /// </remarks>
        private void SelectKeysInsideBand(Rect bandRect)
        {
            if (!isBoxSelectAdditive)
            {
                selectedKeys.Clear();
            hasActiveKey = false;
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

                TimelineGeometry geometry = TimelineGeometry.Create(lane.contentRect.width);
                IReadOnlyList<float> keyTimes = lane.KeyTimes;
                for (int keyIndex = 0; keyIndex < keyTimes.Count; keyIndex++)
                {
                    float keyXInLane = geometry.TimeToX(keyTimes[keyIndex]);
                    float keyXInStack =
                        lane.ChangeCoordinatesTo(laneStack, new Vector2(keyXInLane, 0f)).x;
                    if (keyXInStack >= bandRect.xMin && keyXInStack <= bandRect.xMax)
                    {
                        selectedKeys.Add(
                            new KeyAddress(lane.trackKind, lane.trackIndex, keyIndex));
                    }
                }
            }
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
            float frameStep = 1f / Mathf.Max(1, TransportFrameCount);

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
            hasActiveKey = false;
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
                    // Never key 0. The struct's default is the reserved "invalid" key, so a marker
                    // placed and left alone used to fail validation rule V09 — the clip broke at
                    // bake for having been authored, which is the worst possible default. A new
                    // marker takes the clip set's first registered event when there is one, and the
                    // first legal user key when there is not.
                    selectedClip.events.Add(new EventMarker
                    {
                        normalizedTime = normalizedTime,
                        eventKey = ResolveNewEventKey(),
                        windowSeconds = ResolveNewEventWindowSeconds()
                    });
                    break;
                }
            }
        }

        /// <summary>The event a newly placed marker fires before anyone has chosen one.</summary>
        private uint ResolveNewEventKey()
        {
            AnimEventKeyEntry firstEntry = FindFirstRegistryEntry();
            return firstEntry != null ? firstEntry.eventKey : AnimEventMaskKeys.FirstMaskKey;
        }

        /// <summary>That event's default window, if it has one.</summary>
        private float ResolveNewEventWindowSeconds()
        {
            AnimEventKeyEntry firstEntry = FindFirstRegistryEntry();
            if (firstEntry == null || firstEntry.defaultWindowFrames <= 0)
            {
                return 0f;
            }
            return firstEntry.defaultWindowFrames
                / ResolveReferenceFrameRate(clipSet != null ? clipSet.eventKeys : null);
        }

        /// <summary>The clip set's first named event, or null when it names none.</summary>
        private AnimEventKeyEntry FindFirstRegistryEntry()
        {
            AnimEventKeyRegistry registry = clipSet != null ? clipSet.eventKeys : null;
            if (registry == null || registry.entries == null)
            {
                return null;
            }
            for (int entryIndex = 0; entryIndex < registry.entries.Count; entryIndex++)
            {
                if (registry.entries[entryIndex] != null)
                {
                    return registry.entries[entryIndex];
                }
            }
            return null;
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
        /// <remarks>
        /// <strong>Dragging a key past a neighbour reorders, it does not clamp.</strong> Keys move
        /// freely for the whole gesture and the list is sorted here, on release, so a key dragged
        /// over another ends up on the far side of it. Clamping was the alternative and would have
        /// been easier to implement, but it makes the common retiming edit — pulling a pose earlier
        /// than the one before it — impossible without first moving the other key out of the way.
        /// The cost is that indices change, which is why the selection is cleared below.
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
            hasActiveKey = false;
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
            hasActiveKey = false;
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
        /// <summary>One flipbook track's live fields, so a scrub can update them without a rebuild.</summary>
        private sealed class LiveFlipbookBinding
        {
            public SpriteTrack track;
            public IntegerField valueField;
            public EnumField indexModeField;
            public Label resolvedLabel;
            public Label stateHint;
        }

        /// <summary>One selected object's transform fields, so a scrub can update them in place.</summary>
        /// <remarks>
        /// One of these per selected object rather than one per window: with several parts selected
        /// the panel is a stack of blocks, and a single set of field references would leave every
        /// block but the last frozen at the value it was built with.
        /// </remarks>
        private sealed class LiveTransformBinding
        {
            /// <summary>The rig target this block edits; 0 when <see cref="boneTrack"/> is set.</summary>
            public uint targetId;

            /// <summary>The bone track this block edits; null for a rig target.</summary>
            public BoneTrack boneTrack;

            public VisualElement block;
            public Label stateChip;
            public Vector3Field positionField;
            public Vector3Field rotationField;
            public Vector3Field scaleField;
        }

        private readonly List<LiveTransformBinding> liveTransformBindings =
            new List<LiveTransformBinding>();
        private readonly List<LiveFlipbookBinding> liveFlipbookBindings =
            new List<LiveFlipbookBinding>();

        private void ClearLiveInspectorBindings()
        {
            liveTransformBindings.Clear();
            liveFlipbookBindings.Clear();
        }

        /// <summary>
        /// Pushes the value at the playhead into the fields already on screen.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>This is what makes the panel live.</strong> Scrubbing used to leave it showing
        /// whatever it held when the selection last changed, which made it a snapshot wearing a
        /// live panel's clothes. Rebuilding the pane per tick would have been the easy fix and the
        /// wrong one: it destroys and recreates the very field a user is typing into.
        /// </para>
        /// <para>
        /// A focused field is therefore skipped rather than overwritten. Half-typed text is a value
        /// the user is in the middle of authoring, and a scrub that stamped over it would be the
        /// panel arguing with the person using it.
        /// </para>
        /// </remarks>
        private void RefreshLiveInspectorValues()
        {
            for (int bindingIndex = 0; bindingIndex < liveTransformBindings.Count; bindingIndex++)
            {
                RefreshLiveTransformBinding(liveTransformBindings[bindingIndex]);
            }

            for (int bindingIndex = 0; bindingIndex < liveFlipbookBindings.Count; bindingIndex++)
            {
                RefreshLiveFlipbookBinding(liveFlipbookBindings[bindingIndex]);
            }
        }

        private void RefreshLiveTransformBinding(LiveTransformBinding binding)
        {
            if (binding.boneTrack != null)
            {
                RefreshLiveBoneValues(binding);
                return;
            }

            float3 position;
            float3 rotationDegrees;
            float3 scale;
            TransformValueState valueState = ResolveDisplayedTransform(
                binding.targetId, out position, out rotationDegrees, out scale);

            SetVectorWithoutDisturbingEdit(
                binding.positionField, new Vector3(position.x, position.y, position.z));
            SetVectorWithoutDisturbingEdit(
                binding.rotationField,
                new Vector3(rotationDegrees.x, rotationDegrees.y, rotationDegrees.z));
            SetVectorWithoutDisturbingEdit(
                binding.scaleField, new Vector3(scale.x, scale.y, scale.z));

            if (binding.stateChip != null)
            {
                binding.stateChip.text = DescribeTransformState(valueState);
                binding.stateChip.EnableInClassList(
                    TransformModifiedUssClassName, valueState == TransformValueState.Modified);
            }
            if (binding.block != null)
            {
                binding.block.EnableInClassList(
                    TransformOnKeyUssClassName, valueState == TransformValueState.OnKey);
                binding.block.EnableInClassList(
                    TransformInterpolatedUssClassName,
                    valueState == TransformValueState.Interpolated);
                binding.block.EnableInClassList(
                    TransformModifiedUssClassName, valueState == TransformValueState.Modified);
            }
        }

        private void RefreshLiveBoneValues(LiveTransformBinding binding)
        {
            float3 position;
            float3 rotationDegrees;
            float3 scale;
            bool hasKeys = ClipBoneEditing.TryEvaluate(
                binding.boneTrack, playheadTime, out position, out rotationDegrees, out scale);
            bool isOnKey = ClipBoneEditing.FindKeyIndexAt(binding.boneTrack, playheadTime) >= 0;

            SetVectorWithoutDisturbingEdit(
                binding.positionField, new Vector3(position.x, position.y, position.z));
            SetVectorWithoutDisturbingEdit(
                binding.rotationField,
                new Vector3(rotationDegrees.x, rotationDegrees.y, rotationDegrees.z));
            SetVectorWithoutDisturbingEdit(
                binding.scaleField, new Vector3(scale.x, scale.y, scale.z));

            if (binding.stateChip != null)
            {
                binding.stateChip.text = DescribeBoneState(hasKeys, isOnKey);
            }
            if (binding.block != null)
            {
                binding.block.EnableInClassList(TransformOnKeyUssClassName, isOnKey);
                binding.block.EnableInClassList(
                    TransformInterpolatedUssClassName, hasKeys && !isOnKey);
            }
        }

        private void RefreshLiveFlipbookBinding(LiveFlipbookBinding binding)
        {
            SpriteTrack track = binding.track;
            if (track == null || track.keys == null || track.keys.Count == 0)
            {
                return;
            }

            int effectiveKeyIndex = ClipSpriteEditing.FindEffectiveKeyIndex(track, playheadTime);
            if (effectiveKeyIndex < 0)
            {
                return;
            }
            SpriteKey currentKey = track.keys[effectiveKeyIndex];

            if (binding.valueField != null && !IsBeingEdited(binding.valueField))
            {
                binding.valueField.SetValueWithoutNotify(currentKey.sliceIndex);
            }
            if (binding.indexModeField != null && !IsBeingEdited(binding.indexModeField))
            {
                binding.indexModeField.SetValueWithoutNotify(currentKey.indexMode);
            }
            if (binding.resolvedLabel != null)
            {
                ApplyFlipbookResolvedLabel(binding.resolvedLabel, currentKey, track.baseIndex);
            }
            if (binding.stateHint != null)
            {
                binding.stateHint.text =
                    ClipSpriteEditing.FindKeyIndexAt(track, playheadTime) >= 0
                        ? "On a key — editing changes this key."
                        : "Held from an earlier key — editing keys the value here.";
            }
        }

        private static void SetVectorWithoutDisturbingEdit(Vector3Field field, Vector3 value)
        {
            if (field == null || IsBeingEdited(field))
            {
                return;
            }
            field.SetValueWithoutNotify(value);
        }

        /// <summary>Whether the user currently has focus inside a field.</summary>
        private static bool IsBeingEdited(VisualElement field)
        {
            if (field == null || field.panel == null)
            {
                return false;
            }
            VisualElement focused = field.panel.focusController.focusedElement as VisualElement;
            return focused != null && (focused == field || field.Contains(focused));
        }

        private void RebuildInspector()
        {
            if (inspectorPane == null)
            {
                return;
            }
            inspectorPane.Clear();
            ClearLiveInspectorBindings();

            if (selectedKeys.Count > 0 && BuildKeyInspector())
            {
                return;
            }

            // One labelled block per selected object, in pick order. With a single selection this
            // is exactly the old panel plus a name; with several it is the only way to tell whose
            // numbers are whose.
            if (selectedHierarchyItems.Count > 0)
            {
                // Only marked when there is more than one block: with a single selection every
                // block is the active one, and saying so is noise.
                HierarchyItem activeItem =
                    selectedHierarchyItems.Count > 1 ? ActiveHierarchyItem : null;

                for (int itemIndex = 0; itemIndex < selectedHierarchyItems.Count; itemIndex++)
                {
                    HierarchyItem item = selectedHierarchyItems[itemIndex];
                    bool isActive = item == activeItem;
                    switch (item.kind)
                    {
                        case HierarchyItemKind.RigTarget:
                            BuildRigTargetInspector(item, isActive);
                            break;
                        case HierarchyItemKind.Socket:
                            BuildSocketInspector(item, isActive);
                            break;
                        default:
                            BuildBoneInspector(item, isActive);
                            break;
                    }
                }
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
            // The clicked key, when one is known. Falling back to an arbitrary set member only
            // happens for selections made without a click, such as a box select.
            KeyAddress shown = default(KeyAddress);
            if (hasActiveKey && selectedKeys.Contains(activeKey))
            {
                shown = activeKey;
            }
            else
            {
                foreach (KeyAddress address in selectedKeys)
                {
                    shown = address;
                }
            }

            SerializedProperty keyProperty = FindKeyProperty(shown);
            if (keyProperty == null)
            {
                return false;
            }

            inspectorPane.Add(MakeHeading(
                shown.trackKind.ToString() + " key at "
                + GetKeyTime(shown).ToString("0.###")));
            if (selectedKeys.Count > 1)
            {
                inspectorPane.Add(MakeHint(
                    selectedKeys.Count.ToString() + " selected — editing the last."));
            }

            // A flipbook key gets purpose-built fields rather than the generic property drawer,
            // because its stored number is only meaningful beside its mode and its track's base —
            // three fields the drawer renders as three unrelated numbers.
            if (shown.trackKind == TimelineTrackKind.Sprite)
            {
                AddSelectedFlipbookKeyFields(shown);
                return true;
            }

            // An event marker gets purpose-built fields for the same reason a flipbook key does: the
            // generic drawer renders its key as a bare uint the author has to know the meaning of,
            // and its window as a number of seconds nobody times animation in.
            if (shown.trackKind == TimelineTrackKind.Event)
            {
                AddSelectedEventMarkerFields(shown);
                return true;
            }

            inspectorPane.Add(new PropertyField(keyProperty));
            inspectorPane.Bind(clipSerializedObject);

            AddInterpolationControls(shown);
            return true;
        }

        /// <summary>
        /// The selected event marker: which event it is, how long its window runs, and its payload.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>The window edits in frames and stores seconds.</strong> Animation is timed in
        /// frames and gameplay has to be framerate-independent, so the conversion happens here, at
        /// the one point where a person is looking at the number. The resolved seconds are shown
        /// beside the field so the stored value is never hidden.
        /// </para>
        /// <para>
        /// <strong>The key is a dropdown only when the clip set names its events.</strong> Without a
        /// registry the field falls back to a raw number rather than disabling itself — a project
        /// that has not made a registry yet can still author events, and the toolkit ships without
        /// requiring one.
        /// </para>
        /// </remarks>
        private void AddSelectedEventMarkerFields(KeyAddress address)
        {
            if (selectedClip.events == null || address.keyIndex >= selectedClip.events.Count)
            {
                return;
            }

            EventMarker marker = selectedClip.events[address.keyIndex];
            AnimEventKeyRegistry registry = clipSet != null ? clipSet.eventKeys : null;

            AddEventKeyField(address, marker, registry);
            AddEventWindowField(address, marker, registry);

            IntegerField intParamField = new IntegerField("Int Param");
            intParamField.tooltip =
                "Delivered on the AnimEventOutput pulse. Not carried by the window mask.";
            intParamField.SetValueWithoutNotify(marker.intParam);
            intParamField.RegisterValueChangedCallback(changeEvent =>
            {
                EditEventMarker(address, "Edit Event Payload", editedMarker =>
                {
                    editedMarker.intParam = changeEvent.newValue;
                    return editedMarker;
                });
            });
            inspectorPane.Add(intParamField);

            FloatField floatParamField = new FloatField("Float Param");
            floatParamField.tooltip =
                "Delivered on the AnimEventOutput pulse. Not carried by the window mask.";
            floatParamField.SetValueWithoutNotify(marker.floatParam);
            floatParamField.RegisterValueChangedCallback(changeEvent =>
            {
                EditEventMarker(address, "Edit Event Payload", editedMarker =>
                {
                    editedMarker.floatParam = changeEvent.newValue;
                    return editedMarker;
                });
            });
            inspectorPane.Add(floatParamField);
        }

        /// <summary>Which event this marker fires — a named dropdown, or a raw key without a registry.</summary>
        private void AddEventKeyField(
            KeyAddress address, EventMarker marker, AnimEventKeyRegistry registry)
        {
            if (registry == null || registry.entries == null || registry.entries.Count == 0)
            {
                IntegerField rawKeyField = new IntegerField("Event Key");
                rawKeyField.tooltip =
                    "Raw event key. Assign an Anim Event Key Registry to the clip set to pick "
                    + "events by name instead.";
                rawKeyField.SetValueWithoutNotify((int)marker.eventKey);
                rawKeyField.RegisterValueChangedCallback(changeEvent =>
                {
                    EditEventMarker(address, "Edit Event Key", editedMarker =>
                    {
                        editedMarker.eventKey = (uint)Mathf.Max(0, changeEvent.newValue);
                        return editedMarker;
                    });
                });
                inspectorPane.Add(rawKeyField);
                inspectorPane.Add(MakeHint(DescribeEventKey(marker.eventKey, null)));
                return;
            }

            List<string> choices = new List<string>();
            int selectedChoice = -1;
            for (int entryIndex = 0; entryIndex < registry.entries.Count; entryIndex++)
            {
                AnimEventKeyEntry entry = registry.entries[entryIndex];
                if (entry == null)
                {
                    continue;
                }
                if (entry.eventKey == marker.eventKey)
                {
                    selectedChoice = choices.Count;
                }
                choices.Add(string.IsNullOrEmpty(entry.name)
                    ? "<unnamed> (" + entry.eventKey + ")"
                    : entry.name);
            }

            // A marker whose key the registry does not list keeps its number and says so, rather
            // than being silently snapped onto whichever event happens to be first. Repointing an
            // authored hit frame at a different event is not something a repaint should do.
            if (selectedChoice < 0)
            {
                choices.Add("Unlisted key " + marker.eventKey);
                selectedChoice = choices.Count - 1;
            }

            PopupField<string> keyField =
                new PopupField<string>("Event", choices, selectedChoice);
            keyField.RegisterValueChangedCallback(changeEvent =>
            {
                int choiceIndex = choices.IndexOf(changeEvent.newValue);
                AnimEventKeyEntry chosen = FindRegistryEntry(registry, choiceIndex);
                if (chosen == null)
                {
                    return;
                }
                EditEventMarker(address, "Change Event Key", editedMarker =>
                {
                    editedMarker.eventKey = chosen.eventKey;

                    // The registry's default window applies only when the marker has none of its
                    // own, so re-pointing a hand-tuned six-frame window at another event does not
                    // quietly reset it to that event's default.
                    if (editedMarker.windowSeconds <= 0f && chosen.defaultWindowFrames > 0)
                    {
                        editedMarker.windowSeconds =
                            chosen.defaultWindowFrames / ResolveReferenceFrameRate(registry);
                    }
                    return editedMarker;
                });
            });
            inspectorPane.Add(keyField);
            inspectorPane.Add(MakeHint(DescribeEventKey(marker.eventKey, registry)));
        }

        /// <summary>How long the marker holds its mask bit, edited in frames.</summary>
        private void AddEventWindowField(
            KeyAddress address, EventMarker marker, AnimEventKeyRegistry registry)
        {
            float frameRate = ResolveReferenceFrameRate(registry);

            IntegerField windowField = new IntegerField("Window (frames)");
            windowField.tooltip =
                "How many frames the event's AnimEventMask bit stays open. 0 makes it pulse-only: "
                + "it still fires with its payload, it just holds no state.";
            windowField.SetValueWithoutNotify(Mathf.RoundToInt(marker.windowSeconds * frameRate));
            windowField.RegisterValueChangedCallback(changeEvent =>
            {
                EditEventMarker(address, "Edit Event Window", editedMarker =>
                {
                    editedMarker.windowSeconds = Mathf.Max(0, changeEvent.newValue) / frameRate;
                    return editedMarker;
                });
            });
            inspectorPane.Add(windowField);

            if (marker.windowSeconds > 0f)
            {
                inspectorPane.Add(MakeHint(
                    marker.windowSeconds.ToString("0.###") + "s at "
                    + frameRate.ToString("0.##") + " fps"));
            }
        }

        /// <summary>The entry at a dropdown position, skipping the nulls the choice list skipped.</summary>
        private static AnimEventKeyEntry FindRegistryEntry(
            AnimEventKeyRegistry registry, int choiceIndex)
        {
            if (choiceIndex < 0)
            {
                return null;
            }
            int position = 0;
            for (int entryIndex = 0; entryIndex < registry.entries.Count; entryIndex++)
            {
                AnimEventKeyEntry entry = registry.entries[entryIndex];
                if (entry == null)
                {
                    continue;
                }
                if (position == choiceIndex)
                {
                    return entry;
                }
                position++;
            }
            return null;
        }

        /// <summary>The one-line status under the key field: its number, and whether it can hold a window.</summary>
        private static string DescribeEventKey(uint eventKey, AnimEventKeyRegistry registry)
        {
            if (eventKey < (uint)ReservedEventKeys.FirstUserKey)
            {
                return "Key " + eventKey + " is reserved by the package — this clip will fail "
                    + "validation (V09).";
            }
            if (!AnimEventMaskKeys.IsMaskable(eventKey))
            {
                return "Key " + eventKey + " · pulse-only (outside the maskable range "
                    + AnimEventMaskKeys.FirstMaskKey + "–" + AnimEventMaskKeys.LastMaskKey
                    + ", so a window here would never open).";
            }
            string registeredName = registry != null ? registry.FindName(eventKey) : null;
            string namePrefix = registeredName != null ? registeredName + " · " : string.Empty;
            return namePrefix + "key " + eventKey + " · mask bit "
                + (eventKey - AnimEventMaskKeys.FirstMaskKey) + ".";
        }

        /// <summary>The registry's display rate, or the package default when there is no registry.</summary>
        private static float ResolveReferenceFrameRate(AnimEventKeyRegistry registry)
        {
            if (registry == null || registry.referenceFrameRate < 1f)
            {
                return AnimEventKeyRegistry.DefaultReferenceFrameRate;
            }
            return registry.referenceFrameRate;
        }

        /// <summary>
        /// Applies one undoable edit to an event marker and refreshes what shows it.
        /// </summary>
        /// <remarks>
        /// The timeline is rebuilt as well as the inspector because an event marker's window is
        /// drawn on the lane — editing the number without redrawing the bar would leave the two
        /// disagreeing until something else happened to trigger a rebuild.
        /// </remarks>
        private void EditEventMarker(
            KeyAddress address, string undoLabel, System.Func<EventMarker, EventMarker> edit)
        {
            if (selectedClip.events == null || address.keyIndex >= selectedClip.events.Count)
            {
                return;
            }
            RecordClipEdit(undoLabel);
            selectedClip.events[address.keyIndex] = edit(selectedClip.events[address.keyIndex]);
            CommitClipEdit();
            RebuildTimeline();
        }

        /// <summary>The selected flipbook key: stored value, mode, and what it resolves to.</summary>
        private void AddSelectedFlipbookKeyFields(KeyAddress address)
        {
            if (selectedClip.spriteTracks == null
                || address.trackIndex >= selectedClip.spriteTracks.Count)
            {
                return;
            }
            SpriteTrack track = selectedClip.spriteTracks[address.trackIndex];
            if (track == null || track.keys == null || address.keyIndex >= track.keys.Count)
            {
                return;
            }

            SpriteKey key = track.keys[address.keyIndex];

            IntegerField valueField = new IntegerField("Index");
            valueField.SetValueWithoutNotify(key.sliceIndex);
            valueField.RegisterValueChangedCallback(changeEvent =>
            {
                RecordClipEdit("Edit Flipbook Key");
                SpriteKey editedKey = track.keys[address.keyIndex];
                editedKey.sliceIndex = changeEvent.newValue;
                track.keys[address.keyIndex] = editedKey;
                CommitClipEdit();
                RebuildInspector();
            });
            inspectorPane.Add(valueField);

            EnumField indexModeField = new EnumField("Index Mode", key.indexMode);
            indexModeField.RegisterValueChangedCallback(changeEvent =>
            {
                ToggleFlipbookKeyMode(
                    track, address.keyIndex, (SpriteIndexMode)changeEvent.newValue);
            });
            inspectorPane.Add(indexModeField);

            inspectorPane.Add(MakeFlipbookResolvedLabel(key, track.baseIndex));

            IntegerField baseIndexField = new IntegerField("Base Index");
            baseIndexField.SetValueWithoutNotify(track.baseIndex);
            baseIndexField.tooltip = "Shared by every relative key on this track.";
            baseIndexField.RegisterValueChangedCallback(changeEvent =>
            {
                RecordClipEdit("Change Flipbook Base Index");
                track.baseIndex = changeEvent.newValue;
                CommitClipEdit();
                RebuildInspector();
            });
            inspectorPane.Add(baseIndexField);
        }

        /// <summary>
        /// The selected key's easing, plus handle editing when that easing is a curve.
        /// </summary>
        /// <remarks>
        /// Only transform and bone keys have easing. A flipbook key is chosen by nearest neighbour
        /// rather than blended — an index cannot be halfway between two frames — so offering it an
        /// interpolation mode would be offering a setting with no effect.
        /// </remarks>
        private void AddInterpolationControls(KeyAddress address)
        {
            if (address.trackKind != TimelineTrackKind.Transform
                && address.trackKind != TimelineTrackKind.Bone)
            {
                return;
            }

            Interpolation currentInterpolation = GetKeyInterpolation(address);
            inspectorPane.Add(MakeHeading("Easing"));

            EnumField interpolationField = new EnumField("Interpolation", currentInterpolation);
            interpolationField.tooltip =
                "How the curve leaves this key on its way to the next one.";
            interpolationField.RegisterValueChangedCallback(changeEvent =>
            {
                SetKeyInterpolation(address, (Interpolation)changeEvent.newValue);
                RebuildInspector();
            });
            inspectorPane.Add(interpolationField);

            if (currentInterpolation != Interpolation.Bezier)
            {
                return;
            }

            float2 startHandle;
            float2 endHandle;
            GetKeyBezierHandles(address, out startHandle, out endHandle);

            BezierCurveEditorElement curveEditor = new BezierCurveEditorElement();
            curveEditor.SetHandlesWithoutNotify(startHandle, endHandle);
            curveEditor.handlesChanged += (draggedStart, draggedEnd) =>
            {
                SetKeyBezierHandles(address, draggedStart, draggedEnd);
            };
            inspectorPane.Add(curveEditor);
            inspectorPane.Add(MakeHint(
                "Drag the handles. They stay inside the unit square: outside it the curve stops "
                + "being a function of time, or overshoots further than the baked bounds allow."));
        }

        private Interpolation GetKeyInterpolation(KeyAddress address)
        {
            if (address.trackKind == TimelineTrackKind.Bone)
            {
                return selectedClip.boneTracks[address.trackIndex].keys[address.keyIndex].interpolation;
            }
            return selectedClip.transformTracks[address.trackIndex].keys[address.keyIndex].interpolation;
        }

        private void SetKeyInterpolation(KeyAddress address, Interpolation interpolation)
        {
            RecordClipEdit("Change Key Interpolation");
            if (address.trackKind == TimelineTrackKind.Bone)
            {
                BoneTrack track = selectedClip.boneTracks[address.trackIndex];
                BoneKey key = track.keys[address.keyIndex];
                key.interpolation = interpolation;
                EnsureUsableBezierHandles(ref key.bezierStartHandle, ref key.bezierEndHandle, interpolation);
                track.keys[address.keyIndex] = key;
            }
            else
            {
                TransformTrack track = selectedClip.transformTracks[address.trackIndex];
                TransformKey key = track.keys[address.keyIndex];
                key.interpolation = interpolation;
                EnsureUsableBezierHandles(ref key.bezierStartHandle, ref key.bezierEndHandle, interpolation);
                track.keys[address.keyIndex] = key;
            }
            CommitClipEdit();
        }

        /// <summary>
        /// Gives a key switched to Bézier the handles that describe a straight line.
        /// </summary>
        /// <remarks>
        /// A key that has never carried handles holds two zeros, which the sampler reads as linear.
        /// Writing the diagonal handles on the switch means the curve the editor draws and the curve
        /// the sampler evaluates agree from the first frame, rather than the widget showing a
        /// straight line because it substituted one while the asset held zeros.
        /// </remarks>
        private static void EnsureUsableBezierHandles(
            ref float2 startHandle, ref float2 endHandle, Interpolation interpolation)
        {
            if (interpolation != Interpolation.Bezier)
            {
                return;
            }
            if (math.all(startHandle == float2.zero) && math.all(endHandle == float2.zero))
            {
                startHandle = new float2(1f / 3f, 1f / 3f);
                endHandle = new float2(2f / 3f, 2f / 3f);
            }
        }

        private void GetKeyBezierHandles(
            KeyAddress address, out float2 startHandle, out float2 endHandle)
        {
            if (address.trackKind == TimelineTrackKind.Bone)
            {
                BoneKey key = selectedClip.boneTracks[address.trackIndex].keys[address.keyIndex];
                startHandle = key.bezierStartHandle;
                endHandle = key.bezierEndHandle;
                return;
            }
            TransformKey transformKey =
                selectedClip.transformTracks[address.trackIndex].keys[address.keyIndex];
            startHandle = transformKey.bezierStartHandle;
            endHandle = transformKey.bezierEndHandle;
        }

        private void SetKeyBezierHandles(KeyAddress address, float2 startHandle, float2 endHandle)
        {
            RecordClipEdit("Edit Key Tangents");
            if (address.trackKind == TimelineTrackKind.Bone)
            {
                BoneTrack track = selectedClip.boneTracks[address.trackIndex];
                BoneKey key = track.keys[address.keyIndex];
                key.bezierStartHandle = startHandle;
                key.bezierEndHandle = endHandle;
                track.keys[address.keyIndex] = key;
            }
            else
            {
                TransformTrack track = selectedClip.transformTracks[address.trackIndex];
                TransformKey key = track.keys[address.keyIndex];
                key.bezierStartHandle = startHandle;
                key.bezierEndHandle = endHandle;
                track.keys[address.keyIndex] = key;
            }
            CommitClipEdit();
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
        private void BuildBoneInspector(HierarchyItem item, bool isActive)
        {
            string boneName = item.displayName;
            inspectorPane.Add(MakeSelectionHeading(boneName, isActive));

            // The hierarchy lists every transform, not only bones, so the inspector says which kind
            // this one is. What you can usefully do with it depends on the answer: only a skinned
            // bone moves the mesh when a bone track drives it.
            if (previewController != null)
            {
                string description = previewController.DescribeHierarchyItem(item.previewIndex);
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

            int boneTrackIndex = FindBoneTrackIndex(boneName);
            if (boneTrackIndex < 0)
            {
                inspectorPane.Add(MakeHint("No track on this clip animates this bone."));
                inspectorPane.Add(new Button(() => AddBoneTrack(boneName))
                {
                    text = "Add Bone Track"
                });
                return;
            }

            AddBoneTransformFields(selectedClip.boneTracks[boneTrackIndex]);
        }

        /// <summary>
        /// The live pose of a bone at the playhead, editable in place.
        /// </summary>
        /// <remarks>
        /// This replaced a property drawer on the whole <c>BoneTrack</c>, which rendered every key
        /// as an array — the panel showing a list of keys rather than the value at the time being
        /// looked at. The keys belong on the timeline; this answers "what is this bone doing now".
        /// </remarks>
        private void AddBoneTransformFields(BoneTrack track)
        {
            LiveTransformBinding binding = new LiveTransformBinding { boneTrack = track };
            liveTransformBindings.Add(binding);

            float3 position;
            float3 rotationDegrees;
            float3 scale;
            bool hasKeys = ClipBoneEditing.TryEvaluate(
                track, playheadTime, out position, out rotationDegrees, out scale);
            bool isOnKey = ClipBoneEditing.FindKeyIndexAt(track, playheadTime) >= 0;

            inspectorPane.Add(MakeHeading("Bone Transform"));

            binding.stateChip = MakeHint(DescribeBoneState(hasKeys, isOnKey));
            inspectorPane.Add(binding.stateChip);

            VisualElement transformBlock = new VisualElement();
            transformBlock.AddToClassList(TransformBlockUssClassName);
            transformBlock.EnableInClassList(TransformOnKeyUssClassName, isOnKey);
            transformBlock.EnableInClassList(TransformInterpolatedUssClassName, hasKeys && !isOnKey);
            binding.block = transformBlock;

            Vector3Field positionField = new Vector3Field("Position");
            positionField.SetValueWithoutNotify(new Vector3(position.x, position.y, position.z));
            positionField.RegisterValueChangedCallback(changeEvent =>
            {
                ApplyBoneEdit(
                    track,
                    new float3(changeEvent.newValue.x, changeEvent.newValue.y, changeEvent.newValue.z),
                    rotationDegrees, scale);
            });
            binding.positionField = positionField;
            transformBlock.Add(positionField);

            Vector3Field rotationField = new Vector3Field("Rotation");
            rotationField.SetValueWithoutNotify(
                new Vector3(rotationDegrees.x, rotationDegrees.y, rotationDegrees.z));
            rotationField.tooltip =
                "Euler degrees. The authored key stores a quaternion; this is the readable form of "
                + "it, converted at the boundary.";
            rotationField.RegisterValueChangedCallback(changeEvent =>
            {
                ApplyBoneEdit(
                    track, position,
                    new float3(changeEvent.newValue.x, changeEvent.newValue.y, changeEvent.newValue.z),
                    scale);
            });
            binding.rotationField = rotationField;
            transformBlock.Add(rotationField);

            Vector3Field scaleField = new Vector3Field("Scale");
            scaleField.SetValueWithoutNotify(new Vector3(scale.x, scale.y, scale.z));
            scaleField.RegisterValueChangedCallback(changeEvent =>
            {
                ApplyBoneEdit(
                    track, position, rotationDegrees,
                    new float3(changeEvent.newValue.x, changeEvent.newValue.y, changeEvent.newValue.z));
            });
            binding.scaleField = scaleField;
            transformBlock.Add(scaleField);

            inspectorPane.Add(transformBlock);

            inspectorPane.Add(new Button(() =>
            {
                ApplyBoneEdit(track, position, rotationDegrees, scale);
            })
            {
                text = "Key"
            });
        }

        private static string DescribeBoneState(bool hasKeys, bool isOnKey)
        {
            if (!hasKeys)
            {
                return "No keys yet — editing creates the first one.";
            }
            return isOnKey
                ? "On a key — editing changes this key."
                : "Between keys — this value is sampled, not stored.";
        }

        /// <summary>
        /// Writes a bone pose at the playhead.
        /// </summary>
        /// <remarks>
        /// Always keys, unlike a transform edit. A bone track has no rest pose in this window to
        /// fall back to, so a held-but-unkeyed value would have nothing to be shown against — it
        /// would just be a number that vanished on the next scrub.
        /// </remarks>
        private void ApplyBoneEdit(
            BoneTrack track, float3 position, float3 rotationDegrees, float3 scale)
        {
            if (selectedClip == null || track == null)
            {
                return;
            }

            RecordClipEdit("Key Bone");
            ClipBoneEditing.SetKeyValues(track, playheadTime, position, rotationDegrees, scale);
            CommitClipEdit();

            selectedKeys.Clear();
            hasActiveKey = false;
            RebuildTimeline();
        }

        /// <summary>
        /// The inspector for a rig target: the flipbook tracks driving it, each with its base index.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A target may carry several flipbook tracks, which is how one texture array holds
        /// independent feature sets — a mouth track based at 0 and an eye track based at 32 animate
        /// the same part without either knowing the other exists. They are listed rather than
        /// merged for exactly that reason.
        /// </para>
        /// <para>
        /// The base index is editable here because it is the retargeting handle: every relative key
        /// on the track holds an offset against it, so moving this one number slides the whole track
        /// onto a different span of the array without touching a key.
        /// </para>
        /// </remarks>
        private void BuildRigTargetInspector(HierarchyItem item, bool isActive)
        {
            uint targetId = item.targetId;
            inspectorPane.Add(MakeSelectionHeading(
                ResolveTargetDisplayName(targetId), isActive));
            inspectorPane.Add(MakeHint("Rig target — animated by transform and flipbook tracks."));

            if (selectedClip == null)
            {
                inspectorPane.Add(MakeHint("Select a clip to animate this part."));
                return;
            }

            AddTransformFields(targetId);

            // Keyed off the window's active target rather than the heading's marker: the marker is
            // suppressed when there is only one block, but that block is still the one with a gizmo.
            if (targetId == selectedTargetId)
            {
                RefreshGizmo();
            }

            AddFlipbookFields(targetId);
        }

        // -------------------------------------------------------------------------------------
        // Sockets.
        // -------------------------------------------------------------------------------------

        /// <summary>
        /// The inspector for a socket: what it follows, where it sits, and what to hang off it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Sockets were previously authored only in the rig asset's own inspector, which meant
        /// tuning an offset against a character you could not see and a pose you could not scrub.
        /// The numbers are the same numbers; the difference is that here they are next to the thing
        /// they move.
        /// </para>
        /// <para>
        /// Every edit records undo on the <em>rig</em>, not the clip. A socket is rig structure —
        /// every clip in the set sees the same one — and putting it on the clip's undo stack would
        /// make an undo in one clip silently move an attachment in all the others.
        /// </para>
        /// </remarks>
        private void BuildSocketInspector(HierarchyItem item, bool isActive)
        {
            SocketDefinition socket = FindSocket(item.socketId);
            if (socket == null)
            {
                inspectorPane.Add(MakeHint("This socket is no longer on the rig."));
                return;
            }

            RigAsset rig = clipSet.rig;
            inspectorPane.Add(MakeSelectionHeading(
                string.IsNullOrEmpty(socket.displayName)
                    ? "Socket " + socket.Id.Value.ToString()
                    : socket.displayName,
                isActive));

            bool resolved = previewController != null && previewController.IsSocketResolved(socket);
            inspectorPane.Add(MakeHint(resolved
                ? "Attachment point — follows its target every frame."
                : "Follows nothing: the binding below matches no part or bone, so this socket "
                    + "will sit at the actor's origin."));

            TextField nameField = new TextField("Name");
            nameField.SetValueWithoutNotify(socket.displayName);
            nameField.RegisterValueChangedCallback(changeEvent =>
            {
                RecordSocketEdit(rig, "Rename Socket");
                socket.displayName = changeEvent.newValue;
                CommitSocketEdit(false);
            });
            inspectorPane.Add(nameField);

            EnumField modeField = new EnumField("Follows", socket.mode);
            modeField.tooltip =
                "Rig Target follows a part this rig declares, live. Bone follows a bone of the "
                + "imported skeleton, whose motion is baked into the VAT.";
            modeField.RegisterValueChangedCallback(changeEvent =>
            {
                RecordSocketEdit(rig, "Change Socket Mode");
                socket.mode = (SocketAttachMode)changeEvent.newValue;
                CommitSocketEdit(true);
                RebuildInspector();
            });
            inspectorPane.Add(modeField);

            if (socket.mode == SocketAttachMode.RigTarget)
            {
                inspectorPane.Add(BuildSocketTargetField(rig, socket));
            }
            else
            {
                inspectorPane.Add(BuildSocketBoneField(rig, socket));
                inspectorPane.Add(MakeSocketBakeHint(socket));

                IntegerField layerField = new IntegerField("Layer");
                layerField.SetValueWithoutNotify(socket.layerIndex);
                layerField.tooltip =
                    "Which playback layer drives this socket's time. Only meaningful for a bone "
                    + "socket, whose pose comes from the baked track rather than from a live part.";
                layerField.RegisterValueChangedCallback(changeEvent =>
                {
                    RecordSocketEdit(rig, "Change Socket Layer");
                    socket.layerIndex = Mathf.Max(0, changeEvent.newValue);
                    CommitSocketEdit(true);
                });
                inspectorPane.Add(layerField);
            }

            inspectorPane.Add(MakeHeading("Offset"));

            Vector3Field offsetPositionField = new Vector3Field("Position");
            offsetPositionField.SetValueWithoutNotify(socket.localPosition);
            offsetPositionField.tooltip =
                "In the followed part or bone's local space, so it stays put as the rig moves.";
            offsetPositionField.RegisterValueChangedCallback(changeEvent =>
            {
                RecordSocketEdit(rig, "Move Socket");
                socket.localPosition = changeEvent.newValue;
                CommitSocketEdit(true);
            });
            inspectorPane.Add(offsetPositionField);

            Vector3Field offsetRotationField = new Vector3Field("Rotation");
            offsetRotationField.SetValueWithoutNotify(socket.localEulerAngles);
            offsetRotationField.RegisterValueChangedCallback(changeEvent =>
            {
                RecordSocketEdit(rig, "Rotate Socket");
                socket.localEulerAngles = changeEvent.newValue;
                CommitSocketEdit(true);
            });
            inspectorPane.Add(offsetRotationField);

            inspectorPane.Add(MakeHeading("Preview Attachment"));
            inspectorPane.Add(MakeHint(
                "Editor only. Hangs a prefab off this socket so the placement can be judged "
                + "against the animation; nothing reads it at run time or ships in a build."));

            ObjectField attachmentField = new ObjectField("Prefab");
            attachmentField.objectType = typeof(GameObject);
            attachmentField.allowSceneObjects = false;
            attachmentField.SetValueWithoutNotify(socket.previewAttachment);
            attachmentField.RegisterValueChangedCallback(changeEvent =>
            {
                RecordSocketEdit(rig, "Set Socket Preview Attachment");
                socket.previewAttachment = changeEvent.newValue as GameObject;
                CommitSocketEdit(false);

                if (previewController != null)
                {
                    previewController.RefreshSocketAttachments();
                }
                MarkPreviewDirty();
            });
            inspectorPane.Add(attachmentField);

            inspectorPane.Add(new Button(() => ConfirmDeleteSocket(socket))
            {
                text = "Delete Socket"
            });
        }

        /// <summary>
        /// Says whether a bone socket has baked motion yet, and for how many clips.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>Only a bone socket needs baking, and the asymmetry is the thing worth stating.
        /// </strong> A rig-target socket's motion <em>is</em> its part's transform, resolved live
        /// every frame, so there is nothing to capture. A bone socket follows a bone that exists at
        /// run time only as texels in a VAT texture, so its motion has to be sampled at bake time
        /// and stored — and until that has happened it resolves to the actor's origin.
        /// </para>
        /// <para>
        /// Reported here because "attachment sits at the actor's feet" is otherwise a play-mode
        /// discovery with no obvious cause. The preview marker itself is honest either way: it
        /// follows the posed skeleton, which is where the socket <em>will</em> be once baked.
        /// </para>
        /// </remarks>
        private Label MakeSocketBakeHint(SocketDefinition socket)
        {
            VatTextureSetAsset textures = clipSet != null ? clipSet.vatTextures : null;
            if (textures == null)
            {
                return MakeHint(
                    "Not baked: this clip set has no VAT texture set. A bone socket's motion is "
                    + "captured by the VAT bake — until then it resolves to the actor's origin at "
                    + "run time. Window ▸ DOTS Animation Toolkit ▸ VAT Bake.");
            }

            int bakedClipCount = 0;
            for (int trackIndex = 0;
                textures.socketTracks != null && trackIndex < textures.socketTracks.Count;
                trackIndex++)
            {
                VatSocketTrack track = textures.socketTracks[trackIndex];
                if (track != null && track.socketId == socket.Id.Value)
                {
                    bakedClipCount++;
                }
            }

            if (bakedClipCount == 0)
            {
                return MakeHint(
                    "Not baked: no captured motion for this socket. Re-run the VAT bake, and check "
                    + "the Console for unresolved bone names while you are there.");
            }
            return MakeHint("Baked across " + bakedClipCount.ToString() + " clip(s).");
        }

        /// <summary>A dropdown of the rig's parts, so a target binding cannot be mistyped.</summary>
        private VisualElement BuildSocketTargetField(RigAsset rig, SocketDefinition socket)
        {
            List<string> targetNames = new List<string>();
            List<uint> targetIds = new List<uint>();
            for (int targetIndex = 0; rig.targets != null && targetIndex < rig.targets.Count; targetIndex++)
            {
                RigTargetDefinition target = rig.targets[targetIndex];
                if (target == null)
                {
                    continue;
                }
                targetNames.Add(string.IsNullOrEmpty(target.displayName)
                    ? "Target " + target.Id.Value.ToString()
                    : target.displayName);
                targetIds.Add(target.Id.Value);
            }

            if (targetNames.Count == 0)
            {
                return MakeHint("The rig declares no parts for a socket to follow.");
            }

            int currentIndex = Mathf.Max(0, targetIds.IndexOf(socket.targetId));
            PopupField<string> targetField =
                new PopupField<string>("Target", targetNames, currentIndex);
            targetField.RegisterValueChangedCallback(changeEvent =>
            {
                int chosen = targetNames.IndexOf(changeEvent.newValue);
                if (chosen < 0)
                {
                    return;
                }
                RecordSocketEdit(rig, "Rebind Socket");
                socket.targetId = targetIds[chosen];
                CommitSocketEdit(true);
            });
            return targetField;
        }

        /// <summary>
        /// A dropdown of the loaded prefab's transform names, falling back to typing.
        /// </summary>
        /// <remarks>
        /// The dropdown is the point — a bone binding that resolves to nothing bakes an attachment
        /// at the origin, and typing is how you get one. The text field remains for a set with no
        /// prefab loaded, which is the only case where there are no names to offer.
        /// </remarks>
        private VisualElement BuildSocketBoneField(RigAsset rig, SocketDefinition socket)
        {
            previewController.CollectHierarchyNames(hierarchyNameCache);
            if (hierarchyNameCache.Count == 0)
            {
                TextField boneField = new TextField("Bone");
                boneField.SetValueWithoutNotify(socket.boneName);
                boneField.tooltip =
                    "Assign a prefab in the toolbar's rig field to pick from its bones instead.";
                boneField.RegisterValueChangedCallback(changeEvent =>
                {
                    RecordSocketEdit(rig, "Rebind Socket");
                    socket.boneName = changeEvent.newValue;
                    CommitSocketEdit(true);
                });
                return boneField;
            }

            List<string> boneNames = new List<string>(hierarchyNameCache);
            boneNames.Sort();
            int currentIndex = Mathf.Max(0, boneNames.IndexOf(socket.boneName));

            PopupField<string> bonePopup = new PopupField<string>("Bone", boneNames, currentIndex);
            bonePopup.RegisterValueChangedCallback(changeEvent =>
            {
                RecordSocketEdit(rig, "Rebind Socket");
                socket.boneName = changeEvent.newValue;
                CommitSocketEdit(true);
            });
            return bonePopup;
        }

        private void RecordSocketEdit(RigAsset rig, string undoLabel)
        {
            Undo.RecordObject(rig, undoLabel);
        }

        /// <summary>
        /// Persists a socket edit and refreshes whatever it invalidated.
        /// </summary>
        /// <param name="rebuildMarkers">
        /// Whether the change moves or rebinds a marker, as opposed to only relabelling it.
        /// </param>
        private void CommitSocketEdit(bool rebuildMarkers)
        {
            RigAsset rig = clipSet != null ? clipSet.rig : null;
            if (rig == null)
            {
                return;
            }
            EditorUtility.SetDirty(rig);

            if (rebuildMarkers && previewController != null)
            {
                previewController.RebuildSockets();
            }

            // The row label carries the binding and the unresolved mark, so it is stale the moment
            // either changes.
            RebuildHierarchy();
            MarkPreviewDirty();
        }

        /// <summary>Adds a socket bound to whatever is selected, or to the first part.</summary>
        /// <remarks>
        /// Pre-bound rather than left blank: a socket that follows nothing is the state this whole
        /// feature exists to make visible, and creating one in that state as a matter of course
        /// would train the author to ignore the warning.
        /// </remarks>
        private void AddSocket()
        {
            RigAsset rig = clipSet != null ? clipSet.rig : null;
            if (rig == null)
            {
                ShowNotification(new GUIContent("Assign a clip set with a rig first."));
                return;
            }
            if (rig.sockets == null)
            {
                rig.sockets = new List<SocketDefinition>();
            }

            SocketDefinition socket = new SocketDefinition();
            HierarchyItem activeItem = ActiveHierarchyItem;
            if (activeItem != null && activeItem.kind == HierarchyItemKind.RigTarget)
            {
                socket.mode = SocketAttachMode.RigTarget;
                socket.targetId = activeItem.targetId;
                socket.displayName = activeItem.displayName + " Socket";
            }
            else if (activeItem != null && activeItem.kind == HierarchyItemKind.PrefabTransform)
            {
                socket.mode = SocketAttachMode.Bone;
                socket.boneName = activeItem.displayName;
                socket.displayName = activeItem.displayName + " Socket";
            }
            else if (rig.targets != null && rig.targets.Count > 0 && rig.targets[0] != null)
            {
                socket.mode = SocketAttachMode.RigTarget;
                socket.targetId = rig.targets[0].Id.Value;
                socket.displayName = "New Socket";
            }
            else
            {
                socket.displayName = "New Socket";
            }

            Undo.RecordObject(rig, "Add Socket");
            rig.sockets.Add(socket);

            // Minted here rather than left to OnValidate, which does not run in time. A socket whose
            // id is still 0 is not merely unidentified: 0 is the sentinel for "no socket selected",
            // so the new socket would be unselectable and its marker unfindable.
            rig.EnsureStableIds();

            EditorUtility.SetDirty(rig);
            AssetDatabase.SaveAssetIfDirty(rig);

            if (previewController != null)
            {
                previewController.RebuildSockets();
            }
            RebuildHierarchy();
            MarkPreviewDirty();
        }

        private void ConfirmDeleteSocket(SocketDefinition socket)
        {
            RigAsset rig = clipSet != null ? clipSet.rig : null;
            int socketIndex = FindSocketIndex(socket.Id.Value);
            if (rig == null || socketIndex < 0)
            {
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "Delete Socket",
                    "Delete \"" + socket.displayName + "\"?\n\n"
                        + "Anything attached to it at run time will have nothing to follow.",
                    "Delete",
                    "Cancel"))
            {
                return;
            }

            Undo.RecordObject(rig, "Delete Socket");
            rig.sockets.RemoveAt(socketIndex);
            EditorUtility.SetDirty(rig);
            AssetDatabase.SaveAssetIfDirty(rig);

            ClearHierarchySelection();
            if (previewController != null)
            {
                previewController.RebuildSockets();
            }
            RebuildHierarchy();
            MarkPreviewDirty();
        }

        /// <summary>
        /// A block's name, marked when it is the active row.
        /// </summary>
        /// <remarks>
        /// With several objects selected the panel is a stack of near-identical blocks, so each one
        /// has to say whose numbers it holds. The active marker explains why only one of them has a
        /// gizmo in the viewport.
        /// </remarks>
        private static Label MakeSelectionHeading(string name, bool isActive)
        {
            Label heading = MakeHeading(isActive ? name + "   (active)" : name);
            heading.AddToClassList(SelectionHeadingUssClassName);
            heading.EnableInClassList(SelectionHeadingActiveUssClassName, isActive);
            return heading;
        }

        /// <summary>
        /// The live flipbook index for the selected part: one block per track driving it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>The value at the playhead, not a list of keys.</strong> This panel answers "what
        /// is this part showing right now", the same question the transform block above it answers.
        /// The keys themselves live on the timeline, which is where they can be moved and selected;
        /// repeating them here made a long track unreadable and put the same data in two places that
        /// could disagree.
        /// </para>
        /// <para>
        /// Several blocks are expected. A target can carry several flipbook tracks, each with its
        /// own base — that is how one texture array holds independent feature sets, a mouth based
        /// at 0 and eyes based at 32 driving the same part.
        /// </para>
        /// </remarks>
        private void AddFlipbookFields(uint targetId)
        {
            ClipSpriteEditing.CollectTracksForTarget(
                selectedClip, targetId, flipbookTracks, flipbookTrackIndices);

            inspectorPane.Add(MakeHeading("Flipbook Index"));

            if (flipbookTracks.Count == 0)
            {
                inspectorPane.Add(MakeHint("No flipbook track on this clip drives this part."));
            }

            for (int listIndex = 0; listIndex < flipbookTracks.Count; listIndex++)
            {
                inspectorPane.Add(BuildFlipbookTrackBlock(
                    flipbookTracks[listIndex], flipbookTrackIndices[listIndex]));
            }

            inspectorPane.Add(new Button(() => AddFlipbookTrack(targetId))
            {
                text = "Add Flipbook Track"
            });
        }

        /// <summary>
        /// One track's settings plus the value it is showing at the playhead, editable in place.
        /// </summary>
        private VisualElement BuildFlipbookTrackBlock(SpriteTrack track, int trackIndex)
        {
            VisualElement trackBlock = new VisualElement();
            trackBlock.AddToClassList(FlipbookTrackUssClassName);

            int keyCount = track.keys != null ? track.keys.Count : 0;
            int effectiveKeyIndex = ClipSpriteEditing.FindEffectiveKeyIndex(track, playheadTime);
            bool isOnKey = ClipSpriteEditing.FindKeyIndexAt(track, playheadTime) >= 0;

            trackBlock.Add(MakeHeading("Track " + trackIndex + "  ·  " + keyCount + " key(s)"));

            Label stateHint = MakeHint(keyCount == 0
                ? "Empty — editing the index below creates the first key."
                : (isOnKey
                    ? "On a key — editing changes this key."
                    : "Held from an earlier key — editing keys the value here."));
            trackBlock.Add(stateHint);

            LiveFlipbookBinding binding = new LiveFlipbookBinding
            {
                track = track,
                stateHint = stateHint
            };
            liveFlipbookBindings.Add(binding);

            if (keyCount > 0 && effectiveKeyIndex >= 0)
            {
                SpriteKey currentKey = track.keys[effectiveKeyIndex];

                IntegerField valueField = new IntegerField("Index");
                valueField.SetValueWithoutNotify(currentKey.sliceIndex);
                valueField.tooltip =
                    "The number this key stores: an array index in Absolute mode, or an offset from "
                    + "the base index in RelativeToBase.";
                valueField.RegisterValueChangedCallback(changeEvent =>
                {
                    ApplyFlipbookEdit(track, changeEvent.newValue, currentKey.indexMode);
                });
                binding.valueField = valueField;
                trackBlock.Add(valueField);

                EnumField indexModeField = new EnumField("Index Mode", currentKey.indexMode);
                indexModeField.tooltip =
                    "Absolute names a frame outright. RelativeToBase holds an offset from the "
                    + "track's base index. Switching keeps the frame the key shows.";
                indexModeField.RegisterValueChangedCallback(changeEvent =>
                {
                    ToggleFlipbookKeyMode(
                        track, effectiveKeyIndex, (SpriteIndexMode)changeEvent.newValue);
                });
                binding.indexModeField = indexModeField;
                trackBlock.Add(indexModeField);

                Label resolvedLabel = MakeFlipbookResolvedLabel(currentKey, track.baseIndex);
                binding.resolvedLabel = resolvedLabel;
                trackBlock.Add(resolvedLabel);
            }
            else
            {
                IntegerField emptyValueField = new IntegerField("Index");
                emptyValueField.SetValueWithoutNotify(0);
                emptyValueField.RegisterValueChangedCallback(changeEvent =>
                {
                    ApplyFlipbookEdit(track, changeEvent.newValue, SpriteIndexMode.Absolute);
                });
                trackBlock.Add(emptyValueField);
            }

            IntegerField baseIndexField = new IntegerField("Base Index");
            baseIndexField.SetValueWithoutNotify(track.baseIndex);
            baseIndexField.tooltip =
                "Every RelativeToBase key on this track offsets from here. Changing it retargets "
                + "the whole track onto a different span of the texture array; the keys keep their "
                + "offsets untouched.";
            baseIndexField.RegisterValueChangedCallback(changeEvent =>
            {
                RecordClipEdit("Change Flipbook Base Index");
                track.baseIndex = changeEvent.newValue;
                CommitClipEdit();

                // Rebuilt because every relative key's resolved index just moved, and the resolved
                // index is what the line above shows.
                RebuildInspector();
            });
            trackBlock.Add(baseIndexField);

            EnumField frameModeField = new EnumField("Frame Mode", track.mode);
            frameModeField.RegisterValueChangedCallback(changeEvent =>
            {
                RecordClipEdit("Change Flipbook Frame Mode");
                track.mode = (SpriteFrameMode)changeEvent.newValue;
                CommitClipEdit();
            });
            trackBlock.Add(frameModeField);

            EnumField sliceSpaceField = new EnumField("Slice Space", track.sliceSpace);
            sliceSpaceField.tooltip =
                "Whether this track's resolved value replaces the part's frame outright, or is "
                + "added to the rest slice the character's variant chose.";
            sliceSpaceField.RegisterValueChangedCallback(changeEvent =>
            {
                RecordClipEdit("Change Flipbook Slice Space");
                track.sliceSpace = (SpriteSliceSpace)changeEvent.newValue;
                CommitClipEdit();
            });
            trackBlock.Add(sliceSpaceField);

            trackBlock.Add(new Button(() => RemoveFlipbookTrack(trackIndex))
            {
                text = "Remove Track"
            });
            return trackBlock;
        }

        /// <summary>
        /// Writes a flipbook index at the playhead, creating a key there when there is none.
        /// </summary>
        /// <remarks>
        /// Unlike a transform edit this always keys, regardless of the auto-key toggle. A flipbook
        /// value is a discrete frame with no in-between to hold: there is no equivalent of "showing
        /// a modified pose without committing it", so a held edit would only be a value that
        /// silently disappeared.
        /// </remarks>
        private void ApplyFlipbookEdit(SpriteTrack track, int storedValue, SpriteIndexMode indexMode)
        {
            if (selectedClip == null || track == null)
            {
                return;
            }

            RecordClipEdit("Edit Flipbook Index");
            ClipSpriteEditing.SetKeyValue(track, playheadTime, storedValue, indexMode);
            CommitClipEdit();

            selectedKeys.Clear();
            hasActiveKey = false;
            RebuildTimeline();
        }

        /// <summary>Shows what a key resolves to, in the "+5 → 12" form.</summary>
        private static Label MakeFlipbookResolvedLabel(SpriteKey key, int baseIndex)
        {
            Label resolvedLabel = new Label();
            resolvedLabel.AddToClassList(FlipbookResolvedUssClassName);
            ApplyFlipbookResolvedLabel(resolvedLabel, key, baseIndex);
            return resolvedLabel;
        }

        /// <summary>Writes the "+5 → 12" reading onto an existing label.</summary>
        private static void ApplyFlipbookResolvedLabel(Label resolvedLabel, SpriteKey key, int baseIndex)
        {
            int resolvedIndex = SpriteIndexResolver.Resolve(key.sliceIndex, key.indexMode, baseIndex);

            string resolvedText;
            if (key.indexMode == SpriteIndexMode.RelativeToBase)
            {
                string offsetText = key.sliceIndex >= 0
                    ? "+" + key.sliceIndex.ToString()
                    : key.sliceIndex.ToString();
                resolvedText = offsetText + " → " + resolvedIndex.ToString();
            }
            else if (key.sliceIndex == SpriteIndexResolver.NoChangeSentinel)
            {
                resolvedText = "no change";
            }
            else
            {
                resolvedText = "→ " + resolvedIndex.ToString();
            }

            resolvedLabel.text = resolvedText;
            resolvedLabel.EnableInClassList(
                FlipbookInvalidUssClassName,
                key.indexMode == SpriteIndexMode.RelativeToBase && resolvedIndex < 0);
        }

        /// <summary>
        /// Switches a key between absolute and relative without moving the frame it shows.
        /// </summary>
        /// <remarks>
        /// Absolute→Relative subtracts the base to recover the offset; Relative→Absolute writes the
        /// resolved value out. Both go through <c>SpriteIndexResolver</c>, so this conversion cannot
        /// drift from the resolution the sampler performs.
        /// </remarks>
        private void ToggleFlipbookKeyMode(SpriteTrack track, int keyIndex, SpriteIndexMode newMode)
        {
            SpriteKey key = track.keys[keyIndex];
            if (key.indexMode == newMode)
            {
                return;
            }

            int resolvedIndex = SpriteIndexResolver.Resolve(
                key.sliceIndex, key.indexMode, track.baseIndex);

            RecordClipEdit("Change Flipbook Key Mode");
            key.indexMode = newMode;
            key.sliceIndex = SpriteIndexResolver.StoredValueFor(
                resolvedIndex, newMode, track.baseIndex);
            track.keys[keyIndex] = key;
            CommitClipEdit();

            RebuildInspector();
        }

        private string ResolveTargetDisplayName(uint targetId)
        {
            if (clipSet == null || clipSet.rig == null || clipSet.rig.targets == null)
            {
                return "Target " + targetId.ToString();
            }
            for (int targetIndex = 0; targetIndex < clipSet.rig.targets.Count; targetIndex++)
            {
                RigTargetDefinition target = clipSet.rig.targets[targetIndex];
                if (target != null && target.Id.Value == targetId
                    && !string.IsNullOrEmpty(target.displayName))
                {
                    return target.displayName;
                }
            }
            return "Target " + targetId.ToString();
        }

        private void AddFlipbookTrack(uint targetId)
        {
            if (selectedClip == null || targetId == 0u)
            {
                return;
            }

            if (selectedClip.spriteTracks == null)
            {
                selectedClip.spriteTracks = new List<SpriteTrack>();
            }

            RecordClipEdit("Add Flipbook Track");
            selectedClip.spriteTracks.Add(new SpriteTrack
            {
                targetId = targetId,
                mode = SpriteFrameMode.Slice,
                keys = new List<SpriteKey>()
            });
            CommitClipEdit();

            RebuildTimeline();
        }

        private void RemoveFlipbookTrack(int trackIndex)
        {
            if (selectedClip == null || selectedClip.spriteTracks == null
                || trackIndex < 0 || trackIndex >= selectedClip.spriteTracks.Count)
            {
                return;
            }

            RecordClipEdit("Remove Flipbook Track");
            selectedClip.spriteTracks.RemoveAt(trackIndex);
            CommitClipEdit();

            selectedKeys.Clear();
            hasActiveKey = false;
            RebuildTimeline();
        }

        /// <summary>
        /// Opens one undo step for a direct edit to the clip's own objects.
        /// </summary>
        /// <remarks>
        /// The flipbook rows edit <c>SpriteTrack</c> instances directly rather than through
        /// <c>SerializedProperty</c>, because a track's meaning spans two fields the property
        /// drawers cannot relate — a key's stored number is only interpretable beside its mode and
        /// its track's base. Direct edits get none of the binding machinery's undo for free, so the
        /// gesture is recorded explicitly, exactly as the timeline's own gestures are.
        /// </remarks>
        private void RecordClipEdit(string actionName)
        {
            Undo.IncrementCurrentGroup();
            gestureUndoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(actionName);
            Undo.RecordObject(selectedClip, actionName);
        }

        private void CommitClipEdit()
        {
            Undo.CollapseUndoOperations(gestureUndoGroup);
            EditorUtility.SetDirty(selectedClip);
            RefreshSerializedClip();
            MarkPreviewDirty();
        }

        /// <summary>Whether edits are written straight into a key at the playhead.</summary>
        private bool IsAutoKeyEnabled
        {
            get { return !IsRigEditMode && autoKeyToggle != null && autoKeyToggle.value; }
        }

        /// <summary>
        /// The transform the selected part shows right now: the held edit if there is one, the
        /// sampled track value otherwise.
        /// </summary>
        private TransformValueState ResolveDisplayedTransform(
            uint targetId, out float3 position, out float3 rotationDegrees, out float3 scale)
        {
            TransformTrack track = ClipTransformEditing.FindTransformTrack(selectedClip, targetId);
            bool hasSample = ClipTransformEditing.TryEvaluate(
                track, playheadTime, out position, out rotationDegrees, out scale);

            // The held edit belongs to one part. With several selected, the other blocks must keep
            // showing their own sampled values rather than borrowing this one's uncommitted pose.
            if (hasPendingTransformEdit && pendingTransformTargetId == targetId)
            {
                position = pendingPosition;
                rotationDegrees = pendingRotationDegrees;
                scale = pendingScale;
                return TransformValueState.Modified;
            }

            if (!hasSample)
            {
                return TransformValueState.Unkeyed;
            }
            return ClipTransformEditing.FindKeyIndexAt(track, playheadTime) >= 0
                ? TransformValueState.OnKey
                : TransformValueState.Interpolated;
        }

        /// <summary>
        /// <strong>The single path a transform value is written through.</strong>
        /// </summary>
        /// <remarks>
        /// The numeric fields and the viewport gizmos both call this. With auto-key on it writes a
        /// key at the playhead; with it off the value is held and drawn as modified, which is what
        /// makes "change it and look at it" possible without littering the clip with keys. A gizmo
        /// drag passes <paramref name="forceKey"/> on release so a completed drag is kept even
        /// though the frames during it were not.
        /// </remarks>
        private void ApplyTransformEdit(
            uint targetId, float3 position, float3 rotationDegrees, float3 scale, bool forceKey)
        {
            if (selectedClip == null || targetId == 0u)
            {
                return;
            }

            // Only one part can hold an uncommitted edit at a time. Starting one on a second part
            // keys the first rather than dropping it, because a value you typed and watched should
            // not disappear because you looked at the block below it.
            if (hasPendingTransformEdit && pendingTransformTargetId != targetId)
            {
                CommitPendingTransformEdit();
            }

            pendingTransformTargetId = targetId;
            pendingPosition = position;
            pendingRotationDegrees = rotationDegrees;
            pendingScale = scale;
            hasPendingTransformEdit = true;

            if (IsAutoKeyEnabled || forceKey)
            {
                CommitPendingTransformEdit();
            }

            MarkPreviewDirty();
        }

        /// <summary>Writes the held edit into a key at the playhead, creating the track if needed.</summary>
        private void CommitPendingTransformEdit()
        {
            // Refused rather than merely unreachable. Every route into keying passes through here,
            // so one guard covers the numeric fields, the Key button and the gizmo alike -- and a
            // mode that only *looked* like it could not key would be the ambiguity back again.
            if (IsRigEditMode)
            {
                return;
            }
            if (!hasPendingTransformEdit || selectedClip == null || pendingTransformTargetId == 0u)
            {
                return;
            }

            RecordClipEdit("Key Transform");

            TransformTrack track =
                ClipTransformEditing.FindTransformTrack(selectedClip, pendingTransformTargetId);
            if (track == null)
            {
                // Keying a part with no track yet creates one. Requiring the user to add a track
                // first would be a step that only exists because of how the data is shaped.
                if (selectedClip.transformTracks == null)
                {
                    selectedClip.transformTracks = new List<TransformTrack>();
                }
                track = new TransformTrack
                {
                    targetId = pendingTransformTargetId,
                    keys = new List<TransformKey>()
                };
                selectedClip.transformTracks.Add(track);
            }

            ClipTransformEditing.SetKeyValues(
                track, playheadTime, pendingPosition, pendingRotationDegrees, pendingScale);

            CommitClipEdit();
            hasPendingTransformEdit = false;

            selectedKeys.Clear();
            hasActiveKey = false;
            RebuildTimeline();
        }

        /// <summary>Drops a held edit — used when the playhead or the selection moves off it.</summary>
        private void DiscardPendingTransformEdit()
        {
            hasPendingTransformEdit = false;
        }

        /// <summary>
        /// The always-visible transform block for the selected part.
        /// </summary>
        /// <remarks>
        /// Shown whether or not a key exists at the playhead, because "what is this part doing right
        /// now" is a question with an answer at every time, and an inspector that goes blank between
        /// keys makes scrubbing useless for judging a pose. The state chip says which kind of value
        /// is on screen so a sampled number is never mistaken for a stored one.
        /// </remarks>
        private void AddTransformFields(uint targetId)
        {
            LiveTransformBinding binding = new LiveTransformBinding { targetId = targetId };
            liveTransformBindings.Add(binding);

            float3 position;
            float3 rotationDegrees;
            float3 scale;
            TransformValueState valueState =
                ResolveDisplayedTransform(targetId, out position, out rotationDegrees, out scale);

            inspectorPane.Add(MakeHeading("Transform"));
            binding.stateChip = MakeTransformStateChip(valueState);
            inspectorPane.Add(binding.stateChip);

            VisualElement transformBlock = new VisualElement();
            binding.block = transformBlock;
            transformBlock.AddToClassList(TransformBlockUssClassName);
            transformBlock.EnableInClassList(
                TransformOnKeyUssClassName, valueState == TransformValueState.OnKey);
            transformBlock.EnableInClassList(
                TransformInterpolatedUssClassName, valueState == TransformValueState.Interpolated);
            transformBlock.EnableInClassList(
                TransformModifiedUssClassName, valueState == TransformValueState.Modified);

            Vector3Field positionField = new Vector3Field("Position");
            positionField.SetValueWithoutNotify(new Vector3(position.x, position.y, position.z));
            positionField.RegisterValueChangedCallback(changeEvent =>
            {
                float3 edited = new float3(
                    changeEvent.newValue.x, changeEvent.newValue.y, changeEvent.newValue.z);
                ApplyTransformEdit(targetId, edited, rotationDegrees, scale, false);
                RebuildInspector();
            });
            binding.positionField = positionField;
            transformBlock.Add(positionField);

            Vector3Field rotationField = new Vector3Field("Rotation");
            rotationField.SetValueWithoutNotify(
                new Vector3(rotationDegrees.x, rotationDegrees.y, rotationDegrees.z));
            rotationField.tooltip =
                "Euler degrees in Unity's ZXY order. The bake converts to radians once (section 4.5). "
                + "A flat rig leaves x and y at zero.";
            rotationField.RegisterValueChangedCallback(changeEvent =>
            {
                float3 edited = new float3(
                    changeEvent.newValue.x, changeEvent.newValue.y, changeEvent.newValue.z);
                ApplyTransformEdit(targetId, position, edited, scale, false);
                RebuildInspector();
            });
            binding.rotationField = rotationField;
            transformBlock.Add(rotationField);

            Vector3Field scaleField = new Vector3Field("Scale");
            scaleField.SetValueWithoutNotify(new Vector3(scale.x, scale.y, scale.z));
            scaleField.RegisterValueChangedCallback(changeEvent =>
            {
                float3 edited = new float3(
                    changeEvent.newValue.x, changeEvent.newValue.y, changeEvent.newValue.z);
                ApplyTransformEdit(targetId, position, rotationDegrees, edited, false);
                RebuildInspector();
            });
            binding.scaleField = scaleField;
            transformBlock.Add(scaleField);

            inspectorPane.Add(transformBlock);

            VisualElement keyRow = new VisualElement();
            keyRow.AddToClassList(FlipbookKeyUssClassName);
            keyRow.Add(new Button(() =>
            {
                if (!hasPendingTransformEdit || pendingTransformTargetId != targetId)
                {
                    // Nothing held: key the value currently on screen, which is how a pose reached
                    // by scrubbing gets pinned down.
                    pendingTransformTargetId = targetId;
                    pendingPosition = position;
                    pendingRotationDegrees = rotationDegrees;
                    pendingScale = scale;
                    hasPendingTransformEdit = true;
                }
                CommitPendingTransformEdit();
                RebuildInspector();
            })
            {
                text = "Key"
            });
            if (hasPendingTransformEdit && pendingTransformTargetId == targetId)
            {
                keyRow.Add(new Button(() =>
                {
                    DiscardPendingTransformEdit();
                    RebuildInspector();
                })
                {
                    text = "Revert"
                });
            }
            inspectorPane.Add(keyRow);
        }

        private static string DescribeTransformState(TransformValueState valueState)
        {
            switch (valueState)
            {
                case TransformValueState.OnKey:
                    return "On a key — editing changes this key.";
                case TransformValueState.Interpolated:
                    return "Between keys — this value is sampled, not stored.";
                case TransformValueState.Modified:
                    return "Modified, not keyed — press Key to keep it.";
                default:
                    return "No transform track yet — editing creates one.";
            }
        }

        private static Label MakeTransformStateChip(TransformValueState valueState)
        {
            Label chip = new Label(DescribeTransformState(valueState));
            chip.AddToClassList(HintUssClassName);
            chip.AddToClassList(TransformStateChipUssClassName);
            chip.EnableInClassList(
                TransformModifiedUssClassName, valueState == TransformValueState.Modified);
            return chip;
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
                if (!ClipAssetUtility.RenameClip(selectedClip, changeEvent.newValue))
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
