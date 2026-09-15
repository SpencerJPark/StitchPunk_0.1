// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using DotsAnimationToolkit.Authoring;
using UnityEngine.UIElements;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>The contact sheet: every frame of a sheet as a thumbnail in layer order, the only way to see a Texture2DArray's layers.</summary>
    public sealed class SpriteSheetPreviewElement : VisualElement
    {
        public const float DefaultThumbnailSize = 64f;
        public const float MinimumThumbnailSize = 24f;
        public const float MaximumThumbnailSize = 256f;

        // The list position of the clicked thumbnail.
        public event Action<int> FrameClicked;

        public SpriteSheetPreviewElement()
        {
        }

        public void SetSheet(SpriteSheetAsset sheet)
        {
            throw new NotImplementedException();
        }

        public void SetThumbnailSize(float thumbnailPixels)
        {
            throw new NotImplementedException();
        }

        public void Refresh()
        {
            throw new NotImplementedException();
        }

        public void HighlightFrame(int listPosition)
        {
            throw new NotImplementedException();
        }
    }
}
