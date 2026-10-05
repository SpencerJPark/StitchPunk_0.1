// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    public sealed class MaterialCheckPreviewElement : VisualElement, IDisposable
    {
        private sealed class CheckedPart
        {
            public Renderer CopiedRenderer;
            public Material[] AuthoredMaterials;
        }

        private const string SourceCopyObjectName = "MaterialCheckPreviewSourceCopy";
        private static readonly int BillboardParamsPropertyId = Shader.PropertyToID("_BillboardParams");

        // A domain reload clears this while the HideAndDontSave copies survive in the preview scene,
        // so any copy missing from here is stranded and gets swept.
        private static readonly HashSet<GameObject> OwnedSourceCopies = new HashSet<GameObject>();

        private PreviewRenderUtility renderUtility;
        private readonly MaterialPreviewCameraRig cameraRig = new MaterialPreviewCameraRig();
        private readonly PreviewCameraNavigation cameraNavigation = new PreviewCameraNavigation();
        private readonly PreviewSceneGizmos sceneGizmos = new PreviewSceneGizmos();
        private bool sceneGizmosAdded;

        private readonly ViewportFrameElement frame;
        private readonly Image viewportImage;

        private GameObject prefabCopyRoot;
        private readonly Dictionary<string, CheckedPart> partsBySourcePath = new Dictionary<string, CheckedPart>();
        private readonly HashSet<string> flaggedNodePaths = new HashSet<string>();
        private readonly MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();
        private Material redOverlayMaterial;
        private readonly Dictionary<Texture, Material> redOverlayMaterialsByTexture = new Dictionary<Texture, Material>();
        private float lastTickTimeSinceStartup;

        public MaterialCheckPreviewElement()
        {
            name = "material-check-preview-element";
            style.flexGrow = 1f;

            frame = new ViewportFrameElement();
            frame.name = "material-check-preview-frame";
            frame.style.flexGrow = 1f;
            UnityEditor.UIElements.ToolbarButton resetCameraButton =
                frame.AddResetCameraButton(() => cameraNavigation.ResetView());
            resetCameraButton.name = "material-check-preview-reset-camera-button";
            resetCameraButton.tooltip = "Return: frame the whole rig again (F)";
            frame.SetEmptyState("material-check-preview-empty", "No rig", "Pick a rig to see its parts.");
            frame.ShowEmptyState(true);

            viewportImage = frame.ViewportImage;
            Add(frame);

            Label hintLabel = ToolkitChrome.MakeHint(
                "Drag: orbit Â· Right-drag + WASD/QE: fly (Shift fast) Â· Middle-drag: pan Â· Wheel: zoom Â· F: return");
            hintLabel.name = "material-check-preview-hint";
            Add(hintLabel);

            cameraNavigation.Rig = cameraRig;
            cameraNavigation.AttachTo(viewportImage);

            RegisterCallback<AttachToPanelEvent>(attachEvent => EditorApplication.update += Tick);
            RegisterCallback<DetachFromPanelEvent>(detachEvent => EditorApplication.update -= Tick);
        }

        public void ShowRig(RigAsset rig)
        {
            GameObject sourcePrefab = rig != null ? rig.sourcePrefab : null;
            RebuildPrefabCopy(sourcePrefab);
            frame.ShowEmptyState(prefabCopyRoot == null);
            if (prefabCopyRoot == null)
            {
                return;
            }
            ApplyFlaggedAppearance();
            cameraRig.SetFrameTarget(MeasurePrefabCopyBounds());
            cameraRig.ResetView();
        }

        public void SetFlaggedNodes(IReadOnlyCollection<string> sourceNodePaths)
        {
            flaggedNodePaths.Clear();
            if (sourceNodePaths != null)
            {
                foreach (string sourceNodePath in sourceNodePaths)
                {
                    if (!string.IsNullOrEmpty(sourceNodePath))
                    {
                        flaggedNodePaths.Add(sourceNodePath);
                    }
                }
            }
            ApplyFlaggedAppearance();
        }

        public void FocusNode(string sourceNodePath)
        {
            CheckedPart focusedPart;
            if (string.IsNullOrEmpty(sourceNodePath)
                || !partsBySourcePath.TryGetValue(sourceNodePath, out focusedPart)
                || focusedPart.CopiedRenderer == null)
            {
                sceneGizmos.HideSelection();
                if (prefabCopyRoot != null)
                {
                    cameraRig.SetFrameTarget(MeasurePrefabCopyBounds());
                }
                return;
            }

            Bounds focusedBounds = focusedPart.CopiedRenderer.bounds;
            sceneGizmos.EnsureBuilt();
            sceneGizmos.ShowSelection(focusedBounds.center, Quaternion.identity, focusedBounds.size);
            cameraRig.SetFrameTarget(focusedBounds);
        }

        private void ApplyFlaggedAppearance()
        {
            foreach (KeyValuePair<string, CheckedPart> partEntry in partsBySourcePath)
            {
                CheckedPart part = partEntry.Value;
                if (part.CopiedRenderer == null)
                {
                    continue;
                }
                if (!flaggedNodePaths.Contains(partEntry.Key))
                {
                    part.CopiedRenderer.sharedMaterials = part.AuthoredMaterials;
                    continue;
                }

                // An extra material slot re-renders the last submesh, so the red lands on top of the art.
                Material[] flaggedMaterials = new Material[part.AuthoredMaterials.Length + 1];
                Array.Copy(part.AuthoredMaterials, flaggedMaterials, part.AuthoredMaterials.Length);
                Material lastAuthoredMaterial = part.AuthoredMaterials.Length > 0
                    ? part.AuthoredMaterials[part.AuthoredMaterials.Length - 1]
                    : null;
                flaggedMaterials[flaggedMaterials.Length - 1] = EnsureRedOverlayMaterial(ResolveOverlaySourceTexture(lastAuthoredMaterial));
                part.CopiedRenderer.sharedMaterials = flaggedMaterials;
            }
        }

        // A Texture2DArray (_MainTexArray) cannot be sampled by URP Unlit, so those parts get the plain overlay.
        private static Texture2D ResolveOverlaySourceTexture(Material authoredMaterial)
        {
            if (authoredMaterial == null)
            {
                return null;
            }
            if (authoredMaterial.HasProperty("_MainTex") && authoredMaterial.GetTexture("_MainTex") is Texture2D mainTexture)
            {
                return mainTexture;
            }
            if (authoredMaterial.HasProperty("_BaseMap") && authoredMaterial.GetTexture("_BaseMap") is Texture2D baseMap)
            {
                return baseMap;
            }
            return null;
        }

        private Material EnsureRedOverlayMaterial(Texture2D sourceTexture)
        {
            if (sourceTexture == null)
            {
                if (redOverlayMaterial == null)
                {
                    redOverlayMaterial = BuildRedOverlayMaterial(null);
                }
                return redOverlayMaterial;
            }

            Material textureOverlay;
            if (!redOverlayMaterialsByTexture.TryGetValue(sourceTexture, out textureOverlay) || textureOverlay == null)
            {
                textureOverlay = BuildRedOverlayMaterial(sourceTexture);
                redOverlayMaterialsByTexture[sourceTexture] = textureOverlay;
            }
            return textureOverlay;
        }

        private void DestroyRedOverlayMaterials()
        {
            if (redOverlayMaterial != null)
            {
                UnityEngine.Object.DestroyImmediate(redOverlayMaterial);
            }
            redOverlayMaterial = null;
            foreach (KeyValuePair<Texture, Material> overlayEntry in redOverlayMaterialsByTexture)
            {
                if (overlayEntry.Value != null)
                {
                    UnityEngine.Object.DestroyImmediate(overlayEntry.Value);
                }
            }
            redOverlayMaterialsByTexture.Clear();
        }

        private static Material BuildRedOverlayMaterial(Texture2D sourceTexture)
        {
            Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlitShader == null)
            {
                unlitShader = Shader.Find("Unlit/Transparent");
            }
            // Setting _Surface alone does not make a URP material transparent; blend state and keyword must follow.
            Material overlayMaterial = new Material(unlitShader);
            overlayMaterial.hideFlags = HideFlags.HideAndDontSave;
            Color redTint = new Color(1f, 0.15f, 0.1f, 0.55f);
            Texture overlayTexture = sourceTexture != null ? sourceTexture : Texture2D.whiteTexture;
            overlayMaterial.SetColor("_BaseColor", redTint);
            overlayMaterial.SetColor("_Color", redTint);
            overlayMaterial.SetTexture("_BaseMap", overlayTexture);
            overlayMaterial.SetTexture("_MainTex", overlayTexture);
            overlayMaterial.SetFloat("_Surface", 1f);
            overlayMaterial.SetFloat("_Blend", 0f);
            overlayMaterial.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            overlayMaterial.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            overlayMaterial.SetFloat("_ZWrite", 0f);
            overlayMaterial.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            overlayMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            if (sourceTexture != null)
            {
                // Clip by the art's alpha so the wash follows the painted pixels, not the whole quad.
                overlayMaterial.SetFloat("_AlphaClip", 1f);
                // URP clips on texture alpha times the 0.55 tint alpha, so 0.25 keeps texels above ~45% opacity.
                overlayMaterial.SetFloat("_Cutoff", 0.25f);
                overlayMaterial.EnableKeyword("_ALPHATEST_ON");
            }
            overlayMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return overlayMaterial;
        }

        private void RebuildPrefabCopy(GameObject prefab)
        {
            DestroyPrefabCopy();
            SweepStrandedSourceCopies();
            sceneGizmos.HideSelection();

            if (prefab == null)
            {
                return;
            }
            EnsureRenderUtility();

            prefabCopyRoot = UnityEngine.Object.Instantiate(prefab);
            prefabCopyRoot.name = SourceCopyObjectName;
            prefabCopyRoot.hideFlags = HideFlags.HideAndDontSave;
            OwnedSourceCopies.Add(prefabCopyRoot);
            // Back at the origin so the copy stands on the grid however far out the prefab was authored.
            prefabCopyRoot.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            prefabCopyRoot.transform.localScale = prefab.transform.localScale;
            renderUtility.AddSingleGO(prefabCopyRoot);

            Renderer[] copiedRenderers = prefabCopyRoot.GetComponentsInChildren<Renderer>(true);
            for (int rendererIndex = 0; rendererIndex < copiedRenderers.Length; rendererIndex++)
            {
                Renderer copiedRenderer = copiedRenderers[rendererIndex];
                string nodePath = PrefabAuthoringBridge.GetHierarchyPath(
                    copiedRenderer.transform, prefabCopyRoot.transform);
                if (string.IsNullOrEmpty(nodePath) || partsBySourcePath.ContainsKey(nodePath))
                {
                    continue;
                }
                Material[] authoredMaterials = copiedRenderer.sharedMaterials;
                partsBySourcePath.Add(nodePath, new CheckedPart
                {
                    CopiedRenderer = copiedRenderer,
                    AuthoredMaterials = authoredMaterials
                });
                ApplyBillboardOverride(copiedRenderer, authoredMaterials);
            }
        }

        // Billboarding would turn every part to face the camera, which defeats checking the rig from any side.
        private void ApplyBillboardOverride(Renderer copiedRenderer, Material[] authoredMaterials)
        {
            bool anyBillboarding = false;
            for (int materialIndex = 0; materialIndex < authoredMaterials.Length; materialIndex++)
            {
                Material authoredMaterial = authoredMaterials[materialIndex];
                if (authoredMaterial != null && authoredMaterial.HasProperty(BillboardParamsPropertyId))
                {
                    anyBillboarding = true;
                    break;
                }
            }
            if (!anyBillboarding)
            {
                return;
            }
            propertyBlock.Clear();
            propertyBlock.SetVector(BillboardParamsPropertyId, Vector4.zero);
            copiedRenderer.SetPropertyBlock(propertyBlock);
        }

        private Bounds MeasurePrefabCopyBounds()
        {
            bool hasAnyBounds = false;
            Bounds combinedBounds = new Bounds(Vector3.zero, Vector3.zero);
            foreach (KeyValuePair<string, CheckedPart> partEntry in partsBySourcePath)
            {
                Renderer copiedRenderer = partEntry.Value.CopiedRenderer;
                if (copiedRenderer == null)
                {
                    continue;
                }
                Bounds rendererBounds = copiedRenderer.bounds;
                if (rendererBounds.extents.sqrMagnitude < 0.0000001f)
                {
                    continue;
                }
                if (!hasAnyBounds)
                {
                    combinedBounds = rendererBounds;
                    hasAnyBounds = true;
                    continue;
                }
                combinedBounds.Encapsulate(rendererBounds);
            }
            return hasAnyBounds ? combinedBounds : new Bounds(new Vector3(0f, 1f, 0f), Vector3.one * 2f);
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

            // Grid cells scale to the power of ten nearest the subject so a tiny or huge rig keeps a usable floor.
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

        private void DestroyPrefabCopy()
        {
            partsBySourcePath.Clear();
            DestroyRedOverlayMaterials();
            if (prefabCopyRoot != null)
            {
                OwnedSourceCopies.Remove(prefabCopyRoot);
                UnityEngine.Object.DestroyImmediate(prefabCopyRoot);
                prefabCopyRoot = null;
            }
        }

        // GameObject.Find cannot see a HideAndDontSave object, so only FindObjectsOfTypeAll reaches strays.
        private static void SweepStrandedSourceCopies()
        {
            OwnedSourceCopies.RemoveWhere(ownedCopy => ownedCopy == null);
            GameObject[] allObjects = Resources.FindObjectsOfTypeAll<GameObject>();
            for (int objectIndex = 0; objectIndex < allObjects.Length; objectIndex++)
            {
                GameObject candidate = allObjects[objectIndex];
                if (candidate != null
                    && candidate.name == SourceCopyObjectName
                    && !OwnedSourceCopies.Contains(candidate))
                {
                    UnityEngine.Object.DestroyImmediate(candidate);
                }
            }
        }

        public void Dispose()
        {
            EditorApplication.update -= Tick;
            DestroyPrefabCopy();
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
