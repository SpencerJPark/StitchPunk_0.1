// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>The Cutscene Editor tab's content: a slot/lane timeline plus an inspector for whatever is selected. Unity's own Scene view is the viewport.</summary>
    public sealed partial class CutsceneEditorPanel : VisualElement
    {
        private const float LaneRowHeight = 22f;
        private const float RulerHeight = 24f;
        private const float HeaderColumnWidth = 150f;
        private const float TrailingSeconds = 5f;

        private const string EasingCurveEditorElementName = "cutscene-easing-curve";

        private CutsceneAsset cutscene;
        // Every mutation is a SerializedProperty write plus ApplyModifiedProperties, buying Undo and
        // dirtying for free. The one exception is EnsureStableIds, which writes the object directly
        // and must run before the object is re-read back into this.
        private SerializedObject serializedObject;

        private ObjectField cutsceneField;
        private Label sceneStatusLabel;
        private Button sceneActionButton;
        private Slider zoomSlider;
        private Toggle previewShotToggle;
        private float pixelsPerSecond = 40f;
        private float playheadSeconds;

        // Two scroll views, not one: the header column scrolls vertically with the lanes and never
        // horizontally with them, which is the whole point of freezing it.
        private ScrollView timelineHeaderScroll;
        private ScrollView timelineLaneScroll;
        private VisualElement timelineHeaderContent;
        private bool isSyncingTimelineScroll;
        private ScrollView inspectorScroll;
        private CutsceneTimelinePlayheadElement playheadElement;
        private CutsceneCastPanel castPanel;

        private CutsceneViewportElement viewportElement;
        private VisualElement viewportOverlay;
        private Label viewportMessageLabel;
        private Button viewportActionButton;
        private Toggle shotModeToggle;

        /// <summary>Viewport locked to the camera lane (Shot) vs. the free orbit rig. Shot by default, so scrubbing shows the framed movie.</summary>
        private bool viewportShotMode = true;

        /// <summary>Set while this panel is the one driving <see cref="Selection"/>, so the sync back does not fight it.</summary>
        private bool isDrivingUnitySelection;

        private Button playToggleButton;
        private Image playToggleIcon;
        private Label timeReadoutLabel;
        private Button continueButton;
        private Label transportStatusLabel;
        private FloatField speedField;
        private Toggle loopPlaybackToggle;
        private Toggle skipHoldsToggle;

        private bool isPlaying;
        private double lastTickTime;
        private float playbackSpeed = 1f;
        private float prePlayPlayheadSeconds;

        // The hold the transport is waiting on, valid only while isGatingOnHold. Held by value
        // rather than by index, since a derived hold has no row in CutsceneAsset.holdMarkers to index into.
        private EffectiveHold gatingHold;
        private bool isGatingOnHold;

        // The four fields below are the primary selection, unpacked: they drive the inspector, the
        // Key button and the Scene-view sync, and they always mirror primaryItem.
        private int selectedSlotIndex = -1;
        private SelectedLaneKind selectedLaneKind = SelectedLaneKind.None;
        private int selectedPartTrackIndex = -1;
        private int selectedItemIndex = -1;

        private readonly HashSet<CutsceneItemAddress> selectedItems = new HashSet<CutsceneItemAddress>();

        /// <summary>The last item clicked — what the inspector edits when several are selected.</summary>
        private CutsceneItemAddress? primaryItem;

        // Every lane currently on screen, so a selection change can repaint the markers in place and
        // a band drag can ask each lane what it holds. Rebuilt with the timeline.
        private readonly List<RegisteredLane> registeredLanes = new List<RegisteredLane>();

        private VisualElement timelineContent;
        private BoxSelectElement boxSelectElement;
        private VisualElement boxSelectLane;

        /// <summary>The lane the press landed in, selected if the gesture stays a click.</summary>
        private CutsceneItemAddress boxSelectLaneAddress;
        private Vector2 boxSelectOriginInContent;
        private bool isBoxSelectArmed;
        private bool isBoxSelectActive;
        private bool isBoxSelectAdditive;

        /// <summary>Pointer travel, squared, before a press on empty lane space becomes a band rather than a click.</summary>
        private const float BoxSelectStartToleranceSquared = 9f;

        private bool inspectorRebuildPending;
        private bool timelineRebuildPending;

        private struct RegisteredLane
        {
            public CutsceneItemAddress laneAddress;
            public CutsceneMomentLaneElement momentLane;
            public CutsceneClipBlockLaneElement blockLane;

            public VisualElement Element
            {
                get { return momentLane != null ? (VisualElement)momentLane : blockLane; }
            }
        }

        private readonly CutscenePreviewController previewController = new CutscenePreviewController();
        private readonly CutsceneMarkSceneOverlay markSceneOverlay = new CutsceneMarkSceneOverlay();

        public CutsceneEditorPanel()
        {
            style.flexGrow = 1f;
            style.flexDirection = FlexDirection.Column;

            // G-D1: a scrub must never survive into a saved scene. Exiting before the write is the
            // only correct order — sceneSaving fires before the scene file is actually written.
            EditorSceneManager.sceneSaving += OnSceneSaving;
            focusable = true;
            RegisterCallback<KeyDownEvent>(OnPanelKeyDown);

            // Runs whether or not the transport is playing: it is what flushes a rebuild deferred
            // out of a live drag, so nothing may gate it on isPlaying.
            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                DestroyLeakedViewportGizmos();
                EditorApplication.update += OnEditorTick;
            });
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                EditorApplication.update -= OnEditorTick;
                EditorSceneManager.sceneSaving -= OnSceneSaving;
                AssemblyReloadEvents.beforeAssemblyReload -= DisposeViewportGizmo;
                StopPlayback();
                previewController.ExitPreview();
                DisposeViewportGizmo();
            });
            AssemblyReloadEvents.beforeAssemblyReload += DisposeViewportGizmo;

            Add(BuildToolbar());
            Add(BuildTransportRow());

            // The tab is a whole tool — cast | viewport | inspector over the timeline.
            VisualElement timelineArea = new VisualElement();
            timelineArea.style.flexDirection = FlexDirection.Column;
            timelineArea.style.minHeight = 120f;

            timelineArea.Add(BuildAddSlotRow());

            timelineArea.Add(BuildTimelineColumns());

            castPanel = new CutsceneCastPanel();
            castPanel.PlaceRequested += PlaceSlotFromPrefab;
            castPanel.BindRequested += BindSlotToObject;
            castPanel.SlotSelected += SelectSlotHeader;
            castPanel.FrameRequested += FrameSlotInSceneView;
            castPanel.SyncToStageRequested += SyncCutsceneToStage;

            VisualElement centerColumn = new VisualElement();
            centerColumn.style.flexGrow = 1f;
            centerColumn.style.flexDirection = FlexDirection.Row;
            centerColumn.Add(BuildViewportArea());

            inspectorScroll = new ScrollView(ScrollViewMode.Vertical);
            inspectorScroll.AddToClassList("clip-editor__inspector");
            inspectorScroll.style.width = 300f;
            inspectorScroll.style.flexShrink = 0f;
            centerColumn.Add(inspectorScroll);

            TwoPaneSplitView castSplit = new TwoPaneSplitView(0, 220f, TwoPaneSplitViewOrientation.Horizontal);
            castSplit.style.flexGrow = 1f;
            castSplit.Add(castPanel);
            castSplit.Add(centerColumn);

            VisualElement upperArea = new VisualElement();
            upperArea.style.flexGrow = 1f;
            upperArea.style.minHeight = 160f;
            upperArea.Add(castSplit);

            TwoPaneSplitView verticalSplit = new TwoPaneSplitView(1, 240f, TwoPaneSplitViewOrientation.Vertical);
            verticalSplit.style.flexGrow = 1f;
            verticalSplit.Add(upperArea);
            verticalSplit.Add(timelineArea);
            Add(verticalSplit);

            // Clicking the character in the Hierarchy or the Scene view lights its cast row and its
            // timeline group — the other half of "selection syncs both ways".
            Selection.selectionChanged += OnUnitySelectionChanged;
            RegisterCallback<DetachFromPanelEvent>(_ => Selection.selectionChanged -= OnUnitySelectionChanged);

            markSceneOverlay.MarkClicked += (slotIndex, markIndex) =>
                SelectItem(slotIndex, SelectedLaneKind.MarkKey, -1, markIndex);
            markSceneOverlay.MarkDragged += OnMarkDraggedInSceneView;
            // A duringSceneGui handler that outlives its panel draws against a disposed
            // SerializedObject and comes back after a domain reload with nothing behind it.
            RegisterCallback<DetachFromPanelEvent>(_ => markSceneOverlay.Disable());

            RestoreSessionCutscene();
            RebuildAll();
        }

        // -----------------------------------------------------------------------------------
        // Loading and the scene remember/open flow.
        // -----------------------------------------------------------------------------------

        private const string SessionCutsceneKey = "DotsAnimationToolkit.CutsceneEditor.OpenCutsceneGuid";

        /// <summary>
        /// The panel is destroyed and re-created on every domain reload (the window trap the
        /// AnimationToolkit notes document), so the open cutscene rides SessionState — without this
        /// the tab came back empty after any recompile, reading as a dead tool.
        /// </summary>
        private void RestoreSessionCutscene()
        {
            if (cutscene != null)
            {
                return;
            }
            string savedGuid = SessionState.GetString(SessionCutsceneKey, string.Empty);
            if (string.IsNullOrEmpty(savedGuid))
            {
                return;
            }
            string assetPath = AssetDatabase.GUIDToAssetPath(savedGuid);
            CutsceneAsset saved = string.IsNullOrEmpty(assetPath)
                ? null
                : AssetDatabase.LoadAssetAtPath<CutsceneAsset>(assetPath);
            if (saved != null)
            {
                cutscene = saved;
                serializedObject = new SerializedObject(cutscene);
                cutsceneField.SetValueWithoutNotify(cutscene);
            }
        }

        public void LoadCutscene(CutsceneAsset cutsceneAsset)
        {
            StopPlayback();
            previewController.ExitPreview();
            cutscene = cutsceneAsset;
            string cutsceneGuid = string.Empty;
            if (cutscene != null)
            {
                cutsceneGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(cutscene));
            }
            SessionState.SetString(SessionCutsceneKey, cutsceneGuid);
            serializedObject = cutscene != null ? new SerializedObject(cutscene) : null;
            selectedItems.Clear();
            primaryItem = null;
            selectedSlotIndex = -1;
            selectedLaneKind = SelectedLaneKind.None;
            selectedPartTrackIndex = -1;
            selectedItemIndex = -1;
            cutsceneField.SetValueWithoutNotify(cutscene);
            markSceneOverlay.SetSource(cutscene, serializedObject);
            markSceneOverlay.SetSelection(-1, -1);
            RebuildAll();
        }

        /// <summary>Called by <c>ClipEditorWindow.ShowCutsceneTab</c> when the tab is switched away from (G-D1: tab switch restores the preview).</summary>
        internal void OnHidden()
        {
            StopPlayback();
            previewController.ExitPreview();
            markSceneOverlay.Disable();
            DisposeViewportGizmo();
        }

        /// <summary>
        /// Creates a new <see cref="CutsceneAsset"/> wherever the user chooses, and loads it —
        /// mirroring <c>ClipEditorWindow.CreateClipSet</c>/<c>ClipAssetUtility.CreateClipSet</c>'s
        /// shape exactly: the location is asked for rather than guessed, and loading the new asset
        /// runs the same <see cref="LoadCutscene"/> path picking one by hand does.
        /// </summary>
        private void CreateCutsceneAsset()
        {
            string assetPath = EditorUtility.SaveFilePanelInProject(
                "Create Cutscene",
                "NewCutscene",
                "asset",
                "Choose where to save the new cutscene.");
            if (string.IsNullOrEmpty(assetPath))
            {
                return;
            }

            CutsceneAsset newCutscene = ScriptableObject.CreateInstance<CutsceneAsset>();
            newCutscene.EnsureStableIds();
            newCutscene.name = System.IO.Path.GetFileNameWithoutExtension(assetPath);

            AssetDatabase.CreateAsset(newCutscene, assetPath);
            AssetDatabase.SaveAssets();
            newCutscene.MarkStableIdPersisted();

            LoadCutscene(newCutscene);
            EditorGUIUtility.PingObject(newCutscene);
        }

        // The frozen header column. Both columns hold one entry per row at the same explicit
        // height, so nothing can drift them apart, and their vertical offsets are mirrored.
        private VisualElement BuildTimelineColumns()
        {
            VisualElement columns = new VisualElement();
            columns.style.flexDirection = FlexDirection.Row;
            columns.style.flexGrow = 1f;

            timelineHeaderScroll = new ScrollView(ScrollViewMode.Vertical);
            timelineHeaderScroll.style.width = HeaderColumnWidth;
            timelineHeaderScroll.style.flexShrink = 0f;
            timelineHeaderScroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            timelineHeaderScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            columns.Add(timelineHeaderScroll);

            timelineLaneScroll = new ScrollView(ScrollViewMode.VerticalAndHorizontal);
            timelineLaneScroll.style.flexGrow = 1f;
            columns.Add(timelineLaneScroll);

            // Guarded with a flag rather than by unsubscribing: each assignment raises the other
            // scroller's own callback, and unsubscribing mid-notification loses later events.
            timelineLaneScroll.verticalScroller.valueChanged += scrollValue =>
            {
                if (isSyncingTimelineScroll)
                {
                    return;
                }
                isSyncingTimelineScroll = true;
                timelineHeaderScroll.verticalScroller.value = scrollValue;
                isSyncingTimelineScroll = false;
            };
            timelineHeaderScroll.verticalScroller.valueChanged += scrollValue =>
            {
                if (isSyncingTimelineScroll)
                {
                    return;
                }
                isSyncingTimelineScroll = true;
                timelineLaneScroll.verticalScroller.value = scrollValue;
                isSyncingTimelineScroll = false;
            };
            return columns;
        }

        private void OnSceneSaving(UnityEngine.SceneManagement.Scene scene, string path)
        {
            StopPlayback();
            previewController.ExitPreview();
        }

        private VisualElement BuildToolbar()
        {
            VisualElement toolbar = new VisualElement();
            toolbar.style.flexDirection = FlexDirection.Row;
            toolbar.style.paddingLeft = 6f;
            toolbar.style.paddingRight = 6f;
            toolbar.style.paddingTop = 4f;
            toolbar.style.paddingBottom = 4f;
            toolbar.style.alignItems = Align.Center;

            cutsceneField = new ObjectField("Cutscene")
            {
                objectType = typeof(CutsceneAsset),
                allowSceneObjects = false
            };
            cutsceneField.style.width = 320f;
            cutsceneField.RegisterValueChangedCallback(
                changeEvent => LoadCutscene(changeEvent.newValue as CutsceneAsset));
            toolbar.Add(cutsceneField);

            Button newCutsceneButton = new Button(CreateCutsceneAsset)
            {
                text = "New",
                tooltip = "Creates a new Cutscene asset wherever you choose, and loads it."
            };
            newCutsceneButton.style.marginLeft = 4f;
            toolbar.Add(newCutsceneButton);

            sceneStatusLabel = new Label(string.Empty);
            sceneStatusLabel.style.marginLeft = 12f;
            sceneStatusLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            toolbar.Add(sceneStatusLabel);

            sceneActionButton = new Button { text = string.Empty };
            sceneActionButton.style.marginLeft = 6f;
            sceneActionButton.style.display = DisplayStyle.None;
            toolbar.Add(sceneActionButton);

            zoomSlider = new Slider(
                "Zoom",
                CutsceneTimelineGeometry.MinimumPixelsPerSecond,
                CutsceneTimelineGeometry.MaximumPixelsPerSecond)
            { value = pixelsPerSecond };
            zoomSlider.style.width = 180f;
            zoomSlider.style.marginLeft = 16f;
            zoomSlider.labelElement.style.minWidth = 38f;
            zoomSlider.RegisterValueChangedCallback(changeEvent =>
            {
                pixelsPerSecond = changeEvent.newValue;
                RebuildTimeline();
            });
            toolbar.Add(zoomSlider);

            Button keyButton = new Button(KeySelection)
            {
                text = " Key",
                tooltip = "Keys the selected slot's (or part track's) current live transform at the "
                    + "playhead — move it with Unity's own gizmo first."
            };
            keyButton.style.marginLeft = 16f;
            keyButton.style.flexDirection = FlexDirection.Row;
            keyButton.style.alignItems = Align.Center;
            Image keyIcon = new Image
            {
                image = EditorGUIUtility.IconContent("d_Animation.Record").image,
                pickingMode = PickingMode.Ignore
            };
            keyIcon.AddToClassList("cutscene-editor__transport-icon");
            keyButton.Insert(0, keyIcon);
            toolbar.Add(keyButton);

            toolbar.Add(BuildAutoKeyToggle());

            // Off by default: the in-tab viewport's Shot mode shows the framed movie, so yanking the
            // author's Scene view camera around on every scrub is opt-in.
            previewShotToggle = new Toggle { text = "Drive Scene View", value = false };
            previewShotToggle.style.marginLeft = 16f;
            previewShotToggle.tooltip =
                "Also move the Scene view's own camera to the cutscene camera lane's pose while "
                + "scrubbing. The tab's viewport shows the shot regardless.";
            previewShotToggle.RegisterValueChangedCallback(changeEvent =>
            {
                if (changeEvent.newValue)
                {
                    previewController.ApplyCameraPose(cutscene, playheadSeconds);
                }
            });
            toolbar.Add(previewShotToggle);

            return toolbar;
        }

        // -----------------------------------------------------------------------------------
        // Editor play transport. A rehearsal of runtime pacing, holds included.
        // -----------------------------------------------------------------------------------

        private static Button MakeTransportButton(Action onClick, string iconName, string tooltip, out Image icon)
        {
            Button button = new Button(onClick) { tooltip = tooltip };
            button.AddToClassList("cutscene-editor__transport-button");
            icon = new Image { image = EditorGUIUtility.IconContent(iconName).image };
            icon.AddToClassList("cutscene-editor__transport-icon");
            icon.pickingMode = PickingMode.Ignore;
            button.Add(icon);
            return button;
        }

        private VisualElement BuildTransportRow()
        {
            VisualElement row = new VisualElement();
            row.AddToClassList("cutscene-editor__transport");

            Image discardedIcon;
            row.Add(MakeTransportButton(
                () => SetPlayhead(0f), "d_Animation.FirstKey", "Go to start.", out discardedIcon));

            playToggleButton = MakeTransportButton(
                TogglePlayback, "d_PlayButton", "Play / pause the cutscene in the viewport.",
                out playToggleIcon);
            row.Add(playToggleButton);

            row.Add(MakeTransportButton(
                StopPlayback, "d_StopButton",
                "Stops and returns the playhead to where Play was pressed.", out discardedIcon));

            row.Add(MakeTransportButton(
                () => SetPlayhead(ComputeContentEndSecondsSafe()), "d_Animation.LastKey",
                "Go to end.", out discardedIcon));

            timeReadoutLabel = new Label("0.00 / 0.00 s");
            timeReadoutLabel.AddToClassList("cutscene-editor__time-readout");
            row.Add(timeReadoutLabel);

            continueButton = new Button(ReleaseHold) { text = "Continue ▶" };
            continueButton.tooltip = "Releases the hold the transport is waiting on, the way a host "
                + "releases it at run time.";
            continueButton.style.display = DisplayStyle.None;
            continueButton.style.marginLeft = 8f;
            row.Add(continueButton);

            // Beside the button that acts on it, not at the far end of the row past Speed and two
            // toggles: the owner watched a cutscene stop dead at a cue and saw nothing say so,
            // because the sentence explaining the stop sat 350px away from the Continue it explains.
            transportStatusLabel = new Label(string.Empty);
            transportStatusLabel.AddToClassList("cutscene-editor__transport-status");
            row.Add(transportStatusLabel);

            speedField = new FloatField("Speed") { value = playbackSpeed };
            speedField.style.width = 96f;
            speedField.style.marginLeft = 12f;
            speedField.labelElement.style.minWidth = 42f;
            speedField.RegisterValueChangedCallback(
                changeEvent => playbackSpeed = Mathf.Max(0f, changeEvent.newValue));
            row.Add(speedField);

            // text, not the label parameter: a labeled Toggle carries an inspector's ~150px label
            // column, which is what scattered these controls across the row in the first build.
            loopPlaybackToggle = new Toggle { text = "Loop", value = false };
            loopPlaybackToggle.style.marginLeft = 8f;
            loopPlaybackToggle.tooltip = "Restart from the top on reaching the end, for rehearsing a beat.";
            row.Add(loopPlaybackToggle);

            skipHoldsToggle = new Toggle { text = "Skip Holds", value = false };
            skipHoldsToggle.style.marginLeft = 8f;
            skipHoldsToggle.tooltip = "Run straight through hold markers instead of waiting for Continue.";
            row.Add(skipHoldsToggle);

            return row;
        }

        private float ComputeContentEndSecondsSafe()
        {
            return cutscene != null ? ComputeContentEndSeconds() : 0f;
        }

        /// <summary>Cheap per-move text update; fixed-format so the row never re-lays-out.</summary>
        private void RefreshTimeReadout()
        {
            if (timeReadoutLabel == null)
            {
                return;
            }
            float contentEnd = ComputeContentEndSecondsSafe();
            timeReadoutLabel.text = playheadSeconds.ToString("0.00") + " / "
                + contentEnd.ToString("0.00") + " s";
        }

        private void TogglePlayback()
        {
            if (isPlaying)
            {
                SetPlaying(false);
                return;
            }
            if (cutscene == null)
            {
                return;
            }
            prePlayPlayheadSeconds = playheadSeconds;
            previewController.HoldClipPhaseSeconds = 0f;
            isGatingOnHold = false;
            SetPlaying(true);
        }

        private void StopPlayback()
        {
            bool wasRunning = isPlaying || isGatingOnHold;
            SetPlaying(false);
            isGatingOnHold = false;
            previewController.HoldClipPhaseSeconds = 0f;
            if (wasRunning)
            {
                SetPlayhead(prePlayPlayheadSeconds);
            }
            RefreshTransportStatus();
        }

        private void SetPlaying(bool playing)
        {
            if (playing == isPlaying)
            {
                return;
            }
            isPlaying = playing;
            if (playing)
            {
                lastTickTime = EditorApplication.timeSinceStartup;
                EditorApplication.update += Tick;
            }
            else
            {
                EditorApplication.update -= Tick;
            }
            if (playToggleIcon != null)
            {
                playToggleIcon.image = EditorGUIUtility
                    .IconContent(playing ? "d_PauseButton" : "d_PlayButton").image;
            }
            RefreshTransportStatus();
        }

        private void ReleaseHold()
        {
            if (!isGatingOnHold)
            {
                return;
            }
            // Stepping a hair past the marker so the same hold is not re-detected on the next tick.
            isGatingOnHold = false;
            playheadSeconds += HoldReleaseEpsilon;
            RefreshTransportStatus();
        }

        /// <summary>Nudge past a released hold, well under one frame at any sane speed.</summary>
        private const float HoldReleaseEpsilon = 1e-3f;

        /// <summary>Turns the transport's status line into a banner while the clock is stopped on a hold.</summary>
        private const string HoldingStatusUssClassName = "cutscene-editor__transport-status--holding";

        // The panel's own heartbeat, separate from the transport's: it runs while the tab is open
        // whether or not anything is playing.
        private void OnEditorTick()
        {
            FlushDeferredPaneRebuilds();
            AutoKeyTick();
        }

        // One transport frame: advance the elastic clock, stop dead on a hold, and re-pose. A hold
        // freezes the cutscene clock, not the actors — matching the runtime, where every layer keeps
        // advancing while the timeline sits paused, so a looping walk keeps cycling.
        private void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            float elapsed = (float)(now - lastTickTime);
            lastTickTime = now;

            if (cutscene == null || !previewController.IsActive)
            {
                SetPlaying(false);
                return;
            }

            if (isGatingOnHold)
            {
                previewController.HoldClipPhaseSeconds += elapsed * playbackSpeed;
                ApplyPreviewAtPlayhead();
                return;
            }

            float advancedTime = playheadSeconds + elapsed * playbackSpeed;

            EffectiveHold crossedHold = default;
            bool crossedAHold = (skipHoldsToggle == null || !skipHoldsToggle.value)
                && TryFindFirstHoldCrossed(playheadSeconds, advancedTime, out crossedHold);
            if (crossedAHold)
            {
                gatingHold = crossedHold;
                isGatingOnHold = true;
                SetPlayhead(crossedHold.time);
                RefreshTransportStatus();
                return;
            }

            float contentEnd = ComputeContentEndSeconds();
            if (advancedTime >= contentEnd)
            {
                if (loopPlaybackToggle != null && loopPlaybackToggle.value)
                {
                    // A fresh play: the actors' clocks restart with the timeline's.
                    previewController.HoldClipPhaseSeconds = 0f;
                    SetPlayhead(0f);
                    return;
                }
                SetPlayhead(contentEnd);
                SetPlaying(false);
                return;
            }

            SetPlayhead(advancedTime);
        }

        // One point where the transport stops: an authored hold marker, or the hold a holding event
        // derives. The bake merges the two the same way, so the transport has to rehearse both.
        private struct EffectiveHold
        {
            public float time;
            public string holdId;
            public bool autoReleaseWhenMarksReached;

            /// <summary>True for a hold an event implies, which has no <see cref="CutsceneAsset.holdMarkers"/> row of its own.</summary>
            public bool isDerivedFromEvent;
        }

        /// <summary>Authored holds plus every holding event's derived hold, ascending by time.</summary>
        private List<EffectiveHold> BuildEffectiveHolds()
        {
            List<EffectiveHold> effectiveHolds = new List<EffectiveHold>();
            if (cutscene == null)
            {
                return effectiveHolds;
            }

            if (cutscene.holdMarkers != null)
            {
                for (int holdIndex = 0; holdIndex < cutscene.holdMarkers.Count; holdIndex++)
                {
                    CutsceneHoldMarker holdMarker = cutscene.holdMarkers[holdIndex];
                    if (holdMarker == null)
                    {
                        continue;
                    }
                    effectiveHolds.Add(new EffectiveHold
                    {
                        time = holdMarker.time,
                        holdId = holdMarker.holdId,
                        autoReleaseWhenMarksReached = holdMarker.autoReleaseWhenMarksReached
                    });
                }
            }

            List<CutsceneDerivedHolds.DerivedHold> derivedHolds = CutsceneDerivedHolds.Collect(cutscene);
            for (int derivedIndex = 0; derivedIndex < derivedHolds.Count; derivedIndex++)
            {
                // The bake drops a derived hold that lands on an authored one, so the rehearsal
                // must too, or the transport would stop twice where playback stops once.
                if (HasAuthoredHoldAt(derivedHolds[derivedIndex].time))
                {
                    continue;
                }
                effectiveHolds.Add(new EffectiveHold
                {
                    time = derivedHolds[derivedIndex].time,
                    holdId = derivedHolds[derivedIndex].holdId,
                    isDerivedFromEvent = true
                });
            }

            effectiveHolds.Sort((left, right) => left.time.CompareTo(right.time));
            return effectiveHolds;
        }

        private bool HasAuthoredHoldAt(float timeSeconds)
        {
            if (cutscene.holdMarkers == null)
            {
                return false;
            }
            for (int holdIndex = 0; holdIndex < cutscene.holdMarkers.Count; holdIndex++)
            {
                CutsceneHoldMarker holdMarker = cutscene.holdMarkers[holdIndex];
                if (holdMarker != null && Mathf.Abs(holdMarker.time - timeSeconds) <= HoldMergeEpsilon)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Matches <c>CutsceneBlobBuilder</c>'s own boundary epsilon, so editor and bake merge the same holds.</summary>
        private const float HoldMergeEpsilon = 1e-5f;

        /// <summary>The first hold strictly after <paramref name="fromSeconds"/> and at or before <paramref name="toSeconds"/>.</summary>
        private bool TryFindFirstHoldCrossed(float fromSeconds, float toSeconds, out EffectiveHold crossedHold)
        {
            crossedHold = default;
            bool found = false;
            List<EffectiveHold> effectiveHolds = BuildEffectiveHolds();
            for (int holdIndex = 0; holdIndex < effectiveHolds.Count; holdIndex++)
            {
                EffectiveHold effectiveHold = effectiveHolds[holdIndex];
                if (effectiveHold.time <= fromSeconds || effectiveHold.time > toSeconds)
                {
                    continue;
                }
                if (RendezvousIsSatisfiedAt(effectiveHold))
                {
                    continue;
                }
                if (!found || effectiveHold.time < crossedHold.time)
                {
                    crossedHold = effectiveHold;
                    found = true;
                }
            }
            return found;
        }

        /// <summary>
        /// Whether a rendezvous hold has nothing left to wait for by its own time (3.4). In
        /// rehearsal, arrival IS timeline time - the merged arrival key is the walk - so a hold
        /// every mark issued before it has already arrived at simply plays through, the way the
        /// runtime's own auto-release would. A mark that is still walking gates the transport and
        /// waits for Continue, and the bake warns about that shape as well.
        /// </summary>
        private bool RendezvousIsSatisfiedAt(in EffectiveHold effectiveHold)
        {
            if (!effectiveHold.autoReleaseWhenMarksReached || cutscene.slots == null)
            {
                return false;
            }
            for (int slotIndex = 0; slotIndex < cutscene.slots.Count; slotIndex++)
            {
                CutsceneSlot slot = cutscene.slots[slotIndex];
                if (slot == null || slot.markKeys == null)
                {
                    continue;
                }
                for (int markIndex = 0; markIndex < slot.markKeys.Count; markIndex++)
                {
                    CutsceneMarkKey mark = slot.markKeys[markIndex];
                    if (mark.time <= effectiveHold.time
                        && CutsceneMarkMerge.ArrivalTime(mark) > effectiveHold.time + HoldReleaseEpsilon)
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        // Moves the playhead and re-poses, without rebuilding the timeline. The playhead element
        // repaints itself from its own TimeSeconds; rebuilding every lane each frame would churn the
        // editor on a long vignette.
        private void SetPlayhead(float timeSeconds)
        {
            playheadSeconds = Mathf.Max(0f, timeSeconds);
            if (playheadElement != null)
            {
                playheadElement.TimeSeconds = playheadSeconds;
            }
            RefreshTimeReadout();
            ApplyPreviewAtPlayhead();
        }

        private void RefreshTransportStatus()
        {
            if (transportStatusLabel == null)
            {
                return;
            }

            if (isGatingOnHold && cutscene != null)
            {
                string holdId = gatingHold.holdId;
                transportStatusLabel.text =
                    "⏸ Holding on '" + (string.IsNullOrEmpty(holdId) ? "(unnamed hold)" : holdId) + "'"
                    + (gatingHold.isDerivedFromEvent ? " — the event cue fired." : ".");
                transportStatusLabel.EnableInClassList(HoldingStatusUssClassName, true);
                continueButton.style.display = DisplayStyle.Flex;
                return;
            }

            // No running time in the label: it would rebuild this row's layout every tick, and the
            // playhead already says where the clock is.
            continueButton.style.display = DisplayStyle.None;
            transportStatusLabel.EnableInClassList(HoldingStatusUssClassName, false);
            transportStatusLabel.text = isPlaying ? "Playing" : string.Empty;
        }

        // Keys the current live pose of whatever is selected — a slot's root, or a part track — at
        // the playhead. Requires the preview to be active (the remembered scene open and the slot
        // bound), the same gate BuildSceneBindingRow already shows a note for.
        private void KeySelection()
        {
            if (cutscene == null || !previewController.IsActive || selectedSlotIndex < 0
                || selectedSlotIndex >= cutscene.slots.Count)
            {
                return;
            }

            CutsceneSlot slot = cutscene.slots[selectedSlotIndex];
            SerializedProperty slotProperty =
                serializedObject.FindProperty("slots").GetArrayElementAtIndex(selectedSlotIndex);

            bool keyed;
            if ((selectedLaneKind == SelectedLaneKind.PartTrackHeader || selectedLaneKind == SelectedLaneKind.PartTrackKey)
                && selectedPartTrackIndex >= 0 && selectedPartTrackIndex < slot.partTracks.Count)
            {
                SerializedProperty keysProperty = slotProperty.FindPropertyRelative("partTracks")
                    .GetArrayElementAtIndex(selectedPartTrackIndex).FindPropertyRelative("keys");
                keyed = previewController.TryKeyPartTrack(
                    serializedObject, keysProperty, slot, slot.partTracks[selectedPartTrackIndex], playheadSeconds);
            }
            else
            {
                SerializedProperty keysProperty = slotProperty.FindPropertyRelative("transformKeys");
                keyed = previewController.TryKeyRoot(serializedObject, keysProperty, slot, playheadSeconds);
            }

            if (keyed)
            {
                serializedObject.Update();
                RebuildAll();
            }
        }

        private VisualElement BuildAddSlotRow()
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.paddingLeft = 6f;
            row.style.paddingTop = 2f;
            row.style.paddingBottom = 2f;

            Button addActorButton = new Button(() => AddSlot(CutsceneSlotKind.Actor)) { text = "+ Actor Slot" };
            row.Add(addActorButton);

            Button addPropButton = new Button(() => AddSlot(CutsceneSlotKind.Prop)) { text = "+ Prop Slot" };
            addPropButton.style.marginLeft = 4f;
            row.Add(addPropButton);

            return row;
        }

        // -----------------------------------------------------------------------------------
        // The in-tab scene viewport.
        // -----------------------------------------------------------------------------------

        private VisualElement BuildViewportArea()
        {
            VisualElement container = new VisualElement();
            container.style.flexGrow = 1f;
            container.style.position = Position.Relative;
            container.style.minWidth = 160f;

            viewportElement = new CutsceneViewportElement();
            viewportElement.style.flexGrow = 1f;
            viewportElement.NavigationBrokeShot += OnViewportNavigationBrokeShot;
            viewportElement.NavigationChangedCamera += RenderViewport;
            viewportElement.Clicked += OnViewportClicked;
            viewportElement.tryClaimPress = TryBeginViewportGizmoDrag;
            viewportElement.ClaimedPressDragged += ContinueViewportGizmoDrag;
            viewportElement.ClaimedPressReleased += EndViewportGizmoDrag;
            viewportElement.AboutToRender += DrawViewportGizmoForCamera;
            viewportElement.RegisterCallback<GeometryChangedEvent>(_ => RenderViewport());
            viewportElement.RegisterCallback<KeyDownEvent>(keyEvent =>
            {
                if (keyEvent.keyCode == KeyCode.F)
                {
                    FrameViewportOnCast();
                    keyEvent.StopPropagation();
                }
            });
            container.Add(viewportElement);

            VisualElement controlStrip = new VisualElement();
            controlStrip.AddToClassList("cutscene-editor__viewport-controls");
            controlStrip.style.position = Position.Absolute;
            controlStrip.style.top = 4f;
            controlStrip.style.right = 4f;
            controlStrip.style.flexDirection = FlexDirection.Row;

            shotModeToggle = new Toggle { text = "Shot", value = viewportShotMode };
            shotModeToggle.tooltip = "Locked to the camera lane — the viewport shows the framed movie. "
                + "Drag in the viewport (or turn this off) for a free orbit camera.";
            shotModeToggle.RegisterValueChangedCallback(changeEvent =>
            {
                viewportShotMode = changeEvent.newValue;
                if (!viewportShotMode)
                {
                    viewportElement.AdoptRenderedPoseAsFreeRig();
                }
                RenderViewport();
            });
            controlStrip.Add(shotModeToggle);

            Button frameButton = new Button(FrameViewportOnCast) { text = "Frame" };
            frameButton.tooltip = "Frames the bound cast (or the selected slot) in the viewport. Shortcut: F.";
            frameButton.style.marginLeft = 4f;
            controlStrip.Add(frameButton);
            container.Add(controlStrip);

            viewportOverlay = new VisualElement();
            viewportOverlay.AddToClassList("cutscene-editor__viewport-overlay");
            viewportOverlay.style.position = Position.Absolute;
            viewportOverlay.style.left = 0f;
            viewportOverlay.style.right = 0f;
            viewportOverlay.style.top = 0f;
            viewportOverlay.style.bottom = 0f;
            viewportOverlay.style.alignItems = Align.Center;
            viewportOverlay.style.justifyContent = Justify.Center;
            viewportOverlay.style.display = DisplayStyle.None;

            viewportMessageLabel = new Label(string.Empty);
            viewportMessageLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            viewportMessageLabel.style.marginBottom = 6f;
            viewportOverlay.Add(viewportMessageLabel);

            viewportActionButton = new Button(OnSceneActionButtonClicked) { text = string.Empty };
            viewportActionButton.style.display = DisplayStyle.None;
            viewportOverlay.Add(viewportActionButton);
            container.Add(viewportOverlay);

            return container;
        }

        // Bounds-based over the bound cast only, never Physics.Raycast: cutscene parts carry no
        // colliders, and the cast is a handful of objects rather than a whole scene.
        private void OnViewportClicked(Vector2 localPosition, bool keepsExistingSelection)
        {
            if (cutscene == null || cutscene.slots == null || viewportElement == null)
            {
                return;
            }
            Ray pickRay;
            if (!viewportElement.TryBuildPickRay(localPosition, out pickRay))
            {
                return;
            }

            int nearestSlotIndex = -1;
            float nearestDistance = float.MaxValue;
            List<PreviewPickHit> hits = new List<PreviewPickHit>();
            for (int slotIndex = 0; slotIndex < cutscene.slots.Count; slotIndex++)
            {
                CutsceneSlot slot = cutscene.slots[slotIndex];
                GameObject boundObject = slot == null
                    ? null
                    : previewController.GetBoundObject(slot.SlotId);
                if (boundObject == null)
                {
                    continue;
                }
                PreviewScenePicker.CollectHits(boundObject.transform, null, 0f, pickRay, hits);
                for (int hitIndex = 0; hitIndex < hits.Count; hitIndex++)
                {
                    if (hits[hitIndex].distance < nearestDistance)
                    {
                        nearestDistance = hits[hitIndex].distance;
                        nearestSlotIndex = slotIndex;
                    }
                }
            }

            if (nearestSlotIndex < 0)
            {
                if (!keepsExistingSelection)
                {
                    ClearSelection();
                }
                return;
            }

            // A modifier keeps whatever items are selected in the timeline and only moves the slot,
            // so picking an actor in the viewport does not throw away a beat under construction.
            if (keepsExistingSelection)
            {
                SelectSlotHeaderKeepingItems(nearestSlotIndex);
                return;
            }
            SelectSlotHeader(nearestSlotIndex);
        }

        /// <summary>Drops every selection this panel holds, including the slot.</summary>
        private void ClearSelection()
        {
            selectedItems.Clear();
            primaryItem = null;
            selectedSlotIndex = -1;
            selectedLaneKind = SelectedLaneKind.None;
            selectedPartTrackIndex = -1;
            selectedItemIndex = -1;
            markSceneOverlay.SetSelection(-1, -1);
            RequestTimelineRebuild();
            RequestInspectorRebuild();
            RefreshCastPanel();
        }

        private void SelectSlotHeaderKeepingItems(int slotIndex)
        {
            selectedSlotIndex = slotIndex;
            SyncSceneSelectionToTimelineSelection();
            RequestTimelineRebuild();
            RequestInspectorRebuild();
            RefreshCastPanel();
        }

        private void OnViewportNavigationBrokeShot()
        {
            viewportShotMode = false;
            if (shotModeToggle != null)
            {
                shotModeToggle.SetValueWithoutNotify(false);
            }
        }

        /// <summary>
        /// Renders the open scene into the tab. Shot mode samples the camera lane at the playhead;
        /// Free (or a cutscene with no camera keys yet) renders the orbit rig.
        /// </summary>
        private void RenderViewport()
        {
            if (viewportElement == null)
            {
                return;
            }

            RefreshViewportGizmo();

            bool hasCameraKeys = cutscene != null && cutscene.cameraLane?.keys != null
                && cutscene.cameraLane.keys.Count > 0;
            if (viewportShotMode && hasCameraKeys)
            {
                float3 sampledPosition;
                float3 sampledEulerDegrees;
                float fieldOfView;
                bool isCut;
                CutsceneKeySampler.SampleCameraWithCuts(
                    cutscene.cameraLane.keys, cutscene.cameraLane.cutMarkers, playheadSeconds,
                    out sampledPosition, out sampledEulerDegrees, out fieldOfView, out isCut);
                Vector3 position = new Vector3(sampledPosition.x, sampledPosition.y, sampledPosition.z);
                Quaternion rotation = Quaternion.Euler(sampledEulerDegrees.x, sampledEulerDegrees.y, sampledEulerDegrees.z);
                viewportElement.IsShowingShotPose = true;
                viewportElement.RenderShot(position, rotation, fieldOfView);
                return;
            }

            viewportElement.IsShowingShotPose = false;
            viewportElement.RenderFree();
        }

        // The viewport hosts its own state instead of a toolbar warning nobody reads: no cutscene,
        // no remembered scene, or the wrong scene open all land here.
        private void RefreshViewportOverlay()
        {
            if (viewportOverlay == null)
            {
                return;
            }

            string message = null;
            string action = null;
            if (cutscene == null)
            {
                message = "No cutscene loaded.\nPick one above, or create a new one.";
            }
            else if (string.IsNullOrEmpty(cutscene.sceneGuid))
            {
                message = "This cutscene has no scene yet.";
                action = "Remember Current Scene";
            }
            else if (CutsceneSceneBinding.CurrentSceneGuid() != cutscene.sceneGuid)
            {
                message = "This cutscene plays in\n" + cutscene.scenePath + ".";
                action = "Open Scene";
            }

            if (message == null)
            {
                viewportOverlay.style.display = DisplayStyle.None;
                return;
            }
            viewportOverlay.style.display = DisplayStyle.Flex;
            viewportMessageLabel.text = message;
            viewportActionButton.text = action ?? string.Empty;
            viewportActionButton.style.display = action != null ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>Frames the selected slot's bound object if there is one, else the whole bound cast.</summary>
        private void FrameViewportOnCast()
        {
            if (viewportElement == null || cutscene == null || cutscene.slots == null)
            {
                return;
            }

            bool hasBounds = false;
            Bounds framingBounds = new Bounds();
            if (selectedSlotIndex >= 0 && selectedSlotIndex < cutscene.slots.Count)
            {
                GameObject selectedObject = previewController.GetBoundObject(cutscene.slots[selectedSlotIndex].SlotId);
                if (selectedObject != null)
                {
                    framingBounds = ComputeFramingBounds(selectedObject);
                    hasBounds = true;
                }
            }
            if (!hasBounds)
            {
                for (int slotIndex = 0; slotIndex < cutscene.slots.Count; slotIndex++)
                {
                    GameObject boundObject = previewController.GetBoundObject(cutscene.slots[slotIndex].SlotId);
                    if (boundObject == null)
                    {
                        continue;
                    }
                    Bounds slotBounds = ComputeFramingBounds(boundObject);
                    if (!hasBounds)
                    {
                        framingBounds = slotBounds;
                        hasBounds = true;
                        continue;
                    }
                    framingBounds.Encapsulate(slotBounds);
                }
            }

            if (hasBounds)
            {
                OnViewportNavigationBrokeShot();
                viewportElement.FrameBounds(framingBounds);
            }
        }

        private void RefreshSceneStatus()
        {
            if (cutscene == null)
            {
                sceneStatusLabel.text = string.Empty;
                sceneActionButton.style.display = DisplayStyle.None;
                return;
            }

            string currentGuid = CutsceneSceneBinding.CurrentSceneGuid();

            if (string.IsNullOrEmpty(cutscene.sceneGuid))
            {
                sceneStatusLabel.text = "No scene remembered.";
                sceneActionButton.text = "Remember Current Scene";
                sceneActionButton.style.display = DisplayStyle.Flex;
                sceneActionButton.clicked -= OnSceneActionButtonClicked;
                sceneActionButton.clicked += OnSceneActionButtonClicked;
                return;
            }

            if (currentGuid != cutscene.sceneGuid)
            {
                sceneStatusLabel.text = "Wrong scene open — expects " + cutscene.scenePath
                    + ". Timing edits still work.";
                sceneActionButton.text = "Open Scene";
                sceneActionButton.style.display = DisplayStyle.Flex;
                sceneActionButton.clicked -= OnSceneActionButtonClicked;
                sceneActionButton.clicked += OnSceneActionButtonClicked;
                return;
            }

            sceneStatusLabel.text = "Scene: " + cutscene.scenePath;
            sceneActionButton.style.display = DisplayStyle.None;
        }

        private void OnSceneActionButtonClicked()
        {
            if (cutscene == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(cutscene.sceneGuid))
            {
                SerializedProperty sceneGuidProperty = serializedObject.FindProperty("sceneGuid");
                SerializedProperty scenePathProperty = serializedObject.FindProperty("scenePath");
                sceneGuidProperty.stringValue = CutsceneSceneBinding.CurrentSceneGuid();
                scenePathProperty.stringValue = CutsceneSceneBinding.CurrentScenePath();
                serializedObject.ApplyModifiedProperties();
            }
            else
            {
                CutsceneSceneBinding.TryOpenScene(cutscene.scenePath);
            }

            RefreshSceneStatus();
            RebuildInspector();
        }

        // -----------------------------------------------------------------------------------
        // Structural commits — every add/remove goes through here (see class remarks).
        // -----------------------------------------------------------------------------------

        private void CommitStructuralChange()
        {
            serializedObject.ApplyModifiedProperties();
            cutscene.EnsureStableIds();
            if (cutscene.HasUnpersistedStableId)
            {
                EditorUtility.SetDirty(cutscene);
                cutscene.MarkStableIdPersisted();
            }
            serializedObject.Update();
            RebuildAll();
            ApplyPreviewAtPlayhead();
        }

        private void RebuildAll()
        {
            RefreshSceneStatus();
            SyncPreviewActivation();
            SyncMarkOverlayActivation();
            RebuildTimeline();
            RebuildInspector();
            RefreshCastPanel();
            RefreshViewportOverlay();
            RenderViewport();
        }

        /// <summary>
        /// The marks overlay lives or dies by the same gate the preview does: a disc drawn over a
        /// scene the cutscene was not authored against is measuring nothing.
        /// </summary>
        private void SyncMarkOverlayActivation()
        {
            markSceneOverlay.SetSource(cutscene, serializedObject);
            bool shouldDraw = cutscene != null
                && !string.IsNullOrEmpty(cutscene.sceneGuid)
                && CutsceneSceneBinding.CurrentSceneGuid() == cutscene.sceneGuid;
            if (shouldDraw)
            {
                markSceneOverlay.Enable();
            }
            else
            {
                markSceneOverlay.Disable();
            }
        }

        /// <summary>
        /// A Scene-view drag writes straight to the asset; this pulls the change back through the
        /// bound inspector fields live and rebuilds the rest only once the drag ends - a rebuild per
        /// pointer move is exactly the churn A58 6 warned about.
        /// </summary>
        private void OnMarkDraggedInSceneView(bool isFinished)
        {
            if (isFinished)
            {
                serializedObject.Update();
                RebuildAll();
            }
            ApplyPreviewAtPlayhead();
        }

        /// <summary>Enters preview the moment the remembered scene is the open one, and exits it the moment it is not.</summary>
        private void SyncPreviewActivation()
        {
            if (cutscene == null)
            {
                previewController.ExitPreview();
                return;
            }

            string currentSceneGuid = CutsceneSceneBinding.CurrentSceneGuid();
            bool shouldBeActive = !string.IsNullOrEmpty(cutscene.sceneGuid) && currentSceneGuid == cutscene.sceneGuid;

            if (shouldBeActive && !previewController.IsActive)
            {
                // The in-tab viewport is the primary surface; a Scene view is only required when
                // the author opted into driving its camera.
                if (previewShotToggle != null && previewShotToggle.value)
                {
                    EnsureSceneViewIsOpen();
                }
                previewController.EnterPreview(cutscene, currentSceneGuid);
                FrameCastOnFirstEnter();
                FrameViewportFreeRigOnCast();
                // After the framing, never before: a cutscene with camera keys wants its own shot,
                // and ApplyCameraPose is what puts the view there.
                ApplyPreviewAtPlayhead();
            }
            else if (!shouldBeActive && previewController.IsActive)
            {
                StopPlayback();
                previewController.ExitPreview();
            }
        }

        /// <summary>Makes sure there is a Scene view to preview into.</summary>
        private static void EnsureSceneViewIsOpen()
        {
            if (SceneView.lastActiveSceneView != null)
            {
                return;
            }
            if (SceneView.sceneViews != null && SceneView.sceneViews.Count > 0)
            {
                ((SceneView)SceneView.sceneViews[0]).Focus();
                return;
            }
            EditorWindow.GetWindow<SceneView>();
        }

        /// <summary>Frames every bound cast member once per preview session, so the actors are on screen to begin with.</summary>
        private void FrameCastOnFirstEnter()
        {
            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null || cutscene.slots == null)
            {
                return;
            }

            bool hasBounds = false;
            Bounds castBounds = new Bounds();
            for (int slotIndex = 0; slotIndex < cutscene.slots.Count; slotIndex++)
            {
                GameObject boundObject = previewController.GetBoundObject(cutscene.slots[slotIndex].SlotId);
                if (boundObject == null)
                {
                    continue;
                }
                Bounds slotBounds = ComputeFramingBounds(boundObject);
                if (!hasBounds)
                {
                    castBounds = slotBounds;
                    hasBounds = true;
                    continue;
                }
                castBounds.Encapsulate(slotBounds);
            }

            if (hasBounds)
            {
                sceneView.Frame(castBounds, true);
            }
        }

        /// <summary>Pre-points the viewport's free orbit rig at the bound cast without leaving Shot mode.</summary>
        private void FrameViewportFreeRigOnCast()
        {
            if (viewportElement == null || cutscene.slots == null)
            {
                return;
            }
            bool hasBounds = false;
            Bounds castBounds = new Bounds();
            for (int slotIndex = 0; slotIndex < cutscene.slots.Count; slotIndex++)
            {
                GameObject boundObject = previewController.GetBoundObject(cutscene.slots[slotIndex].SlotId);
                if (boundObject == null)
                {
                    continue;
                }
                Bounds slotBounds = ComputeFramingBounds(boundObject);
                if (!hasBounds)
                {
                    castBounds = slotBounds;
                    hasBounds = true;
                    continue;
                }
                castBounds.Encapsulate(slotBounds);
            }
            if (hasBounds)
            {
                viewportElement.FrameBounds(castBounds);
            }
        }

        // -----------------------------------------------------------------------------------
        // Slot management.
        // -----------------------------------------------------------------------------------

        private void AddSlot(CutsceneSlotKind kind)
        {
            if (serializedObject == null)
            {
                return;
            }

            SerializedProperty slotsProperty = serializedObject.FindProperty("slots");
            int index = slotsProperty.arraySize;
            slotsProperty.InsertArrayElementAtIndex(index);
            SerializedProperty newSlot = slotsProperty.GetArrayElementAtIndex(index);
            newSlot.FindPropertyRelative("name").stringValue =
                (kind == CutsceneSlotKind.Actor ? "Actor " : "Prop ") + (index + 1);
            newSlot.FindPropertyRelative("slotId").uintValue = 0u;
            newSlot.FindPropertyRelative("kind").enumValueIndex = (int)kind;
            newSlot.FindPropertyRelative("rig").objectReferenceValue = null;
            newSlot.FindPropertyRelative("clipSets").ClearArray();
            newSlot.FindPropertyRelative("directionSet").objectReferenceValue = null;
            newSlot.FindPropertyRelative("actorPrefab").objectReferenceValue = null;
            newSlot.FindPropertyRelative("clipBlocks").ClearArray();
            newSlot.FindPropertyRelative("transformKeys").ClearArray();
            newSlot.FindPropertyRelative("facingKeys").ClearArray();
            newSlot.FindPropertyRelative("partTracks").ClearArray();

            CommitStructuralChange();
            selectedSlotIndex = index;
            selectedLaneKind = SelectedLaneKind.None;
            RebuildInspector();
        }

        private void RemoveSlot(int slotIndex)
        {
            SerializedProperty slotsProperty = serializedObject.FindProperty("slots");
            if (slotIndex < 0 || slotIndex >= slotsProperty.arraySize)
            {
                return;
            }
            slotsProperty.DeleteArrayElementAtIndex(slotIndex);
            if (selectedSlotIndex == slotIndex)
            {
                selectedSlotIndex = -1;
                selectedLaneKind = SelectedLaneKind.None;
            }
            CommitStructuralChange();
        }

        private void SelectSlotHeader(int slotIndex)
        {
            selectedItems.Clear();
            primaryItem = null;
            selectedSlotIndex = slotIndex;
            selectedLaneKind = SelectedLaneKind.None;
            selectedPartTrackIndex = -1;
            selectedItemIndex = -1;
            SyncSceneSelectionToTimelineSelection();
            RequestTimelineRebuild();
            RequestInspectorRebuild();
            RefreshCastPanel();
        }

        // -----------------------------------------------------------------------------------
        // Cast panel: staging the scene from the tool.
        // -----------------------------------------------------------------------------------

        // Instantiates a slot's actor prefab at the Scene view pivot and binds it, as one Undo step.
        // A real scene edit, deliberately outside the preview's capture/restore: preview is exited
        // first so the new object is captured at its authored rest pose on re-entry, not mid-scrub.
        private void PlaceSlotFromPrefab(int slotIndex)
        {
            if (cutscene == null || slotIndex < 0 || slotIndex >= cutscene.slots.Count)
            {
                return;
            }
            CutsceneSlot slot = cutscene.slots[slotIndex];
            if (slot.actorPrefab == null)
            {
                return;
            }
            string currentSceneGuid = CutsceneSceneBinding.CurrentSceneGuid();
            if (string.IsNullOrEmpty(cutscene.sceneGuid) || currentSceneGuid != cutscene.sceneGuid)
            {
                return;
            }

            StopPlayback();
            previewController.ExitPreview();

            GameObject placed = PrefabUtility.InstantiatePrefab(slot.actorPrefab) as GameObject;
            if (placed == null)
            {
                return;
            }
            placed.name = slot.name;
            SceneView sceneView = SceneView.lastActiveSceneView;
            placed.transform.position = sceneView != null ? sceneView.pivot : Vector3.zero;
            Undo.RegisterCreatedObjectUndo(placed, "Place Cutscene Slot");

            CutsceneSceneBinding.SetBinding(serializedObject, currentSceneGuid, slot.SlotId, placed);
            serializedObject.Update();

            SetSelectedGameObject(placed);
            RebuildAll();
            FrameSlotInSceneView(slotIndex);
        }

        private void BindSlotToObject(int slotIndex, GameObject boundObject)
        {
            if (cutscene == null || slotIndex < 0 || slotIndex >= cutscene.slots.Count)
            {
                return;
            }
            string currentSceneGuid = CutsceneSceneBinding.CurrentSceneGuid();
            if (string.IsNullOrEmpty(currentSceneGuid))
            {
                return;
            }

            // Re-entered rather than patched: the controller captures rest poses on entry, and a
            // slot bound mid-preview would otherwise never have any.
            StopPlayback();
            previewController.ExitPreview();
            CutsceneSceneBinding.SetBinding(
                serializedObject, currentSceneGuid, cutscene.slots[slotIndex].SlotId, boundObject);
            serializedObject.Update();
            RebuildAll();
            ApplyPreviewAtPlayhead();
        }

        private void FrameSlotInSceneView(int slotIndex)
        {
            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null || cutscene == null || slotIndex < 0 || slotIndex >= cutscene.slots.Count)
            {
                return;
            }
            GameObject boundObject = previewController.GetBoundObject(cutscene.slots[slotIndex].SlotId);
            if (boundObject == null)
            {
                return;
            }
            sceneView.Frame(ComputeFramingBounds(boundObject), false);
        }

        /// <summary>The renderer bounds of an object and its children, or a small box at its position.</summary>
        private static Bounds ComputeFramingBounds(GameObject boundObject)
        {
            Renderer[] renderers = boundObject.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                return new Bounds(boundObject.transform.position, Vector3.one);
            }
            Bounds bounds = renderers[0].bounds;
            for (int rendererIndex = 1; rendererIndex < renderers.Length; rendererIndex++)
            {
                bounds.Encapsulate(renderers[rendererIndex].bounds);
            }
            return bounds;
        }

        /// <summary>Lights the cast row and slot group for whatever the author just picked in Unity's own views.</summary>
        private void OnUnitySelectionChanged()
        {
            if (isDrivingUnitySelection || cutscene == null || panel == null)
            {
                return;
            }
            int slotIndex = CutsceneCastPanel.FindSlotIndexForSelection(
                cutscene, CutsceneSceneBinding.CurrentSceneGuid(), Selection.activeGameObject);
            if (slotIndex < 0 || slotIndex == selectedSlotIndex)
            {
                return;
            }
            selectedSlotIndex = slotIndex;
            selectedLaneKind = SelectedLaneKind.None;
            selectedPartTrackIndex = -1;
            RebuildTimeline();
            RebuildInspector();
            RefreshCastPanel();
        }

        private void SetSelectedGameObject(GameObject target)
        {
            isDrivingUnitySelection = true;
            Selection.activeGameObject = target;
            isDrivingUnitySelection = false;
        }

        private void RefreshCastPanel()
        {
            if (castPanel == null)
            {
                return;
            }
            string currentSceneGuid = CutsceneSceneBinding.CurrentSceneGuid();
            castPanel.Rebuild(cutscene, currentSceneGuid, selectedSlotIndex);
            castPanel.SetStageStatus(ComputeStageStatusText(currentSceneGuid));
        }

        // -----------------------------------------------------------------------------------
        // Sync to Stage: writes the cast panel's resolved bindings into this scene's
        // CutsceneStageAuthoring component, ready for CutsceneStageBaker to bake at the next
        // subscene reopen or Play. Explicit only, never triggered by a Bind/Place click.
        // -----------------------------------------------------------------------------------

        private void SyncCutsceneToStage()
        {
            if (cutscene == null)
            {
                return;
            }

            string currentSceneGuid = CutsceneSceneBinding.CurrentSceneGuid();
            List<KeyValuePair<uint, GameObject>> resolvedBindings = ResolveBoundSlotsForStage(currentSceneGuid);

            GameObject firstBoundObject = null;
            bool spansMultipleScenes = false;
            for (int bindingIndex = 0; bindingIndex < resolvedBindings.Count; bindingIndex++)
            {
                GameObject boundObject = resolvedBindings[bindingIndex].Value;
                if (firstBoundObject == null)
                {
                    firstBoundObject = boundObject;
                }
                else if (boundObject.scene != firstBoundObject.scene)
                {
                    spansMultipleScenes = true;
                }
            }

            CutsceneStageAuthoring stageAuthoring = FindStageAuthoringForCutscene(cutscene);
            if (stageAuthoring == null && firstBoundObject == null)
            {
                // Nothing bound and no stage to update — Stage stays "none".
                RefreshCastPanel();
                return;
            }

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Sync Cutscene To Stage");

            if (stageAuthoring == null)
            {
                GameObject stageGameObject = new GameObject("Cutscene Stage — " + cutscene.name);
                SceneManager.MoveGameObjectToScene(stageGameObject, firstBoundObject.scene);
                Undo.RegisterCreatedObjectUndo(stageGameObject, "Sync Cutscene To Stage");
                stageAuthoring = stageGameObject.AddComponent<CutsceneStageAuthoring>();
            }
            else
            {
                Undo.RecordObject(stageAuthoring, "Sync Cutscene To Stage");
            }

            SerializedObject stageSerializedObject = new SerializedObject(stageAuthoring);
            stageSerializedObject.FindProperty("cutscene").objectReferenceValue = cutscene;
            SerializedProperty bindingsProperty = stageSerializedObject.FindProperty("bindings");
            bindingsProperty.ClearArray();
            for (int bindingIndex = 0; bindingIndex < resolvedBindings.Count; bindingIndex++)
            {
                bindingsProperty.InsertArrayElementAtIndex(bindingIndex);
                SerializedProperty entryProperty = bindingsProperty.GetArrayElementAtIndex(bindingIndex);
                entryProperty.FindPropertyRelative("slotId").uintValue = resolvedBindings[bindingIndex].Key;
                entryProperty.FindPropertyRelative("target").objectReferenceValue = resolvedBindings[bindingIndex].Value;
            }
            stageSerializedObject.ApplyModifiedProperties();
            Undo.CollapseUndoOperations(undoGroup);

            EditorSceneManager.MarkSceneDirty(stageAuthoring.gameObject.scene);

            castPanel.Rebuild(cutscene, currentSceneGuid, selectedSlotIndex);
            castPanel.SetStageStatus(
                spansMultipleScenes ? "Stage: synced (bindings span multiple scenes)" : "Stage: synced");
        }

        /// <summary>Every bound slot resolved to its live GameObject, in the cutscene's own slot order — the same pairs both the sync write and the status check read.</summary>
        private List<KeyValuePair<uint, GameObject>> ResolveBoundSlotsForStage(string currentSceneGuid)
        {
            List<KeyValuePair<uint, GameObject>> resolved = new List<KeyValuePair<uint, GameObject>>();
            if (cutscene == null || cutscene.slots == null)
            {
                return resolved;
            }
            for (int slotIndex = 0; slotIndex < cutscene.slots.Count; slotIndex++)
            {
                CutsceneSlot slot = cutscene.slots[slotIndex];
                if (slot == null)
                {
                    continue;
                }
                CutsceneSlotBindingEntry entry =
                    CutsceneSceneBinding.FindBinding(cutscene, currentSceneGuid, slot.SlotId);
                if (entry == null || string.IsNullOrEmpty(entry.globalObjectId))
                {
                    continue;
                }
                GameObject boundObject = CutsceneSceneBinding.ResolveGameObject(entry.globalObjectId);
                if (boundObject != null)
                {
                    resolved.Add(new KeyValuePair<uint, GameObject>(slot.SlotId, boundObject));
                }
            }
            return resolved;
        }

        /// <summary>"Stage: none" / "Stage: synced" / "Stage: out of date" — recomputed every <see cref="RefreshCastPanel"/>.</summary>
        private string ComputeStageStatusText(string currentSceneGuid)
        {
            if (cutscene == null)
            {
                return string.Empty;
            }
            CutsceneStageAuthoring stageAuthoring = FindStageAuthoringForCutscene(cutscene);
            if (stageAuthoring == null)
            {
                return "Stage: none";
            }

            List<KeyValuePair<uint, GameObject>> resolvedBindings = ResolveBoundSlotsForStage(currentSceneGuid);
            bool matches = stageAuthoring.bindings != null
                && stageAuthoring.bindings.Count == resolvedBindings.Count;
            for (int bindingIndex = 0; matches && bindingIndex < resolvedBindings.Count; bindingIndex++)
            {
                if (!StagedBindingMatches(
                        stageAuthoring, resolvedBindings[bindingIndex].Key, resolvedBindings[bindingIndex].Value))
                {
                    matches = false;
                }
            }

            return matches ? "Stage: synced" : "Stage: out of date";
        }

        private static bool StagedBindingMatches(
            CutsceneStageAuthoring stageAuthoring, uint slotId, GameObject boundObject)
        {
            for (int bindingIndex = 0; bindingIndex < stageAuthoring.bindings.Count; bindingIndex++)
            {
                CutsceneStageSlotBinding binding = stageAuthoring.bindings[bindingIndex];
                if (binding != null && binding.slotId == slotId)
                {
                    return binding.target == boundObject;
                }
            }
            return false;
        }

        private static CutsceneStageAuthoring FindStageAuthoringForCutscene(CutsceneAsset cutscene)
        {
            CutsceneStageAuthoring[] allStageAuthorings =
                UnityEngine.Object.FindObjectsByType<CutsceneStageAuthoring>(FindObjectsInactive.Include);
            for (int stageIndex = 0; stageIndex < allStageAuthorings.Length; stageIndex++)
            {
                if (allStageAuthorings[stageIndex].cutscene == cutscene)
                {
                    return allStageAuthorings[stageIndex];
                }
            }
            return null;
        }

        // Selects whatever GameObject the current timeline selection corresponds to, so Unity's own
        // Move/Rotate/Scale gizmo is already on it — no custom gizmo drawing needed since preview
        // poses the real scene object, never a mirror.
        private void SyncSceneSelectionToTimelineSelection()
        {
            if (!previewController.IsActive || selectedSlotIndex < 0 || selectedSlotIndex >= cutscene.slots.Count)
            {
                return;
            }
            CutsceneSlot slot = cutscene.slots[selectedSlotIndex];
            GameObject target = previewController.GetBoundObject(slot.SlotId);

            if ((selectedLaneKind == SelectedLaneKind.PartTrackHeader || selectedLaneKind == SelectedLaneKind.PartTrackKey)
                && selectedPartTrackIndex >= 0 && selectedPartTrackIndex < slot.partTracks.Count && slot.rig != null)
            {
                Transform partTransform = previewController.GetBoundPartTransform(
                    slot.SlotId, slot.rig, slot.partTracks[selectedPartTrackIndex].tagId);
                if (partTransform != null)
                {
                    target = partTransform.gameObject;
                }
            }

            if (target != null)
            {
                SetSelectedGameObject(target);
            }
        }

        // -----------------------------------------------------------------------------------
        // Timeline: rows, ruler, playhead.
        // -----------------------------------------------------------------------------------

        private void RebuildTimeline()
        {
            Vector2 preservedScroll = timelineLaneScroll.scrollOffset;
            timelineLaneScroll.Clear();
            timelineHeaderScroll.Clear();
            registeredLanes.Clear();
            timelineContent = null;
            timelineHeaderContent = null;
            boxSelectElement = null;

            if (cutscene == null || serializedObject == null)
            {
                Label emptyHint = new Label(
                    "No cutscene loaded.\n\n"
                    + "Pick a Cutscene asset in the toolbar (or press New), then add Actor and Prop "
                    + "slots.\nDouble-click any lane to add a clip block or key at that time.");
                emptyHint.style.unityTextAlign = TextAnchor.MiddleCenter;
                emptyHint.style.whiteSpace = WhiteSpace.Normal;
                emptyHint.style.marginTop = 24f;
                emptyHint.style.color = new Color(0.62f, 0.62f, 0.66f);
                emptyHint.style.alignSelf = Align.Center;
                timelineLaneScroll.Add(emptyHint);
                return;
            }

            serializedObject.Update();

            float contentEnd = ComputeContentEndSeconds();
            float contentWidth = CutsceneTimelineGeometry
                .Create(pixelsPerSecond).TimeToX(contentEnd + TrailingSeconds);

            // A short cutscene must still fill the pane: a 240px ruler floating in a grey void was
            // the single worst thing about the first build (A60). Lanes always reach at least the
            // visible edge; NaN-guarded because the first rebuild runs before any layout pass.
            // The lane column's own viewport is already header-free, so nothing is subtracted here.
            float visibleWidth = timelineLaneScroll.contentViewport.resolvedStyle.width;
            if (!float.IsNaN(visibleWidth) && visibleWidth > 0f)
            {
                contentWidth = Mathf.Max(contentWidth, visibleWidth);
            }

            VisualElement headerContent = new VisualElement();
            headerContent.style.flexDirection = FlexDirection.Column;
            timelineHeaderContent = headerContent;

            VisualElement content = new VisualElement();
            content.style.flexDirection = FlexDirection.Column;
            content.style.position = Position.Relative;
            timelineContent = content;

            CutsceneTimelineRulerElement ruler = new CutsceneTimelineRulerElement
            {
                pixelsPerSecond = pixelsPerSecond,
                contentEndSeconds = contentEnd,
                trailingSeconds = TrailingSeconds
            };
            ruler.style.width = contentWidth;
            ruler.style.height = RulerHeight;
            ruler.Scrubbed += OnPlayheadScrubbed;
            AddTimelineRow(content, null, ruler, null, RulerHeight);
            RefreshTimeReadout();

            SerializedProperty slotsProperty = serializedObject.FindProperty("slots");
            for (int slotIndex = 0; slotIndex < slotsProperty.arraySize; slotIndex++)
            {
                BuildSlotRows(content, slotsProperty.GetArrayElementAtIndex(slotIndex), slotIndex, contentWidth);
            }

            BuildCameraRows(content, contentWidth);
            BuildEventRows(content, contentWidth);
            BuildHoldRows(content, contentWidth);

            playheadElement = new CutsceneTimelinePlayheadElement
            {
                pixelsPerSecond = pixelsPerSecond,
                TimeSeconds = playheadSeconds
            };
            playheadElement.style.position = Position.Absolute;
            playheadElement.style.left = 0f;
            playheadElement.style.top = 0f;
            playheadElement.style.bottom = 0f;
            playheadElement.style.width = contentWidth;
            content.Add(playheadElement);

            // Above the lanes and below the playhead, ignoring the pointer: the lane underneath owns
            // the band drag, this only draws it.
            boxSelectElement = new BoxSelectElement();
            boxSelectElement.style.position = Position.Absolute;
            boxSelectElement.style.left = 0f;
            boxSelectElement.style.right = 0f;
            boxSelectElement.style.top = 0f;
            boxSelectElement.style.bottom = 0f;
            content.Add(boxSelectElement);

            timelineHeaderScroll.Add(headerContent);
            timelineLaneScroll.Add(content);
            timelineLaneScroll.scrollOffset = preservedScroll;
        }

        private float ComputeContentEndSeconds()
        {
            float latest = 1f;
            if (cutscene.slots != null)
            {
                for (int slotIndex = 0; slotIndex < cutscene.slots.Count; slotIndex++)
                {
                    CutsceneSlot slot = cutscene.slots[slotIndex];
                    if (slot == null)
                    {
                        continue;
                    }
                    if (slot.clipBlocks != null)
                    {
                        for (int i = 0; i < slot.clipBlocks.Count; i++)
                        {
                            latest = Mathf.Max(latest, slot.clipBlocks[i].start + slot.clipBlocks[i].duration);
                        }
                    }
                    latest = Mathf.Max(latest, LatestTime(slot.transformKeys));
                    latest = Mathf.Max(latest, LatestTime(slot.facingKeys));
                    if (slot.partTracks != null)
                    {
                        for (int i = 0; i < slot.partTracks.Count; i++)
                        {
                            latest = Mathf.Max(latest, LatestTime(slot.partTracks[i].keys));
                        }
                    }
                    latest = Mathf.Max(latest, LatestTime(slot.attachMarkers));
                    latest = Mathf.Max(latest, LatestArrivalTime(slot.markKeys));
                }
            }
            latest = Mathf.Max(latest, LatestTime(cutscene.cameraLane?.keys));
            if (cutscene.cameraLane?.cutMarkers != null)
            {
                for (int i = 0; i < cutscene.cameraLane.cutMarkers.Count; i++)
                {
                    latest = Mathf.Max(latest, cutscene.cameraLane.cutMarkers[i].time);
                }
            }
            if (cutscene.events != null)
            {
                for (int i = 0; i < cutscene.events.Count; i++)
                {
                    latest = Mathf.Max(latest, cutscene.events[i].time);
                }
            }
            if (cutscene.holdMarkers != null)
            {
                for (int i = 0; i < cutscene.holdMarkers.Count; i++)
                {
                    latest = Mathf.Max(latest, cutscene.holdMarkers[i].time);
                }
            }
            return latest;
        }

        private static float LatestTime(List<CutsceneTransformKey> keys)
        {
            float latest = 0f;
            if (keys != null)
            {
                for (int i = 0; i < keys.Count; i++)
                {
                    latest = Mathf.Max(latest, keys[i].time);
                }
            }
            return latest;
        }

        private static float LatestTime(List<CutsceneAttachMarker> markers)
        {
            float latest = 0f;
            if (markers != null)
            {
                for (int i = 0; i < markers.Count; i++)
                {
                    latest = Mathf.Max(latest, markers[i].time);
                }
            }
            return latest;
        }

        /// <summary>A mark's own time is when its order is issued; the timeline has to reach the rehearsed arrival too, or the walk runs off the end of the ruler.</summary>
        private static float LatestArrivalTime(List<CutsceneMarkKey> marks)
        {
            float latest = 0f;
            if (marks != null)
            {
                for (int i = 0; i < marks.Count; i++)
                {
                    latest = Mathf.Max(latest, CutsceneMarkMerge.ArrivalTime(marks[i]));
                }
            }
            return latest;
        }

        private static float LatestTime(List<CutsceneFacingKey> keys)
        {
            float latest = 0f;
            if (keys != null)
            {
                for (int i = 0; i < keys.Count; i++)
                {
                    latest = Mathf.Max(latest, keys[i].time);
                }
            }
            return latest;
        }

        private static float LatestTime(List<CutsceneCameraKey> keys)
        {
            float latest = 0f;
            if (keys != null)
            {
                for (int i = 0; i < keys.Count; i++)
                {
                    latest = Mathf.Max(latest, keys[i].time);
                }
            }
            return latest;
        }

        // One row across two columns. Both halves carry the same explicit height, so a wrapping
        // label or a themed border can never leave the header column out of step with its lanes.
        /// <summary>Adds a row's header cell and its lane, and returns the header cell so a caller can hang a menu on it.</summary>
        private VisualElement AddTimelineRow(
            VisualElement laneContent, string headerLabel, VisualElement laneElement,
            Action onHeaderClick, float rowHeight, bool isGroup = false, string accentClass = null,
            bool indentLabel = false, bool isSelected = false)
        {
            VisualElement headerCell = new VisualElement();
            headerCell.AddToClassList("cutscene-editor__row");
            headerCell.AddToClassList("cutscene-editor__track-header");
            headerCell.EnableInClassList("cutscene-editor__row--group", isGroup);
            headerCell.EnableInClassList("cutscene-editor__row--selected", isSelected);
            headerCell.EnableInClassList("cutscene-editor__track-header--group", isGroup);
            if (!string.IsNullOrEmpty(accentClass))
            {
                headerCell.AddToClassList("cutscene-editor__track-header--" + accentClass);
            }
            headerCell.style.width = HeaderColumnWidth;
            headerCell.style.height = rowHeight;
            if (!string.IsNullOrEmpty(headerLabel))
            {
                Label label = new Label(headerLabel);
                label.AddToClassList("cutscene-editor__track-header-label");
                label.EnableInClassList("cutscene-editor__track-header-label--group", isGroup);
                label.EnableInClassList("cutscene-editor__track-header-label--indent", indentLabel);
                label.pickingMode = PickingMode.Ignore;
                headerCell.Add(label);
            }
            if (onHeaderClick != null)
            {
                headerCell.RegisterCallback<PointerDownEvent>(_ => onHeaderClick());
            }
            timelineHeaderContent.Add(headerCell);

            VisualElement laneRow = new VisualElement();
            laneRow.AddToClassList("cutscene-editor__row");
            laneRow.EnableInClassList("cutscene-editor__row--group", isGroup);
            laneRow.EnableInClassList("cutscene-editor__row--selected", isSelected);
            laneRow.style.height = rowHeight;
            laneRow.Add(laneElement);
            laneContent.Add(laneRow);
            return headerCell;
        }

        /// <summary>A header-column entry with no lane of its own, plus the matching blank in the lane column.</summary>
        private void AddHeaderOnlyRow(VisualElement laneContent, VisualElement headerElement, float rowHeight)
        {
            // Wrapped rather than sized directly: a bare element's own margins are laid out outside
            // its height and would push this column taller than the lane column, row by row.
            VisualElement headerRow = new VisualElement();
            headerRow.AddToClassList("cutscene-editor__row");
            headerRow.style.height = rowHeight;
            headerRow.style.flexShrink = 0f;
            headerRow.style.overflow = Overflow.Hidden;
            headerRow.Add(headerElement);
            timelineHeaderContent.Add(headerRow);

            VisualElement laneSpacer = new VisualElement();
            laneSpacer.style.height = rowHeight;
            laneSpacer.style.flexShrink = 0f;
            laneContent.Add(laneSpacer);
        }

        // -----------------------------------------------------------------------------------
        // Per-slot rows.
        // -----------------------------------------------------------------------------------

        private void BuildSlotRows(VisualElement content, SerializedProperty slotProperty, int slotIndex, float contentWidth)
        {
            CutsceneSlot slot = cutscene.slots[slotIndex];
            bool isActor = slot.kind == CutsceneSlotKind.Actor;
            string accent = isActor ? "actor" : "prop";

            VisualElement slotHeaderCell = AddTimelineRow(
                content, slot.name,
                new VisualElement { style = { width = contentWidth, height = LaneRowHeight } },
                () => SelectSlotHeader(slotIndex), LaneRowHeight,
                isGroup: true, accentClass: accent,
                isSelected: slotIndex == selectedSlotIndex && selectedLaneKind == SelectedLaneKind.None);
            slotHeaderCell.AddManipulator(new ContextualMenuManipulator(menuEvent =>
                menuEvent.menu.AppendAction("Remove Slot", _ => RemoveSlot(slotIndex))));

            if (isActor)
            {
                SerializedProperty clipBlocksProperty = slotProperty.FindPropertyRelative("clipBlocks");
                List<CutsceneClipBlockDisplay> blockDisplays = new List<CutsceneClipBlockDisplay>(slot.clipBlocks.Count);
                for (int i = 0; i < slot.clipBlocks.Count; i++)
                {
                    CutsceneClipBlock block = slot.clipBlocks[i];
                    blockDisplays.Add(new CutsceneClipBlockDisplay(
                        DescribeClip(slot, block.clipId), block.start, block.duration, block.loop));
                }

                CutsceneClipBlockLaneElement clipLane = new CutsceneClipBlockLaneElement
                {
                    pixelsPerSecond = pixelsPerSecond,
                    style = { width = contentWidth, height = LaneRowHeight }
                };
                RegisterBlockLane(clipLane, slotIndex);
                clipLane.SetBlocks(blockDisplays,
                    selectedSlotIndex == slotIndex && selectedLaneKind == SelectedLaneKind.ClipBlock ? selectedItemIndex : -1);
                clipLane.BlockChangeCommitted += (index, start, duration) =>
                    CommitClipBlockChange(clipBlocksProperty, index, start, duration);
                clipLane.EmptySpaceDoubleClicked += time => AddClipBlock(slotIndex, clipBlocksProperty, time);
                clipLane.BlockDeleteRequested += index => DeleteArrayElement(clipBlocksProperty, index);
                AddTimelineRow(
                    content, "Clip", clipLane, () => SelectSlotHeader(slotIndex), LaneRowHeight,
                    accentClass: accent, indentLabel: true);
            }

            SerializedProperty transformKeysProperty = slotProperty.FindPropertyRelative("transformKeys");
            BuildMomentRow(
                content, isActor ? "Root" : "Move", slot.transformKeys, transformKeysProperty,
                slotIndex, SelectedLaneKind.RootTransformKey, -1, contentWidth,
                new Color(0.65f, 0.85f, 0.55f), time => InsertTransformKeyDefault(transformKeysProperty, time),
                accentClass: accent);

            SerializedProperty attachMarkersProperty = slotProperty.FindPropertyRelative("attachMarkers");
            BuildAttachRow(content, slot, attachMarkersProperty, slotIndex, contentWidth, accent);

            SerializedProperty markKeysProperty = slotProperty.FindPropertyRelative("markKeys");
            BuildMomentRow(
                content, "Marks", slot.markKeys, markKeysProperty,
                slotIndex, SelectedLaneKind.MarkKey, -1, contentWidth,
                new Color(0.45f, 0.65f, 0.95f), time => InsertMarkKeyDefault(slotIndex, markKeysProperty, time),
                accentClass: accent);

            if (isActor)
            {
                SerializedProperty facingKeysProperty = slotProperty.FindPropertyRelative("facingKeys");
                BuildMomentRow(
                    content, "Facing", slot.facingKeys, facingKeysProperty,
                    slotIndex, SelectedLaneKind.FacingKey, -1, contentWidth,
                    new Color(0.85f, 0.75f, 0.4f), time => InsertFacingKeyDefault(facingKeysProperty, time),
                    accentClass: accent);

                // One row per part track — the label IS the track header, keys live beside it.
                // The old header-row-plus-keys-row pair wasted a lane per track (A60).
                SerializedProperty partTracksProperty = slotProperty.FindPropertyRelative("partTracks");
                for (int trackIndex = 0; trackIndex < slot.partTracks.Count; trackIndex++)
                {
                    int capturedTrackIndex = trackIndex;
                    CutsceneKeyedTrack track = slot.partTracks[trackIndex];
                    string tagName = VocabularyRegistryProvider.TargetTags.FindName(track.tagId);
                    SerializedProperty trackProperty = partTracksProperty.GetArrayElementAtIndex(capturedTrackIndex);
                    SerializedProperty keysProperty = trackProperty.FindPropertyRelative("keys");
                    VisualElement partHeaderCell = BuildMomentRow(
                        content, tagName ?? "0x" + track.tagId.ToString("X8"), track.keys, keysProperty,
                        slotIndex, SelectedLaneKind.PartTrackKey, capturedTrackIndex, contentWidth,
                        new Color(0.75f, 0.55f, 0.85f), time => InsertTransformKeyDefault(keysProperty, time),
                        accentClass: accent);
                    partHeaderCell.AddManipulator(new ContextualMenuManipulator(menuEvent =>
                        menuEvent.menu.AppendAction(
                            "Remove Part Track", _ => DeleteArrayElement(partTracksProperty, capturedTrackIndex))));
                }

                Button addPartTrackButton = new Button(() => OpenAddPartTrackPicker(slotIndex))
                {
                    text = "+ Part Track",
                    tooltip = "Adds a keyed override track for one rig part (picked by tag)."
                };
                addPartTrackButton.style.marginLeft = 8f;
                addPartTrackButton.style.width = HeaderColumnWidth - 16f;
                addPartTrackButton.style.marginTop = 2f;
                addPartTrackButton.style.marginBottom = 2f;
                addPartTrackButton.style.fontSize = 10f;
                AddHeaderOnlyRow(content, addPartTrackButton, LaneRowHeight);
            }
        }

        private VisualElement BuildMomentRow(
            VisualElement content, string label, List<CutsceneTransformKey> keys, SerializedProperty keysProperty,
            int slotIndex, SelectedLaneKind laneKind, int partTrackIndex, float contentWidth, Color color,
            Action<float> onAddAtTime, string accentClass = null, bool isGroup = false,
            bool indentLabel = true)
        {
            List<float> times = new List<float>(keys.Count);
            for (int i = 0; i < keys.Count; i++)
            {
                times.Add(keys[i].time);
            }

            CutsceneMomentLaneElement lane = new CutsceneMomentLaneElement
            {
                pixelsPerSecond = pixelsPerSecond,
                markerColor = color,
                style = { width = contentWidth, height = LaneRowHeight }
            };
            bool isSelectedLane = selectedSlotIndex == slotIndex && selectedLaneKind == laneKind
                && selectedPartTrackIndex == partTrackIndex;
            RegisterMomentLane(lane, slotIndex, laneKind, partTrackIndex);
            lane.SetTimes(times, isSelectedLane ? selectedItemIndex : -1);
            lane.MomentSelected += index => SelectItemFromLaneBackground(index, slotIndex, laneKind, partTrackIndex);
            lane.MomentMoveCommitted += (index, time) => CommitMomentTime(keysProperty, index, time);
            lane.EmptySpaceDoubleClicked += onAddAtTime;
            lane.MomentDeleteRequested += index => DeleteArrayElement(keysProperty, index);

            return AddTimelineRow(
                content, label, lane, () => SelectItem(slotIndex, laneKind, partTrackIndex, -1),
                LaneRowHeight, isGroup: isGroup, accentClass: accentClass, indentLabel: indentLabel,
                isSelected: isSelectedLane && selectedItemIndex < 0);
        }

        private VisualElement BuildMomentRow(
            VisualElement content, string label, List<CutsceneFacingKey> keys, SerializedProperty keysProperty,
            int slotIndex, SelectedLaneKind laneKind, int partTrackIndex, float contentWidth, Color color,
            Action<float> onAddAtTime, string accentClass = null, bool isGroup = false,
            bool indentLabel = true)
        {
            List<float> times = new List<float>(keys.Count);
            for (int i = 0; i < keys.Count; i++)
            {
                times.Add(keys[i].time);
            }

            CutsceneMomentLaneElement lane = new CutsceneMomentLaneElement
            {
                pixelsPerSecond = pixelsPerSecond,
                markerColor = color,
                style = { width = contentWidth, height = LaneRowHeight }
            };
            bool isSelectedLane = selectedSlotIndex == slotIndex && selectedLaneKind == laneKind;
            RegisterMomentLane(lane, slotIndex, laneKind, partTrackIndex);
            lane.SetTimes(times, isSelectedLane ? selectedItemIndex : -1);
            lane.MomentSelected += index => SelectItemFromLaneBackground(index, slotIndex, laneKind, partTrackIndex);
            lane.MomentMoveCommitted += (index, time) => CommitMomentTime(keysProperty, index, time);
            lane.EmptySpaceDoubleClicked += onAddAtTime;
            lane.MomentDeleteRequested += index => DeleteArrayElement(keysProperty, index);

            return AddTimelineRow(
                content, label, lane, () => SelectItem(slotIndex, laneKind, partTrackIndex, -1),
                LaneRowHeight, isGroup: isGroup, accentClass: accentClass, indentLabel: indentLabel,
                isSelected: isSelectedLane && selectedItemIndex < 0);
        }

        private VisualElement BuildMomentRow(
            VisualElement content, string label, List<CutsceneMarkKey> keys, SerializedProperty keysProperty,
            int slotIndex, SelectedLaneKind laneKind, int partTrackIndex, float contentWidth, Color color,
            Action<float> onAddAtTime, string accentClass = null, bool isGroup = false,
            bool indentLabel = true)
        {
            List<float> times = new List<float>(keys.Count);
            for (int i = 0; i < keys.Count; i++)
            {
                times.Add(keys[i].time);
            }

            CutsceneMomentLaneElement lane = new CutsceneMomentLaneElement
            {
                pixelsPerSecond = pixelsPerSecond,
                markerColor = color,
                style = { width = contentWidth, height = LaneRowHeight }
            };
            bool isSelectedLane = selectedSlotIndex == slotIndex && selectedLaneKind == laneKind;
            RegisterMomentLane(lane, slotIndex, laneKind, partTrackIndex);
            lane.SetTimes(times, isSelectedLane ? selectedItemIndex : -1);
            lane.MomentSelected += index => SelectItemFromLaneBackground(index, slotIndex, laneKind, partTrackIndex);
            lane.MomentMoveCommitted += (index, time) => CommitMomentTime(keysProperty, index, time);
            lane.EmptySpaceDoubleClicked += onAddAtTime;
            lane.MomentDeleteRequested += index => DeleteArrayElement(keysProperty, index);

            return AddTimelineRow(
                content, label, lane, () => SelectItem(slotIndex, laneKind, partTrackIndex, -1),
                LaneRowHeight, isGroup: isGroup, accentClass: accentClass, indentLabel: indentLabel,
                isSelected: isSelectedLane && selectedItemIndex < 0);
        }

        // The attach lane. Built here rather than through BuildMomentRow, since its markers are not
        // all one kind: an Attach and a Detach get different shapes.
        private void BuildAttachRow(
            VisualElement content, CutsceneSlot slot, SerializedProperty attachMarkersProperty,
            int slotIndex, float contentWidth, string accentClass)
        {
            List<float> times = new List<float>(slot.attachMarkers.Count);
            List<string> variantClasses = new List<string>(slot.attachMarkers.Count);
            for (int i = 0; i < slot.attachMarkers.Count; i++)
            {
                times.Add(slot.attachMarkers[i].time);
                variantClasses.Add(slot.attachMarkers[i].kind == CutsceneAttachKind.Detach
                    ? "cutscene-editor__moment-marker--detach"
                    : "cutscene-editor__moment-marker--attach");
            }

            CutsceneMomentLaneElement lane = new CutsceneMomentLaneElement
            {
                pixelsPerSecond = pixelsPerSecond,
                markerColor = new Color(0.45f, 0.8f, 0.8f),
                style = { width = contentWidth, height = LaneRowHeight }
            };
            bool isSelectedLane = selectedSlotIndex == slotIndex && selectedLaneKind == SelectedLaneKind.AttachMarker;
            RegisterMomentLane(lane, slotIndex, SelectedLaneKind.AttachMarker, -1);
            lane.SetTimes(times, isSelectedLane ? selectedItemIndex : -1, variantClasses);
            lane.MomentSelected += index => SelectItemFromLaneBackground(
                index, slotIndex, SelectedLaneKind.AttachMarker, -1);
            lane.MomentMoveCommitted += (index, time) => CommitMomentTime(attachMarkersProperty, index, time);
            lane.EmptySpaceDoubleClicked += time => InsertAttachMarkerDefault(slotIndex, attachMarkersProperty, time);
            lane.MomentDeleteRequested += index => DeleteArrayElement(attachMarkersProperty, index);

            AddTimelineRow(
                content, "Attach", lane, () => SelectItem(slotIndex, SelectedLaneKind.AttachMarker, -1, -1),
                LaneRowHeight, accentClass: accentClass, indentLabel: true,
                isSelected: isSelectedLane && selectedItemIndex < 0);
        }

        private void BuildCameraRows(VisualElement content, float contentWidth)
        {
            SerializedProperty cameraLaneProperty = serializedObject.FindProperty("cameraLane");
            SerializedProperty keysProperty = cameraLaneProperty.FindPropertyRelative("keys");
            List<float> times = new List<float>(cutscene.cameraLane.keys.Count);
            for (int i = 0; i < cutscene.cameraLane.keys.Count; i++)
            {
                times.Add(cutscene.cameraLane.keys[i].time);
            }

            CutsceneMomentLaneElement lane = new CutsceneMomentLaneElement
            {
                pixelsPerSecond = pixelsPerSecond,
                markerColor = new Color(0.55f, 0.7f, 0.95f),
                style = { width = contentWidth, height = LaneRowHeight }
            };
            bool isSelected = selectedLaneKind == SelectedLaneKind.CameraKey;
            RegisterMomentLane(lane, -1, SelectedLaneKind.CameraKey, -1);
            lane.SetTimes(times, isSelected ? selectedItemIndex : -1);
            lane.MomentSelected += index => SelectItemFromLaneBackground(
                index, -1, SelectedLaneKind.CameraKey, -1);
            lane.MomentMoveCommitted += (index, time) => CommitMomentTime(keysProperty, index, time);
            lane.EmptySpaceDoubleClicked += time => InsertCameraKeyDefault(keysProperty, time);
            lane.MomentDeleteRequested += index => DeleteArrayElement(keysProperty, index);
            AddTimelineRow(
                content, "Camera", lane, () => SelectItem(-1, SelectedLaneKind.CameraKey, -1, -1),
                LaneRowHeight, isGroup: true, accentClass: "camera");

            SerializedProperty cutMarkersProperty = cameraLaneProperty.FindPropertyRelative("cutMarkers");
            List<float> cutTimes = new List<float>(cutscene.cameraLane.cutMarkers.Count);
            for (int i = 0; i < cutscene.cameraLane.cutMarkers.Count; i++)
            {
                cutTimes.Add(cutscene.cameraLane.cutMarkers[i].time);
            }
            CutsceneMomentLaneElement cutLane = new CutsceneMomentLaneElement
            {
                pixelsPerSecond = pixelsPerSecond,
                markerColor = new Color(0.95f, 0.45f, 0.45f),
                style = { width = contentWidth, height = LaneRowHeight }
            };
            cutLane.SetTimes(cutTimes, -1);
            cutLane.MomentMoveCommitted += (index, time) => CommitMomentTime(cutMarkersProperty, index, time);
            cutLane.EmptySpaceDoubleClicked += time => InsertCutMarkerDefault(cutMarkersProperty, time);
            cutLane.MomentDeleteRequested += index => DeleteArrayElement(cutMarkersProperty, index);
            AddTimelineRow(content, "Cuts", cutLane, null, LaneRowHeight,
                accentClass: "camera", indentLabel: true);
        }

        private void BuildEventRows(VisualElement content, float contentWidth)
        {
            SerializedProperty eventsProperty = serializedObject.FindProperty("events");
            List<float> times = new List<float>(cutscene.events.Count);
            List<string> variantClasses = new List<string>(cutscene.events.Count);
            for (int i = 0; i < cutscene.events.Count; i++)
            {
                times.Add(cutscene.events[i].time);
                variantClasses.Add(cutscene.events[i].holdUntilReleased
                    ? "cutscene-editor__moment-marker--holding"
                    : null);
            }

            CutsceneMomentLaneElement lane = new CutsceneMomentLaneElement
            {
                pixelsPerSecond = pixelsPerSecond,
                markerColor = new Color(0.9f, 0.6f, 0.3f),
                style = { width = contentWidth, height = LaneRowHeight }
            };
            bool isSelected = selectedLaneKind == SelectedLaneKind.Event;
            RegisterMomentLane(lane, -1, SelectedLaneKind.Event, -1);
            lane.SetTimes(times, isSelected ? selectedItemIndex : -1, variantClasses);
            lane.MomentSelected += index => SelectItemFromLaneBackground(
                index, -1, SelectedLaneKind.Event, -1);
            lane.MomentMoveCommitted += (index, time) => CommitMomentTime(eventsProperty, index, time);
            lane.EmptySpaceDoubleClicked += time => InsertEventDefault(eventsProperty, time);
            lane.MomentDeleteRequested += index => DeleteArrayElement(eventsProperty, index);
            AddTimelineRow(
                content, "Events", lane, () => SelectItem(-1, SelectedLaneKind.Event, -1, -1),
                LaneRowHeight, isGroup: true, accentClass: "events");
        }

        // The Holds lane: every authored marker, then one read-only ghost per holding event. The
        // ghosts are not selectable — the thing to edit is the event, one row up — but they have to
        // be visible, or the timeline would show a clock stopping somewhere nothing is drawn.
        private void BuildHoldRows(VisualElement content, float contentWidth)
        {
            SerializedProperty holdsProperty = serializedObject.FindProperty("holdMarkers");
            int authoredHoldCount = cutscene.holdMarkers.Count;
            List<float> times = new List<float>(authoredHoldCount);
            List<string> variantClasses = new List<string>(authoredHoldCount);
            List<bool> readOnlyFlags = new List<bool>(authoredHoldCount);
            for (int i = 0; i < authoredHoldCount; i++)
            {
                times.Add(cutscene.holdMarkers[i].time);
                variantClasses.Add(null);
                readOnlyFlags.Add(false);
            }

            List<CutsceneDerivedHolds.DerivedHold> derivedHolds = CutsceneDerivedHolds.Collect(cutscene);
            for (int derivedIndex = 0; derivedIndex < derivedHolds.Count; derivedIndex++)
            {
                if (HasAuthoredHoldAt(derivedHolds[derivedIndex].time))
                {
                    continue;
                }
                times.Add(derivedHolds[derivedIndex].time);
                variantClasses.Add("cutscene-editor__moment-marker--derived");
                readOnlyFlags.Add(true);
            }

            CutsceneMomentLaneElement lane = new CutsceneMomentLaneElement
            {
                pixelsPerSecond = pixelsPerSecond,
                markerColor = new Color(0.95f, 0.85f, 0.3f),
                style = { width = contentWidth, height = LaneRowHeight }
            };
            bool isSelected = selectedLaneKind == SelectedLaneKind.Hold;
            RegisterMomentLane(lane, -1, SelectedLaneKind.Hold, -1);
            lane.SetTimes(times, isSelected ? selectedItemIndex : -1, variantClasses, readOnlyFlags);
            lane.MomentSelected += index => SelectItemFromLaneBackground(
                index, -1, SelectedLaneKind.Hold, -1);
            lane.MomentMoveCommitted += (index, time) => CommitMomentTime(holdsProperty, index, time);
            lane.EmptySpaceDoubleClicked += time => InsertHoldDefault(holdsProperty, time);
            lane.MomentDeleteRequested += index => DeleteArrayElement(holdsProperty, index);
            AddTimelineRow(
                content, "Holds", lane, () => SelectItem(-1, SelectedLaneKind.Hold, -1, -1),
                LaneRowHeight, isGroup: true, accentClass: "holds");
        }

        private void OnPlayheadScrubbed(float time)
        {
            // No rebuild: the playhead element repaints itself from TimeSeconds.
            SetPlayhead(time);
        }

        /// <summary>Poses every bound actor/prop, renders the in-tab viewport, and — only if <see cref="previewShotToggle"/> opts in — also drives the Scene view camera.</summary>
        private void ApplyPreviewAtPlayhead()
        {
            previewController.ApplyPose(cutscene, playheadSeconds);
            if (previewShotToggle != null && previewShotToggle.value)
            {
                previewController.ApplyCameraPose(cutscene, playheadSeconds);
            }
            RenderViewport();
        }

        // -----------------------------------------------------------------------------------
        // Selection.
        // -----------------------------------------------------------------------------------

        // A lane raises MomentSelected on release for both an unmoved marker and a click on empty
        // space. The marker case already resolved on the press, so only the empty one reaches here.
        private void SelectItemFromLaneBackground(
            int itemIndex, int slotIndex, SelectedLaneKind laneKind, int partTrackIndex)
        {
            if (itemIndex >= 0)
            {
                return;
            }
            SelectItem(slotIndex, laneKind, partTrackIndex, -1);
        }

        private void SelectItem(int slotIndex, SelectedLaneKind laneKind, int partTrackIndex, int itemIndex)
        {
            CutsceneItemAddress address =
                new CutsceneItemAddress(slotIndex, laneKind, partTrackIndex, itemIndex);
            selectedItems.Clear();
            if (address.HasItem)
            {
                selectedItems.Add(address);
            }
            primaryItem = address;
            ApplyPrimarySelectionSideEffects();
            RequestTimelineRebuild();
            RequestInspectorRebuild();
        }

        // -----------------------------------------------------------------------------------
        // The selection set: several items across several lanes, dragged and deleted as one.
        // -----------------------------------------------------------------------------------

        // Resolved on the press rather than the release, because a drag has to know what it is about
        // to move. Nothing here rebuilds a lane: the press that raised this owns a pointer capture,
        // and rebuilding would release it and kill the drag one pixel in.
        private void ApplyItemPointerDown(CutsceneItemAddress address, bool toggles, bool adds)
        {
            if (toggles && selectedItems.Contains(address))
            {
                selectedItems.Remove(address);
                if (primaryItem.HasValue && primaryItem.Value.Equals(address))
                {
                    primaryItem = null;
                }
            }
            else
            {
                // Clicking something already selected keeps the whole set, so a drag started on one
                // of several moves all of them.
                if (!toggles && !adds && !selectedItems.Contains(address))
                {
                    selectedItems.Clear();
                }
                selectedItems.Add(address);
                primaryItem = address;
            }

            UnpackPrimarySelection();
            ApplyPrimarySelectionSideEffects();
            RefreshLaneSelectionVisuals();
            RequestInspectorRebuild();
        }

        private void UnpackPrimarySelection()
        {
            if (!primaryItem.HasValue)
            {
                selectedSlotIndex = -1;
                selectedLaneKind = SelectedLaneKind.None;
                selectedPartTrackIndex = -1;
                selectedItemIndex = -1;
                return;
            }
            CutsceneItemAddress address = primaryItem.Value;
            selectedSlotIndex = address.slotIndex;
            selectedLaneKind = address.laneKind;
            selectedPartTrackIndex = address.partTrackIndex;
            selectedItemIndex = address.itemIndex;
        }

        private void ApplyPrimarySelectionSideEffects()
        {
            UnpackPrimarySelection();
            markSceneOverlay.SetSelection(
                selectedLaneKind == SelectedLaneKind.MarkKey ? selectedSlotIndex : -1,
                selectedLaneKind == SelectedLaneKind.MarkKey ? selectedItemIndex : -1);
            SceneView.RepaintAll();
            SyncSceneSelectionToTimelineSelection();
        }

        private bool IsItemSelected(CutsceneItemAddress laneAddress, int itemIndex)
        {
            return selectedItems.Contains(new CutsceneItemAddress(
                laneAddress.slotIndex, laneAddress.laneKind, laneAddress.partTrackIndex, itemIndex));
        }

        private void RefreshLaneSelectionVisuals()
        {
            for (int laneIndex = 0; laneIndex < registeredLanes.Count; laneIndex++)
            {
                RegisteredLane lane = registeredLanes[laneIndex];
                if (lane.momentLane != null)
                {
                    lane.momentLane.RefreshSelectionVisuals();
                }
                else if (lane.blockLane != null)
                {
                    lane.blockLane.RefreshSelectionVisuals();
                }
            }
        }

        private void PreviewSelectionDrag(float deltaSeconds)
        {
            for (int laneIndex = 0; laneIndex < registeredLanes.Count; laneIndex++)
            {
                RegisteredLane lane = registeredLanes[laneIndex];
                if (lane.momentLane != null)
                {
                    lane.momentLane.PreviewOffsetForSelected(deltaSeconds);
                }
                else if (lane.blockLane != null)
                {
                    lane.blockLane.PreviewOffsetForSelected(deltaSeconds);
                }
            }
        }

        private bool TryGetLaneListProperty(CutsceneItemAddress laneAddress, out SerializedProperty listProperty)
        {
            listProperty = null;
            if (cutscene == null || serializedObject == null)
            {
                return false;
            }
            return CutsceneLaneEditing.TryGetLaneListProperty(
                serializedObject, laneAddress.laneKind, laneAddress.slotIndex, laneAddress.partTrackIndex,
                out listProperty);
        }

        /// <summary>The selection grouped by lane, each lane's item indices ascending.</summary>
        private List<KeyValuePair<CutsceneItemAddress, List<int>>> GroupSelectionByLane()
        {
            List<KeyValuePair<CutsceneItemAddress, List<int>>> grouped =
                new List<KeyValuePair<CutsceneItemAddress, List<int>>>();
            foreach (CutsceneItemAddress address in selectedItems)
            {
                if (!address.HasItem)
                {
                    continue;
                }
                CutsceneItemAddress laneAddress = address.LaneOnly();
                int existing = -1;
                for (int laneIndex = 0; laneIndex < grouped.Count; laneIndex++)
                {
                    if (grouped[laneIndex].Key.Equals(laneAddress))
                    {
                        existing = laneIndex;
                        break;
                    }
                }
                if (existing < 0)
                {
                    grouped.Add(new KeyValuePair<CutsceneItemAddress, List<int>>(laneAddress, new List<int>()));
                    existing = grouped.Count - 1;
                }
                grouped[existing].Value.Add(address.itemIndex);
            }
            for (int laneIndex = 0; laneIndex < grouped.Count; laneIndex++)
            {
                grouped[laneIndex].Value.Sort();
            }
            return grouped;
        }

        // One SerializedObject commit for the whole drag, across every lane it touched. The delta is
        // reduced once against the earliest selected item anywhere, so the group travels rigidly
        // rather than piling up on zero lane by lane.
        private void CommitSelectionDrag(float deltaSeconds)
        {
            List<KeyValuePair<CutsceneItemAddress, List<int>>> grouped = GroupSelectionByLane();
            if (grouped.Count == 0)
            {
                return;
            }

            float earliestSelectedTime = float.MaxValue;
            for (int laneIndex = 0; laneIndex < grouped.Count; laneIndex++)
            {
                SerializedProperty listProperty;
                if (!TryGetLaneListProperty(grouped[laneIndex].Key, out listProperty))
                {
                    continue;
                }
                string timeFieldName =
                    CutsceneLaneEditing.TimeFieldNameFor(grouped[laneIndex].Key.laneKind);
                List<int> indices = grouped[laneIndex].Value;
                for (int cursor = 0; cursor < indices.Count; cursor++)
                {
                    if (indices[cursor] < 0 || indices[cursor] >= listProperty.arraySize)
                    {
                        continue;
                    }
                    float itemTime = listProperty.GetArrayElementAtIndex(indices[cursor])
                        .FindPropertyRelative(timeFieldName).floatValue;
                    if (itemTime < earliestSelectedTime)
                    {
                        earliestSelectedTime = itemTime;
                    }
                }
            }
            if (earliestSelectedTime == float.MaxValue)
            {
                return;
            }
            float appliedDelta = earliestSelectedTime + deltaSeconds < 0f
                ? -earliestSelectedTime
                : deltaSeconds;

            selectedItems.Clear();
            List<float> times = new List<float>();
            for (int laneIndex = 0; laneIndex < grouped.Count; laneIndex++)
            {
                CutsceneItemAddress laneAddress = grouped[laneIndex].Key;
                SerializedProperty listProperty;
                if (!TryGetLaneListProperty(laneAddress, out listProperty))
                {
                    continue;
                }
                string timeFieldName = CutsceneLaneEditing.TimeFieldNameFor(laneAddress.laneKind);

                times.Clear();
                for (int itemIndex = 0; itemIndex < listProperty.arraySize; itemIndex++)
                {
                    times.Add(listProperty.GetArrayElementAtIndex(itemIndex)
                        .FindPropertyRelative(timeFieldName).floatValue);
                }
                List<int> indices = grouped[laneIndex].Value;
                CutsceneSelectionMath.ShiftTimes(times, indices, appliedDelta);
                for (int itemIndex = 0; itemIndex < listProperty.arraySize; itemIndex++)
                {
                    listProperty.GetArrayElementAtIndex(itemIndex)
                        .FindPropertyRelative(timeFieldName).floatValue = times[itemIndex];
                }

                CutsceneLaneEditing.SortByTime(listProperty, timeFieldName, indices);
                for (int cursor = 0; cursor < indices.Count; cursor++)
                {
                    selectedItems.Add(new CutsceneItemAddress(
                        laneAddress.slotIndex, laneAddress.laneKind, laneAddress.partTrackIndex,
                        indices[cursor]));
                }
            }

            primaryItem = null;
            foreach (CutsceneItemAddress address in selectedItems)
            {
                primaryItem = address;
                break;
            }
            CommitStructuralChange();
        }

        /// <summary>Deletes every selected item, highest index first so the lower ones stay addressable.</summary>
        private void DeleteSelectedItems()
        {
            List<KeyValuePair<CutsceneItemAddress, List<int>>> grouped = GroupSelectionByLane();
            if (grouped.Count == 0)
            {
                return;
            }
            for (int laneIndex = 0; laneIndex < grouped.Count; laneIndex++)
            {
                SerializedProperty listProperty;
                if (!TryGetLaneListProperty(grouped[laneIndex].Key, out listProperty))
                {
                    continue;
                }
                List<int> indices = grouped[laneIndex].Value;
                for (int cursor = indices.Count - 1; cursor >= 0; cursor--)
                {
                    if (indices[cursor] >= 0 && indices[cursor] < listProperty.arraySize)
                    {
                        listProperty.DeleteArrayElementAtIndex(indices[cursor]);
                    }
                }
            }
            selectedItems.Clear();
            primaryItem = null;
            selectedItemIndex = -1;
            CommitStructuralChange();
        }

        // The panel is focusable so it can hear shortcuts, which means every text field inside it
        // routes its keystrokes through here first — Ctrl+C in a Hold Id field must stay a text copy.
        private static bool IsEditableTarget(IEventHandler target)
        {
            VisualElement targetElement = target as VisualElement;
            while (targetElement != null)
            {
                // Every editable field in UI Toolkit — text, int, float, the vector fields' parts —
                // derives from this one open generic, so one walk covers all of them.
                Type elementType = targetElement.GetType();
                while (elementType != null)
                {
                    if (elementType.IsGenericType
                        && elementType.GetGenericTypeDefinition() == typeof(TextInputBaseField<>))
                    {
                        return true;
                    }
                    elementType = elementType.BaseType;
                }
                targetElement = targetElement.parent;
            }
            return false;
        }

        private void OnPanelKeyDown(KeyDownEvent keyEvent)
        {
            if (cutscene == null || IsEditableTarget(keyEvent.target))
            {
                return;
            }

            if (keyEvent.keyCode == KeyCode.Delete || keyEvent.keyCode == KeyCode.Backspace)
            {
                if (selectedItems.Count == 0)
                {
                    return;
                }
                DeleteSelectedItems();
                keyEvent.StopPropagation();
                return;
            }

            if (!keyEvent.ctrlKey && !keyEvent.commandKey)
            {
                switch (keyEvent.keyCode)
                {
                    case KeyCode.W:
                        SetViewportGizmoMode(GizmoMode.Move);
                        keyEvent.StopPropagation();
                        return;
                    case KeyCode.E:
                        SetViewportGizmoMode(GizmoMode.Rotate);
                        keyEvent.StopPropagation();
                        return;
                    case KeyCode.R:
                        SetViewportGizmoMode(GizmoMode.Scale);
                        keyEvent.StopPropagation();
                        return;
                }
                return;
            }

            switch (keyEvent.keyCode)
            {
                case KeyCode.C:
                    CopySelectionToClipboard();
                    keyEvent.StopPropagation();
                    return;
                case KeyCode.X:
                    if (CopySelectionToClipboard() > 0)
                    {
                        DeleteSelectedItems();
                    }
                    keyEvent.StopPropagation();
                    return;
                case KeyCode.V:
                    PasteClipboardAt(playheadSeconds);
                    keyEvent.StopPropagation();
                    return;
                case KeyCode.D:
                    DuplicateSelection();
                    keyEvent.StopPropagation();
                    return;
            }
        }

        private int CopySelectionToClipboard()
        {
            int copiedCount = CutsceneKeyClipboard.Copy(cutscene, selectedItems);
            ReportTransportAction(copiedCount, "Copied");
            return copiedCount;
        }

        // Pasting into the selected slot rather than the source one is what makes "copy a beat from
        // one actor onto another" a two-key gesture; with no slot selected the items go back where
        // they came from.
        private void PasteClipboardAt(float atSeconds)
        {
            if (!CutsceneKeyClipboard.HasContent || cutscene == null)
            {
                return;
            }
            List<CutsceneItemAddress> pastedAddresses = new List<CutsceneItemAddress>();
            int pastedCount = CutsceneKeyClipboard.Paste(
                cutscene, serializedObject, atSeconds, selectedSlotIndex, pastedAddresses);
            if (pastedCount == 0)
            {
                ReportTransportAction(0, "Pasted");
                return;
            }

            SelectExactly(pastedAddresses);
            CommitStructuralChange();
            ReportTransportAction(pastedCount, "Pasted");
        }

        // Duplicate is copy plus paste at the items' own time, so the copies land on top of the
        // originals and are the ones left selected — drag them off from there.
        private void DuplicateSelection()
        {
            if (selectedItems.Count == 0)
            {
                return;
            }
            float earliestSelectedTime = EarliestSelectedTime();
            if (CutsceneKeyClipboard.Copy(cutscene, selectedItems) == 0)
            {
                return;
            }
            PasteClipboardAt(earliestSelectedTime);
        }

        private float EarliestSelectedTime()
        {
            float earliest = float.MaxValue;
            foreach (CutsceneItemAddress address in selectedItems)
            {
                SerializedProperty listProperty;
                if (!TryGetLaneListProperty(address.LaneOnly(), out listProperty)
                    || address.itemIndex < 0 || address.itemIndex >= listProperty.arraySize)
                {
                    continue;
                }
                float itemTime = listProperty.GetArrayElementAtIndex(address.itemIndex)
                    .FindPropertyRelative(CutsceneLaneEditing.TimeFieldNameFor(address.laneKind)).floatValue;
                earliest = itemTime < earliest ? itemTime : earliest;
            }
            return earliest == float.MaxValue ? playheadSeconds : earliest;
        }

        private void SelectExactly(List<CutsceneItemAddress> addresses)
        {
            selectedItems.Clear();
            primaryItem = null;
            for (int cursor = 0; cursor < addresses.Count; cursor++)
            {
                selectedItems.Add(addresses[cursor]);
                primaryItem = addresses[cursor];
            }
            UnpackPrimarySelection();
        }

        private void ReportTransportAction(int itemCount, string verb)
        {
            if (transportStatusLabel == null)
            {
                return;
            }
            transportStatusLabel.EnableInClassList(HoldingStatusUssClassName, false);
            transportStatusLabel.text = itemCount == 0
                ? verb + " nothing"
                : verb + " " + itemCount.ToString() + (itemCount == 1 ? " item" : " items");
        }

        // -----------------------------------------------------------------------------------
        // Box select: a band over the lane stack picks up whatever it crosses.
        // -----------------------------------------------------------------------------------

        private void BeginBoxSelect(CutsceneItemAddress laneAddress, PointerDownEvent pointerEvent)
        {
            VisualElement lane = pointerEvent.currentTarget as VisualElement;
            if (lane == null || timelineContent == null || boxSelectElement == null)
            {
                return;
            }

            boxSelectLaneAddress = laneAddress;
            boxSelectOriginInContent = lane.ChangeCoordinatesTo(timelineContent, pointerEvent.localPosition);
            boxSelectLane = lane;
            isBoxSelectArmed = true;
            isBoxSelectActive = false;
            isBoxSelectAdditive =
                pointerEvent.shiftKey || pointerEvent.ctrlKey || pointerEvent.commandKey;

            lane.CapturePointer(pointerEvent.pointerId);
            lane.RegisterCallback<PointerMoveEvent>(OnBoxSelectMove);
            lane.RegisterCallback<PointerUpEvent>(OnBoxSelectEnd);
        }

        private void OnBoxSelectMove(PointerMoveEvent moveEvent)
        {
            VisualElement lane = moveEvent.currentTarget as VisualElement;
            if (!isBoxSelectArmed || lane == null || timelineContent == null)
            {
                return;
            }

            Vector2 currentInContent = lane.ChangeCoordinatesTo(timelineContent, moveEvent.localPosition);
            if (!isBoxSelectActive
                && (currentInContent - boxSelectOriginInContent).sqrMagnitude < BoxSelectStartToleranceSquared)
            {
                return;
            }
            isBoxSelectActive = true;
            boxSelectElement.SetBand(BandBetween(boxSelectOriginInContent, currentInContent));
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
                // It was a click after all. Only now is it safe to clear the set — doing it on the
                // press would have emptied the selection a Shift+band was meant to grow.
                if (!isBoxSelectAdditive)
                {
                    SelectItem(
                        boxSelectLaneAddress.slotIndex, boxSelectLaneAddress.laneKind,
                        boxSelectLaneAddress.partTrackIndex, -1);
                }
                return;
            }
            isBoxSelectActive = false;

            Vector2 endInContent = lane != null && timelineContent != null
                ? lane.ChangeCoordinatesTo(timelineContent, upEvent.localPosition)
                : boxSelectOriginInContent;
            SelectItemsInsideBand(BandBetween(boxSelectOriginInContent, endInContent));
            boxSelectElement.HideBand();

            RefreshLaneSelectionVisuals();
            RequestInspectorRebuild();
        }

        private static Rect BandBetween(Vector2 origin, Vector2 current)
        {
            return Rect.MinMaxRect(
                Mathf.Min(origin.x, current.x), Mathf.Min(origin.y, current.y),
                Mathf.Max(origin.x, current.x), Mathf.Max(origin.y, current.y));
        }

        private void SelectItemsInsideBand(Rect bandInContent)
        {
            if (!isBoxSelectAdditive)
            {
                selectedItems.Clear();
                primaryItem = null;
            }

            List<int> collected = new List<int>();
            for (int laneIndex = 0; laneIndex < registeredLanes.Count; laneIndex++)
            {
                RegisteredLane lane = registeredLanes[laneIndex];
                VisualElement laneElement = lane.Element;
                if (laneElement == null)
                {
                    continue;
                }
                Rect laneRectInContent = laneElement.ChangeCoordinatesTo(timelineContent, laneElement.contentRect);
                if (laneRectInContent.yMax < bandInContent.yMin || laneRectInContent.yMin > bandInContent.yMax)
                {
                    continue;
                }

                Rect bandInLane = Rect.MinMaxRect(
                    bandInContent.xMin - laneRectInContent.xMin, 0f,
                    bandInContent.xMax - laneRectInContent.xMin, laneRectInContent.height);
                collected.Clear();
                if (lane.momentLane != null)
                {
                    lane.momentLane.CollectItemsInBand(bandInLane, collected);
                }
                else if (lane.blockLane != null)
                {
                    lane.blockLane.CollectItemsInBand(bandInLane, collected);
                }

                for (int cursor = 0; cursor < collected.Count; cursor++)
                {
                    CutsceneItemAddress address = new CutsceneItemAddress(
                        lane.laneAddress.slotIndex, lane.laneAddress.laneKind,
                        lane.laneAddress.partTrackIndex, collected[cursor]);
                    selectedItems.Add(address);
                    primaryItem = address;
                }
            }

            UnpackPrimarySelection();
            ApplyPrimarySelectionSideEffects();
        }

        // -----------------------------------------------------------------------------------
        // Mutations shared by every moment lane.
        // -----------------------------------------------------------------------------------

        // Every lane hooks up the same way: it answers "is this item selected" from the panel's set,
        // resolves selection on the press, and previews and commits a group drag as one delta.
        private void RegisterMomentLane(
            CutsceneMomentLaneElement lane, int slotIndex, SelectedLaneKind laneKind, int partTrackIndex)
        {
            CutsceneItemAddress laneAddress =
                new CutsceneItemAddress(slotIndex, laneKind, partTrackIndex, -1);
            lane.isItemSelected = itemIndex => IsItemSelected(laneAddress, itemIndex);
            lane.MomentPointerDown += (itemIndex, toggles, adds) => ApplyItemPointerDown(
                new CutsceneItemAddress(slotIndex, laneKind, partTrackIndex, itemIndex), toggles, adds);
            lane.SelectionDragMoved += PreviewSelectionDrag;
            lane.SelectionDragCommitted += CommitSelectionDrag;
            lane.BackgroundPointerDown += pointerEvent => BeginBoxSelect(laneAddress, pointerEvent);
            registeredLanes.Add(new RegisteredLane { laneAddress = laneAddress, momentLane = lane });
        }

        private void RegisterBlockLane(CutsceneClipBlockLaneElement lane, int slotIndex)
        {
            CutsceneItemAddress laneAddress =
                new CutsceneItemAddress(slotIndex, SelectedLaneKind.ClipBlock, -1, -1);
            lane.isItemSelected = itemIndex => IsItemSelected(laneAddress, itemIndex);
            lane.BlockPointerDown += (itemIndex, toggles, adds) => ApplyItemPointerDown(
                new CutsceneItemAddress(slotIndex, SelectedLaneKind.ClipBlock, -1, itemIndex), toggles, adds);
            lane.SelectionDragMoved += PreviewSelectionDrag;
            lane.SelectionDragCommitted += CommitSelectionDrag;
            lane.BackgroundPointerDown += pointerEvent => BeginBoxSelect(laneAddress, pointerEvent);
            registeredLanes.Add(new RegisteredLane { laneAddress = laneAddress, blockLane = lane });
        }

        private void CommitMomentTime(SerializedProperty listProperty, int index, float time)
        {
            listProperty.GetArrayElementAtIndex(index).FindPropertyRelative("time").floatValue = time;
            SortByTime(listProperty);
            CommitStructuralChange();
        }

        private void DeleteArrayElement(SerializedProperty listProperty, int index)
        {
            if (index < 0 || index >= listProperty.arraySize)
            {
                return;
            }
            listProperty.DeleteArrayElementAtIndex(index);
            selectedItems.Clear();
            primaryItem = null;
            selectedItemIndex = -1;
            CommitStructuralChange();
        }

        private static void SortByTime(SerializedProperty listProperty)
        {
            int count = listProperty.arraySize;
            for (int i = 1; i < count; i++)
            {
                int j = i;
                while (j > 0 &&
                    listProperty.GetArrayElementAtIndex(j - 1).FindPropertyRelative("time").floatValue >
                    listProperty.GetArrayElementAtIndex(j).FindPropertyRelative("time").floatValue)
                {
                    listProperty.MoveArrayElement(j, j - 1);
                    j--;
                }
            }
        }

        private static void ZeroFloat3(SerializedProperty float3Property, float x, float y, float z)
        {
            float3Property.FindPropertyRelative("x").floatValue = x;
            float3Property.FindPropertyRelative("y").floatValue = y;
            float3Property.FindPropertyRelative("z").floatValue = z;
        }

        private static void WriteFloat2(SerializedProperty float2Property, float2 value)
        {
            float2Property.FindPropertyRelative("x").floatValue = value.x;
            float2Property.FindPropertyRelative("y").floatValue = value.y;
        }

        private static float2 ReadFloat2(SerializedProperty float2Property)
        {
            return new float2(
                float2Property.FindPropertyRelative("x").floatValue,
                float2Property.FindPropertyRelative("y").floatValue);
        }

        private static void ResetTransformKeyDefaults(SerializedProperty element, float time)
        {
            element.FindPropertyRelative("time").floatValue = time;
            ZeroFloat3(element.FindPropertyRelative("position"), 0f, 0f, 0f);
            ZeroFloat3(element.FindPropertyRelative("rotation"), 0f, 0f, 0f);
            ZeroFloat3(element.FindPropertyRelative("scale"), 1f, 1f, 1f);
            element.FindPropertyRelative("interpolation").enumValueIndex = (int)Interpolation.Linear;
            SerializedProperty startHandle = element.FindPropertyRelative("bezierStartHandle");
            startHandle.FindPropertyRelative("x").floatValue = 0f;
            startHandle.FindPropertyRelative("y").floatValue = 0f;
            SerializedProperty endHandle = element.FindPropertyRelative("bezierEndHandle");
            endHandle.FindPropertyRelative("x").floatValue = 0f;
            endHandle.FindPropertyRelative("y").floatValue = 0f;
        }

        private void InsertTransformKeyDefault(SerializedProperty listProperty, float time)
        {
            int index = listProperty.arraySize;
            listProperty.InsertArrayElementAtIndex(index);
            ResetTransformKeyDefaults(listProperty.GetArrayElementAtIndex(index), time);
            SortByTime(listProperty);
            CommitStructuralChange();
        }

        private void InsertFacingKeyDefault(SerializedProperty listProperty, float time)
        {
            int index = listProperty.arraySize;
            listProperty.InsertArrayElementAtIndex(index);
            SerializedProperty element = listProperty.GetArrayElementAtIndex(index);
            element.FindPropertyRelative("time").floatValue = time;
            element.FindPropertyRelative("angleDegrees").floatValue = 0f;
            SortByTime(listProperty);
            CommitStructuralChange();
        }

        private void InsertCameraKeyDefault(SerializedProperty listProperty, float time)
        {
            int index = listProperty.arraySize;
            listProperty.InsertArrayElementAtIndex(index);
            SerializedProperty element = listProperty.GetArrayElementAtIndex(index);
            element.FindPropertyRelative("time").floatValue = time;
            ZeroFloat3(element.FindPropertyRelative("position"), 0f, 0f, 0f);
            ZeroFloat3(element.FindPropertyRelative("rotation"), 0f, 0f, 0f);
            element.FindPropertyRelative("fieldOfView").floatValue = 60f;
            element.FindPropertyRelative("interpolation").enumValueIndex = (int)Interpolation.Linear;
            SerializedProperty startHandle = element.FindPropertyRelative("bezierStartHandle");
            startHandle.FindPropertyRelative("x").floatValue = 0f;
            startHandle.FindPropertyRelative("y").floatValue = 0f;
            SerializedProperty endHandle = element.FindPropertyRelative("bezierEndHandle");
            endHandle.FindPropertyRelative("x").floatValue = 0f;
            endHandle.FindPropertyRelative("y").floatValue = 0f;
            SortByTime(listProperty);
            CommitStructuralChange();
        }

        private void InsertCutMarkerDefault(SerializedProperty listProperty, float time)
        {
            int index = listProperty.arraySize;
            listProperty.InsertArrayElementAtIndex(index);
            listProperty.GetArrayElementAtIndex(index).FindPropertyRelative("time").floatValue = time;
            SortByTime(listProperty);
            CommitStructuralChange();
        }

        private void InsertEventDefault(SerializedProperty listProperty, float time)
        {
            int index = listProperty.arraySize;
            listProperty.InsertArrayElementAtIndex(index);
            SerializedProperty element = listProperty.GetArrayElementAtIndex(index);
            element.FindPropertyRelative("time").floatValue = time;
            element.FindPropertyRelative("eventKey").uintValue = 0u;
            element.FindPropertyRelative("intParam").intValue = 0;
            element.FindPropertyRelative("floatParam").floatValue = 0f;
            element.FindPropertyRelative("fireOnSkip").boolValue = true;
            SortByTime(listProperty);
            CommitStructuralChange();
        }

        /// <summary>
        /// A fresh Attach at the playhead, pre-pointed at the first other slot so the marker means
        /// something the instant it exists — an unset host would bake as unresolved and silently do
        /// nothing at play time.
        /// </summary>
        private void InsertAttachMarkerDefault(int slotIndex, SerializedProperty listProperty, float time)
        {
            int index = listProperty.arraySize;
            listProperty.InsertArrayElementAtIndex(index);
            SerializedProperty element = listProperty.GetArrayElementAtIndex(index);
            element.FindPropertyRelative("time").floatValue = time;
            element.FindPropertyRelative("kind").enumValueIndex = (int)CutsceneAttachKind.Attach;
            element.FindPropertyRelative("hostSlotId").uintValue = FindFirstOtherSlotId(slotIndex);
            element.FindPropertyRelative("socketId").uintValue = 0u;
            ZeroFloat3(element.FindPropertyRelative("localOffset"), 0f, 0f, 0f);
            ZeroFloat3(element.FindPropertyRelative("localEulerDegrees"), 0f, 0f, 0f);
            element.FindPropertyRelative("hideWhileAttached").boolValue = false;
            ZeroFloat3(element.FindPropertyRelative("detachImpulse"), 0f, 0f, 0f);
            SortByTime(listProperty);
            CommitStructuralChange();
        }

        private uint FindFirstOtherSlotId(int slotIndex)
        {
            for (int otherIndex = 0; otherIndex < cutscene.slots.Count; otherIndex++)
            {
                if (otherIndex != slotIndex && cutscene.slots[otherIndex] != null)
                {
                    return cutscene.slots[otherIndex].SlotId;
                }
            }
            return 0u;
        }

        /// <summary>
        /// A fresh mark at the playhead, standing where the slot's bound object currently stands —
        /// a mark at the world origin is invisible in a scene built anywhere else, and every author
        /// would immediately drag it back to the actor anyway.
        /// </summary>
        private void InsertMarkKeyDefault(int slotIndex, SerializedProperty listProperty, float time)
        {
            Vector3 position = Vector3.zero;
            float facingDegrees = 0f;
            GameObject boundObject = FindBoundObject(slotIndex);
            if (boundObject != null)
            {
                position = boundObject.transform.position;
                facingDegrees = boundObject.transform.rotation.eulerAngles.y;
            }

            int index = listProperty.arraySize;
            listProperty.InsertArrayElementAtIndex(index);
            SerializedProperty element = listProperty.GetArrayElementAtIndex(index);
            element.FindPropertyRelative("time").floatValue = time;
            ZeroFloat3(element.FindPropertyRelative("position"), position.x, position.y, position.z);
            element.FindPropertyRelative("facingDegrees").floatValue = facingDegrees;
            element.FindPropertyRelative("toleranceMeters").floatValue = DefaultMarkToleranceMeters;
            element.FindPropertyRelative("timeoutSeconds").floatValue = 0f;
            element.FindPropertyRelative("previewTravelSeconds").floatValue = DefaultMarkPreviewTravelSeconds;
            SortByTime(listProperty);
            CommitStructuralChange();
        }

        /// <summary>Close enough to a spot that a walk cycle stopping there reads as "arrived".</summary>
        private const float DefaultMarkToleranceMeters = 0.5f;

        /// <summary>How long the editor rehearses the walk. Not a runtime speed — arrival at run time is a distance test.</summary>
        private const float DefaultMarkPreviewTravelSeconds = 2f;

        /// <summary>The GameObject this slot is bound to in the currently open scene, or null.</summary>
        private GameObject FindBoundObject(int slotIndex)
        {
            if (cutscene == null || slotIndex < 0 || slotIndex >= cutscene.slots.Count)
            {
                return null;
            }
            string currentSceneGuid = CutsceneSceneBinding.CurrentSceneGuid();
            if (string.IsNullOrEmpty(cutscene.sceneGuid) || currentSceneGuid != cutscene.sceneGuid)
            {
                return null;
            }
            CutsceneSlotBindingEntry entry = CutsceneSceneBinding.FindBinding(
                cutscene, currentSceneGuid, cutscene.slots[slotIndex].SlotId);
            return entry != null ? CutsceneSceneBinding.ResolveGameObject(entry.globalObjectId) : null;
        }

        private void InsertHoldDefault(SerializedProperty listProperty, float time)
        {
            int index = listProperty.arraySize;
            listProperty.InsertArrayElementAtIndex(index);
            SerializedProperty element = listProperty.GetArrayElementAtIndex(index);
            element.FindPropertyRelative("time").floatValue = time;
            element.FindPropertyRelative("holdId").stringValue = "hold_" + (index + 1);
            SortByTime(listProperty);
            CommitStructuralChange();
        }

        // -----------------------------------------------------------------------------------
        // Clip blocks.
        // -----------------------------------------------------------------------------------

        private void AddClipBlock(int slotIndex, SerializedProperty listProperty, float time)
        {
            CutsceneSlot slot = cutscene.slots[slotIndex];
            ClipAsset firstClip = FindFirstClip(slot);

            int index = listProperty.arraySize;
            listProperty.InsertArrayElementAtIndex(index);
            SerializedProperty element = listProperty.GetArrayElementAtIndex(index);
            element.FindPropertyRelative("clipId").longValue =
                firstClip != null ? unchecked((long)firstClip.stableId) : 0L;
            element.FindPropertyRelative("start").floatValue = time;
            element.FindPropertyRelative("duration").floatValue =
                firstClip != null ? Mathf.Max(0.05f, firstClip.duration) : 1f;
            element.FindPropertyRelative("loop").boolValue = false;

            SortClipBlocksByStart(listProperty);
            CommitStructuralChange();
        }

        private void CommitClipBlockChange(SerializedProperty listProperty, int index, float start, float duration)
        {
            SerializedProperty element = listProperty.GetArrayElementAtIndex(index);
            element.FindPropertyRelative("start").floatValue = start;
            element.FindPropertyRelative("duration").floatValue = duration;
            SortClipBlocksByStart(listProperty);
            CommitStructuralChange();
        }

        private static void SortClipBlocksByStart(SerializedProperty listProperty)
        {
            int count = listProperty.arraySize;
            for (int i = 1; i < count; i++)
            {
                int j = i;
                while (j > 0 &&
                    listProperty.GetArrayElementAtIndex(j - 1).FindPropertyRelative("start").floatValue >
                    listProperty.GetArrayElementAtIndex(j).FindPropertyRelative("start").floatValue)
                {
                    listProperty.MoveArrayElement(j, j - 1);
                    j--;
                }
            }
        }

        private static ClipAsset FindFirstClip(CutsceneSlot slot)
        {
            if (slot.clipSets == null)
            {
                return null;
            }
            for (int setIndex = 0; setIndex < slot.clipSets.Count; setIndex++)
            {
                ClipSetAsset clipSet = slot.clipSets[setIndex];
                if (clipSet == null || clipSet.clips == null)
                {
                    continue;
                }
                for (int clipIndex = 0; clipIndex < clipSet.clips.Count; clipIndex++)
                {
                    if (clipSet.clips[clipIndex] != null)
                    {
                        return clipSet.clips[clipIndex];
                    }
                }
            }
            return null;
        }

        private static string DescribeClip(CutsceneSlot slot, ulong clipId)
        {
            if (clipId == 0UL)
            {
                return "(no clip)";
            }
            ClipAsset clip = FindClipById(slot, clipId);
            return clip != null ? clip.name : "0x" + clipId.ToString("X16");
        }

        private static ClipAsset FindClipById(CutsceneSlot slot, ulong clipId)
        {
            if (slot.clipSets == null)
            {
                return null;
            }
            for (int setIndex = 0; setIndex < slot.clipSets.Count; setIndex++)
            {
                ClipSetAsset clipSet = slot.clipSets[setIndex];
                if (clipSet == null || clipSet.clips == null)
                {
                    continue;
                }
                for (int clipIndex = 0; clipIndex < clipSet.clips.Count; clipIndex++)
                {
                    ClipAsset clip = clipSet.clips[clipIndex];
                    if (clip != null && clip.stableId == clipId)
                    {
                        return clip;
                    }
                }
            }
            return null;
        }

        private static List<ClipAsset> BuildAvailableClips(CutsceneSlot slot)
        {
            List<ClipAsset> clips = new List<ClipAsset>();
            if (slot.clipSets == null)
            {
                return clips;
            }
            HashSet<ulong> seen = new HashSet<ulong>();
            for (int setIndex = 0; setIndex < slot.clipSets.Count; setIndex++)
            {
                ClipSetAsset clipSet = slot.clipSets[setIndex];
                if (clipSet == null || clipSet.clips == null)
                {
                    continue;
                }
                for (int clipIndex = 0; clipIndex < clipSet.clips.Count; clipIndex++)
                {
                    ClipAsset clip = clipSet.clips[clipIndex];
                    if (clip != null && seen.Add(clip.stableId))
                    {
                        clips.Add(clip);
                    }
                }
            }
            return clips;
        }

        private void OpenAddPartTrackPicker(int slotIndex)
        {
            TargetTagRegistry tagRegistry = VocabularyRegistryProvider.TargetTags;
            VocabularyPicker.Open(
                this,
                this,
                tagRegistry,
                tagRegistry,
                VocabularyPickerConfig.ForTrackTagRebind(tagRegistry),
                tagId => AddPartTrack(slotIndex, tagId),
                () => RebuildTimeline());
        }

        private void AddPartTrack(int slotIndex, uint tagId)
        {
            SerializedProperty slotsProperty = serializedObject.FindProperty("slots");
            SerializedProperty partTracksProperty =
                slotsProperty.GetArrayElementAtIndex(slotIndex).FindPropertyRelative("partTracks");
            int index = partTracksProperty.arraySize;
            partTracksProperty.InsertArrayElementAtIndex(index);
            SerializedProperty element = partTracksProperty.GetArrayElementAtIndex(index);
            element.FindPropertyRelative("tagId").uintValue = tagId;
            element.FindPropertyRelative("channels").enumValueFlag = (int)AnimatedChannels.PositionXY;
            element.FindPropertyRelative("keys").ClearArray();

            CommitStructuralChange();
            selectedSlotIndex = slotIndex;
            selectedLaneKind = SelectedLaneKind.PartTrackHeader;
            selectedPartTrackIndex = index;
            RebuildInspector();
        }

        // -----------------------------------------------------------------------------------
        // Inspector.
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// True while <see cref="RebuildInspector"/> is building, so a change event raised by
        /// binding a field cannot start another rebuild. See <see cref="ShouldIgnoreBindingEcho"/>.
        /// </summary>
        private bool isRebuildingInspector;

        // Whether a change event is Unity's binding echoing the value it just bound, rather than a
        // human picking something. Without this guard, a rebuild-on-change callback rebuilds,
        // re-binds and gets called again — an infinite loop of flicker at frame rate.
        private bool ShouldIgnoreBindingEcho<TValue>(ChangeEvent<TValue> changeEvent)
        {
            return isRebuildingInspector
                || EqualityComparer<TValue>.Default.Equals(changeEvent.previousValue, changeEvent.newValue);
        }

        // A field's drag handle captures the pointer on the element itself, so a rebuild's Clear()
        // releases the capture and ends the drag after roughly one pixel — this guard stops that.
        private bool IsPointerGestureInProgress()
        {
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

        // Driven from the editor tick, not from a pointer-capture-out callback: a capture released
        // by the element's own removal has no handler left to notify.
        private void FlushDeferredPaneRebuilds()
        {
            if (IsPointerGestureInProgress())
            {
                return;
            }
            if (timelineRebuildPending)
            {
                timelineRebuildPending = false;
                RebuildTimeline();
            }
            if (inspectorRebuildPending)
            {
                inspectorRebuildPending = false;
                RebuildInspector();
            }
        }

        private void RebuildInspector()
        {
            isRebuildingInspector = true;
            try
            {
                RebuildInspectorContent();
            }
            finally
            {
                isRebuildingInspector = false;
            }
        }

        private void RebuildInspectorContent()
        {
            inspectorScroll.Clear();

            if (cutscene == null)
            {
                inspectorScroll.Add(new Label("Assign a Cutscene asset above."));
                return;
            }

            if (selectedSlotIndex < 0 && selectedLaneKind == SelectedLaneKind.None)
            {
                BuildCutsceneLevelInspector();
                return;
            }

            if (selectedItems.Count > 1)
            {
                Label multiSelectionNote = new Label(
                    "+ " + (selectedItems.Count - 1).ToString() + " more selected — editing the last "
                    + "one clicked. Drag, Delete and copy act on all of them.");
                multiSelectionNote.style.whiteSpace = WhiteSpace.Normal;
                multiSelectionNote.style.opacity = 0.8f;
                inspectorScroll.Add(multiSelectionNote);
            }

            switch (selectedLaneKind)
            {
                case SelectedLaneKind.None:
                    BuildSlotInspector(selectedSlotIndex);
                    return;
                case SelectedLaneKind.ClipBlock:
                    BuildClipBlockInspector(selectedSlotIndex, selectedItemIndex);
                    return;
                case SelectedLaneKind.RootTransformKey:
                    BuildTransformKeyInspector(
                        "slots.Array.data[" + selectedSlotIndex + "].transformKeys", selectedItemIndex,
                        cutscene.slots[selectedSlotIndex].transformKeys.Count);
                    return;
                case SelectedLaneKind.FacingKey:
                    BuildFacingKeyInspector(selectedSlotIndex, selectedItemIndex);
                    return;
                case SelectedLaneKind.PartTrackHeader:
                    BuildPartTrackHeaderInspector(selectedSlotIndex, selectedPartTrackIndex);
                    return;
                case SelectedLaneKind.PartTrackKey:
                    // The merged part row (A60): clicking its header selects the track with no key,
                    // which is the track inspector's case, not a key's.
                    if (selectedItemIndex < 0)
                    {
                        BuildPartTrackHeaderInspector(selectedSlotIndex, selectedPartTrackIndex);
                        return;
                    }
                    BuildTransformKeyInspector(
                        "slots.Array.data[" + selectedSlotIndex + "].partTracks.Array.data["
                            + selectedPartTrackIndex + "].keys",
                        selectedItemIndex,
                        cutscene.slots[selectedSlotIndex].partTracks[selectedPartTrackIndex].keys.Count);
                    return;
                case SelectedLaneKind.AttachMarker:
                    BuildAttachMarkerInspector(selectedSlotIndex, selectedItemIndex);
                    return;
                case SelectedLaneKind.MarkKey:
                    BuildMarkKeyInspector(selectedSlotIndex, selectedItemIndex);
                    return;
                case SelectedLaneKind.CameraKey:
                    BuildCameraKeyInspector(selectedItemIndex);
                    return;
                case SelectedLaneKind.Event:
                    BuildEventInspector(selectedItemIndex);
                    return;
                case SelectedLaneKind.Hold:
                    BuildHoldInspector(selectedItemIndex);
                    return;
            }
        }

        private void BuildCutsceneLevelInspector()
        {
            inspectorScroll.Add(BuildHeading("Cutscene"));
            inspectorScroll.Add(new Label(
                "Select a slot header, or a marker in the timeline, to edit it.")
            { style = { whiteSpace = WhiteSpace.Normal } });
        }

        private void BuildSlotInspector(int slotIndex)
        {
            SerializedProperty slotProperty =
                serializedObject.FindProperty("slots").GetArrayElementAtIndex(slotIndex);
            CutsceneSlot slot = cutscene.slots[slotIndex];

            inspectorScroll.Add(BuildHeading("Slot"));

            PropertyField nameField = new PropertyField(slotProperty.FindPropertyRelative("name"));
            nameField.Bind(serializedObject);
            inspectorScroll.Add(nameField);

            PropertyField kindField = new PropertyField(slotProperty.FindPropertyRelative("kind"));
            kindField.Bind(serializedObject);
            kindField.RegisterCallback<ChangeEvent<string>>(changeEvent =>
            {
                if (ShouldIgnoreBindingEcho(changeEvent))
                {
                    return;
                }
                RebuildAll();
            });
            inspectorScroll.Add(kindField);

            // Props get one too: a door places exactly the way a character does.
            PropertyField actorPrefabField =
                new PropertyField(slotProperty.FindPropertyRelative("actorPrefab"), "Actor Prefab");
            actorPrefabField.Bind(serializedObject);
            actorPrefabField.RegisterCallback<SerializedPropertyChangeEvent>(_ => RefreshCastPanel());
            inspectorScroll.Add(actorPrefabField);

            if (slot.kind == CutsceneSlotKind.Actor)
            {
                PropertyField rigField = new PropertyField(slotProperty.FindPropertyRelative("rig"));
                rigField.Bind(serializedObject);
                rigField.RegisterCallback<ChangeEvent<UnityEngine.Object>>(_ => RebuildTimeline());
                inspectorScroll.Add(rigField);

                PropertyField clipSetsField = new PropertyField(slotProperty.FindPropertyRelative("clipSets"));
                clipSetsField.Bind(serializedObject);
                clipSetsField.RegisterCallback<SerializedPropertyChangeEvent>(_ => RebuildTimeline());
                inspectorScroll.Add(clipSetsField);

                PropertyField directionSetField = new PropertyField(slotProperty.FindPropertyRelative("directionSet"));
                directionSetField.Bind(serializedObject);
                inspectorScroll.Add(directionSetField);

                // Why a clip block is showing nothing, said where the bind that caused it is edited.
                string clipPreviewStatus = previewController.GetClipPreviewStatus(slot.SlotId);
                if (!string.IsNullOrEmpty(clipPreviewStatus))
                {
                    Label clipStatusLabel = new Label(clipPreviewStatus);
                    clipStatusLabel.style.marginTop = 4f;
                    clipStatusLabel.style.whiteSpace = WhiteSpace.Normal;
                    clipStatusLabel.style.color = new Color(0.95f, 0.8f, 0.35f);
                    inspectorScroll.Add(clipStatusLabel);
                }

                float facingAngle;
                CutsceneKeySampler.TryResolveFacingAngle(
                    slot.facingKeys, CutsceneMarkMerge.BuildEffectiveRootKeys(slot),
                    playheadSeconds, out facingAngle);
                float overrideAngle;
                bool isOverride = CutsceneKeySampler.TryResolveFacingOverride(
                    slot.facingKeys, playheadSeconds, out overrideAngle);
                Label facingLabel = new Label(
                    "Facing at playhead: " + facingAngle.ToString("0.#") + "°"
                    + (isOverride ? " (override key)" : " (derived from root travel)")
                    + (slot.directionSet == null
                        ? " — assign a Direction Set to apply it in the preview."
                        : " — " + CutscenePreviewController.DescribeResolvedFacing(slot, playheadSeconds))
                    + DescribeMissingFacingParts(slot));
                facingLabel.style.marginTop = 4f;
                facingLabel.style.whiteSpace = WhiteSpace.Normal;
                inspectorScroll.Add(facingLabel);
            }

            BuildSceneBindingRow(slotIndex);

            Button removeButton = new Button(() => RemoveSlot(slotIndex)) { text = "Remove Slot" };
            removeButton.style.marginTop = 8f;
            inspectorScroll.Add(removeButton);
        }

        /// <summary>
        /// Says so when a slot's facing resolves but its rig cannot show it. Said here because this
        /// is the line where an author reads that facing, and both failures are otherwise silent.
        /// </summary>
        private static string DescribeMissingFacingParts(CutsceneSlot slot)
        {
            string problem = CutsceneDirectionVariants.DescribeFacingRigProblem(slot);
            return problem == null ? string.Empty : " ⚠ " + problem;
        }

        private void BuildSceneBindingRow(int slotIndex)
        {
            string currentSceneGuid = CutsceneSceneBinding.CurrentSceneGuid();
            if (string.IsNullOrEmpty(cutscene.sceneGuid) || currentSceneGuid != cutscene.sceneGuid)
            {
                inspectorScroll.Add(new Label("Open the remembered scene to bind this slot.")
                { style = { whiteSpace = WhiteSpace.Normal, marginTop = 8f } });
                return;
            }

            uint slotId = cutscene.slots[slotIndex].SlotId;
            CutsceneSlotBindingEntry existing =
                CutsceneSceneBinding.FindBinding(cutscene, currentSceneGuid, slotId);
            GameObject boundObject = existing != null
                ? CutsceneSceneBinding.ResolveGameObject(existing.globalObjectId)
                : null;

            ObjectField bindField = new ObjectField("Scene Object")
            {
                objectType = typeof(GameObject),
                allowSceneObjects = true,
                value = boundObject
            };
            bindField.style.marginTop = 8f;
            bindField.RegisterValueChangedCallback(changeEvent =>
            {
                CutsceneSceneBinding.SetBinding(
                    serializedObject, currentSceneGuid, slotId, changeEvent.newValue as GameObject);
                serializedObject.Update();
            });
            inspectorScroll.Add(bindField);
        }

        private void BuildClipBlockInspector(int slotIndex, int blockIndex)
        {
            CutsceneSlot slot = cutscene.slots[slotIndex];
            if (blockIndex < 0 || blockIndex >= slot.clipBlocks.Count)
            {
                return;
            }

            SerializedProperty blockProperty = serializedObject.FindProperty("slots")
                .GetArrayElementAtIndex(slotIndex).FindPropertyRelative("clipBlocks")
                .GetArrayElementAtIndex(blockIndex);

            inspectorScroll.Add(BuildHeading("Clip Block"));

            List<ClipAsset> availableClips = BuildAvailableClips(slot);
            List<string> labels = new List<string> { "(none)" };
            int currentChoice = 0;
            for (int i = 0; i < availableClips.Count; i++)
            {
                labels.Add(availableClips[i].name);
                if (availableClips[i].stableId == slot.clipBlocks[blockIndex].clipId)
                {
                    currentChoice = i + 1;
                }
            }

            DropdownField clipDropdown = new DropdownField("Clip", labels, currentChoice);
            clipDropdown.RegisterValueChangedCallback(changeEvent =>
            {
                int chosenIndex = labels.IndexOf(changeEvent.newValue);
                ulong clipId = chosenIndex > 0 ? availableClips[chosenIndex - 1].stableId : 0UL;
                blockProperty.FindPropertyRelative("clipId").longValue = unchecked((long)clipId);
                serializedObject.ApplyModifiedProperties();
                RebuildTimeline();
            });
            inspectorScroll.Add(clipDropdown);

            AddBoundField(blockProperty, "start", "Start (s)");
            AddBoundField(blockProperty, "duration", "Duration (s)");
            AddBoundField(blockProperty, "loop", "Loop");
            AddBoundField(blockProperty, "speed", "Speed");
            AddBoundField(blockProperty, "clipStartOffsetSeconds", "Start Offset (s)");
        }

        private void BuildTransformKeyInspector(string listPropertyPath, int keyIndex, int keyCount)
        {
            if (keyIndex < 0 || keyIndex >= keyCount)
            {
                return;
            }
            SerializedProperty listProperty = serializedObject.FindProperty(listPropertyPath);
            SerializedProperty keyProperty = listProperty.GetArrayElementAtIndex(keyIndex);

            inspectorScroll.Add(BuildHeading("Key"));
            AddBoundField(keyProperty, "time", "Time (s)");
            AddBoundField(keyProperty, "position", "Position");
            AddBoundField(keyProperty, "rotation", "Rotation");
            AddBoundField(keyProperty, "scale", "Scale");
            AddEasingCurveEditor(
                keyProperty, AddBoundField(keyProperty, "interpolation", "Interpolation"));
        }

        private void BuildFacingKeyInspector(int slotIndex, int keyIndex)
        {
            CutsceneSlot slot = cutscene.slots[slotIndex];
            if (keyIndex < 0 || keyIndex >= slot.facingKeys.Count)
            {
                return;
            }
            SerializedProperty keyProperty = serializedObject.FindProperty("slots")
                .GetArrayElementAtIndex(slotIndex).FindPropertyRelative("facingKeys")
                .GetArrayElementAtIndex(keyIndex);

            inspectorScroll.Add(BuildHeading("Facing Override"));
            AddBoundField(keyProperty, "time", "Time (s)");
            AddBoundField(keyProperty, "angleDegrees", "Angle (0-360)");
        }

        private void BuildPartTrackHeaderInspector(int slotIndex, int trackIndex)
        {
            CutsceneSlot slot = cutscene.slots[slotIndex];
            if (trackIndex < 0 || trackIndex >= slot.partTracks.Count)
            {
                return;
            }
            SerializedProperty trackProperty = serializedObject.FindProperty("slots")
                .GetArrayElementAtIndex(slotIndex).FindPropertyRelative("partTracks")
                .GetArrayElementAtIndex(trackIndex);

            inspectorScroll.Add(BuildHeading("Part Track"));

            uint tagId = slot.partTracks[trackIndex].tagId;
            string tagName = VocabularyRegistryProvider.TargetTags.FindName(tagId);
            Button tagButton = new Button { text = "Tag: " + (tagName ?? "0x" + tagId.ToString("X8")) };
            tagButton.clicked += () =>
            {
                TargetTagRegistry tagRegistry = VocabularyRegistryProvider.TargetTags;
                VocabularyPicker.Open(
                    this, tagButton, tagRegistry, tagRegistry,
                    VocabularyPickerConfig.ForTrackTagRebind(tagRegistry),
                    chosenTagId =>
                    {
                        trackProperty.FindPropertyRelative("tagId").uintValue = chosenTagId;
                        serializedObject.ApplyModifiedProperties();
                        RebuildAll();
                    },
                    () => RebuildAll());
            };
            inspectorScroll.Add(tagButton);

            AddBoundField(trackProperty, "channels", "Channels");
        }

        private void BuildAttachMarkerInspector(int slotIndex, int markerIndex)
        {
            CutsceneSlot slot = cutscene.slots[slotIndex];
            if (markerIndex < 0 || markerIndex >= slot.attachMarkers.Count)
            {
                return;
            }

            CutsceneAttachMarker marker = slot.attachMarkers[markerIndex];
            SerializedProperty markerProperty = serializedObject.FindProperty("slots")
                .GetArrayElementAtIndex(slotIndex).FindPropertyRelative("attachMarkers")
                .GetArrayElementAtIndex(markerIndex);

            inspectorScroll.Add(BuildHeading("Attach"));
            AddBoundField(markerProperty, "time", "Time (s)");

            PropertyField kindField = new PropertyField(markerProperty.FindPropertyRelative("kind"), "Kind");
            kindField.Bind(serializedObject);
            // A full rebuild, not just a repaint: the fields below differ by kind, and the lane's
            // own marker shape is read off this value too. Guarded, because binding raises this
            // event too and an unguarded rebuild here flickers the whole inspector.
            kindField.RegisterCallback<ChangeEvent<string>>(changeEvent =>
            {
                if (ShouldIgnoreBindingEcho(changeEvent))
                {
                    return;
                }
                RebuildAll();
            });
            inspectorScroll.Add(kindField);

            if (marker.kind == CutsceneAttachKind.Detach)
            {
                AddBoundField(markerProperty, "detachImpulse", "Impulse (host space)");
                inspectorScroll.Add(BuildInspectorNote(
                    "The impulse is handed to the host through CutsceneDetachSignal; the toolkit " +
                    "applies no physics of its own."));
                return;
            }

            BuildHostSlotDropdown(slot, slotIndex, markerProperty, marker);
            CutsceneSlot hostSlot = FindSlotById(marker.hostSlotId);
            BuildSocketDropdown(hostSlot, markerProperty, marker);

            AddBoundField(markerProperty, "localOffset", "Offset");
            if (marker.socketId == 0u)
            {
                AddBoundField(markerProperty, "localEulerDegrees", "Rotation");
            }
            AddBoundField(markerProperty, "hideWhileAttached", "Hide While Attached");
        }

        private void BuildMarkKeyInspector(int slotIndex, int markIndex)
        {
            CutsceneSlot slot = cutscene.slots[slotIndex];
            if (markIndex < 0 || markIndex >= slot.markKeys.Count)
            {
                return;
            }
            SerializedProperty markProperty = serializedObject.FindProperty("slots")
                .GetArrayElementAtIndex(slotIndex).FindPropertyRelative("markKeys")
                .GetArrayElementAtIndex(markIndex);

            inspectorScroll.Add(BuildHeading("Mark"));
            AddBoundField(markProperty, "time", "Time (s)");
            AddBoundField(markProperty, "position", "Position (world)");
            AddBoundField(markProperty, "facingDegrees", "Facing (0-360)");
            AddBoundField(markProperty, "toleranceMeters", "Tolerance (m)");
            AddBoundField(markProperty, "timeoutSeconds", "Timeout (s, 0 = wait)");
            AddBoundField(markProperty, "previewTravelSeconds", "Preview Travel (s)");

            GameObject boundObject = FindBoundObject(slotIndex);
            Button setFromObjectButton = new Button(() => SetMarkFromBoundObject(slotIndex, markIndex))
            {
                text = "Set From Object",
                tooltip = boundObject != null
                    ? "Moves this mark to where '" + boundObject.name + "' currently stands."
                    : "Bind this slot to a scene object first."
            };
            setFromObjectButton.SetEnabled(boundObject != null);
            setFromObjectButton.style.marginTop = 6f;
            inspectorScroll.Add(setFromObjectButton);

            inspectorScroll.Add(BuildInspectorNote(
                "The toolkit orders the move and judges arrival by distance; the host walks the " +
                "entity there. Timeout 0 waits forever."));
        }

        /// <summary>
        /// Drops the mark where the bound object currently stands. Arrival is judged on XZ, but the
        /// authored Y is what the merged root key carries, so the object's own Y comes along.
        /// </summary>
        private void SetMarkFromBoundObject(int slotIndex, int markIndex)
        {
            GameObject boundObject = FindBoundObject(slotIndex);
            if (boundObject == null)
            {
                return;
            }
            SerializedProperty markProperty = serializedObject.FindProperty("slots")
                .GetArrayElementAtIndex(slotIndex).FindPropertyRelative("markKeys")
                .GetArrayElementAtIndex(markIndex);

            Vector3 position = boundObject.transform.position;
            ZeroFloat3(markProperty.FindPropertyRelative("position"), position.x, position.y, position.z);
            markProperty.FindPropertyRelative("facingDegrees").floatValue =
                boundObject.transform.rotation.eulerAngles.y;
            serializedObject.ApplyModifiedProperties();
            RebuildAll();
        }

        private void BuildHostSlotDropdown(
            CutsceneSlot slot, int slotIndex, SerializedProperty markerProperty, CutsceneAttachMarker marker)
        {
            List<uint> hostSlotIds = new List<uint>();
            List<string> hostLabels = new List<string>();
            int currentChoice = -1;
            for (int otherIndex = 0; otherIndex < cutscene.slots.Count; otherIndex++)
            {
                CutsceneSlot otherSlot = cutscene.slots[otherIndex];
                if (otherIndex == slotIndex || otherSlot == null)
                {
                    continue;
                }
                if (otherSlot.SlotId == marker.hostSlotId)
                {
                    currentChoice = hostLabels.Count;
                }
                hostSlotIds.Add(otherSlot.SlotId);
                hostLabels.Add(otherSlot.name);
            }

            if (hostLabels.Count == 0)
            {
                inspectorScroll.Add(BuildInspectorNote(
                    "This cutscene has no other slot to ride. Add one to the cast first."));
                return;
            }

            DropdownField hostDropdown = new DropdownField("Host", hostLabels, Mathf.Max(0, currentChoice));
            hostDropdown.RegisterValueChangedCallback(changeEvent =>
            {
                int chosenIndex = hostLabels.IndexOf(changeEvent.newValue);
                if (chosenIndex < 0)
                {
                    return;
                }
                markerProperty.FindPropertyRelative("hostSlotId").uintValue = hostSlotIds[chosenIndex];
                // The socket belongs to the old host's rig; a new host makes it meaningless.
                markerProperty.FindPropertyRelative("socketId").uintValue = 0u;
                serializedObject.ApplyModifiedProperties();
                // Deferred: RebuildAll destroys this very dropdown, and doing that while it is still
                // dispatching its own change event leaves the callback running on a dead element.
                schedule.Execute(RebuildAll);
            });
            inspectorScroll.Add(hostDropdown);
        }

        private void BuildSocketDropdown(
            CutsceneSlot hostSlot, SerializedProperty markerProperty, CutsceneAttachMarker marker)
        {
            if (hostSlot == null || hostSlot.kind != CutsceneSlotKind.Actor || hostSlot.rig == null
                || hostSlot.rig.sockets == null || hostSlot.rig.sockets.Count == 0)
            {
                // A Prop host, or an Actor whose rig declares no sockets, can only be ridden at its
                // root — offering an empty dropdown would suggest otherwise.
                return;
            }

            List<uint> socketIds = new List<uint> { 0u };
            List<string> socketLabels = new List<string> { "(root)" };
            int currentChoice = 0;
            for (int socketIndex = 0; socketIndex < hostSlot.rig.sockets.Count; socketIndex++)
            {
                SocketDefinition socket = hostSlot.rig.sockets[socketIndex];
                if (socket == null || !socket.Id.IsValid)
                {
                    continue;
                }
                if (socket.Id.Value == marker.socketId)
                {
                    currentChoice = socketLabels.Count;
                }
                socketIds.Add(socket.Id.Value);
                socketLabels.Add(socket.displayName);
            }

            DropdownField socketDropdown = new DropdownField("Socket", socketLabels, currentChoice);
            socketDropdown.RegisterValueChangedCallback(changeEvent =>
            {
                int chosenIndex = socketLabels.IndexOf(changeEvent.newValue);
                if (chosenIndex < 0)
                {
                    return;
                }
                markerProperty.FindPropertyRelative("socketId").uintValue = socketIds[chosenIndex];
                serializedObject.ApplyModifiedProperties();
                schedule.Execute(RebuildAll);
            });
            inspectorScroll.Add(socketDropdown);

            SocketDefinition chosenSocket = FindSocketById(hostSlot.rig, marker.socketId);
            if (chosenSocket != null && chosenSocket.mode == SocketAttachMode.Bone)
            {
                inspectorScroll.Add(BuildInspectorNote(
                    "Bone sockets preview at the host root. Playback places them correctly."));
            }
        }

        private CutsceneSlot FindSlotById(uint slotId)
        {
            for (int slotIndex = 0; slotIndex < cutscene.slots.Count; slotIndex++)
            {
                if (cutscene.slots[slotIndex] != null && cutscene.slots[slotIndex].SlotId == slotId)
                {
                    return cutscene.slots[slotIndex];
                }
            }
            return null;
        }

        private static SocketDefinition FindSocketById(RigAsset rig, uint socketId)
        {
            if (socketId == 0u || rig == null || rig.sockets == null)
            {
                return null;
            }
            for (int socketIndex = 0; socketIndex < rig.sockets.Count; socketIndex++)
            {
                if (rig.sockets[socketIndex] != null && rig.sockets[socketIndex].Id.Value == socketId)
                {
                    return rig.sockets[socketIndex];
                }
            }
            return null;
        }

        private static Label BuildInspectorNote(string text)
        {
            Label note = new Label(text);
            note.style.whiteSpace = WhiteSpace.Normal;
            note.style.marginTop = 4f;
            note.style.opacity = 0.75f;
            return note;
        }

        private void BuildCameraKeyInspector(int keyIndex)
        {
            if (keyIndex < 0 || keyIndex >= cutscene.cameraLane.keys.Count)
            {
                return;
            }
            SerializedProperty keyProperty = serializedObject.FindProperty("cameraLane")
                .FindPropertyRelative("keys").GetArrayElementAtIndex(keyIndex);

            inspectorScroll.Add(BuildHeading("Camera Key"));
            AddBoundField(keyProperty, "time", "Time (s)");
            AddBoundField(keyProperty, "position", "Position");
            AddBoundField(keyProperty, "rotation", "Rotation");
            AddBoundField(keyProperty, "fieldOfView", "Field Of View");
            AddEasingCurveEditor(
                keyProperty, AddBoundField(keyProperty, "interpolation", "Interpolation"));

            Button alignButton = new Button(() => AlignCameraKeyToSceneView(keyIndex)) { text = "Align to Scene View" };
            alignButton.style.marginTop = 6f;
            inspectorScroll.Add(alignButton);
        }

        private void AlignCameraKeyToSceneView(int keyIndex)
        {
            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null || sceneView.camera == null)
            {
                return;
            }
            Transform cameraTransform = sceneView.camera.transform;
            SerializedProperty keyProperty = serializedObject.FindProperty("cameraLane")
                .FindPropertyRelative("keys").GetArrayElementAtIndex(keyIndex);
            Vector3 position = cameraTransform.position;
            Vector3 eulerAngles = cameraTransform.eulerAngles;
            ZeroFloat3(keyProperty.FindPropertyRelative("position"), position.x, position.y, position.z);
            ZeroFloat3(keyProperty.FindPropertyRelative("rotation"), eulerAngles.x, eulerAngles.y, eulerAngles.z);
            keyProperty.FindPropertyRelative("fieldOfView").floatValue = sceneView.camera.fieldOfView;
            CommitStructuralChange();
        }

        private void BuildEventInspector(int eventIndex)
        {
            if (eventIndex < 0 || eventIndex >= cutscene.events.Count)
            {
                return;
            }
            SerializedProperty eventProperty =
                serializedObject.FindProperty("events").GetArrayElementAtIndex(eventIndex);

            inspectorScroll.Add(BuildHeading("Event"));
            AddBoundField(eventProperty, "time", "Time (s)");
            AddBoundField(eventProperty, "eventKey", "Event Key");

            // The payload's own container, so a host provider can own it whole: "sequence id 7" is
            // a number here and a named line in the game that authored it.
            VisualElement payloadContainer = new VisualElement();
            inspectorScroll.Add(payloadContainer);
            if (!CutsceneEventInspectorProviders.TryBuild(
                    cutscene.events[eventIndex].eventKey, eventProperty, payloadContainer))
            {
                AddBoundField(eventProperty, "intParam", "Int Param", payloadContainer);
                AddBoundField(eventProperty, "floatParam", "Float Param", payloadContainer);
            }

            AddBoundField(eventProperty, "fireOnSkip", "Fire On Skip");

            // Not AddBoundField: this one changes the marker's glyph and adds a ghost to the Holds
            // row, so it rebuilds the timeline - and a rebuild driven by an unfiltered bind echo is
            // the flicker ShouldIgnoreBindingEcho exists for.
            PropertyField holdField = new PropertyField(
                eventProperty.FindPropertyRelative("holdUntilReleased"), "Hold Until Released");
            holdField.Bind(serializedObject);
            holdField.RegisterCallback<ChangeEvent<bool>>(changeEvent =>
            {
                if (ShouldIgnoreBindingEcho(changeEvent))
                {
                    return;
                }
                RebuildTimeline();
            });
            inspectorScroll.Add(holdField);

            string derivedHoldId;
            CutsceneDerivedHolds.TryResolveHoldId(cutscene.events[eventIndex].eventKey, out derivedHoldId);
            Label holdNote = new Label(cutscene.events[eventIndex].holdUntilReleased
                ? "Holds the clock as '" + derivedHoldId + "' until the host releases that id."
                : "Fires and moves on.");
            holdNote.style.whiteSpace = WhiteSpace.Normal;
            holdNote.style.marginTop = 2f;
            inspectorScroll.Add(holdNote);
        }

        private void BuildHoldInspector(int holdIndex)
        {
            if (holdIndex < 0 || holdIndex >= cutscene.holdMarkers.Count)
            {
                return;
            }
            SerializedProperty holdProperty =
                serializedObject.FindProperty("holdMarkers").GetArrayElementAtIndex(holdIndex);

            inspectorScroll.Add(BuildHeading("Hold Marker"));
            AddBoundField(holdProperty, "time", "Time (s)");
            AddBoundField(holdProperty, "holdId", "Hold Id");
        }

        private PropertyField AddBoundField(
            SerializedProperty parent, string relativePropertyName, string label)
        {
            return AddBoundField(parent, relativePropertyName, label, inspectorScroll);
        }

        private PropertyField AddBoundField(
            SerializedProperty parent, string relativePropertyName, string label, VisualElement container)
        {
            SerializedProperty property = parent.FindPropertyRelative(relativePropertyName);
            PropertyField field = new PropertyField(property, label);
            field.Bind(serializedObject);
            // Requested, never direct: a bound field's own drag handle captures the pointer on
            // itself, so rebuilding the timeline here would end the drag after about one pixel.
            field.RegisterCallback<SerializedPropertyChangeEvent>(_ => RequestTimelineRebuild());
            container.Add(field);
            return field;
        }

        private void AddEasingCurveEditor(SerializedProperty keyProperty, PropertyField interpolationField)
        {
            SerializedProperty interpolationProperty = keyProperty.FindPropertyRelative("interpolation");
            SerializedProperty startHandleProperty = keyProperty.FindPropertyRelative("bezierStartHandle");
            SerializedProperty endHandleProperty = keyProperty.FindPropertyRelative("bezierEndHandle");

            EasingCurveEditorElement curveEditor = new EasingCurveEditorElement();
            curveEditor.name = EasingCurveEditorElementName;
            ShowKeyCurve(curveEditor, interpolationProperty, startHandleProperty, endHandleProperty);

            // Safe to run straight from the change event because it rebuilds no pane: it repaints
            // the widget in place, so a pointer captured on it is never released mid-drag.
            interpolationField.RegisterCallback<SerializedPropertyChangeEvent>(
                _ => ShowKeyCurve(
                    curveEditor, interpolationProperty, startHandleProperty, endHandleProperty));

            curveEditor.curveEdited += (draggedStartHandle, draggedEndHandle) =>
            {
                // Reshaping any preset is what makes the key a Bezier, so the mode is written with
                // the handles. Unity collapses the whole gesture's writes into one Undo step.
                interpolationProperty.enumValueIndex = (int)Interpolation.Bezier;
                WriteFloat2(startHandleProperty, draggedStartHandle);
                WriteFloat2(endHandleProperty, draggedEndHandle);
                serializedObject.ApplyModifiedProperties();

                // Re-poses the scene rather than rebuilding a pane, so the new easing is visible
                // while the handle is still held and the captured pointer survives the write.
                ApplyPreviewAtPlayhead();
            };

            inspectorScroll.Add(curveEditor);
            inspectorScroll.Add(BuildInspectorNote(
                "Drag the handles to reshape the curve - that turns the key into a custom Bezier. "
                + "On every other mode the shape is drawn for reference and does not accept a drag."));
        }

        // A key on a fixed mode ignores its stored handles, so the widget draws that mode's shape
        // but must not let a drag rewrite the key: input is switched off rather than the widget
        // hidden, because the shape is what tells the author what the mode does.
        private static void ShowKeyCurve(
            EasingCurveEditorElement curveEditor,
            SerializedProperty interpolationProperty,
            SerializedProperty startHandleProperty,
            SerializedProperty endHandleProperty)
        {
            Interpolation interpolation = (Interpolation)interpolationProperty.enumValueIndex;
            curveEditor.SetCurveWithoutNotify(
                interpolation, ReadFloat2(startHandleProperty), ReadFloat2(endHandleProperty));
            curveEditor.pickingMode = interpolation == Interpolation.Bezier
                ? PickingMode.Position
                : PickingMode.Ignore;
        }

        private static Label BuildHeading(string text)
        {
            Label heading = new Label(text);
            heading.AddToClassList("clip-editor__heading");
            return heading;
        }
    }
}
