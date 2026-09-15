// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using DotsAnimationToolkit.Authoring;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>The Sprite Sheets tab's catalog: every SpriteSheetAsset plus every Texture2DArray no sheet wraps yet.</summary>
    public sealed class SpriteSheetCatalogColumn : ToolkitCatalogColumn<UnityEngine.Object>
    {
        public event Action<SpriteSheetAsset> SheetSelected;
        public event Action<Texture2DArray> ArraySelected;
        public event Action<SpriteSheetAsset, string> SheetRenameRequested;
        public event Action<SpriteSheetAsset> SheetDeleteRequested;

        public SpriteSheetAsset SelectedSheet => SelectedAsset as SpriteSheetAsset;
        public Texture2DArray SelectedArray => SelectedAsset as Texture2DArray;

        public SpriteSheetCatalogColumn() : base(BuildOptions())
        {
            AssetSelected += RaiseRowSelected;
            RenameRequested += RaiseSheetRenameRequested;
            DeleteRequested += RaiseSheetDeleteRequested;
        }

        public void RescanProject()
        {
            Rescan();
        }

        public void SetSelectedSheet(SpriteSheetAsset sheet)
        {
            Select(sheet);
        }

        public void SetSelectedArray(Texture2DArray array)
        {
            Select(array);
        }

        private static CatalogColumnOptions<UnityEngine.Object> BuildOptions()
        {
            return new CatalogColumnOptions<UnityEngine.Object>
            {
                elementName = "sprite-sheet-catalog-column",
                namePrefix = "sprite-sheets",
                title = "Sheets",
                newButtonIconName = "d_Toolbar Plus",
                newButtonTooltip = "Create a sprite sheet: choose its name and folder",
                refreshButtonIconName = "d_Refresh",
                refreshButtonTooltip = "Rescan the project for sprite sheets and texture arrays",
                emptyProjectMessage = "No sprite sheets or texture arrays in this project yet. Press New.",
                emptySearchMessage = "No sprite sheets match your search.",
                scan = ScanProjectRows,
                secondLine = DescribeRow,
                tooltip = DescribeRowPath,
                allowRename = true,
                allowDelete = true,
                rowAllowsRenameAndDelete = row => row is SpriteSheetAsset,
            };
        }

        // Sheets first found, then every array whose path no sheet's texture points at; one name-sorted list.
        private static IReadOnlyList<UnityEngine.Object> ScanProjectRows()
        {
            List<UnityEngine.Object> foundRows = new List<UnityEngine.Object>();
            HashSet<string> wrappedArrayPaths = new HashSet<string>(StringComparer.Ordinal);

            string[] sheetGuids = AssetDatabase.FindAssets("t:SpriteSheetAsset");
            for (int guidIndex = 0; guidIndex < sheetGuids.Length; guidIndex++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(sheetGuids[guidIndex]);
                SpriteSheetAsset sheet = AssetDatabase.LoadAssetAtPath<SpriteSheetAsset>(assetPath);
                if (sheet == null)
                {
                    continue;
                }

                foundRows.Add(sheet);
                if (sheet.texture != null)
                {
                    wrappedArrayPaths.Add(AssetDatabase.GetAssetPath(sheet.texture));
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

            SpriteSheetAsset sheet = row as SpriteSheetAsset;
            int frameCount = sheet != null && sheet.frames != null ? sheet.frames.Count : 0;
            if (sheet != null && sheet.IsImportedArray)
            {
                return frameCount.ToString() + " frames · "
                    + sheet.texture.width.ToString() + "×" + sheet.texture.height.ToString() + " · imported";
            }

            if (sheet != null && sheet.texture != null)
            {
                return frameCount.ToString() + " frames · "
                    + sheet.layerSize.x.ToString() + "x" + sheet.layerSize.y.ToString();
            }

            return frameCount.ToString() + " frames · not baked";
        }

        private static string DescribeRowPath(UnityEngine.Object row)
        {
            SpriteSheetAsset sheet = row as SpriteSheetAsset;
            if (sheet != null && !sheet.IsImportedArray)
            {
                return sheet.outputPath;
            }

            UnityEngine.Object describedAsset = sheet != null ? sheet.texture : row;
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

            SheetSelected?.Invoke(row as SpriteSheetAsset);
        }

        private void RaiseSheetRenameRequested(UnityEngine.Object row, string newName)
        {
            SpriteSheetAsset sheet = row as SpriteSheetAsset;
            if (sheet != null)
            {
                SheetRenameRequested?.Invoke(sheet, newName);
            }
        }

        private void RaiseSheetDeleteRequested(UnityEngine.Object row)
        {
            SpriteSheetAsset sheet = row as SpriteSheetAsset;
            if (sheet != null)
            {
                SheetDeleteRequested?.Invoke(sheet);
            }
        }
    }
}
