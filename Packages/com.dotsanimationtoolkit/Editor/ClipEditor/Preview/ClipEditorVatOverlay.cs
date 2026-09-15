// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using Unity.Entities;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Optional "Baked VAT" view for the Clip Editor viewport: hides the live skinned renderers and
    /// draws each part's baked runtime mesh through its VatPreviewMaterial at the playhead frame.
    /// </summary>
    public sealed class ClipEditorVatOverlay : IDisposable
    {
        private sealed class Binding
        {
            public VatPartTextures Part;
            public VatPreviewMaterial Material;
            public SkinnedMeshRenderer LiveRenderer;
            public bool HasFrame;
        }

        private readonly List<Binding> bindings = new List<Binding>();
        private readonly List<SkinnedMeshRenderer> renderersDisabledByOverlay = new List<SkinnedMeshRenderer>();
        private GameObject skeletonInstanceRoot;
        private bool enabled;

        public bool Enabled
        {
            get => enabled;
            set
            {
                if (enabled == value)
                {
                    return;
                }
                enabled = value;
                if (enabled)
                {
                    ApplyRendererDisable();
                }
                else
                {
                    UndoRendererDisable();
                }
            }
        }

        public void Rebind(RigAsset rig, ClipSetAsset clipSet, GameObject rootInstance)
        {
            ReleaseBindings();
            skeletonInstanceRoot = rootInstance;

            VatTextureSetAsset textureSet = clipSet != null ? clipSet.vatTextures : null;
            if (textureSet == null || textureSet.parts == null || textureSet.parts.Count == 0 || rootInstance == null)
            {
                return;
            }

            if (!VatBakeSourceResolver.TryResolve(rig, out List<VatBakeSource> sources, out string failureMessage))
            {
                return;
            }

            for (int partIndex = 0; partIndex < textureSet.parts.Count; partIndex++)
            {
                VatPartTextures part = textureSet.parts[partIndex];
                if (part == null)
                {
                    continue;
                }

                VatBakeSource matchedSource = null;
                for (int sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
                {
                    if (sources[sourceIndex].TargetId == part.targetId)
                    {
                        matchedSource = sources[sourceIndex];
                        break;
                    }
                }
                if (matchedSource == null)
                {
                    continue;
                }

                Transform liveNode = string.IsNullOrEmpty(matchedSource.SourceNodePath)
                    ? rootInstance.transform
                    : rootInstance.transform.Find(matchedSource.SourceNodePath);
                SkinnedMeshRenderer liveRenderer = liveNode != null ? liveNode.GetComponent<SkinnedMeshRenderer>() : null;

                Texture mainTexture = liveRenderer != null && liveRenderer.sharedMaterial != null
                    ? liveRenderer.sharedMaterial.mainTexture
                    : null;

                if (!VatPreviewMaterial.TryCreate(textureSet, part, mainTexture, out VatPreviewMaterial material, out string createFailureMessage))
                {
                    continue;
                }

                bindings.Add(new Binding
                {
                    Part = part,
                    Material = material,
                    LiveRenderer = liveRenderer,
                    HasFrame = false,
                });
            }

            if (enabled)
            {
                ApplyRendererDisable();
            }
        }

        public void Sync(BlobAssetReference<ClipRegistryBlob> registry, ulong clipId, float normalizedTime)
        {
            if (!registry.IsCreated)
            {
                MarkAllFramesMissing();
                return;
            }

            if (!TryResolveClipIndex(ref registry.Value, clipId, out int clipIndex))
            {
                MarkAllFramesMissing();
                return;
            }

            ref ClipBlob clip = ref registry.Value.clips[clipIndex];
            for (int bindingIndex = 0; bindingIndex < bindings.Count; bindingIndex++)
            {
                Binding binding = bindings[bindingIndex];
                int targetIndex = binding.Part.targetId == 0u
                    ? -1
                    : ResolveTargetIndex(ref registry.Value, binding.Part.targetId);

                if (VatPreviewFrameResolver.TryResolveGlobalFrame(ref clip, targetIndex, normalizedTime, out float globalFrame))
                {
                    binding.Material.SetFrame(globalFrame, globalFrame, 0f);
                    binding.HasFrame = true;
                }
                else
                {
                    binding.HasFrame = false;
                }
            }
        }

        // Must be called between PreviewRenderUtility.BeginPreview and camera.Render.
        public void Draw(PreviewRenderUtility renderUtility)
        {
            if (!enabled || skeletonInstanceRoot == null)
            {
                return;
            }

            Matrix4x4 rootLocalToWorldMatrix = skeletonInstanceRoot.transform.localToWorldMatrix;
            for (int bindingIndex = 0; bindingIndex < bindings.Count; bindingIndex++)
            {
                Binding binding = bindings[bindingIndex];
                if (!binding.HasFrame || binding.Material == null || binding.Part.runtimeMesh == null)
                {
                    continue;
                }
                renderUtility.DrawMesh(binding.Part.runtimeMesh, rootLocalToWorldMatrix, binding.Material.Material, 0);
            }
        }

        public void Dispose()
        {
            ReleaseBindings();
            enabled = false;
        }

        private void ReleaseBindings()
        {
            UndoRendererDisable();
            for (int bindingIndex = 0; bindingIndex < bindings.Count; bindingIndex++)
            {
                bindings[bindingIndex].Material?.Dispose();
            }
            bindings.Clear();
            skeletonInstanceRoot = null;
        }

        private void ApplyRendererDisable()
        {
            for (int bindingIndex = 0; bindingIndex < bindings.Count; bindingIndex++)
            {
                SkinnedMeshRenderer liveRenderer = bindings[bindingIndex].LiveRenderer;
                if (liveRenderer == null || !liveRenderer.enabled)
                {
                    continue;
                }
                liveRenderer.enabled = false;
                renderersDisabledByOverlay.Add(liveRenderer);
            }
        }

        private void UndoRendererDisable()
        {
            for (int rendererIndex = 0; rendererIndex < renderersDisabledByOverlay.Count; rendererIndex++)
            {
                SkinnedMeshRenderer disabledRenderer = renderersDisabledByOverlay[rendererIndex];
                if (disabledRenderer != null)
                {
                    disabledRenderer.enabled = true;
                }
            }
            renderersDisabledByOverlay.Clear();
        }

        private void MarkAllFramesMissing()
        {
            for (int bindingIndex = 0; bindingIndex < bindings.Count; bindingIndex++)
            {
                bindings[bindingIndex].HasFrame = false;
            }
        }

        private static bool TryResolveClipIndex(ref ClipRegistryBlob registry, ulong clipId, out int clipIndex)
        {
            for (int index = 0; index < registry.sortedClipIds.Length; index++)
            {
                if (registry.sortedClipIds[index] == clipId)
                {
                    clipIndex = index;
                    return true;
                }
            }
            clipIndex = -1;
            return false;
        }

        private static int ResolveTargetIndex(ref ClipRegistryBlob registry, uint targetId)
        {
            for (int index = 0; index < registry.sortedTargetIds.Length; index++)
            {
                if (registry.sortedTargetIds[index] == targetId)
                {
                    return index;
                }
            }
            return -1;
        }
    }
}
