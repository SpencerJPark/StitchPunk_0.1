// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>The ragdoll viewport: the previewed rig with its body boxes, the box-handle drags,
    /// and a Drop / Reset transport over the chosen scenery.</summary>
    public sealed class RagdollViewportElement : VisualElement, IDisposable, ITransportTarget
    {
        private const string DropTooltip =
            "The drop measures each joint's limit against the pose on screen when it starts, so a mid-animation pose is measured against that pose, not the rest pose.";

        private const string GroundOnlyLabel = "Ground only";

        private const string NoClipSetStatusMessage =
            "No clip set assigned: bodies are shown at rest. Pick a clip set on Clip Sets to pose them.";

        public event Action<uint> BodyPicked;
        public event Action BodyBoxEdited;

        private readonly ClipPreviewController previewController;
        private readonly PreviewCameraNavigation cameraNavigation;
        private readonly RagdollBoxDragSession dragSession;
        private readonly TransportCoreElement transport;
        private readonly Image viewportImage;
        private readonly Label statusLabel;
        private readonly Toggle poseFromClipToggle;
        private readonly Label poseClipLabel;
        private readonly Slider poseTimeSlider;
        private readonly VisualElement groundField;

        private RigAsset currentRig;
        private uint selectedBodyId;
        private ClipSetAsset boundClipSet;
        private ClipAsset boundClip;
        private float restPoseNormalizedTime;
        private bool isDisposed;

        public RagdollViewportElement()
        {
            name = "ragdoll-viewport";
            style.flexGrow = 1f;
            style.flexDirection = FlexDirection.Column;
            // Not inside a .toolkit-column (that would inset the viewport sideways), so match its
            // 8px top pad by hand to keep this header level with Bodies and Inspector.
            style.paddingTop = 8f;

            previewController = new ClipPreviewController();
            cameraNavigation = new PreviewCameraNavigation();
            dragSession = new RagdollBoxDragSession();

            // One 32px header row carries the whole viewport toolbar: title, transport, the scenery
            // choice and the pose-from-clip group, matching the reference layout and the height of
            // the Bodies/Inspector pane headers either side of it.
            VisualElement headerRow = ToolkitChrome.MakePaneHeader("Viewport", out Label headerTitleLabel, out VisualElement headerActions);
            headerRow.style.height = 32f;
            headerRow.style.flexShrink = 0f;
            headerTitleLabel.style.flexShrink = 0f;

            VisualElement transportGroup = new VisualElement();
            transportGroup.AddToClassList("toolkit-transport__group");
            transportGroup.style.flexShrink = 0f;
            transport = new TransportCoreElement();
            transport.name = "ragdoll-transport";
            transport.Bind(this);
            transportGroup.Add(transport);
            headerActions.Add(transportGroup);

            groundField = ToolkitChrome.MakeSegmentedControl("ragdoll-ground-segmented", BuildGroundChoices(), 0, OnGroundChoiceSelected);
            groundField.style.flexShrink = 0f;
            headerActions.Add(groundField);

            VisualElement headerSpacer = new VisualElement();
            headerSpacer.style.flexGrow = 1f;
            headerActions.Add(headerSpacer);

            Label poseFromCaption = new Label("Pose from");
            poseFromCaption.AddToClassList("toolkit-hint");
            poseFromCaption.style.flexShrink = 0f;
            headerActions.Add(poseFromCaption);

            poseFromClipToggle = new Toggle();
            poseFromClipToggle.name = "ragdoll-pose-from-clip-toggle";
            poseFromClipToggle.tooltip = DropTooltip;
            poseFromClipToggle.style.flexShrink = 0f;
            poseFromClipToggle.style.marginLeft = 4f;
            headerActions.Add(poseFromClipToggle);

            poseClipLabel = new Label("No clip bound");
            poseClipLabel.name = "ragdoll-pose-clip-label";
            poseClipLabel.tooltip = DropTooltip;
            poseClipLabel.AddToClassList("toolkit-hint");
            poseClipLabel.style.marginLeft = 6f;
            poseClipLabel.style.flexShrink = 0f;
            headerActions.Add(poseClipLabel);

            Add(headerRow);

            ViewportFrameElement viewportFrame = new ViewportFrameElement();
            viewportFrame.AddResetCameraButton(() => cameraNavigation.ResetView());

            viewportImage = viewportFrame.ViewportImage;
            viewportImage.name = "ragdoll-viewport-image";
            viewportImage.RegisterCallback<PointerDownEvent>(OnViewportPointerDown);
            viewportImage.RegisterCallback<PointerMoveEvent>(OnViewportPointerMove);
            viewportImage.RegisterCallback<PointerUpEvent>(OnViewportPointerUp);
            Add(viewportFrame);

            // The scrub value is a live pose-time control, not header chrome, so it keeps its own
            // full-width row directly under the viewport it drives.
            VisualElement poseTimeRow = new VisualElement();
            poseTimeRow.name = "ragdoll-pose-time-row";
            poseTimeRow.style.flexDirection = FlexDirection.Row;
            poseTimeRow.style.alignItems = Align.Center;
            poseTimeRow.style.paddingLeft = 6f;
            poseTimeRow.style.paddingRight = 8f;
            poseTimeRow.style.paddingBottom = 4f;
            poseTimeSlider = new Slider(0f, 1f);
            poseTimeSlider.style.flexGrow = 1f;
            poseTimeSlider.name = "ragdoll-pose-time-slider";
            poseTimeSlider.tooltip = DropTooltip;
            poseTimeSlider.RegisterValueChangedCallback(OnPoseTimeSliderChanged);
            poseTimeRow.Add(poseTimeSlider);
            Add(poseTimeRow);

            Label footerStatusLabel;
            VisualElement footerRow = ToolkitChrome.MakeStatusRow(out footerStatusLabel, out _, true);
            footerStatusLabel.name = "ragdoll-viewport-status";
            statusLabel = footerStatusLabel;
            Add(footerRow);

            cameraNavigation.Rig = previewController;
            cameraNavigation.AttachTo(viewportImage);

            RegisterCallback<AttachToPanelEvent>(attachEvent => EditorApplication.update += Tick);
            RegisterCallback<DetachFromPanelEvent>(detachEvent => EditorApplication.update -= Tick);
        }

        public uint SelectedBodyId
        {
            get { return selectedBodyId; }
        }

        public bool IsDropping
        {
            get { return previewController.RagdollPreviewEnabled; }
        }

        public string StatusMessage
        {
            get { return statusLabel.text; }
        }

        public void Show(RigAsset rig)
        {
            if (rig != currentRig)
            {
                currentRig = rig;
                previewController.DisableRagdollPreview();
                if (rig != null)
                {
                    previewController.SetRig(rig);
                    previewController.FrameRig();
                }
                else
                {
                    previewController.SetRig(null);
                }
            }

            RefreshStatus();
            transport.RefreshState();
        }

        public void SetSelectedBodyId(uint bodyId)
        {
            selectedBodyId = bodyId;
            previewController.SetSelectedRagdollBodyId(bodyId);
        }

        public void SetRestPoseSource(ClipSetAsset clipSet, ClipAsset clip, float normalizedTime)
        {
            if (clipSet != boundClipSet)
            {
                boundClipSet = clipSet;
                previewController.SetClipSet(clipSet);
            }

            boundClip = clip;
            restPoseNormalizedTime = normalizedTime;
            poseClipLabel.text = boundClip != null ? boundClip.name : "No clip bound";
            poseTimeSlider.SetValueWithoutNotify(restPoseNormalizedTime);
        }

        public void Drop()
        {
            if (poseFromClipToggle.value && boundClip != null)
            {
                previewController.SamplePose(boundClip.stableId, restPoseNormalizedTime);
            }

            string refusalReason;
            if (!previewController.TryEnableRagdollPreview(out refusalReason))
            {
                ToolkitChrome.SetStatus(statusLabel, refusalReason, ToolkitStatusTone.Warning);
                transport.RefreshState();
                return;
            }

            transport.RefreshState();
        }

        public void ResetDrop()
        {
            previewController.DisableRagdollPreview();
            transport.RefreshState();
        }

        public void Dispose()
        {
            if (isDisposed)
            {
                return;
            }

            isDisposed = true;
            EditorApplication.update -= Tick;
            previewController.DisableRagdollPreview();
            previewController.Dispose();
        }

        // ITransportTarget: the ragdoll transport is a two-state Drop / Reset, not a scrub timeline.
        bool ITransportTarget.IsPlaying
        {
            get { return IsDropping; }
        }

        bool ITransportTarget.IsLooping
        {
            get { return false; }
            set { }
        }

        TransportCapabilities ITransportTarget.Capabilities
        {
            get { return TransportCapabilities.Stop; }
        }

        void ITransportTarget.TogglePlay()
        {
            if (IsDropping)
            {
                ResetDrop();
            }
            else
            {
                Drop();
            }
        }

        void ITransportTarget.Stop()
        {
            ResetDrop();
        }

        void ITransportTarget.JumpToStart()
        {
        }

        void ITransportTarget.JumpToEnd()
        {
        }

        void ITransportTarget.Step(int frameDelta)
        {
        }

        private List<string> BuildGroundChoices()
        {
            List<string> choices = new List<string>();
            choices.Add(GroundOnlyLabel);
            List<RagdollPreviewPropDefinition> props = RagdollPreviewScenery.instance.Props;
            for (int index = 0; index < props.Count; index++)
            {
                choices.Add(props[index].displayName);
            }
            return choices;
        }

        private void OnGroundChoiceSelected(int selectedIndex)
        {
            // Index 0 is GroundOnlyLabel (no prop), so a chosen prop sits at selectedIndex - 1.
            List<RagdollPreviewPropDefinition> props = RagdollPreviewScenery.instance.Props;
            for (int index = 0; index < props.Count; index++)
            {
                props[index].enabled = index == selectedIndex - 1;
            }
            RagdollPreviewScenery.instance.PersistChange();
        }

        private void RefreshStatus()
        {
            if (boundClipSet == null)
            {
                ToolkitChrome.SetStatus(statusLabel, NoClipSetStatusMessage, ToolkitStatusTone.Warning);
                return;
            }

            if (currentRig == null)
            {
                ToolkitChrome.SetStatus(statusLabel, "Pick a rig to preview.", ToolkitStatusTone.Warning);
                return;
            }

            string liveStatusText = previewController.StatusMessage + (previewController.RagdollPreviewSleeping ? " — settled" : string.Empty);
            ToolkitChrome.SetStatus(statusLabel, liveStatusText, ToolkitStatusTone.Neutral);
        }

        private void OnPoseTimeSliderChanged(ChangeEvent<float> changeEvent)
        {
            restPoseNormalizedTime = changeEvent.newValue;
        }

        private void OnViewportPointerDown(PointerDownEvent pointerDownEvent)
        {
            Vector2 viewportPoint;
            float aspect;
            if (!TryGetViewportPoint(pointerDownEvent.localPosition, out viewportPoint, out aspect))
            {
                return;
            }

            if (dragSession.TryBegin(previewController, currentRig, selectedBodyId, viewportPoint, aspect))
            {
                pointerDownEvent.StopPropagation();
                viewportImage.CapturePointer(pointerDownEvent.pointerId);
            }
        }

        private void OnViewportPointerMove(PointerMoveEvent pointerMoveEvent)
        {
            Vector2 viewportPoint;
            float aspect;
            if (dragSession.ActiveHandle == RagdollBoxHandle.None || !TryGetViewportPoint(pointerMoveEvent.localPosition, out viewportPoint, out aspect))
            {
                return;
            }

            dragSession.Continue(previewController, currentRig, viewportPoint, aspect, pointerMoveEvent.shiftKey);
        }

        private void OnViewportPointerUp(PointerUpEvent pointerUpEvent)
        {
            if (dragSession.End(previewController, currentRig))
            {
                viewportImage.ReleasePointer(pointerUpEvent.pointerId);
                EditorUtility.SetDirty(currentRig);
                BodyBoxEdited?.Invoke();
            }
        }

        private bool TryGetViewportPoint(Vector2 localPosition, out Vector2 viewportPoint, out float aspect)
        {
            viewportPoint = Vector2.zero;
            aspect = 1f;

            Rect viewportRect = viewportImage.contentRect;
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

        private void Tick()
        {
            if (isDisposed)
            {
                return;
            }

            Rect viewportRect = viewportImage.contentRect;
            if (float.IsNaN(viewportRect.width) || viewportRect.width < 1f || viewportRect.height < 1f)
            {
                return;
            }

            Texture renderedTexture = previewController.Render(Mathf.RoundToInt(viewportRect.width), Mathf.RoundToInt(viewportRect.height));
            if (renderedTexture != null)
            {
                viewportImage.image = renderedTexture;
                viewportImage.MarkDirtyRepaint();
            }

            RefreshStatus();
            transport.RefreshState();
        }
    }
}
