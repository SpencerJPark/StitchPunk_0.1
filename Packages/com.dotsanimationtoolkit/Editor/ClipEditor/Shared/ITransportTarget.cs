// Copyright (c) 2026 Spencer Park. All rights reserved.

namespace DotsAnimationToolkit.Editor
{
    [System.Flags]
    public enum TransportCapabilities
    {
        None = 0,
        StepBack = 1,
        StepForward = 2,
        Stop = 4,
        JumpToEnd = 8,
        Loop = 16
    }

    /// <summary>
    /// What a tab must offer so the shared transport bar and the window's key routing can drive it.
    /// </summary>
    public interface ITransportTarget
    {
        bool IsPlaying { get; }
        bool IsLooping { get; set; }
        TransportCapabilities Capabilities { get; }
        void TogglePlay();
        void Stop();
        void JumpToStart();
        void JumpToEnd();
        void Step(int frameDelta);
    }
}
