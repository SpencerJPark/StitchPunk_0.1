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
    /// The Cutscene Editor's in-tab scene viewport (amendment A59): a hidden utility camera
    /// rendering the <em>open</em> scene into a texture on demand. Never a preview world — the
    /// scene is already open and the preview already poses its real objects, so this element only
    /// has to look at them (A59 §1).
    /// </summary>
    /// <remarks>
    /// Two camera modes (A59 §3.3): <em>Free</em> is an orbit rig (focus + yaw/pitch + distance,
    /// the Clip Editor's own camera model); <em>Shot</em> is not stored here at all — the panel
    /// samples the camera lane and passes the pose into <see cref="RenderShot"/>. Starting any
    /// navigation gesture while a shot is displayed raises <see cref="NavigationBrokeShot"/> so
    /// the panel can drop back to Free rather than fight the drag.
    /// </remarks>
    internal sealed class CutsceneViewportElement : VisualElement
    {
        public const string UssClassName = "cutscene-editor__viewport";

        private const string UtilityCameraName = "CutsceneViewportCamera (hidden)";
        private const float OrbitDegreesPerPixel = 0.25f;
        private const float PitchLimitDegrees = 89f;
        private const float ZoomStepFactor = 1.1f;
        private const float MinimumOrbitDistance = 0.05f;

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

        /// <summary>The last pose actually rendered — what a Frame or a broken shot resumes from.</summary>
        private Vector3 renderedCameraPosition;
        private Quaternion renderedCameraRotation = Quaternion.identity;

        /// <summary>Raised when a drag starts while a shot pose is on screen; the panel switches the mode toggle to Free.</summary>
        public event Action NavigationBrokeShot;

        /// <summary>Raised after any user navigation, so the panel re-renders without waiting for a playhead move.</summary>
        public event Action NavigationChangedCamera;

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

            utilityCamera.transform.SetPositionAndRotation(position, rotation);
            utilityCamera.fieldOfView = Mathf.Clamp(fieldOfView, 1f, 179f);

            UniversalRenderPipeline.SingleCameraRequest renderRequest =
                new UniversalRenderPipeline.SingleCameraRequest { destination = renderTarget };
            if (RenderPipeline.SupportsRenderRequest(utilityCamera, renderRequest))
            {
                RenderPipeline.SubmitRenderRequest(utilityCamera, renderRequest);
            }
            else
            {
                // Outside URP (or a pipeline refusing requests) the legacy path still works and is
                // better than a black pane — A59 §1's recorded fallback.
                utilityCamera.targetTexture = renderTarget;
                utilityCamera.Render();
                utilityCamera.targetTexture = null;
            }
            sceneImage.MarkDirtyRepaint();
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
        // Clip Editor viewport opens with. Fly/look are A59 backlog, not silently missing.
        // -----------------------------------------------------------------------------------

        private void OnPointerDown(PointerDownEvent pointerEvent)
        {
            if (pointerEvent.button != 0 && pointerEvent.button != 2)
            {
                return;
            }
            if (IsShowingShotPose)
            {
                AdoptRenderedPoseAsFreeRig();
                NavigationBrokeShot?.Invoke();
            }
            this.CapturePointer(pointerEvent.pointerId);
            capturedPointerId = pointerEvent.pointerId;
            activeDragButton = pointerEvent.button;
            lastPointerPosition = pointerEvent.position;
            Focus();
            pointerEvent.StopPropagation();
        }

        private void OnPointerMove(PointerMoveEvent moveEvent)
        {
            if (capturedPointerId != moveEvent.pointerId || !this.HasPointerCapture(moveEvent.pointerId))
            {
                return;
            }
            Vector2 delta = (Vector2)moveEvent.position - lastPointerPosition;
            lastPointerPosition = moveEvent.position;

            if (activeDragButton == 0)
            {
                orbitYawDegrees += delta.x * OrbitDegreesPerPixel;
                orbitPitchDegrees = Mathf.Clamp(
                    orbitPitchDegrees + delta.y * OrbitDegreesPerPixel, -PitchLimitDegrees, PitchLimitDegrees);
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
            EndDrag();
        }

        private void EndDrag()
        {
            capturedPointerId = -1;
            activeDragButton = -1;
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
