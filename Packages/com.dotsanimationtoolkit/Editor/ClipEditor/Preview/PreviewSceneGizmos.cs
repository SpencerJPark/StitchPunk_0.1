// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// The clip viewport's permanent scene furniture: a reference grid and a selection marker, so a
    /// viewport always draws something rather than leaving an unselected clip looking uninitialised.
    /// Two grids — a floor in XZ at y=0, a backdrop in XY at z=0 rising from it — plus an origin
    /// marker, all as line meshes rather than <c>Handles</c>.
    /// </summary>
    public sealed class PreviewSceneGizmos
    {
        private const int GridHalfLineCount = 5;

        // One world unit per square — the height of Unity's default cube, so a square reads
        // directly as "half a character" without a scale factor.
        private const float GridCellSize = 1f;

        // How far the upright backdrop reaches above the floor, in world units — half the floor's
        // width, since the backdrop has no lower half to balance.
        private const float BackdropHeight = GridHalfLineCount * GridCellSize;

        /// <summary>Keeps a flat object's outline from collapsing to a zero-scale nothing.</summary>
        private const float MinimumSelectionExtent = 0.002f;

        /// <summary>How far the origin's axis stubs reach, in world units.</summary>
        private const float OriginMarkerLength = 0.35f;

        private static readonly Color GridLineColor = new Color(0.32f, 0.32f, 0.34f, 1f);

        // Dimmer than the backdrop's, so the floor recedes and the backdrop stays the one measured against.
        private static readonly Color FloorLineColor = new Color(0.24f, 0.24f, 0.26f, 1f);

        private static readonly Color HorizontalAxisColor = new Color(0.68f, 0.32f, 0.30f, 1f);
        private static readonly Color VerticalAxisColor = new Color(0.36f, 0.62f, 0.36f, 1f);
        private static readonly Color DepthAxisColor = new Color(0.30f, 0.48f, 0.76f, 1f);
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

        // Draws the selection outline as a box of the given world size, centre and orientation. An
        // oriented box, not axis-aligned, so it follows the object's rotation; each axis is clamped
        // to a minimum since a flat object has a zero-thickness bound that would collapse it.
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

                // Backdrop, in the XY plane at z = 0, standing ON the floor: it runs from y = 0
                // upward rather than being centred on the origin, so every square it measures is a
                // square of height above the ground.
                AddLine(
                    vertices, colors, indices,
                    new Vector3(offset, 0f, 0f), new Vector3(offset, BackdropHeight, 0f),
                    isCentreLine ? VerticalAxisColor : GridLineColor);

                // Floor, in the XZ plane at y = 0. The centre lines are left in the plain grid
                // colour: the axes through the origin are drawn once, by the origin marker, and
                // colouring them here as well would double them up in two different reds.
                AddLine(
                    vertices, colors, indices,
                    new Vector3(offset, 0f, -extent), new Vector3(offset, 0f, extent),
                    FloorLineColor);

                AddLine(
                    vertices, colors, indices,
                    new Vector3(-extent, 0f, offset), new Vector3(extent, 0f, offset),
                    FloorLineColor);

                // The backdrop's horizontal rules. Only the half at and above the floor exists, so
                // this runs over the positive indices rather than the full span the other lines do.
                if (lineIndex >= 0)
                {
                    AddLine(
                        vertices, colors, indices,
                        new Vector3(-extent, offset, 0f), new Vector3(extent, offset, 0f),
                        isCentreLine ? HorizontalAxisColor : GridLineColor);
                }
            }

            AddOriginMarker(vertices, colors, indices);
            return BuildLineMesh("ClipPreviewGridMesh", vertices, colors, indices);
        }

        // Three short axis stubs at 0,0,0, in the usual X-red, Y-green, Z-blue convention. Drawn in
        // the positive direction only, so the stubs say which way each axis runs.
        private static void AddOriginMarker(
            List<Vector3> vertices, List<Color> colors, List<int> indices)
        {
            AddLine(
                vertices, colors, indices,
                Vector3.zero, new Vector3(OriginMarkerLength, 0f, 0f), HorizontalAxisColor);
            AddLine(
                vertices, colors, indices,
                Vector3.zero, new Vector3(0f, OriginMarkerLength, 0f), VerticalAxisColor);
            AddLine(
                vertices, colors, indices,
                Vector3.zero, new Vector3(0f, 0f, OriginMarkerLength), DepthAxisColor);
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
