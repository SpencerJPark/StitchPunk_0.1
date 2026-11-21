// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace DotsAnimationToolkit.Authoring
{
    [Serializable]
    public sealed class SheetFlipbookFrame
    {
        public string name;

        [Tooltip("The frame's cell on the sheet in pixels, (0,0) bottom-left.")]
        public RectInt cellPixels;
    }

    /// <summary>A flipbook whose frames are cells on one sheet texture; each frame resolves to an atlas rect offset from frame 0.</summary>
    [CreateAssetMenu(fileName = "NewSheetFlipbook", menuName = "DOTS Animation Toolkit/Sheet Flipbook", order = 42)]
    public sealed class SheetFlipbookAsset : ScriptableObject
    {
        [Tooltip("The sheet texture every frame cell is read from.")]
        public Texture2D sheet;

        [Tooltip("Frame cells in playback order; frame 0 is the cell the cutout is drawn over.")]
        public List<SheetFlipbookFrame> frames = new List<SheetFlipbookFrame>();

        // Scale (1,1) with the UV offset of this frame's cell relative to frame 0's cell.
        public float4 AtlasRectForFrame(int frameIndex)
        {
            if (sheet == null || frames == null || frames.Count == 0 || frameIndex <= 0 || frameIndex >= frames.Count)
            {
                return ClipSampler.IdentityAtlasRect;
            }
            RectInt firstCell = frames[0].cellPixels;
            RectInt frameCell = frames[frameIndex].cellPixels;
            float offsetU = (frameCell.xMin - firstCell.xMin) / (float)sheet.width;
            float offsetV = (frameCell.yMin - firstCell.yMin) / (float)sheet.height;
            return new float4(1f, 1f, offsetU, offsetV);
        }
    }
}
