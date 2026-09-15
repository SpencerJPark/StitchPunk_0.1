// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// The one viewport frame and overlay rail every preview tab shares: an image, an overlay
    /// column, and helpers that parent each control's icon before resolving it.
    /// </summary>
    public sealed class ViewportFrameElement : VisualElement
    {
        public Image ViewportImage { get; }

        public VisualElement Overlay { get; }

        public VisualElement OverlayColumn { get; }

        public ViewportFrameElement()
        {
            AddToClassList("clip-editor__viewport-frame");
            style.flexGrow = 1f;

            ViewportImage = new Image { scaleMode = ScaleMode.ScaleToFit };
            ViewportImage.style.flexGrow = 1f;
            Add(ViewportImage);

            Overlay = new VisualElement { name = "viewport-overlay" };
            Overlay.AddToClassList("clip-editor__viewport-overlay");
            Overlay.pickingMode = PickingMode.Ignore;
            Add(Overlay);

            OverlayColumn = new VisualElement { name = "overlay-column" };
            OverlayColumn.AddToClassList("clip-editor__overlay-column");
            Overlay.Add(OverlayColumn);
        }

        public ToolbarButton AddRailButton(string exactIconName, string tooltip, string fallbackText, Action onClick, bool startsRun = false)
        {
            ToolbarButton railButton = new ToolbarButton(onClick);
            railButton.AddToClassList("clip-editor__overlay-tool-button");
            if (startsRun)
            {
                railButton.AddToClassList("clip-editor__overlay-run-break");
            }
            railButton.tooltip = tooltip;

            Image railButtonIcon = new Image { pickingMode = PickingMode.Ignore };
            railButtonIcon.AddToClassList("clip-editor__overlay-tool-icon");
            // The icon must be parented before ToolkitIcons resolves it, or a resolved icon leaves a blank button.
            railButton.Insert(0, railButtonIcon);
            ToolkitIcons.SetButtonIcon(railButton, railButtonIcon, exactIconName, fallbackText);

            OverlayColumn.Add(railButton);
            return railButton;
        }

        public ToolbarToggle AddRailToggle(string exactIconName, string tooltip, string fallbackText, bool startsRun = false)
        {
            ToolbarToggle railToggle = CreateRailToggle(tooltip, startsRun, out Image railToggleIcon);
            ToolkitIcons.SetToggleIcon(railToggle, railToggleIcon, exactIconName, fallbackText);
            OverlayColumn.Add(railToggle);
            return railToggle;
        }

        public ToolbarToggle AddRailToggle(Texture iconTexture, string tooltip, string fallbackText, bool startsRun = false)
        {
            ToolbarToggle railToggle = CreateRailToggle(tooltip, startsRun, out Image railToggleIcon);
            ToolkitIcons.SetToggleIcon(railToggle, railToggleIcon, iconTexture, fallbackText);
            OverlayColumn.Add(railToggle);
            return railToggle;
        }

        public ToolbarButton AddResetCameraButton(Action onClick)
        {
            return AddRailButton(
                "d_FrameCapture",
                "Put the camera back where the window opened it: head-on, centred on this rig "
                + "and backed off to fit it. Undoes any orbit, pan or flight. Same as "
                + "double-clicking the viewport.\n\n"
                + "Viewport camera: drag to orbit, middle-drag to pan, right-drag to look "
                + "around, right-drag + W/A/S/D and Q/E to fly (Shift for faster), "
                + "Alt + right-drag or the wheel to zoom, F to frame the selection.",
                "Reset Camera",
                onClick);
        }

        private static ToolbarToggle CreateRailToggle(string tooltip, bool startsRun, out Image railToggleIcon)
        {
            ToolbarToggle railToggle = new ToolbarToggle();
            railToggle.AddToClassList("clip-editor__overlay-tool-button");
            if (startsRun)
            {
                railToggle.AddToClassList("clip-editor__overlay-run-break");
            }
            railToggle.tooltip = tooltip;

            railToggleIcon = new Image { pickingMode = PickingMode.Ignore };
            railToggleIcon.AddToClassList("clip-editor__overlay-tool-icon");
            // The icon must be parented before ToolkitIcons resolves it, or a resolved icon leaves a blank toggle.
            railToggle.Add(railToggleIcon);

            return railToggle;
        }
    }
}
