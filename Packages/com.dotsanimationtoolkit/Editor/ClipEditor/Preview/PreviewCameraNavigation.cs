using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Rig-agnostic gesture state machine for orbit/pan/look/dolly/fly camera navigation, shared by every preview viewport.</summary>
    public sealed class PreviewCameraNavigation
    {
        // No Orbit-from-drag resolution here: orbit is either driven by a host's own pointer-move
        // handler (window) or by BeginOrbit() for a host with no picking of its own (AttachTo).
        public enum Gesture
        {
            None,
            Orbit,
            Pan,
            Look,
            Dolly
        }

        public const float WheelZoomPerNotch = 0.3f;

        public IPreviewCameraRig Rig { get; set; }
        public Gesture ActiveGesture { get; private set; }
        public bool IsFlying { get { return ActiveGesture == Gesture.Look; } }
        public event Action CameraChanged;

        // Resolved once at the press and held for the drag, never re-read from live modifiers —
        // releasing Alt mid-drag must not turn a dolly into a look.
        private readonly HashSet<KeyCode> heldFlyKeys = new HashSet<KeyCode>();
        private bool isFlyingFast;

        // Alt+left is the pick-cycle modifier in every host, never a camera gesture, so button 0
        // always resolves to None here regardless of altKey.
        public static Gesture ResolveExclusiveGesture(int button, bool altKey)
        {
            const int RightButton = 1;
            const int MiddleButton = 2;
            if (button == MiddleButton) { return Gesture.Pan; }
            if (button == RightButton) { return altKey ? Gesture.Dolly : Gesture.Look; }
            return Gesture.None;
        }

        public bool TryBeginExclusiveGesture(int button, bool altKey, bool shiftKey)
        {
            Gesture requested = ResolveExclusiveGesture(button, altKey);
            if (requested == Gesture.None || Rig == null)
            {
                return false;
            }
            ActiveGesture = requested;
            heldFlyKeys.Clear();
            isFlyingFast = shiftKey;
            return true;
        }

        public bool TryBeginExclusiveGesture(PointerDownEvent pointerEvent)
        {
            return TryBeginExclusiveGesture(pointerEvent.button, pointerEvent.altKey, pointerEvent.shiftKey);
        }

        public void BeginOrbit()
        {
            ActiveGesture = Gesture.Orbit;
            heldFlyKeys.Clear();
            isFlyingFast = false;
        }

        public void ContinueGesture(Vector2 pixelDelta, float viewportHeightPixels)
        {
            switch (ActiveGesture)
            {
                case Gesture.Orbit:
                    Rig?.Orbit(pixelDelta);
                    break;
                case Gesture.Pan:
                    Rig?.Pan(pixelDelta, viewportHeightPixels);
                    break;
                case Gesture.Look:
                    Rig?.LookAround(pixelDelta);
                    break;
                case Gesture.Dolly:
                    Rig?.Dolly(pixelDelta);
                    break;
                default:
                    return;
            }
            CameraChanged?.Invoke();
        }

        public void EndGesture()
        {
            ActiveGesture = Gesture.None;
            heldFlyKeys.Clear();
            isFlyingFast = false;
        }

        public bool TryHandleKeyDown(KeyDownEvent keyEvent)
        {
            if (IsFlying)
            {
                isFlyingFast = keyEvent.shiftKey;
                if (IsFlyKey(keyEvent.keyCode)) { heldFlyKeys.Add(keyEvent.keyCode); }
                return true;
            }
            if (keyEvent.keyCode == KeyCode.F)
            {
                Rig?.FrameSelection();
                CameraChanged?.Invoke();
                return true;
            }
            return false;
        }

        public void HandleKeyUp(KeyUpEvent keyEvent)
        {
            isFlyingFast = keyEvent.shiftKey;
            heldFlyKeys.Remove(keyEvent.keyCode);
        }

        public void HandleWheel(WheelEvent wheelEvent)
        {
            Rig?.Zoom(wheelEvent.delta.y * WheelZoomPerNotch);
            CameraChanged?.Invoke();
            wheelEvent.StopPropagation();
        }

        public void StepFly(float deltaSeconds)
        {
            if (!IsFlying || heldFlyKeys.Count == 0 || Rig == null) { return; }
            Vector3 localDirection = Vector3.zero;
            foreach (KeyCode heldKey in heldFlyKeys)
            {
                switch (heldKey)
                {
                    case KeyCode.W: localDirection += Vector3.forward; break;
                    case KeyCode.S: localDirection += Vector3.back; break;
                    case KeyCode.A: localDirection += Vector3.left; break;
                    case KeyCode.D: localDirection += Vector3.right; break;
                    case KeyCode.E: localDirection += Vector3.up; break;
                    case KeyCode.Q: localDirection += Vector3.down; break;
                }
            }
            Rig.Fly(localDirection, deltaSeconds, isFlyingFast);
            CameraChanged?.Invoke();
        }

        public void ResetView()
        {
            EndGesture();
            Rig?.ResetView();
            CameraChanged?.Invoke();
        }

        private static bool IsFlyKey(KeyCode keyCode)
        {
            return keyCode == KeyCode.W || keyCode == KeyCode.A || keyCode == KeyCode.S
                || keyCode == KeyCode.D || keyCode == KeyCode.Q || keyCode == KeyCode.E;
        }

        // Full no-pick binding for a host with no picking of its own (Actor Editor, VAT preview) —
        // the Clip Editor window keeps driving these primitives from its own pointer handlers instead.
        public void AttachTo(Image viewport)
        {
            viewport.focusable = true;
            viewport.RegisterCallback<PointerDownEvent>(pointerEvent =>
            {
                if (pointerEvent.clickCount >= 2 && pointerEvent.button == 0) { ResetView(); return; }
                viewport.CapturePointer(pointerEvent.pointerId);
                viewport.Focus();
                if (TryBeginExclusiveGesture(pointerEvent)) { return; }
                if (pointerEvent.button == 0) { BeginOrbit(); }
            });
            viewport.RegisterCallback<PointerMoveEvent>(moveEvent =>
            {
                if (!viewport.HasPointerCapture(moveEvent.pointerId)) { return; }
                ContinueGesture(moveEvent.deltaPosition, viewport.contentRect.height);
            });
            viewport.RegisterCallback<PointerUpEvent>(upEvent =>
            {
                viewport.ReleasePointer(upEvent.pointerId);
                EndGesture();
            });
            // A lost capture never delivers a release — a domain reload, a modal dialog, another
            // element taking it — which would otherwise leave a gesture (fly mode) running forever.
            viewport.RegisterCallback<PointerCaptureOutEvent>(captureEvent => EndGesture());
            viewport.RegisterCallback<WheelEvent>(HandleWheel);
            viewport.RegisterCallback<KeyDownEvent>(keyEvent =>
            {
                if (TryHandleKeyDown(keyEvent)) { keyEvent.StopPropagation(); }
            });
            viewport.RegisterCallback<KeyUpEvent>(HandleKeyUp);
        }
    }
}
