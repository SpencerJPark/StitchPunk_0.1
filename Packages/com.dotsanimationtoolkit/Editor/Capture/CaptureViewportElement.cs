// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>The Capture tab's framing viewport: renders the source at the capture aspect and drives its orbit rig.</summary>
    public sealed class CaptureViewportElement : VisualElement, IDisposable
    {
        public event Action CameraChanged;

        private readonly PreviewCameraNavigation cameraNavigation = new PreviewCameraNavigation();
        private readonly Image viewportImage;
        private readonly VisualElement emptyState;
        private readonly Label statusLabel;

        private ICaptureSource source;
        private float previewSeconds;
        private bool renderingSuspended;
        private float lastTickTimeSinceStartup;

        private int settingsWidth = 512;
        private int settingsHeight = 512;
        private CaptureBackgroundMode settingsBackground = CaptureBackgroundMode.Transparent;
        private Color settingsBackgroundColour = new Color(0.17f, 0.17f, 0.18f, 1f);

        public CaptureViewportElement()
        {
            ViewportFrameElement frame = new ViewportFrameElement();
            frame.style.backgroundColor = new Color(0.12f, 0.12f, 0.12f); // colour from data

            ToolbarButton resetCameraButton = frame.AddResetCameraButton(() => cameraNavigation.ResetView());
            resetCameraButton.name = "capture-reset-camera-button";
            resetCameraButton.tooltip = "Put the camera back head-on, framing the capture.";

            viewportImage = frame.ViewportImage;
            viewportImage.scaleMode = ScaleMode.ScaleToFit;
            Add(frame);

            emptyState = ToolkitChrome.MakeEmptyState(
                "capture-viewport-empty",
                "Nothing to capture",
                "Pick a clip, a profile animation or a cutscene in the bar above.",
                null,
                null);
            emptyState.style.position = Position.Absolute;
            emptyState.style.left = 0f;
            emptyState.style.right = 0f;
            emptyState.style.top = 0f;
            emptyState.style.bottom = 0f;
            frame.Add(emptyState); // overlays the viewport image; shown only while there is no ready source

            statusLabel = ToolkitChrome.MakeHint(string.Empty);
            statusLabel.style.display = DisplayStyle.None;
            Add(statusLabel);

            cameraNavigation.AttachTo(viewportImage);
            cameraNavigation.CameraChanged += () => CameraChanged?.Invoke();

            RegisterCallback<AttachToPanelEvent>(evt => EditorApplication.update += Tick);
            RegisterCallback<DetachFromPanelEvent>(evt => EditorApplication.update -= Tick);
        }

        // Setting a source re-points the navigation's rig; the element never disposes the source.
        public ICaptureSource Source
        {
            get { return source; }
            set
            {
                source = value;
                cameraNavigation.Rig = source?.CameraRig;
            }
        }

        public float PreviewSeconds
        {
            get { return previewSeconds; }
            set { previewSeconds = value; }
        }

        // True while a FrameCaptureRunner owns the source's renders.
        public bool RenderingSuspended
        {
            get { return renderingSuspended; }
            set { renderingSuspended = value; }
        }

        public void ApplySettings(CaptureSettings settings)
        {
            settingsWidth = settings.width;
            settingsHeight = settings.height;
            settingsBackground = settings.background;
            settingsBackgroundColour = settings.backgroundColour;
        }

        private void Tick()
        {
            float deltaSeconds = lastTickTimeSinceStartup > 0f
                ? (float)EditorApplication.timeSinceStartup - lastTickTimeSinceStartup
                : 0f;
            lastTickTimeSinceStartup = (float)EditorApplication.timeSinceStartup;

            if (panel == null || renderingSuspended || source == null || source.NotReadyReason != null)
            {
                bool sourceMissing = source == null;
                emptyState.style.display = sourceMissing ? DisplayStyle.Flex : DisplayStyle.None;
                if (!sourceMissing)
                {
                    statusLabel.text = source.NotReadyReason;
                }
                statusLabel.style.display = sourceMissing ? DisplayStyle.None : DisplayStyle.Flex;
                viewportImage.image = null;
                return;
            }
            emptyState.style.display = DisplayStyle.None;
            statusLabel.style.display = DisplayStyle.None;

            cameraNavigation.StepFly(deltaSeconds);

            Rect viewportRect = viewportImage.contentRect;
            if (float.IsNaN(viewportRect.width) || viewportRect.width < 1f || viewportRect.height < 1f)
            {
                return;
            }

            float pixelsPerPoint = EditorGUIUtility.pixelsPerPoint;
            float captureAspect = settingsWidth / (float)settingsHeight;
            float availableWidthPixels = viewportRect.width * pixelsPerPoint;
            float availableHeightPixels = viewportRect.height * pixelsPerPoint;

            float fitWidth = availableWidthPixels;
            float fitHeight = fitWidth / captureAspect;
            if (fitHeight > availableHeightPixels)
            {
                fitHeight = availableHeightPixels;
                fitWidth = fitHeight * captureAspect;
            }

            int clampedFitWidth = Mathf.Clamp(Mathf.RoundToInt(fitWidth), 8, settingsWidth);
            int clampedFitHeight = Mathf.Clamp(Mathf.RoundToInt(fitHeight), 8, settingsHeight);

            source.PoseAt(previewSeconds);
            // Rendering exactly what will be captured, backdrop included, is deliberate: no grid overlay here.
            Texture renderedFrame = source.RenderFrame(clampedFitWidth, clampedFitHeight, settingsBackground, settingsBackgroundColour);
            viewportImage.image = renderedFrame;
            viewportImage.MarkDirtyRepaint();
        }

        public void Dispose()
        {
            EditorApplication.update -= Tick;
            viewportImage.image = null;
        }
    }
}
