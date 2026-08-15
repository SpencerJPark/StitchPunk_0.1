// Copyright (c) 2026 Stitch Punk. All rights reserved.

using System.Collections.Generic;
using UnityEngine;

namespace StitchPunk.AnimationToolkit.Editor
{
    /// <summary>
    /// The clip viewport's permanent scene furniture: a reference grid and a selection marker.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>The grid is what makes the viewport a viewport rather than a symptom.</strong> Before
    /// it, an unselected clip rendered nothing, which is indistinguishable from a preview that has
    /// failed to initialise — and the two were routinely confused. A viewport that always draws
    /// something answers "is this thing alive?" without the user having to select anything to find
    /// out.
    /// </para>
    /// <para>
    /// <strong>Line meshes, not <c>Handles</c>.</strong> The packaging conformance scan forbids
    /// immediate-mode drawing anywhere in package Editor code, and the preview renders through a
    /// <c>PreviewRenderUtility</c> camera in its own scene, where an immediate-mode handle would
    /// have nothing to draw into anyway. A <see cref="MeshTopology.Lines"/> mesh is the same picture
    /// with none of that.
    /// </para>
    /// <para>
    /// The grid lies in the XY plane at z = 0 rather than being a ground plane. That is the plane
    /// cutout parts live in and the plane the default camera faces head-on, so it reads as graph
    /// paper behind the rig at the camera the window opens with. A floor grid would be edge-on and
    /// invisible until the user thought to orbit.
    /// </para>
    /// <para>
    /// Everything here carries <see cref="HideFlags.HideAndDontSave"/> and is destroyed by
    /// <see cref="Dispose"/>. The meshes and material are created rather than loaded, so they leak
    /// as native allocations across domain reloads if that is skipped.
    /// </para>
    /// </remarks>
    public sealed class PreviewSceneGizmos
    {
        private const int GridHalfLineCount = 5;
        private const float GridCellSize = 1f;

        /// <summary>Keeps a flat object's outline from collapsing to a zero-scale nothing.</summary>
        private const float MinimumSelectionExtent = 0.002f;

        private static readonly Color GridLineColor = new Color(0.32f, 0.32f, 0.34f, 1f);
        private static readonly Color HorizontalAxisColor = new Color(0.68f, 0.32f, 0.30f, 1f);
        private static readonly Color VerticalAxisColor = new Color(0.36f, 0.62f, 0.36f, 1f);
        private static readonly Color SelectionColor = new Color(0.98f, 0.72f, 0.24f, 1f);

        private GameObject gridObject;
        private GameObject selectionObject;
        private Transform selectionTransform;
        private Mesh gridMesh;
        private Mesh selectionMesh;
        private Material lineMaterial;

        /// <summary>The grid's root, or null before <see cref="EnsureBuilt"/> has run.</summary>
        public GameObject GridObject
        {
            get { return gridObject; }
        }

        /// <summary>The selection marker's root, or null before <see cref="EnsureBuilt"/> has run.</summary>
        public GameObject SelectionObject
        {
            get { return selectionObject; }
        }

        /// <summary>Creates the grid and marker if they do not exist yet. Cheap to call per frame.</summary>
        public void EnsureBuilt()
        {
            if (gridObject != null && selectionObject != null)
            {
                return;
            }

            EnsureLineMaterial();

            if (gridObject == null)
            {
                gridMesh = BuildGridMesh();
                gridObject = BuildLineObject("ClipPreviewGrid", gridMesh);
            }

            if (selectionObject == null)
            {
                selectionMesh = BuildWireCubeMesh();
                selectionObject = BuildLineObject("ClipPreviewSelection", selectionMesh);
                selectionTransform = selectionObject.transform;
                selectionObject.SetActive(false);
            }
        }

        /// <summary>
        /// Draws the selection outline as a box of the given world size, centre and orientation.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A box rather than a point marker so that selecting a mesh outlines <em>that mesh</em>:
        /// callers pass a renderer's local bounds mapped to world space, which makes the highlight
        /// an oriented bounding box that follows the object's rotation. A world-axis-aligned box
        /// would swing about as the rig turns and stop reading as "this object".
        /// </para>
        /// <para>
        /// Each axis is clamped to a minimum because a flat object — a cutout quad, a plane — has a
        /// zero-thickness bound, and a zero scale collapses the outline to nothing at all.
        /// </para>
        /// </remarks>
        public void ShowSelection(Vector3 worldCenter, Quaternion worldRotation, Vector3 worldSize)
        {
            if (selectionObject == null)
            {
                return;
            }
            selectionObject.SetActive(true);
            selectionTransform.position = worldCenter;
            selectionTransform.rotation = worldRotation;
            selectionTransform.localScale = new Vector3(
                Mathf.Max(Mathf.Abs(worldSize.x), MinimumSelectionExtent),
                Mathf.Max(Mathf.Abs(worldSize.y), MinimumSelectionExtent),
                Mathf.Max(Mathf.Abs(worldSize.z), MinimumSelectionExtent));
        }

        /// <summary>Hides the selection marker. Idempotent.</summary>
        public void HideSelection()
        {
            if (selectionObject != null)
            {
                selectionObject.SetActive(false);
            }
        }

        private void EnsureLineMaterial()
        {
            if (lineMaterial == null)
            {
                lineMaterial = PreviewLineMaterial.Create("ClipPreviewLines");
            }
        }

        private GameObject BuildLineObject(string objectName, Mesh mesh)
        {
            GameObject lineObject = new GameObject(objectName);
            lineObject.hideFlags = HideFlags.HideAndDontSave;

            MeshFilter meshFilter = lineObject.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = mesh;

            MeshRenderer meshRenderer = lineObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = lineMaterial;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            return lineObject;
        }

        private static Mesh BuildGridMesh()
        {
            List<Vector3> vertices = new List<Vector3>();
            List<Color> colors = new List<Color>();
            List<int> indices = new List<int>();

            float extent = GridHalfLineCount * GridCellSize;
            for (int lineIndex = -GridHalfLineCount; lineIndex <= GridHalfLineCount; lineIndex++)
            {
                float offset = lineIndex * GridCellSize;
                bool isCentreLine = lineIndex == 0;

                AddLine(
                    vertices, colors, indices,
                    new Vector3(offset, -extent, 0f), new Vector3(offset, extent, 0f),
                    isCentreLine ? VerticalAxisColor : GridLineColor);

                AddLine(
                    vertices, colors, indices,
                    new Vector3(-extent, offset, 0f), new Vector3(extent, offset, 0f),
                    isCentreLine ? HorizontalAxisColor : GridLineColor);
            }

            return BuildLineMesh("ClipPreviewGridMesh", vertices, colors, indices);
        }

        /// <summary>A unit cube's twelve edges, centred on the origin.</summary>
        private static Mesh BuildWireCubeMesh()
        {
            List<Vector3> vertices = new List<Vector3>();
            List<Color> colors = new List<Color>();
            List<int> indices = new List<int>();

            Vector3[] corners = new Vector3[8];
            for (int cornerIndex = 0; cornerIndex < 8; cornerIndex++)
            {
                corners[cornerIndex] = new Vector3(
                    (cornerIndex & 1) == 0 ? -0.5f : 0.5f,
                    (cornerIndex & 2) == 0 ? -0.5f : 0.5f,
                    (cornerIndex & 4) == 0 ? -0.5f : 0.5f);
            }

            // Two corners share an edge exactly when their indices differ in one bit, which is what
            // makes the bit-pattern corner table above worth having.
            for (int firstCorner = 0; firstCorner < 8; firstCorner++)
            {
                for (int bit = 1; bit <= 4; bit *= 2)
                {
                    int secondCorner = firstCorner | bit;
                    if (secondCorner == firstCorner)
                    {
                        continue;
                    }
                    AddLine(vertices, colors, indices, corners[firstCorner], corners[secondCorner], SelectionColor);
                }
            }

            return BuildLineMesh("ClipPreviewSelectionMesh", vertices, colors, indices);
        }

        private static void AddLine(
            List<Vector3> vertices, List<Color> colors, List<int> indices,
            Vector3 start, Vector3 end, Color lineColor)
        {
            indices.Add(vertices.Count);
            vertices.Add(start);
            colors.Add(lineColor);

            indices.Add(vertices.Count);
            vertices.Add(end);
            colors.Add(lineColor);
        }

        private static Mesh BuildLineMesh(
            string meshName, List<Vector3> vertices, List<Color> colors, List<int> indices)
        {
            Mesh mesh = new Mesh();
            mesh.name = meshName;
            mesh.hideFlags = HideFlags.HideAndDontSave;
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetIndices(indices, MeshTopology.Lines, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Destroys the grid, the marker, their meshes and their material. Idempotent.</summary>
        public void Dispose()
        {
            DestroyIfPresent(gridObject);
            gridObject = null;

            DestroyIfPresent(selectionObject);
            selectionObject = null;
            selectionTransform = null;

            DestroyIfPresent(gridMesh);
            gridMesh = null;

            DestroyIfPresent(selectionMesh);
            selectionMesh = null;

            DestroyIfPresent(lineMaterial);
            lineMaterial = null;
        }

        private static void DestroyIfPresent(Object target)
        {
            if (target != null)
            {
                Object.DestroyImmediate(target);
            }
        }
    }
}
