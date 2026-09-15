// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using DotsAnimationToolkit.Authoring;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Captures one clip of a clip set on a rig through its own ClipPreviewController.</summary>
    public sealed class ClipCaptureSource : ICaptureSource
    {
        public ClipCaptureSource(ClipSetAsset clipSet, RigAsset rig, ClipAsset clip)
        {
            throw new NotImplementedException();
        }

        public string CaptureName { get { throw new NotImplementedException(); } }
        public string CameraPoseKey { get { throw new NotImplementedException(); } }
        public string NotReadyReason { get { throw new NotImplementedException(); } }
        public float DurationSeconds { get { throw new NotImplementedException(); } }
        public IPreviewCameraRig CameraRig { get { throw new NotImplementedException(); } }

        public PreviewCameraPose CaptureCameraPose() { throw new NotImplementedException(); }
        public void RestoreCameraPose(in PreviewCameraPose pose) { throw new NotImplementedException(); }
        public void PoseAt(float seconds) { throw new NotImplementedException(); }

        public Texture RenderFrame(int pixelWidth, int pixelHeight, CaptureBackgroundMode background, Color backgroundColour)
        {
            throw new NotImplementedException();
        }

        public void Dispose() { throw new NotImplementedException(); }
    }
}
