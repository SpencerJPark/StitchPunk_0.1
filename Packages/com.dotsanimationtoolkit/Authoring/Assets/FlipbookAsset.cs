// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace DotsAnimationToolkit.Authoring
{
    [Serializable]
    public sealed class FlipbookFrame
    {
        public string name;

        // The Texture2DArray layer this frame bakes to; always equal to its position in the frame list after a Bake.
        public int index;

        public Texture2D source;
    }

    /// <summary>A named stack of same-size frames baked into one Texture2DArray; authoring-only, never baked into a clip blob.</summary>
    [CreateAssetMenu(fileName = "NewFlipbook", menuName = "DOTS Animation Toolkit/Flipbook", order = 41)]
    public sealed class FlipbookAsset : ScriptableObject
    {
        public Texture2DArray texture;

        // Taken from the first frame at Bake; read-only in the tab.
        public Vector2Int layerSize;

        // Bake copies every import setting except rows and columns from this array's importer; null uses the fields below.
        public Texture2DArray importSettingsSource;

        public FilterMode filterMode = FilterMode.Point;
        public TextureWrapMode wrapMode = TextureWrapMode.Clamp;
        public bool generateMips = true;
        public bool linear;
        public List<FlipbookFrame> frames = new List<FlipbookFrame>();
        public string outputPath = string.Empty;

        // Names only: the importer owns the layers, so Bake, reorder and frame removal do not apply.
        public bool IsImportedArray => texture != null && frames != null && frames.TrueForAll(frame => frame == null || frame.source == null);

        public FlipbookFrame FindFrameByLayerIndex(int layerIndex)
        {
            if (frames == null)
            {
                return null;
            }
            for (int listPosition = 0; listPosition < frames.Count; listPosition++)
            {
                FlipbookFrame frame = frames[listPosition];
                if (frame != null && frame.index == layerIndex)
                {
                    return frame;
                }
            }
            return null;
        }
    }
}
