// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using DotsAnimationToolkit.Authoring;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>The Flipbooks tab's catalog: every FlipbookAsset plus every Texture2DArray no flipbook wraps yet.</summary>
    public sealed class FlipbookCatalogColumn : ToolkitCatalogColumn<UnityEngine.Object>
    {
        public event Action<FlipbookAsset> FlipbookSelected;
        public event Action<Texture2DArray> ArraySelected;
        public event Action<FlipbookAsset, string> FlipbookRenameRequested;
        public event Action<FlipbookAsset> FlipbookDeleteRequested;

        public FlipbookAsset SelectedFlipbook => SelectedAsset as FlipbookAsset;
        public Texture2DArray SelectedArray => SelectedAsset as Texture2DArray;

        public FlipbookCatalogColumn() : base(BuildOptions())
        {
            AssetSelected += RaiseRowSelected;
            RenameRequested += RaiseFlipbookRenameRequested;
            DeleteRequested += RaiseFlipbookDeleteRequested;
        }

        public void RescanProject()
        {
            Rescan();
        }

        public void SetSelectedFlipbook(FlipbookAsset flipbook)
        {
            Select(flipbook);
        }

        public void SetSelectedArray(Texture2DArray array)
        {
            Select(array);
        }

        private static CatalogColumnOptions<UnityEngine.Object> BuildOptions()
        {
            return new CatalogColumnOptions<UnityEngine.Object>
            {
                elementName = "flipbook-catalog-column",
                namePrefix = "flipbooks",
                // No title: the Flipbooks sidebar owns the header and hoists HeaderActions into it.
                title = string.Empty,
                newButtonIconName = "d_Toolbar Plus",
                newButtonTooltip = "Create a flipbook: choose its name and folder",
                refreshButtonIconName = "d_Refresh",
                refreshButtonTooltip = "Rescan the project for flipbooks and texture arrays",
                emptyProjectMessage = "No flipbooks or texture arrays in this project yet. Press New.",
                emptySearchMessage = "No flipbooks match your search.",
                scan = ScanProjectRows,
                secondLine = DescribeRow,
                tooltip = DescribeRowPath,
                allowRename = true,
                allowDelete = true,
                rowAllowsRenameAndDelete = row => row is FlipbookAsset,
            };
        }

        // Flipbooks first found, then every array whose path no flipbook's texture points at; one name-sorted list.
        private static IReadOnlyList<UnityEngine.Object> ScanProjectRows()
        {
            List<UnityEngine.Object> foundRows = new List<UnityEngine.Object>();
            HashSet<string> wrappedArrayPaths = new HashSet<string>(StringComparer.Ordinal);

            string[] flipbookGuids = AssetDatabase.FindAssets("t:FlipbookAsset");
            for (int guidIndex = 0; guidIndex < flipbookGuids.Length; guidIndex++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(flipbookGuids[guidIndex]);
                FlipbookAsset flipbook = AssetDatabase.LoadAssetAtPath<FlipbookAsset>(assetPath);
                if (flipbook == null)
                {
                    continue;
                }

                foundRows.Add(flipbook);
                if (flipbook.texture != null)
                {
                    wrappedArrayPaths.Add(AssetDatabase.GetAssetPath(flipbook.texture));
                }
            }

            string[] arrayGuids = AssetDatabase.FindAssets("t:Texture2DArray");
            for (int guidIndex = 0; guidIndex < arrayGuids.Length; guidIndex++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(arrayGuids[guidIndex]);
                if (wrappedArrayPaths.Contains(assetPath))
                {
                    continue;
                }

                Texture2DArray array = AssetDatabase.LoadAssetAtPath<Texture2DArray>(assetPath);
                if (array != null)
                {
                    foundRows.Add(array);
                }
            }

            foundRows.Sort((firstRow, secondRow) =>
                string.Compare(firstRow.name, secondRow.name, StringComparison.OrdinalIgnoreCase));

            return foundRows;
        }

        private static string DescribeRow(UnityEngine.Object row)
        {
            Texture2DArray bareArray = row as Texture2DArray;
            if (bareArray != null)
            {
                return bareArray.depth.ToString() + " frames · "
                    + bareArray.width.ToString() + "×" + bareArray.height.ToString() + " · imported, unnamed";
            }

            FlipbookAsset flipbook = row as FlipbookAsset;
            int frameCount = flipbook != null && flipbook.frames != null ? flipbook.frames.Count : 0;
            if (flipbook != null && flipbook.IsImportedArray)
            {
                return frameCount.ToString() + " frames · "
                    + flipbook.texture.width.ToString() + "×" + flipbook.texture.height.ToString() + " · imported";
            }

            if (flipbook != null && flipbook.texture != null)
            {
                return frameCount.ToString() + " frames · "
                    + flipbook.layerSize.x.ToString() + "x" + flipbook.layerSize.y.ToString();
            }

            return frameCount.ToString() + " frames · not baked";
        }

        private static string DescribeRowPath(UnityEngine.Object row)
        {
            FlipbookAsset flipbook = row as FlipbookAsset;
            if (flipbook != null && !flipbook.IsImportedArray)
            {
                return flipbook.outputPath;
            }

            UnityEngine.Object describedAsset = flipbook != null ? flipbook.texture : row;
            return describedAsset != null ? AssetDatabase.GetAssetPath(describedAsset) : string.Empty;
        }

        private void RaiseRowSelected(UnityEngine.Object row)
        {
            Texture2DArray bareArray = row as Texture2DArray;
            if (bareArray != null)
            {
                ArraySelected?.Invoke(bareArray);
                return;
            }

            FlipbookSelected?.Invoke(row as FlipbookAsset);
        }

        private void RaiseFlipbookRenameRequested(UnityEngine.Object row, string newName)
        {
            FlipbookAsset flipbook = row as FlipbookAsset;
            if (flipbook != null)
            {
                FlipbookRenameRequested?.Invoke(flipbook, newName);
            }
        }

        private void RaiseFlipbookDeleteRequested(UnityEngine.Object row)
        {
            FlipbookAsset flipbook = row as FlipbookAsset;
            if (flipbook != null)
            {
                FlipbookDeleteRequested?.Invoke(flipbook);
            }
        }
    }
}
