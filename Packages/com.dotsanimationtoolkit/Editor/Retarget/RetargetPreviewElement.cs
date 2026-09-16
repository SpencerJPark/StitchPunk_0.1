// Copyright (c) 2026 Spencer Park. All rights reserved.
using System;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Hosts a live pose preview for a retarget pick, playing the picked clip on the picked rig.</summary>
    public sealed class RetargetPreviewElement : VisualElement, IDisposable, ITransportTarget
    {
        private readonly ClipPreviewController previewController;
        private readonly PreviewCameraNavigation cameraNavigation;
        private readonly Image viewportImage;
        private readonly ViewportFrameElement viewportFrame;
        private readonly TransportCoreElement transportCore;
        private readonly Label statusLabel;

        private ClipSetAsset boundClipSet;
        private ClipAsset boundClip;
        private RigAsset boundRig;
        private float normalizedTime;
        private bool isPlaying;
        private bool isDisposed;
        private double lastTickTimeSeconds;

        public RetargetPreviewElement()
        {
            name = "retarget-preview";
            style.flexGrow = 1f;
            style.flexDirection = FlexDirection.Column;

            previewController = new ClipPreviewController();
            cameraNavigation = new PreviewCameraNavigation();
            isPlaying = true;

            VisualElement headerRow = new VisualElement();
            headerRow.AddToClassList("toolkit-pane-header");
            Label titleLabel = new Label("Preview");
            titleLabel.AddToClassList("toolkit-pane-title");
            headerRow.Add(titleLabel);
            Add(headerRow);

            viewportFrame = new ViewportFrameElement();
            Button resetCameraButton = viewportFrame.AddResetCameraButton(() => cameraNavigation.ResetView());
            resetCameraButton.name = "retarget-preview-reset-camera-button";
            viewportImage = viewportFrame.ViewportImage;
            viewportFrame.SetEmptyState(
                "retarget-preview-empty",
                "No pose to preview",
                "Pick a clip set, a clip and a rig in the bar above to see the clip play on that rig.");
            Add(viewportFrame);

            VisualElement transportRow = new VisualElement();
            transportRow.AddToClassList("toolkit-transport");
            VisualElement transportGroup = new VisualElement();
            transportGroup.AddToClassList("toolkit-transport__group");
            transportCore = new TransportCoreElement();
            transportCore.Bind(this);
            transportGroup.Add(transportCore);
            transportRow.Add(transportGroup);
            Add(transportRow);

            statusLabel = new Label();
            statusLabel.AddToClassList("toolkit-hint");
            statusLabel.name = "retarget-preview-status";
            Add(statusLabel);

            cameraNavigation.Rig = previewController;
            cameraNavigation.AttachTo(viewportImage);

            lastTickTimeSeconds = EditorApplication.timeSinceStartup;

            RegisterCallback<AttachToPanelEvent>(attachEvent => EditorApplication.update += Tick);
            RegisterCallback<DetachFromPanelEvent>(detachEvent => EditorApplication.update -= Tick);
        }

        public bool IsPlaying => isPlaying;

        public bool IsLooping { get; set; }

        public TransportCapabilities Capabilities => TransportCapabilities.None;

        public ITransportTarget TransportTarget { get { return this; } }

        public void Show(ClipSetAsset clipSet, ClipAsset clip, RigAsset rig)
        {
            if (rig != boundRig)
            {
                boundRig = rig;
                previewController.SetRig(rig);
                previewController.FrameRig();
            }

            if (clipSet != boundClipSet)
            {
                boundClipSet = clipSet;
                previewController.SetClipSet(clipSet);
            }

            if (clip != boundClip)
            {
                boundClip = clip;
                normalizedTime = 0f;
            }
        }

        public int FindHierarchyIndexByName(string boneName)
        {
            return previewController.FindHierarchyIndexByName(boneName);
        }

        public void TogglePlay()
        {
            isPlaying = !isPlaying;
            transportCore.RefreshState();
        }

        public void Stop()
        {
        }

        public void JumpToStart()
        {
        }

        public void JumpToEnd()
        {
        }

        public void Step(int frameDelta)
        {
        }

        private void Tick()
        {
            if (isDisposed)
            {
                return;
            }

            double currentTimeSeconds = EditorApplication.timeSinceStartup;
            float elapsedSeconds = (float)(currentTimeSeconds - lastTickTimeSeconds);
            lastTickTimeSeconds = currentTimeSeconds;

            viewportFrame.ShowEmptyState(boundClip == null || boundRig == null);

            Rect viewportRect = viewportImage.contentRect;
            if (float.IsNaN(viewportRect.width) || viewportRect.width < 1f || viewportRect.height < 1f)
            {
                return;
            }

            if (isPlaying && boundClip != null)
            {
                float clipDurationSeconds = Mathf.Max(boundClip.duration, 0.001f);
                normalizedTime = Mathf.Repeat(normalizedTime + (elapsedSeconds / clipDurationSeconds), 1f);
            }

            if (boundClip != null && boundRig != null)
            {
                previewController.SamplePose(boundClip.stableId, normalizedTime);
            }

            Texture renderedTexture = previewController.Render(Mathf.RoundToInt(viewportRect.width), Mathf.RoundToInt(viewportRect.height));
            if (renderedTexture != null)
            {
                viewportImage.image = renderedTexture;
                viewportImage.MarkDirtyRepaint();
            }

            statusLabel.text = ResolveStatusText();
        }

        private string ResolveStatusText()
        {
            if (boundRig == null)
            {
                return "Pick a rig to preview.";
            }

            if (boundClip == null)
            {
                return "Pick a clip.";
            }

            if (!previewController.IsClipInRegistry(boundClip.stableId))
            {
                return "This clip is not in the picked set, so nothing poses.";
            }

            return previewController.StatusMessage;
        }

        public void Dispose()
        {
            if (isDisposed)
            {
                return;
            }

            isDisposed = true;
            EditorApplication.update -= Tick;
            previewController.Dispose();
        }
    }
}
