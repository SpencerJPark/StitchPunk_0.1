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
        private readonly PopupField<string> groundField;

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

            previewController = new ClipPreviewController();
            cameraNavigation = new PreviewCameraNavigation();
            dragSession = new RagdollBoxDragSession();

            // Transport sits under the viewport as on every other tab that plays; the ground choice is
            // a viewport setting, so it rides the pane header.
            VisualElement headerRow = ToolkitChrome.MakePaneHeader("Viewport", out _, out VisualElement headerActions);
            groundField = new PopupField<string>(BuildGroundChoices(), 0);
            groundField.name = "ragdoll-ground-field";
            groundField.RegisterValueChangedCallback(OnGroundFieldChanged);
            headerActions.Add(groundField);
            Add(headerRow);

            VisualElement poseRow = new VisualElement();
            poseRow.name = "ragdoll-pose-row";
            poseRow.style.flexDirection = FlexDirection.Row;
            poseRow.style.alignItems = Align.Center;
            poseRow.style.paddingLeft = 6f;
            poseRow.style.paddingRight = 8f;
            poseRow.style.paddingBottom = 4f;
            poseFromClipToggle = new Toggle("Pose from clip");
            poseFromClipToggle.name = "ragdoll-pose-from-clip-toggle";
            poseFromClipToggle.tooltip = DropTooltip;
            poseRow.Add(poseFromClipToggle);
            poseClipLabel = new Label("No clip bound");
            poseClipLabel.tooltip = DropTooltip;
            poseClipLabel.AddToClassList("toolkit-hint");
            poseClipLabel.style.marginLeft = 8f;
            poseClipLabel.style.marginRight = 8f;
            poseRow.Add(poseClipLabel);
            poseTimeSlider = new Slider(0f, 1f);
            poseTimeSlider.style.flexGrow = 1f;
            poseTimeSlider.name = "ragdoll-pose-time-slider";
            poseTimeSlider.tooltip = DropTooltip;
            poseTimeSlider.RegisterValueChangedCallback(OnPoseTimeSliderChanged);
            poseRow.Add(poseTimeSlider);
            Add(poseRow);

            ViewportFrameElement viewportFrame = new ViewportFrameElement();
            viewportFrame.AddResetCameraButton(() => cameraNavigation.ResetView());

            viewportImage = viewportFrame.ViewportImage;
            viewportImage.name = "ragdoll-viewport-image";
            viewportImage.RegisterCallback<PointerDownEvent>(OnViewportPointerDown);
            viewportImage.RegisterCallback<PointerMoveEvent>(OnViewportPointerMove);
            viewportImage.RegisterCallback<PointerUpEvent>(OnViewportPointerUp);
            Add(viewportFrame);

            VisualElement transportRow = new VisualElement();
            transportRow.AddToClassList("toolkit-transport");
            VisualElement transportGroup = new VisualElement();
            transportGroup.AddToClassList("toolkit-transport__group");
            transport = new TransportCoreElement();
            transport.name = "ragdoll-transport";
            transport.Bind(this);
            transportGroup.Add(transport);
            transportRow.Add(transportGroup);
            Add(transportRow);

            statusLabel = ToolkitChrome.MakeHint(string.Empty);
            statusLabel.name = "ragdoll-viewport-status";
            Add(statusLabel);

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

            statusLabel.text = currentRig == null ? "Pick a rig to preview." : previewController.StatusMessage;
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
                statusLabel.text = refusalReason;
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

        private void OnGroundFieldChanged(ChangeEvent<string> changeEvent)
        {
            List<RagdollPreviewPropDefinition> props = RagdollPreviewScenery.instance.Props;
            for (int index = 0; index < props.Count; index++)
            {
                props[index].enabled = props[index].displayName == changeEvent.newValue;
            }
            RagdollPreviewScenery.instance.PersistChange();
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

            statusLabel.text = previewController.StatusMessage + (previewController.RagdollPreviewSleeping ? " — settled" : string.Empty);
            transport.RefreshState();
        }
    }
}
