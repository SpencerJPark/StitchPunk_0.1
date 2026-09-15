// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Renders a capture one frame per EditorApplication.update tick, writing PNGs or collecting GIF frames.</summary>
    public sealed class FrameCaptureRunner
    {
        public bool IsRunning { get { throw new NotImplementedException(); } }
        public bool WasCancelled { get { throw new NotImplementedException(); } }
        public int FrameCount { get { throw new NotImplementedException(); } }
        public int FramesCaptured { get { throw new NotImplementedException(); } }
        public IReadOnlyList<string> WrittenFilePaths { get { throw new NotImplementedException(); } }

        public void Start(ICaptureSource source, CaptureSettings settings, Action<int, int> progress, Action<string> finished)
        {
            throw new NotImplementedException();
        }

        public void Cancel() { throw new NotImplementedException(); }
    }
}
