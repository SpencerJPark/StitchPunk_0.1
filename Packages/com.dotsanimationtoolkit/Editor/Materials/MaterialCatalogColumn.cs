// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>The Materials tab's catalog: every material the bound rig's prefab uses, with the target kinds each serves.</summary>
    public sealed class MaterialCatalogColumn : ToolkitCatalogColumn<Material>
    {
        private readonly Dictionary<Material, RigMaterialUsage> usageByMaterial;

        public event Action<Material> MaterialSelected;

        public MaterialCatalogColumn() : this(new Dictionary<Material, RigMaterialUsage>())
        {
        }

        private MaterialCatalogColumn(Dictionary<Material, RigMaterialUsage> usageByMaterial)
            : base(BuildOptions(usageByMaterial))
        {
            this.usageByMaterial = usageByMaterial;
            AssetSelected += RaiseMaterialSelected;
        }

        public void SetUsages(IReadOnlyList<RigMaterialUsage> usages)
        {
            usageByMaterial.Clear();
            if (usages != null)
            {
                for (int usageIndex = 0; usageIndex < usages.Count; usageIndex++)
                {
                    RigMaterialUsage usage = usages[usageIndex];
                    if (usage == null || usage.Material == null || usageByMaterial.ContainsKey(usage.Material))
                    {
                        continue;
                    }

                    usageByMaterial.Add(usage.Material, usage);
                }
            }

            Rescan();
        }

        public void RescanProject()
        {
            Rescan();
        }

        public void SetSelectedMaterial(Material material)
        {
            Select(material);
        }

        private static CatalogColumnOptions<Material> BuildOptions(Dictionary<Material, RigMaterialUsage> usageByMaterial)
        {
            return new CatalogColumnOptions<Material>
            {
                elementName = "material-catalog-column",
                namePrefix = "materials",
                // No title: the sidebar owns the header.
                title = string.Empty,
                newButtonIconName = "d_Toolbar Plus",
                newButtonTooltip = "Create a material from the package's shader",
                refreshButtonIconName = "d_Refresh",
                refreshButtonTooltip = "Rescan the project for materials",
                emptyProjectTitle = "No materials yet",
                emptyProjectMessage = "A material is the look of a cutout or mesh: its texture, tint and cutoff. Pick a cutout or mesh to make one, or create one here.",
                emptyProjectActionText = "Create material",
                emptySearchMessage = "No materials match your search.",
                scan = () => ScanToolkitMaterials(usageByMaterial),
                secondLine = material => DescribeMaterial(usageByMaterial, material),
                tooltip = material => AssetDatabase.GetAssetPath(material),
                thumbnail = material => AssetPreview.GetAssetPreview(material),
                allowRename = false,
                allowDelete = false,
            };
        }

        private static IReadOnlyList<Material> ScanToolkitMaterials(Dictionary<Material, RigMaterialUsage> usageByMaterial)
        {
            HashSet<Material> distinctMaterials = new HashSet<Material>(usageByMaterial.Keys);
            string[] guids = AssetDatabase.FindAssets("t:Material", new[] { "Assets" });
            for (int guidIndex = 0; guidIndex < guids.Length; guidIndex++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[guidIndex]);
                Material material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
                if (material != null && MaterialFeatureResolver.IsToolkitShader(material.shader))
                {
                    distinctMaterials.Add(material);
                }
            }

            List<Material> materials = new List<Material>(distinctMaterials);
            materials.Sort((Material left, Material right) =>
                string.Compare(left.name, right.name, StringComparison.OrdinalIgnoreCase));
            return materials;
        }

        private static string DescribeMaterial(Dictionary<Material, RigMaterialUsage> usageByMaterial, Material material)
        {
            if (material == null)
            {
                return string.Empty;
            }

            if (usageByMaterial.ContainsKey(material))
            {
                string usageDescription = DescribeUsage(usageByMaterial, material);
                if (!string.IsNullOrEmpty(usageDescription))
                {
                    return usageDescription;
                }
            }

            MaterialFeature features = MaterialFeatureResolver.ReadFeatures(material);
            string featureLine;
            if ((features & MaterialFeature.Vat) != 0)
            {
                bool isBaked = material.HasProperty(MaterialFeatureResolver.VatBoneTexturePropertyName)
                    && material.GetTexture(MaterialFeatureResolver.VatBoneTexturePropertyName) != null;
                featureLine = isBaked ? "VAT" : "VAT · not baked";
            }
            else if ((features & MaterialFeature.Flipbook) != 0)
            {
                featureLine = "Flipbook";
            }
            else
            {
                featureLine = "Static";
            }

            if (!MaterialFeatureResolver.IsToolkitShader(material.shader))
            {
                featureLine += " · custom shader";
            }

            return featureLine;
        }

        private static string DescribeUsage(Dictionary<Material, RigMaterialUsage> usageByMaterial, Material material)
        {
            if (material == null || !usageByMaterial.TryGetValue(material, out RigMaterialUsage usage) || usage == null)
            {
                return string.Empty;
            }

            if (usage.Targets.Count == 0)
            {
                return "no rig target";
            }

            TargetKind[] kindsInEnumOrder = { TargetKind.Quad, TargetKind.VatMesh, TargetKind.FlipbookPlane };
            Dictionary<TargetKind, int> countByKind = new Dictionary<TargetKind, int>();
            Dictionary<TargetKind, string> firstDisplayNameByKind = new Dictionary<TargetKind, string>();
            for (int targetIndex = 0; targetIndex < usage.Targets.Count; targetIndex++)
            {
                RigTargetDefinition target = usage.Targets[targetIndex];
                if (target == null)
                {
                    continue;
                }

                if (countByKind.TryGetValue(target.kind, out int existingCount))
                {
                    countByKind[target.kind] = existingCount + 1;
                }
                else
                {
                    countByKind[target.kind] = 1;
                    firstDisplayNameByKind[target.kind] = target.displayName;
                }
            }

            List<string> kindSegments = new List<string>();
            List<ValidationMessage> validationMessages = new List<ValidationMessage>();
            for (int kindIndex = 0; kindIndex < kindsInEnumOrder.Length; kindIndex++)
            {
                TargetKind kind = kindsInEnumOrder[kindIndex];
                if (!countByKind.TryGetValue(kind, out int kindCount))
                {
                    continue;
                }

                string kindLabel = DescribeTargetKind(kind);
                kindSegments.Add(kindCount > 1 ? kindLabel + " ×" + kindCount.ToString() : kindLabel);

                MaterialContractValidation.Validate(material, kind, firstDisplayNameByKind[kind], validationMessages);
            }

            HashSet<string> distinctErrorTexts = new HashSet<string>();
            for (int messageIndex = 0; messageIndex < validationMessages.Count; messageIndex++)
            {
                ValidationMessage validationMessage = validationMessages[messageIndex];
                if (validationMessage.IsError)
                {
                    distinctErrorTexts.Add(validationMessage.text);
                }
            }

            string usageDescription = string.Join(" · ", kindSegments);
            if (distinctErrorTexts.Count > 0)
            {
                usageDescription += distinctErrorTexts.Count > 1
                    ? " · " + distinctErrorTexts.Count.ToString() + " problems"
                    : " · 1 problem";
            }

            return usageDescription;
        }

        private static string DescribeTargetKind(TargetKind kind)
        {
            switch (kind)
            {
                case TargetKind.VatMesh:
                    return "VAT Mesh";
                case TargetKind.FlipbookPlane:
                    return "Flipbook Plane";
                default:
                    return "Quad";
            }
        }

        private void RaiseMaterialSelected(Material material)
        {
            MaterialSelected?.Invoke(material);
        }
    }
}
