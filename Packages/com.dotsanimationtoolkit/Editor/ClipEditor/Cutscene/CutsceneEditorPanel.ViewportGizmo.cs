// Copyright (c) 2026 Spencer Park. All rights reserved.

using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    public sealed partial class CutsceneEditorPanel
    {
        /// <summary>Handle length as a fraction of the distance from the camera, so the gizmo holds one size on screen.</summary>
        private const float ViewportGizmoScreenFraction = 0.18f;

        // Renamed off PreviewTransformGizmo's own "ClipPreviewGizmo" so a leak sweep can tell this
        // one from the Clip Editor's, which is a live object in the same session.
        private const string ViewportGizmoObjectName = "CutsceneViewportGizmo (hidden)";

        private readonly PreviewTransformGizmo viewportGizmo = new PreviewTransformGizmo();
        private GizmoMode viewportGizmoMode = GizmoMode.Move;
        private GizmoHandle draggedViewportHandle = GizmoHandle.None;

        /// <summary>True between a gizmo press and its release. Auto Key reads this the way it reads <c>hotControl</c>.</summary>
        private bool isViewportGizmoDragging;

        private Transform draggedViewportTransform;
        private Vector3 dragStartPivot;
        private Vector3 dragStartLocalPosition;
        private Quaternion dragStartLocalRotation;
        private Vector3 dragStartLocalScale;
        private float dragStartAxisParameter;
        private float dragStartAngleDegrees;
        private float dragStartPivotDistance;
        private float dragHandleLength;

        // The gizmo is never a scene object: its mesh is submitted for the tab's own camera alone.
        // A GameObject with HideAndDontSave would render in the Scene view too (scene visibility
        // cannot hide one — it is not in a scene), and would outlive a domain reload as a leak.
        private void DrawViewportGizmoForCamera(Camera viewportCamera)
        {
            GameObject gizmoObject = viewportGizmo.GizmoObject;
            if (gizmoObject == null || !isViewportGizmoVisible)
            {
                return;
            }
            MeshFilter meshFilter = gizmoObject.GetComponent<MeshFilter>();
            MeshRenderer meshRenderer = gizmoObject.GetComponent<MeshRenderer>();
            if (meshFilter == null || meshFilter.sharedMesh == null || meshRenderer == null)
            {
                return;
            }
            Graphics.DrawMesh(
                meshFilter.sharedMesh, Matrix4x4.identity, meshRenderer.sharedMaterial, 0,
                viewportCamera, 0, null, false, false, false);
        }

        private bool isViewportGizmoVisible;

        /// <summary>The transform the gizmo stands on: the selected part when a part track is picked, else the slot's bound object.</summary>
        private Transform ResolveViewportGizmoTarget()
        {
            if (cutscene == null || !previewController.IsActive
                || selectedSlotIndex < 0 || selectedSlotIndex >= cutscene.slots.Count)
            {
                return null;
            }
            CutsceneSlot slot = cutscene.slots[selectedSlotIndex];
            if (slot == null)
            {
                return null;
            }

            if ((selectedLaneKind == SelectedLaneKind.PartTrackHeader
                    || selectedLaneKind == SelectedLaneKind.PartTrackKey)
                && selectedPartTrackIndex >= 0 && selectedPartTrackIndex < slot.partTracks.Count
                && slot.rig != null)
            {
                Transform partTransform = previewController.GetBoundPartTransform(
                    slot.SlotId, slot.rig, slot.partTracks[selectedPartTrackIndex].tagId);
                if (partTransform != null)
                {
                    return partTransform;
                }
            }

            GameObject boundObject = previewController.GetBoundObject(slot.SlotId);
            return boundObject == null ? null : boundObject.transform;
        }

        // Rebuilt before every render rather than cached: the pivot moves with the playhead, and the
        // handle length has to follow the camera or the gizmo grows as you dolly in.
        private void RefreshViewportGizmo()
        {
            Transform target = isPlaying ? null : ResolveViewportGizmoTarget();
            if (target == null || viewportElement == null)
            {
                isViewportGizmoVisible = false;
                viewportGizmo.Hide();
                return;
            }

            Vector3 pivot = target.position;
            dragHandleLength = ComputeViewportHandleLength(pivot);
            viewportGizmo.Rebuild(viewportGizmoMode, pivot, dragHandleLength, draggedViewportHandle);

            // Rebuild activates the object, which would put it in the Scene view as well. The mesh
            // is all this wants; nothing may render from the scene itself.
            if (viewportGizmo.GizmoObject != null)
            {
                viewportGizmo.GizmoObject.SetActive(false);
                viewportGizmo.GizmoObject.name = ViewportGizmoObjectName;
            }
            isViewportGizmoVisible = true;
        }

        private float ComputeViewportHandleLength(Vector3 pivot)
        {
            float distanceToCamera = Vector3.Distance(viewportElement.RenderedCameraPosition, pivot);
            return Mathf.Max(0.05f, distanceToCamera * ViewportGizmoScreenFraction);
        }

        /// <summary>The single writer of the gizmo mode, so W/E/R and any future buttons cannot disagree.</summary>
        private void SetViewportGizmoMode(GizmoMode mode)
        {
            viewportGizmoMode = mode;
            RefreshGizmoModeToggles();
            RenderViewport();
        }

        /// <summary>Lights the rail toggle matching <see cref="viewportGizmoMode"/> so W/E/R and rail clicks never disagree.</summary>
        private void RefreshGizmoModeToggles()
        {
            if (gizmoModeToggles == null)
            {
                return;
            }
            for (int gizmoModeIndex = 0; gizmoModeIndex < gizmoModeToggles.Length; gizmoModeIndex++)
            {
                gizmoModeToggles[gizmoModeIndex]?.SetValueWithoutNotify(gizmoModeIndex == (int)viewportGizmoMode);
            }
        }

        private bool TryBeginViewportGizmoDrag(Vector2 localPosition)
        {
            Transform target = isPlaying ? null : ResolveViewportGizmoTarget();
            Ray pickRay;
            if (target == null || viewportElement == null
                || !viewportElement.TryBuildPickRay(localPosition, out pickRay))
            {
                return false;
            }

            Vector3 pivot = target.position;
            float handleLength = ComputeViewportHandleLength(pivot);
            GizmoHandle handle = PreviewGizmoMath.PickHandle(
                pickRay, viewportGizmoMode, pivot, handleLength);
            if (handle == GizmoHandle.None)
            {
                return false;
            }

            draggedViewportHandle = handle;
            draggedViewportTransform = target;
            dragStartPivot = pivot;
            dragHandleLength = handleLength;
            dragStartLocalPosition = target.localPosition;
            dragStartLocalRotation = target.localRotation;
            dragStartLocalScale = target.localScale;
            dragStartPivotDistance = Vector3.Distance(pickRay.origin, pivot);
            isViewportGizmoDragging = true;

            if (viewportGizmoMode == GizmoMode.Rotate)
            {
                Vector3 planeHit;
                if (PreviewGizmoMath.TryIntersectPlane(
                        pickRay, pivot, PreviewGizmoMath.GetRotationPlaneNormal(handle), out planeHit))
                {
                    dragStartAngleDegrees =
                        PreviewGizmoMath.AngleAroundPivotDegrees(planeHit, pivot, handle);
                }
            }
            else
            {
                PreviewGizmoMath.TryGetClosestAxisParameter(
                    pickRay, pivot, PreviewGizmoMath.GetHandleAxis(handle), out dragStartAxisParameter);
            }

            Undo.RecordObject(target, "Move Cutscene Actor");
            RenderViewport();
            return true;
        }

        private void ContinueViewportGizmoDrag(Vector2 localPosition)
        {
            Ray pickRay;
            if (!isViewportGizmoDragging || draggedViewportTransform == null
                || viewportElement == null
                || !viewportElement.TryBuildPickRay(localPosition, out pickRay))
            {
                return;
            }

            switch (viewportGizmoMode)
            {
                case GizmoMode.Rotate:
                    ApplyViewportRotateDrag(pickRay);
                    break;
                case GizmoMode.Scale:
                    ApplyViewportScaleDrag(pickRay);
                    break;
                default:
                    ApplyViewportMoveDrag(pickRay);
                    break;
            }
            RenderViewport();
        }

        private void ApplyViewportMoveDrag(Ray pickRay)
        {
            Vector3 axis = PreviewGizmoMath.GetHandleAxis(draggedViewportHandle);
            float axisParameter;
            if (!PreviewGizmoMath.TryGetClosestAxisParameter(
                    pickRay, dragStartPivot, axis, out axisParameter))
            {
                return;
            }
            // Written in the parent's space, because localPosition is what a root key stores.
            Vector3 worldDelta = axis * (axisParameter - dragStartAxisParameter);
            Transform parent = draggedViewportTransform.parent;
            Vector3 localDelta = parent == null ? worldDelta : parent.InverseTransformVector(worldDelta);
            draggedViewportTransform.localPosition = dragStartLocalPosition + localDelta;
        }

        private void ApplyViewportRotateDrag(Ray pickRay)
        {
            Vector3 planeNormal = PreviewGizmoMath.GetRotationPlaneNormal(draggedViewportHandle);
            Vector3 planeHit;
            if (!PreviewGizmoMath.TryIntersectPlane(pickRay, dragStartPivot, planeNormal, out planeHit))
            {
                return;
            }
            float angleDegrees = PreviewGizmoMath.AngleAroundPivotDegrees(
                planeHit, dragStartPivot, draggedViewportHandle);
            Quaternion worldTurn = Quaternion.AngleAxis(dragStartAngleDegrees - angleDegrees, planeNormal);
            Transform parent = draggedViewportTransform.parent;
            Quaternion localTurn = parent == null
                ? worldTurn
                : Quaternion.Inverse(parent.rotation) * worldTurn * parent.rotation;
            draggedViewportTransform.localRotation = localTurn * dragStartLocalRotation;
        }

        private void ApplyViewportScaleDrag(Ray pickRay)
        {
            if (draggedViewportHandle == GizmoHandle.ScaleUniform)
            {
                // Uniform scale reads the pointer's distance from the camera-to-pivot line rather
                // than an axis, since the uniform handle sits on the pivot and has no direction.
                float currentDistance = Vector3.Distance(pickRay.origin, dragStartPivot);
                float ratio = dragStartPivotDistance <= 1e-4f
                    ? 1f
                    : currentDistance / dragStartPivotDistance;
                draggedViewportTransform.localScale = dragStartLocalScale * Mathf.Max(0.01f, ratio);
                return;
            }

            Vector3 axis = PreviewGizmoMath.GetHandleAxis(draggedViewportHandle);
            float axisParameter;
            if (!PreviewGizmoMath.TryGetClosestAxisParameter(
                    pickRay, dragStartPivot, axis, out axisParameter))
            {
                return;
            }
            float travel = axisParameter - dragStartAxisParameter;
            float scaleFactor = Mathf.Max(0.01f, 1f + travel / Mathf.Max(0.01f, dragHandleLength));
            Vector3 scaled = dragStartLocalScale;
            if (draggedViewportHandle == GizmoHandle.AxisX) { scaled.x *= scaleFactor; }
            else if (draggedViewportHandle == GizmoHandle.AxisY) { scaled.y *= scaleFactor; }
            else { scaled.z *= scaleFactor; }
            draggedViewportTransform.localScale = scaled;
        }

        // The drag wrote the same transform Unity's own gizmo writes, so Key and Auto Key need no
        // in-tab special case beyond seeing the gesture end.
        private void EndViewportGizmoDrag()
        {
            isViewportGizmoDragging = false;
            draggedViewportHandle = GizmoHandle.None;
            draggedViewportTransform = null;
            RenderViewport();
        }

        private void DisposeViewportGizmo()
        {
            isViewportGizmoVisible = false;
            viewportGizmo.Dispose();
        }

        /// <summary>
        /// A HideAndDontSave object outlives the panel across a domain reload, so one is swept by
        /// name on attach — the same net the viewport camera needs.
        /// </summary>
        private static void DestroyLeakedViewportGizmos()
        {
            // Not GameObject.Find: HideAndDontSave objects are hidden from it.
            GameObject[] allObjects = Resources.FindObjectsOfTypeAll<GameObject>();
            for (int objectIndex = 0; objectIndex < allObjects.Length; objectIndex++)
            {
                GameObject candidate = allObjects[objectIndex];
                if (candidate != null && candidate.name == ViewportGizmoObjectName)
                {
                    Object.DestroyImmediate(candidate);
                }
            }
        }
    }
}
