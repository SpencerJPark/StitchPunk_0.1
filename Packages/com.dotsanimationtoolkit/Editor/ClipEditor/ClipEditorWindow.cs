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

        // How far the pointer may travel between press and release and still count as a click.
        // Without this, every viewport orbit (which begins with the same press as a selection)
        // would also change the selection.
        private const float ClickMovementToleranceSquared = 9f;

        private const string HiddenUssClassName = "clip-editor--hidden";
        private const string TabActiveUssClassName = "clip-editor__tab--active";

        private const string HintUssClassName = "clip-editor__hint";
        private const string ReconcileRowUssClassName = "clip-editor__reconcile-row";
        private const string ReconcileRowLabelUssClassName = "clip-editor__reconcile-row-label";
        private const string ReconcileRemapUssClassName = "clip-editor__reconcile-remap";
        private const string ViewportFrameRigEditUssClassName =
            "clip-editor__viewport-frame--rig-edit";

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

        /// <summary>The Texture Packer tab's cover pane, and the panel built into it the first time it is opened.</summary>
        private VisualElement texturePackerPane;
        private TexturePackerPanel texturePackerPanel;

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
        private readonly ToolbarToggle[] tabToggles = new ToolbarToggle[7];

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

        private Image previewImage;
        private Label previewStatusLabel;
        private ValidationBadgeElement validationBadge;

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
        /// a rig and a clip set are independent, and this mirrors the shared selection every tab
        /// writes to, not just the two fields on this window's own toolbar.
        /// </summary>
        private RigAsset activeRig;

        private readonly ActiveAssetSelection selection = new ActiveAssetSelection();
        private readonly ClipEditorSession session = new ClipEditorSession();
        private ClipListPane clipListPane;
        private RigHierarchyPane hierarchyPane;
        private ClipInspectorPane clipInspectorPane;
        private TimelinePane timelinePane;

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

        private ToolbarToggle rigEditToggle;
        private ToolbarToggle ragdollPreviewToggle;
        private VisualElement reconcilePanel;
        private ScrollView reconcileList;
        private Label reconcileTitle;
        private VisualElement viewportFrame;
        private Label rigEditBanner;

        // Reused per inspector rebuild rather than allocated, since a rebuild happens on every
        // scrub tick that changes the displayed value.
        private readonly List<SpriteTrack> flipbookTracks = new List<SpriteTrack>();
        private readonly List<int> flipbookTrackIndices = new List<int>();

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
        private int gestureUndoGroup;
        private string gestureUndoName;

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
                // Setting Profile writes the shared rig selection from inside the panel — one
                // writer, not two.
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

        /// <summary>Brings the Clip Editor forward on its Texture Packer tab, with <paramref name="recipe"/> loaded.</summary>
        public static void FocusTexturePackerTab(TexturePackRecipeAsset recipe)
        {
            ClipEditorWindow window = FocusTab(ClipEditorTab.TexturePacker);
            if (window != null && window.texturePackerPanel != null && recipe != null)
            {
                window.texturePackerPanel.LoadRecipe(recipe);
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
            for (int itemIndex = 0; itemIndex < hierarchyPane.SelectedHierarchyItems.Count; itemIndex++)
            {
                state.selectedNames.Add(hierarchyPane.SelectedHierarchyItems[itemIndex].displayName);
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
            for (int itemIndex = 0; itemIndex < hierarchyPane.SelectedHierarchyItems.Count; itemIndex++)
            {
                sessionSelectedNames.Add(hierarchyPane.SelectedHierarchyItems[itemIndex].displayName);
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

            // The rig first: it is what the hierarchy and the preview are built from, and
            // ApplyClipSetSelection below deliberately leaves it alone. Restoring it second would
            // rebuild both panes twice, the first time against no rig at all. SetRig raises
            // ApplyRigSelection, which is what does the rebuilding.
            selection.SetRig(restoredRig);

            // SetClipSet raises ApplyClipSetSelection, which is what repopulates the clip list, the
            // hierarchy and the preview from the set.
            selection.SetClipSet(restoredClipSet);

            int restoredClipIndex = restoredClip != null && restoredClipSet != null
                && restoredClipSet.clips != null
                ? restoredClipSet.clips.IndexOf(restoredClip)
                : -1;
            if (clipListPane != null && restoredClipIndex >= 0)
            {
                clipListPane.SelectClipRow(restoredClipIndex);
            }
            else if (restoredClip != null)
            {
                // The clip is no longer in the set — deleted, or moved to another set while this
                // window was down. Loading it anyway would show a timeline the clip list disagrees
                // with, so the set is all that comes back.
                session.SetSelectedClip(null);
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
            // Built here, not in CreateGUI: the hidden instance and the fixtures reach them before any
            // tree exists, and a VisualElement cannot be a field initializer. Bound to the shared
            // selection and session now; CreateGUI hands each its UXML root.
            clipListPane = new ClipListPane();
            clipListPane.Bind(null, selection, session, previewController);
            hierarchyPane = new RigHierarchyPane();
            hierarchyPane.Bind(null, selection, session, previewController);
            clipInspectorPane = new ClipInspectorPane();
            clipInspectorPane.Bind(null, selection, session, previewController);
            timelinePane = new TimelinePane();
            timelinePane.Bind(null, selection, session, previewController);
            WirePanes();

            // Raised while this instance is still alive and before Unity serializes it, which is the
            // only moment the state below can still be read. See RememberSessionState.
            AssemblyReloadEvents.beforeAssemblyReload += RememberSessionState;
        }

        // Every window operation a pane reaches, and every pane event the window answers. Wired
        // here with the panes rather than in CreateGUI, so an instance that never builds its
        // tree (the hidden window, the fixtures) still has working panes.
        private void WirePanes()
        {
            hierarchyPane.IsRigEditMode = () => IsRigEditMode;
            hierarchyPane.ResolveTargetDisplayName = ResolveTargetDisplayName;
            hierarchyPane.TreeSelectionChanged += OnHierarchyTreeSelectionChanged;
            hierarchyPane.SelectionCleared += OnHierarchySelectionCleared;
            hierarchyPane.ContextMenuRequested += BuildHierarchyContextMenu;
            hierarchyPane.PrefabOpenRequested += OpenPrefabAt;
            hierarchyPane.ReparentRequested += ReparentInPrefab;

            clipInspectorPane.PickerRoot = rootVisualElement;
            clipInspectorPane.IsRigEditMode = () => IsRigEditMode;
            clipInspectorPane.RecordClipEdit = RecordClipEdit;
            clipInspectorPane.CommitClipEdit = CommitClipEdit;
            clipInspectorPane.RequestInspectorRebuild = RequestInspectorRebuild;
            clipInspectorPane.RequestTimelineRebuild = RequestTimelineRebuild;
            clipInspectorPane.RequestHierarchyRebuild = RequestHierarchyRebuild;
            clipInspectorPane.RebuildTimeline = timelinePane.RebuildTimeline;
            clipInspectorPane.MarkPreviewDirty = MarkPreviewDirty;
            clipInspectorPane.BeginUndoGesture = BeginUndoGesture;
            clipInspectorPane.EndUndoGesture = EndUndoGesture;
            clipInspectorPane.ReportStatus = text => timelinePane.StatusLabel.text = text;
            clipInspectorPane.GetKeyTime = timelinePane.GetKeyTime;
            clipInspectorPane.ResolveEventFlatIndex = timelinePane.ResolveEventFlatIndex;
            clipInspectorPane.FindBoneTrackIndex = hierarchyPane.FindBoneTrackIndex;
            clipInspectorPane.FindHierarchyItemForKey = hierarchyPane.FindHierarchyItemForKey;
            clipInspectorPane.BuildComponentStack = BuildComponentStack;
            clipInspectorPane.AddSocketDirectory = AddSocketDirectory;
            clipInspectorPane.FocusSocket = FocusSocket;
            clipInspectorPane.RecordSocketEdit = RecordSocketEdit;
            clipInspectorPane.CommitSocketEdit = CommitSocketEdit;
            clipInspectorPane.CommitSocketPlacementEdit = CommitSocketPlacementEdit;
            clipInspectorPane.ResolveDisplayedTransform = ResolveDisplayedTransform;
            clipInspectorPane.ReadRigEditPose = ReadRigEditPose;
            clipInspectorPane.ApplyTransformEdit = ApplyTransformEdit;
            clipInspectorPane.CommitPendingTransformEdit = CommitPendingTransformEdit;
            clipInspectorPane.DiscardPendingTransformEdit = DiscardPendingTransformEdit;
            clipInspectorPane.KeyDisplayedTransform = KeyDisplayedTransform;
            clipInspectorPane.IsTransformEditHeldFor = IsTransformEditHeldFor;
            clipInspectorPane.ClipRenamed += OnClipRenamed;

            timelinePane.WindowRoot = rootVisualElement;
            timelinePane.TransportTarget = this;
            timelinePane.SnapFrameCountProvider = () => SnapFrameCount;
            timelinePane.TransportFrameCountProvider = () => TransportFrameCount;
            timelinePane.LargeStepFramesProvider = () => LargeStepFrames;
            timelinePane.IsTransformActiveProvider = () => IsTransformActive;
            timelinePane.SetPlayheadTime = SetPlayheadTime;
            timelinePane.RecordClipEdit = RecordClipEdit;
            timelinePane.CommitClipEdit = CommitClipEdit;
            timelinePane.MarkPreviewDirty = MarkPreviewDirty;
            timelinePane.BeginUndoGesture = BeginUndoGesture;
            timelinePane.RecordUndoGestureStep = RecordUndoGestureStep;
            timelinePane.EndUndoGesture = EndUndoGesture;
            timelinePane.EnsureClipTrackTagsAssigned = EnsureClipTrackTagsAssigned;
            timelinePane.RecordSocketEdit = RecordSocketEdit;
            timelinePane.CommitSocketEdit = CommitSocketEdit;
            timelinePane.ShowNotification = ShowNotification;
            timelinePane.BuildObjectRef = BuildObjectRef;
            timelinePane.DescribeTrackBinding = DescribeTrackBinding;
            timelinePane.OpenTimelineTrackTagPicker = OpenTimelineTrackTagPicker;
            timelinePane.OpenTimelinePartPicker = OpenTimelinePartPicker;
            timelinePane.IsTargetSelected = hierarchyPane.IsTargetSelected;
            timelinePane.IsBoneSelected = hierarchyPane.IsBoneSelected;
            timelinePane.DescribeSelection = hierarchyPane.DescribeSelection;
            timelinePane.RefreshHierarchyRows = hierarchyPane.RefreshHierarchyRows;
            timelinePane.SelectHierarchyItem = hierarchyPane.SelectHierarchyItem;
            timelinePane.SelectItemByIdWithoutNotify = hierarchyPane.SelectItemByIdWithoutNotify;
            timelinePane.ClearTreeSelectionWithoutNotify = hierarchyPane.ClearTreeSelectionWithoutNotify;
            timelinePane.RebuildHierarchy = hierarchyPane.RebuildHierarchy;
            timelinePane.RebuildInspector = clipInspectorPane.RebuildInspector;
            timelinePane.ResolveEventKeyAddressForFlatIndex = clipInspectorPane.ResolveEventKeyAddressForFlatIndex;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorApplication.update -= OnEditorTick;
            PrefabStage.prefabSaved -= OnPrefabStageSaved;
            PrefabStage.prefabStageClosing -= OnPrefabStageClosing;
            AssemblyReloadEvents.beforeAssemblyReload -= RememberSessionState;
            selection.ClipSetChanged -= ApplyClipSetSelection;
            selection.RigChanged -= ApplyRigSelection;
            session.SelectedClipChanged -= SelectClip;
            session.RebuildRequested -= OnPaneRequestedRebuild;
            session.HierarchySelectionChanged -= DiscardPendingTransformEdit;

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

            if (clipListPane != null)
            {
                clipListPane.Dispose();
                clipListPane = null;
            }
            if (hierarchyPane != null)
            {
                hierarchyPane.Dispose();
                hierarchyPane = null;
            }
            if (clipInspectorPane != null)
            {
                clipInspectorPane.Dispose();
                clipInspectorPane = null;
            }
            if (timelinePane != null)
            {
                timelinePane.Dispose();
                timelinePane = null;
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
            if (texturePackerPanel != null)
            {
                texturePackerPanel.Dispose();
                texturePackerPanel = null;
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

            session.SelectedKeys.Clear();
            session.HasActiveKey = false;
            clipInspectorPane.RefreshSerializedClip();
            MarkPreviewDirty();
            timelinePane.RebuildTimeline();

            // Undo can restore a different clip length or frame rate, and the ruler and the
            // transport fields both read from those rather than deriving them.
            OnClipTimingChanged();
            clipInspectorPane.RebuildInspector();

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

            // Before BindToolbar, so RestoreView's writes to the selection at the end of this method
            // land on live handlers instead of firing into nothing.
            selection.ClipSetChanged += ApplyClipSetSelection;
            selection.RigChanged += ApplyRigSelection;
            session.SelectedClipChanged += SelectClip;
            session.RebuildRequested += OnPaneRequestedRebuild;
            session.HierarchySelectionChanged += DiscardPendingTransformEdit;

            BindToolbar();
            clipListPane.Bind(rootVisualElement.Q<VisualElement>("clip-list-pane"), selection, session, previewController);
            hierarchyPane.Bind(rootVisualElement.Q<VisualElement>("hierarchy-pane"), selection, session, previewController);
            BindViewport();
            clipInspectorPane.Bind(rootVisualElement.Q<VisualElement>("inspector-pane"), selection, session, previewController);
            timelinePane.Bind(rootVisualElement.Q<VisualElement>("timeline-pane"), selection, session, previewController);
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

            clipListPane.RefreshClipActionButtons();
            hierarchyPane.RebuildHierarchy();
            timelinePane.RebuildTimeline();

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
            texturePackerPane = rootVisualElement.Q<VisualElement>("texture-packer-pane");
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
                    clipInspectorPane.RebuildInspector();
                });
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

        // The one place that reads the rig's prefab, so every consumer below follows the rig field
        // to the same answer.
        private GameObject LoadedPrefab
        {
            get
            {
                return activeRig != null ? activeRig.sourcePrefab : null;
            }
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
            for (int itemIndex = 0; itemIndex < hierarchyPane.SelectedHierarchyItems.Count; itemIndex++)
            {
                roundTripSelectedNames.Add(hierarchyPane.SelectedHierarchyItems[itemIndex].displayName);
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
            hierarchyPane.RebuildHierarchy();
            RestoreRoundTripState();
            timelinePane.RebuildTimeline();
            hierarchyPane.RefreshPrefabActionState();
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
                int itemId = hierarchyPane.FindItemIdByName(roundTripSelectedNames[nameIndex]);
                if (itemId != RigHierarchyPane.NothingSelectedItemId)
                {
                    restoredIds.Add(itemId);
                }
            }

            if (restoredIds.Count == 0)
            {
                return;
            }
            hierarchyPane.SelectItemsById(restoredIds);
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
            clipInspectorPane.RebuildInspector();
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

            clipInspectorPane.RebuildInspector();
        }

        // Clicking the lit tab is a no-op, not a toggle-off: nothing sits behind a tab to reveal, so
        // a false value would leave the window showing a pane no tab claims. Snapped back to true.
        /// <summary>Binds the five tab toggles as a radio group.</summary>
        private void BindTabs()
        {
            BindTab(ClipEditorTab.TexturePacker, "tab-texture-packer",
                "Pack greyscale images into the channels of one texture: drag images from the "
                + "sidebar or the Project window onto the canvas, wire their channels into the "
                + "Pack Output node, and bake over the output in place.");
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

            ShowTexturePackerTab(activeTab == ClipEditorTab.TexturePacker);
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
                    vatBakePanel.Bind(selection);
                    vatBakePane.Add(vatBakePanel);
                }
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
                rigsPanel.Bind(selection);
                rigsPanel.UseInEditorRequested += OnRigUseInEditorRequested;
                rigsPanel.RigTargetsChanged += OnPanelChangedRigTargets;
                newRigPane.Add(rigsPanel);
            }

            if (isShown)
            {
                rigsPanel.RescanProject();
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
                clipSetsPanel.Bind(selection);
                clipSetsPanel.OpenInEditorRequested += OnClipSetOpenRequested;
                clipSetsPanel.SetClipsChanged += OnPanelChangedSetClips;
                clipSetsPane.Add(clipSetsPanel);
            }

            if (isShown)
            {
                clipSetsPanel.RescanProject();
            }

            clipSetsPane.EnableInClassList(HiddenUssClassName, !isShown);
        }

        private void ShowTexturePackerTab(bool isShown)
        {
            if (texturePackerPane == null)
            {
                return;
            }

            if (isShown && texturePackerPanel == null)
            {
                texturePackerPanel = new TexturePackerPanel();
                texturePackerPane.Add(texturePackerPanel);
            }

            if (isShown)
            {
                texturePackerPanel.RescanProject();
            }

            texturePackerPane.EnableInClassList(HiddenUssClassName, !isShown);
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
                    actorEditorPanel.Bind(selection);
                    actorEditorPane.Add(actorEditorPanel);
                }

                actorEditorPanel.SetSource(previewController);
                actorEditorPanel.RescanProject();
            }

            actorEditorPane.EnableInClassList(HiddenUssClassName, !isShown);
            if (actorEditorPanel != null)
            {
                actorEditorPanel.SetTicking(isShown);
            }
        }

        /// <summary>Answers the Rigs panel's Use in Clip Editor button: switches tabs — the rig is already the shared selection.</summary>
        private void OnRigUseInEditorRequested(RigAsset rig)
        {
            SetActiveTab(ClipEditorTab.ClipEditor);
        }

        /// <summary>Answers the Rigs panel changing the open rig's targets: re-reads the rig so the hierarchy and bindings follow.</summary>
        private void OnPanelChangedRigTargets(RigAsset rig)
        {
            if (rig == null || rig != activeRig)
            {
                return;
            }
            hierarchyPane.RebuildHierarchy();
            timelinePane.RebuildTimeline();
            clipInspectorPane.RebuildInspector();
        }

        /// <summary>Answers the Clip Sets panel's Open in Clip Editor button: switches tabs — the set is already the shared selection.</summary>
        private void OnClipSetOpenRequested(ClipSetAsset requestedSet)
        {
            SetActiveTab(ClipEditorTab.ClipEditor);
        }

        /// <summary>Answers the Clip Sets panel adding or removing a clip on the currently open set.</summary>
        private void OnPanelChangedSetClips(ClipSetAsset changedSet)
        {
            if (changedSet == null || changedSet != clipSet)
            {
                return;
            }
            clipListPane?.RefreshClipList();
            clipListPane?.RefreshClipActionButtons();
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
            HierarchyItem item = hierarchyPane.ActiveHierarchyItem;
            if (item == null)
            {
                ShowNotification(new GUIContent("Select a part to edit its base pose."));
                return;
            }

            string path = hierarchyPane.ResolveHierarchyPath(item);
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
            clipInspectorPane.RefreshSerializedClip();
            RunReconciliation();
            timelinePane.RebuildTimeline();
            MarkPreviewDirty();
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
                string pathToOpen = hierarchyPane.ResolveHierarchyPath(item);
                GameObject prefabToOpen = prefab;
                RedockBesideSceneView(() =>
                {
                    PrefabAuthoringBridge.OpenPrefab(prefabToOpen, pathToOpen);
                    EditorApplication.delayCall += ClipEditorDocking.FocusPrefabAuthoring;
                });
                return;
            }

            PrefabAuthoringBridge.OpenPrefab(prefab, hierarchyPane.ResolveHierarchyPath(item));

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
                            LoadedPrefab, hierarchyPane.ResolveHierarchyPath(item)))
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
                hierarchyPath = hierarchyPane.ResolveHierarchyPath(item)
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
                hierarchyPath = hierarchyPane.ResolveHierarchyPath(item)
            };
        }

        /// <summary>
        /// Repaints the tree and the viewport after a billboard edit, so both agree with the rig.
        /// </summary>
        private void RefreshAfterBillboardEdit()
        {
            hierarchyPane.RefreshHierarchyRows();
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
            if (hierarchyPane.SelectedSocketId != 0u)
            {
                Transform marker = previewController.GetSocketMarker(hierarchyPane.SelectedSocketId);
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
            HierarchyItem activeRigEditItem = IsRigEditMode ? hierarchyPane.ActiveHierarchyItem : null;
            if (!GizmoDragRouting.ShouldShowTransformGizmo(
                    IsRigEditMode, activeRigEditItem != null, hierarchyPane.SelectedTargetId != 0u, selectedClip != null))
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
                hierarchyPane.SelectedTargetId, out position, out rotationDegrees, out scale);

            previewController.SetGizmo(
                true, gizmoMode, new Vector3(position.x, position.y, position.z), activeGizmoHandle);
        }

        // Unlike clip authoring there is no track to sample: ResolveDisplayedTransform would return
        // an offset-from-rest value with no relationship to where the node actually sits.
        /// <summary>Rig Edit's gizmo pivot: the selected node's own live preview transform, or a held drag's value.</summary>
        private void RefreshRigEditGizmo(HierarchyItem item)
        {
            Transform node = hierarchyPane.ResolveHierarchyTransform(item);
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
            bool draggingSocket = hierarchyPane.SelectedSocketId != 0u;
            HierarchyItem activeRigEditItem = (!draggingSocket && IsRigEditMode) ? hierarchyPane.ActiveHierarchyItem : null;
            if (!draggingSocket
                && !GizmoDragRouting.ShouldShowTransformGizmo(
                    IsRigEditMode, activeRigEditItem != null, hierarchyPane.SelectedTargetId != 0u, selectedClip != null))
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
                Transform marker = previewController.GetSocketMarker(hierarchyPane.SelectedSocketId);
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
                Transform node = hierarchyPane.ResolveHierarchyTransform(activeRigEditItem);
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
                    hierarchyPane.SelectedTargetId, out position, out rotationDegrees, out scale);
            }

            // One step for the whole drag, opened before the first pointer move writes anything —
            // the same rule the release path follows for a key. Only for a drag that will hold a
            // clip value: a socket or Rig Edit drag records its own object when it commits, and a
            // step here would be a "Move Part" that moved nothing.
            if (hierarchyPane.SelectedSocketId == 0u && !IsRigEditMode)
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
                clipInspectorPane.RebuildInspector();
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

            clipInspectorPane.RebuildInspector();
            RefreshGizmo();
        }

        /// <summary>Sends a drag's value wherever the current selection says it belongs.</summary>
        private void ApplyGizmoDragValue(float3 position, float3 rotationDegrees, float3 scale)
        {
            if (hierarchyPane.SelectedSocketId != 0u)
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
            ApplyTransformEdit(hierarchyPane.SelectedTargetId, position, rotationDegrees, scale, false);
        }

        private bool hasPendingSocketEdit;
        private float3 pendingSocketPosition;
        private float3 pendingSocketRotation;

        /// <summary>Moves the selected node live during a Rig Edit drag, without touching the asset.</summary>
        private void PreviewRigNodeDrag(float3 position, float3 rotationDegrees, float3 scale)
        {
            Transform node = hierarchyPane.ResolveHierarchyTransform(hierarchyPane.ActiveHierarchyItem);
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
            Transform marker = previewController.GetSocketMarker(hierarchyPane.SelectedSocketId);
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
            SocketDefinition socket = hierarchyPane.FindSocket(hierarchyPane.SelectedSocketId);
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
            clipInspectorPane.RebuildInspector();
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
                hierarchyPane.SelectedSocketId != 0u, false, IsRigEditMode, IsAutoKeyEnabled, hasPendingEdit);

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

            clipInspectorPane.RebuildInspector();
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
                hierarchyPane.ClearHierarchySelection();
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
            if (previewController == null)
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
                if (!hierarchyPane.TryFindSocketSourceItemId(pickedSocketId, out itemId))
                {
                    return;
                }
                hierarchyPane.SelectItemById(itemId);
                FocusSocket(pickedSocketId);
                clipInspectorPane.RebuildInspector();
                return;
            }
            else if (previewController.TryGetTargetIdForTransform(pickedTransform, out pickedTargetId))
            {
                // A cutout part quad. Its row is a rig target, not a transform of the previewed
                // prefab, so the id comes from the target table rather than the preview hierarchy.
                if (!hierarchyPane.TryFindRigTargetItemId(pickedTargetId, out itemId))
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

            // Every item is expanded when the tree is built, so the row exists to be scrolled to.
            hierarchyPane.SelectItemById(itemId);
        }

        private void OnPreviewWheel(WheelEvent wheelEvent)
        {
            cameraNavigation.HandleWheel(wheelEvent);
        }

        // Window state, written to no asset: no data model pairs a rig with a clip set, so this
        // only records what this window plays the set against — no undo step, no actor bake changes.
        // The shared selection is the source now; this mirrors it and refreshes everything downstream.
        private void ApplyRigSelection(RigAsset rig)
        {
            activeRig = rig;

            if (previewController != null)
            {
                previewController.SetRig(activeRig);
                previewController.SetSkinnedSource(LoadedPrefab);
            }
            // Cleared before the tree is rebuilt: the old instance's transforms are gone, so the
            // held index now points into a hierarchy that no longer exists.
            hierarchyPane.SelectHierarchyItem(RigHierarchyPane.NothingSelectedItemId);
            previousPickCandidates.Clear();
            pickCandidates.Clear();
            hierarchyPane.RebuildHierarchy();
            timelinePane.RebuildTimeline();
            clipInspectorPane.RebuildInspector();
            if (validationBadge != null)
            {
                validationBadge.Refresh(activeRig, clipSet);
            }

            // LoadedPrefab is what Edit Prefab's enabled state depends on, and a pick here is one
            // of the places it changes. Without this the button was disabled at bind time — when
            // no rig is assigned yet — and never re-enabled, so assigning a rig left a button that
            // swallowed clicks in silence. ApplyClipSetSelection carries the same refresh now, for
            // the other place LoadedPrefab can change.
            hierarchyPane.RefreshPrefabActionState();
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

            string childPath = hierarchyPane.ResolveHierarchyPath(dragged);
            string parentPath = hierarchyPane.ResolveHierarchyPath(newParent);

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

        // -------------------------------------------------------------------------------------
        // Selection plumbing
        // -------------------------------------------------------------------------------------

        private void ApplyClipSetSelection(ClipSetAsset newClipSet)
        {
            clipSet = newClipSet;
            session.SetSelectedClip(null);

            // The Rig field is deliberately left alone. A clip set names no rig and a rig names no
            // clips — they are independent assets, paired only where an ActorAuthoring states both —
            // so swapping the open set must not swap the rig underneath it, any more than swapping
            // the rig should empty the clip list. Playing this set against the rig already loaded is
            // the whole point of the window: the tags line up, or they are reported as not lining up.

            // The hierarchy's rows come from the rig, which has not changed — but which of them a
            // clip already animates is drawn from the set, so the rows are re-rendered rather than
            // left showing the previous set's bold.
            hierarchyPane.SelectHierarchyItem(RigHierarchyPane.NothingSelectedItemId);
            hierarchyPane.RebuildHierarchy();

            if (previewController != null)
            {
                previewController.SetClipSet(clipSet);
            }
            if (validationBadge != null)
            {
                validationBadge.Refresh(activeRig, clipSet);
            }
        }

        private void SelectClip(ClipAsset clip)
        {
            selectedClip = clip;
            session.SelectedKeys.Clear();
            session.HasActiveKey = false;
            SetPlaying(false);
            playheadTime = 0f;
            session.SetPlayhead(playheadTime);
            clipInspectorPane.RefreshSerializedClip();
            timelinePane.RebuildTimeline();

            // The bar is bound once, while nothing is selected, so its fields start disabled and
            // showing zero. Without this they stay that way for the rest of the session and the
            // clip length simply cannot be typed into.
            SyncTransportFromClip();
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

        // A pane created, deleted or renamed a clip: the preview and the badge are the window's to refresh.
        // The tree's selection changed by a click or a driven select: key selection and hierarchy
        // selection are one selection with two sources, so the keys go and both panes rebuild.
        private void OnHierarchyTreeSelectionChanged()
        {
            session.SelectedKeys.Clear();
            session.HasActiveKey = false;
            timelinePane.RebuildTimeline();
            clipInspectorPane.RebuildInspector();
        }

        private void OnHierarchySelectionCleared()
        {
            timelinePane.RebuildTimeline();
            clipInspectorPane.RebuildInspector();
        }

        // The inspector's Key button: adopt what is on screen if nothing is held, then key it.
        private void KeyDisplayedTransform(uint targetId, float3 position, float3 rotationDegrees, float3 scale)
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
        }

        private bool IsTransformEditHeldFor(uint targetId)
        {
            return hasPendingTransformEdit && pendingTransformTargetId == targetId;
        }

        private void OnClipRenamed()
        {
            clipListPane?.RefreshClipList();
            timelinePane.RebuildTimeline();
        }

        private void OnPaneRequestedRebuild()
        {
            MarkPreviewDirty();
            if (validationBadge != null)
            {
                validationBadge.Refresh(activeRig, clipSet);
            }
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
            session.SetPlayhead(playheadTime);
            if (timelinePane.Playhead != null)
            {
                timelinePane.Playhead.NormalizedTime = playheadTime;
            }
            SyncTransportPlayhead();

            // The inspector shows the value at the playhead, so it moves with it. In place rather
            // than by rebuilding: a rebuild would destroy the field being typed into.
            clipInspectorPane.RefreshLiveInspectorValues();
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
            clipInspectorPane.RefreshSerializedClip();
            MarkPreviewDirty();
        }

        /// <summary>The Add Event button on the transport bar: opens the event picker anchored to it.</summary>
        private void OpenAddEventPicker()
        {
            if (selectedClip == null)
            {
                return;
            }

            AnimEventKeyRegistry registry = ClipInspectorPane.ResolveEventKeyRegistry();
            VocabularyPicker.Open(
                rootVisualElement,
                addEventButton,
                registry,
                registry,
                VocabularyPickerConfig.ForEventKeys(registry),
                chosenEventKey => timelinePane.AddEventAtPlayhead(chosenEventKey),
                // A Create… mint or an Edit… rename changes what a lane header should read, not
                // just the marker inspector — the timeline has to rebuild, not just RebuildInspector.
                timelinePane.RebuildTimeline);
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
            clipInspectorPane.RebuildInspector();
        }

        /// <summary>Rebuilds the timeline, or defers it to the end of a live drag.</summary>
        private void RequestTimelineRebuild()
        {
            if (IsPointerGestureInProgress())
            {
                timelineRebuildPending = true;
                return;
            }
            timelinePane.RebuildTimeline();
        }

        /// <summary>Rebuilds the hierarchy, or defers it to the end of a live drag.</summary>
        private void RequestHierarchyRebuild()
        {
            if (IsPointerGestureInProgress())
            {
                hierarchyRebuildPending = true;
                return;
            }
            hierarchyPane.RebuildHierarchy();
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
                hierarchyPane.RebuildHierarchy();
            }
            if (rebuildTimeline)
            {
                timelinePane.RebuildTimeline();
            }
            if (rebuildInspector)
            {
                clipInspectorPane.RebuildInspector();
            }
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
            int socketIndex = hierarchyPane.FindSocketIndex(socket.Id.Value);
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

            hierarchyPane.ClearHierarchySelection();
            if (previewController != null)
            {
                previewController.RebuildSockets();
            }
            hierarchyPane.RebuildHierarchy();
            MarkPreviewDirty();
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
        internal readonly struct TrackBindingLabel
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
                timelinePane.RebuildTimeline);
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
                    timelinePane.OnTrackListChanged();
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
                    timelinePane.OnTrackListChanged();
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
            timelinePane.RebuildTimeline();
        }

        // Lands an existing row's tag on another rig part: the row is the subject, so its keys are
        // already where they belong and only the tag's wearer moves. A rig edit, never a clip one —
        // the old wearer is cleared, and every clip set sharing the rig follows the keys to the new part.
        private void MoveTagToRigPart(uint tagId, uint newTargetId)
        {
            RigAsset rig = ActiveRig;
            if (tagId == 0u
                || !WriteRigPartTag(rig, hierarchyPane.FindRigTargetById(newTargetId), tagId, "Move Target Tag"))
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
                timelinePane.OnTrackListChanged();
                clipInspectorPane.RefreshSerializedClip();
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
            timelinePane.RebuildTimeline();
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
            clipInspectorPane.RefreshSerializedClip();
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

            session.SelectedKeys.Clear();
            session.HasActiveKey = false;

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

        /// <summary>
        /// A rig target's live preview pose, for Rig Edit's read-only display -- the same source
        /// <see cref="RefreshRigEditGizmo"/> pivots on, found by target id instead of by hierarchy
        /// row since a component block only has the id.
        /// </summary>
        private void ReadRigEditPose(
            uint targetId, out float3 position, out float3 rotationDegrees, out float3 scale)
        {
            Transform root = previewController != null ? previewController.HierarchyRoot : null;
            Transform node = root != null ? hierarchyPane.ResolveTargetSourceNode(targetId, root) : null;
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

    }
}
