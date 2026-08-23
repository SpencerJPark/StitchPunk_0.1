// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>One ragdoll body's box, resolved to world space, for <see cref="PreviewRagdollBoxHandles"/> to draw and pick against.</summary>
    public struct RagdollBoxVisual
    {
        /// <summary>The body's stable id, so the selected one can be told apart from the rest.</summary>
        public uint bodyId;

        /// <summary>World-space centre of the box.</summary>
        public Vector3 center;

        /// <summary>World-space orientation of the box.</summary>
        public Quaternion rotation;

        /// <summary>Full size, local to <see cref="rotation"/>.</summary>
        public Vector3 size;
    }

    /// <summary>The specific ragdoll box handle under the cursor, or none.</summary>
    public enum RagdollBoxHandle : byte
    {
        None = 0,
        Center = 1,
        FaceNegX = 2,
        FacePosX = 3,
        FaceNegY = 4,
        FacePosY = 5,
        FaceNegZ = 6,
        FacePosZ = 7,
        RotateX = 8,
        RotateY = 9,
        RotateZ = 10
    }

    /// <summary>
    /// The viewport's ragdoll box handles (Phase D6, spec §8.3): a wireframe box for every body the
    /// rig declares, the selected one highlighted, plus the selected body's own grab handles — six
    /// face handles that resize, a centre handle that moves, and one rotation ring in
    /// <see cref="RagdollSpace.Planar2D"/> or three in <see cref="RagdollSpace.Spatial3D"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Two different things share one mesh, the same way <see cref="PreviewSceneGizmos"/>'s
    /// grid and selection outline are two different things in two different objects.</strong> Every
    /// body's wireframe is scene furniture — drawn whenever the rig has bodies, exactly as
    /// <c>PreviewSocketMarkers</c> shows every socket regardless of which one is selected. The grab
    /// handles are authoring surface — drawn only for the selected body, live whenever a Ragdoll
    /// component is selected rather than only in Rig Edit mode (spec §8.3: "placing a box is a rig
    /// edit but not a hierarchy edit," the same call socket placement already makes).
    /// </para>
    /// <para>
    /// <strong>Line mesh, not <c>Handles</c>, matching <see cref="PreviewTransformGizmo"/> exactly.</strong>
    /// <c>Conformance_E</c> bans immediate-mode drawing in package editor sources, and the preview
    /// renders through its own <c>PreviewRenderUtility</c> scene where an immediate-mode handle would
    /// have nothing to draw into regardless.
    /// </para>
    /// <para>
    /// <strong>Geometry is baked in world space, not carried by a <c>Transform</c>.</strong> A body's
    /// box can be rotated arbitrarily relative to the viewport, and several boxes are drawn into one
    /// mesh at once (every body's wireframe, plus one selected body's handles) — there is no single
    /// object transform that could carry all of that, so every vertex is computed in world space
    /// directly, the same choice <see cref="PreviewTransformGizmo"/> makes for its own handles.
    /// </para>
    /// </remarks>
    public sealed class PreviewRagdollBoxHandles
    {
        private const int RingSegments = 48;
        private const float FaceHandleSizeFactor = 0.06f;
        private const float CenterHandleSizeFactor = 0.08f;
        private const float RotateRingRadiusFactor = 1.15f;

        private static readonly Color BodyWireColor = new Color(0.45f, 0.62f, 0.55f, 1f);
        private static readonly Color SelectedBodyWireColor = new Color(0.98f, 0.72f, 0.24f, 1f);
        private static readonly Color FaceHandleColor = new Color(0.55f, 0.80f, 0.95f, 1f);
        private static readonly Color CenterHandleColor = new Color(0.92f, 0.86f, 0.45f, 1f);
        private static readonly Color RotateRingColor = new Color(0.70f, 0.55f, 0.90f, 1f);
        private static readonly Color HighlightColor = new Color(1f, 0.92f, 0.35f, 1f);

        private readonly List<Vector3> vertexBuffer = new List<Vector3>();
        private readonly List<Color> colorBuffer = new List<Color>();
        private readonly List<int> indexBuffer = new List<int>();

        private GameObject handlesObject;
        private Mesh handlesMesh;
        private Material lineMaterial;

        /// <summary>The handles' root, or null before <see cref="EnsureBuilt"/> has run.</summary>
        public GameObject HandlesObject
        {
            get { return handlesObject; }
        }

        /// <summary>Creates the object and mesh if they do not exist. Cheap to call per frame.</summary>
        public void EnsureBuilt()
        {
            if (handlesObject != null)
            {
                return;
            }

            handlesMesh = new Mesh();
            handlesMesh.name = "ClipPreviewRagdollBoxHandlesMesh";
            handlesMesh.hideFlags = HideFlags.HideAndDontSave;
            handlesMesh.MarkDynamic();

            lineMaterial = PreviewLineMaterial.Create("ClipPreviewRagdollBoxHandles");

            handlesObject = new GameObject("ClipPreviewRagdollBoxHandles");
            handlesObject.hideFlags = HideFlags.HideAndDontSave;

            MeshFilter meshFilter = handlesObject.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = handlesMesh;

            MeshRenderer meshRenderer = handlesObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = lineMaterial;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            handlesObject.SetActive(false);
        }

        /// <summary>Hides the handles. Idempotent.</summary>
        public void Hide()
        {
            if (handlesObject != null)
            {
                handlesObject.SetActive(false);
            }
        }

        /// <summary>
        /// Picks the selected body's grab handle under a ray, or <see cref="RagdollBoxHandle.None"/>.
        /// </summary>
        /// <remarks>
        /// <strong>What is drawn is exactly what this tests</strong> — the same discipline
        /// <see cref="PreviewTransformGizmo"/>'s own remarks name: this method and
        /// <see cref="AppendGrabHandles"/> share the same size constants rather than each picking
        /// its own, so a handle is grabbable precisely where it appears.
        /// </remarks>
        public static RagdollBoxHandle Pick(Ray ray, in RagdollBoxVisual box, RagdollSpace space, float handleLength)
        {
            float pickRadius = handleLength * PreviewGizmoMath.HandlePickRadiusFactor;
            Vector3 axisX = box.rotation * Vector3.right;
            Vector3 axisY = box.rotation * Vector3.up;
            Vector3 axisZ = box.rotation * Vector3.forward;
            Vector3 halfSize = box.size * 0.5f;

            // Centre tested first: it sits inside the box every face handle is offset from, so
            // testing the faces first could never be reached from inside their own shared origin.
            if (PreviewGizmoMath.DistanceFromRayToSegment(ray, box.center, box.center) <= pickRadius * 1.2f)
            {
                return RagdollBoxHandle.Center;
            }

            RagdollBoxHandle bestHandle = RagdollBoxHandle.None;
            float bestDistance = pickRadius;
            TestFace(ray, box.center + axisX * halfSize.x, RagdollBoxHandle.FacePosX, pickRadius, ref bestHandle, ref bestDistance);
            TestFace(ray, box.center - axisX * halfSize.x, RagdollBoxHandle.FaceNegX, pickRadius, ref bestHandle, ref bestDistance);
            TestFace(ray, box.center + axisY * halfSize.y, RagdollBoxHandle.FacePosY, pickRadius, ref bestHandle, ref bestDistance);
            TestFace(ray, box.center - axisY * halfSize.y, RagdollBoxHandle.FaceNegY, pickRadius, ref bestHandle, ref bestDistance);
            TestFace(ray, box.center + axisZ * halfSize.z, RagdollBoxHandle.FacePosZ, pickRadius, ref bestHandle, ref bestDistance);
            TestFace(ray, box.center - axisZ * halfSize.z, RagdollBoxHandle.FaceNegZ, pickRadius, ref bestHandle, ref bestDistance);
            if (bestHandle != RagdollBoxHandle.None)
            {
                return bestHandle;
            }

            float ringRadius = handleLength * RotateRingRadiusFactor;
            float ringPickTolerance = pickRadius * 1.6f;
            if (space == RagdollSpace.Planar2D)
            {
                if (TryPickRing(ray, box.center, axisX, axisY, ringRadius, ringPickTolerance))
                {
                    return RagdollBoxHandle.RotateZ;
                }
                return RagdollBoxHandle.None;
            }

            RagdollBoxHandle bestRing = RagdollBoxHandle.None;
            float bestRingError = ringPickTolerance;
            TestRing(ray, box.center, axisY, axisZ, ringRadius, RagdollBoxHandle.RotateX, ref bestRing, ref bestRingError);
            TestRing(ray, box.center, axisZ, axisX, ringRadius, RagdollBoxHandle.RotateY, ref bestRing, ref bestRingError);
            TestRing(ray, box.center, axisX, axisY, ringRadius, RagdollBoxHandle.RotateZ, ref bestRing, ref bestRingError);
            return bestRing;
        }

        private static void TestFace(
            Ray ray, Vector3 facePoint, RagdollBoxHandle handle, float pickRadius,
            ref RagdollBoxHandle bestHandle, ref float bestDistance)
        {
            float distance = PreviewGizmoMath.DistanceFromRayToSegment(ray, facePoint, facePoint);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestHandle = handle;
            }
        }

        private static bool TryPickRing(
            Ray ray, Vector3 center, Vector3 planeAxis1, Vector3 planeAxis2, float radius, float tolerance)
        {
            Vector3 planeNormal = Vector3.Cross(planeAxis1, planeAxis2).normalized;
            Vector3 planeHit;
            if (!PreviewGizmoMath.TryIntersectPlane(ray, center, planeNormal, out planeHit))
            {
                return false;
            }
            return Mathf.Abs(Vector3.Distance(planeHit, center) - radius) <= tolerance;
        }

        private static void TestRing(
            Ray ray, Vector3 center, Vector3 planeAxis1, Vector3 planeAxis2, float radius,
            RagdollBoxHandle handle, ref RagdollBoxHandle bestHandle, ref float bestError)
        {
            Vector3 planeNormal = Vector3.Cross(planeAxis1, planeAxis2).normalized;
            Vector3 planeHit;
            if (!PreviewGizmoMath.TryIntersectPlane(ray, center, planeNormal, out planeHit))
            {
                return;
            }
            float error = Mathf.Abs(Vector3.Distance(planeHit, center) - radius);
            if (error < bestError)
            {
                bestError = error;
                bestHandle = handle;
            }
        }

        /// <summary>
        /// Rebuilds every body's wireframe, plus the selected body's grab handles.
        /// </summary>
        /// <param name="boxes">Every ragdoll body currently resolved in the preview.</param>
        /// <param name="selectedBodyId">Which body's grab handles to draw; 0 draws none.</param>
        /// <param name="space">Governs whether one ring or three are drawn for the selection.</param>
        /// <param name="activeHandle">Highlighted while a drag is in progress.</param>
        /// <param name="handleLength">
        /// The gizmo scale every other viewport handle uses (<c>ClipPreviewController.GizmoHandleLength</c>),
        /// so a ragdoll box's grab handles size with the camera exactly like every other gizmo.
        /// </param>
        public void Rebuild(
            IReadOnlyList<RagdollBoxVisual> boxes, uint selectedBodyId, RagdollSpace space,
            RagdollBoxHandle activeHandle, float handleLength)
        {
            EnsureBuilt();
            if (handlesMesh == null)
            {
                return;
            }

            vertexBuffer.Clear();
            colorBuffer.Clear();
            indexBuffer.Clear();

            for (int index = 0; index < boxes.Count; index++)
            {
                RagdollBoxVisual box = boxes[index];
                bool isSelected = box.bodyId != 0u && box.bodyId == selectedBodyId;
                AppendWireBox(box.center, box.rotation, box.size, isSelected ? SelectedBodyWireColor : BodyWireColor);

                if (isSelected)
                {
                    AppendGrabHandles(box, space, activeHandle, handleLength);
                }
            }

            handlesMesh.Clear();
            handlesMesh.SetVertices(vertexBuffer);
            handlesMesh.SetColors(colorBuffer);
            handlesMesh.SetIndices(indexBuffer, MeshTopology.Lines, 0);
            handlesMesh.RecalculateBounds();

            handlesObject.SetActive(boxes.Count > 0);
        }

        private void AppendGrabHandles(
            RagdollBoxVisual box, RagdollSpace space, RagdollBoxHandle activeHandle, float handleLength)
        {
            Vector3 axisX = box.rotation * Vector3.right;
            Vector3 axisY = box.rotation * Vector3.up;
            Vector3 axisZ = box.rotation * Vector3.forward;
            Vector3 halfSize = box.size * 0.5f;

            float faceHandleSize = handleLength * FaceHandleSizeFactor;
            AppendBox(box.center + axisX * halfSize.x, faceHandleSize,
                activeHandle == RagdollBoxHandle.FacePosX ? HighlightColor : FaceHandleColor);
            AppendBox(box.center - axisX * halfSize.x, faceHandleSize,
                activeHandle == RagdollBoxHandle.FaceNegX ? HighlightColor : FaceHandleColor);
            AppendBox(box.center + axisY * halfSize.y, faceHandleSize,
                activeHandle == RagdollBoxHandle.FacePosY ? HighlightColor : FaceHandleColor);
            AppendBox(box.center - axisY * halfSize.y, faceHandleSize,
                activeHandle == RagdollBoxHandle.FaceNegY ? HighlightColor : FaceHandleColor);
            AppendBox(box.center + axisZ * halfSize.z, faceHandleSize,
                activeHandle == RagdollBoxHandle.FacePosZ ? HighlightColor : FaceHandleColor);
            AppendBox(box.center - axisZ * halfSize.z, faceHandleSize,
                activeHandle == RagdollBoxHandle.FaceNegZ ? HighlightColor : FaceHandleColor);

            AppendBox(box.center, handleLength * CenterHandleSizeFactor,
                activeHandle == RagdollBoxHandle.Center ? HighlightColor : CenterHandleColor);

            float ringRadius = handleLength * RotateRingRadiusFactor;
            if (space == RagdollSpace.Planar2D)
            {
                // The body's own local Z — the twist axis every limit and boxEulerAngles' own
                // authoring convention already measures about (spec §6.2's plane normal, expressed
                // in this body's own axes rather than recomputed from the billboard frame, since the
                // handle rotates the box's authored data, not the live simulated pose).
                AppendRing(box.center, axisX, axisY, ringRadius,
                    activeHandle == RagdollBoxHandle.RotateZ ? HighlightColor : RotateRingColor);
            }
            else
            {
                AppendRing(box.center, axisY, axisZ, ringRadius,
                    activeHandle == RagdollBoxHandle.RotateX ? HighlightColor : RotateRingColor);
                AppendRing(box.center, axisZ, axisX, ringRadius,
                    activeHandle == RagdollBoxHandle.RotateY ? HighlightColor : RotateRingColor);
                AppendRing(box.center, axisX, axisY, ringRadius,
                    activeHandle == RagdollBoxHandle.RotateZ ? HighlightColor : RotateRingColor);
            }
        }

        private void AppendWireBox(Vector3 center, Quaternion rotation, Vector3 size, Color color)
        {
            Vector3 halfSize = size * 0.5f;
            Vector3[] corners = new Vector3[8];
            for (int cornerIndex = 0; cornerIndex < 8; cornerIndex++)
            {
                Vector3 localCorner = new Vector3(
                    (cornerIndex & 1) == 0 ? -halfSize.x : halfSize.x,
                    (cornerIndex & 2) == 0 ? -halfSize.y : halfSize.y,
                    (cornerIndex & 4) == 0 ? -halfSize.z : halfSize.z);
                corners[cornerIndex] = center + rotation * localCorner;
            }
            for (int firstCorner = 0; firstCorner < 8; firstCorner++)
            {
                for (int bit = 1; bit <= 4; bit *= 2)
                {
                    int secondCorner = firstCorner | bit;
                    if (secondCorner != firstCorner)
                    {
                        AppendLine(corners[firstCorner], corners[secondCorner], color);
                    }
                }
            }
        }

        private void AppendBox(Vector3 center, float halfExtent, Color color)
        {
            Vector3[] corners = new Vector3[8];
            for (int cornerIndex = 0; cornerIndex < 8; cornerIndex++)
            {
                corners[cornerIndex] = center + new Vector3(
                    (cornerIndex & 1) == 0 ? -halfExtent : halfExtent,
                    (cornerIndex & 2) == 0 ? -halfExtent : halfExtent,
                    (cornerIndex & 4) == 0 ? -halfExtent : halfExtent);
            }
            for (int firstCorner = 0; firstCorner < 8; firstCorner++)
            {
                for (int bit = 1; bit <= 4; bit *= 2)
                {
                    int secondCorner = firstCorner | bit;
                    if (secondCorner != firstCorner)
                    {
                        AppendLine(corners[firstCorner], corners[secondCorner], color);
                    }
                }
            }
        }

        /// <summary>A ring in the plane spanned by two (assumed orthonormal) axes.</summary>
        private void AppendRing(Vector3 center, Vector3 planeAxis1, Vector3 planeAxis2, float radius, Color color)
        {
            for (int segment = 0; segment < RingSegments; segment++)
            {
                float startAngle = segment / (float)RingSegments * Mathf.PI * 2f;
                float endAngle = (segment + 1) / (float)RingSegments * Mathf.PI * 2f;
                AppendLine(
                    center + (planeAxis1 * Mathf.Cos(startAngle) + planeAxis2 * Mathf.Sin(startAngle)) * radius,
                    center + (planeAxis1 * Mathf.Cos(endAngle) + planeAxis2 * Mathf.Sin(endAngle)) * radius,
                    color);
            }
        }

        private void AppendLine(Vector3 start, Vector3 end, Color color)
        {
            indexBuffer.Add(vertexBuffer.Count);
            vertexBuffer.Add(start);
            colorBuffer.Add(color);

            indexBuffer.Add(vertexBuffer.Count);
            vertexBuffer.Add(end);
            colorBuffer.Add(color);
        }

        /// <summary>Destroys the handles object, mesh and material. Idempotent.</summary>
        public void Dispose()
        {
            if (handlesObject != null)
            {
                Object.DestroyImmediate(handlesObject);
                handlesObject = null;
            }
            if (handlesMesh != null)
            {
                Object.DestroyImmediate(handlesMesh);
                handlesMesh = null;
            }
            if (lineMaterial != null)
            {
                Object.DestroyImmediate(lineMaterial);
                lineMaterial = null;
            }
        }
    }
}
