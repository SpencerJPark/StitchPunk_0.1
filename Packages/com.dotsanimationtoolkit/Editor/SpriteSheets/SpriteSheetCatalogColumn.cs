// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using UnityEditor;
using DotsAnimationToolkit.Authoring;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>The Sprite Sheets tab's catalog: the shared column configured for SpriteSheetAsset.</summary>
    public sealed class SpriteSheetCatalogColumn : ToolkitCatalogColumn<SpriteSheetAsset>
    {
        public event Action<SpriteSheetAsset> SheetSelected;
        public event Action<SpriteSheetAsset, string> SheetRenameRequested;
        public event Action<SpriteSheetAsset> SheetDeleteRequested;

        public SpriteSheetAsset SelectedSheet => SelectedAsset;

        public SpriteSheetCatalogColumn() : base(BuildOptions())
        {
            AssetSelected += RaiseSheetSelected;
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

        private static CatalogColumnOptions<SpriteSheetAsset> BuildOptions()
        {
            return new CatalogColumnOptions<SpriteSheetAsset>
            {
                elementName = "sprite-sheet-catalog-column",
                namePrefix = "sprite-sheets",
                title = "Sheets",
                newButtonIconName = "d_Toolbar Plus",
                newButtonTooltip = "Create a sprite sheet: choose its name and folder",
                refreshButtonIconName = "d_Refresh",
                refreshButtonTooltip = "Rescan the project for sprite sheets",
                emptyProjectMessage = "No sprite sheets in this project yet. Press New.",
                emptySearchMessage = "No sprite sheets match your search.",
                scan = ScanProjectSheets,
                secondLine = DescribeSheet,
                tooltip = DescribeSheetOutputPath,
                allowRename = true,
                allowDelete = true,
            };
        }

        private static IReadOnlyList<SpriteSheetAsset> ScanProjectSheets()
        {
            List<SpriteSheetAsset> foundSheets = new List<SpriteSheetAsset>();
            string[] sheetGuids = AssetDatabase.FindAssets("t:SpriteSheetAsset");
            for (int guidIndex = 0; guidIndex < sheetGuids.Length; guidIndex++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(sheetGuids[guidIndex]);
                SpriteSheetAsset sheet = AssetDatabase.LoadAssetAtPath<SpriteSheetAsset>(assetPath);
                if (sheet != null)
                {
                    foundSheets.Add(sheet);
                }
            }

            foundSheets.Sort((firstSheet, secondSheet) =>
                string.Compare(firstSheet.name, secondSheet.name, StringComparison.OrdinalIgnoreCase));

            return foundSheets;
        }

        private static string DescribeSheet(SpriteSheetAsset sheet)
        {
            int frameCount = sheet != null && sheet.frames != null ? sheet.frames.Count : 0;
            if (sheet != null && sheet.texture != null)
            {
                return frameCount.ToString() + " frames · "
                    + sheet.layerSize.x.ToString() + "x" + sheet.layerSize.y.ToString();
            }

            return frameCount.ToString() + " frames · not baked";
        }

        private static string DescribeSheetOutputPath(SpriteSheetAsset sheet)
        {
            return sheet != null ? sheet.outputPath : string.Empty;
        }

        private void RaiseSheetSelected(SpriteSheetAsset sheet)
        {
            SheetSelected?.Invoke(sheet);
        }

        private void RaiseSheetRenameRequested(SpriteSheetAsset sheet, string newName)
        {
            SheetRenameRequested?.Invoke(sheet, newName);
        }

        private void RaiseSheetDeleteRequested(SpriteSheetAsset sheet)
        {
            SheetDeleteRequested?.Invoke(sheet);
        }
    }
}
