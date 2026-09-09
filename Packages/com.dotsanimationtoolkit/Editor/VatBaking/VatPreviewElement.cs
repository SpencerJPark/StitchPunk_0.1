using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using DotsAnimationToolkit.Authoring;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Self-contained VAT-bake preview viewport: shows the source mesh at rest until a set is baked,
    /// then plays that set through the package's own shader, with the rest pose available as an overlay.
    /// </summary>
    public sealed class VatPreviewElement : VisualElement, ITransportTarget, IDisposable
    {
        private PreviewRenderUtility renderUtility;
        private readonly PreviewOrbitCameraRig cameraRig = new PreviewOrbitCameraRig();
        private readonly PreviewCameraNavigation cameraNavigation = new PreviewCameraNavigation();
        private readonly VatPreviewPlayback playback = new VatPreviewPlayback();
        private VatPreviewMaterial material;

        private VatTextureSetAsset textureSet;

        // The one part shown by this preview: the first entry in textureSet.parts, or null. Part
        // switching is a later amendment's work - this preview has only ever shown one part.
        private VatPartTextures previewedPart;
        private ClipSetAsset clipSetForNames;
        private SkinnedMeshRenderer sourceRenderer;

        private Image viewportImage;
        private Label statusLabel;
        private DropdownField clipDropdown;
        private Label frameReadoutLabel;
        private TransportCoreElement transportCore;
        private ToolbarToggle ghostToggle;

        private readonly PreviewSceneGizmos sceneGizmos = new PreviewSceneGizmos();
        private bool sceneGizmosAdded;

        private bool isPlaying;
        private float stopReturnTime;
        private float lastTickTimeSinceStartup;

        // One copy of the source hierarchy, held at the pose it was authored in and never animated.
        // It is the subject shown before anything is baked, and the rest-pose reference the Ghost
        // toggle lays over the playing bake afterwards - the same object either way, only its
        // materials and visibility change.
        private GameObject sourceCopyRoot;
        private readonly List<Renderer> sourceCopyRenderers = new List<Renderer>();
        private readonly List<Material[]> sourceCopyAuthoredMaterials = new List<Material[]>();
        private Material ghostOverlayMaterial;
        private bool isGhostOn;

        private VatTextureSetAsset lastFramedTextureSet;
        private SkinnedMeshRenderer lastFramedSourceRenderer;

        private const string SourceCopyObjectName = "VatPreviewSourceCopy";

        // Every copy a live preview still owns. A domain reload clears this and the field below it
        // while the HideAndDontSave copies themselves survive in the preview scene, so anything
        // named SourceCopyObjectName that is missing from here is stranded and gets swept. Tracking
        // ownership rather than sweeping by name alone is what lets the standalone window and the
        // Clip Editor's VAT Bake tab both hold a copy without destroying each other's.
        private static readonly HashSet<GameObject> OwnedSourceCopies = new HashSet<GameObject>();

        public VatPreviewElement()
        {
            playback.Loop = true;

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
            resetCameraButton.name = "vat-reset-camera-button";
            resetCameraButton.AddToClassList("clip-editor__overlay-tool-button");
            resetCameraButton.tooltip = "Put the camera back head-on, framing the baked mesh.";
            Image resetCameraIcon = new Image();
            resetCameraIcon.AddToClassList("clip-editor__overlay-tool-icon");
            resetCameraIcon.pickingMode = PickingMode.Ignore;
            // The four-argument SetButtonIcon only swaps the image; parenting the icon is the
            // caller's job, and skipping it leaves a button with neither glyph nor word.
            resetCameraButton.Insert(0, resetCameraIcon);
            ToolkitIcons.SetButtonIcon(resetCameraButton, resetCameraIcon, "d_FrameCapture", "Reset Camera");
            overlayColumn.Add(resetCameraButton);

            ghostToggle = new ToolbarToggle();
            ghostToggle.name = "vat-ghost-toggle";
            ghostToggle.value = false;
            ghostToggle.AddToClassList("clip-editor__overlay-tool-button");
            ghostToggle.AddToClassList("clip-editor__overlay-run-break");
            ghostToggle.tooltip =
                "Lay the source mesh at rest over the playing bake, so how far the bake moves is "
                + "visible against a pose that does not.";
            Image ghostIcon = new Image();
            ghostIcon.AddToClassList("clip-editor__overlay-tool-icon");
            ghostIcon.pickingMode = PickingMode.Ignore;
            ghostToggle.Add(ghostIcon);
            ToolkitIcons.SetToggleIcon(ghostToggle, ghostIcon, ToolkitIcons.GhostGlyph, "Ghost");
            ghostToggle.RegisterValueChangedCallback(changeEvent => SetGhostEnabled(changeEvent.newValue));
            ghostToggle.SetEnabled(false);
            overlayColumn.Add(ghostToggle);

            viewportOverlay.Add(overlayColumn);
            viewportFrame.Add(viewportOverlay);
            Add(viewportFrame);

            statusLabel = new Label("No VAT texture set to preview.");
            statusLabel.style.whiteSpace = WhiteSpace.Normal;
            Add(statusLabel);

            VisualElement transportRow = new VisualElement();
            transportRow.AddToClassList("toolkit-transport");

            VisualElement transportGroup = new VisualElement();
            transportGroup.AddToClassList("toolkit-transport__group");
            transportCore = new TransportCoreElement();
            transportCore.Bind(this);
            transportGroup.Add(transportCore);
            transportRow.Add(transportGroup);

            VisualElement clipGroup = new VisualElement();
            clipGroup.AddToClassList("toolkit-transport__group");
            Label clipCaption = new Label("Clip");
            clipCaption.AddToClassList("toolkit-transport__caption");
            clipGroup.Add(clipCaption);
            clipDropdown = new DropdownField();
            clipDropdown.RegisterValueChangedCallback(OnClipDropdownChanged);
            clipGroup.Add(clipDropdown);
            transportRow.Add(clipGroup);

            frameReadoutLabel = new Label();
            frameReadoutLabel.AddToClassList("toolkit-transport__derived");
            frameReadoutLabel.AddToClassList("toolkit-transport__derived--counter");
            transportRow.Add(frameReadoutLabel);

            Add(transportRow);

            cameraNavigation.Rig = cameraRig;
            cameraNavigation.AttachTo(viewportImage);

            RegisterCallback<AttachToPanelEvent>(evt => EditorApplication.update += Tick);
            RegisterCallback<DetachFromPanelEvent>(evt => EditorApplication.update -= Tick);
        }

        /// <summary>Re-points the preview at a source renderer, a baked set, or both; either may be null.</summary>
        public void Show(VatTextureSetAsset textureSet, ClipSetAsset clipSetForNames, SkinnedMeshRenderer sourceRenderer)
        {
            bool subjectChanged = textureSet != lastFramedTextureSet || sourceRenderer != lastFramedSourceRenderer;

            material?.Dispose();
            material = null;
            this.textureSet = textureSet;
            previewedPart = textureSet != null && textureSet.parts != null && textureSet.parts.Count > 0
                ? textureSet.parts[0]
                : null;
            this.clipSetForNames = clipSetForNames;
            this.sourceRenderer = sourceRenderer;
            lastFramedTextureSet = textureSet;
            lastFramedSourceRenderer = sourceRenderer;

            // Rebuilt every time rather than only when the renderer reference changes: the copy is a
            // snapshot of the source's current pose, and re-posing the rig between bakes would
            // otherwise leave a rest-pose reference that is no longer the rest pose.
            RebuildSourceCopy();

            // The overlay only means anything once there is a bake to lay it over - before that the
            // same copy IS the subject, already on screen.
            bool ghostIsAvailable = sourceRenderer != null && textureSet != null;
            if (ghostToggle != null)
            {
                ghostToggle.SetEnabled(ghostIsAvailable);
                if (!ghostIsAvailable)
                {
                    ghostToggle.SetValueWithoutNotify(false);
                    isGhostOn = false;
                }
                ghostToggle.tooltip = sourceRenderer == null
                    ? "Assign a Skinned Mesh to overlay its rest pose on the bake."
                    : "Lay the source mesh at rest over the playing bake, so how far the bake moves "
                        + "is visible against a pose that does not.";
            }
            RefreshSourceCopyAppearance();

            if (textureSet == null || textureSet.clipRanges == null || textureSet.clipRanges.Count == 0)
            {
                // The clock has to be cleared too, not just the picture: Tick runs with no set
                // loaded, so a leftover range would keep counting frames for a set that is gone.
                isPlaying = false;
                playback.ClearRange();
                transportCore?.RefreshState();
                clipDropdown.choices = new List<string>();
                clipDropdown.SetValueWithoutNotify(string.Empty);

                if (sourceRenderer != null && sourceRenderer.sharedMesh != null)
                {
                    statusLabel.text = "Source shown at rest — bake to play it back from the textures.";
                    cameraRig.SetFrameTarget(sourceRenderer.sharedMesh.bounds);
                }
                else
                {
                    statusLabel.text = "No VAT texture set to preview.";
                    cameraRig.ClearFrameTarget();
                }
                if (subjectChanged)
                {
                    cameraRig.ResetView();
                }
                return;
            }

            List<string> choices = new List<string>();
            for (int rangeIndex = 0; rangeIndex < textureSet.clipRanges.Count; rangeIndex++)
            {
                choices.Add(DescribeRange(textureSet.clipRanges[rangeIndex]));
            }
            clipDropdown.choices = choices;
            clipDropdown.SetValueWithoutNotify(choices[0]);

            SelectRange(0);
            if (subjectChanged)
            {
                cameraRig.ResetView();
            }

            Texture mainTexture = sourceRenderer != null && sourceRenderer.sharedMaterial != null
                ? sourceRenderer.sharedMaterial.mainTexture
                : null;
            bool created = VatPreviewMaterial.TryCreate(textureSet, previewedPart, mainTexture, out material, out string failureMessage);
            statusLabel.text = created
                ? "bones " + previewedPart.boneCount.ToString() + " · frames " + textureSet.clipRanges[0].frameCount.ToString()
                    + " · " + previewedPart.textureWidth.ToString() + "x" + previewedPart.boneTexture.height.ToString()
                : failureMessage;
        }

        private string DescribeRange(VatClipRange range)
        {
            string clipLabel = "clip 0x" + range.clipId.ToString("X16");
            if (clipSetForNames != null && clipSetForNames.clips != null)
            {
                for (int clipIndex = 0; clipIndex < clipSetForNames.clips.Count; clipIndex++)
                {
                    ClipAsset clip = clipSetForNames.clips[clipIndex];
                    if (clip != null && clip.Id.Value == range.clipId)
                    {
                        clipLabel = clip.name;
                        break;
                    }
                }
            }
            return range.targetId == 0u ? clipLabel : clipLabel + " · target 0x" + range.targetId.ToString("X8");
        }

        private void SelectRange(int rangeIndex)
        {
            if (textureSet == null || textureSet.clipRanges == null || rangeIndex < 0 || rangeIndex >= textureSet.clipRanges.Count)
            {
                return;
            }
            VatClipRange range = textureSet.clipRanges[rangeIndex];
            playback.SetRange(range);
            // Only the frame target moves here. Re-framing on every dropdown change would yank the
            // camera back from wherever the user had just orbited to compare two clips.
            cameraRig.SetFrameTarget(ResolveFrameBounds(range));
        }

        // No bake has ever written VatClipRange.bounds, so a zero-sized box means "not measured",
        // not "empty" — framing it would put the camera a metre from the origin, inside the mesh.
        // The runtime mesh is what actually gets drawn, at identity, so its bounds are the truth here.
        private Bounds ResolveFrameBounds(VatClipRange range)
        {
            if (range.bounds.extents.sqrMagnitude > 0.0001f)
            {
                return range.bounds;
            }
            if (previewedPart != null && previewedPart.runtimeMesh != null)
            {
                return previewedPart.runtimeMesh.bounds;
            }
            return new Bounds(Vector3.zero, Vector3.one);
        }

        private void OnClipDropdownChanged(ChangeEvent<string> changeEvent)
        {
            if (clipDropdown.choices == null)
            {
                return;
            }
            int index = clipDropdown.choices.IndexOf(changeEvent.newValue);
            if (index >= 0)
            {
                SelectRange(index);
            }
        }

        // Runs whether or not a set is loaded: with nothing baked yet the viewport still has to draw
        // its grid and backdrop, or the pane opens looking broken rather than empty.
        private void Tick()
        {
            float deltaSeconds = lastTickTimeSinceStartup > 0f
                ? (float)EditorApplication.timeSinceStartup - lastTickTimeSinceStartup
                : 0f;
            lastTickTimeSinceStartup = (float)EditorApplication.timeSinceStartup;

            if (isPlaying && !playback.Advance(deltaSeconds))
            {
                isPlaying = false;
                transportCore?.RefreshState();
            }
            cameraNavigation.StepFly(deltaSeconds);

            if (material != null)
            {
                material.SetFrame(playback.GlobalFrame, playback.GlobalFrame, 0f);
            }
            RenderViewport();
            RefreshFrameReadout();
        }

        private void RefreshFrameReadout()
        {
            if (frameReadoutLabel == null)
            {
                return;
            }
            if (!playback.HasRange)
            {
                frameReadoutLabel.text = string.Empty;
                return;
            }
            frameReadoutLabel.text = "frame " + playback.LocalFrameIndex.ToString() + " · global "
                + Mathf.RoundToInt(playback.GlobalFrame).ToString();
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

            // Added once and left in the scene: a viewport with nothing baked yet should still read
            // as a working 3D view, the same reference floor/backdrop the Clip Editor and Actor
            // Editor viewports always draw.
            sceneGizmos.EnsureBuilt();
            if (!sceneGizmosAdded && sceneGizmos.GridObject != null && sceneGizmos.SelectionObject != null)
            {
                renderUtility.AddSingleGO(sceneGizmos.GridObject);
                renderUtility.AddSingleGO(sceneGizmos.SelectionObject);
                sceneGizmosAdded = true;
            }

            renderUtility.BeginPreview(viewportRect, GUIStyle.none);
            cameraRig.ApplyTo(renderUtility.camera);

            if (material != null && previewedPart != null && previewedPart.runtimeMesh != null)
            {
                renderUtility.DrawMesh(previewedPart.runtimeMesh, Matrix4x4.identity, material.Material, 0);
            }

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

        public bool IsPlaying { get { return isPlaying; } }

        public bool IsLooping
        {
            get { return playback.Loop; }
            set { playback.Loop = value; transportCore?.RefreshState(); }
        }

        public TransportCapabilities Capabilities
        {
            get { return TransportCapabilities.StepBack | TransportCapabilities.StepForward | TransportCapabilities.Stop | TransportCapabilities.JumpToEnd | TransportCapabilities.Loop; }
        }

        public void TogglePlay()
        {
            if (!isPlaying)
            {
                stopReturnTime = playback.Time;
            }
            isPlaying = !isPlaying;
            transportCore?.RefreshState();
        }

        public void Stop()
        {
            isPlaying = false;
            playback.Time = stopReturnTime;
            transportCore?.RefreshState();
        }

        public void JumpToStart()
        {
            playback.JumpToStart();
        }

        public void JumpToEnd()
        {
            playback.JumpToEnd();
        }

        public void Step(int frameDelta)
        {
            playback.StepFrames(frameDelta);
        }

        private void SetGhostEnabled(bool enabled)
        {
            isGhostOn = enabled;
            RefreshSourceCopyAppearance();
        }

        // Rebuilt only when the source renderer itself changes; the copy is inert, so there is
        // nothing to keep in step frame to frame.
        private void RebuildSourceCopy()
        {
            DestroySourceCopy();
            SweepStrandedSourceCopies();
            if (sourceRenderer == null)
            {
                return;
            }
            EnsureRenderUtility();

            sourceCopyRoot = UnityEngine.Object.Instantiate(sourceRenderer.transform.root.gameObject);
            sourceCopyRoot.name = SourceCopyObjectName;
            sourceCopyRoot.hideFlags = HideFlags.HideAndDontSave;
            OwnedSourceCopies.Add(sourceCopyRoot);
            // The baked runtimeMesh is drawn at Matrix4x4.identity - its vertices are already in the
            // source renderer's own object space. The instantiated copy keeps the source's world
            // transform from whatever scene it was cloned out of, which is a different space entirely
            // (and usually a different scale); without resetting it here the copy sits offset from,
            // and out of scale with, the very mesh it is supposed to overlay.
            sourceCopyRoot.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            sourceCopyRoot.transform.localScale = Vector3.one;
            renderUtility.AddSingleGO(sourceCopyRoot);

            Renderer[] copiedRenderers = sourceCopyRoot.GetComponentsInChildren<Renderer>(true);
            for (int rendererIndex = 0; rendererIndex < copiedRenderers.Length; rendererIndex++)
            {
                sourceCopyRenderers.Add(copiedRenderers[rendererIndex]);
                sourceCopyAuthoredMaterials.Add(copiedRenderers[rendererIndex].sharedMaterials);
            }

            RefreshSourceCopyAppearance();
        }

        // Three states, and the texture set is what separates them: with nothing baked the copy is
        // the subject itself, shown as authored; once a bake exists it is only the Ghost overlay.
        private void RefreshSourceCopyAppearance()
        {
            if (sourceCopyRoot == null)
            {
                return;
            }

            bool nothingBakedYet = textureSet == null;
            bool shouldBeVisible = nothingBakedYet || isGhostOn;
            sourceCopyRoot.SetActive(shouldBeVisible);
            if (!shouldBeVisible)
            {
                return;
            }

            for (int rendererIndex = 0; rendererIndex < sourceCopyRenderers.Count; rendererIndex++)
            {
                Renderer copiedRenderer = sourceCopyRenderers[rendererIndex];
                if (copiedRenderer == null)
                {
                    continue;
                }
                if (nothingBakedYet)
                {
                    copiedRenderer.sharedMaterials = sourceCopyAuthoredMaterials[rendererIndex];
                    continue;
                }
                Material[] overlayMaterials = new Material[copiedRenderer.sharedMaterials.Length];
                for (int slotIndex = 0; slotIndex < overlayMaterials.Length; slotIndex++)
                {
                    overlayMaterials[slotIndex] = EnsureGhostOverlayMaterial();
                }
                copiedRenderer.sharedMaterials = overlayMaterials;
            }
        }

        private Material EnsureGhostOverlayMaterial()
        {
            if (ghostOverlayMaterial != null)
            {
                return ghostOverlayMaterial;
            }
            ghostOverlayMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            ghostOverlayMaterial.hideFlags = HideFlags.HideAndDontSave;
            Color ghostColor = ToolkitPalette.Accent;
            ghostColor.a = 0.35f;
            ghostOverlayMaterial.SetColor("_BaseColor", ghostColor);
            ghostOverlayMaterial.SetFloat("_Surface", 1f); // transparent
            ghostOverlayMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return ghostOverlayMaterial;
        }

        private void DestroySourceCopy()
        {
            sourceCopyRenderers.Clear();
            sourceCopyAuthoredMaterials.Clear();
            if (sourceCopyRoot != null)
            {
                OwnedSourceCopies.Remove(sourceCopyRoot);
                UnityEngine.Object.DestroyImmediate(sourceCopyRoot);
                sourceCopyRoot = null;
            }
        }

        // GameObject.Find cannot see a HideAndDontSave object, so the stranded copies are only
        // reachable through Resources.FindObjectsOfTypeAll.
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
            // Without this a tick after disposal would call EnsureRenderUtility and build a second
            // PreviewRenderUtility nothing owns.
            EditorApplication.update -= Tick;
            material?.Dispose();
            material = null;
            DestroySourceCopy();
            if (ghostOverlayMaterial != null)
            {
                UnityEngine.Object.DestroyImmediate(ghostOverlayMaterial);
                ghostOverlayMaterial = null;
            }
            sceneGizmos.Dispose();
            if (renderUtility != null)
            {
                renderUtility.Cleanup();
                renderUtility = null;
            }
        }
    }
}
