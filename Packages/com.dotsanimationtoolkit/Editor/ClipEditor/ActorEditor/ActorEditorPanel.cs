// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// The Actor Editor tab: a profile header over three columns — layers, the composited viewport and
    /// an inspector — with a transport bar under the viewport that steps, stops and turns the actor
    /// through its directions.
    /// </summary>
    public sealed class ActorEditorPanel : VisualElement, IDisposable, ITransportTarget
    {
        private const string LayersColumnUssClassName = "actor-editor__layers-column";
        private const string ViewportColumnUssClassName = "actor-editor__viewport-column";
        private const string InspectorColumnUssClassName = "actor-editor__inspector-column";

        private const float SideColumnWidth = 340f;

        // 315 degrees is due south-east under FacingResolver.FromMovement's convention (0 = east,
        // positive y = north) — the direction every fresh preview and every Reset settles on.
        private const float SouthEastSliderAngleDegrees = 315f;

        /// <summary>Raised when the header's Profile field picks a different asset.</summary>
        public event Action<ActorProfileAsset> ProfileChanged;

        private ActorProfileAsset profile;
        private readonly ActorPreviewComposer composer;

        // The window's preview, not one owned here: the clips a profile plays will already be in
        // the window's registry, so a facing or layer change is just a different sample into it.
        // Safe only because the tabs are exclusive; nothing here disposes it.
        private ClipPreviewController previewController;
        private RigAsset windowRig;

        private readonly PreviewCameraNavigation cameraNavigation = new PreviewCameraNavigation();
        private PreviewCameraPose restoredCameraPose;
        private bool hasBorrowedCamera;
        private ToolbarToggle billboardToggle;
        private ToolbarToggle ragdollToggle;
        private bool restoreBillboardEnabled;
        private bool isTicking;
        private double lastTickTimeSinceStartup;

        private bool isComposerProfileStale;
        private bool isPlaying;
        private float currentFacingAngleDegrees = SouthEastSliderAngleDegrees;
        private Direction currentMemberFacing = Direction.SouthEast;
        private string ragdollRefusalReason;
        private ActorEditorSelection currentSelection = ActorEditorSelection.None;

        private ObjectField profileField;
        private ValidationBadgeElement validationBadge;
        private TransportCoreElement transportCore;
        private Slider directionSlider;
        private Label directionReadoutLabel;
        private VisualElement layersColumn;
        private VisualElement viewportColumn;
        private VisualElement inspectorColumn;
        private Image viewportImage;
        private Label viewportStatusLabel;
        private ActorEditorLayersColumn layersColumnView;
        private ActorEditorInspectorColumn inspectorColumnView;

        public ActorEditorPanel()
        {
            composer = new ActorPreviewComposer();

            // Inline styles rather than a stylesheet, matching DirectionSetsPanel/VatBakePanel:
            // this element carries no sheet of its own.
            style.flexGrow = 1f;
            style.paddingLeft = 8f;
            style.paddingRight = 8f;
            style.paddingTop = 6f;
            style.paddingBottom = 6f;

            Add(BuildHeaderRow());
            Add(BuildBody());
        }

        // -----------------------------------------------------------------------------------------
        // ITransportTarget
        // -----------------------------------------------------------------------------------------

        public bool IsPlaying { get { return isPlaying; } }

        public bool IsLooping
        {
            get { return false; }
            set { }
        }

        public TransportCapabilities Capabilities
        {
            get { return TransportCapabilities.StepForward | TransportCapabilities.Stop; }
        }

        public void TogglePlay()
        {
            SetPlaying(!isPlaying);
        }

        public void Stop()
        {
            SetPlaying(false);
            JumpToStart();
        }

        public void JumpToStart()
        {
            composer.Reset();
            previewController?.DisableRagdollPreview();
            ragdollRefusalReason = null;
            SetRagdollToggleWithoutNotify(false);
            currentFacingAngleDegrees = SouthEastSliderAngleDegrees;
            currentMemberFacing = Direction.SouthEast;
            composer.Facing = Direction.SouthEast;
            directionSlider?.SetValueWithoutNotify(currentFacingAngleDegrees);
            RefreshDirectionReadoutLabel();
        }

        // An actor has no end, its layers loop.
        public void JumpToEnd()
        {
        }

        public void Step(int frameDelta)
        {
            if (!isPlaying && composer.IsCreated && previewController != null)
            {
                composer.Tick(Mathf.Max(0, frameDelta) * (1f / 30f), previewController);
                RenderViewport();
            }
        }

        private void SetPlaying(bool playing)
        {
            isPlaying = playing;
            transportCore?.RefreshState();
        }

        /// <summary>The profile this panel is authoring. Raises <see cref="ProfileChanged"/> whenever it actually changes, whether set here or picked through the header field.</summary>
        public ActorProfileAsset Profile
        {
            get { return profile; }
            set
            {
                if (profile == value)
                {
                    return;
                }
                profile = value;
                if (profileField != null)
                {
                    profileField.SetValueWithoutNotify(profile);
                }

                // The composer's blob is rebuilt on the next tick rather than here: SetProfile needs
                // the window's preview controller, and a profile can be assigned before SetSource
                // ever runs (a freshly built panel, or a double-click opener racing tab creation).
                isComposerProfileStale = true;
                currentSelection = ActorEditorSelection.None;
                ragdollRefusalReason = null;

                layersColumnView?.Bind(profile, composer);
                inspectorColumnView?.Bind(profile, composer);
                RefreshValidationBadge();

                ProfileChanged?.Invoke(profile);
            }
        }

        // -----------------------------------------------------------------------------------------
        // Host entry points
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Hands the panel the window's preview and rig. Called on every tab switch and whenever
        /// the toolbar Rig field changes while this pane is open.
        /// </summary>
        public void SetSource(ClipPreviewController controller, RigAsset rig)
        {
            previewController = controller;
            windowRig = rig;
            cameraNavigation.Rig = controller;
        }

        // Starts or stops the per-frame tick with the pane's visibility, and borrows the shared
        // preview's camera and billboard state for as long as it has it, restoring both on the way
        // out, the same contract DirectionSetsPanel used for its own tab. The ragdoll trigger
        // subscription follows the same lifetime: a hidden tab must not go on dropping or restoring
        // a ragdoll nobody can see.
        public void SetTicking(bool ticking)
        {
            if (ticking == isTicking)
            {
                return;
            }
            isTicking = ticking;

            if (ticking)
            {
                BorrowPreviewCamera();
                composer.RagdollStartRequested += OnComposerRagdollStartRequested;
                composer.RagdollStopRequested += OnComposerRagdollStopRequested;
                lastTickTimeSinceStartup = EditorApplication.timeSinceStartup;
                EditorApplication.update += Tick;
            }
            else
            {
                EditorApplication.update -= Tick;
                composer.RagdollStartRequested -= OnComposerRagdollStartRequested;
                composer.RagdollStopRequested -= OnComposerRagdollStopRequested;
                previewController?.DisableRagdollPreview();
                ragdollRefusalReason = null;
                SetRagdollToggleWithoutNotify(false);
                ReturnPreviewCamera();
            }
        }

        /// <summary>Disposes the composer's native blob and layer array. Called by the window on teardown, before the preview controller it samples into.</summary>
        public void Dispose()
        {
            composer.RagdollStartRequested -= OnComposerRagdollStartRequested;
            composer.RagdollStopRequested -= OnComposerRagdollStopRequested;
            composer.Dispose();
        }

        private void BorrowPreviewCamera()
        {
            if (previewController == null || hasBorrowedCamera)
            {
                return;
            }

            restoredCameraPose = previewController.CapturePose();
            restoreBillboardEnabled = previewController.BillboardPreviewEnabled;
            hasBorrowedCamera = true;

            previewController.OrbitYaw = 0f;
            previewController.OrbitPitch = 0f;
            previewController.BillboardPreviewEnabled = true;
            previewController.DisableRagdollPreview();
            previewController.FrameRig();

            billboardToggle?.SetValueWithoutNotify(true);
            SetRagdollToggleWithoutNotify(false);
        }

        private void ReturnPreviewCamera()
        {
            if (previewController == null || !hasBorrowedCamera)
            {
                return;
            }

            previewController.RestorePose(in restoredCameraPose);
            previewController.BillboardPreviewEnabled = restoreBillboardEnabled;
            hasBorrowedCamera = false;
        }

        private void SetRagdollToggleWithoutNotify(bool ragdollOn)
        {
            ragdollToggle?.SetValueWithoutNotify(ragdollOn);
        }

        // -----------------------------------------------------------------------------------------
        // Layout
        // -----------------------------------------------------------------------------------------

        private VisualElement BuildHeaderRow()
        {
            VisualElement header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 6f;

            profileField = new ObjectField("Profile") { objectType = typeof(ActorProfileAsset) };
            profileField.name = "actor-editor-profile-field";
            profileField.style.flexGrow = 1f;
            profileField.RegisterValueChangedCallback(
                changeEvent => Profile = changeEvent.newValue as ActorProfileAsset);
            header.Add(profileField);

            validationBadge = new ValidationBadgeElement { name = "actor-editor-validation-badge" };
            validationBadge.style.marginLeft = 6f;
            header.Add(validationBadge);

            return header;
        }

        private VisualElement BuildTransportRow()
        {
            VisualElement transportRow = new VisualElement { name = "actor-editor-transport-row" };
            transportRow.AddToClassList("toolkit-transport");

            VisualElement coreGroup = new VisualElement();
            coreGroup.AddToClassList("toolkit-transport__group");
            transportCore = new TransportCoreElement();
            transportCore.Bind(this);
            coreGroup.Add(transportCore);
            transportRow.Add(coreGroup);

            VisualElement directionGroup = new VisualElement();
            directionGroup.AddToClassList("toolkit-transport__group");

            Label directionCaption = new Label("Direction");
            directionCaption.AddToClassList("toolkit-transport__caption");
            directionGroup.Add(directionCaption);

            directionSlider = new Slider(0f, 360f) { value = currentFacingAngleDegrees };
            directionSlider.style.width = 120f;
            directionSlider.tooltip =
                "Turn the actor. 0 degrees is due east; the readout says which authored clip that "
                + "resolves to and whether it is mirrored.";
            directionSlider.RegisterValueChangedCallback(OnDirectionSliderChanged);
            directionGroup.Add(directionSlider);

            directionReadoutLabel = new Label();
            directionReadoutLabel.AddToClassList("toolkit-transport__derived");
            directionGroup.Add(directionReadoutLabel);

            transportRow.Add(directionGroup);

            RefreshDirectionReadoutLabel();

            return transportRow;
        }

        private VisualElement BuildBody()
        {
            VisualElement body = new VisualElement();
            body.style.flexDirection = FlexDirection.Row;
            body.style.flexGrow = 1f;

            layersColumn = new VisualElement { name = "layers-column" };
            layersColumn.AddToClassList(LayersColumnUssClassName);
            layersColumn.style.width = SideColumnWidth;
            // Holds its width: a layer box header carries seven controls, and a column that gave
            // way to the preview would ellipsize every layer name to one letter.
            layersColumn.style.flexShrink = 0f;
            layersColumn.style.marginRight = 8f;
            body.Add(layersColumn);

            VisualElement layersHeader = new VisualElement();
            layersHeader.AddToClassList("toolkit-pane-header");
            Label layersTitle = new Label("Layers");
            layersTitle.AddToClassList("toolkit-pane-title");
            layersHeader.Add(layersTitle);

            VisualElement layersActions = new VisualElement();
            layersActions.AddToClassList("toolkit-pane-actions");
            Button addLayerButton = ToolkitIcons.MakeIconButton(
                () => layersColumnView?.AddLayer(), ToolkitIcons.Plus, "Add a layer above Override.", "+ Layer");
            ToolkitIcons.SetButtonIconAndText(addLayerButton, ToolkitIcons.Plus, "Layer");
            addLayerButton.AddToClassList("toolkit-pane-action");
            addLayerButton.name = "actor-editor-add-layer-button";
            layersActions.Add(addLayerButton);
            layersHeader.Add(layersActions);

            layersColumn.Add(layersHeader);

            layersColumnView = new ActorEditorLayersColumn();
            layersColumnView.style.flexGrow = 1f;
            layersColumnView.SelectionChanged += OnTreeSelectionChanged;
            layersColumnView.ProfileEdited += OnAnyColumnProfileEdited;
            layersColumn.Add(layersColumnView);

            viewportColumn = new VisualElement { name = "viewport-column" };
            viewportColumn.AddToClassList(ViewportColumnUssClassName);
            viewportColumn.style.flexGrow = 1f;
            body.Add(viewportColumn);

            VisualElement viewportHeader = new VisualElement();
            viewportHeader.AddToClassList("toolkit-pane-header");
            Label viewportTitle = new Label("Preview");
            viewportTitle.AddToClassList("toolkit-pane-title");
            viewportHeader.Add(viewportTitle);
            viewportColumn.Add(viewportHeader);

            viewportStatusLabel = new Label();
            viewportStatusLabel.style.whiteSpace = WhiteSpace.Normal;
            viewportColumn.Add(viewportStatusLabel);

            VisualElement viewportFrame = new VisualElement { name = "viewport-frame" };
            viewportFrame.AddToClassList("clip-editor__viewport-frame");
            viewportFrame.style.flexGrow = 1f;
            viewportColumn.Add(viewportFrame);

            viewportImage = new Image();
            viewportImage.style.flexGrow = 1f;
            viewportImage.AddToClassList("actor-editor__viewport-image");
            viewportFrame.Add(viewportImage);

            VisualElement viewportOverlay = new VisualElement { name = "viewport-overlay" };
            viewportOverlay.AddToClassList("clip-editor__viewport-overlay");
            viewportOverlay.pickingMode = PickingMode.Ignore;
            viewportFrame.Add(viewportOverlay);

            VisualElement overlayColumn = new VisualElement { name = "overlay-column" };
            overlayColumn.AddToClassList("clip-editor__overlay-column");
            viewportOverlay.Add(overlayColumn);

            ToolbarButton resetCameraButton = new ToolbarButton(() => cameraNavigation.ResetView())
            {
                name = "actor-reset-camera-button"
            };
            resetCameraButton.AddToClassList("clip-editor__overlay-tool-button");
            resetCameraButton.tooltip =
                "Put the camera back where the window opened it: head-on, centred on this rig "
                + "and backed off to fit it. Undoes any orbit, pan or flight. Same as "
                + "double-clicking the viewport.\n\n"
                + "Viewport camera: drag to orbit, middle-drag to pan, right-drag to look "
                + "around, right-drag + W/A/S/D and Q/E to fly (Shift for faster), "
                + "Alt + right-drag or the wheel to zoom, F to frame the selection.";
            Image resetCameraIcon = new Image { pickingMode = PickingMode.Ignore };
            resetCameraIcon.AddToClassList("clip-editor__overlay-tool-icon");
            resetCameraButton.Insert(0, resetCameraIcon);
            ToolkitIcons.SetButtonIcon(resetCameraButton, resetCameraIcon, "d_FrameCapture", "Reset Camera");
            overlayColumn.Add(resetCameraButton);

            billboardToggle = new ToolbarToggle { name = "actor-billboard-preview-toggle" };
            billboardToggle.AddToClassList("clip-editor__overlay-tool-button");
            billboardToggle.AddToClassList("clip-editor__overlay-run-break");
            billboardToggle.tooltip = "Preview this actor's screen-aligned billboard parts, if it has any.";
            Image billboardIcon = new Image { pickingMode = PickingMode.Ignore };
            billboardIcon.AddToClassList("clip-editor__overlay-tool-icon");
            billboardToggle.Add(billboardIcon);
            ToolkitIcons.SetToggleIcon(billboardToggle, billboardIcon, "d_BillboardRenderer Icon", "Billboard");
            billboardToggle.RegisterValueChangedCallback(changeEvent =>
            {
                if (previewController != null)
                {
                    previewController.BillboardPreviewEnabled = changeEvent.newValue;
                }
            });
            overlayColumn.Add(billboardToggle);

            ragdollToggle = new ToolbarToggle { name = "actor-ragdoll-preview-toggle" };
            ragdollToggle.AddToClassList("clip-editor__overlay-tool-button");
            ragdollToggle.tooltip =
                "Drop the previewed rig as an active ragdoll to see whether a pose still reads on impact. "
                + "Turning it off restores the pose exactly.";
            Image ragdollIcon = new Image { pickingMode = PickingMode.Ignore };
            ragdollIcon.AddToClassList("clip-editor__overlay-tool-icon");
            ragdollToggle.Add(ragdollIcon);
            ToolkitIcons.SetToggleIcon(ragdollToggle, ragdollIcon, "d_Avatar Icon", "Ragdoll");
            ragdollToggle.RegisterValueChangedCallback(OnRagdollToggleChanged);
            overlayColumn.Add(ragdollToggle);

            cameraNavigation.AttachTo(viewportImage);

            viewportColumn.Add(BuildTransportRow());

            // The expanded findings list floats over the viewport, the same corner the Clip Editor
            // tab uses for its own badge.
            validationBadge.AttachMessagePanel(viewportColumn);

            inspectorColumn = new VisualElement { name = "inspector-column" };
            inspectorColumn.AddToClassList(InspectorColumnUssClassName);
            inspectorColumn.style.width = SideColumnWidth;
            inspectorColumn.style.minWidth = 260f;
            inspectorColumn.style.marginLeft = 8f;
            body.Add(inspectorColumn);

            VisualElement inspectorHeader = new VisualElement();
            inspectorHeader.AddToClassList("toolkit-pane-header");
            Label inspectorTitle = new Label("Actor Inspector");
            inspectorTitle.AddToClassList("toolkit-pane-title");
            inspectorHeader.Add(inspectorTitle);
            inspectorColumn.Add(inspectorHeader);

            inspectorColumnView = new ActorEditorInspectorColumn();
            inspectorColumnView.style.flexGrow = 1f;
            inspectorColumnView.ProfileEdited += OnAnyColumnProfileEdited;
            inspectorColumn.Add(inspectorColumnView);

            // Shows the "no profile" state immediately rather than an empty column until the first
            // profile is picked.
            layersColumnView.Bind(profile, composer);
            inspectorColumnView.Bind(profile, composer);

            return body;
        }

        // -----------------------------------------------------------------------------------------
        // Header actions
        // -----------------------------------------------------------------------------------------

        private void OnTreeSelectionChanged(ActorEditorSelection selection)
        {
            currentSelection = selection;
            inspectorColumnView?.SetSelection(currentSelection);
        }

        // Either column's own edit can change what the composer needs to sample (a new layer, a
        // re-pointed clip, a changed direction fill) so both funnel into the one staleness flag
        // rather than each guessing whether its own edit mattered to the blob.
        private void OnAnyColumnProfileEdited()
        {
            isComposerProfileStale = true;
            RefreshValidationBadge();
        }

        private void OnDirectionSliderChanged(ChangeEvent<float> changeEvent)
        {
            currentFacingAngleDegrees = changeEvent.newValue;
            ApplyFacingFromSliderAngle();
        }

        private void ApplyFacingFromSliderAngle()
        {
            float angleRadians = Mathf.Deg2Rad * currentFacingAngleDegrees;
            float2 facingVector = new float2(Mathf.Cos(angleRadians), Mathf.Sin(angleRadians));
            AnimationDirections quantizeDirections = profile != null ? profile.turnDirections : AnimationDirections.Six;

            currentMemberFacing = FacingResolver.FromMovement(in facingVector, quantizeDirections, currentMemberFacing);
            composer.Facing = currentMemberFacing;
            RefreshDirectionReadoutLabel();
        }

        private void RefreshDirectionReadoutLabel()
        {
            if (directionReadoutLabel == null)
            {
                return;
            }

            FacingResolver.ToAuthoredSide(currentMemberFacing, out Direction clipFacing, out bool mirrorX);
            string readout = Mathf.RoundToInt(currentFacingAngleDegrees) + "° → " + clipFacing;
            if (mirrorX)
            {
                readout += ", mirrored";
            }
            directionReadoutLabel.text = readout;
        }

        private void RefreshValidationBadge()
        {
            if (validationBadge == null)
            {
                return;
            }

            if (profile == null)
            {
                validationBadge.RefreshFromMessages(new List<ValidationMessage>(), "No profile");
                return;
            }

            List<ValidationMessage> messages =
                ActorProfileValidation.Validate(profile, VocabularyRegistryProvider.AnimationNames);
            messages.AddRange(ClipValidation.ValidateBind(profile.rig, profile.clipSets));
            validationBadge.RefreshFromMessages(messages);
        }

        // -----------------------------------------------------------------------------------------
        // Ragdoll mix (A71-T7) — the composer decides when an animation's ragdoll trigger fires;
        // this panel is the only thing allowed to act on the window's actual ragdoll preview.
        // -----------------------------------------------------------------------------------------

        private void OnComposerRagdollStartRequested(uint animationKey)
        {
            if (previewController == null)
            {
                return;
            }

            if (previewController.TryEnableRagdollPreview(out string refusalReason))
            {
                ragdollRefusalReason = null;
                SetRagdollToggleWithoutNotify(true);
            }
            else
            {
                ragdollRefusalReason = refusalReason;
            }
        }

        private void OnComposerRagdollStopRequested(uint animationKey)
        {
            ragdollRefusalReason = null;
            previewController?.DisableRagdollPreview();
            SetRagdollToggleWithoutNotify(false);
        }

        // A74-D5: a manual click does what the Clip Editor's own ragdoll toggle does. A refused enable
        // snaps the toggle back off — ragdollRefusalReason already surfaces the reason in the status label.
        private void OnRagdollToggleChanged(ChangeEvent<bool> changeEvent)
        {
            if (previewController == null)
            {
                return;
            }

            if (changeEvent.newValue)
            {
                if (previewController.TryEnableRagdollPreview(out string refusalReason))
                {
                    ragdollRefusalReason = null;
                }
                else
                {
                    ragdollRefusalReason = refusalReason;
                    SetRagdollToggleWithoutNotify(false);
                }
            }
            else
            {
                ragdollRefusalReason = null;
                previewController.DisableRagdollPreview();
            }
        }

        // -----------------------------------------------------------------------------------------
        // Tick
        // -----------------------------------------------------------------------------------------

        private void Tick()
        {
            if (previewController == null)
            {
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            float elapsedSeconds = lastTickTimeSinceStartup > 0d ? (float)(now - lastTickTimeSinceStartup) : 0f;
            lastTickTimeSinceStartup = now;

            if (isComposerProfileStale)
            {
                composer.SetProfile(profile, previewController);
                isComposerProfileStale = false;
                currentFacingAngleDegrees = SouthEastSliderAngleDegrees;
                currentMemberFacing = Direction.SouthEast;
                directionSlider?.SetValueWithoutNotify(currentFacingAngleDegrees);
                RefreshDirectionReadoutLabel();
            }

            if (composer.IsCreated)
            {
                // Advancing at zero elapsed still re-samples the composited pose, so a scrub made
                // while paused (the layer row's time field) is visible immediately.
                composer.Tick(isPlaying ? elapsedSeconds : 0f, previewController);
            }

            RefreshValidationBadge();
            layersColumnView?.RefreshIfChanged();
            inspectorColumnView?.RefreshIfChanged();

            RenderViewport();
        }

        private void RenderViewport()
        {
            string status = previewController.StatusMessage;
            if (windowRig == null)
            {
                status = "No rig in the top bar — pick a profile to set it, or assign one directly.";
            }

            if (!string.IsNullOrEmpty(ragdollRefusalReason))
            {
                status = AppendStatus(status, "Ragdoll: " + ragdollRefusalReason);
            }
            else if (composer.RagdollOn)
            {
                status = AppendStatus(status, "Ragdoll: on (" + ResolveAnimationDisplayName(composer.RagdollStartedByKey) + ")");
            }

            if (viewportStatusLabel != null)
            {
                viewportStatusLabel.text = status;
            }

            if (viewportImage == null)
            {
                return;
            }
            Rect viewportRect = viewportImage.contentRect;
            if (float.IsNaN(viewportRect.width) || viewportRect.width < 1f || viewportRect.height < 1f)
            {
                // Layout has not run yet; rendering into a zero rect throws inside the utility.
                return;
            }

            Texture renderedTexture = previewController.Render(
                Mathf.RoundToInt(viewportRect.width), Mathf.RoundToInt(viewportRect.height));
            if (renderedTexture != null)
            {
                viewportImage.image = renderedTexture;
                viewportImage.MarkDirtyRepaint();
            }
        }

        private static string AppendStatus(string existingStatus, string addition)
        {
            return string.IsNullOrEmpty(existingStatus) ? addition : existingStatus + " · " + addition;
        }

        private static string ResolveAnimationDisplayName(uint animationKey)
        {
            if (animationKey == 0u)
            {
                return "unknown";
            }
            string resolvedName = VocabularyRegistryProvider.AnimationNames.FindName(animationKey);
            return resolvedName ?? "0x" + animationKey.ToString("X8");
        }
    }
}
