// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    public sealed class MaterialPreviewElement : VisualElement, IDisposable
    {
        private const string SubjectObjectName = "MaterialPreviewSubject";

        // A domain reload clears this while the HideAndDontSave subjects survive in the preview
        // scene, so any subject missing from here is stranded and gets swept.
        private static readonly HashSet<GameObject> OwnedSubjects = new HashSet<GameObject>();

        private PreviewRenderUtility renderUtility;
        private readonly MaterialPreviewCameraRig cameraRig = new MaterialPreviewCameraRig();
        private readonly PreviewCameraNavigation cameraNavigation = new PreviewCameraNavigation();
        private readonly PreviewSceneGizmos sceneGizmos = new PreviewSceneGizmos();
        private bool sceneGizmosAdded;

        private readonly ViewportFrameElement frame;
        private readonly Image viewportImage;

        private GameObject subjectObject;
        private MeshFilter subjectMeshFilter;
        private MeshRenderer subjectRenderer;
        private Material neutralSurfaceMaterial;
        private float lastTickTimeSinceStartup;

        public Mesh ShownMesh { get; private set; }
        public Material ShownMaterial { get; private set; }

        public MaterialPreviewElement()
        {
            name = "material-preview-element";
            style.flexShrink = 0f;

            frame = new ViewportFrameElement();
            frame.name = "material-preview-frame";
            frame.style.height = 300f;
            UnityEditor.UIElements.ToolbarButton resetCameraButton =
                frame.AddResetCameraButton(() => cameraNavigation.ResetView());
            resetCameraButton.name = "material-preview-reset-camera-button";
            resetCameraButton.tooltip = "Return: frame the whole mesh again (F)";
            frame.SetEmptyState("material-preview-empty", "Nothing to preview", "Pick a material, cutout or mesh.");
            frame.ShowEmptyState(true);

            viewportImage = frame.ViewportImage;
            Add(frame);
            Label hintLabel = ToolkitChrome.MakeHint(
                "Drag: orbit · Right-drag + WASD/QE: fly (Shift fast) · Middle-drag: pan · Wheel: zoom · F: return");
            hintLabel.name = "material-preview-hint";
            Add(hintLabel);

            cameraNavigation.Rig = cameraRig;
            cameraNavigation.AttachTo(viewportImage);

            RegisterCallback<AttachToPanelEvent>(attachEvent => EditorApplication.update += Tick);
            RegisterCallback<DetachFromPanelEvent>(detachEvent => EditorApplication.update -= Tick);
        }

        public void Show(Mesh mesh, Material material)
        {
            bool nothingToShow = mesh == null && material == null;
            frame.ShowEmptyState(nothingToShow);

            bool meshChanged = mesh != ShownMesh || subjectObject == null;
            ShownMesh = mesh;
            ShownMaterial = material;
            if (nothingToShow)
            {
                if (subjectRenderer != null)
                {
                    subjectRenderer.enabled = false;
                }
                return;
            }

            EnsureRenderUtility();
            EnsureSubject();

            Mesh meshToDraw = mesh != null ? mesh : Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            subjectMeshFilter.sharedMesh = meshToDraw;
            Material materialToDraw = material != null ? material : GetNeutralSurfaceMaterial();
            Material[] materialPerSubMesh = new Material[Mathf.Max(meshToDraw.subMeshCount, 1)];
            for (int subMeshIndex = 0; subMeshIndex < materialPerSubMesh.Length; subMeshIndex++)
            {
                materialPerSubMesh[subMeshIndex] = materialToDraw;
            }
            subjectRenderer.sharedMaterials = materialPerSubMesh;
            subjectRenderer.enabled = true;

            if (meshChanged)
            {
                // mesh.bounds, not renderer.bounds: the renderer's bounds can be stale right after a swap.
                cameraRig.SetFrameTarget(meshToDraw.bounds);
                cameraRig.ResetView();
            }
        }

        private Material GetNeutralSurfaceMaterial()
        {
            if (neutralSurfaceMaterial == null)
            {
                neutralSurfaceMaterial = PreviewSurfaceMaterialResolver.CreateNeutralSurfaceMaterial();
            }
            return neutralSurfaceMaterial;
        }

        private void EnsureSubject()
        {
            if (subjectObject != null)
            {
                return;
            }
            SweepStrandedSubjects();

            subjectObject = new GameObject(SubjectObjectName);
            subjectObject.hideFlags = HideFlags.HideAndDontSave;
            OwnedSubjects.Add(subjectObject);
            subjectMeshFilter = subjectObject.AddComponent<MeshFilter>();
            subjectRenderer = subjectObject.AddComponent<MeshRenderer>();
            subjectObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            renderUtility.AddSingleGO(subjectObject);
        }

        private void Tick()
        {
            float nowSinceStartup = (float)EditorApplication.timeSinceStartup;
            float deltaSeconds = lastTickTimeSinceStartup > 0f
                ? nowSinceStartup - lastTickTimeSinceStartup
                : 0f;
            lastTickTimeSinceStartup = nowSinceStartup;

            cameraNavigation.StepFly(deltaSeconds);
            RenderViewport();
        }

        private void RenderViewport()
        {
            if (viewportImage == null)
            {
                return;
            }
            Rect viewportRect = viewportImage.contentRect;
            if (float.IsNaN(viewportRect.width) || viewportRect.width < 1f || viewportRect.height < 1f)
            {
                return;
            }

            EnsureRenderUtility();

            sceneGizmos.EnsureBuilt();
            if (!sceneGizmosAdded && sceneGizmos.GridObject != null && sceneGizmos.SelectionObject != null)
            {
                renderUtility.AddSingleGO(sceneGizmos.GridObject);
                renderUtility.AddSingleGO(sceneGizmos.SelectionObject);
                sceneGizmosAdded = true;
            }

            // Grid cells scale to the power of ten nearest the subject so a tiny or huge mesh keeps a usable floor.
            if (sceneGizmos.GridObject != null)
            {
                float gridScale = Mathf.Pow(10f, Mathf.Round(Mathf.Log10(cameraRig.SubjectRadius)));
                sceneGizmos.GridObject.transform.localScale = Vector3.one * gridScale;
            }

            renderUtility.BeginPreview(viewportRect, GUIStyle.none);
            cameraRig.ApplyTo(renderUtility.camera);
            renderUtility.camera.nearClipPlane = cameraRig.SuggestedNearClip;
            renderUtility.camera.farClipPlane = cameraRig.SuggestedFarClip;
            renderUtility.camera.Render();
            viewportImage.image = renderUtility.EndPreview();
            viewportImage.MarkDirtyRepaint();
        }

        private void EnsureRenderUtility()
        {
            if (renderUtility != null)
            {
                return;
            }
            renderUtility = new PreviewRenderUtility();
            renderUtility.camera.fieldOfView = 45f;
            renderUtility.camera.nearClipPlane = 0.1f;
            renderUtility.camera.farClipPlane = 200f;
            renderUtility.camera.clearFlags = CameraClearFlags.SolidColor;
            renderUtility.camera.backgroundColor = new Color(0.17f, 0.17f, 0.18f, 1f);
            renderUtility.ambientColor = new Color(0.45f, 0.45f, 0.45f, 1f);
            renderUtility.lights[0].intensity = 1.1f;
            renderUtility.lights[0].transform.rotation = Quaternion.Euler(40f, 40f, 0f);
            renderUtility.lights[1].intensity = 0.5f;
            renderUtility.lights[1].transform.rotation = Quaternion.Euler(-20f, -110f, 0f);
        }

        // GameObject.Find cannot see a HideAndDontSave object, so only FindObjectsOfTypeAll reaches strays.
        private static void SweepStrandedSubjects()
        {
            OwnedSubjects.RemoveWhere(ownedSubject => ownedSubject == null);
            GameObject[] allObjects = Resources.FindObjectsOfTypeAll<GameObject>();
            for (int objectIndex = 0; objectIndex < allObjects.Length; objectIndex++)
            {
                GameObject candidate = allObjects[objectIndex];
                if (candidate != null
                    && candidate.name == SubjectObjectName
                    && !OwnedSubjects.Contains(candidate))
                {
                    UnityEngine.Object.DestroyImmediate(candidate);
                }
            }
        }

        public void Dispose()
        {
            EditorApplication.update -= Tick;
            if (subjectObject != null)
            {
                OwnedSubjects.Remove(subjectObject);
                UnityEngine.Object.DestroyImmediate(subjectObject);
            }
            subjectObject = null;
            subjectMeshFilter = null;
            subjectRenderer = null;
            if (neutralSurfaceMaterial != null)
            {
                UnityEngine.Object.DestroyImmediate(neutralSurfaceMaterial);
                neutralSurfaceMaterial = null;
            }
            sceneGizmos.Dispose();
            sceneGizmosAdded = false;
            if (renderUtility != null)
            {
                renderUtility.Cleanup();
                renderUtility = null;
            }
        }
    }
}
