// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    public static class VatMaterialUpgrader
    {
        private const string VatBoneTexturePropertyName = "_VatBoneTex";

        public static void UpgradeMaterialsForSet(VatTextureSetAsset textureSet,
            IReadOnlyList<VatBakeSource> bakedSources, List<string> reportLines)
        {
            if (textureSet.flavor != VatFlavor.BoneMatrix)
            {
                reportLines.Add("Materials left alone: a vertex-flavour bake has no shipped shader to switch to.");
                return;
            }

            for (int sourceIndex = 0; sourceIndex < bakedSources.Count; sourceIndex++)
            {
                VatBakeSource source = bakedSources[sourceIndex];
                Material material = source.PrefabRenderer != null ? source.PrefabRenderer.sharedMaterial : null;
                if (material == null)
                {
                    reportLines.Add(source.DisplayName + ": no material, nothing to switch");
                    continue;
                }

                string sharingPartNames = FindNamesOfOtherSourcesSharingMaterial(bakedSources, sourceIndex, material);
                if (sharingPartNames.Length > 0)
                {
                    reportLines.Add(source.DisplayName + ": refused, its material is shared with "
                        + sharingPartNames + "; give each part its own material.");
                    continue;
                }

                if (!textureSet.TryGetPart(source.TargetId, out VatPartTextures part) || part.boneTexture == null)
                {
                    reportLines.Add(source.DisplayName + ": not baked, material left alone");
                    continue;
                }

                reportLines.Add(UpgradeOneMaterial(source.DisplayName, material, part));
            }
        }

        private static string FindNamesOfOtherSourcesSharingMaterial(IReadOnlyList<VatBakeSource> bakedSources,
            int sourceIndex, Material material)
        {
            List<string> sharingNames = new List<string>();
            for (int otherIndex = 0; otherIndex < bakedSources.Count; otherIndex++)
            {
                if (otherIndex == sourceIndex)
                {
                    continue;
                }

                VatBakeSource other = bakedSources[otherIndex];
                if (other.PrefabRenderer != null && other.PrefabRenderer.sharedMaterial == material)
                {
                    sharingNames.Add(other.DisplayName);
                }
            }

            return string.Join(", ", sharingNames);
        }

        private static string UpgradeOneMaterial(string displayName, Material material, VatPartTextures part)
        {
            if (!material.HasProperty(VatBoneTexturePropertyName))
            {
                return displayName + ": " + material.name + " has no " + VatBoneTexturePropertyName
                    + " input, so its shader cannot play VAT; use ToolkitVatCrowdUnlit or add the VAT inputs to your shader (see shader-contract.md).";
            }

            // Same property mapping as VatPreviewMaterial so the preview and the real material agree.
            material.SetTexture(VatBoneTexturePropertyName, part.boneTexture);
            material.SetVector("_VatTexelParams",
                new Vector4(part.textureWidth, part.boneTexture.height, part.rowsPerFrame, part.boneCount));
            EditorUtility.SetDirty(material);
            if (AssetDatabase.Contains(material))
            {
                AssetDatabase.SaveAssetIfDirty(material);
            }

            return displayName + ": VAT textures set on " + material.name;
        }
    }
}
