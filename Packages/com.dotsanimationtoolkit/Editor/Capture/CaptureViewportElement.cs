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
            VisualElement viewportFrame = new VisualElement();
            viewportFrame.AddToClassList("clip-editor__viewport-frame");
            viewportFrame.style.flexGrow = 1f;
            viewportFrame.style.backgroundColor = new Color(0.12f, 0.12f, 0.12f);

            viewportImage = new Image();
            viewportImage.scaleMode = ScaleMode.ScaleToFit;
            viewportImage.style.flexGrow = 1f;
            viewportFrame.Add(viewportImage);

            VisualElement viewportOverlay = new VisualElement();
            viewportOverlay.AddToClassList("clip-editor__viewport-overlay");
            viewportOverlay.pickingMode = PickingMode.Ignore;

            VisualElement overlayColumn = new VisualElement();
            overlayColumn.AddToClassList("clip-editor__overlay-column");

            ToolbarButton resetCameraButton = new ToolbarButton(() => cameraNavigation.ResetView());
            resetCameraButton.name = "capture-reset-camera-button";
            resetCameraButton.AddToClassList("clip-editor__overlay-tool-button");
            resetCameraButton.tooltip = "Put the camera back head-on, framing the capture.";
            Image resetCameraIcon = new Image();
            resetCameraIcon.AddToClassList("clip-editor__overlay-tool-icon");
            resetCameraIcon.pickingMode = PickingMode.Ignore;
            resetCameraButton.Insert(0, resetCameraIcon);
            ToolkitIcons.SetButtonIcon(resetCameraButton, resetCameraIcon, "d_FrameCapture", "Reset Camera");
            overlayColumn.Add(resetCameraButton);

            viewportOverlay.Add(overlayColumn);
            viewportFrame.Add(viewportOverlay);
            Add(viewportFrame);

            statusLabel = new Label("Choose something to capture.");
            statusLabel.style.whiteSpace = WhiteSpace.Normal;
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
                statusLabel.text = source == null ? "Choose something to capture." : source.NotReadyReason;
                statusLabel.style.display = DisplayStyle.Flex;
                viewportImage.image = null;
                return;
            }
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
