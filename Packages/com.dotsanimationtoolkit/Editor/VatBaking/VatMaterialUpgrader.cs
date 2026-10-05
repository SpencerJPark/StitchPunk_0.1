// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    public static class VatMaterialUpgrader
    {
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
            bool alreadyVat = (MaterialFeatureResolver.ReadFeatures(material) & MaterialFeature.Vat) != 0;
            if (!alreadyVat)
            {
                if (!MaterialFeatureResolver.IsToolkitShader(material.shader))
                {
                    return displayName + ": custom shader has no " + MaterialFeatureResolver.VatBoneTexturePropertyName
                        + ", add the VAT properties";
                }

                MaterialFeature wantedFeatures = MaterialFeatureResolver.ReadFeatures(material) | MaterialFeature.Vat;
                if (!MaterialAuthoringUtility.TrySetToolkitFeatures(material, wantedFeatures, out string resultMessage))
                {
                    return displayName + ": " + resultMessage;
                }
            }

            // Same property mapping as VatPreviewMaterial so the preview and the real material agree.
            material.SetTexture("_VatBoneTex", part.boneTexture);
            material.SetVector("_VatTexelParams",
                new Vector4(part.textureWidth, part.boneTexture.height, part.rowsPerFrame, part.boneCount));
            EditorUtility.SetDirty(material);
            if (AssetDatabase.Contains(material))
            {
                AssetDatabase.SaveAssetIfDirty(material);
            }

            return displayName + (alreadyVat ? ": textures set" : ": switched to VAT, textures set");
        }
    }
}
