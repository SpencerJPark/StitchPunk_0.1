// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
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
            List<Material> materials = new List<Material>();
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
                    materials.Add(usage.Material);
                }
            }

            SetItems(materials);
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
                title = "Materials",
                emptyProjectMessage = "Pick a rig with a source prefab to list its materials.",
                emptySearchMessage = "No materials match your search.",
            };
        }

        private void RaiseMaterialSelected(Material material)
        {
            MaterialSelected?.Invoke(material);
        }
    }
}
