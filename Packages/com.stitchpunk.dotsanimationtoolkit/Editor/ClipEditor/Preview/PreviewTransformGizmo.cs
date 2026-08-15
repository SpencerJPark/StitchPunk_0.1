// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System.Collections.Generic;
using UnityEngine;

namespace StitchPunk.AnimationToolkit.Editor
{
    /// <summary>
    /// The move / rotate / scale handles drawn on the selected part in the clip viewport.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One dynamic line mesh, rebuilt whenever the pivot or the mode changes, for the same reason
    /// the bone handles are: the geometry follows a pose that moves as the clip scrubs, and a
    /// GameObject per handle would be a scene full of objects to keep in step.
    /// </para>
    /// <para>
    /// <strong>What is drawn is exactly what <see cref="PreviewGizmoMath.PickHandle"/> tests.</strong>
    /// Both take the same pivot and the same handle length, so a handle is grabbable precisely where
    /// it appears. Letting the drawing and the picking size themselves independently is how a gizmo
    /// ends up with an invisible dead zone.
    /// </para>
    /// </remarks>
    public sealed class PreviewTransformGizmo
    {
        private const int RotateRingSegments = 48;

        private static readonly Color AxisXColor = new Color(0.90f, 0.30f, 0.28f, 1f);
        private static readonly Color AxisYColor = new Color(0.40f, 0.85f, 0.35f, 1f);
        private static readonly Color AxisZColor = new Color(0.35f, 0.55f, 0.95f, 1f);
        private static readonly Color RotateColor = new Color(0.40f, 0.70f, 0.95f, 1f);
        private static readonly Color UniformColor = new Color(0.92f, 0.86f, 0.45f, 1f);
        private static readonly Color HighlightColor = new Color(1f, 0.92f, 0.35f, 1f);

        private readonly List<Vector3> vertexBuffer = new List<Vector3>();
        private readonly List<Color> colorBuffer = new List<Color>();
        private readonly List<int> indexBuffer = new List<int>();

        private GameObject gizmoObject;
        private Mesh gizmoMesh;
        private Material lineMaterial;

        /// <summary>The gizmo's root, or null before it is built.</summary>
        public GameObject GizmoObject
        {
            get { return gizmoObject; }
        }

        /// <summary>Creates the object and mesh if they do not exist. Cheap to call per frame.</summary>
        public void EnsureBuilt()
        {
            if (gizmoObject != null)
            {
                return;
            }

            gizmoMesh = new Mesh();
            gizmoMesh.name = "ClipPreviewGizmoMesh";
            gizmoMesh.hideFlags = HideFlags.HideAndDontSave;
            gizmoMesh.MarkDynamic();

            lineMaterial = PreviewLineMaterial.Create("ClipPreviewGizmo");

            gizmoObject = new GameObject("ClipPreviewGizmo");
            gizmoObject.hideFlags = HideFlags.HideAndDontSave;

            MeshFilter meshFilter = gizmoObject.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = gizmoMesh;

            MeshRenderer meshRenderer = gizmoObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = lineMaterial;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            gizmoObject.SetActive(false);
        }

        /// <summary>Hides the gizmo. Idempotent.</summary>
        public void Hide()
        {
            if (gizmoObject != null)
            {
                gizmoObject.SetActive(false);
            }
        }

        /// <summary>
        /// Rebuilds the handles for a mode at a pivot, highlighting the one being dragged.
        /// </summary>
        public void Rebuild(
            GizmoMode mode, Vector3 pivot, float handleLength, GizmoHandle activeHandle)
        {
            EnsureBuilt();
            if (gizmoMesh == null)
            {
                return;
            }

            vertexBuffer.Clear();
            colorBuffer.Clear();
            indexBuffer.Clear();

            switch (mode)
            {
                case GizmoMode.Rotate:
                    // One ring per axis, coloured like the axis it turns about, so which ring is
                    // which is readable without dragging one to find out.
                    AppendRotateRing(pivot, Vector3.right, handleLength,
                        activeHandle == GizmoHandle.RotateX ? HighlightColor : AxisXColor);
                    AppendRotateRing(pivot, Vector3.up, handleLength,
                        activeHandle == GizmoHandle.RotateY ? HighlightColor : AxisYColor);
                    AppendRotateRing(pivot, Vector3.forward, handleLength,
                        activeHandle == GizmoHandle.RotateZ ? HighlightColor : AxisZColor);
                    break;
                case GizmoMode.Scale:
                    AppendAxis(pivot, Vector3.right, handleLength,
                        activeHandle == GizmoHandle.AxisX ? HighlightColor : AxisXColor, true);
                    AppendAxis(pivot, Vector3.up, handleLength,
                        activeHandle == GizmoHandle.AxisY ? HighlightColor : AxisYColor, true);
                    AppendAxis(pivot, Vector3.forward, handleLength,
                        activeHandle == GizmoHandle.AxisZ ? HighlightColor : AxisZColor, true);
                    AppendBox(pivot, handleLength * 0.09f,
                        activeHandle == GizmoHandle.ScaleUniform ? HighlightColor : UniformColor);
                    break;
                default:
                    AppendAxis(pivot, Vector3.right, handleLength,
                        activeHandle == GizmoHandle.AxisX ? HighlightColor : AxisXColor, false);
                    AppendAxis(pivot, Vector3.up, handleLength,
                        activeHandle == GizmoHandle.AxisY ? HighlightColor : AxisYColor, false);
                    AppendAxis(pivot, Vector3.forward, handleLength,
                        activeHandle == GizmoHandle.AxisZ ? HighlightColor : AxisZColor, false);
                    break;
            }

            gizmoMesh.Clear();
            gizmoMesh.SetVertices(vertexBuffer);
            gizmoMesh.SetColors(colorBuffer);
            gizmoMesh.SetIndices(indexBuffer, MeshTopology.Lines, 0);
            gizmoMesh.RecalculateBounds();

            gizmoObject.SetActive(true);
        }

        private void AppendAxis(
            Vector3 pivot, Vector3 direction, float length, Color color, bool cappedWithBox)
        {
            Vector3 axisEnd = pivot + direction * length;
            AppendLine(pivot, axisEnd, color);

            if (cappedWithBox)
            {
                AppendBox(axisEnd, length * 0.07f, color);
                return;
            }

            // A simple arrowhead: two short strokes angled back along the axis, in whichever plane
            // is not degenerate for this direction.
            Vector3 sideways = Mathf.Abs(direction.z) > 0.5f ? Vector3.right : Vector3.forward;
            Vector3 perpendicular = Vector3.Cross(direction, sideways).normalized;
            if (perpendicular.sqrMagnitude < 1e-6f)
            {
                perpendicular = Vector3.up;
            }
            float headLength = length * 0.16f;
            AppendLine(axisEnd, axisEnd - direction * headLength + perpendicular * headLength * 0.5f, color);
            AppendLine(axisEnd, axisEnd - direction * headLength - perpendicular * headLength * 0.5f, color);
        }

        /// <summary>Draws a ring in the plane whose normal is <paramref name="planeNormal"/>.</summary>
        private void AppendRotateRing(Vector3 pivot, Vector3 planeNormal, float radius, Color color)
        {
            // The two in-plane axes are chosen to match PreviewGizmoMath.AngleAroundPivotDegrees, so
            // the ring a user grabs is measured the same way it is drawn.
            Vector3 firstAxis;
            Vector3 secondAxis;
            if (planeNormal == Vector3.right)
            {
                firstAxis = Vector3.up;
                secondAxis = Vector3.forward;
            }
            else if (planeNormal == Vector3.up)
            {
                firstAxis = Vector3.forward;
                secondAxis = Vector3.right;
            }
            else
            {
                firstAxis = Vector3.right;
                secondAxis = Vector3.up;
            }

            for (int segment = 0; segment < RotateRingSegments; segment++)
            {
                float startAngle = segment / (float)RotateRingSegments * Mathf.PI * 2f;
                float endAngle = (segment + 1) / (float)RotateRingSegments * Mathf.PI * 2f;
                AppendLine(
                    pivot + (firstAxis * Mathf.Cos(startAngle) + secondAxis * Mathf.Sin(startAngle)) * radius,
                    pivot + (firstAxis * Mathf.Cos(endAngle) + secondAxis * Mathf.Sin(endAngle)) * radius,
                    color);
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

        private void AppendLine(Vector3 start, Vector3 end, Color color)
        {
            indexBuffer.Add(vertexBuffer.Count);
            vertexBuffer.Add(start);
            colorBuffer.Add(color);

            indexBuffer.Add(vertexBuffer.Count);
            vertexBuffer.Add(end);
            colorBuffer.Add(color);
        }

        /// <summary>Destroys the gizmo object, mesh and material. Idempotent.</summary>
        public void Dispose()
        {
            if (gizmoObject != null)
            {
                Object.DestroyImmediate(gizmoObject);
                gizmoObject = null;
            }
            if (gizmoMesh != null)
            {
                Object.DestroyImmediate(gizmoMesh);
                gizmoMesh = null;
            }
            if (lineMaterial != null)
            {
                Object.DestroyImmediate(lineMaterial);
                lineMaterial = null;
            }
        }
    }
}
