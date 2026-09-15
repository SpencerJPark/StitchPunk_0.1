// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Per-layer Texture2D thumbnails of Texture2DArrays, copied on the GPU so unreadable compressed arrays still preview.</summary>
    public sealed class FlipbookLayerThumbnailCache : IDisposable
    {
        private readonly Dictionary<Texture2DArray, Texture2D[]> layerThumbnailsByArray = new Dictionary<Texture2DArray, Texture2D[]>();

        // Null for a null array or an out-of-range layer. The texture is owned by the cache: never destroy it yourself.
        public Texture2D GetLayerThumbnail(Texture2DArray array, int layerIndex)
        {
            if (array == null || layerIndex < 0 || layerIndex >= array.depth)
            {
                return null;
            }

            if (!layerThumbnailsByArray.TryGetValue(array, out Texture2D[] layerThumbnails) || layerThumbnails.Length != array.depth)
            {
                // A reimport can change the depth of the same array object; rebuild its slots from scratch.
                if (layerThumbnails != null)
                {
                    DestroyThumbnails(layerThumbnails);
                }

                layerThumbnails = new Texture2D[array.depth];
                layerThumbnailsByArray[array] = layerThumbnails;
            }

            Texture2D layerThumbnail = layerThumbnails[layerIndex];
            if (layerThumbnail == null)
            {
                layerThumbnail = new Texture2D(array.width, array.height, array.graphicsFormat, TextureCreationFlags.None);
                layerThumbnail.hideFlags = HideFlags.HideAndDontSave;
                layerThumbnail.filterMode = array.filterMode;
                Graphics.CopyTexture(array, layerIndex, 0, layerThumbnail, 0, 0);
                layerThumbnails[layerIndex] = layerThumbnail;
            }

            return layerThumbnail;
        }

        public void Clear()
        {
            foreach (Texture2D[] layerThumbnails in layerThumbnailsByArray.Values)
            {
                DestroyThumbnails(layerThumbnails);
            }

            layerThumbnailsByArray.Clear();
        }

        public void Dispose()
        {
            Clear();
        }

        private static void DestroyThumbnails(Texture2D[] layerThumbnails)
        {
            for (int layerIndex = 0; layerIndex < layerThumbnails.Length; layerIndex++)
            {
                if (layerThumbnails[layerIndex] != null)
                {
                    UnityEngine.Object.DestroyImmediate(layerThumbnails[layerIndex]);
                }
            }
        }
    }
}
