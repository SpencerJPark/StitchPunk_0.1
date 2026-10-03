// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    public sealed class CutoutCatalogColumn : ToolkitCatalogColumn<CutoutAsset>
    {
        public CutoutCatalogColumn() : base(BuildOptions())
        {
        }

        public void RescanProject()
        {
            Rescan();
        }

        public void SetSelectedCutout(CutoutAsset cutout)
        {
            Select(cutout);
        }

        private static CatalogColumnOptions<CutoutAsset> BuildOptions()
        {
            return new CatalogColumnOptions<CutoutAsset>
            {
                elementName = "cutout-catalog-column",
                namePrefix = "cutouts",
                // No title: the Cutouts sidebar owns the header.
                title = string.Empty,
                newButtonIconName = "d_Toolbar Plus",
                newButtonTooltip = "Start a cutout: pick the flipbook to draw it over",
                refreshButtonIconName = "d_Refresh",
                refreshButtonTooltip = "Rescan the project for cutouts",
                emptyProjectTitle = "No cutouts yet",
                emptyProjectMessage = "A cutout is a flat mesh drawn tight around a flipbook's art.",
                emptyProjectActionText = "New Cutout",
                emptySearchMessage = "No cutouts match your search.",
                scan = ScanProjectCutouts,
                secondLine = DescribeCutout,
                tooltip = DescribeCutoutTooltip,
                allowRename = true,
                allowDelete = true,
            };
        }

        private static IReadOnlyList<CutoutAsset> ScanProjectCutouts()
        {
            List<CutoutAsset> cutouts = new List<CutoutAsset>();
            string[] guids = AssetDatabase.FindAssets("t:CutoutAsset");
            for (int guidIndex = 0; guidIndex < guids.Length; guidIndex++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[guidIndex]);
                CutoutAsset cutout = AssetDatabase.LoadAssetAtPath<CutoutAsset>(assetPath);
                if (cutout != null)
                {
                    cutouts.Add(cutout);
                }
            }

            cutouts.Sort((CutoutAsset left, CutoutAsset right) =>
                string.Compare(left.name, right.name, StringComparison.OrdinalIgnoreCase));
            return cutouts;
        }

        private static string DescribeCutout(CutoutAsset cutout)
        {
            string flipbookName = cutout.flipbook != null ? cutout.flipbook.name : "no flipbook";
            return cutout.outlinePixels.Count + " verts · " + flipbookName;
        }

        private static string DescribeCutoutTooltip(CutoutAsset cutout)
        {
            string tooltipText = AssetDatabase.GetAssetPath(cutout);
            if (!string.IsNullOrEmpty(cutout.outputPath))
            {
                tooltipText += "\n" + cutout.outputPath;
            }

            return tooltipText;
        }
    }
}
