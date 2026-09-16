// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
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

        /// <summary>Raised when the header's Profile field picks a different asset.</summary>
        public event Action<ActorProfileAsset> ProfileChanged;

        private ActorProfileAsset profile;
        private readonly ActorPreviewComposer composer;

        // The window's preview, not one owned here: the clips a profile plays will already be in
        // the window's registry, so a facing or layer change is just a different sample into it.
        // Safe only because the tabs are exclusive; nothing here disposes it.
        private ClipPreviewController previewController;

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
        private Direction currentMemberFacing = Direction.SouthEast;
        private string ragdollRefusalReason;
        private ActorEditorSelection currentSelection = ActorEditorSelection.None;

        private ActiveAssetSelection selection;
        private ActorEditorProfilesColumn profilesColumn;
        private ValidationBadgeElement validationBadge;
        private TransportCoreElement transportCore;
        private EnumField directionField;
        private Label directionReadoutLabel;
        private VisualElement layersColumn;
        private VisualElement viewportColumn;
        private VisualElement inspectorColumn;
        private Image viewportImage;
        private Label viewportStatusLabel;
        private ActorEditorLayersColumn layersColumnView;
        private ActorEditorInspectorColumn inspectorColumnView;
        private LayerEventStripElement layerEventStrip;

        public ActorEditorPanel()
        {
            composer = new ActorPreviewComposer();

            // This element only hosts the profiles/layers/viewport/inspector columns, each of
            // which owns its own side inset — adding a second one here would gutter the
            // profiles catalog away from the pane edge.
            style.flexGrow = 1f;
            style.paddingLeft = 0f;
            style.paddingRight = 0f;
            style.paddingTop = 6f;
            style.paddingBottom = 6f;

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
            currentMemberFacing = Direction.SouthEast;
            composer.Facing = Direction.SouthEast;
            directionField?.SetValueWithoutNotify(Direction.SouthEast);
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
                // A step is one frame of playback, so a stepped loop wrap still fires its markers.
                layerEventStrip?.Tick(true);
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
                profilesColumn?.SetSelectedProfile(profile);

                // The composer's blob is rebuilt on the next tick rather than here: SetProfile needs
                // the window's preview controller, and a profile can be assigned before SetSource
                // ever runs (a freshly built panel, or a double-click opener racing tab creation).
                isComposerProfileStale = true;
                currentSelection = ActorEditorSelection.None;
                ragdollRefusalReason = null;

                layersColumnView?.Bind(profile, composer);
                inspectorColumnView?.Bind(profile, composer);
                layerEventStrip?.Bind(profile, composer);

                // Picking a profile sets the shared rig, never the shared clip set.
                if (profile != null && profile.rig != null)
                {
                    selection?.SetRig(profile.rig);
                }

                RefreshValidationBadge();

                ProfileChanged?.Invoke(profile);
            }
        }

        // -----------------------------------------------------------------------------------------
        // Host entry points
        // -----------------------------------------------------------------------------------------

        // The rig is not handed over here: it is read from the shared selection this panel is bound to.
        public void SetSource(ClipPreviewController controller)
        {
            previewController = controller;
            cameraNavigation.Rig = controller;
        }

        public void Bind(ActiveAssetSelection sharedSelection)
        {
            selection = sharedSelection;
            profilesColumn?.Bind(sharedSelection);
        }

        public void RescanProject() => profilesColumn?.RescanProject();

        public void LoadCatalog(IReadOnlyList<ActorProfileAsset> profiles) => profilesColumn?.LoadCatalog(profiles);

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
            layerEventStrip?.Dispose();
            composer.Dispose();
            profilesColumn?.Dispose();
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

            directionField = new EnumField(currentMemberFacing);
            directionField.style.width = 120f;
            directionField.tooltip =
                "Turn the actor. The readout says which authored clip that direction resolves to "
                + "and whether it is mirrored.";
            directionField.RegisterValueChangedCallback(OnDirectionFieldChanged);
            directionGroup.Add(directionField);

            directionReadoutLabel = new Label();
            directionReadoutLabel.AddToClassList("toolkit-transport__derived");
            directionGroup.Add(directionReadoutLabel);

            transportRow.Add(directionGroup);

            RefreshDirectionReadoutLabel();

            return transportRow;
        }

        private VisualElement BuildBody()
        {
            layersColumn = new VisualElement { name = "layers-column" };
            layersColumn.AddToClassList(LayersColumnUssClassName);
            layersColumn.AddToClassList("toolkit-column");
            layersColumn.AddToClassList("toolkit-column--raised");
            // Floored rather than fixed: a layer box header carries seven controls, and dragging
            // this pane narrower than that would ellipsize every layer name to one letter.
            layersColumn.style.minWidth = 220f;

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
            layersColumnView.AddToClassList("toolkit-list-surface");
            layersColumnView.SelectionChanged += OnTreeSelectionChanged;
            layersColumnView.ProfileEdited += OnAnyColumnProfileEdited;
            layersColumn.Add(layersColumnView);

            viewportColumn = new VisualElement { name = "viewport-column" };
            viewportColumn.AddToClassList(ViewportColumnUssClassName);
            viewportColumn.AddToClassList("toolkit-column");
            viewportColumn.AddToClassList("toolkit-column--flush");
            viewportColumn.style.flexGrow = 1f;
            viewportColumn.style.minWidth = 200f;

            VisualElement viewportHeader = new VisualElement();
            viewportHeader.AddToClassList("toolkit-pane-header");
            Label viewportTitle = new Label("Preview");
            viewportTitle.AddToClassList("toolkit-pane-title");
            viewportHeader.Add(viewportTitle);

            VisualElement viewportActions = new VisualElement();
            viewportActions.AddToClassList("toolkit-pane-actions");
            validationBadge = new ValidationBadgeElement { name = "actor-editor-validation-badge" };
            viewportActions.Add(validationBadge);
            viewportHeader.Add(viewportActions);

            viewportColumn.Add(viewportHeader);

            viewportStatusLabel = ToolkitChrome.MakeHint(string.Empty);
            viewportColumn.Add(viewportStatusLabel);

            ViewportFrameElement viewportFrame = new ViewportFrameElement { name = "viewport-frame" };
            viewportFrame.Overlay.name = "viewport-overlay";
            viewportFrame.OverlayColumn.name = "overlay-column";
            viewportColumn.Add(viewportFrame);

            viewportImage = viewportFrame.ViewportImage;
            viewportImage.AddToClassList("actor-editor__viewport-image");

            ToolbarButton resetCameraButton = viewportFrame.AddResetCameraButton(() => cameraNavigation.ResetView());
            resetCameraButton.name = "actor-reset-camera-button";

            billboardToggle = viewportFrame.AddRailToggle(
                "d_BillboardRenderer Icon",
                "Preview this actor's screen-aligned billboard parts, if it has any.",
                "Billboard",
                true);
            billboardToggle.name = "actor-billboard-preview-toggle";
            billboardToggle.RegisterValueChangedCallback(changeEvent =>
            {
                if (previewController != null)
                {
                    previewController.BillboardPreviewEnabled = changeEvent.newValue;
                }
            });

            ragdollToggle = viewportFrame.AddRailToggle(
                "d_Avatar Icon",
                "Drop the previewed rig as an active ragdoll to see whether a pose still reads on impact. "
                + "Turning it off restores the pose exactly.",
                "Ragdoll");
            ragdollToggle.name = "actor-ragdoll-preview-toggle";
            ragdollToggle.RegisterValueChangedCallback(OnRagdollToggleChanged);

            cameraNavigation.AttachTo(viewportImage);

            viewportColumn.Add(BuildTransportRow());

            layerEventStrip = new LayerEventStripElement();
            viewportColumn.Add(layerEventStrip);

            // The expanded findings list floats over the viewport, the same corner the Clip Editor
            // tab uses for its own badge.
            validationBadge.AttachMessagePanel(viewportColumn);

            inspectorColumn = new VisualElement { name = "inspector-column" };
            inspectorColumn.AddToClassList(InspectorColumnUssClassName);
            inspectorColumn.AddToClassList("toolkit-column");
            inspectorColumn.style.minWidth = 260f;

            VisualElement inspectorHeader = new VisualElement();
            inspectorHeader.AddToClassList("toolkit-pane-header");
            Label inspectorTitle = new Label("Actor Inspector");
            inspectorTitle.AddToClassList("toolkit-pane-title");
            inspectorHeader.Add(inspectorTitle);
            inspectorColumn.Add(inspectorHeader);

            inspectorColumnView = new ActorEditorInspectorColumn();
            inspectorColumnView.style.flexGrow = 1f;
            inspectorColumnView.AddToClassList("toolkit-list-surface");
            inspectorColumnView.ProfileEdited += OnAnyColumnProfileEdited;
            inspectorColumn.Add(inspectorColumnView);

            // Shows the "no profile" state immediately rather than an empty column until the first
            // profile is picked.
            layersColumnView.Bind(profile, composer);
            inspectorColumnView.Bind(profile, composer);
            layerEventStrip.Bind(profile, composer);

            profilesColumn = new ActorEditorProfilesColumn();
            profilesColumn.ProfileSelected += picked => Profile = picked;

            // Three nested CoverPaneSplitViews (profiles | layers | viewport | inspector) rather than
            // four flex columns, matching CutsceneEditorPanel's cast | viewport | inspector split.
            // Every split needs its own minWidth: this whole pane is a cover pane hidden via USS
            // class when the tab switches away (see ClipEditorWindow.ShowActorEditorTab), and a
            // hidden TwoPaneSplitView lays out at zero by zero, collapsing to nothing but the
            // flexible pane on the way back (see AnimationToolkit.md).
            CoverPaneSplitView rightSplit = new CoverPaneSplitView(
                "ActorEditor.Preview", 1, SideColumnWidth, TwoPaneSplitViewOrientation.Horizontal);
            rightSplit.style.flexGrow = 1f;
            rightSplit.style.minWidth = 460f;
            rightSplit.Add(viewportColumn);
            rightSplit.Add(inspectorColumn);

            CoverPaneSplitView middleSplit = new CoverPaneSplitView(
                "ActorEditor.Layers", 0, SideColumnWidth, TwoPaneSplitViewOrientation.Horizontal);
            middleSplit.style.flexGrow = 1f;
            middleSplit.style.minWidth = 680f;
            middleSplit.Add(layersColumn);
            middleSplit.Add(rightSplit);

            CoverPaneSplitView body = new CoverPaneSplitView(
                "ActorEditor.Profiles", 0, 260f, TwoPaneSplitViewOrientation.Horizontal);
            body.style.flexGrow = 1f;
            body.style.minWidth = 880f;
            body.Add(profilesColumn);
            body.Add(middleSplit);

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

        private void OnDirectionFieldChanged(ChangeEvent<Enum> changeEvent)
        {
            Direction desiredFacing = (Direction)changeEvent.newValue;
            AnimationDirections quantizeDirections = profile != null ? profile.turnDirections : AnimationDirections.Six;

            currentMemberFacing = FacingResolver.Snap(desiredFacing, quantizeDirections);
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
            string readout = currentMemberFacing + " → " + clipFacing;
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

            // Several clip sets can each carry VAT textures (that overlap is V39's concern); the
            // bind's stale-bake check only needs the first one that actually supplies them.
            ClipSetAsset vatOwnerSet = null;
            if (profile.clipSets != null)
            {
                for (int clipSetIndex = 0; clipSetIndex < profile.clipSets.Count; clipSetIndex++)
                {
                    ClipSetAsset candidateClipSet = profile.clipSets[clipSetIndex];
                    if (candidateClipSet != null && candidateClipSet.vatTextures != null)
                    {
                        vatOwnerSet = candidateClipSet;
                        break;
                    }
                }
            }

            List<ValidationMessage> bindMessages;
            if (profile.rig != null && vatOwnerSet != null)
            {
                ulong recomputedVatSourceHash = VatSourceHashResolver.ComputeSourceHash(
                    vatOwnerSet,
                    profile.rig,
                    vatOwnerSet.vatTextures.flavor);
                bindMessages = ClipValidation.ValidateBind(
                    profile.rig,
                    profile.clipSets,
                    vatSourceHashRecomputed: true,
                    recomputedVatSourceHash: recomputedVatSourceHash);
            }
            else
            {
                bindMessages = ClipValidation.ValidateBind(profile.rig, profile.clipSets);
            }

            ValidationBadgeElement.DescribeStaleVatBake(bindMessages, vatOwnerSet, profile.rig);
            messages.AddRange(bindMessages);
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
                currentMemberFacing = Direction.SouthEast;
                directionField?.SetValueWithoutNotify(Direction.SouthEast);
                RefreshDirectionReadoutLabel();
            }

            if (composer.IsCreated)
            {
                // Advancing at zero elapsed still re-samples the composited pose, so a scrub made
                // while paused (the layer row's time field) is visible immediately.
                composer.Tick(isPlaying ? elapsedSeconds : 0f, previewController);
                layerEventStrip?.Tick(isPlaying);
            }

            RefreshValidationBadge();
            layersColumnView?.RefreshIfChanged();
            inspectorColumnView?.RefreshIfChanged();

            RenderViewport();
        }

        private void RenderViewport()
        {
            string status = previewController.StatusMessage;
            RigAsset activeRig = selection != null ? selection.Rig : null;
            if (activeRig == null)
            {
                status = "No rig picked — choose a profile, or pick a rig in the column on the left.";
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
