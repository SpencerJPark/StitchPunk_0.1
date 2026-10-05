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

        private const float FramesPerSecond = 12f;
        private static readonly int BillboardParamsPropertyId = Shader.PropertyToID("_BillboardParams");
        private static readonly int ImageIndexPropertyId = Shader.PropertyToID("_ImageIndex");
        private static readonly int MainTexArrayPropertyId = Shader.PropertyToID("_MainTexArray");

        private readonly MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();
        private readonly VisualElement frameRow;
        private readonly Label frameLabel;
        private readonly SliderInt frameSlider;
        private readonly Button framePlayButton;
        private int frameCount;
        private int frameIndex;
        private bool framesPlaying;
        private double nextFrameAdvanceTime;

        public Mesh ShownMesh { get; private set; }
        public Material ShownMaterial { get; private set; }

        public MaterialPreviewElement()
        {
            name = "material-preview-element";
            style.flexGrow = 1f;

            frame = new ViewportFrameElement();
            frame.name = "material-preview-frame";
            frame.style.flexGrow = 1f;
            UnityEditor.UIElements.ToolbarButton resetCameraButton =
                frame.AddResetCameraButton(() => cameraNavigation.ResetView());
            resetCameraButton.name = "material-preview-reset-camera-button";
            resetCameraButton.tooltip = "Return: frame the whole mesh again (F)";
            frame.SetEmptyState("material-preview-empty", "Nothing to preview", "Pick a material, cutout or mesh.");
            frame.ShowEmptyState(true);

            viewportImage = frame.ViewportImage;
            Add(frame);

            frameRow = new VisualElement { name = "material-preview-frame-row" };
            frameRow.style.flexDirection = FlexDirection.Row;
            frameRow.style.alignItems = Align.Center;
            frameRow.Add(ToolkitChrome.MakeIconSquare(() => StepFrame(-1), "d_Animation.PrevKey", "Previous frame"));
            frameLabel = new Label { name = "material-preview-frame-label" };
            frameLabel.AddToClassList("cutouts-frame-label");
            frameRow.Add(frameLabel);
            frameRow.Add(ToolkitChrome.MakeIconSquare(() => StepFrame(1), "d_Animation.NextKey", "Next frame"));
            framePlayButton = ToolkitChrome.MakeIconSquare(ToggleFramesPlaying, "d_PlayButton", "Play the frames at 12 fps");
            framePlayButton.name = "material-preview-frame-play";
            frameRow.Add(framePlayButton);
            frameSlider = new SliderInt(0, 0) { name = "material-preview-frame-slider" };
            frameSlider.style.flexGrow = 1f;
            frameSlider.RegisterValueChangedCallback(
                (ChangeEvent<int> changeEvent) => SetFrameIndex(changeEvent.newValue, false));
            frameRow.Add(frameSlider);
            frameRow.style.display = DisplayStyle.None;
            Add(frameRow);
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
            if (material != ShownMaterial)
            {
                frameIndex = 0;
                SetFramesPlaying(false);
            }
            ShownMesh = mesh;
            ShownMaterial = material;
            if (nothingToShow)
            {
                if (subjectRenderer != null)
                {
                    subjectRenderer.enabled = false;
                }
                RefreshFrameRow(null);
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
            RefreshFrameRow(material);

            if (meshChanged)
            {
                // mesh.bounds, not renderer.bounds: the renderer's bounds can be stale right after a swap.
                cameraRig.SetFrameTarget(meshToDraw.bounds);
                cameraRig.ResetView();
            }
        }

        private void RefreshFrameRow(Material material)
        {
            Texture2DArray frameArray = null;
            if (material != null && material.HasProperty(MainTexArrayPropertyId) && material.HasProperty(ImageIndexPropertyId))
            {
                frameArray = material.GetTexture(MainTexArrayPropertyId) as Texture2DArray;
            }
            frameCount = frameArray != null ? frameArray.depth : 0;
            bool hasFrames = frameCount > 0;
            frameRow.style.display = hasFrames ? DisplayStyle.Flex : DisplayStyle.None;
            if (!hasFrames)
            {
                SetFramesPlaying(false);
            }
            frameIndex = hasFrames ? Mathf.Clamp(frameIndex, 0, frameCount - 1) : 0;
            frameSlider.lowValue = 0;
            frameSlider.highValue = Mathf.Max(frameCount - 1, 0);
            frameSlider.SetValueWithoutNotify(frameIndex);
            frameLabel.text = (frameIndex + 1) + " / " + frameCount;
            ApplyPropertyBlock();
        }

        private void StepFrame(int direction)
        {
            if (frameCount <= 0)
            {
                return;
            }
            SetFrameIndex((frameIndex + direction + frameCount) % frameCount, true);
        }

        private void SetFrameIndex(int newIndex, bool updateSlider)
        {
            frameIndex = Mathf.Clamp(newIndex, 0, Mathf.Max(frameCount - 1, 0));
            if (updateSlider)
            {
                frameSlider.SetValueWithoutNotify(frameIndex);
            }
            frameLabel.text = (frameIndex + 1) + " / " + frameCount;
            ApplyPropertyBlock();
        }

        private void ToggleFramesPlaying()
        {
            SetFramesPlaying(!framesPlaying);
        }

        private void SetFramesPlaying(bool playing)
        {
            if (framesPlaying == playing)
            {
                return;
            }
            framesPlaying = playing;
            nextFrameAdvanceTime = EditorApplication.timeSinceStartup + 1.0 / FramesPerSecond;
            ToolkitIcons.SetButtonIcon(framePlayButton, playing ? "d_PauseButton" : "d_PlayButton", "•");
            framePlayButton.tooltip = playing ? "Pause" : "Play the frames at 12 fps";
        }

        // The preview draws the real material asset, so billboarding and the frame index are overridden per renderer only.
        private void ApplyPropertyBlock()
        {
            if (subjectRenderer == null)
            {
                return;
            }
            propertyBlock.Clear();
            Material shownMaterial = ShownMaterial;
            if (shownMaterial != null)
            {
                if (shownMaterial.HasProperty(BillboardParamsPropertyId))
                {
                    propertyBlock.SetVector(BillboardParamsPropertyId, Vector4.zero);
                }
                if (frameCount > 0)
                {
                    propertyBlock.SetFloat(ImageIndexPropertyId, frameIndex);
                }
            }
            subjectRenderer.SetPropertyBlock(propertyBlock);
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

            if (framesPlaying && frameCount > 1 && EditorApplication.timeSinceStartup >= nextFrameAdvanceTime)
            {
                nextFrameAdvanceTime = EditorApplication.timeSinceStartup + 1.0 / FramesPerSecond;
                StepFrame(1);
            }

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
