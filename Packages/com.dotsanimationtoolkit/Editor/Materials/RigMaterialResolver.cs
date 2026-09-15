// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    public sealed class RigMaterialUsage
    {
        public Material Material;
        public readonly List<RigTargetDefinition> Targets = new List<RigTargetDefinition>();

        // Renderer paths below the prefab root that use this material but match no rig target's sourceNodePath.
        public readonly List<string> UnmappedNodePaths = new List<string>();
    }

    /// <summary>Lists every material a rig's source prefab renders with, and which rig targets use each one.</summary>
    public static class RigMaterialResolver
    {
        public static List<RigMaterialUsage> Resolve(RigAsset rig)
        {
            List<RigMaterialUsage> emptyResult = new List<RigMaterialUsage>();
            if (rig == null || rig.sourcePrefab == null)
            {
                return emptyResult;
            }

            Transform prefabRoot = rig.sourcePrefab.transform;
            Dictionary<string, RigTargetDefinition> targetByPath = new Dictionary<string, RigTargetDefinition>(StringComparer.Ordinal);
            for (int targetIndex = 0; targetIndex < rig.targets.Count; targetIndex++)
            {
                RigTargetDefinition target = rig.targets[targetIndex];
                if (target == null || string.IsNullOrEmpty(target.sourceNodePath))
                {
                    continue;
                }
                if (!targetByPath.ContainsKey(target.sourceNodePath))
                {
                    targetByPath[target.sourceNodePath] = target;
                }
            }

            Dictionary<Material, RigMaterialUsage> usageByMaterial = new Dictionary<Material, RigMaterialUsage>();
            List<RigMaterialUsage> orderedUsages = new List<RigMaterialUsage>();

            Renderer[] renderers = rig.sourcePrefab.GetComponentsInChildren<Renderer>(true);
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                Renderer renderer = renderers[rendererIndex];
                string path = PrefabAuthoringBridge.GetHierarchyPath(renderer.transform, prefabRoot);
                targetByPath.TryGetValue(path, out RigTargetDefinition matchedTarget);

                Material[] sharedMaterials = renderer.sharedMaterials;
                for (int materialIndex = 0; materialIndex < sharedMaterials.Length; materialIndex++)
                {
                    Material material = sharedMaterials[materialIndex];
                    if (material == null)
                    {
                        continue;
                    }

                    if (!usageByMaterial.TryGetValue(material, out RigMaterialUsage usage))
                    {
                        usage = new RigMaterialUsage { Material = material };
                        usageByMaterial[material] = usage;
                        orderedUsages.Add(usage);
                    }

                    if (matchedTarget != null)
                    {
                        if (!usage.Targets.Contains(matchedTarget))
                        {
                            usage.Targets.Add(matchedTarget);
                        }
                    }
                    else if (!usage.UnmappedNodePaths.Contains(path))
                    {
                        usage.UnmappedNodePaths.Add(path);
                    }
                }
            }

            orderedUsages.Sort((first, second) => string.Compare(first.Material.name, second.Material.name, StringComparison.OrdinalIgnoreCase));
            return orderedUsages;
        }

        public static void CollectFlipbookBindingWarnings(
            RigMaterialUsage usage, ClipSetAsset clipSet, List<ValidationMessage> output)
        {
            if (usage == null || usage.Material == null || clipSet == null)
            {
                return;
            }
            if (usage.Material.HasProperty(MaterialContractValidation.TextureArraySamplerPropertyName))
            {
                return;
            }

            List<(FlipbookAsset Flipbook, RigTargetDefinition Target)> reportedPairs =
                new List<(FlipbookAsset Flipbook, RigTargetDefinition Target)>();

            for (int clipIndex = 0; clipIndex < clipSet.clips.Count; clipIndex++)
            {
                ClipAsset clip = clipSet.clips[clipIndex];
                if (clip == null)
                {
                    continue;
                }

                for (int trackIndex = 0; trackIndex < clip.spriteTracks.Count; trackIndex++)
                {
                    SpriteTrack track = clip.spriteTracks[trackIndex];
                    if (track == null || track.flipbook == null)
                    {
                        continue;
                    }

                    for (int targetIndex = 0; targetIndex < usage.Targets.Count; targetIndex++)
                    {
                        RigTargetDefinition target = usage.Targets[targetIndex];
                        if (!TrackTargetMatchResolver.TrackBindsTarget(track.targetId, track.tagId, target))
                        {
                            continue;
                        }

                        bool alreadyReported = false;
                        for (int pairIndex = 0; pairIndex < reportedPairs.Count; pairIndex++)
                        {
                            if (ReferenceEquals(reportedPairs[pairIndex].Flipbook, track.flipbook) &&
                                ReferenceEquals(reportedPairs[pairIndex].Target, target))
                            {
                                alreadyReported = true;
                                break;
                            }
                        }
                        if (alreadyReported)
                        {
                            continue;
                        }
                        reportedPairs.Add((track.flipbook, target));

                        string partName = string.IsNullOrEmpty(target.displayName) ? target.sourceNodePath : target.displayName;
                        string text = $"Flipbook '{track.flipbook.name}' binds part '{partName}', but material '{usage.Material.name}' has no _MainTexArray, so the flipbook's frames never reach the screen.";
                        output.Add(new ValidationMessage(ValidationSeverity.Warning, ValidationCode.None, usage.Material, text));
                    }
                }
            }
        }
    }
}
