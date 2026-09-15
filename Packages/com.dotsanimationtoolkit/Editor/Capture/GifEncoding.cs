// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Self-contained GIF89a encoder: one sampled median-cut global palette, LZW, looping forever.</summary>
    public static class GifEncoding
    {
        // Frames arrive in Texture2D.GetPixels32 order (bottom row first); the encoder writes them top row first.
        public static byte[] Encode(IReadOnlyList<Color32[]> frames, int width, int height, int delayCentiseconds)
        {
            throw new NotImplementedException();
        }
    }
}
