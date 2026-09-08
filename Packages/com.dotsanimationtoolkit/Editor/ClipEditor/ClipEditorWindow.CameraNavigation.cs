// Copyright (c) 2026 Spencer Park. All rights reserved.

using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    public sealed partial class ClipEditorWindow
    {
        // The window still has to try ragdoll and gizmo handle drags, and its own pick logic,
        // between the exclusive camera gestures and the plain left-drag orbit (OnPreviewPointerDown) —
        // so it keeps these method names as a thin adapter over the shared state machine rather than
        // wiring PreviewCameraNavigation.AttachTo directly, which assumes a host with no picking of its own.
        private readonly PreviewCameraNavigation cameraNavigation = new PreviewCameraNavigation();

        private bool IsCameraFlying
        {
            get { return cameraNavigation.IsFlying; }
        }

        private bool TryBeginCameraGesture(PointerDownEvent pointerEvent)
        {
            return cameraNavigation.TryBeginExclusiveGesture(pointerEvent);
        }

        private void ContinueCameraGesture(Vector2 pixelDelta)
        {
            cameraNavigation.ContinueGesture(pixelDelta, previewImage.contentRect.height);
        }

        private void EndCameraGesture()
        {
            cameraNavigation.EndGesture();
        }

        // Only the flying branch is shared: F stays the window's own case below in OnViewportKeyDown
        // (FrameViewportSelection also Repaints, which the shared class's own F handling does not),
        // matching the swallow-then-F order this window has always used.
        private bool TryHandleFlyKeyDown(KeyDownEvent keyEvent)
        {
            if (cameraNavigation.IsFlying)
            {
                return cameraNavigation.TryHandleKeyDown(keyEvent);
            }
            return false;
        }

        private void OnViewportKeyUp(KeyUpEvent keyEvent)
        {
            cameraNavigation.HandleKeyUp(keyEvent);
        }

        private void StepCameraFly(float deltaSeconds)
        {
            cameraNavigation.StepFly(deltaSeconds);
        }

        /// <summary>
        /// Puts the camera back where the window opened it: head-on, centred on the rig currently in
        /// the viewport and backed off to fit it. The Reset Camera button, and a double-click.
        /// </summary>
        private void ResetViewportCamera()
        {
            cameraNavigation.ResetView();
            Repaint();
        }

        /// <summary>Frames the selection, or the rig when nothing is selected — the F key.</summary>
        private void FrameViewportSelection()
        {
            if (previewController == null)
            {
                return;
            }

            previewController.FrameSelection();
            Repaint();
        }
    }
}
