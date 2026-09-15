// Copyright (c) 2026 Spencer Park. All rights reserved.
using System;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Hosts a live pose preview for a retarget pick, playing the picked clip on the picked rig.</summary>
    public sealed class RetargetPreviewElement : VisualElement, IDisposable
    {
        private readonly ClipPreviewController previewController;
        private readonly PreviewCameraNavigation cameraNavigation;
        private readonly Image viewportImage;
        private readonly Button playButton;
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

            VisualElement headerRow = new VisualElement();
            headerRow.AddToClassList("toolkit-pane-header");
            Label titleLabel = new Label("Preview");
            titleLabel.AddToClassList("toolkit-pane-title");
            headerRow.Add(titleLabel);
            Add(headerRow);

            VisualElement toolbarRow = new VisualElement();
            toolbarRow.style.flexDirection = FlexDirection.Row;
            playButton = new Button(TogglePlayback) { text = "Pause" };
            playButton.name = "retarget-preview-play-button";
            toolbarRow.Add(playButton);
            Button resetViewButton = new Button(() => cameraNavigation.ResetView()) { text = "Reset View" };
            toolbarRow.Add(resetViewButton);
            Add(toolbarRow);

            VisualElement viewportFrame = new VisualElement();
            viewportFrame.style.flexGrow = 1f;
            viewportImage = new Image();
            viewportImage.style.flexGrow = 1f;
            viewportFrame.Add(viewportImage);
            Add(viewportFrame);

            statusLabel = new Label();
            statusLabel.AddToClassList("clip-editor__hint");
            statusLabel.name = "retarget-preview-status";
            Add(statusLabel);

            cameraNavigation.Rig = previewController;
            cameraNavigation.AttachTo(viewportImage);

            isPlaying = true;
            lastTickTimeSeconds = EditorApplication.timeSinceStartup;

            RegisterCallback<AttachToPanelEvent>(attachEvent => EditorApplication.update += Tick);
            RegisterCallback<DetachFromPanelEvent>(detachEvent => EditorApplication.update -= Tick);
        }

        public bool IsPlaying => isPlaying;

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

        private void TogglePlayback()
        {
            isPlaying = !isPlaying;
            playButton.text = isPlaying ? "Pause" : "Play";
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
