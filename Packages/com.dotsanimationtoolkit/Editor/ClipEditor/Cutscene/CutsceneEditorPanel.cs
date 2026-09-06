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
    /// <summary>
    /// The Cutscene Editor tab's content (Phase G, G2): a slot/lane timeline plus an inspector for
    /// whatever is selected. Unity's own Scene view is the viewport (spec §3) — this panel is
    /// timeline and inspector only, exactly as the spec's cover-pane shape for every other tab
    /// already establishes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>v1 scope cuts, recorded rather than hidden.</strong> The header column scrolls
    /// horizontally together with the lanes rather than staying frozen (a real Clip-Editor-style
    /// frozen header needs synchronized dual scroll regions this pass did not build); only one item
    /// drags at a time, no box-select and no multi-key drag; a moment lane's "add" always inserts a
    /// bare default and leaves filling it in to the inspector. All three are candidates for a later
    /// visual pass, not correctness gaps — every add/move/resize/delete this spec calls for works.
    /// </para>
    /// <para>
    /// <strong>Everything routes through <see cref="serializedObject"/>.</strong> Every mutation is a
    /// <see cref="SerializedProperty"/> write plus <see cref="SerializedObject.ApplyModifiedProperties"/>,
    /// the same "buys Undo, dirtying and prefab-override handling for free" reasoning
    /// <c>RigAssetEditor</c> documents. The one exception is <see cref="CutsceneAsset.EnsureStableIds"/>,
    /// which mints a fresh slot's id by writing the object directly — SerializedProperty has no
    /// notion of "mint a fresh id", so it runs immediately after every structural change, before the
    /// object is re-read back into <see cref="serializedObject"/>.
    /// </para>
    /// </remarks>
    public sealed class CutsceneEditorPanel : VisualElement
    {
        private enum SelectedLaneKind
        {
            None,
            ClipBlock,
            RootTransformKey,
            FacingKey,
            PartTrackHeader,
            PartTrackKey,
            AttachMarker,
            CameraKey,
            Event,
            Hold
        }

        private const float LaneRowHeight = 22f;
        private const float RulerHeight = 24f;
        private const float HeaderColumnWidth = 150f;
        private const float TrailingSeconds = 5f;

        private CutsceneAsset cutscene;
        private SerializedObject serializedObject;

        private ObjectField cutsceneField;
        private Label sceneStatusLabel;
        private Button sceneActionButton;
        private Slider zoomSlider;
        private Toggle previewShotToggle;
        private float pixelsPerSecond = 40f;
        private float playheadSeconds;

        private ScrollView timelineScrollView;
        private ScrollView inspectorScroll;
        private CutsceneTimelinePlayheadElement playheadElement;
        private CutsceneCastPanel castPanel;

        private CutsceneViewportElement viewportElement;
        private VisualElement viewportOverlay;
        private Label viewportMessageLabel;
        private Button viewportActionButton;
        private Toggle shotModeToggle;

        /// <summary>Viewport locked to the camera lane (Shot) vs. the free orbit rig. Shot by default: scrubbing should show the framed movie (A59 §3.3).</summary>
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

        /// <summary>Index into <see cref="CutsceneAsset.holdMarkers"/> the transport is waiting on, or −1.</summary>
        private int gatingHoldIndex = -1;

        private int selectedSlotIndex = -1;
        private SelectedLaneKind selectedLaneKind = SelectedLaneKind.None;
        private int selectedPartTrackIndex = -1;
        private int selectedItemIndex = -1;

        private readonly CutscenePreviewController previewController = new CutscenePreviewController();

        public CutsceneEditorPanel()
        {
            style.flexGrow = 1f;
            style.flexDirection = FlexDirection.Column;

            // G-D1: a scrub must never survive into a saved scene. Exiting before the write is the
            // only correct order — sceneSaving fires before the scene file is actually written.
            EditorSceneManager.sceneSaving += OnSceneSaving;
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                EditorSceneManager.sceneSaving -= OnSceneSaving;
                StopPlayback();
                previewController.ExitPreview();
            });

            Add(BuildToolbar());
            Add(BuildTransportRow());

            // A59 §3.1: the tab is a whole tool — cast | viewport | inspector over the timeline.
            VisualElement timelineArea = new VisualElement();
            timelineArea.style.flexDirection = FlexDirection.Column;
            timelineArea.style.minHeight = 120f;

            timelineArea.Add(BuildAddSlotRow());

            timelineScrollView = new ScrollView(ScrollViewMode.VerticalAndHorizontal);
            timelineScrollView.style.flexGrow = 1f;
            timelineArea.Add(timelineScrollView);

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
            inspectorScroll.style.width = 300f;
            inspectorScroll.style.flexShrink = 0f;
            inspectorScroll.style.paddingLeft = 6f;
            inspectorScroll.style.paddingRight = 6f;
            inspectorScroll.style.paddingTop = 6f;
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
            // timeline group — the other half of "selection syncs both ways" (A58 §3.3).
            Selection.selectionChanged += OnUnitySelectionChanged;
            RegisterCallback<DetachFromPanelEvent>(_ => Selection.selectionChanged -= OnUnitySelectionChanged);

            RestoreSessionCutscene();
            RebuildAll();
        }

        // -----------------------------------------------------------------------------------
        // Loading and the scene remember/open flow (spec §3).
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
            selectedSlotIndex = -1;
            selectedLaneKind = SelectedLaneKind.None;
            selectedPartTrackIndex = -1;
            selectedItemIndex = -1;
            cutsceneField.SetValueWithoutNotify(cutscene);
            RebuildAll();
        }

        /// <summary>Called by <c>ClipEditorWindow.ShowCutsceneTab</c> when the tab is switched away from (G-D1: tab switch restores the preview).</summary>
        internal void OnHidden()
        {
            StopPlayback();
            previewController.ExitPreview();
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
                    + "playhead — move it with Unity's own gizmo first (spec §3)."
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

            // Off by default since A59: the in-tab viewport's Shot mode shows the framed movie, so
            // yanking the author's Scene view camera around on every scrub became opt-in.
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
        // Editor play transport (A58 §3.2). A rehearsal of runtime pacing, holds included.
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

            transportStatusLabel = new Label(string.Empty);
            transportStatusLabel.style.marginLeft = 12f;
            transportStatusLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            row.Add(transportStatusLabel);

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
            gatingHoldIndex = -1;
            SetPlaying(true);
        }

        private void StopPlayback()
        {
            bool wasRunning = isPlaying || gatingHoldIndex >= 0;
            SetPlaying(false);
            gatingHoldIndex = -1;
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
            if (gatingHoldIndex < 0)
            {
                return;
            }
            // Stepping a hair past the marker so the same hold is not re-detected on the next tick.
            gatingHoldIndex = -1;
            playheadSeconds += HoldReleaseEpsilon;
            RefreshTransportStatus();
        }

        /// <summary>Nudge past a released hold, well under one frame at any sane speed.</summary>
        private const float HoldReleaseEpsilon = 1e-3f;

        /// <summary>
        /// One transport frame: advance the elastic clock, stop dead on a hold, and re-pose.
        /// </summary>
        /// <remarks>
        /// <strong>A hold freezes the cutscene clock, not the actors.</strong> At run time
        /// <c>PlaybackTimeSystem</c> keeps advancing every layer while
        /// <c>CutsceneTimelineSystem</c> sits paused, so a looping walk keeps cycling and the camera
        /// holds its shot (spec §2). <see cref="CutscenePreviewController.HoldClipPhaseSeconds"/>
        /// is the editor's copy of that: seconds the clips advanced while the timeline did not.
        /// </remarks>
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

            if (gatingHoldIndex >= 0)
            {
                previewController.HoldClipPhaseSeconds += elapsed * playbackSpeed;
                ApplyPreviewAtPlayhead();
                return;
            }

            float advancedTime = playheadSeconds + elapsed * playbackSpeed;

            int crossedHoldIndex = skipHoldsToggle != null && skipHoldsToggle.value
                ? -1
                : FindFirstHoldCrossed(playheadSeconds, advancedTime);
            if (crossedHoldIndex >= 0)
            {
                gatingHoldIndex = crossedHoldIndex;
                SetPlayhead(cutscene.holdMarkers[crossedHoldIndex].time);
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

        /// <summary>The first hold marker strictly after <paramref name="fromSeconds"/> and at or before <paramref name="toSeconds"/>.</summary>
        private int FindFirstHoldCrossed(float fromSeconds, float toSeconds)
        {
            int firstIndex = -1;
            if (cutscene.holdMarkers == null)
            {
                return -1;
            }
            for (int holdIndex = 0; holdIndex < cutscene.holdMarkers.Count; holdIndex++)
            {
                CutsceneHoldMarker holdMarker = cutscene.holdMarkers[holdIndex];
                if (holdMarker == null || holdMarker.time <= fromSeconds || holdMarker.time > toSeconds)
                {
                    continue;
                }
                if (firstIndex < 0 || holdMarker.time < cutscene.holdMarkers[firstIndex].time)
                {
                    firstIndex = holdIndex;
                }
            }
            return firstIndex;
        }

        /// <summary>
        /// Moves the playhead and re-poses, without rebuilding the timeline.
        /// </summary>
        /// <remarks>
        /// The playhead element repaints itself from its own <c>TimeSeconds</c>; rebuilding every
        /// lane each frame is what would make a 30s vignette churn the editor (A58 §6).
        /// </remarks>
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

            if (gatingHoldIndex >= 0 && cutscene != null && gatingHoldIndex < cutscene.holdMarkers.Count)
            {
                string holdId = cutscene.holdMarkers[gatingHoldIndex].holdId;
                transportStatusLabel.text =
                    "Holding on '" + (string.IsNullOrEmpty(holdId) ? "(unnamed hold)" : holdId) + "'.";
                continueButton.style.display = DisplayStyle.Flex;
                return;
            }

            // No running time in the label: it would rebuild this row's layout every tick, and the
            // playhead already says where the clock is.
            continueButton.style.display = DisplayStyle.None;
            transportStatusLabel.text = isPlaying ? "Playing" : string.Empty;
        }

        /// <summary>
        /// Keys the current live pose of whatever is selected — a slot's root, or a part track — at
        /// the playhead (spec §3). Requires the preview to be active (the remembered scene open and
        /// the slot bound), the same gate <see cref="BuildSceneBindingRow"/> already shows a note for.
        /// </summary>
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
        // The in-tab scene viewport (amendment A59).
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
        /// Free (or a cutscene with no camera keys yet) renders the orbit rig (A59 §3.3).
        /// </summary>
        private void RenderViewport()
        {
            if (viewportElement == null)
            {
                return;
            }

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

        /// <summary>
        /// The viewport hosts its own state instead of a toolbar warning nobody reads (A59 §3.1):
        /// no cutscene, no remembered scene, or the wrong scene open all land here.
        /// </summary>
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
            else if (CutsceneSceneBindingUtility.CurrentSceneGuid() != cutscene.sceneGuid)
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

            string currentGuid = CutsceneSceneBindingUtility.CurrentSceneGuid();

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
                sceneGuidProperty.stringValue = CutsceneSceneBindingUtility.CurrentSceneGuid();
                scenePathProperty.stringValue = CutsceneSceneBindingUtility.CurrentScenePath();
                serializedObject.ApplyModifiedProperties();
            }
            else
            {
                CutsceneSceneBindingUtility.TryOpenScene(cutscene.scenePath);
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
            RebuildTimeline();
            RebuildInspector();
            RefreshCastPanel();
            RefreshViewportOverlay();
            RenderViewport();
        }

        /// <summary>
        /// Enters preview the moment the remembered scene is the open one, and exits it the moment
        /// it is not — spec §3's "wrong scene open" warning already covers timing edits staying live;
        /// this is the posing half of that same rule (G-D1).
        /// </summary>
        private void SyncPreviewActivation()
        {
            if (cutscene == null)
            {
                previewController.ExitPreview();
                return;
            }

            string currentSceneGuid = CutsceneSceneBindingUtility.CurrentSceneGuid();
            bool shouldBeActive = !string.IsNullOrEmpty(cutscene.sceneGuid) && currentSceneGuid == cutscene.sceneGuid;

            if (shouldBeActive && !previewController.IsActive)
            {
                // The in-tab viewport is the primary surface now (A59); a Scene view is only
                // required when the author opted into driving its camera.
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

        /// <summary>
        /// Makes sure there is a Scene view to preview into (A58 §3.4). The docked
        /// Hierarchy + Scene view + Cutscene window arrangement is the intended workflow, and a
        /// preview posing objects nobody can see is the shape of the defect A58 exists to fix.
        /// </summary>
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
            selectedSlotIndex = slotIndex;
            selectedLaneKind = SelectedLaneKind.None;
            selectedPartTrackIndex = -1;
            SyncSceneSelectionToTimelineSelection();
            RebuildTimeline();
            RebuildInspector();
            RefreshCastPanel();
        }

        // -----------------------------------------------------------------------------------
        // Cast panel: staging the scene from the tool (A58 §3.3).
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Instantiates a slot's actor prefab at the Scene view pivot and binds it, as one Undo step.
        /// </summary>
        /// <remarks>
        /// <strong>A real scene edit, deliberately outside the preview's capture/restore
        /// (decision A58-D3).</strong> The preview poses objects and un-poses them on exit;
        /// placement creates one that is meant to survive a save. Preview is exited first so the
        /// new object is captured at its authored rest pose on re-entry rather than mid-scrub.
        /// </remarks>
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
            string currentSceneGuid = CutsceneSceneBindingUtility.CurrentSceneGuid();
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

            CutsceneSceneBindingUtility.SetBinding(serializedObject, currentSceneGuid, slot.SlotId, placed);
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
            string currentSceneGuid = CutsceneSceneBindingUtility.CurrentSceneGuid();
            if (string.IsNullOrEmpty(currentSceneGuid))
            {
                return;
            }

            // Re-entered rather than patched: the controller captures rest poses on entry, and a
            // slot bound mid-preview would otherwise never have any.
            StopPlayback();
            previewController.ExitPreview();
            CutsceneSceneBindingUtility.SetBinding(
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
                cutscene, CutsceneSceneBindingUtility.CurrentSceneGuid(), Selection.activeGameObject);
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
            string currentSceneGuid = CutsceneSceneBindingUtility.CurrentSceneGuid();
            castPanel.Rebuild(cutscene, currentSceneGuid, selectedSlotIndex);
            castPanel.SetStageStatus(ComputeStageStatusText(currentSceneGuid));
        }

        // -----------------------------------------------------------------------------------
        // Sync to Stage (amendment A61-T3): writes the cast panel's resolved bindings into this
        // scene's CutsceneStageAuthoring component, ready for CutsceneStageBaker to bake at the next
        // subscene reopen or Play. A61-D2: explicit only, never triggered by a Bind/Place click.
        // -----------------------------------------------------------------------------------

        private void SyncCutsceneToStage()
        {
            if (cutscene == null)
            {
                return;
            }

            string currentSceneGuid = CutsceneSceneBindingUtility.CurrentSceneGuid();
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
                // Nothing bound and no stage to update — Stage stays "none" (spec §3.3).
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
                    CutsceneSceneBindingUtility.FindBinding(cutscene, currentSceneGuid, slot.SlotId);
                if (entry == null || string.IsNullOrEmpty(entry.globalObjectId))
                {
                    continue;
                }
                GameObject boundObject = CutsceneSceneBindingUtility.ResolveGameObject(entry.globalObjectId);
                if (boundObject != null)
                {
                    resolved.Add(new KeyValuePair<uint, GameObject>(slot.SlotId, boundObject));
                }
            }
            return resolved;
        }

        /// <summary>"Stage: none" / "Stage: synced" / "Stage: out of date" (spec §3.3) — recomputed every <see cref="RefreshCastPanel"/>.</summary>
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

        /// <summary>
        /// Selects whatever GameObject the current timeline selection corresponds to (spec §3), so
        /// Unity's own Move/Rotate/Scale gizmo is already on it — no custom gizmo drawing needed
        /// since preview poses the real scene object, never a mirror.
        /// </summary>
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
            Vector2 preservedScroll = timelineScrollView.scrollOffset;
            timelineScrollView.Clear();

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
                timelineScrollView.Add(emptyHint);
                return;
            }

            serializedObject.Update();

            float contentEnd = ComputeContentEndSeconds();
            float contentWidth = CutsceneTimelineGeometry
                .Create(pixelsPerSecond).TimeToX(contentEnd + TrailingSeconds);

            // A short cutscene must still fill the pane: a 240px ruler floating in a grey void was
            // the single worst thing about the first build (A60). Lanes always reach at least the
            // visible edge; NaN-guarded because the first rebuild runs before any layout pass.
            float visibleWidth = timelineScrollView.contentViewport.resolvedStyle.width;
            if (!float.IsNaN(visibleWidth) && visibleWidth > 0f)
            {
                contentWidth = Mathf.Max(contentWidth, visibleWidth - HeaderColumnWidth);
            }

            VisualElement content = new VisualElement();
            content.style.flexDirection = FlexDirection.Column;
            content.style.position = Position.Relative;

            CutsceneTimelineRulerElement ruler = new CutsceneTimelineRulerElement
            {
                pixelsPerSecond = pixelsPerSecond,
                contentEndSeconds = contentEnd,
                trailingSeconds = TrailingSeconds
            };
            ruler.style.width = contentWidth;
            ruler.style.height = RulerHeight;
            ruler.Scrubbed += OnPlayheadScrubbed;
            content.Add(CreateRow(null, ruler, null));
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
            playheadElement.style.left = HeaderColumnWidth;
            playheadElement.style.top = 0f;
            playheadElement.style.bottom = 0f;
            playheadElement.style.width = contentWidth;
            content.Add(playheadElement);

            timelineScrollView.Add(content);
            timelineScrollView.scrollOffset = preservedScroll;
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

        private VisualElement CreateRow(
            string headerLabel, VisualElement laneElement, Action onHeaderClick,
            bool isGroup = false, string accentClass = null, bool indentLabel = false,
            bool isSelected = false)
        {
            VisualElement row = new VisualElement();
            row.AddToClassList("cutscene-editor__row");
            row.EnableInClassList("cutscene-editor__row--group", isGroup);
            row.EnableInClassList("cutscene-editor__row--selected", isSelected);

            VisualElement headerCell = new VisualElement();
            headerCell.AddToClassList("cutscene-editor__track-header");
            headerCell.EnableInClassList("cutscene-editor__track-header--group", isGroup);
            if (!string.IsNullOrEmpty(accentClass))
            {
                headerCell.AddToClassList("cutscene-editor__track-header--" + accentClass);
            }
            headerCell.style.width = HeaderColumnWidth;
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
            row.Add(headerCell);
            row.Add(laneElement);
            return row;
        }

        // -----------------------------------------------------------------------------------
        // Per-slot rows.
        // -----------------------------------------------------------------------------------

        private void BuildSlotRows(VisualElement content, SerializedProperty slotProperty, int slotIndex, float contentWidth)
        {
            CutsceneSlot slot = cutscene.slots[slotIndex];
            bool isActor = slot.kind == CutsceneSlotKind.Actor;
            string accent = isActor ? "actor" : "prop";

            VisualElement headerRow = CreateRow(
                slot.name,
                new VisualElement { style = { width = contentWidth, height = LaneRowHeight } },
                () => SelectSlotHeader(slotIndex),
                isGroup: true, accentClass: accent,
                isSelected: slotIndex == selectedSlotIndex && selectedLaneKind == SelectedLaneKind.None);
            headerRow.AddManipulator(new ContextualMenuManipulator(menuEvent =>
                menuEvent.menu.AppendAction("Remove Slot", _ => RemoveSlot(slotIndex))));
            content.Add(headerRow);

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
                clipLane.SetBlocks(blockDisplays,
                    selectedSlotIndex == slotIndex && selectedLaneKind == SelectedLaneKind.ClipBlock ? selectedItemIndex : -1);
                clipLane.BlockSelected += index => SelectItem(slotIndex, SelectedLaneKind.ClipBlock, -1, index);
                clipLane.BlockChangeCommitted += (index, start, duration) =>
                    CommitClipBlockChange(clipBlocksProperty, index, start, duration);
                clipLane.EmptySpaceDoubleClicked += time => AddClipBlock(slotIndex, clipBlocksProperty, time);
                clipLane.BlockDeleteRequested += index => DeleteArrayElement(clipBlocksProperty, index);
                content.Add(CreateRow(
                    "Clip", clipLane, () => SelectSlotHeader(slotIndex),
                    accentClass: accent, indentLabel: true));
            }

            SerializedProperty transformKeysProperty = slotProperty.FindPropertyRelative("transformKeys");
            BuildMomentRow(
                content, isActor ? "Root" : "Move", slot.transformKeys, transformKeysProperty,
                slotIndex, SelectedLaneKind.RootTransformKey, -1, contentWidth,
                new Color(0.65f, 0.85f, 0.55f), time => InsertTransformKeyDefault(transformKeysProperty, time),
                accentClass: accent);

            SerializedProperty attachMarkersProperty = slotProperty.FindPropertyRelative("attachMarkers");
            BuildAttachRow(content, slot, attachMarkersProperty, slotIndex, contentWidth, accent);

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
                    BuildMomentRow(
                        content, tagName ?? "0x" + track.tagId.ToString("X8"), track.keys, keysProperty,
                        slotIndex, SelectedLaneKind.PartTrackKey, capturedTrackIndex, contentWidth,
                        new Color(0.75f, 0.55f, 0.85f), time => InsertTransformKeyDefault(keysProperty, time),
                        accentClass: accent);
                    VisualElement partRow = content[content.childCount - 1];
                    partRow.AddManipulator(new ContextualMenuManipulator(menuEvent =>
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
                addPartTrackButton.style.marginBottom = 4f;
                addPartTrackButton.style.fontSize = 10f;
                content.Add(addPartTrackButton);
            }
        }

        private void BuildMomentRow(
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
            lane.SetTimes(times, isSelectedLane ? selectedItemIndex : -1);
            lane.MomentSelected += index => SelectItem(slotIndex, laneKind, partTrackIndex, index);
            lane.MomentMoveCommitted += (index, time) => CommitMomentTime(keysProperty, index, time);
            lane.EmptySpaceDoubleClicked += onAddAtTime;
            lane.MomentDeleteRequested += index => DeleteArrayElement(keysProperty, index);

            content.Add(CreateRow(
                label, lane, () => SelectItem(slotIndex, laneKind, partTrackIndex, -1),
                isGroup: isGroup, accentClass: accentClass, indentLabel: indentLabel,
                isSelected: isSelectedLane && selectedItemIndex < 0));
        }

        private void BuildMomentRow(
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
            lane.SetTimes(times, isSelectedLane ? selectedItemIndex : -1);
            lane.MomentSelected += index => SelectItem(slotIndex, laneKind, partTrackIndex, index);
            lane.MomentMoveCommitted += (index, time) => CommitMomentTime(keysProperty, index, time);
            lane.EmptySpaceDoubleClicked += onAddAtTime;
            lane.MomentDeleteRequested += index => DeleteArrayElement(keysProperty, index);

            content.Add(CreateRow(
                label, lane, () => SelectItem(slotIndex, laneKind, partTrackIndex, -1),
                isGroup: isGroup, accentClass: accentClass, indentLabel: indentLabel,
                isSelected: isSelectedLane && selectedItemIndex < 0));
        }

        /// <summary>
        /// The attach lane (amendment A63 §3.4). Built here rather than through
        /// <see cref="BuildMomentRow"/> because its markers are not all one kind: an Attach and a
        /// Detach get different shapes, which needs the per-marker class overload.
        /// </summary>
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
            lane.SetTimes(times, isSelectedLane ? selectedItemIndex : -1, variantClasses);
            lane.MomentSelected += index => SelectItem(slotIndex, SelectedLaneKind.AttachMarker, -1, index);
            lane.MomentMoveCommitted += (index, time) => CommitMomentTime(attachMarkersProperty, index, time);
            lane.EmptySpaceDoubleClicked += time => InsertAttachMarkerDefault(slotIndex, attachMarkersProperty, time);
            lane.MomentDeleteRequested += index => DeleteArrayElement(attachMarkersProperty, index);

            content.Add(CreateRow(
                "Attach", lane, () => SelectItem(slotIndex, SelectedLaneKind.AttachMarker, -1, -1),
                accentClass: accentClass, indentLabel: true,
                isSelected: isSelectedLane && selectedItemIndex < 0));
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
            lane.SetTimes(times, isSelected ? selectedItemIndex : -1);
            lane.MomentSelected += index => SelectItem(-1, SelectedLaneKind.CameraKey, -1, index);
            lane.MomentMoveCommitted += (index, time) => CommitMomentTime(keysProperty, index, time);
            lane.EmptySpaceDoubleClicked += time => InsertCameraKeyDefault(keysProperty, time);
            lane.MomentDeleteRequested += index => DeleteArrayElement(keysProperty, index);
            content.Add(CreateRow(
                "Camera", lane, () => SelectItem(-1, SelectedLaneKind.CameraKey, -1, -1),
                isGroup: true, accentClass: "camera"));

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
            content.Add(CreateRow("Cuts", cutLane, null, accentClass: "camera", indentLabel: true));
        }

        private void BuildEventRows(VisualElement content, float contentWidth)
        {
            SerializedProperty eventsProperty = serializedObject.FindProperty("events");
            List<float> times = new List<float>(cutscene.events.Count);
            for (int i = 0; i < cutscene.events.Count; i++)
            {
                times.Add(cutscene.events[i].time);
            }

            CutsceneMomentLaneElement lane = new CutsceneMomentLaneElement
            {
                pixelsPerSecond = pixelsPerSecond,
                markerColor = new Color(0.9f, 0.6f, 0.3f),
                style = { width = contentWidth, height = LaneRowHeight }
            };
            bool isSelected = selectedLaneKind == SelectedLaneKind.Event;
            lane.SetTimes(times, isSelected ? selectedItemIndex : -1);
            lane.MomentSelected += index => SelectItem(-1, SelectedLaneKind.Event, -1, index);
            lane.MomentMoveCommitted += (index, time) => CommitMomentTime(eventsProperty, index, time);
            lane.EmptySpaceDoubleClicked += time => InsertEventDefault(eventsProperty, time);
            lane.MomentDeleteRequested += index => DeleteArrayElement(eventsProperty, index);
            content.Add(CreateRow(
                "Events", lane, () => SelectItem(-1, SelectedLaneKind.Event, -1, -1),
                isGroup: true, accentClass: "events"));
        }

        private void BuildHoldRows(VisualElement content, float contentWidth)
        {
            SerializedProperty holdsProperty = serializedObject.FindProperty("holdMarkers");
            List<float> times = new List<float>(cutscene.holdMarkers.Count);
            for (int i = 0; i < cutscene.holdMarkers.Count; i++)
            {
                times.Add(cutscene.holdMarkers[i].time);
            }

            CutsceneMomentLaneElement lane = new CutsceneMomentLaneElement
            {
                pixelsPerSecond = pixelsPerSecond,
                markerColor = new Color(0.95f, 0.85f, 0.3f),
                style = { width = contentWidth, height = LaneRowHeight }
            };
            bool isSelected = selectedLaneKind == SelectedLaneKind.Hold;
            lane.SetTimes(times, isSelected ? selectedItemIndex : -1);
            lane.MomentSelected += index => SelectItem(-1, SelectedLaneKind.Hold, -1, index);
            lane.MomentMoveCommitted += (index, time) => CommitMomentTime(holdsProperty, index, time);
            lane.EmptySpaceDoubleClicked += time => InsertHoldDefault(holdsProperty, time);
            lane.MomentDeleteRequested += index => DeleteArrayElement(holdsProperty, index);
            content.Add(CreateRow(
                "Holds", lane, () => SelectItem(-1, SelectedLaneKind.Hold, -1, -1),
                isGroup: true, accentClass: "holds"));
        }

        private void OnPlayheadScrubbed(float time)
        {
            // No rebuild: the playhead element repaints itself from TimeSeconds, and rebuilding
            // every lane per pointer-move is exactly the churn A58 §6 warned about.
            SetPlayhead(time);
        }

        /// <summary>Poses every bound actor/prop, renders the in-tab viewport, and — only if <see cref="previewShotToggle"/> opts in — also drives the Scene view camera (G4, now opt-in per A59).</summary>
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

        private void SelectItem(int slotIndex, SelectedLaneKind laneKind, int partTrackIndex, int itemIndex)
        {
            selectedSlotIndex = slotIndex;
            selectedLaneKind = laneKind;
            selectedPartTrackIndex = partTrackIndex;
            selectedItemIndex = itemIndex;
            SyncSceneSelectionToTimelineSelection();
            RebuildTimeline();
            RebuildInspector();
        }

        // -----------------------------------------------------------------------------------
        // Mutations shared by every moment lane.
        // -----------------------------------------------------------------------------------

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

        /// <summary>
        /// Whether a change event is Unity's binding echoing the value it just bound, rather than a
        /// human picking something.
        /// </summary>
        /// <remarks>
        /// <c>SerializedDefaultEnumBinding</c> sends a <c>ChangeEvent&lt;string&gt;</c> from
        /// <c>OnFieldAttached</c> carrying the value it just bound, so a callback that rebuilds the
        /// inspector from one rebuilds, re-binds and is called again — the inspector flickers at
        /// frame rate and every field becomes a fresh instance every frame. Measured at 600 rebuilds
        /// over a few idle seconds before this guard existed.
        /// </remarks>
        private bool ShouldIgnoreBindingEcho<TValue>(ChangeEvent<TValue> changeEvent)
        {
            return isRebuildingInspector
                || EqualityComparer<TValue>.Default.Equals(changeEvent.previousValue, changeEvent.newValue);
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

            // Props get one too: a door places exactly the way a character does (A58 §3.3).
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
                // Silence here is what made the missing preview read as a broken tool (A58 §1).
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
                bool isOverride = CutsceneKeySampler.TryResolveFacingAngle(
                    slot.facingKeys, slot.transformKeys, playheadSeconds, out facingAngle);
                Label facingLabel = new Label(
                    "Facing at playhead: " + facingAngle.ToString("0.#") + "°"
                    + (isOverride ? " (override key)" : " (derived from root travel)")
                    + (slot.directionSet == null
                        ? " — assign a Direction Set to apply it in the preview."
                        : " — " + CutscenePreviewController.DescribeResolvedFacing(slot, playheadSeconds)));
                facingLabel.style.marginTop = 4f;
                facingLabel.style.whiteSpace = WhiteSpace.Normal;
                inspectorScroll.Add(facingLabel);
            }

            BuildSceneBindingRow(slotIndex);

            Button removeButton = new Button(() => RemoveSlot(slotIndex)) { text = "Remove Slot" };
            removeButton.style.marginTop = 8f;
            inspectorScroll.Add(removeButton);
        }

        private void BuildSceneBindingRow(int slotIndex)
        {
            string currentSceneGuid = CutsceneSceneBindingUtility.CurrentSceneGuid();
            if (string.IsNullOrEmpty(cutscene.sceneGuid) || currentSceneGuid != cutscene.sceneGuid)
            {
                inspectorScroll.Add(new Label("Open the remembered scene to bind this slot.")
                { style = { whiteSpace = WhiteSpace.Normal, marginTop = 8f } });
                return;
            }

            uint slotId = cutscene.slots[slotIndex].SlotId;
            CutsceneSlotBindingEntry existing =
                CutsceneSceneBindingUtility.FindBinding(cutscene, currentSceneGuid, slotId);
            GameObject boundObject = existing != null
                ? CutsceneSceneBindingUtility.ResolveGameObject(existing.globalObjectId)
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
                CutsceneSceneBindingUtility.SetBinding(
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
            AddBoundField(keyProperty, "interpolation", "Interpolation");
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
            AddBoundField(keyProperty, "interpolation", "Interpolation");

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
            AddBoundField(eventProperty, "intParam", "Int Param");
            AddBoundField(eventProperty, "floatParam", "Float Param");
            AddBoundField(eventProperty, "fireOnSkip", "Fire On Skip");
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

        private void AddBoundField(SerializedProperty parent, string relativePropertyName, string label)
        {
            SerializedProperty property = parent.FindPropertyRelative(relativePropertyName);
            PropertyField field = new PropertyField(property, label);
            field.Bind(serializedObject);
            field.RegisterCallback<SerializedPropertyChangeEvent>(_ => RebuildTimeline());
            inspectorScroll.Add(field);
        }

        private static Label BuildHeading(string text)
        {
            Label heading = new Label(text);
            heading.style.unityFontStyleAndWeight = FontStyle.Bold;
            heading.style.marginTop = 4f;
            heading.style.marginBottom = 4f;
            return heading;
        }
    }
}
