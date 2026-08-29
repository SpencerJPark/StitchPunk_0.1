// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// The viewport's camera gestures: the Scene view's orbit, pan, look, dolly and WASD fly, plus
    /// the Reset Camera button and the F key.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Its own gesture, resolved at the press and held until the release.</strong> Which
    /// camera move a drag is depends on the button and the modifiers <em>at the moment the button
    /// went down</em>, never on what they are during the drag: releasing Alt halfway through an
    /// Alt + right-drag would otherwise turn a dolly into a look mid-gesture, and the camera would
    /// lurch. <see cref="activeCameraGesture"/> is that decision, and it is also what makes the
    /// gesture exclusive — while it is set, the viewport does not pick, does not start a gizmo drag
    /// and does not read W/E/R as gizmo modes.
    /// </para>
    /// <para>
    /// <strong>Left-drag still orbits, and is not resolved here.</strong> The Scene view reserves
    /// left for selection and orbits on Alt + left; this viewport has orbited on <em>any</em>
    /// left-drag since it existed, with a click inside a few pixels of the press selecting instead.
    /// So Alt + left orbits as the Scene view does, but through that older path in
    /// <c>OnPreviewPointerMove</c> rather than as a gesture of this one — see
    /// <see cref="ResolveCameraGesture"/> for why claiming it here would cost the Alt-click pick
    /// cycling.
    /// </para>
    /// <para>
    /// <strong>Flying is stepped on the editor tick, not on key events.</strong> Key repeat is a
    /// keyboard setting — it starts late and fires at whatever rate the OS is set to — so moving on
    /// each <c>KeyDownEvent</c> would make the fly speed a property of the user's control panel. The
    /// keys only record what is held; <see cref="StepCameraFly"/> integrates it against real elapsed
    /// time from the same 30 Hz tick that renders the preview.
    /// </para>
    /// </remarks>
    public sealed partial class ClipEditorWindow
    {
        /// <summary>
        /// Which camera move a drag in the viewport is, for as long as it lasts.
        /// </summary>
        /// <remarks>
        /// There is no Orbit member, and that is the point: orbiting is the left button's older,
        /// non-exclusive path in <c>OnPreviewPointerMove</c> — see <see cref="ResolveCameraGesture"/>.
        /// </remarks>
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

        /// <summary>
        /// Whether this press is a camera gesture rather than a pick or a gizmo drag, and if so,
        /// starts it.
        /// </summary>
        /// <remarks>
        /// Called after the pointer is captured and the image is focused: the capture is what makes
        /// the moves and the release arrive here even when the pointer leaves the viewport, and the
        /// focus is what makes the fly keys arrive at all.
        /// </remarks>
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

        /// <summary>
        /// The Scene view's own mapping from button and modifier to camera move. Middle is tested
        /// first so Alt + middle pans rather than falling into the Alt branch.
        /// </summary>
        /// <remarks>
        /// <strong>The left button is deliberately absent, Alt or no Alt.</strong> Alt + left orbits
        /// here too — but through the older path in <c>OnPreviewPointerMove</c>, which orbits on any
        /// plain left-drag. Claiming it as an exclusive gesture instead would cost the pick cycling:
        /// Alt + <em>click</em> is what steps through overlapping hits, and an exclusive gesture
        /// never reaches the pick. The old path already tells a drag from a click by how far the
        /// pointer travelled, so it gets both right where this could only get one.
        /// </remarks>
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

        /// <summary>
        /// Ends whatever camera gesture was in flight, and stops the fly.
        /// </summary>
        /// <remarks>
        /// Called from the pointer release <em>and</em> from <c>PointerCaptureOutEvent</c>. The
        /// second is not belt-and-braces: a capture lost to a domain reload, a modal dialog or
        /// another element taking it never delivers a release, and a viewport left in
        /// <see cref="CameraGesture.Look"/> goes on swallowing every keystroke as a fly key — W and
        /// E would stop switching gizmo modes with nothing on screen to say why.
        /// </remarks>
        private void EndCameraGesture()
        {
            activeCameraGesture = CameraGesture.None;
            heldFlyKeys.Clear();
            isFlyingFast = false;
        }

        /// <summary>
        /// Records a key while flying. Returns whether the key belonged to the camera and must not
        /// reach the gizmo shortcuts.
        /// </summary>
        /// <remarks>
        /// Every key is swallowed while flying, not only the six that move: the Scene view does the
        /// same, and the alternative is W meaning "forward" while R two keys later means "scale",
        /// which is the mode confusion this exists to avoid.
        /// </remarks>
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

        /// <summary>
        /// Moves the camera for whatever fly keys are held, from the window's editor tick.
        /// </summary>
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
        /// <remarks>
        /// Ends any gesture in flight first. Resetting from the button while the right mouse button
        /// is still down would otherwise leave the fly keys armed against a camera that had just
        /// jumped somewhere else.
        /// </remarks>
        private void ResetViewportCamera()
        {
            if (previewController == null)
            {
                return;
            }

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
