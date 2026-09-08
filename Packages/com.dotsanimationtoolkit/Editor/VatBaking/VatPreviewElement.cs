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
    /// Self-contained VAT-bake preview viewport: plays a baked texture set through the package's own
    /// shader and, optionally, overlays a translucent "source ghost" so bake drift shows as a double image.
    /// </summary>
    public sealed class VatPreviewElement : VisualElement, ITransportTarget, IDisposable
    {
        private PreviewRenderUtility renderUtility;
        private readonly PreviewOrbitCameraRig cameraRig = new PreviewOrbitCameraRig();
        private readonly PreviewCameraNavigation cameraNavigation = new PreviewCameraNavigation();
        private readonly VatPreviewPlayback playback = new VatPreviewPlayback();
        private VatPreviewMaterial material;

        private VatTextureSetAsset textureSet;
        private ClipSetAsset clipSetForNames;
        private SkinnedMeshRenderer sourceRenderer;
        private IReadOnlyList<VatBakeClip> bakeClips;

        private Image viewportImage;
        private Label statusLabel;
        private DropdownField clipDropdown;
        private Label frameReadoutLabel;
        private TransportCoreElement transportCore;
        private ToolbarToggle ghostToggle;

        private bool isPlaying;
        private float stopReturnTime;
        private float lastTickTimeSinceStartup;

        private GameObject ghostRoot;
        private SkinnedMeshRenderer ghostRenderer;
        private readonly BoneTrackPoser ghostPoser = new BoneTrackPoser();
        private bool isGhostOn;

        private ulong currentClipId;
        private uint currentTargetId;

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
            ToolkitIcons.SetButtonIcon(resetCameraButton, resetCameraIcon, "d_FrameCapture", "Reset Camera");
            overlayColumn.Add(resetCameraButton);

            ghostToggle = new ToolbarToggle();
            ghostToggle.name = "vat-ghost-toggle";
            ghostToggle.AddToClassList("clip-editor__overlay-tool-button");
            ghostToggle.value = false;
            Image ghostIcon = new Image();
            ghostIcon.AddToClassList("clip-editor__overlay-tool-icon");
            ghostIcon.pickingMode = PickingMode.Ignore;
            ToolkitIcons.SetToggleIcon(ghostToggle, ghostIcon, "d_SkinnedMeshRenderer Icon", "Ghost");
            ghostToggle.RegisterValueChangedCallback(changeEvent => SetGhostEnabled(changeEvent.newValue));
            ghostToggle.SetEnabled(false);
            overlayColumn.Add(ghostToggle);

            viewportOverlay.Add(overlayColumn);
            viewportFrame.Add(viewportOverlay);
            Add(viewportFrame);

            statusLabel = new Label();
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
            transportRow.Add(frameReadoutLabel);

            Add(transportRow);

            cameraNavigation.Rig = cameraRig;
            cameraNavigation.AttachTo(viewportImage);

            RegisterCallback<AttachToPanelEvent>(evt => EditorApplication.update += Tick);
            RegisterCallback<DetachFromPanelEvent>(evt => EditorApplication.update -= Tick);
        }

        public void Show(VatTextureSetAsset textureSet, ClipSetAsset clipSetForNames, SkinnedMeshRenderer sourceRenderer, IReadOnlyList<VatBakeClip> bakeClipsOrNull)
        {
            material?.Dispose();
            material = null;
            this.textureSet = textureSet;
            this.clipSetForNames = clipSetForNames;
            this.sourceRenderer = sourceRenderer;
            bakeClips = bakeClipsOrNull;

            DisableGhost();
            ghostToggle?.SetEnabled(sourceRenderer != null && bakeClips != null && bakeClips.Count > 0);
            if (ghostToggle != null)
            {
                ghostToggle.tooltip = sourceRenderer != null
                    ? "Overlay a translucent copy of the source, posed the same way, to spot drift from the bake."
                    : "No source renderer for this preview session — pick a set through Bake, not the Preview Set field, to enable the ghost.";
            }

            if (textureSet == null || textureSet.clipRanges == null || textureSet.clipRanges.Count == 0)
            {
                statusLabel.text = "No VAT texture set to preview.";
                clipDropdown.choices = new List<string>();
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

            Texture mainTexture = sourceRenderer != null && sourceRenderer.sharedMaterial != null
                ? sourceRenderer.sharedMaterial.mainTexture
                : null;
            bool created = VatPreviewMaterial.TryCreate(textureSet, mainTexture, out material, out string failureMessage);
            statusLabel.text = created
                ? "bones " + textureSet.boneCount.ToString() + " · frames " + textureSet.clipRanges[0].frameCount.ToString()
                    + " · " + textureSet.textureWidth.ToString() + "x" + textureSet.boneTexture.height.ToString()
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
            currentClipId = range.clipId;
            currentTargetId = range.targetId;
            playback.SetRange(range);
            cameraRig.SetFrameTarget(range.bounds);
            cameraRig.ResetView();
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

        private void Tick()
        {
            if (textureSet == null)
            {
                return;
            }

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
            if (isGhostOn)
            {
                TickGhost();
            }

            RenderViewport();
            RefreshFrameReadout();
        }

        private void RefreshFrameReadout()
        {
            if (frameReadoutLabel == null || !playback.HasRange)
            {
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
            renderUtility.BeginPreview(viewportRect, GUIStyle.none);
            cameraRig.ApplyTo(renderUtility.camera);

            if (material != null && textureSet != null && textureSet.runtimeMesh != null)
            {
                renderUtility.DrawMesh(textureSet.runtimeMesh, Matrix4x4.identity, material.Material, 0);
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
            if (enabled)
            {
                EnableGhost();
            }
            else
            {
                DisableGhost();
            }
        }

        private void EnableGhost()
        {
            if (isGhostOn || sourceRenderer == null)
            {
                return;
            }
            EnsureRenderUtility();

            ghostRoot = UnityEngine.Object.Instantiate(sourceRenderer.transform.root.gameObject);
            ghostRoot.hideFlags = HideFlags.HideAndDontSave;
            renderUtility.AddSingleGO(ghostRoot);

            // The instantiated copy mirrors the source hierarchy exactly, so the same relative path finds
            // the equivalent renderer on the copy.
            string relativePath = AnimationUtility.CalculateTransformPath(sourceRenderer.transform, sourceRenderer.transform.root);
            Transform ghostRendererTransform = string.IsNullOrEmpty(relativePath)
                ? ghostRoot.transform
                : ghostRoot.transform.Find(relativePath);
            ghostRenderer = ghostRendererTransform != null ? ghostRendererTransform.GetComponent<SkinnedMeshRenderer>() : null;

            if (ghostRenderer != null)
            {
                Material ghostMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                ghostMaterial.hideFlags = HideFlags.HideAndDontSave;
                Color ghostColor = ToolkitPalette.Accent;
                ghostColor.a = 0.35f;
                ghostMaterial.SetColor("_BaseColor", ghostColor);
                ghostMaterial.SetFloat("_Surface", 1f); // transparent
                ghostMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                Material[] ghostMaterials = new Material[ghostRenderer.sharedMaterials.Length];
                for (int materialIndex = 0; materialIndex < ghostMaterials.Length; materialIndex++)
                {
                    ghostMaterials[materialIndex] = ghostMaterial;
                }
                ghostRenderer.sharedMaterials = ghostMaterials;
                ghostPoser.Bind(ghostRoot.transform);
            }

            isGhostOn = true;
        }

        private void DisableGhost()
        {
            if (ghostRoot != null)
            {
                UnityEngine.Object.DestroyImmediate(ghostRoot);
                ghostRoot = null;
                ghostRenderer = null;
            }
            isGhostOn = false;
        }

        private void TickGhost()
        {
            if (ghostRoot == null || bakeClips == null || !playback.HasRange)
            {
                return;
            }

            VatBakeClip matchedClip = default(VatBakeClip);
            bool foundMatch = false;
            // Match against whichever range is currently selected — clipId/targetId identify it uniquely.
            for (int clipIndex = 0; clipIndex < bakeClips.Count; clipIndex++)
            {
                VatBakeClip candidate = bakeClips[clipIndex];
                if (candidate.clipId == currentClipId && candidate.targetId == currentTargetId)
                {
                    matchedClip = candidate;
                    foundMatch = true;
                    break;
                }
            }
            if (!foundMatch)
            {
                return;
            }

            if (matchedClip.animationClip != null)
            {
                matchedClip.animationClip.SampleAnimation(ghostRoot, playback.Time);
            }
            else if (matchedClip.boneTracks != null && matchedClip.boneTracks.Count > 0 && playback.Duration > 0f)
            {
                ghostPoser.ApplyTracks(matchedClip.boneTracks, playback.Time / playback.Duration);
            }
        }

        public void Dispose()
        {
            material?.Dispose();
            material = null;
            DisableGhost();
            if (renderUtility != null)
            {
                renderUtility.Cleanup();
                renderUtility = null;
            }
        }
    }
}
