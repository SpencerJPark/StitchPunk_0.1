// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Viewport of the prefab a new rig is being cut from: an inert preview-scene copy the camera
    /// orbits, showing which of its nodes are ticked as targets and which one is being edited.
    /// Nodes are addressed by the same hierarchy path a <c>RigTargetDefinition</c> stores.
    /// </summary>
    public sealed class RigSourcePreviewElement : VisualElement, IDisposable
    {
        // One renderer of the preview copy, plus how the prefab author left it — restored whenever a
        // node goes back to being excluded, since including one forces it visible.
        private sealed class PreviewNode
        {
            public Renderer CopiedRenderer;
            public Material[] AuthoredMaterials;
            public bool AuthoredRendererEnabled;
            public bool AuthoredObjectActive;
        }

        private const string SourceCopyObjectName = "NewRigPreviewSourceCopy";

        // Copies a live preview still owns. A domain reload clears this while the HideAndDontSave
        // copies themselves survive in the preview scene, so anything named SourceCopyObjectName
        // that is missing from here is stranded and gets swept.
        private static readonly HashSet<GameObject> OwnedSourceCopies = new HashSet<GameObject>();

        private PreviewRenderUtility renderUtility;
        private readonly PreviewOrbitCameraRig cameraRig = new PreviewOrbitCameraRig();
        private readonly PreviewCameraNavigation cameraNavigation = new PreviewCameraNavigation();
        private readonly PreviewSceneGizmos sceneGizmos = new PreviewSceneGizmos();
        private bool sceneGizmosAdded;

        private Image viewportImage;
        private Label statusLabel;
        private ToolbarToggle showExcludedToggle;

        private GameObject prefabCopyRoot;
        private readonly Dictionary<string, PreviewNode> nodesBySourcePath =
            new Dictionary<string, PreviewNode>();
        private readonly HashSet<string> excludedNodePaths = new HashSet<string>();
        private string focusedNodePath;
        private Material excludedNodeMaterial;
        private bool showExcludedNodes = true;
        private float lastTickTimeSinceStartup;

        public RigSourcePreviewElement()
        {
            style.flexGrow = 1f;

            VisualElement viewportFrame = new VisualElement();
            viewportFrame.AddToClassList("clip-editor__viewport-frame");
            viewportFrame.style.flexGrow = 1f;

            viewportImage = new Image();
            viewportImage.style.flexGrow = 1f;
            viewportFrame.Add(viewportImage);

            VisualElement viewportOverlay = new VisualElement();
            viewportOverlay.AddToClassList("clip-editor__viewport-overlay");
            viewportOverlay.pickingMode = PickingMode.Ignore;

            VisualElement overlayColumn = new VisualElement();
            overlayColumn.AddToClassList("clip-editor__overlay-column");

            ToolbarButton resetCameraButton = new ToolbarButton(() => cameraNavigation.ResetView());
            resetCameraButton.name = "new-rig-reset-camera-button";
            resetCameraButton.AddToClassList("clip-editor__overlay-tool-button");
            resetCameraButton.tooltip = "Put the camera back head-on, framing the whole prefab.";
            Image resetCameraIcon = new Image();
            resetCameraIcon.AddToClassList("clip-editor__overlay-tool-icon");
            resetCameraIcon.pickingMode = PickingMode.Ignore;
            // The four-argument SetButtonIcon only swaps the image; parenting the icon is the
            // caller's job, and skipping it leaves a button with neither glyph nor word.
            resetCameraButton.Insert(0, resetCameraIcon);
            ToolkitIcons.SetButtonIcon(resetCameraButton, resetCameraIcon, "d_FrameCapture", "Reset Camera");
            overlayColumn.Add(resetCameraButton);

            showExcludedToggle = new ToolbarToggle();
            showExcludedToggle.name = "new-rig-show-excluded-toggle";
            showExcludedToggle.value = true;
            showExcludedToggle.AddToClassList("clip-editor__overlay-tool-button");
            showExcludedToggle.AddToClassList("clip-editor__overlay-run-break");
            showExcludedToggle.tooltip =
                "Keep the nodes you have not ticked on screen, greyed out. Turn it off to see only "
                + "what the new rig will actually carry.";
            Image showExcludedIcon = new Image();
            showExcludedIcon.AddToClassList("clip-editor__overlay-tool-icon");
            showExcludedIcon.pickingMode = PickingMode.Ignore;
            showExcludedToggle.Add(showExcludedIcon);
            ToolkitIcons.SetToggleIcon(showExcludedToggle, showExcludedIcon, "d_scenevis_visible", "Untargeted");
            showExcludedToggle.RegisterValueChangedCallback(changeEvent =>
            {
                showExcludedNodes = changeEvent.newValue;
                RefreshNodeAppearance();
            });
            overlayColumn.Add(showExcludedToggle);

            viewportOverlay.Add(overlayColumn);
            viewportFrame.Add(viewportOverlay);
            Add(viewportFrame);

            statusLabel = new Label("Assign a source prefab to see it here.");
            statusLabel.style.whiteSpace = WhiteSpace.Normal;
            statusLabel.style.marginTop = 4f;
            Add(statusLabel);

            cameraNavigation.Rig = cameraRig;
            cameraNavigation.AttachTo(viewportImage);

            RegisterCallback<AttachToPanelEvent>(attachEvent => EditorApplication.update += Tick);
            RegisterCallback<DetachFromPanelEvent>(detachEvent => EditorApplication.update -= Tick);
        }

        /// <summary>Re-points the preview at a prefab, or clears it when given null.</summary>
        public void ShowPrefab(GameObject prefab)
        {
            RebuildPrefabCopy(prefab);

            if (prefab == null)
            {
                statusLabel.text = "Assign a source prefab to see it here.";
                cameraRig.ClearFrameTarget();
                cameraRig.ResetView();
                return;
            }

            statusLabel.text = prefab.name + " — " + nodesBySourcePath.Count.ToString() + " renderer(s).";
            cameraRig.SetFrameTarget(MeasurePrefabCopyBounds());
            cameraRig.ResetView();
        }

        /// <summary>Marks whether a node is ticked as a rig target, which is what its shading shows.</summary>
        public void SetNodeIncluded(string sourceNodePath, bool isIncluded)
        {
            if (string.IsNullOrEmpty(sourceNodePath))
            {
                return;
            }
            if (isIncluded)
            {
                excludedNodePaths.Remove(sourceNodePath);
            }
            else
            {
                excludedNodePaths.Add(sourceNodePath);
            }
            RefreshNodeAppearance();
        }

        /// <summary>Boxes one node in the viewport and makes it what F frames. Unknown paths clear the box.</summary>
        public void FocusNode(string sourceNodePath)
        {
            focusedNodePath = sourceNodePath;

            PreviewNode focusedNode;
            if (string.IsNullOrEmpty(sourceNodePath)
                || !nodesBySourcePath.TryGetValue(sourceNodePath, out focusedNode)
                || focusedNode.CopiedRenderer == null)
            {
                ClearFocus();
                return;
            }

            Bounds focusedBounds = focusedNode.CopiedRenderer.bounds;
            sceneGizmos.EnsureBuilt();
            sceneGizmos.ShowSelection(focusedBounds.center, Quaternion.identity, focusedBounds.size);
            cameraRig.SetFrameTarget(focusedBounds);
        }

        /// <summary>Drops the focus box and puts F back on the whole prefab.</summary>
        public void ClearFocus()
        {
            focusedNodePath = null;
            sceneGizmos.HideSelection();
            if (prefabCopyRoot != null)
            {
                cameraRig.SetFrameTarget(MeasurePrefabCopyBounds());
            }
        }

        // Runs whether or not a prefab is assigned: an empty viewport still has to draw its grid, or
        // the pane opens looking broken rather than empty.
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

            renderUtility.BeginPreview(viewportRect, GUIStyle.none);
            cameraRig.ApplyTo(renderUtility.camera);
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

        private void RebuildPrefabCopy(GameObject prefab)
        {
            DestroyPrefabCopy();
            SweepStrandedSourceCopies();
            excludedNodePaths.Clear();
            focusedNodePath = null;
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
            // Put back at the origin so the copy stands on the grid rather than wherever the prefab
            // happens to have been authored, which is often hundreds of units out.
            prefabCopyRoot.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            prefabCopyRoot.transform.localScale = prefab.transform.localScale;
            renderUtility.AddSingleGO(prefabCopyRoot);

            Renderer[] copiedRenderers = prefabCopyRoot.GetComponentsInChildren<Renderer>(true);
            for (int rendererIndex = 0; rendererIndex < copiedRenderers.Length; rendererIndex++)
            {
                Renderer copiedRenderer = copiedRenderers[rendererIndex];
                string nodePath = PrefabAuthoringBridge.GetHierarchyPath(
                    copiedRenderer.transform, prefabCopyRoot.transform);
                if (string.IsNullOrEmpty(nodePath) || nodesBySourcePath.ContainsKey(nodePath))
                {
                    continue;
                }
                nodesBySourcePath.Add(nodePath, new PreviewNode
                {
                    CopiedRenderer = copiedRenderer,
                    AuthoredMaterials = copiedRenderer.sharedMaterials,
                    AuthoredRendererEnabled = copiedRenderer.enabled,
                    AuthoredObjectActive = copiedRenderer.gameObject.activeSelf
                });
            }
        }

        // Three states per node, and the tick is what separates them: a target is shown as authored,
        // an untargeted node is greyed back, and with the rail toggle off it is gone entirely.
        private void RefreshNodeAppearance()
        {
            foreach (KeyValuePair<string, PreviewNode> nodeEntry in nodesBySourcePath)
            {
                PreviewNode node = nodeEntry.Value;
                if (node.CopiedRenderer == null)
                {
                    continue;
                }

                bool isExcluded = excludedNodePaths.Contains(nodeEntry.Key);
                if (!isExcluded)
                {
                    // Forced visible rather than left as authored: ticking a node the prefab had
                    // switched off is the author saying the rig carries it, and a target that draws
                    // nothing would read as a broken tick.
                    node.CopiedRenderer.gameObject.SetActive(true);
                    node.CopiedRenderer.enabled = true;
                    node.CopiedRenderer.sharedMaterials = node.AuthoredMaterials;
                    continue;
                }

                node.CopiedRenderer.gameObject.SetActive(node.AuthoredObjectActive);
                node.CopiedRenderer.enabled = showExcludedNodes && node.AuthoredRendererEnabled;
                if (!showExcludedNodes)
                {
                    continue;
                }

                Material[] greyedMaterials = new Material[node.CopiedRenderer.sharedMaterials.Length];
                for (int slotIndex = 0; slotIndex < greyedMaterials.Length; slotIndex++)
                {
                    greyedMaterials[slotIndex] = EnsureExcludedNodeMaterial();
                }
                node.CopiedRenderer.sharedMaterials = greyedMaterials;
            }
        }

        private Material EnsureExcludedNodeMaterial()
        {
            if (excludedNodeMaterial != null)
            {
                return excludedNodeMaterial;
            }
            Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlitShader == null)
            {
                unlitShader = Shader.Find("Unlit/Color");
            }
            excludedNodeMaterial = new Material(unlitShader);
            excludedNodeMaterial.hideFlags = HideFlags.HideAndDontSave;
            Color greyedColor = new Color(0.55f, 0.57f, 0.6f, 0.22f);
            excludedNodeMaterial.SetColor("_BaseColor", greyedColor);
            excludedNodeMaterial.SetColor("_Color", greyedColor);
            excludedNodeMaterial.SetFloat("_Surface", 1f); // transparent
            excludedNodeMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return excludedNodeMaterial;
        }

        // A prefab with no renderers, or one whose renderers are all switched off, measures as an
        // empty box; framing that would drop the camera a metre from the origin with nothing in it.
        private Bounds MeasurePrefabCopyBounds()
        {
            bool hasAnyBounds = false;
            Bounds combinedBounds = new Bounds(Vector3.zero, Vector3.zero);
            foreach (KeyValuePair<string, PreviewNode> nodeEntry in nodesBySourcePath)
            {
                Renderer copiedRenderer = nodeEntry.Value.CopiedRenderer;
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

        private void DestroyPrefabCopy()
        {
            nodesBySourcePath.Clear();
            if (prefabCopyRoot != null)
            {
                OwnedSourceCopies.Remove(prefabCopyRoot);
                UnityEngine.Object.DestroyImmediate(prefabCopyRoot);
                prefabCopyRoot = null;
            }
        }

        // GameObject.Find cannot see a HideAndDontSave object, so the stranded copies a domain
        // reload left behind are only reachable through Resources.FindObjectsOfTypeAll.
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
            if (excludedNodeMaterial != null)
            {
                UnityEngine.Object.DestroyImmediate(excludedNodeMaterial);
                excludedNodeMaterial = null;
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
