// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>The Capture tab's framing viewport: renders the source at the capture aspect and drives its orbit rig.</summary>
    public sealed class CaptureViewportElement : VisualElement, IDisposable
    {
        public event Action CameraChanged;

        public CaptureViewportElement()
        {
            throw new NotImplementedException();
        }

        // Setting a source re-points the navigation's rig; the element never disposes the source.
        public ICaptureSource Source { get { throw new NotImplementedException(); } set { throw new NotImplementedException(); } }

        public float PreviewSeconds { get { throw new NotImplementedException(); } set { throw new NotImplementedException(); } }

        // True while a FrameCaptureRunner owns the source's renders.
        public bool RenderingSuspended { get { throw new NotImplementedException(); } set { throw new NotImplementedException(); } }

        public void ApplySettings(CaptureSettings settings) { throw new NotImplementedException(); }

        public void Dispose() { throw new NotImplementedException(); }
    }
}
