// Copyright (c) 2026 Spencer Park. All rights reserved.

using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// The viewport's ragdoll box handles (Phase D6, spec §8.3): selecting a body, and dragging its
    /// centre, faces or rotation ring(s).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Its own drag pipeline, parallel to <c>TryBeginGizmoDrag</c>/<c>ContinueGizmoDrag</c>/
    /// <c>EndGizmoDrag</c>, not a mode of it.</strong> A ragdoll box is picked and drawn by
    /// <see cref="PreviewRagdollBoxHandles"/>, an entirely different mesh and hit-test from
    /// <see cref="PreviewTransformGizmo"/>'s move/rotate/scale handles, so the two cannot share one
    /// state machine without one of them growing branches the other never takes. What they do share
    /// is the <em>destination</em> a finished drag routes to — <see cref="GizmoDragRouting"/> — per
    /// spec §8.3's explicit instruction to route through the existing router rather than committing
    /// directly.
    /// </para>
    /// <para>
    /// <strong>Undo is recorded once, at the press, never per pointer move.</strong> The asset field
    /// is mutated live during the drag so the handles and the box track the cursor, exactly the
    /// shape <c>ContinueGizmoDrag</c> uses for a Rig Edit drag — the difference is only that a
    /// ragdoll body has no separate "held" representation to fall back to, because spec §8.3 never
    /// offers one: placing a box always writes the rig, live.
    /// </para>
    /// </remarks>
    public sealed partial class ClipEditorWindow
    {
        private const float RagdollBoxMinimumFullSize = 0.02f;

        /// <summary>Which ragdoll body the component stack and the viewport handles are pointed at. 0 for none.</summary>
        private uint selectedRagdollBodyId;

        private RagdollBoxHandle activeRagdollBoxHandle = RagdollBoxHandle.None;
        private uint ragdollDragBodyId;
        private Transform ragdollDragNode;
        private Vector3 ragdollDragStartLocalCenter;
        private Vector3 ragdollDragStartLocalSize;
        private Vector3 ragdollDragStartEulerAngles;
        private Vector3 ragdollDragBoxWorldCenterAtStart;

        // Face-drag state.
        private Vector3 ragdollDragWorldAxis;
        private Vector3 ragdollDragLocalAxis;
        private int ragdollDragAxisComponent;
        private int ragdollDragHandleSign;
        private float ragdollDragStartAxisParameter;

        // Centre-drag state.
        private Vector3 ragdollCenterDragStartWorldHit;

        // Rotation-drag state.
        private Vector3 ragdollDragPlaneAxis1;
        private Vector3 ragdollDragPlaneAxis2;
        private float ragdollDragStartAngleDegrees;

        /// <summary>
        /// Points the component stack's active marking and the viewport handles at one ragdoll body
        /// (spec §8.3, mirroring <c>FocusSocket</c>). Separate field from
        /// <see cref="selectedSocketId"/>/<see cref="selectedTargetId"/> because a Ragdoll selection
        /// does not move the ordinary hierarchy selection or outline — a body's node may itself be
        /// the object already outlined.
        /// </summary>
        private void FocusRagdollBody(uint bodyId)
        {
            selectedRagdollBodyId = bodyId;
            if (previewController != null)
            {
                previewController.SetSelectedRagdollBodyId(bodyId);
            }
            RebuildInspector();
        }

        private static RagdollBodyDefinition FindRagdollBodyById(RigAsset rig, uint bodyId)
        {
            if (rig == null || rig.ragdollBodies == null || bodyId == 0u)
            {
                return null;
            }
            for (int index = 0; index < rig.ragdollBodies.Count; index++)
            {
                RagdollBodyDefinition definition = rig.ragdollBodies[index];
                if (definition != null && definition.Id.Value == bodyId)
                {
                    return definition;
                }
            }
            return null;
        }

        /// <summary>Whether the press landed on the selected ragdoll body's grab handle, and if so, starts the drag.</summary>
        private bool TryBeginRagdollBoxDrag(Vector2 localPosition)
        {
            if (selectedRagdollBodyId == 0u || previewController == null)
            {
                return false;
            }

            Vector2 viewportPoint;
            float aspect;
            if (!TryGetViewportPoint(localPosition, out viewportPoint, out aspect))
            {
                return false;
            }

            RagdollBoxHandle handle = previewController.PickRagdollBoxHandle(viewportPoint, aspect);
            if (handle == RagdollBoxHandle.None)
            {
                return false;
            }

            RigAsset rig = ActiveRig;
            RagdollBodyDefinition body = FindRagdollBodyById(rig, selectedRagdollBodyId);
            RagdollBoxVisual box;
            if (rig == null || body == null || !previewController.TryGetSelectedRagdollBoxVisual(out box))
            {
                return false;
            }
            Transform node = previewController.ResolveRagdollNode(body.address);
            if (node == null)
            {
                return false;
            }

            Ray pressRay = previewController.BuildViewportRay(viewportPoint, aspect);

            ragdollDragBodyId = selectedRagdollBodyId;
            ragdollDragNode = node;
            ragdollDragStartLocalCenter = ToVector3(body.boxCenter);
            ragdollDragStartLocalSize = ToVector3(body.boxSize);
            ragdollDragStartEulerAngles = ToVector3(body.boxEulerAngles);
            ragdollDragBoxWorldCenterAtStart = box.center;

            Quaternion boxLocalRotation = Quaternion.Euler(ragdollDragStartEulerAngles);

            bool dragStarted;
            switch (handle)
            {
                case RagdollBoxHandle.Center:
                    dragStarted = BeginCenterDrag(pressRay, box.center);
                    break;
                case RagdollBoxHandle.RotateX:
                case RagdollBoxHandle.RotateY:
                case RagdollBoxHandle.RotateZ:
                    dragStarted = BeginRotateDrag(pressRay, box.center, node.rotation, boxLocalRotation, handle);
                    break;
                default:
                    dragStarted = BeginFaceDrag(pressRay, box.center, node.rotation, boxLocalRotation, handle);
                    break;
            }

            if (!dragStarted)
            {
                return false;
            }

            // One undo group for the whole gesture — never per pointer move (spec §8.3's own "Undo
            // recorded on the rig asset", singular).
            Undo.RecordObject(rig, "Edit Ragdoll Box");

            activeRagdollBoxHandle = handle;
            previewController.SetActiveRagdollBoxHandle(handle);
            return true;
        }

        private bool BeginCenterDrag(Ray pressRay, Vector3 boxWorldCenter)
        {
            Vector3 planeHit;
            if (!PreviewGizmoMath.TryIntersectPlane(pressRay, boxWorldCenter, previewController.CameraForward, out planeHit))
            {
                return false;
            }
            ragdollCenterDragStartWorldHit = planeHit;
            return true;
        }

        private bool BeginFaceDrag(
            Ray pressRay, Vector3 boxWorldCenter, Quaternion nodeRotation, Quaternion boxLocalRotation,
            RagdollBoxHandle handle)
        {
            Vector3 localAxis;
            int axisComponent;
            int handleSign;
            ResolveFaceAxis(handle, out localAxis, out axisComponent, out handleSign);

            ragdollDragLocalAxis = localAxis;
            ragdollDragAxisComponent = axisComponent;
            ragdollDragHandleSign = handleSign;
            ragdollDragWorldAxis = nodeRotation * (boxLocalRotation * localAxis);

            float startParameter;
            if (!PreviewGizmoMath.TryGetClosestAxisParameter(
                    pressRay, boxWorldCenter, ragdollDragWorldAxis, out startParameter))
            {
                return false;
            }
            ragdollDragStartAxisParameter = startParameter;
            return true;
        }

        private bool BeginRotateDrag(
            Ray pressRay, Vector3 boxWorldCenter, Quaternion nodeRotation, Quaternion boxLocalRotation,
            RagdollBoxHandle handle)
        {
            Vector3 axis1;
            Vector3 axis2;
            ResolveRingAxes(handle, nodeRotation, boxLocalRotation, out axis1, out axis2);
            ragdollDragPlaneAxis1 = axis1;
            ragdollDragPlaneAxis2 = axis2;

            Vector3 planeNormal = Vector3.Cross(axis1, axis2).normalized;
            Vector3 planeHit;
            if (!PreviewGizmoMath.TryIntersectPlane(pressRay, boxWorldCenter, planeNormal, out planeHit))
            {
                return false;
            }
            ragdollDragStartAngleDegrees = AngleAroundPivot(planeHit, boxWorldCenter, axis1, axis2);
            return true;
        }

        /// <summary>Turns pointer motion into the dragged field's new value, written straight onto the rig asset.</summary>
        private void ContinueRagdollBoxDrag(Vector2 localPosition, bool symmetric)
        {
            Vector2 viewportPoint;
            float aspect;
            if (!TryGetViewportPoint(localPosition, out viewportPoint, out aspect) || previewController == null)
            {
                return;
            }

            RigAsset rig = ActiveRig;
            RagdollBodyDefinition body = FindRagdollBodyById(rig, ragdollDragBodyId);
            if (rig == null || body == null || ragdollDragNode == null)
            {
                return;
            }

            Ray dragRay = previewController.BuildViewportRay(viewportPoint, aspect);

            switch (activeRagdollBoxHandle)
            {
                case RagdollBoxHandle.Center:
                    ContinueCenterDrag(dragRay, body);
                    break;
                case RagdollBoxHandle.RotateX:
                case RagdollBoxHandle.RotateY:
                case RagdollBoxHandle.RotateZ:
                    ContinueRotateDrag(dragRay, body);
                    break;
                default:
                    ContinueFaceDrag(dragRay, body, symmetric);
                    break;
            }
        }

        private void ContinueCenterDrag(Ray dragRay, RagdollBodyDefinition body)
        {
            Vector3 planeHit;
            if (!PreviewGizmoMath.TryIntersectPlane(
                    dragRay, ragdollDragBoxWorldCenterAtStart, previewController.CameraForward, out planeHit))
            {
                return;
            }
            Vector3 worldDelta = planeHit - ragdollCenterDragStartWorldHit;
            Vector3 localDelta = Quaternion.Inverse(ragdollDragNode.rotation) * worldDelta;
            body.boxCenter = ToFloat3(ragdollDragStartLocalCenter + localDelta);
        }

        private void ContinueFaceDrag(Ray dragRay, RagdollBodyDefinition body, bool symmetric)
        {
            float currentParameter;
            if (!PreviewGizmoMath.TryGetClosestAxisParameter(
                    dragRay, ragdollDragBoxWorldCenterAtStart, ragdollDragWorldAxis, out currentParameter))
            {
                return;
            }
            float rawDelta = currentParameter - ragdollDragStartAxisParameter;

            Vector3 newSize = ragdollDragStartLocalSize;
            Vector3 newCenter = ragdollDragStartLocalCenter;
            float startComponent = GetAxisComponent(ragdollDragStartLocalSize, ragdollDragAxisComponent);

            if (symmetric)
            {
                // Both faces move together: the centre never moves, and the dragged face's own
                // motion is mirrored onto the opposite face (spec §8.3: "symmetric with a modifier
                // held").
                float sizeDelta = 2f * ragdollDragHandleSign * rawDelta;
                SetAxisComponent(
                    ref newSize, ragdollDragAxisComponent,
                    Mathf.Max(RagdollBoxMinimumFullSize, startComponent + sizeDelta));
            }
            else
            {
                // One-sided: only the dragged face moves, so the centre shifts by half of what the
                // face moved (see the Phase D6 report for the full derivation).
                float sizeDelta = ragdollDragHandleSign * rawDelta;
                SetAxisComponent(
                    ref newSize, ragdollDragAxisComponent,
                    Mathf.Max(RagdollBoxMinimumFullSize, startComponent + sizeDelta));
                newCenter = ragdollDragStartLocalCenter + ragdollDragLocalAxis * (rawDelta * 0.5f);
            }

            body.boxSize = ToFloat3(newSize);
            body.boxCenter = ToFloat3(newCenter);
        }

        private void ContinueRotateDrag(Ray dragRay, RagdollBodyDefinition body)
        {
            Vector3 planeNormal = Vector3.Cross(ragdollDragPlaneAxis1, ragdollDragPlaneAxis2).normalized;
            Vector3 planeHit;
            if (!PreviewGizmoMath.TryIntersectPlane(
                    dragRay, ragdollDragBoxWorldCenterAtStart, planeNormal, out planeHit))
            {
                return;
            }
            float currentAngle = AngleAroundPivot(
                planeHit, ragdollDragBoxWorldCenterAtStart, ragdollDragPlaneAxis1, ragdollDragPlaneAxis2);
            float angleDelta = Mathf.DeltaAngle(ragdollDragStartAngleDegrees, currentAngle);

            Vector3 newEuler = ragdollDragStartEulerAngles;
            switch (activeRagdollBoxHandle)
            {
                case RagdollBoxHandle.RotateX:
                    newEuler.x += angleDelta;
                    break;
                case RagdollBoxHandle.RotateY:
                    newEuler.y += angleDelta;
                    break;
                default:
                    newEuler.z += angleDelta;
                    break;
            }
            body.boxEulerAngles = ToFloat3(newEuler);
        }

        /// <summary>
        /// Ends a ragdoll box drag, routed through <see cref="GizmoDragRouting"/> per spec §8.3 —
        /// always <see cref="GizmoDragDestination.RagdollBody"/> in practice, since a selected body
        /// wins outright, but decided by the same table every other viewport drag answers to rather
        /// than special-cased out of it.
        /// </summary>
        private void EndRagdollBoxDrag()
        {
            if (activeRagdollBoxHandle == RagdollBoxHandle.None)
            {
                return;
            }
            activeRagdollBoxHandle = RagdollBoxHandle.None;
            ragdollDragNode = null;
            if (previewController != null)
            {
                previewController.SetActiveRagdollBoxHandle(RagdollBoxHandle.None);
            }

            RigAsset rig = ActiveRig;
            GizmoDragDestination destination = GizmoDragRouting.Resolve(
                selectedSocketId != 0u, true, IsRigEditMode, IsAutoKeyEnabled, true);
            if (destination == GizmoDragDestination.RagdollBody && rig != null)
            {
                EditorUtility.SetDirty(rig);
            }
            RebuildInspector();
        }

        // -----------------------------------------------------------------------------------
        // Small geometry helpers, kept local: they exist only to keep the drag math above
        // readable, and nothing outside this file needs them.
        // -----------------------------------------------------------------------------------

        private static void ResolveFaceAxis(
            RagdollBoxHandle handle, out Vector3 localAxis, out int axisComponent, out int handleSign)
        {
            switch (handle)
            {
                case RagdollBoxHandle.FacePosX:
                    localAxis = Vector3.right;
                    axisComponent = 0;
                    handleSign = 1;
                    break;
                case RagdollBoxHandle.FaceNegX:
                    localAxis = Vector3.right;
                    axisComponent = 0;
                    handleSign = -1;
                    break;
                case RagdollBoxHandle.FacePosY:
                    localAxis = Vector3.up;
                    axisComponent = 1;
                    handleSign = 1;
                    break;
                case RagdollBoxHandle.FaceNegY:
                    localAxis = Vector3.up;
                    axisComponent = 1;
                    handleSign = -1;
                    break;
                case RagdollBoxHandle.FacePosZ:
                    localAxis = Vector3.forward;
                    axisComponent = 2;
                    handleSign = 1;
                    break;
                default:
                    localAxis = Vector3.forward;
                    axisComponent = 2;
                    handleSign = -1;
                    break;
            }
        }

        /// <summary>
        /// The world-space axes a rotation ring's angle is measured within — matching
        /// <see cref="PreviewRagdollBoxHandles.Pick"/>'s own ring plane pairing exactly, so a ring's
        /// drag direction agrees with which ring the pick math decided was under the cursor.
        /// </summary>
        private static void ResolveRingAxes(
            RagdollBoxHandle handle, Quaternion nodeRotation, Quaternion boxLocalRotation,
            out Vector3 axis1, out Vector3 axis2)
        {
            Vector3 worldAxisX = nodeRotation * (boxLocalRotation * Vector3.right);
            Vector3 worldAxisY = nodeRotation * (boxLocalRotation * Vector3.up);
            Vector3 worldAxisZ = nodeRotation * (boxLocalRotation * Vector3.forward);
            switch (handle)
            {
                case RagdollBoxHandle.RotateX:
                    axis1 = worldAxisY;
                    axis2 = worldAxisZ;
                    break;
                case RagdollBoxHandle.RotateY:
                    axis1 = worldAxisZ;
                    axis2 = worldAxisX;
                    break;
                default:
                    axis1 = worldAxisX;
                    axis2 = worldAxisY;
                    break;
            }
        }

        private static float AngleAroundPivot(Vector3 point, Vector3 pivot, Vector3 axis1, Vector3 axis2)
        {
            Vector3 offset = point - pivot;
            float coordinate1 = Vector3.Dot(offset, axis1);
            float coordinate2 = Vector3.Dot(offset, axis2);
            return Mathf.Atan2(coordinate2, coordinate1) * Mathf.Rad2Deg;
        }

        private static float GetAxisComponent(Vector3 value, int axisComponent)
        {
            switch (axisComponent)
            {
                case 0:
                    return value.x;
                case 1:
                    return value.y;
                default:
                    return value.z;
            }
        }

        private static void SetAxisComponent(ref Vector3 target, int axisComponent, float value)
        {
            switch (axisComponent)
            {
                case 0:
                    target.x = value;
                    break;
                case 1:
                    target.y = value;
                    break;
                default:
                    target.z = value;
                    break;
            }
        }

    }
}
