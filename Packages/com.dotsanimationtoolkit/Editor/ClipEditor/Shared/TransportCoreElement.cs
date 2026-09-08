// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// The shared jump/step/play/stop/loop button run every transport bar inserts.
    /// </summary>
    public sealed class TransportCoreElement : VisualElement
    {
        public int largeStepFrames = 10;

        private Button jumpStartButton;
        private Button stepBackButton;
        private Button playButton;
        private Button stopButton;
        private Button stepForwardButton;
        private Button jumpEndButton;
        private Button loopButton;

        private ITransportTarget boundTarget;

        public TransportCoreElement()
        {
            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Center;

            jumpStartButton = ToolkitIcons.MakeIconButton(
                () => boundTarget?.JumpToStart(), ToolkitIcons.JumpToStart, string.Empty, "|<");
            jumpStartButton.name = "jump-start-button";
            Add(jumpStartButton);

            stepBackButton = ToolkitIcons.MakeIconButton(
                null, ToolkitIcons.StepBack, string.Empty, "<");
            stepBackButton.name = "step-back-button";
            stepBackButton.RegisterCallback<ClickEvent>(clickEvent =>
                boundTarget?.Step(clickEvent.shiftKey ? -largeStepFrames : -1));
            Add(stepBackButton);

            playButton = ToolkitIcons.MakeIconButton(
                () => boundTarget?.TogglePlay(), ToolkitIcons.Play, "Play or pause. Shortcut: Space.", "Play");
            playButton.name = "play-toggle";
            Add(playButton);

            stopButton = ToolkitIcons.MakeIconButton(
                () => boundTarget?.Stop(), ToolkitIcons.Stop,
                "Stop and return the playhead to where Play was pressed.", "Stop");
            stopButton.name = "stop-button";
            Add(stopButton);

            stepForwardButton = ToolkitIcons.MakeIconButton(
                null, ToolkitIcons.StepForward, string.Empty, ">");
            stepForwardButton.name = "step-forward-button";
            stepForwardButton.RegisterCallback<ClickEvent>(clickEvent =>
                boundTarget?.Step(clickEvent.shiftKey ? largeStepFrames : 1));
            Add(stepForwardButton);

            jumpEndButton = ToolkitIcons.MakeIconButton(
                () => boundTarget?.JumpToEnd(), ToolkitIcons.JumpToEnd, string.Empty, ">|");
            jumpEndButton.name = "jump-end-button";
            Add(jumpEndButton);

            loopButton = ToolkitIcons.MakeIconButton(
                () =>
                {
                    if (boundTarget == null)
                    {
                        return;
                    }
                    boundTarget.IsLooping = !boundTarget.IsLooping;
                    RefreshState();
                },
                ToolkitIcons.Loop,
                "Wrap at the end during preview playback. Preview only — the clip's own loop mode is authored on the asset.",
                "Loop");
            loopButton.name = "loop-button";
            Image loopIcon = loopButton.Q<Image>(className: "toolkit-icon-button__icon");
            if (loopIcon != null)
            {
                loopIcon.name = "loop-icon";
            }
            Add(loopButton);

            RefreshTooltips();
        }

        public void Bind(ITransportTarget target)
        {
            boundTarget = target;

            TransportCapabilities capabilities = target?.Capabilities ?? TransportCapabilities.None;
            stepBackButton.style.display =
                (capabilities & TransportCapabilities.StepBack) != 0 ? DisplayStyle.Flex : DisplayStyle.None;
            stepForwardButton.style.display =
                (capabilities & TransportCapabilities.StepForward) != 0 ? DisplayStyle.Flex : DisplayStyle.None;
            stopButton.style.display =
                (capabilities & TransportCapabilities.Stop) != 0 ? DisplayStyle.Flex : DisplayStyle.None;
            jumpEndButton.style.display =
                (capabilities & TransportCapabilities.JumpToEnd) != 0 ? DisplayStyle.Flex : DisplayStyle.None;
            loopButton.style.display =
                (capabilities & TransportCapabilities.Loop) != 0 ? DisplayStyle.Flex : DisplayStyle.None;

            RefreshState();
        }

        public void RefreshState()
        {
            if (boundTarget == null)
            {
                return;
            }

            ToolkitIcons.SetButtonIcon(
                playButton,
                boundTarget.IsPlaying ? ToolkitIcons.Pause : ToolkitIcons.Play,
                boundTarget.IsPlaying ? "Pause" : "Play");
            playButton.EnableInClassList("toolkit-icon-button--playing", boundTarget.IsPlaying);
            loopButton.EnableInClassList("toolkit-icon-button--lit", boundTarget.IsLooping);

            RefreshTooltips();
        }

        private void RefreshTooltips()
        {
            jumpStartButton.tooltip = "Jump to the first frame. Shortcut: Home.";
            stepBackButton.tooltip =
                "Step back one frame. Shortcut: Left arrow (Shift for " + largeStepFrames + ").";
            stepForwardButton.tooltip =
                "Step forward one frame. Shortcut: Right arrow (Shift for " + largeStepFrames + ").";
            jumpEndButton.tooltip = "Jump to the last frame. Shortcut: End.";
        }
    }
}
