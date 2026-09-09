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
    /// The clip timeline editor: clip selector, transport, timeline, viewport, hierarchy and
    /// inspector docked around ClipEditorWindow.uxml's layout. Undo is per gesture, not per
    /// mutation — a key drag collapses into one Ctrl+Z — and the viewport renders independently of
    /// selection, from the moment the window opens.
    /// </summary>
    public sealed partial class ClipEditorWindow : EditorWindow, ITransportTarget
    {
        private const float PlaybackHertz = 30f;

        /// <summary>How long the preview waits for a gesture to go quiet before rebuilding.</summary>
        private const double PreviewSettleSeconds = 0.25;

        // The longest the preview may go without a rebuild while it is dirty. PreviewSettleSeconds
        // alone is a trailing edge that never fires during a continuous drag; this bound is what
        // makes the drag live.
        private const double PreviewMaxWaitSeconds = 0.06;

        private const string LayoutAssetPath =
            "Packages/com.dotsanimationtoolkit/Editor/ClipEditor/ClipEditorWindow.uxml";

        /// <summary>Shared stylesheet path other package windows load to get the `--toolkit-color-*` tokens and shared chrome classes.</summary>
        public const string StyleSheetPath =
            "Packages/com.dotsanimationtoolkit/Editor/ClipEditor/ClipEditorWindow.uss";

        /// <summary>
        /// Prefix for the persisted split positions. Keyed by window rather than by project on
        /// purpose: a dock layout is a habit of the person, and following them between projects is
        /// the behaviour every other editor window has.
        /// </summary>
        private const string SplitPrefsPrefix = "DotsAnimationToolkit.ClipEditor.Split.";

        /// <summary>Below this a pane is a sliver with nothing readable in it, so it is never stored.</summary>
        private const float MinimumSplitDimension = 60f;

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

        // How far the pointer may travel between press and release and still count as a click.
        // Without this, every viewport orbit (which begins with the same press as a selection)
        // would also change the selection.
        private const float ClickMovementToleranceSquared = 9f;

        private const string HiddenUssClassName = "clip-editor--hidden";
        private const string TabActiveUssClassName = "clip-editor__tab--active";
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
        private const string SelectionHeadingRowUssClassName = "clip-editor__selection-heading-row";
        private const string SelectionHeadingUssClassName = "clip-editor__selection-heading";
        private const string SelectionHeadingActiveUssClassName =
            "clip-editor__selection-heading--active";
        private const string SelectionHeadingTagButtonUssClassName =
            "clip-editor__selection-heading-tag-button";

        private ObjectField clipSetField;
        private ListView clipListView;
        private Button newClipButton;
        private Button deleteClipButton;
        private TreeView hierarchyTreeView;
        private Label hierarchyEmptyLabel;
        private ToolbarToggle snapToggle;
        private ToolbarToggle autoKeyToggle;

        // Lit on a bar action while it is armed to write keys.
        private const string RecordingBarActionUssClassName = "toolkit-bar-action--recording";

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

        // Where the held, unkeyed transform value lives — in an object, so Ctrl+Z can reach it (see
        // HeldTransformEdit). The properties below let every reader in this file keep reading the
        // value the way it always did.
        private HeldTransformEdit heldTransformEdit;

        private bool hasPendingTransformEdit
        {
            get { return heldTransformEdit != null && heldTransformEdit.hasValue; }
            set { EnsureHeldTransformEdit().hasValue = value; }
        }

        private uint pendingTransformTargetId
        {
            get { return heldTransformEdit != null ? heldTransformEdit.targetId : 0u; }
            set { EnsureHeldTransformEdit().targetId = value; }
        }

        private float3 pendingPosition
        {
            get { return ToFloat3(heldTransformEdit != null ? heldTransformEdit.position : Vector3.zero); }
            set { EnsureHeldTransformEdit().position = ToVector3(value); }
        }

        private float3 pendingRotationDegrees
        {
            get
            {
                return ToFloat3(
                    heldTransformEdit != null ? heldTransformEdit.rotationDegrees : Vector3.zero);
            }
            set { EnsureHeldTransformEdit().rotationDegrees = ToVector3(value); }
        }

        private float3 pendingScale
        {
            get { return ToFloat3(heldTransformEdit != null ? heldTransformEdit.scale : Vector3.one); }
            set { EnsureHeldTransformEdit().scale = ToVector3(value); }
        }

        private HeldTransformEdit EnsureHeldTransformEdit()
        {
            if (heldTransformEdit == null)
            {
                heldTransformEdit = ScriptableObject.CreateInstance<HeldTransformEdit>();
                heldTransformEdit.hideFlags = HideFlags.HideAndDontSave;
            }
            return heldTransformEdit;
        }

        // Opens one undo step for the value a part is about to hold without keying. With Auto Key
        // off this is the only record of the move; recorded before the write, so the step holds the
        // value the part is moving away from.
        private void RecordHeldTransformEdit(string actionName)
        {
            Undo.RecordObject(EnsureHeldTransformEdit(), actionName);
        }

        /// <summary>
        /// A Rig Edit gizmo drag's held value, entirely separate from
        /// <see cref="hasPendingTransformEdit"/>. Rig Edit addresses whatever hierarchy node is
        /// selected — a rig target, a bare grouping transform, or a skinned bone — none of which is
        /// guaranteed to carry a target id or a selected clip, so it cannot reuse the clip-keying
        /// pending state that every check above gates on.
        /// </summary>
        private bool hasPendingRigPoseEdit;
        private float3 pendingRigPosition;
        private float3 pendingRigRotationDegrees;
        private float3 pendingRigScale;

        // Whether this window has written the prefab's base pose since it opened. An undo that
        // reverts a base pose changes the prefab asset, but nothing in the ordinary undo refresh
        // re-reads the preview instance — this flag is what limits the reload to sessions that
        // actually did a rig edit, rather than reloading on every Ctrl+Z.
        private bool hasWrittenPrefabPose;

        /// <summary>The VAT bake tab, and the panel built into it the first time it is opened.</summary>
        private VisualElement vatBakePane;
        private VatBakePanel vatBakePanel;

        /// <summary>The Rigs tab’s cover pane, and the panel built into it the first time it is opened.</summary>
        private VisualElement newRigPane;
        private RigsPanel rigsPanel;

        /// <summary>The Clip Sets tab's cover pane, and the panel built into it the first time it is opened.</summary>
        private VisualElement clipSetsPane;
        private ClipSetsPanel clipSetsPanel;

        /// <summary>The Actor Editor pane, and the panel built into it the first time it is opened.</summary>
        private VisualElement actorEditorPane;
        private ActorEditorPanel actorEditorPanel;

        /// <summary>
        /// Which view is showing. One field, not a bool per pane: exactly one is true, and four
        /// bools that must sum to one is how you end up with two lit tabs over one pane.
        /// </summary>
        private ClipEditorTab activeTab = ClipEditorTab.ClipEditor;

        /// <summary>
        /// The tab toggles, indexed by <see cref="ClipEditorTab"/>. Held rather than re-queried
        /// because every switch has to write the ones that did not change, and a lookup miss would
        /// leave one lit alongside the new one.
        /// </summary>
        private readonly ToolbarToggle[] tabToggles = new ToolbarToggle[6];

        /// <summary>The Cutscene Editor's cover pane, and the panel built into it the first time it is opened.</summary>
        private VisualElement cutscenePane;
        private CutsceneEditorPanel cutscenePanel;

        /// <summary>Guards the re-entry a tab switch causes by assigning the other toggles' values.</summary>
        private bool isApplyingTab;

        /// <summary>
        /// The floating rows over the viewport: the clip set / rig identity controls, and the gizmo
        /// and preview toggles. Held so a tab switch can hide the whole stack in one call.
        /// </summary>
        private VisualElement viewportOverlay;

        /// <summary>The three gizmo-mode toggles, indexed by <see cref="GizmoMode"/>.</summary>
        private readonly ToolbarToggle[] gizmoModeToggles = new ToolbarToggle[3];

        /// <summary>Guards the re-entry writing the other two gizmo toggles causes.</summary>
        private bool isApplyingGizmoMode;

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
        private ScrollView inspectorPane;
        private Label statusLabel;
        private Image previewImage;
        private Label previewStatusLabel;
        private ValidationBadgeElement validationBadge;
        private ObjectField skinnedSourceField;

        private ClipPreviewController previewController;
        private bool previewRegistryDirty;
        private double previewDirtiedAt;
        private double previewLastRefreshedAt;

        /// <summary>
        /// Pane rebuilds a live pointer gesture has postponed, flushed by the tick once it ends.
        /// </summary>
        // See IsPointerGestureInProgress for why a rebuild during a drag is fatal.
        private bool inspectorRebuildPending;
        private bool timelineRebuildPending;
        private bool hierarchyRebuildPending;

        private ClipSetAsset clipSet;

        /// <summary>
        /// The rig this window is playing the open set against. Window state, stored on no asset —
        /// a rig and a clip set are independent, and the two toolbar pickers above are independent
        /// with them.
        /// </summary>
        private RigAsset activeRig;

        // The rig an open Clip Editor is currently showing, or null when none is open or none is
        // picked. The one way a rig reaches code outside this window now that no asset records one.
        internal static RigAsset RigOfOpenWindow
        {
            get
            {
                ClipEditorWindow[] openWindows =
                    Resources.FindObjectsOfTypeAll<ClipEditorWindow>();
                for (int windowIndex = 0; windowIndex < openWindows.Length; windowIndex++)
                {
                    if (openWindows[windowIndex].activeRig != null)
                    {
                        return openWindows[windowIndex].activeRig;
                    }
                }
                return null;
            }
        }

        private ClipAsset selectedClip;
        private SerializedObject clipSerializedObject;

        private readonly HashSet<KeyAddress> selectedKeys = new HashSet<KeyAddress>();

        // The key the inspector edits: the one most recently clicked, not an arbitrary member of the
        // selection. A HashSet has no order, so iterating selectedKeys for "the last" one showed
        // whichever key the hash buckets happened to yield, not the one the user clicked.
        private KeyAddress activeKey;
        private bool hasActiveKey;

        // What is selected in the hierarchy, as the tree item id — which is also the preview's
        // index for the same transform. -1 is nothing. An index rather than a name, since names
        // repeat; an index rather than a Transform, since the preview skeleton rebuilds whenever the rig changes.
        private int selectedHierarchyItemId = NothingSelectedItemId;

        // What a hierarchy row stands for: the rig's parts, and the previewed prefab's transforms —
        // two kinds sharing one tree since they are the two kinds of thing a clip animates. An enum
        // rather than a pair of booleans, since most code asks "is this a prefab transform".
        private enum HierarchyItemKind
        {
            /// <summary>A transform of the previewed prefab.</summary>
            PrefabTransform,

            /// <summary>A part the rig declares, which transform and flipbook tracks bind to.</summary>
            RigTarget
        }

        private sealed class HierarchyItem
        {
            public HierarchyItemKind kind;
            public string displayName;

            // The rig part this row is, or 0 when the rig declares none for it. Set for a rig-target
            // row always, and for a previewed node whenever a part records that node's path as its
            // source — everything asking "which part is this row" reads this, not the row's kind.
            public uint targetId;

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

        /// <summary>Rebuilt per paste, which is once per keystroke and not per frame.</summary>
        private readonly List<ClipObjectRef> pasteDestinations = new List<ClipObjectRef>();

        /// <summary>The row the gizmo and the viewport outline follow, of the several selected.</summary>
        private int activeHierarchyItemId = NothingSelectedItemId;

        /// <summary>
        /// The previous selection, so the next change can be diffed to find the row just added.
        /// </summary>
        private readonly HashSet<int> previouslySelectedItemIds = new HashSet<int>();

        /// <summary>Set while a selection change is being applied, to stop it re-entering itself.</summary>
        private bool isHandlingHierarchySelection;

        private Button editPrefabButton;
        private ToolbarToggle rigEditToggle;
        private ToolbarToggle ragdollPreviewToggle;
        private VisualElement reconcilePanel;
        private ScrollView reconcileList;
        private Label reconcileTitle;
        private VisualElement viewportFrame;
        private Label rigEditBanner;

        // The selected transform's name, which is the identity bone tracks bind by. Carried
        // alongside the index rather than derived from it, so the bake's contract and the window's
        // selection stay separate things.
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
        // Where Stop returns the playhead: the position it held before playback last started.
        private float prePlayPlayheadTime;

        // Drag state. The undo group is captured on pointer-down so every move inside the gesture
        // collapses into it on release.
        private bool isDraggingKeys;
        private int gestureUndoGroup;
        private string gestureUndoName;
        private float dragPreviousTime;
        private TimelineTrackKind dragTrackKind;
        private int dragTrackIndex;

        // Opens the Clip Editor, docked beside the Scene view when it is being created. The dock
        // neighbour is a request, not a command: Unity honours it only when the window is created,
        // and an existing window keeps wherever the user put it.
        [MenuItem("Window/DOTS Animation Toolkit/DOTS Animator")]
        public static void ShowWindow()
        {
            ClipEditorWindow window = GetWindow<ClipEditorWindow>(
                "DOTS Animator", ClipEditorDocking.PreferredDockNeighbours());
            window.titleContent = new GUIContent("DOTS Animator");
            window.minSize = new Vector2(820f, 460f);
        }

        /// <summary>Brings the Clip Editor forward on its Actor Editor tab, with <paramref name="profile"/> loaded.</summary>
        public static void FocusWithActorEditorTab(ActorProfileAsset profile)
        {
            // After FocusTab, not before: the pane's panel is built on first switch to it, so
            // addressing it earlier would target a panel that does not exist yet this session.
            ClipEditorWindow window = FocusTab(ClipEditorTab.ActorEditor);
            if (window != null && window.actorEditorPanel != null && profile != null)
            {
                // Raises ProfileChanged, which OnActorEditorProfileChanged answers by pointing the
                // toolbar Rig field at profile.rig — one writer, not two.
                window.actorEditorPanel.Profile = profile;
            }
        }

        /// <summary>Brings the Clip Editor forward on its Cutscene Editor tab, with <paramref name="cutscene"/> loaded.</summary>
        public static void FocusCutsceneTab(DotsAnimationToolkit.Authoring.CutsceneAsset cutscene)
        {
            ClipEditorWindow window = FocusTab(ClipEditorTab.CutsceneEditor);
            if (window != null && window.cutscenePanel != null && cutscene != null)
            {
                window.cutscenePanel.LoadCutscene(cutscene);
            }
        }

        /// <summary>Brings the Clip Editor forward on its Clip Editor tab, opening it if it is closed.</summary>
        public static void FocusClipEditing()
        {
            FocusTab(ClipEditorTab.ClipEditor);
        }

        /// <summary>Brings the Clip Editor forward on its VAT bake tab.</summary>
        public static void FocusVatBakeSettings()
        {
            FocusTab(ClipEditorTab.VatBake);
        }

        // What every "go there, show that" entry point resolves to, so the window is opened,
        // focused and switched by one path. The view is switched through SetActiveTab, never by
        // calling a Show…Tab method directly, since that is the single writer of the toggles' lit state.
        private static ClipEditorWindow FocusTab(ClipEditorTab tab)
        {
            ClipEditorWindow window = FindOpenWindow();
            if (window == null)
            {
                ShowWindow();
                window = FindOpenWindow();
            }
            if (window == null)
            {
                return null;
            }
            window.Focus();
            window.SetActiveTab(tab);
            return window;
        }

        private static ClipEditorWindow FindOpenWindow()
        {
            ClipEditorWindow[] openWindows = Resources.FindObjectsOfTypeAll<ClipEditorWindow>();
            for (int windowIndex = 0; windowIndex < openWindows.Length; windowIndex++)
            {
                if (openWindows[windowIndex] != null)
                {
                    return openWindows[windowIndex];
                }
            }
            return null;
        }

        // Re-creates a floating window as a docked one, carrying what makes it the same window.
        // Unity has no API to dock an existing window, so the only route is to close and reopen it
        // asking for a dock neighbour; the close is deferred since this runs from inside the
        // window's own event handling.
        private void RedockBesideSceneView(System.Action afterDocked)
        {
            ClipEditorDocking.CarriedState state = new ClipEditorDocking.CarriedState
            {
                clipSet = clipSet,
                selectedClip = selectedClip,
                rig = ActiveRig,
                playheadTime = playheadTime,
                rigEditMode = IsRigEditMode,
                tab = (int)activeTab
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
        /// <returns>Whether there was a carried state to adopt.</returns>
        private bool AdoptCarriedState()
        {
            ClipEditorDocking.CarriedState state = ClipEditorDocking.ConsumePendingState();
            if (state == null)
            {
                return false;
            }

            RestoreView(
                state.clipSet as ClipSetAsset, state.rig as RigAsset,
                state.selectedClip as ClipAsset, state.rigEditMode,
                state.playheadTime, (ClipEditorTab)state.tab, state.selectedNames);
            return true;
        }

        // -------------------------------------------------------------------------------------
        // Surviving a domain reload.
        // -------------------------------------------------------------------------------------

        // What the window was looking at, kept across a recompile. A domain reload destroys and
        // re-creates this instance, and every plain field comes back at its default, so the window
        // redraws looking healthy while holding nothing (every control gated on a clip set quietly
        // does nothing when pressed) unless this is restored.
        [SerializeField] private ClipSetAsset sessionClipSet;
        [SerializeField] private RigAsset sessionRig;
        [SerializeField] private ClipAsset sessionSelectedClip;
        [SerializeField] private float sessionPlayheadTime;
        [SerializeField] private bool sessionRigEditMode;

        // Carried for the same reason the rig and the playhead are: a recompile while authoring a
        // direction set or a VAT bake should not drop you back on the Clip Editor tab.
        [SerializeField] private ClipEditorTab sessionTab;
        [SerializeField] private List<string> sessionSelectedNames = new List<string>();
        [SerializeField] private bool hasSessionState;

        private void RememberSessionState()
        {
            sessionClipSet = clipSet;
            sessionRig = activeRig;
            sessionSelectedClip = selectedClip;
            sessionPlayheadTime = playheadTime;
            sessionRigEditMode = IsRigEditMode;
            sessionTab = activeTab;
            sessionSelectedNames.Clear();
            for (int itemIndex = 0; itemIndex < selectedHierarchyItems.Count; itemIndex++)
            {
                sessionSelectedNames.Add(selectedHierarchyItems[itemIndex].displayName);
            }
            hasSessionState = true;
        }

        private void RestoreSessionState()
        {
            if (!hasSessionState)
            {
                return;
            }
            hasSessionState = false;

            RestoreView(
                sessionClipSet, sessionRig, sessionSelectedClip, sessionRigEditMode,
                sessionPlayheadTime, sessionTab, sessionSelectedNames);
        }

        // Puts a remembered view back on a tree that has just been built from scratch — the one
        // operation a re-dock and a domain reload both need. The clip is selected through the list
        // rather than by calling SelectClip directly, so the row is highlighted too.
        private void RestoreView(
            ClipSetAsset restoredClipSet,
            RigAsset restoredRig,
            ClipAsset restoredClip,
            bool restoredRigEditMode,
            float restoredPlayheadTime,
            ClipEditorTab restoredTab,
            List<string> restoredSelectionNames)
        {
            // First, so everything below lands on the view the user was actually on. Switching last
            // would rebuild the dock and then cover it, which is the same picture by a slower route
            // but leaves the panes offered a selection they were not showing at the time.
            SetActiveTab(restoredTab);

            // The rig first, and with notify: it is what the hierarchy and the preview are built
            // from, and OnClipSetChanged below deliberately leaves it alone. Restoring it second
            // would rebuild both panes twice, the first time against no rig at all.
            if (skinnedSourceField != null)
            {
                skinnedSourceField.value = restoredRig;
            }

            if (clipSetField != null)
            {
                // With notify: OnClipSetChanged is what repopulates the clip list, the hierarchy and
                // the preview from the set.
                clipSetField.value = restoredClipSet;
            }

            int restoredClipIndex = restoredClip != null && restoredClipSet != null
                && restoredClipSet.clips != null
                ? restoredClipSet.clips.IndexOf(restoredClip)
                : -1;
            if (clipListView != null && restoredClipIndex >= 0)
            {
                clipListView.SetSelection(restoredClipIndex);
                clipListView.ScrollToItem(restoredClipIndex);
            }
            else if (restoredClip != null)
            {
                // The clip is no longer in the set — deleted, or moved to another set while this
                // window was down. Loading it anyway would show a timeline the clip list disagrees
                // with, so the set is all that comes back.
                SelectClip(null);
            }

            if (rigEditToggle != null)
            {
                rigEditToggle.SetValueWithoutNotify(restoredRigEditMode);
                ApplyRigEditChrome();
            }

            // Reuses the round-trip restore, because it is the same problem: put the playhead and
            // the selection back on a tree that has just been rebuilt from scratch.
            roundTripPlayheadTime = restoredPlayheadTime;
            roundTripSelectedNames.Clear();
            roundTripSelectedNames.AddRange(restoredSelectionNames);
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
            cameraNavigation.Rig = previewController;

            // Raised while this instance is still alive and before Unity serializes it, which is the
            // only moment the state below can still be read. See RememberSessionState.
            AssemblyReloadEvents.beforeAssemblyReload += RememberSessionState;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorApplication.update -= OnEditorTick;
            PrefabStage.prefabSaved -= OnPrefabStageSaved;
            PrefabStage.prefabStageClosing -= OnPrefabStageClosing;
            AssemblyReloadEvents.beforeAssemblyReload -= RememberSessionState;

            // Again here, so the capture does not depend on Unity raising beforeAssemblyReload
            // before OnDisable rather than after. Both run before this instance is serialized, and
            // the second call simply overwrites the first with the same values. On a plain window
            // close it is written into an instance that is about to be destroyed, and costs nothing.
            RememberSessionState();

            // Before the controller it borrows is disposed, and that order is the whole point. The
            // panel's tick is an EditorApplication.update subscription of its own; left registered
            // it would go on calling Render on a disposed controller every editor tick, from a
            // window that has already closed. Disposed rather than merely dropped (A71): the panel
            // now owns an ActorPreviewComposer, which holds a Persistent-allocator blob and layer
            // array of its own, on top of rendering through the window's controller.
            if (actorEditorPanel != null)
            {
                actorEditorPanel.SetTicking(false);
                actorEditorPanel.Dispose();
                actorEditorPanel = null;
            }

            // Both cover panes own a PreviewRenderUtility of their own, plus a copy of whatever
            // prefab they were showing. Same rule as the controller below: nothing here is GC'd.
            if (rigsPanel != null)
            {
                rigsPanel.Dispose();
                rigsPanel = null;
            }
            if (clipSetsPanel != null)
            {
                clipSetsPanel.Dispose();
                clipSetsPanel = null;
            }
            if (vatBakePanel != null)
            {
                vatBakePanel.Dispose();
                vatBakePanel = null;
            }

            // The preview owns a Persistent-allocator blob and a PreviewRenderUtility, neither of
            // which the GC reclaims. Leaking them survives domain reloads as a growing native
            // allocation, so disposal here is load-bearing rather than tidy.
            if (previewController != null)
            {
                previewController.Dispose();
                previewController = null;
            }

            // Not saved and owned by nothing else, so it is this window's to destroy. Its undo
            // entries are left pointing at a dead object, which is inert: the window they described
            // the held value of has gone with it.
            if (heldTransformEdit != null)
            {
                DestroyImmediate(heldTransformEdit);
                heldTransformEdit = null;
            }
        }

        /// <summary>
        /// Undo replaces the key lists wholesale, so held addresses may now point past the end.
        /// Clearing the selection is the honest response — keeping it would leave the window
        /// showing a selection of keys that no longer exist.
        /// </summary>
        private void OnUndoRedo()
        {
            // A gesture holds times recorded before the undo, so finishing it afterwards would
            // write them back over whatever the undo restored. Discarded rather than cancelled:
            // cancelling restores those same stale times and reverts a group from inside the undo
            // callback that is already running.
            DiscardKeyTransform();

            selectedKeys.Clear();
            hasActiveKey = false;
            RefreshSerializedClip();
            MarkPreviewDirty();
            RebuildTimeline();

            // Undo can restore a different clip length or frame rate, and the ruler and the
            // transport fields both read from those rather than deriving them.
            OnClipTimingChanged();
            RebuildInspector();

            // An undo can put a held, unkeyed move back (HeldTransformEdit) or take one away, and
            // the gizmo is drawn at the pose that value decides. Left alone it would sit where the
            // part no longer is.
            RefreshGizmo();

            // The preview is an instance of the prefab, so an undo that put a base pose back has
            // changed the asset underneath it and nothing above re-reads that. Gated on the flag
            // rather than done every time — see hasWrittenPrefabPose.
            if (hasWrittenPrefabPose)
            {
                ReloadAfterPrefabEdit();
            }
        }

        // -------------------------------------------------------------------------------------
        // Layout. The tree comes from UXML; everything below resolves slots and wires behaviour.
        // -------------------------------------------------------------------------------------

        // Builds the tree and wires every control to it. Unity's one call per live visual tree,
        // including after every domain reload. Must run exactly once per tree and never be called by
        // hand: RegisterTransportShortcuts and BindKeyTransform register on rootVisualElement itself,
        // which survives Clear() below, so a second run doubles the transport's key handlers.
        private void CreateGUI()
        {
            rootVisualElement.Clear();

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

            // After BindTimeline, not with the rest of the toolbar bindings where it used to sit:
            // the three GeometryChanged handlers that re-apply the view on a resize hang off
            // laneStack, laneColumn and the scroll viewport, and BindTimeline is what resolves the
            // first two. Bound before them, those three registrations were skipped every time —
            // silently, since each is guarded by a null check — and the timeline kept painting at
            // the pixel scale it had before the pane was dragged to a new size.
            BindTimelineView();
            BindSplits();

            // Sync the preview with the state the window opened in, so the viewport reports "no clip
            // set" from the first frame instead of an empty status line.
            if (previewController != null)
            {
                previewController.SetClipSet(clipSet);
            }
            if (validationBadge != null)
            {
                validationBadge.Refresh(activeRig, clipSet);
            }

            RefreshClipActionButtons();
            RebuildHierarchy();
            RebuildTimeline();

            // Last, because both drive the fields and the tree this method has only just finished
            // building. A re-dock carries its state in hand and takes precedence; otherwise this is
            // either a first open, which restores nothing, or the far side of a domain reload.
            if (!AdoptCarriedState())
            {
                RestoreSessionState();
            }
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

            // Snap and Auto Key are no longer in the top bar. They sit on the status row over the
            // key area with the scale pivot, because all three answer "what will my next edit here
            // do" — a different question from the clip set and rig identity this bar is for.
            // Still resolved from this method, which is where every toggle in the window is bound:
            // Q searches the whole tree, and splitting the bindings by which row an element ended
            // up in would only make them harder to find.
            snapToggle = rootVisualElement.Q<ToolbarToggle>("snap-toggle");
            BindTransportBar();
            BindKeyTransform();

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
                SetOverlayToolIcon(
                    billboardPreviewToggle, billboardPreviewToggle.Q<Image>("billboard-preview-icon"),
                    "d_BillboardRenderer Icon", "Billboard");
            }

            // Drives the toolkit's own preview simulation (RagdollPreviewSimulation), never a host
            // game's ragdoll systems — a package cannot name a consuming project's namespaces.
            ragdollPreviewToggle = rootVisualElement.Q<ToolbarToggle>("ragdoll-preview-toggle");
            if (ragdollPreviewToggle != null)
            {
                ragdollPreviewToggle.tooltip =
                    "Drop the previewed rig as an active ragdoll — its own physics, ground contact "
                    + "and self-collision — to see whether a pose still reads on impact. Turning it "
                    + "off restores the pose exactly.";
                ragdollPreviewToggle.RegisterValueChangedCallback(OnRagdollPreviewToggleChanged);
                SetOverlayToolIcon(
                    ragdollPreviewToggle, ragdollPreviewToggle.Q<Image>("ragdoll-preview-icon"),
                    "d_Avatar Icon", "Ragdoll");
            }

            actorEditorPane = rootVisualElement.Q<VisualElement>("actor-editor-pane");
            vatBakePane = rootVisualElement.Q<VisualElement>("vat-bake-pane");
            newRigPane = rootVisualElement.Q<VisualElement>("new-rig-pane");
            clipSetsPane = rootVisualElement.Q<VisualElement>("clip-sets-pane");
            cutscenePane = rootVisualElement.Q<VisualElement>("cutscene-pane");

            // Before BindTabs, which hides the whole stack on any tab but Clip Editor.
            viewportOverlay = rootVisualElement.Q<VisualElement>("viewport-overlay");

            BindGizmoModeToggle(GizmoMode.Move, "gizmo-move-toggle", "gizmo-move-icon",
                "d_MoveTool", "Move",
                "Move the selected part, bone or socket. Same as pressing W in the viewport.");
            BindGizmoModeToggle(GizmoMode.Rotate, "gizmo-rotate-toggle", "gizmo-rotate-icon",
                "d_RotateTool", "Rotate",
                "Rotate the selection. Same as pressing E in the viewport.");
            BindGizmoModeToggle(GizmoMode.Scale, "gizmo-scale-toggle", "gizmo-scale-icon",
                "d_ScaleTool", "Scale",
                "Scale the selection. Same as pressing R in the viewport.");
            SetGizmoMode(gizmoMode);

            // A command rather than a mode, so its own run in the strip. See
            // ClipEditorWindow.CameraNavigation for the gestures it undoes.
            ToolbarButton resetCameraButton =
                rootVisualElement.Q<ToolbarButton>("reset-camera-button");
            if (resetCameraButton != null)
            {
                resetCameraButton.tooltip =
                    "Put the camera back where the window opened it: head-on, centred on this rig "
                    + "and backed off to fit it. Undoes any orbit, pan or flight. Same as "
                    + "double-clicking the viewport.\n\n"
                    + "Viewport camera: drag to orbit, middle-drag to pan, right-drag to look "
                    + "around, right-drag + W/A/S/D and Q/E to fly (Shift for faster), "
                    + "Alt + right-drag or the wheel to zoom, F to frame the selection.";
                resetCameraButton.clicked += ResetViewportCamera;
                // A viewfinder frame, not a physical camera body: this centres and fits the rig,
                // it does not represent a camera object in the scene.
                SetOverlayToolIcon(
                    resetCameraButton, resetCameraButton.Q<Image>("reset-camera-icon"),
                    "d_FrameCapture", "Reset Camera");
            }

            BindTabs();

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
                autoKeyToggle.EnableInClassList(RecordingBarActionUssClassName, autoKeyToggle.value);
                autoKeyToggle.RegisterValueChangedCallback(changeEvent =>
                {
                    autoKeyToggle.EnableInClassList(RecordingBarActionUssClassName, changeEvent.newValue);
                    // Turning auto-key on adopts whatever is currently held, rather than discarding
                    // it — the user has just said they want their edits kept.
                    if (changeEvent.newValue && hasPendingTransformEdit)
                    {
                        CommitPendingTransformEdit();
                    }
                    RebuildInspector();
                });
            }

            // The rig this window plays the open set against. Window state only — no asset records
            // it, and picking one here changes nothing any actor bakes. The rig's own sourcePrefab
            // is what the preview instantiates and the hierarchy pane lists, so an empty field is
            // an empty hierarchy pane rather than a missing one.
            skinnedSourceField = rootVisualElement.Q<ObjectField>("skinned-source-field");
            if (skinnedSourceField != null)
            {
                skinnedSourceField.objectType = typeof(RigAsset);
                skinnedSourceField.allowSceneObjects = false;
                skinnedSourceField.tooltip =
                    "The rig this clip set animates. Its Source Prefab (set on the rig asset "
                    + "itself) is what the preview instantiates for bone tracks — use the Rigs tab to "
                    + "create one, or open an existing rig to assign or change its prefab.";
                skinnedSourceField.RegisterValueChangedCallback(OnSkinnedSourceChanged);
            }

            VisualElement badgeSlot = rootVisualElement.Q<VisualElement>("validation-badge-slot");
            if (badgeSlot != null)
            {
                validationBadge = new ValidationBadgeElement();
                badgeSlot.Add(validationBadge);
            }
        }

        private TargetTagRegistry ResolveTargetTagRegistry()
        {
            return VocabularyRegistryProvider.TargetTags;
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

        // Creates a clip in the assigned set and selects it, ready to author. Shared with the clip
        // set's own inspector via ClipAssetUtility, so a clip made here is indistinguishable from
        // one made there. Pinged as well as selected, since it is written to disk without asking where.
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

        // Enables the Clips pane's actions for the states in which they mean something. A clip is
        // only meaningful inside a set, so "no set assigned" disables New; Delete additionally needs
        // a clip selected.
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

        // Asks what to do with the selected clip, then un-registers it and optionally trashes it.
        // Three answers, since "remove from the set" and "delete the file" are different intentions
        // a two-button dialog would conflate. Deleting the asset is not undoable; removing from the set is.
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
                validationBadge.Refresh(activeRig, clipSet);
            }
        }

        /// <summary>Selects whatever now occupies <paramref name="removedIndex"/>, or the last clip.</summary>
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

        /// <summary>Creates a clip set wherever the user chooses, and loads it into the window.</summary>
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

            editPrefabButton = rootVisualElement.Q<Button>("edit-prefab-button");
            if (editPrefabButton != null)
            {
                editPrefabButton.clicked += OpenPrefabForSelection;
            }
            RefreshPrefabActionState();
        }

        // Disabled rather than left to fail on click: a scene object dropped into the rig field
        // has no prefab asset behind it to open.
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
                : "Assign a rig in the toolbar's Rig field, and give that rig a Source Prefab, to edit it.";
        }

        // The one place that reads the rig's prefab, so every consumer below follows the rig field
        // to the same answer.
        private GameObject LoadedPrefab
        {
            get
            {
                RigAsset rig = skinnedSourceField != null ? skinnedSourceField.value as RigAsset : null;
                return rig != null ? rig.sourcePrefab : null;
            }
        }

        /// <summary>The path of a hierarchy row's object below the prefab root, for addressing it in a stage.</summary>
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

            Transform node;
            switch (item.kind)
            {
                case HierarchyItemKind.RigTarget:
                    // The recorded path first, when the target has one: two planes called "Plane"
                    // is the ordinary case, and a name match would pick whichever came first.
                    node = ResolveTargetSourceNode(item.targetId, root);
                    if (node == null)
                    {
                        node = PrefabAuthoringBridge.FindByName(root, item.displayName);
                    }
                    break;
                default:
                    node = previewController.GetTransformByIndex(item.previewIndex);
                    break;
            }
            return node != null ? PrefabAuthoringBridge.GetHierarchyPath(node, root) : string.Empty;
        }

        /// <summary>The previewed node a rig target records as its source, or null when it has none.</summary>
        private Transform ResolveTargetSourceNode(uint targetId, Transform root)
        {
            RigAsset rig = ActiveRig;
            if (rig == null || rig.targets == null || root == null || targetId == 0u)
            {
                return null;
            }
            for (int targetIndex = 0; targetIndex < rig.targets.Count; targetIndex++)
            {
                RigTargetDefinition target = rig.targets[targetIndex];
                if (target == null || target.Id.Value != targetId
                    || string.IsNullOrEmpty(target.sourceNodePath))
                {
                    continue;
                }
                return PrefabAuthoringBridge.ResolveByPath(root, target.sourceNodePath);
            }
            return null;
        }

        // -------------------------------------------------------------------------------------
        // Round trip. Prefab mode owns the structure; this window owns the animation bound to it,
        // and has to be told when the first changes under the second.
        // -------------------------------------------------------------------------------------

        /// <summary>The playhead and selection to restore once the rebuilt tree is standing.</summary>
        private float roundTripPlayheadTime;
        private readonly List<string> roundTripSelectedNames = new List<string>();
        private bool hasRoundTripState;

        // Remembered by name, not tree id: the ids index a hierarchy walk the prefab edit is about
        // to invalidate, and a name that fails to resolve is the rename/delete signal the reconciler reports.
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
        private void FocusSelf()
        {
            // Called from a deferred callback that can outlive the window; this is the
            // Unity-object null check, which a plain reference comparison would miss.
            if (this == null)
            {
                return;
            }
            Focus();
        }

        /// <summary>Whether a stage is editing the prefab this window has loaded.</summary>
        private bool IsStageOurPrefab(PrefabStage stage)
        {
            if (stage == null)
            {
                return false;
            }
            string loadedPath = PrefabAuthoringBridge.ResolveAssetPath(LoadedPrefab);
            return !string.IsNullOrEmpty(loadedPath) && stage.assetPath == loadedPath;
        }

        /// <summary>Rebuilds everything downstream of the prefab, then reports what no longer binds.</summary>
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

            // Order matters: the tree must rebuild from the new hierarchy before selection/playhead
            // restore, and reconciliation last, since it diffs against that rebuilt hierarchy.
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

        // A drag in Animate mode writes a key into a clip; the same drag in Rig Edit mode writes the
        // prefab asset instead, so the toolbar, viewport border and Auto Key all reflect the mode.
        /// <summary>Whether a gizmo drag edits the rig's base setup instead of keying the clip.</summary>
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

        /// <summary>Wires the Ragdoll toolbar toggle to the preview controller's enable/disable calls.</summary>
        private void OnRagdollPreviewToggleChanged(ChangeEvent<bool> changeEvent)
        {
            if (previewController == null)
            {
                return;
            }

            if (changeEvent.newValue)
            {
                string refusalReason;
                if (!previewController.TryEnableRagdollPreview(out refusalReason))
                {
                    // Refuses to engage: the toggle snaps back off and the status line says why.
                    ragdollPreviewToggle.SetValueWithoutNotify(false);
                    previewController.ReportTransientStatus(refusalReason);
                    return;
                }

                // A held, unkeyed edit belongs to the clip at the frozen playhead; a ragdoll about
                // to own every transform underneath it is not a context that edit survives into.
                DiscardPendingTransformEdit();
            }
            else
            {
                previewController.DisableRagdollPreview();
            }

            RebuildInspector();
        }

        // Clicking the lit tab is a no-op, not a toggle-off: nothing sits behind a tab to reveal, so
        // a false value would leave the window showing a pane no tab claims. Snapped back to true.
        /// <summary>Binds the four tab toggles as a radio group.</summary>
        private void BindTabs()
        {
            BindTab(ClipEditorTab.Rigs, "tab-new-rig",
                "Scan a prefab's hierarchy for renderer-bearing nodes, choose which become rig "
                + "targets, and optionally point this clip set at the result.");
            BindTab(ClipEditorTab.ClipSets, "tab-clip-sets",
                "Browse every clip set in the project, create one — name, folder, starting clips — or "
                + "add and remove clips on an existing one.");
            BindTab(ClipEditorTab.ClipEditor, "tab-clip-editor",
                "The clip list, rig hierarchy, viewport, inspector and timeline. What the window "
                + "opens on, and what every other tab is drawn over.");
            BindTab(ClipEditorTab.VatBake, "tab-vat-bake",
                "Bake the open clip set's VAT textures. Nothing is torn down when you leave, so a "
                + "bake, a look at the result and another bake is three clicks rather than three "
                + "windows.");
            BindTab(ClipEditorTab.ActorEditor, "tab-actor-editor",
                "Author an actor profile: layers, the animations on them, each animation's "
                + "direction coverage — and watch them mix in the viewport.");
            BindTab(ClipEditorTab.CutsceneEditor, "tab-cutscene-editor",
                "Stage a multi-actor cutscene: clip blocks and keys on a timeline, scene-view "
                + "posing, a camera lane, and an event/hold lane.");

            ApplyActiveTab();
        }

        private void BindTab(ClipEditorTab tab, string elementName, string tooltip)
        {
            ToolbarToggle toggle = rootVisualElement.Q<ToolbarToggle>(elementName);
            tabToggles[(int)tab] = toggle;
            if (toggle == null)
            {
                return;
            }

            toggle.tooltip = tooltip;
            toggle.RegisterValueChangedCallback(changeEvent =>
            {
                if (isApplyingTab)
                {
                    return;
                }
                SetActiveTab(tab);
            });
        }

        // The one place every tab-changing caller goes through, so a pane can never be shown
        // behind a tab that isn't lit.
        /// <summary>Switches the window to <paramref name="tab"/>.</summary>
        private void SetActiveTab(ClipEditorTab tab)
        {
            activeTab = tab;
            ApplyActiveTab();
        }

        // Resolved per keystroke rather than cached: the panels are rebuilt on tab switches and after
        // a domain reload, and a stale reference would drive a panel that is no longer in the tree.
        private ITransportTarget ResolveActiveTransportTarget()
        {
            switch (activeTab)
            {
                case ClipEditorTab.ClipEditor:
                    return this;
                case ClipEditorTab.CutsceneEditor:
                    return cutscenePanel as ITransportTarget;
                case ClipEditorTab.ActorEditor:
                    return actorEditorPanel as ITransportTarget;
                case ClipEditorTab.VatBake:
                    return vatBakePanel != null ? vatBakePanel.TransportTarget : null;
                default:
                    return null;
            }
        }

        private void ApplyActiveTab()
        {
            // The assignments below raise change callbacks on three toggles that did not change and
            // one that did; without this every switch would re-enter SetActiveTab four times.
            isApplyingTab = true;
            for (int tabIndex = 0; tabIndex < tabToggles.Length; tabIndex++)
            {
                ToolbarToggle toggle = tabToggles[tabIndex];
                if (toggle == null)
                {
                    continue;
                }
                bool isActive = tabIndex == (int)activeTab;
                toggle.SetValueWithoutNotify(isActive);
                toggle.EnableInClassList(TabActiveUssClassName, isActive);
            }
            isApplyingTab = false;

            ShowRigsTab(activeTab == ClipEditorTab.Rigs);
            ShowClipSetsTab(activeTab == ClipEditorTab.ClipSets);
            ShowActorEditorTab(activeTab == ClipEditorTab.ActorEditor);
            ShowVatBakeTab(activeTab == ClipEditorTab.VatBake);
            ShowCutsceneTab(activeTab == ClipEditorTab.CutsceneEditor);

            // The overlay's controls only mean anything while looking at the 3D area, and the cover
            // panes are drawn over the whole body — so on any other tab it is underneath one of them
            // and would only ever be half-visible during a transition. The top bar is the opposite
            // case and stays put: the clip set and rig are what every tab reads.
            if (viewportOverlay != null)
            {
                viewportOverlay.EnableInClassList(
                    HiddenUssClassName, activeTab != ClipEditorTab.ClipEditor);
            }

            // The Actor Editor drives the same ragdoll preview from its own animation triggers
            // (A71-D4); the toolbar toggle is hidden rather than left to fight over one ragdoll
            // state with whatever the last-clicked control said.
            if (ragdollPreviewToggle != null)
            {
                ragdollPreviewToggle.EnableInClassList(
                    HiddenUssClassName, activeTab == ClipEditorTab.ActorEditor);
            }
        }

        /// <summary>Shows or hides the Cutscene Editor over the dock.</summary>
        private void ShowCutsceneTab(bool isShown)
        {
            if (cutscenePane == null)
            {
                return;
            }

            if (isShown && cutscenePanel == null)
            {
                cutscenePanel = new CutsceneEditorPanel();
                cutscenePane.Add(cutscenePanel);
            }

            if (!isShown && cutscenePanel != null)
            {
                cutscenePanel.OnHidden();
            }

            // Covers the dock rather than replacing it (same for the VAT bake, Rigs and
            // Direction Sets cover panes): a hidden TwoPaneSplitView lays out at zero by zero
            // and comes back collapsed with no handle to reopen it.
            cutscenePane.EnableInClassList(HiddenUssClassName, !isShown);
        }

        /// <summary>Shows or hides the VAT bake tab over the editor.</summary>
        private void ShowVatBakeTab(bool isShown)
        {
            if (vatBakePane == null)
            {
                return;
            }

            if (isShown)
            {
                // Built on first use, not at bind time: most sessions never open this stack of
                // object fields, and building it eagerly would tax every window that only ever
                // wanted to edit a clip.
                if (vatBakePanel == null)
                {
                    vatBakePanel = new VatBakePanel();
                    vatBakePane.Add(vatBakePanel);
                }

                vatBakePanel.SetSource(clipSet, activeRig);
            }

            vatBakePane.EnableInClassList(HiddenUssClassName, !isShown);
        }

        /// <summary>Shows or hides the Rigs tab over the editor.</summary>
        private void ShowRigsTab(bool isShown)
        {
            if (newRigPane == null)
            {
                return;
            }

            if (isShown && rigsPanel == null)
            {
                rigsPanel = new RigsPanel();
                rigsPanel.UseInEditorRequested += OnRigUseInEditorRequested;
                rigsPanel.RigTargetsChanged += OnPanelChangedRigTargets;
                newRigPane.Add(rigsPanel);
            }

            if (isShown)
            {
                rigsPanel.SetSource(activeRig);
            }

            newRigPane.EnableInClassList(HiddenUssClassName, !isShown);
        }

        /// <summary>Shows or hides the Clip Sets browse/create/edit flow over the editor.</summary>
        private void ShowClipSetsTab(bool isShown)
        {
            if (clipSetsPane == null)
            {
                return;
            }

            if (isShown && clipSetsPanel == null)
            {
                clipSetsPanel = new ClipSetsPanel();
                clipSetsPanel.OpenInEditorRequested += OnClipSetOpenRequested;
                clipSetsPanel.SetClipsChanged += OnPanelChangedSetClips;
                clipSetsPane.Add(clipSetsPanel);
            }

            if (isShown)
            {
                clipSetsPanel.SetSource(clipSet);
            }

            clipSetsPane.EnableInClassList(HiddenUssClassName, !isShown);
        }

        /// <summary>Shows or hides the Actor Editor pane over the editor.</summary>
        private void ShowActorEditorTab(bool isShown)
        {
            if (actorEditorPane == null)
            {
                return;
            }

            if (isShown)
            {
                if (actorEditorPanel == null)
                {
                    actorEditorPanel = new ActorEditorPanel();
                    actorEditorPanel.ProfileChanged += OnActorEditorProfileChanged;
                    actorEditorPane.Add(actorEditorPanel);
                }

                actorEditorPanel.SetSource(previewController, activeRig);
            }

            actorEditorPane.EnableInClassList(HiddenUssClassName, !isShown);
            if (actorEditorPanel != null)
            {
                actorEditorPanel.SetTicking(isShown);
            }
        }

        // Both panes read the window's selection rather than holding their own, so a change made
        // while a pane is open has to reach it or it would offer to act on a set the window
        // stopped showing.
        /// <summary>Pushes the window's clip set and rig at whichever pane is open.</summary>
        private void RefreshOpenPaneSource()
        {
            if (vatBakePanel != null)
            {
                vatBakePanel.SetSource(clipSet, activeRig);
            }
            if (actorEditorPanel != null)
            {
                actorEditorPanel.SetSource(previewController, activeRig);
            }
            if (clipSetsPanel != null)
            {
                clipSetsPanel.SetSource(clipSet);
            }
            if (rigsPanel != null)
            {
                rigsPanel.SetSource(activeRig);
            }
        }

        // Picking a profile sets the window's Rig, never its Clip Set — the rig is shared by every
        // tab, the clip set is the Clip Editor tab's own edit target.
        /// <summary>Answers the Actor Editor panel picking a different profile: points the toolbar Rig field at it.</summary>
        private void OnActorEditorProfileChanged(ActorProfileAsset pickedProfile)
        {
            if (pickedProfile != null && pickedProfile.rig != null && skinnedSourceField != null)
            {
                skinnedSourceField.value = pickedProfile.rig;
            }
        }

        /// <summary>Answers the Rigs panel asking for a rig to become this window's: points the toolbar Rig field at it and returns to the Clip Editor.</summary>
        private void OnRigUseInEditorRequested(RigAsset rig)
        {
            if (rig != null && skinnedSourceField != null)
            {
                skinnedSourceField.value = rig;
                SetActiveTab(ClipEditorTab.ClipEditor);
            }
        }

        /// <summary>Answers the Rigs panel changing the open rig's targets: re-reads the rig so the hierarchy and bindings follow.</summary>
        private void OnPanelChangedRigTargets(RigAsset rig)
        {
            if (rig == null || rig != activeRig)
            {
                return;
            }
            RebuildHierarchy();
            RebuildTimeline();
            RebuildInspector();
        }

        /// <summary>Answers the Clip Sets panel's "Open in Clip Editor" button: loads the set and switches tabs.</summary>
        private void OnClipSetOpenRequested(ClipSetAsset requestedSet)
        {
            if (clipSetField != null)
            {
                clipSetField.value = requestedSet;
            }
            SetActiveTab(ClipEditorTab.ClipEditor);
        }

        /// <summary>Answers the Clip Sets panel adding or removing a clip on the currently open set.</summary>
        private void OnPanelChangedSetClips(ClipSetAsset changedSet)
        {
            if (changedSet == null || changedSet != clipSet)
            {
                return;
            }
            RefreshClipList();
            RefreshClipActionButtons();
            MarkPreviewDirty();
            if (validationBadge != null)
            {
                validationBadge.Refresh(activeRig, clipSet);
            }
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

        /// <summary>Writes a gizmo drag into the prefab's base pose.</summary>
        private void CommitRigBaseEdit(float3 position, float3 rotationDegrees, float3 scale)
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

            // Absolute local values, not a rest-pose offset: Rig Edit's drag starts from the node's
            // live preview transform, so what arrives here is already the pose to write.
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

            hasWrittenPrefabPose = true;
            ReloadAfterPrefabEdit();
        }

        // -------------------------------------------------------------------------------------
        // Reconciliation.
        // -------------------------------------------------------------------------------------

        private readonly List<BrokenBinding> brokenBindings = new List<BrokenBinding>();
        private readonly HashSet<string> hierarchyNameCache = new HashSet<string>();

        // Transform and sprite tracks never appear here: they bind to a rig target's stable id,
        // which no prefab edit can touch, so listing them would invent a problem to look thorough.
        /// <summary>Re-checks every name-based binding and shows the panel when any has broken.</summary>
        private void RunReconciliation()
        {
            if (previewController == null)
            {
                return;
            }
            previewController.CollectHierarchyNames(hierarchyNameCache);
            BindingReconciler.Collect(ActiveRig, clipSet, hierarchyNameCache, brokenBindings);
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

            RigAsset rig = ActiveRig;
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

        /// <summary>Deletes a broken track, behind a confirmation naming what is lost.</summary>
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

            RigAsset rig = ActiveRig;
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

        // Recollected rather than removing the fixed row: a delete shifts every later index into
        // the same list, and patching findings by hand is exactly the bookkeeping that goes wrong.
        /// <summary>Re-runs the whole check after one fix.</summary>
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

        /// <summary>Builds the right-click menu for one hierarchy row.</summary>
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

        // Writes the rig asset, not the prefab: billboard configuration travels with the rig and is
        // shared by every actor instanced from it, so unlike the reparent drag this is not gated on
        // Rig Edit mode.
        /// <summary>Adds the make/clear billboard-root entries for one row.</summary>
        private void AppendBillboardMenuActions(
            ContextualMenuPopulateEvent menuEvent, HierarchyItem item)
        {
            RigAsset rig = ActiveRig;
            if (rig == null || item == null)
            {
                return;
            }

            ClipObjectRef objectRef = BuildObjectRef(item);
            int existingIndex = FindBillboardRootIndexFor(rig, item);
            if (existingIndex >= 0)
            {
                menuEvent.menu.AppendAction(
                    "Billboard/Remove Billboard",
                    action => ConfirmRemoveComponent(
                        objectRef,
                        new ClipComponentInstance(ClipComponentKind.Billboard, existingIndex)));
                return;
            }

            menuEvent.menu.AppendAction(
                "Billboard/Add Billboard",
                action => AddComponent(objectRef, ClipComponentKind.Billboard));
        }

        /// <summary>The rig's billboard root addressing this row, or −1.</summary>
        private int FindBillboardRootIndexFor(RigAsset rig, HierarchyItem item)
        {
            if (rig.billboardRoots == null)
            {
                return -1;
            }
            RigNodeAddress address = BuildBillboardAddressFor(item);
            for (int rootIndex = 0; rootIndex < rig.billboardRoots.Count; rootIndex++)
            {
                BillboardRootDefinition definition = rig.billboardRoots[rootIndex];
                if (definition == null || definition.address.kind != address.kind)
                {
                    continue;
                }
                if (address.kind == RigNodeAddressKind.RigTarget)
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

        private RigNodeAddress BuildBillboardAddressFor(HierarchyItem item)
        {
            if (item.kind == HierarchyItemKind.RigTarget)
            {
                return new RigNodeAddress
                {
                    kind = RigNodeAddressKind.RigTarget,
                    targetId = item.targetId
                };
            }
            return new RigNodeAddress
            {
                kind = RigNodeAddressKind.HierarchyPath,
                hierarchyPath = ResolveHierarchyPath(item)
            };
        }

        /// <summary>The rig's ragdoll body addressing this row, or −1.</summary>
        private int FindRagdollBodyIndexFor(RigAsset rig, HierarchyItem item)
        {
            if (rig.ragdollBodies == null)
            {
                return -1;
            }
            RigNodeAddress address = BuildRagdollAddressFor(item);
            for (int bodyIndex = 0; bodyIndex < rig.ragdollBodies.Count; bodyIndex++)
            {
                RagdollBodyDefinition definition = rig.ragdollBodies[bodyIndex];
                if (definition == null || definition.address.kind != address.kind)
                {
                    continue;
                }
                if (address.kind == RigNodeAddressKind.RigTarget)
                {
                    if (definition.address.targetId == address.targetId)
                    {
                        return bodyIndex;
                    }
                    continue;
                }
                if (address.kind == RigNodeAddressKind.Bone)
                {
                    if (string.Equals(
                            definition.address.boneName, address.boneName,
                            System.StringComparison.Ordinal))
                    {
                        return bodyIndex;
                    }
                    continue;
                }
                if (string.Equals(
                        definition.address.hierarchyPath,
                        address.hierarchyPath,
                        System.StringComparison.Ordinal))
                {
                    return bodyIndex;
                }
            }
            return -1;
        }

        // Unlike BuildBillboardAddressFor, this can come back RigNodeAddressKind.Bone: billboarding
        // rejects that kind at validation, but a ragdoll body welds cleanly to a skinned bone.
        /// <summary>How a ragdoll body would address this row: by rig-target id, bone name, or hierarchy path.</summary>
        private RigNodeAddress BuildRagdollAddressFor(HierarchyItem item)
        {
            if (item.kind == HierarchyItemKind.RigTarget)
            {
                return new RigNodeAddress
                {
                    kind = RigNodeAddressKind.RigTarget,
                    targetId = item.targetId
                };
            }
            if (previewController != null && previewController.IsSkinnedBone(item.previewIndex))
            {
                return new RigNodeAddress
                {
                    kind = RigNodeAddressKind.Bone,
                    boneName = item.displayName
                };
            }
            return new RigNodeAddress
            {
                kind = RigNodeAddressKind.HierarchyPath,
                hierarchyPath = ResolveHierarchyPath(item)
            };
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

            // The validation findings are shown over the preview, and only while the summary button
            // asks for them. Attached from here rather than built here, because the panel and that
            // button are two halves of one control — see ValidationBadgeElement. BindToolbar has
            // already run, so the badge exists.
            //
            // Into the overlay column itself, as its third child below the two control rows, rather
            // than into a layer of its own. The panel's max-width and max-height are percentages,
            // and a percentage resolves against the parent — the column is frame-sized precisely so
            // "60%" keeps meaning 60% of the 3D area, which is what stops a findings list from
            // eating the space being posed in.
            if (validationBadge != null)
            {
                validationBadge.AttachMessagePanel(viewportOverlay);
            }

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

            // Focusable so W/E/R — and the fly keys they double as — reach the viewport rather than
            // the window's other shortcuts.
            previewImage.focusable = true;
            previewImage.RegisterCallback<KeyDownEvent>(OnViewportKeyDown);
            previewImage.RegisterCallback<KeyUpEvent>(OnViewportKeyUp);
            previewImage.RegisterCallback<PointerDownEvent>(OnPreviewPointerDown);
            previewImage.RegisterCallback<PointerMoveEvent>(OnPreviewPointerMove);
            previewImage.RegisterCallback<PointerUpEvent>(OnPreviewPointerUp);
            previewImage.RegisterCallback<WheelEvent>(OnPreviewWheel);

            // A capture lost without a release — a domain reload, a modal dialog, another element
            // taking it — would otherwise leave a camera gesture running forever. See
            // EndCameraGesture: the stuck state is fly mode, which swallows every keystroke.
            previewImage.RegisterCallback<PointerCaptureOutEvent>(
                captureEvent => EndCameraGesture());
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
            BindTrackHeaderResizer();

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

        // -------------------------------------------------------------------------------------
        // Split persistence
        // -------------------------------------------------------------------------------------

        // Fixed panes are resolved by name, not TwoPaneSplitView.fixedPane: that property is only
        // populated once the split has laid itself out, and this runs before first layout.
        /// <summary>Restores each split's stored position and keeps storing it as the user drags.</summary>
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

        // Deferred to the split's first layout: that is the earliest moment its real dimension
        // exists, since a first-open window's position is still the default rect.
        /// <summary>Sizes a never-before-opened split to a fraction of itself, once it knows how big it is.</summary>
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

        // A drag strip, not a TwoPaneSplitView: this row lives inside the timeline's scroll view,
        // whose height is whatever the tracks add up to, so there is nothing definite for a split
        // to divide.
        /// <summary>Makes the track-name column draggable, and remembers where it was left.</summary>
        private void BindTrackHeaderResizer()
        {
            trackHeaderStack = rootVisualElement.Q<VisualElement>("track-header-stack");
            trackHeaderResizer = rootVisualElement.Q<VisualElement>("track-header-resizer");
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
            VisualElement timelineRow = rootVisualElement.Q<VisualElement>("timeline-row");
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
        // Viewport gestures
        // -------------------------------------------------------------------------------------

        /// <summary>
        /// W / E / R switch the gizmo mode, matching every other 3D tool — unless the right button
        /// is down, where the same keys fly the camera, matching the Scene view. F frames.
        /// </summary>
        private void OnViewportKeyDown(KeyDownEvent keyEvent)
        {
            // First: while flying, no key is a gizmo shortcut. See TryHandleFlyKeyDown.
            if (TryHandleFlyKeyDown(keyEvent))
            {
                keyEvent.StopPropagation();
                return;
            }

            // F frames here as it does over the timeline (OnTimelineKeyDown), and means the same
            // thing in both: fit what this pane is for around the selection. The panes own separate
            // focus, so which one is under the cursor decides which framing you get.
            if (keyEvent.keyCode == KeyCode.F)
            {
                FrameViewportSelection();
                keyEvent.StopPropagation();
                return;
            }

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

            SetGizmoMode(requestedMode);
            keyEvent.StopPropagation();
        }

        // Both the W/E/R keys and the overlay's buttons must land here: the toggles have to be
        // written whichever one triggered the change, or a gizmo can describe a mode it isn't in.
        /// <summary>The single writer of gizmo mode.</summary>
        private void SetGizmoMode(GizmoMode mode)
        {
            gizmoMode = mode;

            isApplyingGizmoMode = true;
            for (int modeIndex = 0; modeIndex < gizmoModeToggles.Length; modeIndex++)
            {
                ToolbarToggle toggle = gizmoModeToggles[modeIndex];
                if (toggle != null)
                {
                    toggle.SetValueWithoutNotify(modeIndex == (int)mode);
                }
            }
            isApplyingGizmoMode = false;

            RefreshGizmo();
        }

        private void BindGizmoModeToggle(
            GizmoMode mode, string elementName, string iconElementName, string iconName,
            string fallbackText, string tooltip)
        {
            ToolbarToggle toggle = rootVisualElement.Q<ToolbarToggle>(elementName);
            gizmoModeToggles[(int)mode] = toggle;
            if (toggle == null)
            {
                return;
            }

            toggle.tooltip = tooltip;
            toggle.RegisterValueChangedCallback(changeEvent =>
            {
                if (isApplyingGizmoMode)
                {
                    return;
                }
                // A radio group, like the tabs: clicking the lit mode is a no-op rather than a way
                // to have no gizmo mode at all.
                SetGizmoMode(mode);
            });
            SetOverlayToolIcon(toggle, toggle.Q<Image>(iconElementName), iconName, fallbackText);
        }

        // The viewport tool rail is icon-first: a built-in editor icon reads at a glance where the
        // word it replaces would not fit the rail's width. A name that stops resolving (an editor
        // version dropping it) must cost the button its picture, never the button itself, so every
        // caller falls back to the same word the control used to show as text.
        private static void SetOverlayToolIcon(Button control, Image icon, string iconName, string fallbackText)
        {
            Texture iconTexture = ResolveOverlayToolIconTexture(iconName);
            if (iconTexture != null && icon != null)
            {
                icon.image = iconTexture;
                return;
            }
            if (icon != null)
            {
                icon.RemoveFromHierarchy();
            }
            if (control != null)
            {
                control.text = fallbackText;
            }
        }

        private static void SetOverlayToolIcon(Toggle control, Image icon, string iconName, string fallbackText)
        {
            Texture iconTexture = ResolveOverlayToolIconTexture(iconName);
            if (iconTexture != null && icon != null)
            {
                icon.image = iconTexture;
                return;
            }
            if (icon != null)
            {
                icon.RemoveFromHierarchy();
            }
            if (control != null)
            {
                control.text = fallbackText;
            }
        }

        private static Texture ResolveOverlayToolIconTexture(string iconName)
        {
            GUIContent iconContent = EditorGUIUtility.IconContent(iconName);
            return iconContent != null ? iconContent.image : null;
        }

        // Pivot comes from the authored value, not the mirrored quad: the quad follows the built
        // registry, which rebuilds on a debounce, so a gizmo anchored to it would lag its own drag.
        /// <summary>Puts the gizmo on the selected part at the value currently displayed.</summary>
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

            // Rig Edit answers "is there a gizmo" by node alone -- it writes the prefab's base pose,
            // which has nothing to do with a clip or with whether the rig declares this node a
            // target, so any selected hierarchy node qualifies. See GizmoDragRouting for the shared
            // rule; it used to be reimplemented here as "selectedTargetId == 0u || selectedClip ==
            // null", which is a clip-authoring question and made Rig Edit dead whenever no clip was
            // open or the node was a bare grouping transform or skinned bone.
            HierarchyItem activeRigEditItem = IsRigEditMode ? ActiveHierarchyItem : null;
            if (!GizmoDragRouting.ShouldShowTransformGizmo(
                    IsRigEditMode, activeRigEditItem != null, selectedTargetId != 0u, selectedClip != null))
            {
                previewController.SetGizmo(false, gizmoMode, Vector3.zero, GizmoHandle.None);
                return;
            }

            if (IsRigEditMode)
            {
                RefreshRigEditGizmo(activeRigEditItem);
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

        // Unlike clip authoring there is no track to sample: ResolveDisplayedTransform would return
        // an offset-from-rest value with no relationship to where the node actually sits.
        /// <summary>Rig Edit's gizmo pivot: the selected node's own live preview transform, or a held drag's value.</summary>
        private void RefreshRigEditGizmo(HierarchyItem item)
        {
            Transform node = ResolveHierarchyTransform(item);
            if (node == null)
            {
                previewController.SetGizmo(false, gizmoMode, Vector3.zero, GizmoHandle.None);
                return;
            }

            Vector3 pivot = hasPendingRigPoseEdit
                ? new Vector3(pendingRigPosition.x, pendingRigPosition.y, pendingRigPosition.z)
                : node.localPosition;
            previewController.SetGizmo(true, gizmoMode, pivot, activeGizmoHandle);
        }

        /// <summary>Starts an orbit and casts the pick ray, from the exact position of the press.</summary>
        private void OnPreviewPointerDown(PointerDownEvent pointerEvent)
        {
            // Cast here, not on release, so it uses the position the user aimed at rather than
            // wherever the pointer drifted to before the button came up. Applied only on release,
            // and only if this turned out to be a click rather than an orbit.
            isPickPending = false;

            // Double click reframes. With the camera persisting across every selection change, an
            // orbit that wandered off the rig would otherwise have no way back. Left button only:
            // the other two are camera gestures whose second press is not a second click at all.
            if (pointerEvent.clickCount >= 2 && pointerEvent.button == 0)
            {
                ResetViewportCamera();
                return;
            }
            previewImage.CapturePointer(pointerEvent.pointerId);
            previewImage.Focus();

            if (previewController == null)
            {
                return;
            }

            // Before the handles and before the pick: a camera gesture is exclusive, and Alt + left
            // in particular must orbit rather than grab whichever gizmo handle it started over.
            if (TryBeginCameraGesture(pointerEvent))
            {
                return;
            }

            // A press on a ragdoll box handle or an ordinary gizmo handle is a drag, not an orbit
            // and not a selection. Tested first for exactly that reason: a handle sits on top of
            // the thing it edits, so any other order would make it unusable. Ragdoll first, since a
            // selected body's grab handles can be on screen at the same time as an ordinary gizmo.
            if (TryBeginRagdollBoxDrag(pointerEvent.localPosition))
            {
                return;
            }
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

            if (cameraNavigation.ActiveGesture != PreviewCameraNavigation.Gesture.None)
            {
                ContinueCameraGesture(moveEvent.deltaPosition);
                return;
            }
            if (activeRagdollBoxHandle != RagdollBoxHandle.None)
            {
                ContinueRagdollBoxDrag(moveEvent.localPosition, moveEvent.shiftKey);
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
            HierarchyItem activeRigEditItem = (!draggingSocket && IsRigEditMode) ? ActiveHierarchyItem : null;
            if (!draggingSocket
                && !GizmoDragRouting.ShouldShowTransformGizmo(
                    IsRigEditMode, activeRigEditItem != null, selectedTargetId != 0u, selectedClip != null))
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
            else if (IsRigEditMode)
            {
                // No track to seed from -- the drag starts from the node's own live pose, not a
                // clip-relative offset. See RefreshRigEditGizmo for why sampling the clip here would
                // be wrong.
                Transform node = ResolveHierarchyTransform(activeRigEditItem);
                if (node == null)
                {
                    return false;
                }
                Vector3 nodeEuler = node.localEulerAngles;
                position = new float3(node.localPosition.x, node.localPosition.y, node.localPosition.z);
                rotationDegrees = new float3(nodeEuler.x, nodeEuler.y, nodeEuler.z);
                scale = new float3(node.localScale.x, node.localScale.y, node.localScale.z);
            }
            else
            {
                ResolveDisplayedTransform(
                    selectedTargetId, out position, out rotationDegrees, out scale);
            }

            // One step for the whole drag, opened before the first pointer move writes anything —
            // the same rule the release path follows for a key. Only for a drag that will hold a
            // clip value: a socket or Rig Edit drag records its own object when it commits, and a
            // step here would be a "Move Part" that moved nothing.
            if (selectedSocketId == 0u && !IsRigEditMode)
            {
                RecordHeldTransformEdit("Move Part");
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

        /// <summary>Turns pointer motion into a transform value and writes it through the shared path.</summary>
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

        /// <summary>Sends a drag's value wherever the current selection says it belongs.</summary>
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
            if (IsRigEditMode)
            {
                pendingRigPosition = position;
                pendingRigRotationDegrees = rotationDegrees;
                pendingRigScale = scale;
                hasPendingRigPoseEdit = true;
                PreviewRigNodeDrag(position, rotationDegrees, scale);
                return;
            }
            ApplyTransformEdit(selectedTargetId, position, rotationDegrees, scale, false);
        }

        private bool hasPendingSocketEdit;
        private float3 pendingSocketPosition;
        private float3 pendingSocketRotation;

        /// <summary>Moves the selected node live during a Rig Edit drag, without touching the asset.</summary>
        private void PreviewRigNodeDrag(float3 position, float3 rotationDegrees, float3 scale)
        {
            Transform node = ResolveHierarchyTransform(ActiveHierarchyItem);
            if (node == null)
            {
                return;
            }
            node.localPosition = new Vector3(position.x, position.y, position.z);
            node.localRotation = Quaternion.Euler(rotationDegrees.x, rotationDegrees.y, rotationDegrees.z);
            node.localScale = new Vector3(scale.x, scale.y, scale.z);
        }

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

        /// <summary>Writes a finished socket drag back as an offset in the followed thing's space.</summary>
        private void CommitSocketDrag()
        {
            SocketDefinition socket = FindSocket(selectedSocketId);
            RigAsset rig = ActiveRig;
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

            // The gizmo works in the mirror root's space; a socket stores its offset in the
            // followed part's space, so the followed pose has to be divided back out here.
            Quaternion inverseBase = Quaternion.Inverse(baseRotation);
            Undo.RecordObject(rig, "Place Socket");
            socket.localPosition = inverseBase * (draggedPosition - basePosition);
            socket.localEulerAngles = (inverseBase * draggedRotation).eulerAngles;
            CommitSocketEdit(true);
            RebuildInspector();
        }

        /// <summary>Ends a gizmo drag, keying the result when auto-key asked for it.</summary>
        private void EndGizmoDrag()
        {
            if (activeGizmoHandle == GizmoHandle.None)
            {
                return;
            }
            activeGizmoHandle = GizmoHandle.None;

            // The fork the whole mode exists for. The rule itself lives in GizmoDragRouting so it
            // can be read — and tested — as a table, rather than reconstructed from these branches.
            // Rig Edit reads its own held-edit flag: a Rig Edit drag never goes through
            // ApplyTransformEdit (see ApplyGizmoDragValue), so hasPendingTransformEdit stays false
            // for it and would make Resolve report Nothing regardless of the drag that just happened.
            bool hasPendingEdit = IsRigEditMode ? hasPendingRigPoseEdit : hasPendingTransformEdit;
            GizmoDragDestination destination = GizmoDragRouting.Resolve(
                selectedSocketId != 0u, false, IsRigEditMode, IsAutoKeyEnabled, hasPendingEdit);

            switch (destination)
            {
                case GizmoDragDestination.SocketOffset:
                    CommitSocketDrag();
                    RefreshGizmo();
                    return;

                case GizmoDragDestination.RigBasePose:
                    CommitRigBaseEdit(pendingRigPosition, pendingRigRotationDegrees, pendingRigScale);
                    hasPendingRigPoseEdit = false;
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

        // Selecting on press would mean every orbit also reselected whatever the camera happened
        // to start over; committing on release, within a few pixels, lets one button do both.
        /// <summary>Ends the orbit and, if the pointer never really moved, applies the pick.</summary>
        private void OnPreviewPointerUp(PointerUpEvent upEvent)
        {
            previewImage.ReleasePointer(upEvent.pointerId);

            if (cameraNavigation.ActiveGesture != PreviewCameraNavigation.Gesture.None)
            {
                EndCameraGesture();
                return;
            }

            if (activeRagdollBoxHandle != RagdollBoxHandle.None)
            {
                EndRagdollBoxDrag();
                return;
            }

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

        // Cycle advances only when the click lands on the same set of candidates again; anything
        // else resets to the nearest, or a click somewhere new would resume at a stale ordinal.
        /// <summary>Selects whichever of the press's hits is current, cycling on a modified click.</summary>
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

        // Routed through SetSelectionById so it fires the tree's own selection-changed handler:
        // a viewport click and a tree click run the same code and cannot mean different things.
        /// <summary>Selects a transform of the previewed rig by driving the tree, not by bypassing it.</summary>
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
            // The row selected is the socket's source, and the gizmo goes on the socket itself:
            // that pair is what "I clicked the sword" has to mean now that a socket is a component
            // of the thing it follows rather than a row of its own.
            if (previewController.TryGetSocketIdForTransform(pickedTransform, out pickedSocketId))
            {
                if (!TryFindSocketSourceItemId(pickedSocketId, out itemId))
                {
                    return;
                }
                hierarchyTreeView.SetSelectionById(itemId);
                hierarchyTreeView.ScrollToItemById(itemId);
                FocusSocket(pickedSocketId);
                RebuildInspector();
                return;
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
            cameraNavigation.HandleWheel(wheelEvent);
        }

        // Window state, written to no asset: no data model pairs a rig with a clip set, so this
        // only records what this window plays the set against — no undo step, no actor bake changes.
        /// <summary>Handles a pick in the toolbar's Rig field: records it and refreshes everything downstream.</summary>
        private void OnSkinnedSourceChanged(ChangeEvent<Object> changeEvent)
        {
            activeRig = changeEvent.newValue as RigAsset;

            if (previewController != null)
            {
                previewController.SetRig(activeRig);
                previewController.SetSkinnedSource(LoadedPrefab);
            }
            // Cleared before the tree is rebuilt: the old instance's transforms are gone, so the
            // held index now points into a hierarchy that no longer exists.
            SelectHierarchyItem(NothingSelectedItemId);
            previousPickCandidates.Clear();
            pickCandidates.Clear();
            RebuildHierarchy();
            RebuildTimeline();
            RebuildInspector();
            if (validationBadge != null)
            {
                validationBadge.Refresh(activeRig, clipSet);
            }

            // LoadedPrefab is what Edit Prefab's enabled state depends on, and a pick here is one
            // of the places it changes. Without this the button was disabled at bind time — when
            // no rig is assigned yet — and never re-enabled, so assigning a rig left a button that
            // swallowed clicks in silence. OnClipSetChanged carries the same refresh now, for the
            // other place LoadedPrefab can change.
            RefreshPrefabActionState();
            RefreshOpenPaneSource();
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

        /// <summary>Rebuilds the hierarchy from the assigned rigged prefab.</summary>
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
                hierarchyEmptyLabel.text = ResolveHierarchyEmptyMessage();
                hierarchyEmptyLabel.EnableInClassList(HiddenUssClassName, rootItems.Count > 0);
            }
        }

        /// <summary>What the empty-hierarchy hint should say, given why it is empty.</summary>
        private string ResolveHierarchyEmptyMessage()
        {
            if (clipSet == null)
            {
                return "Assign a clip set.";
            }
            if (ActiveRig == null)
            {
                return "Assign a rig to the toolbar's Rig field.";
            }
            if (ActiveRig.sourcePrefab == null)
            {
                return "Rig \"" + ActiveRig.name + "\" has no Source Prefab assigned yet. Open "
                    + "the rig asset and assign one to preview and author bone tracks.";
            }
            return "This rig's source prefab has no child transforms to show.";
        }

        // The id is the preview's own index for that transform, not a counter kept here — two
        // independent walks would agree only as long as nobody changed one of them.
        /// <summary>Builds one tree item, taking its id from the preview.</summary>
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
                previewIndex = itemId,
                targetId = ResolveNodeTargetId(transformNode)
            };
            hierarchyItemsById[itemId] = item;
            return new TreeViewItemData<HierarchyItem>(itemId, item, childItems);
        }

        /// <summary>The rig part claiming a previewed node, or 0 when none does.</summary>
        private uint ResolveNodeTargetId(Transform transformNode)
        {
            RigAsset rig = ActiveRig;
            Transform root = previewController != null ? previewController.HierarchyRoot : null;
            if (rig == null || root == null || transformNode == null)
            {
                return 0u;
            }
            return ClipComponentModel.ResolveTargetIdForNode(
                rig, PrefabAuthoringBridge.GetHierarchyPath(transformNode, root));
        }

        // A target recording which previewed node it stands for is skipped: that node's own row is
        // where it appears, and two rows for one part could not be told apart.
        /// <summary>One row per rig target that has no node of its own, flat.</summary>
        private List<TreeViewItemData<HierarchyItem>> BuildRigTargetItems()
        {
            List<TreeViewItemData<HierarchyItem>> targetItems =
                new List<TreeViewItemData<HierarchyItem>>();
            RigAsset rig = ActiveRig;
            if (rig == null || rig.targets == null)
            {
                return targetItems;
            }

            Transform hierarchyRoot = previewController != null
                ? previewController.HierarchyRoot
                : null;

            for (int targetIndex = 0; targetIndex < rig.targets.Count; targetIndex++)
            {
                RigTargetDefinition target = rig.targets[targetIndex];
                if (target == null)
                {
                    continue;
                }
                if (hierarchyRoot != null && !string.IsNullOrEmpty(target.sourceNodePath)
                    && PrefabAuthoringBridge.ResolveByPath(
                        hierarchyRoot, target.sourceNodePath) != null)
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

        /// <summary>A socket's one-line label: its name, what it follows, and a mark when that resolves to nothing.</summary>
        private string DescribeSocketLabel(SocketDefinition socket)
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
            RigAsset rig = ActiveRig;
            if (rig == null || rig.sockets == null)
            {
                return null;
            }
            for (int socketIndex = 0; socketIndex < rig.sockets.Count; socketIndex++)
            {
                SocketDefinition socket = rig.sockets[socketIndex];
                if (socket != null && socket.Id.Value == socketId)
                {
                    return socket;
                }
            }
            return null;
        }

        private int FindSocketIndex(uint socketId)
        {
            RigAsset rig = ActiveRig;
            if (rig == null || rig.sockets == null)
            {
                return -1;
            }
            for (int socketIndex = 0; socketIndex < rig.sockets.Count; socketIndex++)
            {
                SocketDefinition socket = rig.sockets[socketIndex];
                if (socket != null && socket.Id.Value == socketId)
                {
                    return socketIndex;
                }
            }
            return -1;
        }

        // Manipulator and double-click callback are attached once, reading the row's current item
        // through a field the bind step refreshes — rows are recycled, so binding per-item would
        // stack a new handler on the same element every time it scrolled back into view.
        /// <summary>One hierarchy row, wired for the two gestures that reach prefab mode.</summary>
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

        // Built on DragAndDrop and UI Toolkit's drag events, not the TreeView's own drag hooks
        // (not public in this Unity version) — the built-in reorderable flag would reorder the
        // view and leave the prefab untouched, which is the parallel hierarchy this must not become.
        /// <summary>Wires one row for drag-to-reparent, which only does anything in Rig Edit mode.</summary>
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

        /// <summary>Moves a dragged object under the row it was dropped on, in the prefab asset.</summary>
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

            hasWrittenPrefabPose = true;
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
            // Either binding counts. A claimed node can be animated as a part and still carry a
            // bone track left over from before it was one, and a row that went un-bolded because
            // the wrong half was checked would say this clip does not touch it.
            bool isAnimated = item.targetId != 0u && CountTracksForTarget(item.targetId) > 0;
            if (!isAnimated && item.kind != HierarchyItemKind.RigTarget)
            {
                isAnimated = FindBoneTrackIndex(item.displayName) >= 0;
            }
            label.EnableInClassList(AnimatedBoneUssClassName, isAnimated);
            ApplyBillboardIndicator(label, item);
        }

        /// <summary>The rig target with this id, or null.</summary>
        private RigTargetDefinition FindRigTargetById(uint targetId)
        {
            RigAsset rig = ActiveRig;
            if (rig == null || rig.targets == null)
            {
                return null;
            }
            for (int targetIndex = 0; targetIndex < rig.targets.Count; targetIndex++)
            {
                RigTargetDefinition target = rig.targets[targetIndex];
                if (target != null && target.Id.Value == targetId)
                {
                    return target;
                }
            }
            return null;
        }

        // Three states, not two: "this node billboards" and "this node decides how it billboards"
        // are different facts, and a fully billboarded node's animated rotation is replaced outright
        // at resolve time, so keying it changes nothing visible without a marker to explain why.
        /// <summary>Marks a row as a billboard root, as inheriting one, or as neither.</summary>
        private void ApplyBillboardIndicator(HierarchyRowLabel label, HierarchyItem item)
        {
            label.EnableInClassList(BillboardRootUssClassName, false);
            label.EnableInClassList(BillboardInheritedUssClassName, false);
            label.tooltip = string.Empty;

            RigAsset rig = ActiveRig;
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

        /// <summary>The preview transform a hierarchy row stands for, or null when it stands for none.</summary>
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
                default:
                    return previewController.GetTransformByIndex(item.previewIndex);
            }
        }

        // Sockets have no rows of their own; a socket whose source resolves to nothing has no row
        // to offer, which is what the clip inspector's socket list exists to catch.
        /// <summary>The hierarchy row a socket hangs off: the part or bone it follows.</summary>
        private bool TryFindSocketSourceItemId(uint socketId, out int itemId)
        {
            itemId = NothingSelectedItemId;
            SocketDefinition socket = FindSocket(socketId);
            if (socket == null)
            {
                return false;
            }

            if (socket.mode == SocketAttachMode.RigTarget)
            {
                return TryFindRigTargetItemId(socket.targetId, out itemId);
            }

            foreach (KeyValuePair<int, HierarchyItem> pair in hierarchyItemsById)
            {
                if (pair.Value.kind == HierarchyItemKind.RigTarget)
                {
                    continue;
                }
                if (string.Equals(
                        pair.Value.displayName, socket.boneName, System.StringComparison.Ordinal))
                {
                    itemId = pair.Key;
                    return true;
                }
            }
            return false;
        }

        /// <summary>The row standing for a part, whichever pane it came from.</summary>
        private bool TryFindRigTargetItemId(uint targetId, out int itemId)
        {
            foreach (KeyValuePair<int, HierarchyItem> pair in hierarchyItemsById)
            {
                if (targetId != 0u && pair.Value.targetId == targetId)
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

        /// <summary>The single place a hierarchy selection takes effect, whichever surface caused it.</summary>
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

        }

        // Active is the row just added, found by diffing against the previous selection rather than
        // taking the last of selectedIndices, which is ordered by row position, not click order.
        /// <summary>Adopts the tree's whole selection and works out which row of it is active.</summary>
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

        // Compared by resolved item, not only by id: a rebuilt hierarchy can hand out the same ids
        // for freshly constructed items, and treating that as an echo would leave
        // selectedHierarchyItems holding detached objects the inspector would then edit.
        /// <summary>Whether the tree is reporting the selection that has already been applied.</summary>
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

        // RefreshItems re-resolves the tree's selection while redrawing it, which raises
        // selectionChanged — every call here is only a redraw and must not reach the selection handler.
        /// <summary>Repaints the tree's rows without letting the repaint pose as a selection change.</summary>
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

                // A socket is reached through the object carrying it, so selecting nothing leaves
                // the gizmo nothing to be on.
                selectedSocketId = 0u;
                if (previewController != null)
                {
                    previewController.SetSelectedHierarchyIndex(-1);
                    previewController.SetSelectedSocketId(0u);
                }
                return;
            }

            selectedHierarchyItemId = FindItemIdOf(activeItem);
            // Selecting an object drops whichever socket the gizmo was on, unless that socket is
            // one of this object's own components — the gizmo has to be on something the selection
            // can still see.
            if (!SocketBelongsToItem(selectedSocketId, activeItem))
            {
                selectedSocketId = 0u;
            }
            if (previewController != null)
            {
                previewController.SetSelectedSocketId(selectedSocketId);
            }
            // targetId is set on a RigTarget row always, and on a PrefabTransform row whenever a
            // part claims that node — a claimed part is as much a clip-authoring target as a rig
            // target row is (see HierarchyItem.targetId), so the gizmo/drag key on it either way.
            selectedTargetId = activeItem.targetId;
            if (activeItem.kind == HierarchyItemKind.RigTarget)
            {
                selectedBoneName = null;
                if (previewController != null)
                {
                    previewController.SetSelectedTargetId(activeItem.targetId);
                }
                return;
            }

            selectedBoneName = activeItem.displayName;
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
            if (targetId == 0u)
            {
                return false;
            }
            for (int itemIndex = 0; itemIndex < selectedHierarchyItems.Count; itemIndex++)
            {
                if (selectedHierarchyItems[itemIndex].targetId == targetId)
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

        /// <summary>Names what the timeline is focused on, for the status line.</summary>
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

            // The Rig field is deliberately left alone. A clip set names no rig and a rig names no
            // clips — they are independent assets, paired only where an ActorAuthoring states both —
            // so swapping the open set must not swap the rig underneath it, any more than swapping
            // the rig should empty the clip list. Playing this set against the rig already loaded is
            // the whole point of the window: the tags line up, or they are reported as not lining up.
            RefreshClipList();
            RefreshClipActionButtons();

            // The hierarchy's rows come from the rig, which has not changed — but which of them a
            // clip already animates is drawn from the set, so the rows are re-rendered rather than
            // left showing the previous set's bold.
            SelectHierarchyItem(NothingSelectedItemId);
            RebuildHierarchy();

            if (previewController != null)
            {
                previewController.SetClipSet(clipSet);
            }
            if (validationBadge != null)
            {
                validationBadge.Refresh(activeRig, clipSet);
            }
            RefreshOpenPaneSource();
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
            if (playing && !isPlaying)
            {
                prePlayPlayheadTime = playheadTime;
            }
            isPlaying = playing;
            lastTickTime = EditorApplication.timeSinceStartup;
            RefreshTransportCoreState();
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

            // Before the pose is advanced: a deferred rebuild is holding stale panes, and scrubbing
            // over them would refresh values into fields that are about to be replaced.
            FlushDeferredPaneRebuilds();

            if (isPlaying && selectedClip != null)
            {
                // Advance in seconds then convert, so a clip's duration sets playback speed exactly
                // the way it does at runtime rather than every clip taking the same wall time.
                float duration = Mathf.Max(ClipAsset.MinimumDuration, selectedClip.duration);
                float speed = playbackSpeedField != null ? playbackSpeedField.value : 1f;
                float advanced = playheadTime + (float)elapsed * speed / duration;

                if (isLoopEnabled)
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

            // Before the render, so a held fly key has moved the camera by the time this frame is
            // drawn. Integrated against the real elapsed time rather than counted in key repeats —
            // see ClipEditorWindow.CameraNavigation.
            StepCameraFly((float)elapsed);

            // The preview updates every tick, not only while playing — scrubbing a paused clip is
            // the authoring loop this window exists for.
            //
            // But only on this tab. The 2D Direction Sets pane drives the same controller into its
            // own viewport, and one PreviewRenderUtility cannot serve two of them in a frame: both
            // would sample and render, and each would show whatever the other posed last. Which one
            // won would depend on tick order, so it would read as the direction viewer flickering
            // rather than as two writers.
            if (activeTab == ClipEditorTab.ClipEditor)
            {
                UpdatePreview(now);
            }
        }

        /// <summary>Marks the preview's registry stale; the tick rebuilds it after a short delay.</summary>
        private void MarkPreviewDirty()
        {
            previewRegistryDirty = true;
            previewDirtiedAt = EditorApplication.timeSinceStartup;
        }

        // Every early exit below is about the pose, not the picture: with no clip, no registry, or
        // a clip the registry lacks, the mirror is simply not re-posed — the render still runs and
        // returns the scene, rather than leaving the window looking dead.
        /// <summary>Advances the viewport one frame.</summary>
        private void UpdatePreview(double now)
        {
            if (previewController == null || previewImage == null)
            {
                return;
            }

            // Trailing edge OR a max wait. The trailing edge alone never fired during a drag —
            // MarkPreviewDirty re-stamps previewDirtiedAt faster than the settle time elapses — so
            // the viewport stood still until the drag ended. The settle time still collapses a
            // finished gesture into one last rebuild.
            if (previewRegistryDirty
                && (now - previewDirtiedAt > PreviewSettleSeconds
                    || now - previewLastRefreshedAt > PreviewMaxWaitSeconds))
            {
                previewRegistryDirty = false;
                previewLastRefreshedAt = now;
                previewController.Refresh();

                // Revalidated on the same debounced beat as the preview rebuild, for the same
                // reason: a full set validation walks every key of every clip, so running it per
                // repaint would make a large set's window crawl.
                if (validationBadge != null)
                {
                    validationBadge.Refresh(activeRig, clipSet);
                }
            }

            string viewportStatus = previewController.StatusMessage;

            // A ragdoll has no timeline: the playhead is frozen while it runs, meaning the clip is
            // not re-sampled at all — the ragdoll step still writes the mirrors' transforms, in Render.
            if (previewController.RagdollPreviewEnabled)
            {
                // Falls through to the render below unconditionally; the ragdoll step happens there.
            }
            else if (selectedClip != null
                && !previewController.SamplePose(selectedClip.Id.Value, playheadTime))
            {
                // Not gated on HasRegistry: SamplePose poses whatever the clip actually carries, and
                // bone tracks need no registry at all. Gating the call meant a skinned rig with no
                // cutout targets never sampled, so its timeline scrubbed while the rig stood still.
                // False now means "posed nothing", which is only worth explaining when a registry
                // exists and the clip is missing from it.
                if (previewController.HasRegistry)
                {
                    viewportStatus = "Clip is not in the built registry — is it listed in the set?";
                }
            }
            else if (selectedClip == null && clipSet != null && string.IsNullOrEmpty(viewportStatus))
            {
                // Only when the controller has nothing of its own to say: a rig with no targets or a
                // set that failed to build is the more useful message, and this must not bury it.
                viewportStatus = "Select a clip to pose the rig.";
            }

            // The held, unkeyed edit is in no registry — the registry is built from committed keys —
            // so without this a drag with auto-key off moved the numbers and nothing else. Applied
            // after the sample for the same reason ResolveDisplayedTransform prefers it in the
            // fields: the held value is what the author is currently looking at.
            if (hasPendingTransformEdit
                && !previewController.RagdollPreviewEnabled
                && previewController.HasRegistry)
            {
                previewController.ApplyHeldTargetPose(
                    pendingTransformTargetId, pendingPosition, pendingRotationDegrees, pendingScale);
            }

            if (previewStatusLabel != null)
            {
                previewStatusLabel.text = viewportStatus;

                // Collapsed when there is nothing to say, rather than left as an empty line. It sits
                // directly above the preview and takes a line's height whatever its text, so on a
                // healthy set that is a strip of dead space between the pane's top and the rig — and
                // the whole point of what changed around it is that the viewport keeps its room.
                previewStatusLabel.EnableInClassList(
                    HiddenUssClassName, string.IsNullOrEmpty(viewportStatus));
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
            bool timeIsActuallyMoving = !Mathf.Approximately(clampedTime, playheadTime);

            // A held edit describes the part at one instant, so moving off that instant ends it.
            // Carrying it along would silently apply a value the user never keyed to a time they
            // never looked at.
            if (hasPendingTransformEdit && timeIsActuallyMoving)
            {
                DiscardPendingTransformEdit();

                // Requested: every scrubbing gesture in the window reaches here — a key drag, the
                // ruler, the transport's own Frame and Time captions — and the pane must not be
                // torn down while one of them holds the pointer.
                RequestInspectorRebuild();
            }

            // Scrubbing while ragdoll preview is on turns it off first: a ragdoll has no timeline,
            // and Play advances time through this same setter every tick, so the rule catches both.
            if (timeIsActuallyMoving && previewController != null && previewController.RagdollPreviewEnabled)
            {
                previewController.DisableRagdollPreview();
                if (ragdollPreviewToggle != null)
                {
                    ragdollPreviewToggle.SetValueWithoutNotify(false);
                }
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
                timelineRowCount = 0;
                SyncGhostLanes();
                RebuildInspector();
                return;
            }

            // Focus mode: with a selection, the timeline shows only that selection's tracks. It is
            // what makes a busy clip readable — but a row that has silently vanished is worse than a
            // busy timeline, so the status line always says what is being hidden and how to undo it.
            bool isFocused = selectedHierarchyItems.Count > 0;
            int hiddenTrackCount = 0;

            // A track with no keys writes nothing at any time, so it is not a curve yet — it is a
            // component waiting for its first key, and the place to make that key is the part's own
            // Key button in the inspector. Counted, because "I added a Transform and no row
            // appeared" needs an answer on screen rather than in the source.
            int keylessTrackCount = 0;

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
                if (track.keys == null || track.keys.Count == 0)
                {
                    keylessTrackCount++;
                    continue;
                }
                TrackBindingLabel binding = DescribeTrackBinding(track.targetId, track.tagId);
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

            List<SpriteTrack> spriteTracks = selectedClip.spriteTracks;
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
                TrackBindingLabel binding = DescribeTrackBinding(track.targetId, track.tagId);
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
            List<BoneTrack> boneTracks = selectedClip.boneTracks;
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

            if (selectedClip.events != null && selectedClip.events.Count > 0)
            {
                // One lane per event name (E6 Task 2), not one shared lane with stacking: three
                // events on one frame land on three rows rather than piling under one. Events stay
                // visible while focused — they belong to the clip rather than to any one part, so
                // hiding them would make event authoring impossible the moment anything was selected.
                AnimEventKeyRegistry eventRegistry = ResolveEventKeyRegistry();
                List<uint> eventLaneKeys = EventLaneAddressing.ComputeLaneKeys(selectedClip.events);
                for (int laneIndex = 0; laneIndex < eventLaneKeys.Count; laneIndex++)
                {
                    List<int> laneFlatIndices = EventLaneAddressing.ResolveLaneFlatIndices(
                        selectedClip.events, laneIndex);
                    times.Clear();
                    for (int position = 0; position < laneFlatIndices.Count; position++)
                    {
                        times.Add(selectedClip.events[laneFlatIndices[position]].normalizedTime);
                    }
                    AddTrackRow(
                        DescribeEventName(eventLaneKeys[laneIndex], eventRegistry),
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

            SetPlayheadTime(playheadTime);
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
            if (selectedClip == null || selectedClip.events == null)
            {
                return eventWindowLengths;
            }

            List<int> laneFlatIndices =
                EventLaneAddressing.ResolveLaneFlatIndices(selectedClip.events, laneIndex);
            float duration = Mathf.Max(selectedClip.duration, ClipAsset.MinimumDuration);
            for (int position = 0; position < laneFlatIndices.Count; position++)
            {
                eventWindowLengths.Add(
                    selectedClip.events[laneFlatIndices[position]].windowSeconds / duration);
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
                isKeySelected = selectedKeys.Contains,

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

        private void RepaintLanes()
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
        private void RefreshLaneKeys()
        {
            if (laneColumn == null || selectedClip == null)
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

            dragAutoScroll = rootVisualElement.schedule
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

            dragPointerLaneX = moveEvent.localPosition.x;
            UpdateKeyDrag();
        }

        /// <summary>Moves the selection to follow the pointer, using the view as it is right now.</summary>
        private void UpdateKeyDrag()
        {
            if (!isDraggingKeys || selectedClip == null)
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
            foreach (KeyAddress address in selectedKeys)
            {
                SetKeyTime(address, GetKeyTime(address) + delta);
            }
            dragPreviousTime = pointerTime;

            EditorUtility.SetDirty(selectedClip);
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
                + "   " + (normalizedTime * selectedClip.duration).ToString("0.###") + "s"
                + "   " + selectedKeys.Count.ToString() + " key(s)" + range;
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

            EditorUtility.SetDirty(selectedClip);
            selectedKeys.Clear();
            hasActiveKey = false;
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
                selectedKeys.Clear();
                hasActiveKey = false;
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
            gestureUndoName = actionName;
            Undo.SetCurrentGroupName(actionName);
            Undo.RecordObject(selectedClip, actionName);
        }

        // Undo.RecordObject snapshots once per frame; a multi-frame drag mutating every frame after
        // the first needs a fresh recording each step, or those changes are never captured at all.
        /// <summary>Re-records the clip before another step of an in-progress gesture.</summary>
        private void RecordUndoGestureStep()
        {
            if (selectedClip == null)
            {
                return;
            }
            Undo.RecordObject(
                selectedClip,
                string.IsNullOrEmpty(gestureUndoName) ? "Edit Animation Keys" : gestureUndoName);
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

            if (selectedClip == null)
            {
                return;
            }

            bool commandModifier = keyEvent.ctrlKey || keyEvent.commandKey;

            switch (keyEvent.keyCode)
            {
                case KeyCode.Space:
                    ((ITransportTarget)this).TogglePlay();
                    break;
                case KeyCode.Delete:
                case KeyCode.Backspace:
                    DeleteSelectedKeys();
                    break;
                case KeyCode.Home:
                    ((ITransportTarget)this).JumpToStart();
                    break;
                case KeyCode.End:
                    ((ITransportTarget)this).JumpToEnd();
                    break;
                case KeyCode.LeftArrow:
                    ((ITransportTarget)this).Step(keyEvent.shiftKey ? -Mathf.Max(1, LargeStepFrames) : -1);
                    break;
                case KeyCode.RightArrow:
                    ((ITransportTarget)this).Step(keyEvent.shiftKey ? Mathf.Max(1, LargeStepFrames) : 1);
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
        private void CopySelectedKeys()
        {
            ClipKeyClipboard.Copy(selectedClip, selectedKeys);
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
        private void PasteKeysAtPlayhead()
        {
            if (!ClipKeyClipboard.HasContent || selectedClip == null)
            {
                return;
            }

            pasteDestinations.Clear();
            for (int itemIndex = 0; itemIndex < selectedHierarchyItems.Count; itemIndex++)
            {
                pasteDestinations.Add(BuildObjectRef(selectedHierarchyItems[itemIndex]));
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
                ClipKeyClipboard.Paste(selectedClip, rig, pasteDestinations, playheadTime);

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
                EditorUtility.SetDirty(selectedClip);
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
        private void DeleteSelectedKeys()
        {
            if (selectedKeys.Count == 0)
            {
                return;
            }

            KeyAddress[] ordered = new List<KeyAddress>(selectedKeys).ToArray();
            int[] removalIndex = new int[ordered.Length];
            for (int index = 0; index < ordered.Length; index++)
            {
                removalIndex[index] = ordered[index].trackKind == TimelineTrackKind.Event
                    ? EventLaneAddressing.ResolveFlatIndex(
                        selectedClip.events, ordered[index].trackIndex, ordered[index].keyIndex)
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
                        if (address.trackIndex < selectedClip.transformTracks.Count
                            && flatOrLocalIndex < selectedClip.transformTracks[address.trackIndex].keys.Count)
                        {
                            selectedClip.transformTracks[address.trackIndex].keys.RemoveAt(flatOrLocalIndex);
                        }
                        break;
                    case TimelineTrackKind.Sprite:
                        if (address.trackIndex < selectedClip.spriteTracks.Count
                            && flatOrLocalIndex < selectedClip.spriteTracks[address.trackIndex].keys.Count)
                        {
                            selectedClip.spriteTracks[address.trackIndex].keys.RemoveAt(flatOrLocalIndex);
                        }
                        break;
                    case TimelineTrackKind.Bone:
                        if (selectedClip.boneTracks != null
                            && address.trackIndex < selectedClip.boneTracks.Count
                            && flatOrLocalIndex < selectedClip.boneTracks[address.trackIndex].keys.Count)
                        {
                            selectedClip.boneTracks[address.trackIndex].keys.RemoveAt(flatOrLocalIndex);
                        }
                        break;
                    default:
                        if (flatOrLocalIndex >= 0 && flatOrLocalIndex < selectedClip.events.Count)
                        {
                            selectedClip.events.RemoveAt(flatOrLocalIndex);
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
                {
                    int flatIndex = ResolveEventFlatIndex(address);
                    return flatIndex >= 0 ? selectedClip.events[flatIndex].normalizedTime : 0f;
                }
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
                    int flatIndex = ResolveEventFlatIndex(address);
                    if (flatIndex < 0)
                    {
                        break;
                    }
                    EventMarker marker = selectedClip.events[flatIndex];
                    marker.normalizedTime = normalizedTime;
                    selectedClip.events[flatIndex] = marker;
                    break;
                }
            }
        }

        /// <summary>The flat <see cref="selectedClip"/>.events position one event address points to.</summary>
        private int ResolveEventFlatIndex(KeyAddress address)
        {
            return EventLaneAddressing.ResolveFlatIndex(
                selectedClip.events, address.trackIndex, address.keyIndex);
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
                    // trackIndex addresses an existing event lane — double-clicking the "Footstep"
                    // lane adds another Footstep. A negative trackIndex (the transport bar's Add
                    // Event button) has no lane to read, so it carries the key the picker already
                    // chose instead of guessing one. Never key 0: that struct default is the
                    // reserved "invalid" key, which used to fail validation at bake time.
                    List<uint> laneKeys = EventLaneAddressing.ComputeLaneKeys(selectedClip.events);
                    uint eventKey = trackIndex >= 0 && trackIndex < laneKeys.Count
                        ? laneKeys[trackIndex]
                        : explicitEventKey;
                    selectedClip.events.Add(new EventMarker
                    {
                        normalizedTime = normalizedTime,
                        eventKey = eventKey,
                        windowSeconds = ResolveDefaultWindowSecondsForKey(eventKey)
                    });
                    break;
                }
            }
        }

        /// <summary>The Add Event button on the transport bar: opens the event picker anchored to it.</summary>
        private void OpenAddEventPicker()
        {
            if (selectedClip == null)
            {
                return;
            }

            AnimEventKeyRegistry registry = ResolveEventKeyRegistry();
            VocabularyPicker.Open(
                rootVisualElement,
                addEventButton,
                registry,
                registry,
                VocabularyPickerConfig.ForEventKeys(registry),
                chosenEventKey => AddEventAtPlayhead(chosenEventKey),
                // A Create… mint or an Edit… rename changes what a lane header should read, not
                // just the marker inspector — the timeline has to rebuild, not just RebuildInspector.
                RebuildTimeline);
        }

        // Selects the new marker rather than clearing selection, unlike a double-click add: a
        // toolbar button gives the author no on-screen cue to where the marker landed.
        /// <summary>Places a marker for <paramref name="eventKey"/> at the playhead and selects it.</summary>
        private void AddEventAtPlayhead(uint eventKey)
        {
            if (selectedClip == null)
            {
                return;
            }

            float insertTime = TimelineGeometry.Snap(playheadTime, SnapFrameCount);
            BeginUndoGesture("Add Event");

            // -1: this button targets no particular lane, unlike a double-click inside one, so
            // InsertKey carries the key the picker already chose instead of reading laneKeys[-1].
            InsertKey(TimelineTrackKind.Event, -1, insertTime, eventKey);
            EndUndoGesture();

            EditorUtility.SetDirty(selectedClip);

            // Select the marker just added, before the sort below can move it — SortTrackKeys
            // remaps whatever is selected through the sort's index map, so selecting first and
            // sorting after is what lets the selection follow the marker to wherever it lands
            // rather than pointing at whatever key ends up in its old slot.
            int newFlatIndex = selectedClip.events.Count - 1;
            KeyAddress newAddress = ResolveEventKeyAddressForFlatIndex(newFlatIndex);
            selectedKeys.Clear();
            selectedKeys.Add(newAddress);
            activeKey = newAddress;
            hasActiveKey = true;

            SortTrackKeys(TimelineTrackKind.Event, newAddress.trackIndex);
            SetPlayheadTime(insertTime);
            RebuildTimeline();
        }

        /// <summary>That event's default window, if it has one.</summary>
        private float ResolveDefaultWindowSecondsForKey(uint eventKey)
        {
            AnimEventKeyRegistry registry = ResolveEventKeyRegistry();
            AnimEventKeyEntry entry = FindRegistryEntryByKey(registry, eventKey);
            if (entry == null || entry.defaultWindowFrames <= 0)
            {
                return 0f;
            }
            return entry.defaultWindowFrames / ResolveReferenceFrameRate(registry);
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
            if (selectedClip == null)
            {
                return;
            }
            List<uint> laneKeys = EventLaneAddressing.ComputeLaneKeys(selectedClip.events);
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
            if (selectedClip == null)
            {
                return;
            }
            AnimEventKeyRegistry registry = ResolveEventKeyRegistry();
            VocabularyPicker.Open(
                rootVisualElement,
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
            if (selectedClip == null || selectedClip.events == null)
            {
                return;
            }
            List<int> flatIndices = EventLaneAddressing.ResolveLaneFlatIndices(selectedClip.events, laneIndex);
            if (flatIndices.Count == 0)
            {
                return;
            }

            RecordClipEdit("Change Event");
            for (int position = 0; position < flatIndices.Count; position++)
            {
                EventMarker marker = selectedClip.events[flatIndices[position]];
                marker.eventKey = chosenEventKey;
                selectedClip.events[flatIndices[position]] = marker;
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
            if (selectedClip == null || selectedClip.events == null)
            {
                return;
            }
            List<int> flatIndices = EventLaneAddressing.ResolveLaneFlatIndices(selectedClip.events, laneIndex);
            if (flatIndices.Count == 0)
            {
                return;
            }

            List<uint> laneKeys = EventLaneAddressing.ComputeLaneKeys(selectedClip.events);
            string laneLabel = laneIndex < laneKeys.Count
                ? DescribeEventName(laneKeys[laneIndex], ResolveEventKeyRegistry())
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
                selectedClip.events.RemoveAt(flatIndices[position]);
            }
            CommitClipEdit();

            selectedKeys.Clear();
            hasActiveKey = false;
            RebuildTimeline();
        }

        // The sampler's segment search assumes ascending times; an out-of-order key does not throw,
        // it silently makes a segment unreachable. Dragging a key past a neighbour reorders rather
        // than clamps, so indices change here — the selection is remapped, not cleared, below.
        /// <summary>Restores ascending key order after an edit, and moves the selection with the keys.</summary>
        private void SortTrackKeys(TimelineTrackKind trackKind, int trackIndex)
        {
            int[] newIndexOfOldIndex;
            switch (trackKind)
            {
                case TimelineTrackKind.Transform:
                    newIndexOfOldIndex = SortKeysTrackingIndices(
                        selectedClip.transformTracks[trackIndex].keys, TransformKeyTime);
                    break;
                case TimelineTrackKind.Sprite:
                    newIndexOfOldIndex = SortKeysTrackingIndices(
                        selectedClip.spriteTracks[trackIndex].keys, SpriteKeyTime);
                    break;
                case TimelineTrackKind.Bone:
                    newIndexOfOldIndex = SortKeysTrackingIndices(
                        selectedClip.boneTracks[trackIndex].keys, BoneKeyTime);
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
                selectedClip.events, laneIndex);
            List<EventMarker> laneMarkers = new List<EventMarker>(flatIndices.Count);
            for (int position = 0; position < flatIndices.Count; position++)
            {
                laneMarkers.Add(selectedClip.events[flatIndices[position]]);
            }

            int[] newIndexOfOldIndex = SortKeysTrackingIndices(laneMarkers, EventMarkerTime);

            for (int position = 0; position < flatIndices.Count; position++)
            {
                selectedClip.events[flatIndices[position]] = laneMarkers[position];
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
            if (selectedKeys.Count == 0)
            {
                return;
            }

            List<KeyAddress> remapped = new List<KeyAddress>(selectedKeys.Count);
            bool changed = false;
            foreach (KeyAddress address in selectedKeys)
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

            selectedKeys.Clear();
            for (int index = 0; index < remapped.Count; index++)
            {
                selectedKeys.Add(remapped[index]);
            }

            if (hasActiveKey
                && activeKey.trackKind == trackKind
                && activeKey.trackIndex == trackIndex)
            {
                if (activeKey.keyIndex >= 0 && activeKey.keyIndex < newIndexOfOldIndex.Length)
                {
                    activeKey = new KeyAddress(
                        trackKind, trackIndex, newIndexOfOldIndex[activeKey.keyIndex]);
                }
                else
                {
                    hasActiveKey = false;
                }
            }
        }

        /// <summary>Selects every key in the clip, across every track.</summary>
        private void SelectAllKeys()
        {
            if (selectedClip == null)
            {
                return;
            }
            selectedKeys.Clear();
            hasActiveKey = false;

            AddTrackKeysToSelection(TimelineTrackKind.Transform, selectedClip.transformTracks.Count);
            AddTrackKeysToSelection(TimelineTrackKind.Sprite, selectedClip.spriteTracks.Count);
            AddTrackKeysToSelection(
                TimelineTrackKind.Bone,
                selectedClip.boneTracks != null ? selectedClip.boneTracks.Count : 0);
            AddTrackKeysToSelection(
                TimelineTrackKind.Event,
                EventLaneAddressing.ComputeLaneKeys(selectedClip.events).Count);

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
                selectedKeys.Add(new KeyAddress(trackKind, trackIndex, keyIndex));
            }
        }

        /// <summary>Clears the key selection without touching the hierarchy selection.</summary>
        private void DeselectAllKeys()
        {
            if (selectedKeys.Count == 0)
            {
                return;
            }
            selectedKeys.Clear();
            hasActiveKey = false;
            RepaintLanes();
            RebuildInspector();
            RebuildTimeline();
        }

        /// <summary>Selects every key on one track, replacing the selection unless adding to it.</summary>
        private void SelectAllKeysOnTrack(
            TimelineTrackKind trackKind, int trackIndex, bool additive)
        {
            if (selectedClip == null)
            {
                return;
            }
            if (!additive)
            {
                selectedKeys.Clear();
                hasActiveKey = false;
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
        // Inspector. Bound fields get undo, dirtying and prefab overrides for free, so nothing
        // here hand-rolls an edit path.
        // -------------------------------------------------------------------------------------

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
        private sealed class LiveTransformBinding
        {
            /// <summary>The rig target this block edits; 0 when <see cref="boneName"/> is set.</summary>
            public uint targetId;

            // The name, not the track: a node with no keys yet still has a block on screen, and the
            // track that will hold its poses does not exist yet either.
            /// <summary>The node this block edits by name; empty for a part.</summary>
            public string boneName;

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

        // A focused field is skipped, not overwritten: half-typed text is a value the user is
        // mid-authoring, and a scrub that stamped over it would fight the person using it.
        /// <summary>Pushes the value at the playhead into the fields already on screen.</summary>
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
            if (!string.IsNullOrEmpty(binding.boneName))
            {
                RefreshLiveBoneValues(binding);
                return;
            }

            // Rig Edit's fields show the live preview pose, not the clip's offset-from-rest value
            // (see AddTransformFields) -- the per-tick refresh has to keep showing that same thing,
            // or the correct value painted when the block was built would be overwritten by the
            // wrong one on the very next tick.
            if (IsRigEditMode)
            {
                RefreshLiveRigEditTransform(binding);
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

        /// <summary>
        /// Rig Edit's per-tick refresh for a rig target's transform block: the same live-pose
        /// source <see cref="AddTransformFields"/> paints it with initially, kept in sync so the
        /// fields never drift from what the viewport gizmo is dragging.
        /// </summary>
        private void RefreshLiveRigEditTransform(LiveTransformBinding binding)
        {
            float3 position;
            float3 rotationDegrees;
            float3 scale;
            ReadRigEditPose(binding.targetId, out position, out rotationDegrees, out scale);

            SetVectorWithoutDisturbingEdit(
                binding.positionField, new Vector3(position.x, position.y, position.z));
            SetVectorWithoutDisturbingEdit(
                binding.rotationField,
                new Vector3(rotationDegrees.x, rotationDegrees.y, rotationDegrees.z));
            SetVectorWithoutDisturbingEdit(
                binding.scaleField, new Vector3(scale.x, scale.y, scale.z));
        }

        private void RefreshLiveBoneValues(LiveTransformBinding binding)
        {
            // Rig Edit's fields show the live preview pose, not a bone track's key value -- see
            // AddBoneTransformFields. The per-tick refresh has to keep showing that same thing.
            if (IsRigEditMode)
            {
                RefreshLiveRigEditBone(binding);
                return;
            }

            // Looked up per refresh rather than held, because the first key on this node mints the
            // track: a reference captured when the block was built would stay null for the rest of
            // the block's life, leaving the fields frozen the moment they started to matter.
            BoneTrack track = FindBoneTrack(binding.boneName);

            float3 position;
            float3 rotationDegrees;
            float3 scale;
            bool hasKeys = ClipBoneEditing.TryEvaluate(
                track, playheadTime, out position, out rotationDegrees, out scale);
            bool isOnKey = ClipBoneEditing.FindKeyIndexAt(track, playheadTime) >= 0;

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

        /// <summary>
        /// Rig Edit's per-tick refresh for a bone or bare grouping transform's block: the same
        /// live-pose source <see cref="AddBoneTransformFields"/> paints it with initially.
        /// </summary>
        private void RefreshLiveRigEditBone(LiveTransformBinding binding)
        {
            float3 position;
            float3 rotationDegrees;
            float3 scale;
            ReadRigEditBonePose(binding.boneName, out position, out rotationDegrees, out scale);

            SetVectorWithoutDisturbingEdit(
                binding.positionField, new Vector3(position.x, position.y, position.z));
            SetVectorWithoutDisturbingEdit(
                binding.rotationField,
                new Vector3(rotationDegrees.x, rotationDegrees.y, rotationDegrees.z));
            SetVectorWithoutDisturbingEdit(
                binding.scaleField, new Vector3(scale.x, scale.y, scale.z));
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

        // The capture test is not redundant with the focus test: a field's drag handle captures the
        // mouse without focusing the input behind it, so focus alone misses a number being dragged.
        /// <summary>Whether the user is currently typing in a field, or dragging it.</summary>
        private static bool IsBeingEdited(VisualElement field)
        {
            if (field == null || field.panel == null)
            {
                return false;
            }

            VisualElement capturing =
                field.panel.GetCapturingElement(PointerId.mousePointerId) as VisualElement;
            if (capturing != null && (capturing == field || field.Contains(capturing)))
            {
                return true;
            }

            VisualElement focused = field.panel.focusController.focusedElement as VisualElement;
            return focused != null && (focused == field || field.Contains(focused));
        }

        // -------------------------------------------------------------------------------------
        // Deferred pane rebuilds.
        // -------------------------------------------------------------------------------------

        // A field's drag handle captures the mouse on the element itself, so a rebuild's Clear()
        // releases the capture and ends the drag after roughly one pixel — this guard stops that.
        /// <summary>Whether a pointer gesture inside this window is live, so nothing may be torn down yet.</summary>
        private bool IsPointerGestureInProgress()
        {
            IPanel panel = rootVisualElement != null ? rootVisualElement.panel : null;
            return panel != null && panel.GetCapturingElement(PointerId.mousePointerId) != null;
        }

        /// <summary>Rebuilds the inspector, or defers it to the end of a live drag.</summary>
        private void RequestInspectorRebuild()
        {
            if (IsPointerGestureInProgress())
            {
                inspectorRebuildPending = true;
                return;
            }
            RebuildInspector();
        }

        /// <summary>Rebuilds the timeline, or defers it to the end of a live drag.</summary>
        private void RequestTimelineRebuild()
        {
            if (IsPointerGestureInProgress())
            {
                timelineRebuildPending = true;
                return;
            }
            RebuildTimeline();
        }

        /// <summary>Rebuilds the hierarchy, or defers it to the end of a live drag.</summary>
        private void RequestHierarchyRebuild()
        {
            if (IsPointerGestureInProgress())
            {
                hierarchyRebuildPending = true;
                return;
            }
            RebuildHierarchy();
        }

        // Driven from the tick, not a pointer-capture-out callback: a capture released by the
        // element's own removal has no handler left to notify.
        /// <summary>Runs what a gesture deferred, once the gesture is over.</summary>
        private void FlushDeferredPaneRebuilds()
        {
            if (!hierarchyRebuildPending && !timelineRebuildPending && !inspectorRebuildPending)
            {
                return;
            }
            if (IsPointerGestureInProgress())
            {
                return;
            }

            bool rebuildHierarchy = hierarchyRebuildPending;
            bool rebuildTimeline = timelineRebuildPending;
            bool rebuildInspector = inspectorRebuildPending;
            hierarchyRebuildPending = false;
            timelineRebuildPending = false;
            inspectorRebuildPending = false;

            // Hierarchy first: rebuilding it notifies its own selection, which usually rebuilds the
            // other two itself. Not relied on — that notification is suppressed when it reports a
            // selection already applied — so the other two still run, and at worst repeat work.
            if (rebuildHierarchy)
            {
                RebuildHierarchy();
            }
            if (rebuildTimeline)
            {
                RebuildTimeline();
            }
            if (rebuildInspector)
            {
                RebuildInspector();
            }
        }

        /// <summary>Fills the inspector for whatever is selected: a key, a bone, or the clip itself.</summary>
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
                    BuildComponentStack(item, item == activeItem);
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

            // The key's object first, with its components. A key is a moment of something, and the
            // something is what the channels belong to — reading the key without it meant losing
            // sight of what else the part was doing at that time. An event marker has no object:
            // it belongs to the clip, so it gets no stack.
            if (shown.trackKind != TimelineTrackKind.Event)
            {
                HierarchyItem owningItem = FindHierarchyItemForKey(shown);
                if (owningItem != null)
                {
                    BuildComponentStack(owningItem, true);
                }
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

            AddKeyValueFields(keyProperty);
            inspectorPane.Bind(clipSerializedObject);

            AddInterpolationControls(shown);
            return true;
        }

        // Flattened rather than one PropertyField on the struct: the drawer renders an array
        // element as a foldout named "Element 3", meaningless beside a heading naming the key by
        // its time. Easing fields are skipped since AddInterpolationControls shows them as a curve.
        /// <summary>The key's own values, each as its own field, with the easing fields left out.</summary>
        private void AddKeyValueFields(SerializedProperty keyProperty)
        {
            SerializedProperty childProperty = keyProperty.Copy();
            SerializedProperty endProperty = keyProperty.GetEndProperty();
            bool enterChildren = true;
            while (childProperty.NextVisible(enterChildren)
                && !SerializedProperty.EqualContents(childProperty, endProperty))
            {
                enterChildren = false;
                if (IsEasingPropertyName(childProperty.name))
                {
                    continue;
                }
                inspectorPane.Add(new PropertyField(childProperty.Copy()));
            }
        }

        private static bool IsEasingPropertyName(string propertyName)
        {
            return propertyName == "interpolation"
                || propertyName == "bezierStartHandle"
                || propertyName == "bezierEndHandle";
        }

        // The window field edits in frames but stores seconds — the conversion happens here, at the
        // one point a person is looking at the number, with resolved seconds shown beside it.
        /// <summary>The selected event marker: which event it is, how long its window runs, and its payload.</summary>
        private void AddSelectedEventMarkerFields(KeyAddress address)
        {
            int flatIndex = ResolveEventFlatIndex(address);
            if (selectedClip.events == null || flatIndex < 0)
            {
                return;
            }

            EventMarker marker = selectedClip.events[flatIndex];
            AnimEventKeyRegistry registry = ResolveEventKeyRegistry();

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

        /// <summary>Which event this marker fires, chosen from the project's event-name vocabulary.</summary>
        private void AddEventKeyField(
            KeyAddress address, EventMarker marker, AnimEventKeyRegistry registry)
        {
            Button eventButton = new Button
            {
                text = "Event: " + DescribeEventName(marker.eventKey, registry)
            };
            eventButton.clicked += () => OpenEventKeyPicker(address, registry, eventButton);
            inspectorPane.Add(eventButton);
            inspectorPane.Add(MakeHint(DescribeEventKey(marker.eventKey, registry)));
        }

        /// <summary>The event's name, or an unresolved id when the registry does not (or no longer) names it.</summary>
        private static string DescribeEventName(uint eventKey, AnimEventKeyRegistry registry)
        {
            string resolvedName = registry != null ? registry.FindName(eventKey) : null;
            return resolvedName ?? "(unresolved 0x" + eventKey.ToString("X8") + ")";
        }

        private void OpenEventKeyPicker(
            KeyAddress address, AnimEventKeyRegistry registry, Button anchor)
        {
            VocabularyPicker.Open(
                rootVisualElement,
                anchor,
                registry,
                registry,
                VocabularyPickerConfig.ForEventKeys(registry),
                chosenEventKey => ApplyEventKeyChoice(address, chosenEventKey, registry),
                RebuildInspector);
        }

        private void ApplyEventKeyChoice(
            KeyAddress address, uint chosenEventKey, AnimEventKeyRegistry registry)
        {
            int flatIndex = ResolveEventFlatIndex(address);
            if (flatIndex < 0)
            {
                return;
            }

            AnimEventKeyEntry chosen = FindRegistryEntryByKey(registry, chosenEventKey);
            EditEventMarker(address, "Change Event Key", editedMarker =>
            {
                editedMarker.eventKey = chosenEventKey;

                // The registry's default window applies only when the marker has none of its own,
                // so re-pointing a hand-tuned six-frame window at another event does not quietly
                // reset it to that event's default.
                if (editedMarker.windowSeconds <= 0f && chosen != null && chosen.defaultWindowFrames > 0)
                {
                    editedMarker.windowSeconds =
                        chosen.defaultWindowFrames / ResolveReferenceFrameRate(registry);
                }
                return editedMarker;
            });

            // The eventKey just written can move the marker into a different lane (E6 Task 2), so
            // its selection has to follow — re-resolved from the flat index captured before the
            // edit rather than trusting the caller's now possibly-stale lane/local pair.
            KeyAddress newAddress = ResolveEventKeyAddressForFlatIndex(flatIndex);
            if (selectedKeys.Remove(address))
            {
                selectedKeys.Add(newAddress);
            }
            if (hasActiveKey && activeKey.Equals(address))
            {
                activeKey = newAddress;
            }

            RebuildInspector();
        }

        /// <summary>The lane-local <see cref="KeyAddress"/> for an event marker at a known flat index.</summary>
        private KeyAddress ResolveEventKeyAddressForFlatIndex(int flatIndex)
        {
            uint eventKey = selectedClip.events[flatIndex].eventKey;
            List<uint> laneKeys = EventLaneAddressing.ComputeLaneKeys(selectedClip.events);
            int laneIndex = laneKeys.IndexOf(eventKey);
            int localIndex = EventLaneAddressing
                .ResolveLaneFlatIndices(selectedClip.events, laneIndex).IndexOf(flatIndex);
            return new KeyAddress(TimelineTrackKind.Event, laneIndex, localIndex);
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

        /// <summary>The entry holding a specific key, or null when the registry does not have it.</summary>
        private static AnimEventKeyEntry FindRegistryEntryByKey(
            AnimEventKeyRegistry registry, uint eventKey)
        {
            if (registry == null || registry.entries == null)
            {
                return null;
            }
            for (int entryIndex = 0; entryIndex < registry.entries.Count; entryIndex++)
            {
                AnimEventKeyEntry entry = registry.entries[entryIndex];
                if (entry != null && entry.eventKey == eventKey)
                {
                    return entry;
                }
            }
            return null;
        }

        /// <summary>The one-line status under the event button: its name, and whether it can hold a window.</summary>
        private static string DescribeEventKey(uint eventKey, AnimEventKeyRegistry registry)
        {
            string displayName = DescribeEventName(eventKey, registry);
            if (eventKey < (uint)ReservedEventKeys.FirstUserKey)
            {
                return displayName + " is reserved by the package — this clip will fail "
                    + "validation (V09).";
            }
            if (!AnimEventMaskKeys.IsMaskable(eventKey))
            {
                return displayName
                    + " · pulse-only (outside the maskable range, so a window here would never open).";
            }
            return displayName + " · mask bit " + (eventKey - AnimEventMaskKeys.FirstMaskKey) + ".";
        }

        /// <summary>The project-wide event registry; the only source now that the per-set override is gone.</summary>
        private AnimEventKeyRegistry ResolveEventKeyRegistry()
        {
            return VocabularyRegistryProvider.AnimEventKeys;
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

        /// <summary>Applies one undoable edit to an event marker and refreshes what shows it.</summary>
        private void EditEventMarker(
            KeyAddress address, string undoLabel, System.Func<EventMarker, EventMarker> edit)
        {
            int flatIndex = ResolveEventFlatIndex(address);
            if (selectedClip.events == null || flatIndex < 0)
            {
                return;
            }
            RecordClipEdit(undoLabel);
            selectedClip.events[flatIndex] = edit(selectedClip.events[flatIndex]);
            CommitClipEdit();

            // Requested: the payload fields are dragged, and a timeline rebuild per mouse move is
            // wasted work at best.
            RequestTimelineRebuild();
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
                RequestInspectorRebuild();
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
                RequestInspectorRebuild();
            });
            inspectorPane.Add(baseIndexField);
        }

        // Dragging writes Bezier and refreshes the dropdown's label in place rather than rebuilding
        // the inspector: a rebuild mid-gesture would replace the element under the captured pointer.
        /// <summary>The selected key's easing: a named preset to start from, and the curve it draws.</summary>
        private void AddInterpolationControls(KeyAddress address)
        {
            if (address.trackKind != TimelineTrackKind.Transform
                && address.trackKind != TimelineTrackKind.Bone)
            {
                return;
            }

            Interpolation currentInterpolation = GetKeyInterpolation(address);
            float2 startHandle;
            float2 endHandle;
            GetKeyBezierHandles(address, out startHandle, out endHandle);

            inspectorPane.Add(MakeHeading("Easing"));

            EasingCurveEditorElement curveEditor = new EasingCurveEditorElement();
            curveEditor.SetCurveWithoutNotify(currentInterpolation, startHandle, endHandle);

            DropdownField presetField = new DropdownField(
                "Curve",
                new List<string>(EasingPresets.DisplayNames),
                EasingPresets.IndexOf(currentInterpolation, startHandle, endHandle));
            presetField.tooltip =
                "The shape the curve leaves this key with. Pick a preset, then drag the handles to "
                + "make it your own.";
            presetField.RegisterValueChangedCallback(changeEvent =>
            {
                ApplyEasingPreset(address, curveEditor, changeEvent.newValue);
            });

            curveEditor.curveEdited += (draggedStart, draggedEnd) =>
            {
                SetKeyCurve(address, Interpolation.Bezier, draggedStart, draggedEnd);
                presetField.SetValueWithoutNotify(EasingPresets.DisplayNameOf(
                    Interpolation.Bezier, draggedStart, draggedEnd));
            };

            inspectorPane.Add(presetField);
            inspectorPane.Add(curveEditor);
            inspectorPane.Add(MakeHint(
                "Drag the handles to reshape the curve — that turns any preset into a custom one. "
                + "They stay inside the unit square: outside it the curve stops being a function of "
                + "time, or overshoots further than the baked bounds allow."));
        }

        // Picking "Custom" keeps the shape already on screen and only changes what stores it: it is
        // a request to start editing, not a request to look different.
        /// <summary>Writes the chosen preset onto the key and onto the curve widget.</summary>
        private void ApplyEasingPreset(
            KeyAddress address, EasingCurveEditorElement curveEditor, string chosenDisplayName)
        {
            int chosenIndex = EasingPresets.IndexOfDisplayName(chosenDisplayName);
            if (chosenIndex < 0)
            {
                return;
            }

            if (EasingPresets.IsCustomIndex(chosenIndex))
            {
                float2 shownStartHandle;
                float2 shownEndHandle;
                curveEditor.GetHandles(out shownStartHandle, out shownEndHandle);
                SetKeyCurve(address, Interpolation.Bezier, shownStartHandle, shownEndHandle);
                curveEditor.SetCurveWithoutNotify(
                    Interpolation.Bezier, shownStartHandle, shownEndHandle);
                return;
            }

            EasingPreset preset = EasingPresets.At(chosenIndex);
            SetKeyCurve(address, preset.interpolation, preset.startHandle, preset.endHandle);
            curveEditor.SetCurveWithoutNotify(
                preset.interpolation, preset.startHandle, preset.endHandle);
        }

        private Interpolation GetKeyInterpolation(KeyAddress address)
        {
            if (address.trackKind == TimelineTrackKind.Bone)
            {
                return selectedClip.boneTracks[address.trackIndex].keys[address.keyIndex].interpolation;
            }
            return selectedClip.transformTracks[address.trackIndex].keys[address.keyIndex].interpolation;
        }

        // Handles are written even for fixed modes, which never read them: they are the matching
        // cubic, so a key later switched to Bezier starts from the shape it was already playing.
        /// <summary>Writes a key's easing mode and its handles together.</summary>
        private void SetKeyCurve(
            KeyAddress address, Interpolation interpolation, float2 startHandle, float2 endHandle)
        {
            RecordClipEdit("Change Key Easing");
            EnsureUsableBezierHandles(ref startHandle, ref endHandle, interpolation);
            if (address.trackKind == TimelineTrackKind.Bone)
            {
                BoneTrack track = selectedClip.boneTracks[address.trackIndex];
                BoneKey key = track.keys[address.keyIndex];
                key.interpolation = interpolation;
                key.bezierStartHandle = startHandle;
                key.bezierEndHandle = endHandle;
                track.keys[address.keyIndex] = key;
            }
            else
            {
                TransformTrack track = selectedClip.transformTracks[address.trackIndex];
                TransformKey key = track.keys[address.keyIndex];
                key.interpolation = interpolation;
                key.bezierStartHandle = startHandle;
                key.bezierEndHandle = endHandle;
                track.keys[address.keyIndex] = key;
            }
            CommitClipEdit();
        }

        // A key that never carried handles holds two zeros, which the sampler reads as linear;
        // writing the diagonal handles on the switch keeps the editor and the sampler agreeing.
        /// <summary>Gives a Bezier key with no handles the ones that describe a straight line.</summary>
        private static void EnsureUsableBezierHandles(
            ref float2 startHandle, ref float2 endHandle, Interpolation interpolation)
        {
            if (interpolation != Interpolation.Bezier)
            {
                return;
            }
            if (math.all(startHandle == float2.zero) && math.all(endHandle == float2.zero))
            {
                startHandle = EasingPresets.LinearStartHandle;
                endHandle = EasingPresets.LinearEndHandle;
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

        /// <summary>The live pose of a bone at the playhead, editable in place.</summary>
        private void AddBoneTransformFields(VisualElement parent, string boneName)
        {
            LiveTransformBinding binding = new LiveTransformBinding { boneName = boneName };
            liveTransformBindings.Add(binding);

            bool isRigEdit = IsRigEditMode;

            float3 position;
            float3 rotationDegrees;
            float3 scale;
            bool hasKeys;
            bool isOnKey;
            if (isRigEdit)
            {
                // A bone track has no rest pose to fall back to (ApplyBoneEdit's own remark), so
                // outside Rig Edit an unkeyed bone reads as zero -- correct there, since zero
                // literally is "no offset yet". Rig Edit has no offset concept at all; it shows the
                // node's live preview pose, the same source RefreshRigEditGizmo pivots on.
                ReadRigEditBonePose(boneName, out position, out rotationDegrees, out scale);
                hasKeys = false;
                isOnKey = false;
            }
            else
            {
                // Resolved rather than passed in, and allowed to come back null: every object shows
                // a transform from the moment it is selected, and the track that stores its poses is
                // minted by the first key. Everything below reads a null track as "no keys", which is
                // exactly what an unkeyed node has.
                BoneTrack track = FindBoneTrack(boneName);
                hasKeys = ClipBoneEditing.TryEvaluate(
                    track, playheadTime, out position, out rotationDegrees, out scale);
                isOnKey = ClipBoneEditing.FindKeyIndexAt(track, playheadTime) >= 0;
            }

            binding.stateChip = MakeHint(isRigEdit
                ? "Base pose — drag the viewport gizmo to edit it. Rig Edit writes the prefab, not "
                    + "a key, so these fields are read-only here."
                : DescribeBoneState(hasKeys, isOnKey));
            parent.Add(binding.stateChip);

            VisualElement transformBlock = new VisualElement();
            transformBlock.AddToClassList(TransformBlockUssClassName);
            transformBlock.EnableInClassList(TransformOnKeyUssClassName, isOnKey);
            transformBlock.EnableInClassList(TransformInterpolatedUssClassName, hasKeys && !isOnKey);
            binding.block = transformBlock;

            Vector3Field positionField = new Vector3Field("Position");
            positionField.SetValueWithoutNotify(new Vector3(position.x, position.y, position.z));
            positionField.SetEnabled(!isRigEdit);
            positionField.RegisterValueChangedCallback(changeEvent =>
            {
                ApplyBoneEditFromFields(binding);
            });
            binding.positionField = positionField;
            transformBlock.Add(positionField);

            Vector3Field rotationField = new Vector3Field("Rotation");
            rotationField.SetValueWithoutNotify(
                new Vector3(rotationDegrees.x, rotationDegrees.y, rotationDegrees.z));
            rotationField.SetEnabled(!isRigEdit);
            rotationField.tooltip =
                "Euler degrees. The authored key stores a quaternion; this is the readable form of "
                + "it, converted at the boundary.";
            rotationField.RegisterValueChangedCallback(changeEvent =>
            {
                ApplyBoneEditFromFields(binding);
            });
            binding.rotationField = rotationField;
            transformBlock.Add(rotationField);

            Vector3Field scaleField = new Vector3Field("Scale");
            scaleField.SetValueWithoutNotify(new Vector3(scale.x, scale.y, scale.z));
            scaleField.SetEnabled(!isRigEdit);
            scaleField.RegisterValueChangedCallback(changeEvent =>
            {
                ApplyBoneEditFromFields(binding);
            });
            binding.scaleField = scaleField;
            transformBlock.Add(scaleField);

            parent.Add(transformBlock);

            // Every route into keying is refused in Rig Edit (ApplyBoneEdit is a clip edit; this
            // mode writes the prefab), so a Key button that could not do anything would just be
            // another dead control on top of the read-only fields above.
            if (isRigEdit)
            {
                return;
            }

            parent.Add(new Button(() =>
            {
                ApplyBoneEdit(boneName, position, rotationDegrees, scale);
            })
            {
                text = "Key"
            });
        }

        /// <summary>
        /// A skinned bone or bare grouping transform's live preview pose, for Rig Edit's read-only
        /// display -- the same source <see cref="RefreshRigEditGizmo"/> pivots on, found by name
        /// since a component block only has the bone name.
        /// </summary>
        private void ReadRigEditBonePose(
            string boneName, out float3 position, out float3 rotationDegrees, out float3 scale)
        {
            int previewIndex = previewController != null
                ? previewController.FindHierarchyIndexByName(boneName)
                : -1;
            Transform node = previewIndex >= 0 ? previewController.GetTransformByIndex(previewIndex) : null;
            if (node == null)
            {
                position = float3.zero;
                rotationDegrees = float3.zero;
                scale = new float3(1f, 1f, 1f);
                return;
            }

            position = new float3(node.localPosition.x, node.localPosition.y, node.localPosition.z);
            Vector3 nodeEuler = node.localEulerAngles;
            rotationDegrees = new float3(nodeEuler.x, nodeEuler.y, nodeEuler.z);
            scale = new float3(node.localScale.x, node.localScale.y, node.localScale.z);
        }

        /// <summary>The bone track posing a node on this clip, or null when nothing keys it yet.</summary>
        private BoneTrack FindBoneTrack(string boneName)
        {
            if (selectedClip == null || selectedClip.boneTracks == null
                || string.IsNullOrEmpty(boneName))
            {
                return null;
            }
            for (int trackIndex = 0; trackIndex < selectedClip.boneTracks.Count; trackIndex++)
            {
                BoneTrack track = selectedClip.boneTracks[trackIndex];
                if (track != null
                    && string.Equals(track.boneName, boneName, System.StringComparison.Ordinal))
                {
                    return track;
                }
            }
            return null;
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

        // Always keys, unlike a transform edit: a bone track has no rest pose in this window to
        // fall back to, so a held-but-unkeyed value would vanish on the next scrub.
        /// <summary>Writes a bone pose at the playhead, creating the track if this is its first key.</summary>
        private void ApplyBoneEdit(
            string boneName, float3 position, float3 rotationDegrees, float3 scale)
        {
            if (selectedClip == null || string.IsNullOrEmpty(boneName))
            {
                return;
            }

            RecordClipEdit("Key Bone");

            BoneTrack track = FindBoneTrack(boneName);
            bool isFirstKey = track == null;
            if (isFirstKey)
            {
                if (selectedClip.boneTracks == null)
                {
                    selectedClip.boneTracks = new List<BoneTrack>();
                }
                track = new BoneTrack
                {
                    boneName = boneName,
                    keys = new List<BoneKey>()
                };
                selectedClip.boneTracks.Add(track);
            }

            ClipBoneEditing.SetKeyValues(track, playheadTime, position, rotationDegrees, scale);
            CommitClipEdit();

            selectedKeys.Clear();
            hasActiveKey = false;

            // Requested, not run: a drag calls this on every mouse move, and rebuilding the timeline
            // per move is the stutter even where it does not destroy the field outright.
            RequestTimelineRebuild();

            // Only the first key rebuilds the panels around the field. It is the one that changes
            // what they say — the row becomes animated and the component stops reading "not keyed"
            // — and a rebuild on every keystroke would destroy the field being typed into.
            if (isFirstKey)
            {
                RequestHierarchyRebuild();
                RequestInspectorRebuild();
            }
        }

        /// <summary>Writes a bone transform block's three fields as one key, then re-states the block.</summary>
        private void ApplyBoneEditFromFields(LiveTransformBinding binding)
        {
            if (binding == null
                || string.IsNullOrEmpty(binding.boneName)
                || binding.positionField == null
                || binding.rotationField == null
                || binding.scaleField == null)
            {
                return;
            }

            ApplyBoneEdit(
                binding.boneName,
                ToFloat3(binding.positionField.value),
                ToFloat3(binding.rotationField.value),
                ToFloat3(binding.scaleField.value));
            RefreshLiveTransformBinding(binding);
        }

        // -------------------------------------------------------------------------------------
        // Sockets.
        // -------------------------------------------------------------------------------------

        // "Follows" is fixed here: changing it would move the socket onto a different object, so
        // that is done by removing it and adding one where it belongs. Every edit records undo on
        // the rig, not the clip: a socket is rig structure every clip in the set shares.
        /// <summary>A socket's fields: what it follows, where it sits, and what to hang off it.</summary>
        private void AddSocketFields(VisualElement parent, SocketDefinition socket)
        {
            RigAsset rig = ActiveRig;
            if (rig == null)
            {
                return;
            }

            bool resolved = previewController != null && previewController.IsSocketResolved(socket);
            parent.Add(MakeHint(resolved
                ? "Attachment point — follows this object every frame."
                : "Follows nothing: the binding below matches no part or bone, so this socket "
                    + "will sit at the actor's origin."));

            parent.Add(new Button(() => FocusSocket(socket.Id.Value))
            {
                text = "Move in View",
                tooltip =
                    "Puts the viewport gizmo on this socket's marker. W and E then move and rotate "
                    + "it, writing the offset below."
            });

            TextField nameField = new TextField("Name");
            nameField.SetValueWithoutNotify(socket.displayName);
            nameField.RegisterValueChangedCallback(changeEvent =>
            {
                RecordSocketEdit(rig, "Rename Socket");
                socket.displayName = changeEvent.newValue;
                CommitSocketEdit(false);
            });
            parent.Add(nameField);

            // The binding is stated, not offered. This socket is a component of the object it
            // follows, so rebinding it is removing it here and adding one where it belongs —
            // a dropdown that silently moved it into another object's stack would read as a
            // disappearance.
            Label followsLabel = MakeHint(socket.mode == SocketAttachMode.RigTarget
                ? "Follows this rig target, live."
                : "Follows this bone, whose motion is baked into the VAT.");
            parent.Add(followsLabel);

            if (socket.mode == SocketAttachMode.Bone)
            {
                parent.Add(MakeSocketBakeHint(socket));

                IntegerField layerField = new IntegerField("Layer");
                layerField.SetValueWithoutNotify(socket.layerIndex);
                layerField.tooltip =
                    "Which playback layer drives this socket's time. Only meaningful for a bone "
                    + "socket, whose pose comes from the baked track rather than from a live part.";
                layerField.RegisterValueChangedCallback(changeEvent =>
                {
                    RecordSocketEdit(rig, "Change Socket Layer");
                    socket.layerIndex = Mathf.Max(0, changeEvent.newValue);
                    CommitSocketPlacementEdit();
                });
                parent.Add(layerField);
            }

            parent.Add(MakeHeading("Offset"));

            Vector3Field offsetPositionField = new Vector3Field("Position");
            offsetPositionField.SetValueWithoutNotify(socket.localPosition);
            offsetPositionField.tooltip =
                "In the followed part or bone's local space, so it stays put as the rig moves.";
            offsetPositionField.RegisterValueChangedCallback(changeEvent =>
            {
                RecordSocketEdit(rig, "Move Socket");
                socket.localPosition = changeEvent.newValue;
                CommitSocketPlacementEdit();
            });
            parent.Add(offsetPositionField);

            Vector3Field offsetRotationField = new Vector3Field("Rotation");
            offsetRotationField.SetValueWithoutNotify(socket.localEulerAngles);
            offsetRotationField.RegisterValueChangedCallback(changeEvent =>
            {
                RecordSocketEdit(rig, "Rotate Socket");
                socket.localEulerAngles = changeEvent.newValue;
                CommitSocketPlacementEdit();
            });
            parent.Add(offsetRotationField);

            parent.Add(MakeHeading("Preview Attachment"));
            parent.Add(MakeHint(
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
            parent.Add(attachmentField);

        }

        // Only a bone socket needs baking: a rig-target socket's motion is its part's transform,
        // resolved live every frame, while a bone socket follows a bone that exists at run time
        // only as VAT texels, so its motion has to be sampled and stored ahead of time.
        /// <summary>Says whether a bone socket has baked motion yet, and for how many clips.</summary>
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

        /// <summary>A dropdown of the loaded prefab's transform names, falling back to typing.</summary>
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
            CommitSocketEdit(rebuildMarkers, true);
        }

        // Passing false for rebuildRows is what makes a socket's numbers draggable: a hierarchy
        // rebuild notifies its own selection, which rebuilds the inspector mid-drag and destroys
        // the field being dragged.
        /// <summary>Persists a socket edit, saying separately whether the hierarchy rows went stale with it.</summary>
        private void CommitSocketEdit(bool rebuildMarkers, bool rebuildRows)
        {
            RigAsset rig = ActiveRig;
            if (rig == null)
            {
                return;
            }
            EditorUtility.SetDirty(rig);

            if (rebuildMarkers && previewController != null)
            {
                previewController.RebuildSockets();
            }

            if (rebuildRows)
            {
                RequestHierarchyRebuild();
            }
            MarkPreviewDirty();
        }

        /// <summary>Persists a socket edit that only moved it: the cheapest refresh a dragged number needs.</summary>
        private void CommitSocketPlacementEdit()
        {
            CommitSocketEdit(false, false);
            if (previewController != null)
            {
                previewController.RefreshSocketPlacement();
            }
        }

        /// <summary>Deletes a socket, behind a confirmation naming what will lose its attachment point.</summary>
        private void ConfirmDeleteSocket(SocketDefinition socket)
        {
            RigAsset rig = ActiveRig;
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

        /// <summary>A block's name, marked when it is the active row.</summary>
        private SelectionHeadingElement MakeSelectionHeading(string name, bool isActive)
        {
            Label label = MakeHeading(isActive ? name + "   (active)" : name);
            label.AddToClassList(SelectionHeadingUssClassName);
            label.EnableInClassList(SelectionHeadingActiveUssClassName, isActive);

            // Part tag button: shown only when the row names a claimed rig target, bound in
            // BuildComponentStack once the target is resolved. Built here rather than left null so
            // the row layout (label grown, button at the far edge) is correct before the caller
            // fills it in.
            Button tagButton = new Button();
            tagButton.AddToClassList(SelectionHeadingTagButtonUssClassName);
            tagButton.tooltip = "The role this part plays, stored on the rig — shared by every "
                + "clip in this set. A clip that binds a track to this tag plays on any other rig "
                + "that tags a part the same way.";

            SelectionHeadingElement row = new SelectionHeadingElement(label, tagButton);
            row.AddToClassList(SelectionHeadingRowUssClassName);
            row.Add(label);
            row.Add(tagButton);
            return row;
        }

        /// <summary>One selection heading: the part's name, plus its rig-level tag button at the far edge.</summary>
        private sealed class SelectionHeadingElement : VisualElement
        {
            public readonly Label label;
            public readonly Button tagButton;

            public SelectionHeadingElement(Label label, Button tagButton)
            {
                this.label = label;
                this.tagButton = tagButton;
            }
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

                // Every relative key's resolved index just moved, and the resolved index is what the
                // line above shows. Refreshed in place first so a drag reads true as it goes; the
                // rebuild lands when the drag ends and catches the rows this cannot reach.
                RefreshLiveInspectorValues();
                RequestInspectorRebuild();
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

            return trackBlock;
        }

        // Unlike a transform edit this always keys, regardless of auto-key: a flipbook value is a
        // discrete frame with no in-between to hold, so a held edit would just silently disappear.
        /// <summary>Writes a flipbook index at the playhead, creating a key there when there is none.</summary>
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

            // Requested: the Index field is dragged, and a per-move timeline rebuild would take the
            // field with it the moment the first key mints a lane.
            RequestTimelineRebuild();

            // The resolved "+5 → 12" reading is the one thing on screen this changes, and it is
            // refreshable without a rebuild.
            RefreshLiveInspectorValues();
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

        /// <summary>Switches a key between absolute and relative without moving the frame it shows.</summary>
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
            RigAsset rig = ActiveRig;
            if (rig != null && rig.targets != null)
            {
                for (int targetIndex = 0; targetIndex < rig.targets.Count; targetIndex++)
                {
                    RigTargetDefinition target = rig.targets[targetIndex];
                    if (target != null && target.Id.Value == targetId
                        && !string.IsNullOrEmpty(target.displayName))
                    {
                        return target.displayName;
                    }
                }
            }

            // Same "(unresolved 0x...)" form as a dangling tag or event key: the rig has no name
            // for this id, whether because no rig is assigned or the target is gone.
            return "(unresolved 0x" + targetId.ToString("X8") + ")";
        }

        /// <summary>
        /// How one track's binding reads on its timeline header, and which rig part it drives.
        /// </summary>
        private readonly struct TrackBindingLabel
        {
            /// <summary>What the clip stores its keys against — the primary half of the header.</summary>
            public readonly string tagText;

            /// <summary>
            /// Where that tag lands on the rig currently open. Display only: it is derived from the
            /// rig, never from the clip, so it changes when the rig does and the keys do not.
            /// </summary>
            public readonly string partText;

            /// <summary>The rig target this track drives right now, or 0 when nothing resolves.</summary>
            public readonly uint resolvedTargetId;

            public TrackBindingLabel(string tagText, string partText, uint resolvedTargetId)
            {
                this.tagText = tagText;
                this.partText = partText;
                this.resolvedTargetId = resolvedTargetId;
            }
        }

        /// <summary>
        /// Reads a Transform or Flipbook track's binding tag first and its rig part second, because
        /// the tag is what the clip stores and the part is only where that tag happens to land on
        /// the rig currently open.
        /// </summary>
        private TrackBindingLabel DescribeTrackBinding(uint targetId, uint tagId)
        {
            if (tagId == 0u)
            {
                // Legacy target-bound track: creation now always assigns a tag, so this state only
                // survives in assets authored before that. The keys are real and still play; the
                // tag half reads as the action that fixes it, not as a state.
                return new TrackBindingLabel(
                    "(assign tag)", ResolveTargetDisplayName(targetId), targetId);
            }

            TargetTagRegistry tagRegistry = ResolveTargetTagRegistry();
            string tagName = tagRegistry != null ? tagRegistry.FindName(tagId) : null;
            string tagText = tagName ?? "(unresolved 0x" + tagId.ToString("X8") + ")";

            // A tag is unique within a rig, so there is at most one part to name.
            RigTargetDefinition boundTarget = ClipComponentModel.FindTargetByTag(ActiveRig, tagId);
            if (boundTarget == null)
            {
                // Nothing on this rig wears the tag, so the track drives nothing here. Still a row:
                // the keys exist and will play on a rig that does tag a part this way.
                return new TrackBindingLabel(tagText, "(no tagged part)", 0u);
            }

            return new TrackBindingLabel(
                tagText,
                string.IsNullOrEmpty(boundTarget.displayName)
                    ? "(unnamed part)"
                    : boundTarget.displayName,
                boundTarget.Id.Value);
        }

        // -------------------------------------------------------------------------------------
        // Timeline binding surface: a row's tag half moves the row's keys to another tag (clip
        // edit), its part half moves the tag to another rig part (rig edit).
        // -------------------------------------------------------------------------------------

        private TransformTrack GetTransformTrackAt(int trackIndex)
        {
            if (selectedClip == null || selectedClip.transformTracks == null
                || trackIndex < 0 || trackIndex >= selectedClip.transformTracks.Count)
            {
                return null;
            }
            return selectedClip.transformTracks[trackIndex];
        }

        private SpriteTrack GetSpriteTrackAt(int trackIndex)
        {
            if (selectedClip == null || selectedClip.spriteTracks == null
                || trackIndex < 0 || trackIndex >= selectedClip.spriteTracks.Count)
            {
                return null;
            }
            return selectedClip.spriteTracks[trackIndex];
        }

        private uint GetTimelineTrackTagId(TimelineTrackKind trackKind, int trackIndex)
        {
            if (trackKind == TimelineTrackKind.Transform)
            {
                TransformTrack track = GetTransformTrackAt(trackIndex);
                return track != null ? track.tagId : 0u;
            }
            if (trackKind == TimelineTrackKind.Sprite)
            {
                SpriteTrack track = GetSpriteTrackAt(trackIndex);
                return track != null ? track.tagId : 0u;
            }
            return 0u;
        }

        private void OpenTimelineTrackTagPicker(
            TimelineTrackKind trackKind, int trackIndex, VisualElement anchor)
        {
            if (selectedClip == null)
            {
                return;
            }
            TargetTagRegistry tagRegistry = ResolveTargetTagRegistry();
            VocabularyPicker.Open(
                rootVisualElement,
                anchor,
                tagRegistry,
                tagRegistry,
                VocabularyPickerConfig.ForTrackTagRebind(tagRegistry),
                chosenTagId => RetagTrack(trackKind, trackIndex, chosenTagId),
                RebuildTimeline);
        }

        /// <summary>
        /// The part half's picker. A legacy row with no tag opens the tag picker instead — the row
        /// needs its identity before "where does it land" means anything.
        /// </summary>
        private void OpenTimelinePartPicker(
            TimelineTrackKind trackKind, int trackIndex, VisualElement anchor)
        {
            uint trackTagId = GetTimelineTrackTagId(trackKind, trackIndex);
            if (trackTagId == 0u)
            {
                OpenTimelineTrackTagPicker(trackKind, trackIndex, anchor);
                return;
            }
            RigAsset rig = ActiveRig;
            if (rig == null)
            {
                ShowNotification(new GUIContent("Assign a rig to place tags on parts."));
                return;
            }
            RigTargetPicker.Open(
                rootVisualElement, anchor, rig, ResolveTargetTagRegistry(), trackTagId,
                pickedTargetId => MoveTagToRigPart(trackTagId, pickedTargetId));
        }

        /// <summary>
        /// Moves a track — its keys — to a tag (A56 D2). Picking a tag another keyed same-kind
        /// track already binds merges this row into it and deletes this one, which is what "move
        /// these keys to that line" means when the line already exists.
        /// </summary>
        private void RetagTrack(TimelineTrackKind trackKind, int trackIndex, uint chosenTagId)
        {
            if (selectedClip == null || chosenTagId == 0u)
            {
                return;
            }

            if (trackKind == TimelineTrackKind.Transform)
            {
                TransformTrack track = GetTransformTrackAt(trackIndex);
                if (track == null || track.tagId == chosenTagId)
                {
                    return;
                }
                int destinationIndex = -1;
                for (int otherIndex = 0; otherIndex < selectedClip.transformTracks.Count; otherIndex++)
                {
                    if (otherIndex != trackIndex
                        && selectedClip.transformTracks[otherIndex] != null
                        && selectedClip.transformTracks[otherIndex].tagId == chosenTagId)
                    {
                        destinationIndex = otherIndex;
                        break;
                    }
                }
                RecordClipEdit("Move Keys To Tag");
                if (destinationIndex >= 0)
                {
                    ClipComponentModel.MergeTransformTracks(
                        track, selectedClip.transformTracks[destinationIndex]);
                    selectedClip.transformTracks.RemoveAt(trackIndex);
                    OnTrackListChanged();
                }
                else
                {
                    track.tagId = chosenTagId;
                }
            }
            else if (trackKind == TimelineTrackKind.Sprite)
            {
                SpriteTrack track = GetSpriteTrackAt(trackIndex);
                if (track == null || track.tagId == chosenTagId)
                {
                    return;
                }
                int destinationIndex = -1;
                for (int otherIndex = 0; otherIndex < selectedClip.spriteTracks.Count; otherIndex++)
                {
                    if (otherIndex != trackIndex
                        && selectedClip.spriteTracks[otherIndex] != null
                        && selectedClip.spriteTracks[otherIndex].tagId == chosenTagId)
                    {
                        destinationIndex = otherIndex;
                        break;
                    }
                }
                if (destinationIndex >= 0 && !ClipComponentModel.SpriteTracksMergeCompatible(
                        track, selectedClip.spriteTracks[destinationIndex]))
                {
                    // Refused, not forced: a sprite key's number is meaningless under the other
                    // track's mode/base, so a silent merge would retune every key it moved.
                    ShowNotification(new GUIContent(
                        "That tag's flipbook row uses different frame settings — merge refused."));
                    return;
                }
                RecordClipEdit("Move Keys To Tag");
                if (destinationIndex >= 0)
                {
                    ClipComponentModel.MergeSpriteTracks(
                        track, selectedClip.spriteTracks[destinationIndex]);
                    selectedClip.spriteTracks.RemoveAt(trackIndex);
                    OnTrackListChanged();
                }
                else
                {
                    track.tagId = chosenTagId;
                }
            }
            else
            {
                return;
            }

            CommitClipEdit();
            if (validationBadge != null)
            {
                validationBadge.Refresh(ActiveRig, clipSet);
            }
            RebuildTimeline();
        }

        /// <summary>
        /// A merge deleted a track, so every stored track index after it points one row off.
        /// Selection and expansion are addressed by those indices; cheaper to drop than remap.
        /// </summary>
        private void OnTrackListChanged()
        {
            selectedKeys.Clear();
            hasActiveKey = false;
            expandedTrackKeys.Clear();
        }

        // Lands an existing row's tag on another rig part: the row is the subject, so its keys are
        // already where they belong and only the tag's wearer moves. A rig edit, never a clip one —
        // the old wearer is cleared, and every clip set sharing the rig follows the keys to the new part.
        private void MoveTagToRigPart(uint tagId, uint newTargetId)
        {
            RigAsset rig = ActiveRig;
            if (tagId == 0u
                || !WriteRigPartTag(rig, FindRigTargetById(newTargetId), tagId, "Move Target Tag"))
            {
                return;
            }
            FinishRigTagEdit(rig);
        }

        // Renames what a rig part is, from the inspector — and brings its animation along. The
        // mirror image of MoveTagToRigPart: there the row is the subject, here the part is, so its
        // keys are expected to follow onto the new tag rather than being left behind. The carry is
        // scoped to the open clip set; another clip set keyed against the old tag is not rewritten.
        private void RetagRigPart(RigTargetDefinition wearer, uint chosenTagId)
        {
            RigAsset rig = ActiveRig;
            if (rig == null || wearer == null || wearer.tagId == chosenTagId)
            {
                return;
            }
            uint previousTagId = wearer.tagId;

            // One group for the rig write and every clip the carry touches, so a single Ctrl+Z puts
            // all of it back rather than unpicking the sweep one clip at a time.
            Undo.IncrementCurrentGroup();
            int tagEditUndoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Set Target Tag");

            if (!WriteRigPartTag(rig, wearer, chosenTagId, "Set Target Tag"))
            {
                return;
            }
            CarryClipSetKeysToTag(previousTagId, chosenTagId);
            Undo.CollapseUndoOperations(tagEditUndoGroup);

            FinishRigTagEdit(rig);
        }

        // Enforces a tag's uniqueness within a rig here, the one core behind both surfaces that can
        // change it, so it cannot be enforced on one and not the other.
        /// <summary>Writes which tag a rig part wears.</summary>
        private bool WriteRigPartTag(
            RigAsset rig, RigTargetDefinition wearer, uint tagId, string operationName)
        {
            if (rig == null || wearer == null || wearer.tagId == tagId)
            {
                return false;
            }

            Undo.RecordObject(rig, operationName);
            if (tagId != 0u)
            {
                RigTargetDefinition previousWearer = ClipComponentModel.FindTargetByTag(rig, tagId);
                if (previousWearer != null)
                {
                    previousWearer.tagId = 0u;
                }
            }
            wearer.tagId = tagId;
            EditorUtility.SetDirty(rig);
            return true;
        }

        // Every clip in the set, not only the open one: a part's animation living in four clips
        // would otherwise have one clip follow the retag and three left behind.
        /// <summary>Moves every row keyed against <paramref name="fromTagId"/> onto <paramref name="toTagId"/>.</summary>
        private void CarryClipSetKeysToTag(uint fromTagId, uint toTagId)
        {
            if (clipSet == null || clipSet.clips == null || fromTagId == 0u || toTagId == 0u)
            {
                return;
            }

            int movedTrackCount = 0;
            int mergedTrackCount = 0;
            int refusedTrackCount = 0;
            int touchedClipCount = 0;
            bool touchedSelectedClip = false;

            for (int clipIndex = 0; clipIndex < clipSet.clips.Count; clipIndex++)
            {
                ClipAsset clip = clipSet.clips[clipIndex];
                if (clip == null || !ClipComponentModel.ClipHasTrackTagged(clip, fromTagId))
                {
                    continue;
                }

                // Recorded before the move, because the snapshot has to predate the edit — which is
                // also why the clips with nothing to move are skipped above rather than here.
                Undo.RecordObject(clip, "Set Target Tag");
                ClipComponentModel.TagMoveOutcome outcome =
                    ClipComponentModel.MoveTracksToTag(clip, fromTagId, toTagId);
                movedTrackCount += outcome.movedTrackCount;
                mergedTrackCount += outcome.mergedTrackCount;
                refusedTrackCount += outcome.refusedTrackCount;
                if (outcome.ChangedTrackCount == 0)
                {
                    continue;
                }
                EditorUtility.SetDirty(clip);
                touchedClipCount++;
                touchedSelectedClip |= clip == selectedClip;
            }

            if (touchedSelectedClip)
            {
                // A merge deleted a track, so every stored track index is suspect — and even without
                // one the open clip's serialized copy and its preview are now behind the asset.
                OnTrackListChanged();
                RefreshSerializedClip();
                MarkPreviewDirty();
            }

            if (movedTrackCount + mergedTrackCount + refusedTrackCount == 0)
            {
                return;
            }
            ShowNotification(new GUIContent(DescribeTagCarry(
                movedTrackCount, mergedTrackCount, refusedTrackCount, touchedClipCount)));
        }

        private static string DescribeTagCarry(
            int movedTrackCount, int mergedTrackCount, int refusedTrackCount, int touchedClipCount)
        {
            string message = "Moved " + (movedTrackCount + mergedTrackCount).ToString()
                + " row(s) in " + touchedClipCount.ToString() + " clip(s) to the new tag.";
            if (mergedTrackCount > 0)
            {
                message += "\n" + mergedTrackCount.ToString()
                    + " merged into a row that tag already had.";
            }
            if (refusedTrackCount > 0)
            {
                message += "\n" + refusedTrackCount.ToString()
                    + " flipbook row(s) stayed put — different frame settings.";
            }
            return message;
        }

        private void FinishRigTagEdit(RigAsset rig)
        {
            if (validationBadge != null)
            {
                validationBadge.Refresh(rig, clipSet);
            }

            // Rebuilds the inspector as its last act, so the button that was just picked re-reads
            // its own label from here rather than needing a second refresh call beside this one.
            RebuildTimeline();
        }

        /// <summary>
        /// A56 D4: no keyed track goes tagless. Any transform/flipbook track still carrying the
        /// tagId == 0 sentinel gets its part tagged — reusing the registry tag named like the part,
        /// minting one otherwise — and binds it. Run after every path that creates tracks; a no-op
        /// when everything is already tagged, so it is safe inside any open undo group.
        /// </summary>
        private void EnsureClipTrackTagsAssigned(string operationName)
        {
            RigAsset rig = ActiveRig;
            TargetTagRegistry tagRegistry = ResolveTargetTagRegistry();
            if (selectedClip == null || rig == null || tagRegistry == null)
            {
                return;
            }

            bool recordedRig = false;
            bool mintedAnyEntry = false;
            bool changedAnything = false;

            List<TransformTrack> transformTracks = selectedClip.transformTracks;
            for (int trackIndex = 0; transformTracks != null && trackIndex < transformTracks.Count; trackIndex++)
            {
                TransformTrack track = transformTracks[trackIndex];
                if (track == null || track.tagId != 0u || track.targetId == 0u)
                {
                    continue;
                }
                if (!recordedRig)
                {
                    Undo.RecordObject(rig, operationName);
                    recordedRig = true;
                }
                bool createdRegistryEntry;
                uint assignedTagId = ClipComponentModel.EnsureTargetTagged(
                    rig, track.targetId, tagRegistry, out createdRegistryEntry);
                mintedAnyEntry |= createdRegistryEntry;
                if (assignedTagId != 0u)
                {
                    track.tagId = assignedTagId;
                    changedAnything = true;
                }
            }

            List<SpriteTrack> spriteTracks = selectedClip.spriteTracks;
            for (int trackIndex = 0; spriteTracks != null && trackIndex < spriteTracks.Count; trackIndex++)
            {
                SpriteTrack track = spriteTracks[trackIndex];
                if (track == null || track.tagId != 0u || track.targetId == 0u)
                {
                    continue;
                }
                if (!recordedRig)
                {
                    Undo.RecordObject(rig, operationName);
                    recordedRig = true;
                }
                bool createdRegistryEntry;
                uint assignedTagId = ClipComponentModel.EnsureTargetTagged(
                    rig, track.targetId, tagRegistry, out createdRegistryEntry);
                mintedAnyEntry |= createdRegistryEntry;
                if (assignedTagId != 0u)
                {
                    track.tagId = assignedTagId;
                    changedAnything = true;
                }
            }

            if (changedAnything)
            {
                EditorUtility.SetDirty(rig);
                EditorUtility.SetDirty(selectedClip);
            }
            if (mintedAnyEntry)
            {
                // CreateVocabularyEntry only mints in memory; an unpersisted row is lost on the
                // next domain reload.
                VocabularyRegistryProvider.PersistVocabulary(tagRegistry);
            }
        }

        /// <summary>Opens one undo step for a direct edit to the clip's own objects.</summary>
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
            TransformTrack track = ClipTransformEditing.FindTransformTrack(selectedClip, ActiveRig, targetId);
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

        // With auto-key on this writes a key at the playhead; with it off the value is held and
        // drawn as modified. A gizmo drag passes forceKey on release so the completed drag is kept
        // even though the frames during it were not.
        /// <summary>The single path a transform value is written through.</summary>
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

            // The move is the undo step here, not the key it may never become: with Auto Key off
            // nothing else on the stack describes it. Skipped mid-drag because BeginGizmoDrag has
            // already opened one step for the whole drag — recording per pointer move would bury
            // every other step under a few hundred of them.
            if (activeGizmoHandle == GizmoHandle.None)
            {
                RecordHeldTransformEdit("Move Part");
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

        // Values are read back off the fields, not closed over at build time: the block no longer
        // rebuilds on every change, so a captured value would stay stale and silently undo a
        // second field's drag.
        /// <summary>Writes a part transform block's three fields as one edit, then re-states the block.</summary>
        private void ApplyTransformEditFromFields(LiveTransformBinding binding)
        {
            if (binding == null
                || binding.positionField == null
                || binding.rotationField == null
                || binding.scaleField == null)
            {
                return;
            }

            ApplyTransformEdit(
                binding.targetId,
                ToFloat3(binding.positionField.value),
                ToFloat3(binding.rotationField.value),
                ToFloat3(binding.scaleField.value),
                false);
            RefreshLiveTransformBinding(binding);
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
                ClipTransformEditing.FindTransformTrack(selectedClip, ActiveRig, pendingTransformTargetId);
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
                    tagId = ClipComponentModel.ResolveNewTrackTagId(
                        ActiveRig, pendingTransformTargetId),
                    keys = new List<TransformKey>()
                };
                selectedClip.transformTracks.Add(track);
            }

            ClipTransformEditing.SetKeyValues(
                track, playheadTime, pendingPosition, pendingRotationDegrees, pendingScale);

            // A56 D4: keying an untagged part tags it, so the new row is born with its identity.
            EnsureClipTrackTagsAssigned("Key Transform");

            CommitClipEdit();
            hasPendingTransformEdit = false;

            selectedKeys.Clear();
            hasActiveKey = false;

            // Auto-key routes every mouse move of a field drag through here, so this is requested
            // rather than run for the same reason ApplyBoneEdit's is.
            RequestTimelineRebuild();
        }

        /// <summary>Drops a held edit — used when the playhead or the selection moves off it.</summary>
        private void DiscardPendingTransformEdit()
        {
            // Recorded like the move it throws away, so Revert is a step you can take back rather
            // than the one gesture in the block that is final.
            RecordHeldTransformEdit("Revert Held Move");
            hasPendingTransformEdit = false;
        }

        /// <summary>The always-visible transform block for the selected part.</summary>
        private void AddTransformFields(VisualElement parent, uint targetId)
        {
            LiveTransformBinding binding = new LiveTransformBinding { targetId = targetId };
            liveTransformBindings.Add(binding);

            bool isRigEdit = IsRigEditMode;

            float3 position;
            float3 rotationDegrees;
            float3 scale;
            TransformValueState valueState;
            if (isRigEdit)
            {
                // The clip has an offset-from-rest value here (zero, if the part is unkeyed) that
                // has no relationship to where the node actually sits -- see RefreshRigEditGizmo.
                // Rig Edit shows and edits the live preview pose instead.
                ReadRigEditPose(targetId, out position, out rotationDegrees, out scale);
                valueState = TransformValueState.Unkeyed;
            }
            else
            {
                valueState = ResolveDisplayedTransform(targetId, out position, out rotationDegrees, out scale);
            }

            binding.stateChip = isRigEdit
                ? MakeHint("Base pose — drag the viewport gizmo to edit it. Rig Edit writes the "
                    + "prefab, not a key, so these fields are read-only here.")
                : MakeTransformStateChip(valueState);
            parent.Add(binding.stateChip);

            VisualElement transformBlock = new VisualElement();
            binding.block = transformBlock;
            transformBlock.AddToClassList(TransformBlockUssClassName);
            transformBlock.EnableInClassList(
                TransformOnKeyUssClassName, !isRigEdit && valueState == TransformValueState.OnKey);
            transformBlock.EnableInClassList(
                TransformInterpolatedUssClassName,
                !isRigEdit && valueState == TransformValueState.Interpolated);
            transformBlock.EnableInClassList(
                TransformModifiedUssClassName, !isRigEdit && valueState == TransformValueState.Modified);

            Vector3Field positionField = new Vector3Field("Position");
            positionField.SetValueWithoutNotify(new Vector3(position.x, position.y, position.z));
            positionField.SetEnabled(!isRigEdit);
            positionField.RegisterValueChangedCallback(changeEvent =>
            {
                ApplyTransformEditFromFields(binding);
            });
            binding.positionField = positionField;
            transformBlock.Add(positionField);

            Vector3Field rotationField = new Vector3Field("Rotation");
            rotationField.SetValueWithoutNotify(
                new Vector3(rotationDegrees.x, rotationDegrees.y, rotationDegrees.z));
            rotationField.SetEnabled(!isRigEdit);
            rotationField.tooltip =
                "Euler degrees in Unity's ZXY order. The bake converts to radians once. "
                + "A flat rig leaves x and y at zero.";
            rotationField.RegisterValueChangedCallback(changeEvent =>
            {
                ApplyTransformEditFromFields(binding);
            });
            binding.rotationField = rotationField;
            transformBlock.Add(rotationField);

            Vector3Field scaleField = new Vector3Field("Scale");
            scaleField.SetValueWithoutNotify(new Vector3(scale.x, scale.y, scale.z));
            scaleField.SetEnabled(!isRigEdit);
            scaleField.RegisterValueChangedCallback(changeEvent =>
            {
                ApplyTransformEditFromFields(binding);
            });
            binding.scaleField = scaleField;
            transformBlock.Add(scaleField);

            parent.Add(transformBlock);

            // Keying is refused outright in Rig Edit (CommitPendingTransformEdit), so a Key/Revert
            // row that could not do anything would just be another dead control on top of the
            // read-only fields above.
            if (isRigEdit)
            {
                return;
            }

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
            parent.Add(keyRow);
        }

        /// <summary>
        /// A rig target's live preview pose, for Rig Edit's read-only display -- the same source
        /// <see cref="RefreshRigEditGizmo"/> pivots on, found by target id instead of by hierarchy
        /// row since a component block only has the id.
        /// </summary>
        private void ReadRigEditPose(
            uint targetId, out float3 position, out float3 rotationDegrees, out float3 scale)
        {
            Transform root = previewController != null ? previewController.HierarchyRoot : null;
            Transform node = root != null ? ResolveTargetSourceNode(targetId, root) : null;
            if (node == null)
            {
                position = float3.zero;
                rotationDegrees = float3.zero;
                scale = new float3(1f, 1f, 1f);
                return;
            }

            position = new float3(node.localPosition.x, node.localPosition.y, node.localPosition.z);
            Vector3 nodeEuler = node.localEulerAngles;
            rotationDegrees = new float3(nodeEuler.x, nodeEuler.y, nodeEuler.z);
            scale = new float3(node.localScale.x, node.localScale.y, node.localScale.z);
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

                // Sockets are rig data, so they are listed whether or not a clip is open.
                AddSocketDirectory();
                return;
            }
            clipSerializedObject.Update();

            inspectorPane.Add(MakeHeading("Clip"));
            inspectorPane.Add(MakeClipNameField());
            AddBoundField("duration");
            AddBoundField("defaultLoop");
            AddBoundField("rig");
            AddBoneTrackControls();
            AddSocketDirectory();
            inspectorPane.Bind(clipSerializedObject);
        }

        /// <summary>Clip-level bone-track summary, plus the by-name fallback for a set with no rig assigned.</summary>
        private void AddBoneTrackControls()
        {
            inspectorPane.Add(MakeHeading("Bone Tracks"));

            int boneTrackCount = selectedClip.boneTracks != null ? selectedClip.boneTracks.Count : 0;
            inspectorPane.Add(new Label(boneTrackCount.ToString() + " track(s)"));

            // LoadedPrefab, not just whether a rig is assigned: a rig with no sourcePrefab yet has
            // no hierarchy to pick a bone from either, and the typed fallback covers that state.
            bool hasHierarchy = LoadedPrefab != null;
            if (hasHierarchy)
            {
                inspectorPane.Add(MakeHint("Pick a bone in the Hierarchy pane to add or edit its track."));
                return;
            }

            TextField boneNameField = new TextField("Bone Name");
            boneNameField.tooltip =
                "Assign a rig with a Source Prefab in the toolbar to pick from the hierarchy "
                + "instead. Case sensitive — the bake reports a name it cannot resolve.";
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

        // isDelayed is load-bearing: without it the field commits on every keystroke, and each
        // commit is a file rename on disk.
        /// <summary>The clip's asset name, editable in place.</summary>
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
                    int flatIndex = ResolveEventFlatIndex(address);
                    if (events == null || flatIndex < 0 || flatIndex >= events.arraySize)
                    {
                        return null;
                    }
                    return events.GetArrayElementAtIndex(flatIndex);
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
