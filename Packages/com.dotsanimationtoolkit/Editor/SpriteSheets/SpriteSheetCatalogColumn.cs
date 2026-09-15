// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
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
        }

        public void RescanProject()
        {
            throw new NotImplementedException();
        }

        public void SetSelectedSheet(SpriteSheetAsset sheet)
        {
            throw new NotImplementedException();
        }

        private static CatalogColumnOptions<SpriteSheetAsset> BuildOptions()
        {
            return new CatalogColumnOptions<SpriteSheetAsset>();
        }
    }
}
