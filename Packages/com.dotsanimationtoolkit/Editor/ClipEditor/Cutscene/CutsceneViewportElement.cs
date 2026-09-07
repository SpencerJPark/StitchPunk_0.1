// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// The Cutscene Editor's in-tab scene viewport: a hidden utility camera rendering the open
    /// scene into a texture on demand, never a preview world of its own.
    /// </summary>
    internal sealed class CutsceneViewportElement : VisualElement
    {
        public const string UssClassName = "cutscene-editor__viewport";

        private const string UtilityCameraName = "CutsceneViewportCamera (hidden)";
        private const float OrbitDegreesPerPixel = 0.25f;
        private const float PitchLimitDegrees = 89f;
        private const float ZoomStepFactor = 1.1f;
        private const float MinimumOrbitDistance = 0.05f;

        /// <summary>Metres per key press for the fly keys, before the Shift boost.</summary>
        private const float FlyMetresPerPress = 0.35f;
        private const float FlyBoostFactor = 4f;

        /// <summary>Pointer travel, in pixels, before a press stops being a click and becomes navigation.</summary>
        private const float ClickTravelTolerancePixels = 3f;

        private readonly Image sceneImage;

        private Camera utilityCamera;
        private RenderTexture renderTarget;

        private Vector3 orbitFocus = Vector3.zero;
        private float orbitYawDegrees = 30f;
        private float orbitPitchDegrees = 20f;
        private float orbitDistance = 10f;
        private float freeFieldOfView = 60f;

        private int capturedPointerId = -1;
        private int activeDragButton = -1;
        private Vector2 lastPointerPosition;
        private Vector2 pressPointerPosition;
        private Vector2 pressLocalPosition;
        private bool pressTravelledPastClick;
        private bool pressWasAdditive;
        private bool pressClaimedByOverlay;

        /// <summary>The last pose actually rendered — what a Frame, a broken shot, or a pick ray resumes from.</summary>
        private Vector3 renderedCameraPosition;
        private Quaternion renderedCameraRotation = Quaternion.identity;
        private float renderedFieldOfView = 60f;

        /// <summary>Raised when a drag starts while a shot pose is on screen; the panel switches the mode toggle to Free.</summary>
        public event Action NavigationBrokeShot;

        /// <summary>Raised after any user navigation, so the panel re-renders without waiting for a playhead move.</summary>
        public event Action NavigationChangedCamera;

        /// <summary>Raised on a press that never travelled far enough to navigate, with the point in this element's own space and whether a modifier was held.</summary>
        public event Action<Vector2, bool> Clicked;

        // A gizmo has to answer before navigation does, and it has to answer on the press itself,
        // so this is a claim rather than an event: returning true takes the whole gesture.
        /// <summary>Offered every left press before navigation; return true to take the gesture.</summary>
        public Func<Vector2, bool> tryClaimPress;

        /// <summary>Pointer moves during a claimed gesture, in this element's own space.</summary>
        public event Action<Vector2> ClaimedPressDragged;

        /// <summary>The end of a claimed gesture.</summary>
        public event Action ClaimedPressReleased;

        // Anything drawn for this viewport alone is queued here, immediately before the render it
        // belongs to: a Graphics.DrawMesh submission lasts one frame and names one camera.
        /// <summary>Raised inside a render, once the camera exists, so per-camera draws can be queued.</summary>
        public event Action<Camera> AboutToRender;

        /// <summary>Where the camera stood for the last render — what a gizmo scales its handles against.</summary>
        public Vector3 RenderedCameraPosition
        {
            get { return renderedCameraPosition; }
        }

        /// <summary>True while the panel is feeding shot poses in; gates the gesture-breaks-shot event.</summary>
        public bool IsShowingShotPose { get; set; }

        public CutsceneViewportElement()
        {
            AddToClassList(UssClassName);
            focusable = true;

            sceneImage = new Image { scaleMode = ScaleMode.StretchToFill };
            sceneImage.pickingMode = PickingMode.Ignore;
            sceneImage.StretchToParentSize();
            Add(sceneImage);

            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<PointerCaptureOutEvent>(_ => EndDrag());
            RegisterCallback<WheelEvent>(OnWheel);
            RegisterCallback<KeyDownEvent>(OnFlyKeyDown);
            RegisterCallback<DetachFromPanelEvent>(_ => ReleaseViewportResources());
            RegisterCallback<AttachToPanelEvent>(_ => DestroyLeakedCameras());
        }

        // -----------------------------------------------------------------------------------
        // Rendering.
        // -----------------------------------------------------------------------------------

        /// <summary>Renders the open scene from the free orbit rig.</summary>
        public void RenderFree()
        {
            Quaternion rotation = Quaternion.Euler(orbitPitchDegrees, orbitYawDegrees, 0f);
            Vector3 position = orbitFocus - rotation * Vector3.forward * orbitDistance;
            Render(position, rotation, freeFieldOfView);
        }

        /// <summary>Renders the open scene from a sampled camera-lane pose (Shot mode).</summary>
        public void RenderShot(Vector3 position, Quaternion rotation, float fieldOfView)
        {
            Render(position, rotation, fieldOfView);
        }

        private void Render(Vector3 position, Quaternion rotation, float fieldOfView)
        {
            if (panel == null || !EnsureRenderResources())
            {
                return;
            }

            renderedCameraPosition = position;
            renderedCameraRotation = rotation;
            renderedFieldOfView = Mathf.Clamp(fieldOfView, 1f, 179f);

            utilityCamera.transform.SetPositionAndRotation(position, rotation);
            utilityCamera.fieldOfView = Mathf.Clamp(fieldOfView, 1f, 179f);
            AboutToRender?.Invoke(utilityCamera);

            UniversalRenderPipeline.SingleCameraRequest renderRequest =
                new UniversalRenderPipeline.SingleCameraRequest { destination = renderTarget };
            if (RenderPipeline.SupportsRenderRequest(utilityCamera, renderRequest))
            {
                RenderPipeline.SubmitRenderRequest(utilityCamera, renderRequest);
            }
            else
            {
                // Outside URP (or a pipeline refusing requests) the legacy path still works and is
                // better than a black pane.
                utilityCamera.targetTexture = renderTarget;
                utilityCamera.Render();
                utilityCamera.targetTexture = null;
            }
            sceneImage.MarkDirtyRepaint();
        }

        // Built from the pose last rendered rather than from the camera component: the utility
        // camera is disabled and its projection means nothing between renders.
        /// <summary>A world ray through a point in this element's own space, or false before anything has been rendered.</summary>
        public bool TryBuildPickRay(Vector2 localPosition, out Ray pickRay)
        {
            pickRay = default(Ray);
            Rect rect = contentRect;
            if (rect.width < 1f || rect.height < 1f)
            {
                return false;
            }

            // UI Toolkit measures y down from the top; a viewport point measures it up from the bottom.
            Vector2 viewportPoint = new Vector2(
                localPosition.x / rect.width, 1f - localPosition.y / rect.height);

            // The same construction PreviewScenePicker.BuildRay uses, done here from the rendered
            // pose rather than from a Transform: the utility camera is disabled and its own
            // transform and projection mean nothing between renders.
            float tangentOfHalfFieldOfView = Mathf.Tan(renderedFieldOfView * 0.5f * Mathf.Deg2Rad);
            float aspect = rect.width / rect.height;
            Vector3 directionInCameraSpace = new Vector3(
                (viewportPoint.x * 2f - 1f) * tangentOfHalfFieldOfView * aspect,
                (viewportPoint.y * 2f - 1f) * tangentOfHalfFieldOfView,
                1f);
            pickRay = new Ray(renderedCameraPosition, renderedCameraRotation * directionInCameraSpace);
            return true;
        }

        /// <summary>Points the free rig at <paramref name="bounds"/> and re-renders (the F / Frame action).</summary>
        public void FrameBounds(Bounds bounds)
        {
            orbitFocus = bounds.center;
            float radius = Mathf.Max(0.5f, bounds.extents.magnitude);
            orbitDistance = radius / Mathf.Tan(Mathf.Max(1f, freeFieldOfView) * 0.5f * Mathf.Deg2Rad) * 1.2f;
            NavigationChangedCamera?.Invoke();
        }

        /// <summary>Adopts the last rendered pose as the free rig, so leaving Shot mode does not jump the view.</summary>
        public void AdoptRenderedPoseAsFreeRig()
        {
            Vector3 eulerAngles = renderedCameraRotation.eulerAngles;
            orbitPitchDegrees = NormalizePitch(eulerAngles.x);
            orbitYawDegrees = eulerAngles.y;
            orbitFocus = renderedCameraPosition + renderedCameraRotation * Vector3.forward * orbitDistance;
        }

        private static float NormalizePitch(float degrees)
        {
            if (degrees > 180f)
            {
                degrees -= 360f;
            }
            return Mathf.Clamp(degrees, -PitchLimitDegrees, PitchLimitDegrees);
        }

        private bool EnsureRenderResources()
        {
            Rect rect = contentRect;
            int pixelWidth = Mathf.RoundToInt(rect.width * EditorGUIUtility.pixelsPerPoint);
            int pixelHeight = Mathf.RoundToInt(rect.height * EditorGUIUtility.pixelsPerPoint);
            if (pixelWidth < 8 || pixelHeight < 8)
            {
                return false;
            }

            if (renderTarget == null || renderTarget.width != pixelWidth || renderTarget.height != pixelHeight)
            {
                if (renderTarget != null)
                {
                    renderTarget.Release();
                    UnityEngine.Object.DestroyImmediate(renderTarget);
                }
                renderTarget = new RenderTexture(pixelWidth, pixelHeight, 24, RenderTextureFormat.ARGB32)
                {
                    name = "CutsceneViewportRT",
                    hideFlags = HideFlags.HideAndDontSave
                };
                renderTarget.Create();
                sceneImage.image = renderTarget;
            }

            if (utilityCamera == null)
            {
                GameObject cameraObject = new GameObject(UtilityCameraName)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                utilityCamera = cameraObject.AddComponent<Camera>();
                utilityCamera.enabled = false;
                utilityCamera.clearFlags = CameraClearFlags.Skybox;
                utilityCamera.cullingMask = ~0;
            }
            return true;
        }

        /// <summary>
        /// HideAndDontSave objects survive domain reloads while this element does not, so a reload
        /// would otherwise leak one hidden camera per session. Swept by name on attach.
        /// </summary>
        private static void DestroyLeakedCameras()
        {
            // Not GameObject.Find: HideAndDontSave objects are hidden from it. FindObjectsOfTypeAll
            // is the only net that catches a camera a domain reload orphaned.
            Camera[] allCameras = Resources.FindObjectsOfTypeAll<Camera>();
            for (int cameraIndex = 0; cameraIndex < allCameras.Length; cameraIndex++)
            {
                Camera candidate = allCameras[cameraIndex];
                if (candidate != null && candidate.gameObject.name == UtilityCameraName)
                {
                    UnityEngine.Object.DestroyImmediate(candidate.gameObject);
                }
            }
        }

        private void ReleaseViewportResources()
        {
            if (renderTarget != null)
            {
                renderTarget.Release();
                UnityEngine.Object.DestroyImmediate(renderTarget);
                renderTarget = null;
            }
            if (utilityCamera != null)
            {
                UnityEngine.Object.DestroyImmediate(utilityCamera.gameObject);
                utilityCamera = null;
            }
        }

        // -----------------------------------------------------------------------------------
        // Navigation: left-drag orbits, middle-drag pans, wheel dollies — the same defaults the
        // Clip Editor viewport opens with.
        // -----------------------------------------------------------------------------------

        private void OnPointerDown(PointerDownEvent pointerEvent)
        {
            // Left orbits (or picks), middle pans, right looks — the Scene view's own division.
            if (pointerEvent.button != 0 && pointerEvent.button != 1 && pointerEvent.button != 2)
            {
                return;
            }

            pressClaimedByOverlay = pointerEvent.button == 0
                && tryClaimPress != null && tryClaimPress(pointerEvent.localPosition);

            // The shot is not broken here any more: a click that selects something must leave the
            // framed view alone, so navigation only claims the gesture once it actually travels.
            this.CapturePointer(pointerEvent.pointerId);
            capturedPointerId = pointerEvent.pointerId;
            activeDragButton = pointerEvent.button;
            lastPointerPosition = pointerEvent.position;
            pressPointerPosition = pointerEvent.position;
            pressLocalPosition = pointerEvent.localPosition;
            pressTravelledPastClick = false;
            pressWasAdditive =
                pointerEvent.ctrlKey || pointerEvent.commandKey || pointerEvent.shiftKey;
            Focus();
            pointerEvent.StopPropagation();
        }

        private void OnPointerMove(PointerMoveEvent moveEvent)
        {
            if (capturedPointerId != moveEvent.pointerId || !this.HasPointerCapture(moveEvent.pointerId))
            {
                return;
            }
            if (pressClaimedByOverlay)
            {
                ClaimedPressDragged?.Invoke(moveEvent.localPosition);
                return;
            }

            if (!pressTravelledPastClick)
            {
                if (((Vector2)moveEvent.position - pressPointerPosition).sqrMagnitude
                    < ClickTravelTolerancePixels * ClickTravelTolerancePixels)
                {
                    return;
                }
                pressTravelledPastClick = true;
                if (IsShowingShotPose)
                {
                    AdoptRenderedPoseAsFreeRig();
                    NavigationBrokeShot?.Invoke();
                }
            }

            Vector2 delta = (Vector2)moveEvent.position - lastPointerPosition;
            lastPointerPosition = moveEvent.position;

            if (activeDragButton == 0)
            {
                orbitYawDegrees += delta.x * OrbitDegreesPerPixel;
                orbitPitchDegrees = Mathf.Clamp(
                    orbitPitchDegrees + delta.y * OrbitDegreesPerPixel, -PitchLimitDegrees, PitchLimitDegrees);
            }
            else if (activeDragButton == 1)
            {
                // Looking turns the camera in place, so the focus is carried around to keep the eye
                // where it is; orbiting turns around the focus and leaves it alone.
                Vector3 eyePosition = CurrentEyePosition();
                orbitYawDegrees += delta.x * OrbitDegreesPerPixel;
                orbitPitchDegrees = Mathf.Clamp(
                    orbitPitchDegrees - delta.y * OrbitDegreesPerPixel, -PitchLimitDegrees, PitchLimitDegrees);
                orbitFocus = eyePosition
                    + Quaternion.Euler(orbitPitchDegrees, orbitYawDegrees, 0f) * Vector3.forward * orbitDistance;
            }
            else
            {
                // Pan rate ties world units to pixels at the focus plane, so the scene tracks the
                // cursor at any distance — the Clip Editor's own Pan lesson.
                float viewHeightPixels = Mathf.Max(1f, contentRect.height);
                float worldPerPixel = 2f * orbitDistance
                    * Mathf.Tan(freeFieldOfView * 0.5f * Mathf.Deg2Rad) / viewHeightPixels;
                Quaternion rotation = Quaternion.Euler(orbitPitchDegrees, orbitYawDegrees, 0f);
                orbitFocus += rotation * new Vector3(-delta.x * worldPerPixel, delta.y * worldPerPixel, 0f);
            }
            NavigationChangedCamera?.Invoke();
        }

        private void OnPointerUp(PointerUpEvent upEvent)
        {
            if (capturedPointerId == upEvent.pointerId)
            {
                this.ReleasePointer(upEvent.pointerId);
            }
            if (pressClaimedByOverlay)
            {
                pressClaimedByOverlay = false;
                EndDrag();
                ClaimedPressReleased?.Invoke();
                return;
            }

            bool wasClick = capturedPointerId == upEvent.pointerId
                && activeDragButton == 0 && !pressTravelledPastClick;
            bool wasAdditive = pressWasAdditive;
            Vector2 clickedLocalPosition = pressLocalPosition;
            EndDrag();
            if (wasClick)
            {
                Clicked?.Invoke(clickedLocalPosition, wasAdditive);
            }
        }

        private void EndDrag()
        {
            capturedPointerId = -1;
            activeDragButton = -1;
            pressTravelledPastClick = false;
            pressClaimedByOverlay = false;
        }

        /// <summary>Where the eye sits for the current orbit rig.</summary>
        private Vector3 CurrentEyePosition()
        {
            Quaternion rotation = Quaternion.Euler(orbitPitchDegrees, orbitYawDegrees, 0f);
            return orbitFocus - rotation * Vector3.forward * orbitDistance;
        }

        // WASD/QE fly the focus, which the eye follows at a fixed distance. Repeat comes from the
        // OS key repeat rather than a tick, so holding a key keeps moving without a per-frame hook.
        private void OnFlyKeyDown(KeyDownEvent keyEvent)
        {
            // Only while the right button is held, exactly as the Scene view does it. Otherwise
            // these keys would swallow W/E/R before the gizmo modes ever saw them.
            if (activeDragButton != 1)
            {
                return;
            }

            Vector3 flyDirection = Vector3.zero;
            switch (keyEvent.keyCode)
            {
                case KeyCode.W: flyDirection = Vector3.forward; break;
                case KeyCode.S: flyDirection = Vector3.back; break;
                case KeyCode.A: flyDirection = Vector3.left; break;
                case KeyCode.D: flyDirection = Vector3.right; break;
                case KeyCode.E: flyDirection = Vector3.up; break;
                case KeyCode.Q: flyDirection = Vector3.down; break;
                default: return;
            }
            if (keyEvent.ctrlKey || keyEvent.commandKey)
            {
                return;
            }

            if (IsShowingShotPose)
            {
                AdoptRenderedPoseAsFreeRig();
                NavigationBrokeShot?.Invoke();
            }
            float stepMetres = FlyMetresPerPress * (keyEvent.shiftKey ? FlyBoostFactor : 1f);
            orbitFocus += Quaternion.Euler(orbitPitchDegrees, orbitYawDegrees, 0f) * flyDirection * stepMetres;
            NavigationChangedCamera?.Invoke();
            keyEvent.StopPropagation();
        }

        private void OnWheel(WheelEvent wheelEvent)
        {
            if (IsShowingShotPose)
            {
                AdoptRenderedPoseAsFreeRig();
                NavigationBrokeShot?.Invoke();
            }
            float factor = wheelEvent.delta.y > 0f ? ZoomStepFactor : 1f / ZoomStepFactor;
            orbitDistance = Mathf.Max(MinimumOrbitDistance, orbitDistance * factor);
            NavigationChangedCamera?.Invoke();
            wheelEvent.StopPropagation();
        }
    }
}
