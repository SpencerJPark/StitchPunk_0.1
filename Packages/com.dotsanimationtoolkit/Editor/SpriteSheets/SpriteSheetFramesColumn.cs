// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using DotsAnimationToolkit.Authoring;
using UnityEngine;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>The Sprite Sheets tab's frame list: the layer order, a drop target for images, and drag-to-reorder.</summary>
    public sealed class SpriteSheetFramesColumn : VisualElement
    {
        // Raised after any add, remove or reorder; the frames' index fields are already renumbered.
        public event Action FramesChanged;

        // The list position of the newly selected frame.
        public event Action<int> FrameSelected;

        public SpriteSheetFramesColumn()
        {
        }

        // The sheet is edited in place (the panel passes its working copy); null clears the list.
        public void SetSheet(SpriteSheetAsset sheet)
        {
            throw new NotImplementedException();
        }

        public void AddSources(IReadOnlyList<Texture2D> sourceTextures)
        {
            throw new NotImplementedException();
        }

        public void SelectFrame(int listPosition)
        {
            throw new NotImplementedException();
        }

        public void RefreshRows()
        {
            throw new NotImplementedException();
        }
    }
}
