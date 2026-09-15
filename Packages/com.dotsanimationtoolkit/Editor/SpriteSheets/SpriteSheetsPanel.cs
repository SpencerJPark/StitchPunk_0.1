// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using DotsAnimationToolkit.Authoring;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>The Sprite Sheets tab: catalogs, frame list and contact sheet around one sheet's working copy, with Bake, Save and discard.</summary>
    public sealed class SpriteSheetsPanel : VisualElement, IDisposable
    {
        public SpriteSheetAsset LoadedSheet { get; private set; }
        public bool HasUnsavedChanges { get; private set; }

        public SpriteSheetsPanel()
        {
        }

        public void RescanProject()
        {
            throw new NotImplementedException();
        }

        public void LoadSheet(SpriteSheetAsset sheet)
        {
            throw new NotImplementedException();
        }

        public void Dispose()
        {
        }
    }
}
