using System;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Standalone orbit-camera rig ported from ClipPreviewController's orbit math, for hosts
    /// (e.g. the VAT bake preview) that have no controller of their own to derive a camera from.
    /// </summary>
    public sealed class PreviewOrbitCameraRig : IPreviewCameraRig
    {
        private const float DefaultOrbitDistance = 6f;
        private const float MinimumOrbitDistance = 1f;
        private const float MaximumOrbitDistance = 60f;
        private const float DegreesPerDragPixel = 0.4f;
        private const float FlySpeedUnitsPerSecond = 4f;
        private const float FlyFastMultiplier = 4f;
        private const float DollyFractionPerPixel = 0.005f;
        private const float FrameFieldOfViewDegrees = 45f;
        private const float FramePadding = 1.25f;
        private const float MinimumFrameRadius = 0.25f;

        private float orbitYaw = 0f;
        private float orbitPitch = 0f;
        private float orbitDistance = DefaultOrbitDistance;
        private Vector3 orbitFocus = Vector3.zero;

        private Bounds frameTargetBounds;
        private bool hasFrameTarget = false;

        private Quaternion OrbitRotation
        {
            get { return Quaternion.Euler(orbitPitch, orbitYaw, 0f); }
        }

        // Where the orbit rig puts the camera - the focus, backed off along the view.
        private Vector3 CameraOrbitPosition
        {
            get { return orbitFocus + OrbitRotation * new Vector3(0f, 0f, -orbitDistance); }
        }

        public Vector3 CameraPosition
        {
            get { return CameraOrbitPosition; }
        }

        public Quaternion CameraRotation
        {
            get { return OrbitRotation; }
        }

        public float DebugOrbitDistance
        {
            get { return orbitDistance; }
        }

        public void Orbit(Vector2 pixelDelta)
        {
            orbitYaw += pixelDelta.x * DegreesPerDragPixel;
            orbitPitch = Mathf.Clamp(orbitPitch + pixelDelta.y * DegreesPerDragPixel, -85f, 85f);
        }

        public void Zoom(float amount)
        {
            orbitDistance = Mathf.Clamp(orbitDistance + amount, MinimumOrbitDistance, MaximumOrbitDistance);
        }

        // The viewport's height has to come from the caller: a pan only tracks the pointer if a pixel of
        // drag is worth exactly the world distance a pixel spans at the focus.
        public void Pan(Vector2 pixelDelta, float viewportHeightPixels)
        {
            if (viewportHeightPixels < 1f) { return; }
            float halfFieldOfViewRadians = FrameFieldOfViewDegrees * 0.5f * Mathf.Deg2Rad;
            float worldUnitsPerPixel = 2f * orbitDistance * Mathf.Tan(halfFieldOfViewRadians) / viewportHeightPixels;
            // Negated in x and not in y because the world moves with the pointer while UI Toolkit's y runs
            // down the screen: dragging right pushes the scene right, so the camera goes left.
            orbitFocus += OrbitRotation * new Vector3(-pixelDelta.x * worldUnitsPerPixel, pixelDelta.y * worldUnitsPerPixel, 0f);
        }

        // Turns the camera about its own position rather than about the rig. This class has no camera
        // position field - it is derived from the focus - so a look must capture the position FIRST,
        // rotate, then put the focus back `distance` ahead of the (now rotated) camera. Getting this
        // backwards swings the camera around the rig instead of turning it in place.
        public void LookAround(Vector2 pixelDelta)
        {
            Vector3 heldCameraPosition = CameraOrbitPosition;
            orbitYaw += pixelDelta.x * DegreesPerDragPixel;
            orbitPitch = Mathf.Clamp(orbitPitch + pixelDelta.y * DegreesPerDragPixel, -85f, 85f);
            orbitFocus = heldCameraPosition + OrbitRotation * new Vector3(0f, 0f, orbitDistance);
        }

        // Dragging right or up moves in.
        public void Dolly(Vector2 pixelDelta)
        {
            float pixelsTowardsSubject = pixelDelta.x - pixelDelta.y;
            Zoom(-pixelsTowardsSubject * orbitDistance * DollyFractionPerPixel);
        }

        // Moves the focus, which carries the camera with it; the distance between them is untouched on
        // purpose, so the orbit pivot stays the same distance ahead of the camera after a flight.
        public void Fly(Vector3 localDirection, float deltaSeconds, bool fast)
        {
            if (localDirection.sqrMagnitude < Mathf.Epsilon || deltaSeconds <= 0f) { return; }
            float speed = FlySpeedUnitsPerSecond * (fast ? FlyFastMultiplier : 1f);
            orbitFocus += OrbitRotation * localDirection.normalized * speed * deltaSeconds;
        }

        public void ResetView()
        {
            orbitYaw = 0f;
            orbitPitch = 0f;
            if (hasFrameTarget) { Frame(frameTargetBounds); }
        }

        // No selection concept exists in a bare mesh preview - frame the whole target, same as ResetView.
        public void FrameSelection()
        {
            if (hasFrameTarget) { Frame(frameTargetBounds); }
        }

        public void SetFrameTarget(Bounds bounds)
        {
            frameTargetBounds = bounds;
            hasFrameTarget = true;
        }

        public void Frame(Bounds bounds)
        {
            orbitFocus = bounds.center;
            orbitDistance = DistanceThatFrames(Mathf.Max(bounds.extents.magnitude, MinimumFrameRadius));
        }

        public void ApplyTo(Camera camera)
        {
            camera.transform.position = CameraOrbitPosition;
            camera.transform.rotation = OrbitRotation;
        }

        public PreviewCameraPose CapturePose()
        {
            return new PreviewCameraPose
            {
                yawDegrees = orbitYaw,
                pitchDegrees = orbitPitch,
                distance = orbitDistance,
                focus = orbitFocus
            };
        }

        public void RestorePose(in PreviewCameraPose pose)
        {
            orbitYaw = pose.yawDegrees;
            orbitPitch = pose.pitchDegrees;
            orbitDistance = pose.distance;
            orbitFocus = pose.focus;
        }

        // How far back a sphere of `radius` has to be seen from to fit the frame, clamped to the range
        // Zoom allows - framing must never put the camera somewhere the user cannot zoom back out of.
        private static float DistanceThatFrames(float radius)
        {
            float halfFieldOfViewRadians = FrameFieldOfViewDegrees * 0.5f * Mathf.Deg2Rad;
            return Mathf.Clamp(radius / Mathf.Tan(halfFieldOfViewRadians) * FramePadding, MinimumOrbitDistance, MaximumOrbitDistance);
        }
    }
}
