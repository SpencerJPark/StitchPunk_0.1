// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    public sealed partial class ClipEditorWindow
    {
        // Which camera move a drag in the viewport is. No Orbit member: orbiting is the left
        // button's older, non-exclusive path in OnPreviewPointerMove — see ResolveCameraGesture.
        private enum CameraGesture
        {
            None,

            /// <summary>Middle drag: slides the camera and the focus together.</summary>
            Pan,

            /// <summary>Right drag: turns the camera in place, and arms WASD / QE flying.</summary>
            Look,

            /// <summary>Alt + right drag: moves the camera along its own view direction.</summary>
            Dolly
        }

        // Resolved once at the press and held for the drag, never re-read from live modifiers —
        // releasing Alt mid-drag must not turn a dolly into a look.
        private CameraGesture activeCameraGesture = CameraGesture.None;

        /// <summary>
        /// Which fly keys are down right now. A set rather than a direction, because two keys held
        /// at once are a diagonal and releasing one of them has to leave the other still flying.
        /// </summary>
        private readonly HashSet<KeyCode> heldFlyKeys = new HashSet<KeyCode>();

        private bool isFlyingFast;

        /// <summary>Whether the viewport is in fly mode, where W/A/S/D move instead of switching gizmos.</summary>
        private bool IsCameraFlying
        {
            get { return activeCameraGesture == CameraGesture.Look; }
        }

        // Whether this press is a camera gesture rather than a pick or gizmo drag, and if so starts
        // it. Called after the pointer is captured, so moves and release still arrive off-viewport.
        private bool TryBeginCameraGesture(PointerDownEvent pointerEvent)
        {
            CameraGesture requested =
                ResolveCameraGesture(pointerEvent.button, pointerEvent.altKey);
            if (requested == CameraGesture.None || previewController == null)
            {
                return false;
            }

            activeCameraGesture = requested;
            heldFlyKeys.Clear();
            isFlyingFast = pointerEvent.shiftKey;
            return true;
        }

        // The Scene view's own mapping from button and modifier to camera move. The left button is
        // deliberately absent: it orbits through the older OnPreviewPointerMove path instead, which
        // can also tell an Alt+click pick from an Alt+drag orbit — an exclusive gesture here could not.
        private static CameraGesture ResolveCameraGesture(int button, bool altKey)
        {
            const int RightButton = 1;
            const int MiddleButton = 2;

            if (button == MiddleButton)
            {
                return CameraGesture.Pan;
            }
            if (button == RightButton)
            {
                return altKey ? CameraGesture.Dolly : CameraGesture.Look;
            }
            return CameraGesture.None;
        }

        private void ContinueCameraGesture(Vector2 pixelDelta)
        {
            if (previewController == null)
            {
                return;
            }

            switch (activeCameraGesture)
            {
                case CameraGesture.Pan:
                    // The height of the rendered image, which is what a pan has to be measured
                    // against for the scene to stay under the cursor. previewImage is never null
                    // here: a gesture can only be in flight if a press landed on it.
                    previewController.Pan(pixelDelta, previewImage.contentRect.height);
                    break;
                case CameraGesture.Look:
                    previewController.LookAround(pixelDelta);
                    break;
                case CameraGesture.Dolly:
                    previewController.Dolly(pixelDelta);
                    break;
            }
        }

        // Ends whatever camera gesture was in flight, and stops the fly. Called from the pointer
        // release and from PointerCaptureOutEvent — a lost capture never delivers a release, and a
        // viewport stuck in Look mode would go on swallowing every keystroke as a fly key.
        private void EndCameraGesture()
        {
            activeCameraGesture = CameraGesture.None;
            heldFlyKeys.Clear();
            isFlyingFast = false;
        }

        // Records a key while flying, and swallows every key while flying (not only the six that
        // move) — otherwise W could mean "forward" while R two keys later means "scale".
        private bool TryHandleFlyKeyDown(KeyDownEvent keyEvent)
        {
            if (!IsCameraFlying)
            {
                return false;
            }

            isFlyingFast = keyEvent.shiftKey;
            if (IsFlyKey(keyEvent.keyCode))
            {
                heldFlyKeys.Add(keyEvent.keyCode);
            }
            return true;
        }

        private void OnViewportKeyUp(KeyUpEvent keyEvent)
        {
            // Read unconditionally rather than only while flying: Shift's own release is a key up
            // like any other, and it is the only event that says the accelerator is off.
            isFlyingFast = keyEvent.shiftKey;
            heldFlyKeys.Remove(keyEvent.keyCode);
        }

        private static bool IsFlyKey(KeyCode keyCode)
        {
            return keyCode == KeyCode.W
                || keyCode == KeyCode.A
                || keyCode == KeyCode.S
                || keyCode == KeyCode.D
                || keyCode == KeyCode.Q
                || keyCode == KeyCode.E;
        }

        // Moves the camera for whatever fly keys are held, from the window's editor tick rather
        // than KeyDownEvent repeat — which fires at whatever rate the OS is set to.
        private void StepCameraFly(float deltaSeconds)
        {
            if (!IsCameraFlying || heldFlyKeys.Count == 0 || previewController == null)
            {
                return;
            }

            Vector3 localDirection = Vector3.zero;
            foreach (KeyCode heldKey in heldFlyKeys)
            {
                switch (heldKey)
                {
                    case KeyCode.W:
                        localDirection += Vector3.forward;
                        break;
                    case KeyCode.S:
                        localDirection += Vector3.back;
                        break;
                    case KeyCode.A:
                        localDirection += Vector3.left;
                        break;
                    case KeyCode.D:
                        localDirection += Vector3.right;
                        break;
                    case KeyCode.E:
                        localDirection += Vector3.up;
                        break;
                    case KeyCode.Q:
                        localDirection += Vector3.down;
                        break;
                }
            }

            previewController.Fly(localDirection, deltaSeconds, isFlyingFast);
        }

        /// <summary>
        /// Puts the camera back where the window opened it: head-on, centred on the rig currently in
        /// the viewport and backed off to fit it. The Reset Camera button, and a double-click.
        /// </summary>
        private void ResetViewportCamera()
        {
            if (previewController == null)
            {
                return;
            }

            // Otherwise a reset while right mouse is still held leaves the fly keys armed against a
            // camera that just jumped somewhere else.
            EndCameraGesture();
            previewController.ResetView();
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
