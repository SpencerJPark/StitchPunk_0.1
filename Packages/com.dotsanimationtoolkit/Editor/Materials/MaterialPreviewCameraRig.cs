// Copyright (c) 2026 Spencer Park. All rights reserved.

using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    // Orbit rig scaled to the subject: a mesh of any size or plane is framed and navigable without
    // the character-scale limits (minimum focus height, fixed distance range, fixed fly speed).
    public sealed class MaterialPreviewCameraRig : IPreviewCameraRig
    {
        private const float DegreesPerDragPixel = 0.4f;
        private const float FieldOfViewDegrees = 45f;
        private const float FramePadding = 1.3f;
        private const float ReturnYawDegrees = 30f;
        private const float ReturnPitchDegrees = 25f;
        private const float MinimumDistanceInRadii = 0.05f;
        private const float MaximumDistanceInRadii = 50f;
        private const float ZoomFractionPerAmount = 0.1f;
        private const float FlyRadiiPerSecond = 1.5f;
        private const float FlyFastMultiplier = 4f;
        private const float DollyFractionPerPixel = 0.005f;
        private const float MinimumSubjectRadius = 0.001f;

        private float orbitYaw = ReturnYawDegrees;
        private float orbitPitch = ReturnPitchDegrees;
        private float orbitDistance = 3f;
        private Vector3 orbitFocus = Vector3.zero;
        private float subjectRadius = 1f;

        private Bounds frameTargetBounds;
        private bool hasFrameTarget = false;

        public float SubjectRadius
        {
            get { return subjectRadius; }
        }

        public float SuggestedNearClip
        {
            get { return Mathf.Max(orbitDistance * 0.01f, subjectRadius * 0.001f); }
        }

        public float SuggestedFarClip
        {
            get { return orbitDistance + subjectRadius * 100f; }
        }

        private Quaternion OrbitRotation
        {
            get { return Quaternion.Euler(orbitPitch, orbitYaw, 0f); }
        }

        private Vector3 CameraOrbitPosition
        {
            get { return orbitFocus + OrbitRotation * new Vector3(0f, 0f, -orbitDistance); }
        }

        public void Orbit(Vector2 pixelDelta)
        {
            orbitYaw += pixelDelta.x * DegreesPerDragPixel;
            orbitPitch = Mathf.Clamp(orbitPitch + pixelDelta.y * DegreesPerDragPixel, -85f, 85f);
        }

        // Scaled by the current distance so a wheel notch feels the same at any subject scale.
        public void Zoom(float amount)
        {
            orbitDistance = ClampDistance(orbitDistance * (1f + amount * ZoomFractionPerAmount));
        }

        public void Pan(Vector2 pixelDelta, float viewportHeightPixels)
        {
            if (viewportHeightPixels < 1f) { return; }
            float halfFieldOfViewRadians = FieldOfViewDegrees * 0.5f * Mathf.Deg2Rad;
            float worldUnitsPerPixel = 2f * orbitDistance * Mathf.Tan(halfFieldOfViewRadians) / viewportHeightPixels;
            orbitFocus += OrbitRotation * new Vector3(-pixelDelta.x * worldUnitsPerPixel, pixelDelta.y * worldUnitsPerPixel, 0f);
        }

        // Capture the camera position first, rotate, then put the focus back ahead of the rotated
        // camera; the other order swings the camera around the rig instead of turning it in place.
        public void LookAround(Vector2 pixelDelta)
        {
            Vector3 heldCameraPosition = CameraOrbitPosition;
            orbitYaw += pixelDelta.x * DegreesPerDragPixel;
            orbitPitch = Mathf.Clamp(orbitPitch + pixelDelta.y * DegreesPerDragPixel, -85f, 85f);
            orbitFocus = heldCameraPosition + OrbitRotation * new Vector3(0f, 0f, orbitDistance);
        }

        public void Dolly(Vector2 pixelDelta)
        {
            float pixelsTowardsSubject = pixelDelta.x - pixelDelta.y;
            orbitDistance = ClampDistance(orbitDistance - pixelsTowardsSubject * orbitDistance * DollyFractionPerPixel);
        }

        public void Fly(Vector3 localDirection, float deltaSeconds, bool fast)
        {
            if (localDirection.sqrMagnitude < Mathf.Epsilon || deltaSeconds <= 0f) { return; }
            float speed = subjectRadius * FlyRadiiPerSecond * (fast ? FlyFastMultiplier : 1f);
            orbitFocus += OrbitRotation * localDirection.normalized * speed * deltaSeconds;
        }

        public void ResetView()
        {
            orbitYaw = ReturnYawDegrees;
            orbitPitch = ReturnPitchDegrees;
            if (hasFrameTarget)
            {
                Frame(frameTargetBounds);
            }
        }

        public void FrameSelection()
        {
            ResetView();
        }

        public void SetFrameTarget(Bounds bounds)
        {
            frameTargetBounds = bounds;
            hasFrameTarget = true;
            subjectRadius = Mathf.Max(bounds.extents.magnitude, MinimumSubjectRadius);
        }

        public void ApplyTo(Camera camera)
        {
            camera.transform.position = CameraOrbitPosition;
            camera.transform.rotation = OrbitRotation;
        }

        private void Frame(Bounds bounds)
        {
            subjectRadius = Mathf.Max(bounds.extents.magnitude, MinimumSubjectRadius);
            orbitFocus = bounds.center;
            float halfFieldOfViewRadians = FieldOfViewDegrees * 0.5f * Mathf.Deg2Rad;
            orbitDistance = ClampDistance(subjectRadius / Mathf.Tan(halfFieldOfViewRadians) * FramePadding);
        }

        private float ClampDistance(float distance)
        {
            return Mathf.Clamp(distance, subjectRadius * MinimumDistanceInRadii, subjectRadius * MaximumDistanceInRadii);
        }
    }
}
