// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using System.Text;
using DotsAnimationToolkit.Authoring;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// One VAT part a bake will produce: which rig target it stands for, and the skinned mesh in the
    /// rig's source prefab that it samples.
    /// </summary>
    public sealed class VatBakeSource
    {
        public uint TargetId;            // 0 = the rig names no VAT target and the prefab has one mesh
        public string DisplayName;       // the target's displayName, else the node's name
        public string SourceNodePath;    // empty = the prefab root itself
        public SkinnedMeshRenderer PrefabRenderer;
    }

    /// <summary>
    /// Reads a rig's source prefab and says which skinned meshes its VAT bake covers, then makes the
    /// single throwaway instance the bake poses. The rig is the whole input; nothing is dragged in.
    /// </summary>
    public static class VatBakeSourceResolver
    {
        public static bool TryResolve(RigAsset rig, out List<VatBakeSource> sources, out string failureMessage)
        {
            sources = new List<VatBakeSource>();
            failureMessage = string.Empty;

            if (rig == null)
            {
                failureMessage = "Assign the Rig these textures are baked for.";
                return false;
            }

            if (rig.sourcePrefab == null)
            {
                failureMessage = $"Rig '{rig.name}' has no Source Prefab, so there is nothing to sample. Set one in the Clip Editor's Rigs tab.";
                return false;
            }

            Transform prefabRoot = rig.sourcePrefab.transform;
            SkinnedMeshRenderer[] skinnedRenderers = rig.sourcePrefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (skinnedRenderers.Length == 0)
            {
                failureMessage = $"'{rig.sourcePrefab.name}' has no skinned mesh. A VAT bake needs one; a rig of cutout quads is not a VAT subject.";
                return false;
            }

            Dictionary<string, SkinnedMeshRenderer> rendererByPath = new Dictionary<string, SkinnedMeshRenderer>(StringComparer.Ordinal);
            for (int rendererIndex = 0; rendererIndex < skinnedRenderers.Length; rendererIndex++)
            {
                SkinnedMeshRenderer renderer = skinnedRenderers[rendererIndex];
                string path = PrefabAuthoringBridge.GetHierarchyPath(renderer.transform, prefabRoot);
                rendererByPath[path] = renderer;
            }

            List<VatBakeSource> vatMeshTargetSources = new List<VatBakeSource>();
            List<VatBakeSource> anyKindTargetSources = new List<VatBakeSource>();
            for (int targetIndex = 0; targetIndex < rig.targets.Count; targetIndex++)
            {
                RigTargetDefinition target = rig.targets[targetIndex];
                if (target == null || string.IsNullOrEmpty(target.sourceNodePath))
                {
                    continue;
                }
                if (!rendererByPath.TryGetValue(target.sourceNodePath, out SkinnedMeshRenderer matchedRenderer))
                {
                    continue;
                }

                VatBakeSource source = new VatBakeSource
                {
                    TargetId = target.Id.Value,
                    DisplayName = string.IsNullOrEmpty(target.displayName) ? matchedRenderer.name : target.displayName,
                    SourceNodePath = target.sourceNodePath,
                    PrefabRenderer = matchedRenderer,
                };
                anyKindTargetSources.Add(source);
                if (target.kind == TargetKind.VatMesh)
                {
                    vatMeshTargetSources.Add(source);
                }
            }

            if (vatMeshTargetSources.Count > 0)
            {
                sources = vatMeshTargetSources;
                return true;
            }

            if (anyKindTargetSources.Count > 0)
            {
                sources = anyKindTargetSources;
                return true;
            }

            if (skinnedRenderers.Length == 1)
            {
                SkinnedMeshRenderer onlyRenderer = skinnedRenderers[0];
                string onlyPath = PrefabAuthoringBridge.GetHierarchyPath(onlyRenderer.transform, prefabRoot);
                sources.Add(new VatBakeSource
                {
                    TargetId = 0,
                    DisplayName = onlyRenderer.name,
                    SourceNodePath = onlyPath,
                    PrefabRenderer = onlyRenderer,
                });
                return true;
            }

            StringBuilder pathList = new StringBuilder();
            for (int rendererIndex = 0; rendererIndex < skinnedRenderers.Length; rendererIndex++)
            {
                if (rendererIndex > 0)
                {
                    pathList.Append(", ");
                }
                pathList.Append(PrefabAuthoringBridge.GetHierarchyPath(skinnedRenderers[rendererIndex].transform, prefabRoot));
            }

            failureMessage = $"'{rig.sourcePrefab.name}' has {skinnedRenderers.Length} skinned meshes and none of them is a rig target, so the bake cannot tell which to sample: {pathList}. Tick the ones you want in the Clip Editor's Rigs tab, and set their Kind to VAT Mesh.";
            return false;
        }

        public static bool TryCreateBakeInstance(RigAsset rig, out GameObject instanceRoot, out string failureMessage)
        {
            instanceRoot = null;
            failureMessage = string.Empty;

            if (rig == null || rig.sourcePrefab == null)
            {
                failureMessage = "Assign the Rig these textures are baked for.";
                return false;
            }

            // A throwaway copy: the bake poses it frame by frame, and posing the prefab asset
            // itself would corrupt every other user of that prefab.
            instanceRoot = UnityEngine.Object.Instantiate(rig.sourcePrefab);
            instanceRoot.name = rig.sourcePrefab.name;
            // Not PrefabUtility.InstantiatePrefab: a linked instance forwards edits back to the
            // source asset, and a bake pose written into the .prefab on disk is exactly that mistake.
            instanceRoot.hideFlags = HideFlags.HideAndDontSave;
            return true;
        }

        public static SkinnedMeshRenderer FindInInstance(GameObject instanceRoot, string sourceNodePath)
        {
            if (instanceRoot == null)
            {
                return null;
            }

            Transform node = PrefabAuthoringBridge.ResolveByPath(instanceRoot.transform, sourceNodePath);
            return node == null ? null : node.GetComponent<SkinnedMeshRenderer>();
        }
    }
}
