// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Draws every cutscene mark in the Scene view as a tolerance disc, and lets one be clicked and
    /// dragged along its own ground plane (amendment A64 §3.4).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Line meshes and a ray, not <c>Handles</c>.</strong> A64 §3.4 asked for
    /// <c>Handles.DrawWireDisc</c> and <c>Handles.PositionHandle</c>; <c>Conformance_E</c> bans
    /// every <c>Handles.</c> call in this package's Editor sources, so the disc is the same line
    /// mesh <see cref="PreviewSceneGizmos"/> already draws its grid with and the drag is a ray
    /// against the mark's own Y plane. A mark is a spot on the ground, so a planar drag is the
    /// motion an author wants anyway — height stays wherever it was authored instead of being one
    /// stray axis pull away from a mark nobody can walk to.
    /// </para>
    /// <para>
    /// The <c>duringSceneGui</c> subscription is the leak to watch: one that outlives the panel
    /// keeps drawing against a disposed <see cref="SerializedObject"/> and survives domain reloads
    /// badly, so <see cref="Disable"/> is called from the panel's hide path as well as its detach.
    /// </para>
    /// </remarks>
    internal sealed class CutsceneMarkSceneOverlay
    {
        /// <summary>Smallest clickable radius, so a 5 cm tolerance is still something a mouse can hit.</summary>
        private const float MinimumPickRadius = 0.35f;

        private static readonly Color UnselectedColor = new Color(0.45f, 0.65f, 0.95f, 0.9f);
        private static readonly Color SelectedColor = new Color(1f, 0.82f, 0.25f, 1f);

        private CutsceneAsset cutscene;
        private SerializedObject serializedObject;
        private int selectedSlotIndex = -1;
        private int selectedMarkIndex = -1;

        private bool isEnabled;
        private bool isDragging;
        private int draggedSlotIndex = -1;
        private int draggedMarkIndex = -1;

        private Mesh discMesh;
        private Material lineMaterial;

        /// <summary>Raised when a click lands on a mark: (slot index, mark index).</summary>
        public Action<int, int> MarkClicked;

        /// <summary>Raised while and after a drag; <c>true</c> on mouse-up, when the panel may afford a full rebuild.</summary>
        public Action<bool> MarkDragged;

        public void SetSource(CutsceneAsset cutsceneAsset, SerializedObject cutsceneSerializedObject)
        {
            cutscene = cutsceneAsset;
            serializedObject = cutsceneSerializedObject;
        }

        public void SetSelection(int slotIndex, int markIndex)
        {
            selectedSlotIndex = slotIndex;
            selectedMarkIndex = markIndex;
        }

        public void Enable()
        {
            if (isEnabled)
            {
                return;
            }
            SceneView.duringSceneGui += OnSceneGui;
            isEnabled = true;
            SceneView.RepaintAll();
        }

        public void Disable()
        {
            if (!isEnabled)
            {
                return;
            }
            SceneView.duringSceneGui -= OnSceneGui;
            isEnabled = false;
            isDragging = false;
            DestroyDrawingResources();
            SceneView.RepaintAll();
        }

        private void OnSceneGui(SceneView sceneView)
        {
            if (cutscene == null || cutscene.slots == null)
            {
                return;
            }

            Event currentEvent = Event.current;
            if (currentEvent.type == EventType.Repaint)
            {
                DrawAllMarks();
                return;
            }

            if (currentEvent.button != 0 || currentEvent.alt)
            {
                return;
            }

            if (currentEvent.type == EventType.MouseDown)
            {
                BeginDragIfAMarkWasHit(currentEvent);
                return;
            }
            if (currentEvent.type == EventType.MouseDrag && isDragging)
            {
                DragToRay(currentEvent, isFinished: false);
                currentEvent.Use();
                return;
            }
            if (currentEvent.type == EventType.MouseUp && isDragging)
            {
                DragToRay(currentEvent, isFinished: true);
                isDragging = false;
                currentEvent.Use();
            }
        }

        private void DrawAllMarks()
        {
            EnsureDrawingResources();
            for (int slotIndex = 0; slotIndex < cutscene.slots.Count; slotIndex++)
            {
                CutsceneSlot slot = cutscene.slots[slotIndex];
                if (slot == null || slot.markKeys == null)
                {
                    continue;
                }
                for (int markIndex = 0; markIndex < slot.markKeys.Count; markIndex++)
                {
                    CutsceneMarkKey mark = slot.markKeys[markIndex];
                    bool isSelected = slotIndex == selectedSlotIndex && markIndex == selectedMarkIndex;
                    lineMaterial.SetColor("_Color", isSelected ? SelectedColor : UnselectedColor);
                    lineMaterial.SetPass(0);
                    Graphics.DrawMeshNow(discMesh, Matrix4x4.TRS(
                        new Vector3(mark.position.x, mark.position.y, mark.position.z),
                        Quaternion.Euler(0f, mark.facingDegrees, 0f),
                        Vector3.one * Mathf.Max(MinimumPickRadius, mark.toleranceMeters)));
                }
            }
        }

        private void BeginDragIfAMarkWasHit(Event currentEvent)
        {
            int hitSlotIndex;
            int hitMarkIndex;
            if (!TryPickMark(currentEvent.mousePosition, out hitSlotIndex, out hitMarkIndex))
            {
                return;
            }

            // One snapshot for the whole drag, so the author undoes a move rather than a hundred
            // of them. Every write below goes straight to the asset, never through
            // ApplyModifiedProperties, which would register an undo entry per drag frame.
            Undo.RegisterCompleteObjectUndo(cutscene, "Move Cutscene Mark");
            isDragging = true;
            draggedSlotIndex = hitSlotIndex;
            draggedMarkIndex = hitMarkIndex;
            if (MarkClicked != null)
            {
                MarkClicked(hitSlotIndex, hitMarkIndex);
            }
            currentEvent.Use();
        }

        private void DragToRay(Event currentEvent, bool isFinished)
        {
            CutsceneMarkKey mark = cutscene.slots[draggedSlotIndex].markKeys[draggedMarkIndex];
            Vector3 hitPoint;
            if (TryIntersectGroundPlane(currentEvent.mousePosition, mark.position.y, out hitPoint))
            {
                mark.position = new Unity.Mathematics.float3(hitPoint.x, mark.position.y, hitPoint.z);
                cutscene.slots[draggedSlotIndex].markKeys[draggedMarkIndex] = mark;
                EditorUtility.SetDirty(cutscene);
                // The panel's inspector fields are bound to this object; without the pull they would
                // not move until the drag ended.
                if (serializedObject != null)
                {
                    serializedObject.Update();
                }
            }

            if (MarkDragged != null)
            {
                MarkDragged(isFinished);
            }
        }

        private bool TryPickMark(Vector2 mousePosition, out int hitSlotIndex, out int hitMarkIndex)
        {
            hitSlotIndex = -1;
            hitMarkIndex = -1;
            float nearestSquaredDistance = float.MaxValue;

            for (int slotIndex = 0; slotIndex < cutscene.slots.Count; slotIndex++)
            {
                CutsceneSlot slot = cutscene.slots[slotIndex];
                if (slot == null || slot.markKeys == null)
                {
                    continue;
                }
                for (int markIndex = 0; markIndex < slot.markKeys.Count; markIndex++)
                {
                    CutsceneMarkKey mark = slot.markKeys[markIndex];
                    Vector3 hitPoint;
                    if (!TryIntersectGroundPlane(mousePosition, mark.position.y, out hitPoint))
                    {
                        continue;
                    }
                    float pickRadius = Mathf.Max(MinimumPickRadius, mark.toleranceMeters);
                    Vector2 planarOffset = new Vector2(hitPoint.x - mark.position.x, hitPoint.z - mark.position.z);
                    float squaredDistance = planarOffset.sqrMagnitude;
                    if (squaredDistance <= pickRadius * pickRadius && squaredDistance < nearestSquaredDistance)
                    {
                        nearestSquaredDistance = squaredDistance;
                        hitSlotIndex = slotIndex;
                        hitMarkIndex = markIndex;
                    }
                }
            }
            return hitSlotIndex >= 0;
        }

        private static bool TryIntersectGroundPlane(Vector2 mousePosition, float planeHeight, out Vector3 hitPoint)
        {
            Ray pickRay = HandleUtility.GUIPointToWorldRay(mousePosition);
            Plane groundPlane = new Plane(Vector3.up, new Vector3(0f, planeHeight, 0f));
            float distanceAlongRay;
            if (!groundPlane.Raycast(pickRay, out distanceAlongRay))
            {
                hitPoint = Vector3.zero;
                return false;
            }
            hitPoint = pickRay.GetPoint(distanceAlongRay);
            return true;
        }

        private void EnsureDrawingResources()
        {
            if (lineMaterial == null)
            {
                lineMaterial = new Material(Shader.Find("Hidden/Internal-Colored"));
                lineMaterial.hideFlags = HideFlags.HideAndDontSave;
                lineMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                lineMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                lineMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                lineMaterial.SetInt("_ZWrite", 0);
                // Drawn through geometry on purpose: a mark on the far side of a wall is still a
                // spot the author is placing, and an invisible one reads as a missing mark.
                lineMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            }
            if (discMesh == null)
            {
                discMesh = BuildUnitDiscMesh();
            }
        }

        private void DestroyDrawingResources()
        {
            if (lineMaterial != null)
            {
                UnityEngine.Object.DestroyImmediate(lineMaterial);
                lineMaterial = null;
            }
            if (discMesh != null)
            {
                UnityEngine.Object.DestroyImmediate(discMesh);
                discMesh = null;
            }
        }

        /// <summary>A unit-radius ring in the XZ plane plus a tick along +Z, which the mark's facing rotates.</summary>
        private static Mesh BuildUnitDiscMesh()
        {
            const int SegmentCount = 48;
            Vector3[] vertices = new Vector3[SegmentCount + 2];
            int[] indices = new int[SegmentCount * 2 + 2];

            for (int i = 0; i < SegmentCount; i++)
            {
                float angle = i * 2f * Mathf.PI / SegmentCount;
                vertices[i] = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                indices[i * 2] = i;
                indices[i * 2 + 1] = (i + 1) % SegmentCount;
            }
            vertices[SegmentCount] = Vector3.zero;
            vertices[SegmentCount + 1] = new Vector3(0f, 0f, 1f);
            indices[SegmentCount * 2] = SegmentCount;
            indices[SegmentCount * 2 + 1] = SegmentCount + 1;

            Color[] colors = new Color[vertices.Length];
            for (int i = 0; i < colors.Length; i++)
            {
                colors[i] = Color.white;
            }

            Mesh mesh = new Mesh();
            mesh.name = "CutsceneMarkDisc";
            mesh.hideFlags = HideFlags.HideAndDontSave;
            mesh.vertices = vertices;
            mesh.colors = colors;
            mesh.SetIndices(indices, MeshTopology.Lines, 0);
            return mesh;
        }
    }
}
