using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Rig-agnostic camera surface a preview viewport implements so navigation gestures can drive it.</summary>
    public interface IPreviewCameraRig
    {
        void Orbit(Vector2 pixelDelta);
        void Zoom(float amount);
        void Pan(Vector2 pixelDelta, float viewportHeightPixels);
        void LookAround(Vector2 pixelDelta);
        void Dolly(Vector2 pixelDelta);
        void Fly(Vector3 localDirection, float deltaSeconds, bool fast);
        void ResetView();
        void FrameSelection();
    }

    /// <summary>Snapshot of an orbit camera's yaw/pitch/distance/focus, for rigs that want to store or restore a pose.</summary>
    public struct PreviewCameraPose
    {
        public float yawDegrees;
        public float pitchDegrees;
        public float distance;
        public Vector3 focus;
    }
}
