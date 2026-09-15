// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using Unity.Mathematics;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Poses a VAT bake preview's non-VAT nodes (cutout quads, flipbooks) on the rig's source copy
    /// so the preview shows the whole animation, not just the sampled VAT mesh.
    /// </summary>
    public sealed class VatPreviewPartPoser : IDisposable, RegistryTargetPoser.ITargetPoseWriter
    {
        private struct NonVatPartBinding
        {
            public Transform Node;
            public Renderer PartRenderer;
            public TargetRestPose RestPose;
        }

        private static readonly int ImageIndexPropertyId = Shader.PropertyToID("_ImageIndex");
        private static readonly int AtlasFramePropertyId = Shader.PropertyToID("_AtlasFrame");

        private readonly RegistryTargetPoser targetPoser = new RegistryTargetPoser();
        private readonly Dictionary<uint, NonVatPartBinding> nonVatPartBindings = new Dictionary<uint, NonVatPartBinding>();
        private readonly HashSet<Renderer> vatPartRenderers = new HashSet<Renderer>();
        private readonly HashSet<Renderer> renderersWithWrittenPropertyBlocks = new HashSet<Renderer>();
        private readonly MaterialPropertyBlock scratchPropertyBlock = new MaterialPropertyBlock();
        private bool hasPosedSinceRebuild;

        public bool HasNonVatParts
        {
            get { return nonVatPartBindings.Count > 0; }
        }

        public bool Rebuild(RigAsset rig, ClipSetAsset clipSet, VatTextureSetAsset textureSet, GameObject sourceCopyRoot)
        {
            RestoreRestPose();
            nonVatPartBindings.Clear();
            vatPartRenderers.Clear();
            hasPosedSinceRebuild = false;

            if (rig == null || clipSet == null || sourceCopyRoot == null)
            {
                return false;
            }

            HashSet<uint> vatPartTargetIds = new HashSet<uint>();
            if (textureSet != null && textureSet.parts != null)
            {
                for (int partIndex = 0; partIndex < textureSet.parts.Count; partIndex++)
                {
                    VatPartTextures vatPartTextures = textureSet.parts[partIndex];
                    if (vatPartTextures != null)
                    {
                        vatPartTargetIds.Add(vatPartTextures.targetId);
                    }
                }
            }

            for (int targetIndex = 0; targetIndex < rig.targets.Count; targetIndex++)
            {
                RigTargetDefinition targetDefinition = rig.targets[targetIndex];
                if (targetDefinition == null || string.IsNullOrEmpty(targetDefinition.sourceNodePath))
                {
                    continue;
                }

                uint targetId = targetDefinition.Id.Value;
                if (vatPartTargetIds.Contains(targetId))
                {
                    continue;
                }

                Transform node = sourceCopyRoot.transform.Find(targetDefinition.sourceNodePath);
                if (node == null)
                {
                    continue;
                }

                // restSliceIndex lives on the authored RigTargetAuthoring, not the rig target
                // definition itself; a node the baker never got to (no authoring yet) still poses,
                // just always from slice 0.
                RigTargetAuthoring targetAuthoring = node.GetComponent<RigTargetAuthoring>();
                int restSliceIndex = targetAuthoring != null ? targetAuthoring.restSliceIndex : 0;

                nonVatPartBindings[targetId] = new NonVatPartBinding
                {
                    Node = node,
                    PartRenderer = node.GetComponent<Renderer>(),
                    RestPose = RestPoseCapture.FromTransform(node, restSliceIndex),
                };
            }

            List<VatBakeSource> vatBakeSources;
            string failureMessage;
            if (VatBakeSourceResolver.TryResolve(rig, out vatBakeSources, out failureMessage))
            {
                for (int sourceIndex = 0; sourceIndex < vatBakeSources.Count; sourceIndex++)
                {
                    VatBakeSource vatBakeSource = vatBakeSources[sourceIndex];
                    Transform vatNode = string.IsNullOrEmpty(vatBakeSource.SourceNodePath)
                        ? sourceCopyRoot.transform
                        : sourceCopyRoot.transform.Find(vatBakeSource.SourceNodePath);
                    if (vatNode == null)
                    {
                        continue;
                    }

                    SkinnedMeshRenderer vatSkinnedMeshRenderer = vatNode.GetComponent<SkinnedMeshRenderer>();
                    if (vatSkinnedMeshRenderer != null)
                    {
                        vatPartRenderers.Add(vatSkinnedMeshRenderer);
                    }
                }
            }

            targetPoser.Rebuild(rig, new ClipSetAsset[] { clipSet });
            return targetPoser.LastOutcome == RegistryBuildOutcome.Built;
        }

        public bool IsVatPartRenderer(Renderer renderer)
        {
            return vatPartRenderers.Contains(renderer);
        }

        public void PoseAt(ulong clipId, float normalizedTime)
        {
            if (targetPoser.PoseTargets(clipId, normalizedTime, this))
            {
                hasPosedSinceRebuild = true;
            }
        }

        bool RegistryTargetPoser.ITargetPoseWriter.TryGetRestPose(uint targetId, out TargetRestPose restPose)
        {
            NonVatPartBinding binding;
            if (nonVatPartBindings.TryGetValue(targetId, out binding) && binding.Node != null)
            {
                restPose = binding.RestPose;
                return true;
            }

            restPose = default(TargetRestPose);
            return false;
        }

        void RegistryTargetPoser.ITargetPoseWriter.WritePose(uint targetId, in TargetPose pose)
        {
            NonVatPartBinding binding;
            if (!nonVatPartBindings.TryGetValue(targetId, out binding) || binding.Node == null)
            {
                return;
            }

            // Local-space rest, matching the cutscene preview's convention: these nodes are real,
            // nested children of the source copy, so writing the Clip Editor's root-relative rest
            // here would place every child of the node in the wrong spot.
            binding.Node.localPosition = new Vector3(pose.localPosition.x, pose.localPosition.y, pose.localPosition.z);
            float3 rotationDegrees = math.degrees(pose.rotation);
            binding.Node.localRotation = Quaternion.Euler(rotationDegrees.x, rotationDegrees.y, rotationDegrees.z);
            binding.Node.localScale = new Vector3(pose.scale.x, pose.scale.y, pose.scale.z);

            if (binding.PartRenderer == null)
            {
                return;
            }

            binding.PartRenderer.GetPropertyBlock(scratchPropertyBlock);
            scratchPropertyBlock.SetFloat(ImageIndexPropertyId, pose.sliceIndex);
            scratchPropertyBlock.SetVector(AtlasFramePropertyId, new Vector4(pose.atlasRect.x, pose.atlasRect.y, pose.atlasRect.z, pose.atlasRect.w));
            binding.PartRenderer.SetPropertyBlock(scratchPropertyBlock);
            renderersWithWrittenPropertyBlocks.Add(binding.PartRenderer);
        }

        public void RestoreRestPose()
        {
            foreach (KeyValuePair<uint, NonVatPartBinding> bindingEntry in nonVatPartBindings)
            {
                NonVatPartBinding binding = bindingEntry.Value;
                if (binding.Node == null)
                {
                    continue;
                }

                binding.Node.localPosition = new Vector3(binding.RestPose.localPosition.x, binding.RestPose.localPosition.y, binding.RestPose.localPosition.z);
                float3 restRotationDegrees = math.degrees(binding.RestPose.rotation);
                binding.Node.localRotation = Quaternion.Euler(restRotationDegrees.x, restRotationDegrees.y, restRotationDegrees.z);
                binding.Node.localScale = new Vector3(binding.RestPose.scale.x, binding.RestPose.scale.y, binding.RestPose.scale.z);
            }

            foreach (Renderer renderer in renderersWithWrittenPropertyBlocks)
            {
                if (renderer != null)
                {
                    renderer.SetPropertyBlock(null);
                }
            }
            renderersWithWrittenPropertyBlocks.Clear();
        }

        public void Dispose()
        {
            RestoreRestPose();
            targetPoser.Dispose();
            nonVatPartBindings.Clear();
            vatPartRenderers.Clear();
        }
    }
}
