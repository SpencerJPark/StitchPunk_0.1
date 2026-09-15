// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>One thing the Capture tab can render over time: a clip, a profile animation or a cutscene.</summary>
    public interface ICaptureSource : IDisposable
    {
        // The default file stem, e.g. "Walk"; never empty.
        string CaptureName { get; }

        // Stable per source (asset GUID plus any selector), so a remembered camera pose finds its way back.
        string CameraPoseKey { get; }

        // Null when the source can render; otherwise the sentence the panel shows instead of the viewport.
        string NotReadyReason { get; }

        float DurationSeconds { get; }

        IPreviewCameraRig CameraRig { get; }

        PreviewCameraPose CaptureCameraPose();

        void RestoreCameraPose(in PreviewCameraPose pose);

        void PoseAt(float seconds);

        // The texture belongs to the source's render utility or target: read it, never destroy it.
        Texture RenderFrame(int pixelWidth, int pixelHeight, CaptureBackgroundMode background, Color backgroundColour);
    }
}
