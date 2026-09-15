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
        private readonly VatPreviewPartPoser partPoser = new VatPreviewPartPoser();

        private sealed class PartPreview
        {
            public VatPartTextures Part;
            public VatPreviewMaterial Material;
        }

        private readonly List<PartPreview> partPreviews = new List<PartPreview>();
        private readonly List<ulong> clipIds = new List<ulong>();
        private int selectedClipIndex = -1;
        private ulong selectedClipId;
        private bool hasSelectedClip;
        private VatClipRange selectedClockRange;
        private bool otherPartsRestoredToRestPose;

        private VatTextureSetAsset textureSet;
        private RigAsset rig;
        private ClipSetAsset clipSetForNames;
        private SkinnedMeshRenderer sourceRenderer;

        private Image viewportImage;
        private Label statusLabel;
        private DropdownField clipDropdown;
        private Label frameReadoutLabel;
        private TransportCoreElement transportCore;
        private ToolbarToggle vatPartsToggle;
        private ToolbarToggle otherPartsToggle;
        private ToolbarToggle ghostToggle;
        private ToolbarButton resetCameraButton;

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

            ViewportFrameElement viewportFrame = new ViewportFrameElement();

            resetCameraButton = viewportFrame.AddResetCameraButton(() => cameraNavigation.ResetView());
            resetCameraButton.name = "vat-reset-camera-button";
            resetCameraButton.tooltip = "Put the camera back head-on, framing the baked mesh.";

            vatPartsToggle = viewportFrame.AddRailToggle(
                ToolkitIcons.VatPartsGlyph,
                "VAT parts — show the baked meshes playing.",
                "VAT",
                true);
            vatPartsToggle.name = "vat-parts-toggle";
            vatPartsToggle.SetValueWithoutNotify(true);
            vatPartsToggle.SetEnabled(false);

            otherPartsToggle = viewportFrame.AddRailToggle(
                ToolkitIcons.CutoutPartsGlyph,
                "Other parts — pose the quads and flipbooks from the clip set.",
                "Parts",
                true);
            otherPartsToggle.name = "other-parts-toggle";
            otherPartsToggle.SetValueWithoutNotify(true);
            // Visibility is decided per renderer in RefreshSourceCopyAppearance, which nothing else
            // re-runs on this toggle; without it the posed half stays hidden until Ghost is touched.
            otherPartsToggle.RegisterValueChangedCallback(changeEvent => RefreshSourceCopyAppearance());
            otherPartsToggle.SetEnabled(false);

            ghostToggle = viewportFrame.AddRailToggle(
                ToolkitIcons.GhostGlyph,
                "Lay the source mesh at rest over the playing bake, so how far the bake moves is "
                + "visible against a pose that does not.",
                "Ghost",
                true);
            ghostToggle.name = "vat-ghost-toggle";
            ghostToggle.RegisterValueChangedCallback(changeEvent => SetGhostEnabled(changeEvent.newValue));
            ghostToggle.SetEnabled(false);

            viewportImage = viewportFrame.ViewportImage;
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
        public void Show(VatTextureSetAsset textureSet, ClipSetAsset clipSetForNames, RigAsset rig, SkinnedMeshRenderer sourceRenderer)
        {
            bool subjectChanged = textureSet != lastFramedTextureSet || sourceRenderer != lastFramedSourceRenderer;

            DisposePartPreviews();
            this.textureSet = textureSet;
            this.rig = rig;
            this.clipSetForNames = clipSetForNames;
            this.sourceRenderer = sourceRenderer;
            lastFramedTextureSet = textureSet;
            lastFramedSourceRenderer = sourceRenderer;

            // Rebuilt every time rather than only when the renderer reference changes: the copy is a
            // snapshot of the source's current pose, and re-posing the rig between bakes would
            // otherwise leave a rest-pose reference that is no longer the rest pose.
            RebuildSourceCopy();
            partPoser.Rebuild(rig, clipSetForNames, textureSet, sourceCopyRoot);
            otherPartsRestoredToRestPose = false;

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

            if (textureSet == null || textureSet.clipRanges == null || textureSet.clipRanges.Count == 0)
            {
                vatPartsToggle?.SetEnabled(false);
                otherPartsToggle?.SetEnabled(false);
                RefreshSourceCopyAppearance();

                // The clock has to be cleared too, not just the picture: Tick runs with no set
                // loaded, so a leftover range would keep counting frames for a set that is gone.
                isPlaying = false;
                playback.ClearRange();
                transportCore?.RefreshState();
                clipDropdown.choices = new List<string>();
                clipDropdown.SetValueWithoutNotify(string.Empty);
                clipIds.Clear();
                selectedClipIndex = -1;
                hasSelectedClip = false;

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

            Texture mainTexture = sourceRenderer != null && sourceRenderer.sharedMaterial != null
                ? sourceRenderer.sharedMaterial.mainTexture
                : null;
            string firstFailureMessage = null;
            if (textureSet.parts != null)
            {
                for (int partIndex = 0; partIndex < textureSet.parts.Count; partIndex++)
                {
                    VatPartTextures part = textureSet.parts[partIndex];
                    if (part == null)
                    {
                        continue;
                    }
                    bool created = VatPreviewMaterial.TryCreate(textureSet, part, mainTexture, out VatPreviewMaterial partMaterial, out string partFailureMessage);
                    if (created)
                    {
                        partPreviews.Add(new PartPreview { Part = part, Material = partMaterial });
                    }
                    else if (firstFailureMessage == null)
                    {
                        firstFailureMessage = partFailureMessage;
                    }
                }
            }

            bool hasBake = partPreviews.Count > 0;
            vatPartsToggle?.SetEnabled(hasBake);
            bool otherPartsAvailable = hasBake
                && ClipSetContentResolver.HasNonVatContent(new ClipSetAsset[] { clipSetForNames })
                && partPoser.HasNonVatParts;
            if (otherPartsToggle != null)
            {
                otherPartsToggle.SetEnabled(otherPartsAvailable);
                otherPartsToggle.tooltip = hasBake && !otherPartsAvailable
                    ? "Other parts — this clip set keys no quads, flipbooks or billboards."
                    : "Other parts — pose the quads and flipbooks from the clip set.";
            }

            RefreshSourceCopyAppearance();

            clipIds.Clear();
            for (int rangeIndex = 0; rangeIndex < textureSet.clipRanges.Count; rangeIndex++)
            {
                ulong clipId = textureSet.clipRanges[rangeIndex].clipId;
                if (!clipIds.Contains(clipId))
                {
                    clipIds.Add(clipId);
                }
            }
            List<string> choices = new List<string>();
            for (int clipIndex = 0; clipIndex < clipIds.Count; clipIndex++)
            {
                choices.Add(DescribeClip(clipIds[clipIndex]));
            }
            clipDropdown.choices = choices;
            clipDropdown.SetValueWithoutNotify(choices.Count > 0 ? choices[0] : string.Empty);

            SelectClip(0);
            if (subjectChanged)
            {
                cameraRig.ResetView();
            }

            statusLabel.text = hasBake
                ? "parts " + partPreviews.Count.ToString() + " · frames " + selectedClockRange.frameCount.ToString()
                : (firstFailureMessage ?? "No VAT parts to preview.");
        }

        private string DescribeClip(ulong clipId)
        {
            if (clipSetForNames != null && clipSetForNames.clips != null)
            {
                for (int clipIndex = 0; clipIndex < clipSetForNames.clips.Count; clipIndex++)
                {
                    ClipAsset clip = clipSetForNames.clips[clipIndex];
                    if (clip != null && clip.Id.Value == clipId)
                    {
                        return clip.name;
                    }
                }
            }
            return "unnamed clip";
        }

        private void SelectClip(int clipIndex)
        {
            if (textureSet == null || textureSet.clipRanges == null || clipIndex < 0 || clipIndex >= clipIds.Count)
            {
                return;
            }
            selectedClipIndex = clipIndex;
            selectedClipId = clipIds[clipIndex];
            hasSelectedClip = true;
            otherPartsRestoredToRestPose = false;

            VatClipRange untargetedRange = default;
            bool foundUntargetedRange = false;
            VatClipRange firstRange = default;
            bool foundFirstRange = false;
            for (int rangeIndex = 0; rangeIndex < textureSet.clipRanges.Count; rangeIndex++)
            {
                VatClipRange candidate = textureSet.clipRanges[rangeIndex];
                if (candidate.clipId != selectedClipId)
                {
                    continue;
                }
                if (!foundFirstRange)
                {
                    firstRange = candidate;
                    foundFirstRange = true;
                }
                if (candidate.targetId == 0u)
                {
                    untargetedRange = candidate;
                    foundUntargetedRange = true;
                    break;
                }
            }
            if (!foundFirstRange && !foundUntargetedRange)
            {
                return;
            }
            selectedClockRange = foundUntargetedRange ? untargetedRange : firstRange;
            playback.SetRange(selectedClockRange);
            // Only the frame target moves here. Re-framing on every dropdown change would yank the
            // camera back from wherever the user had just orbited to compare two clips.
            cameraRig.SetFrameTarget(ResolveFrameBounds(selectedClockRange));
        }

        // Per part per frame: the part's own targeted range for the selected clip wins, and the
        // clip's untargeted range is the fallback for a part that was not baked its own range.
        private bool TryResolvePartRange(uint targetId, out VatClipRange range)
        {
            range = default;
            if (!hasSelectedClip || textureSet == null || textureSet.clipRanges == null)
            {
                return false;
            }
            VatClipRange untargetedRange = default;
            bool foundUntargetedRange = false;
            for (int rangeIndex = 0; rangeIndex < textureSet.clipRanges.Count; rangeIndex++)
            {
                VatClipRange candidate = textureSet.clipRanges[rangeIndex];
                if (candidate.clipId != selectedClipId)
                {
                    continue;
                }
                if (candidate.targetId == targetId)
                {
                    range = candidate;
                    return true;
                }
                if (candidate.targetId == 0u)
                {
                    untargetedRange = candidate;
                    foundUntargetedRange = true;
                }
            }
            if (foundUntargetedRange)
            {
                range = untargetedRange;
                return true;
            }
            return false;
        }

        private float SelectedClipDuration()
        {
            if (hasSelectedClip && clipSetForNames != null && clipSetForNames.clips != null)
            {
                for (int clipIndex = 0; clipIndex < clipSetForNames.clips.Count; clipIndex++)
                {
                    ClipAsset clip = clipSetForNames.clips[clipIndex];
                    if (clip != null && clip.Id.Value == selectedClipId)
                    {
                        return clip.duration;
                    }
                }
            }
            return playback.Duration;
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
            if (partPreviews.Count > 0 && partPreviews[0].Part != null && partPreviews[0].Part.runtimeMesh != null)
            {
                return partPreviews[0].Part.runtimeMesh.bounds;
            }
            return new Bounds(Vector3.zero, Vector3.one);
        }

        private void DisposePartPreviews()
        {
            for (int partIndex = 0; partIndex < partPreviews.Count; partIndex++)
            {
                partPreviews[partIndex].Material?.Dispose();
            }
            partPreviews.Clear();
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
                SelectClip(index);
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

            for (int partIndex = 0; partIndex < partPreviews.Count; partIndex++)
            {
                PartPreview partPreview = partPreviews[partIndex];
                if (partPreview.Material == null || partPreview.Part == null)
                {
                    continue;
                }
                if (TryResolvePartRange(partPreview.Part.targetId, out VatClipRange partRange))
                {
                    float frame = VatPreviewFrameResolver.GlobalFrameForRange(in partRange, playback.Time);
                    partPreview.Material.SetFrame(frame, frame, 0f);
                }
            }

            if (otherPartsToggle != null && otherPartsToggle.value && hasSelectedClip
                && partPoser.HasNonVatParts && partPreviews.Count > 0)
            {
                otherPartsRestoredToRestPose = false;
                float duration = SelectedClipDuration();
                float normalizedTime = duration > 0f ? playback.Time / duration : 0f;
                partPoser.PoseAt(selectedClipId, normalizedTime);
            }
            else if (!otherPartsRestoredToRestPose)
            {
                partPoser.RestoreRestPose();
                RefreshSourceCopyAppearance();
                otherPartsRestoredToRestPose = true;
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

            if (vatPartsToggle == null || vatPartsToggle.value)
            {
                for (int partIndex = 0; partIndex < partPreviews.Count; partIndex++)
                {
                    PartPreview partPreview = partPreviews[partIndex];
                    if (partPreview.Material != null && partPreview.Part != null && partPreview.Part.runtimeMesh != null)
                    {
                        renderUtility.DrawMesh(partPreview.Part.runtimeMesh, Matrix4x4.identity, partPreview.Material.Material, 0);
                    }
                }
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
        // the subject itself, shown as authored; once a bake exists the VAT-part renderers become
        // only the Ghost overlay, while every other renderer follows the Other Parts toggle.
        private void RefreshSourceCopyAppearance()
        {
            if (sourceCopyRoot == null)
            {
                return;
            }

            bool nothingBakedYet = textureSet == null;
            bool otherPartsVisible = otherPartsToggle != null && otherPartsToggle.value;

            for (int rendererIndex = 0; rendererIndex < sourceCopyRenderers.Count; rendererIndex++)
            {
                Renderer copiedRenderer = sourceCopyRenderers[rendererIndex];
                if (copiedRenderer == null)
                {
                    continue;
                }

                bool isVatPartRenderer = partPoser.IsVatPartRenderer(copiedRenderer);
                bool shouldBeVisible = isVatPartRenderer
                    ? (nothingBakedYet || isGhostOn)
                    : (nothingBakedYet || otherPartsVisible);

                copiedRenderer.enabled = shouldBeVisible;
                if (!shouldBeVisible)
                {
                    continue;
                }

                if (!isVatPartRenderer || nothingBakedYet)
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
            // The copy is being destroyed regardless, so this only clears the poser's bindings
            // before its transforms go with it - not a real restore, no point posing a corpse.
            if (sourceCopyRoot != null)
            {
                partPoser.RestoreRestPose();
            }
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
            DisposePartPreviews();
            DestroySourceCopy();
            partPoser.Dispose();
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
