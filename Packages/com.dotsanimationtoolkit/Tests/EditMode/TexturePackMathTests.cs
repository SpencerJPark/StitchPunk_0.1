// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using DotsAnimationToolkit.Editor;
using NUnit.Framework;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>Guards the pure channel-packing math against routing, invert and resample regressions.</summary>
    public sealed class TexturePackMathTests
    {
        private const string SourcePath = "SourceTexture.png";

        [Test]
        public void ComposePixels_RoutesInvertsAndFillsDefaults()
        {
            PackSourcePixels source = new PackSourcePixels
            {
                width = 2,
                height = 2,
                pixels = new Color32[]
                {
                    new Color32(0, 0, 0, 255),
                    new Color32(64, 0, 0, 255),
                    new Color32(128, 0, 0, 255),
                    new Color32(255, 0, 0, 255)
                }
            };

            PackRequest request = new PackRequest
            {
                resolution = new Vector2Int(2, 2),
                channels = new PackChannelBinding[]
                {
                    new PackChannelBinding { sourceAssetPath = SourcePath, sourceChannel = PackChannelIndex.Red, invert = true, defaultValue = 0f },
                    new PackChannelBinding { sourceAssetPath = null, sourceChannel = -1, invert = false, defaultValue = 0.5f },
                    new PackChannelBinding { sourceAssetPath = SourcePath, sourceChannel = PackChannelIndex.Red, invert = false, defaultValue = 0f },
                    new PackChannelBinding { sourceAssetPath = null, sourceChannel = -1, invert = false, defaultValue = 1f }
                }
            };

            Dictionary<string, PackSourcePixels> sourcesByPath = new Dictionary<string, PackSourcePixels> { { SourcePath, source } };

            bool composed = TexturePackMath.ComposePixels(request, sourcesByPath, 2, 2, out Color32[] packedPixels);

            Assert.IsTrue(composed);
            Color32 pixelOne = packedPixels[1];
            Assert.AreEqual(191, pixelOne.r);
            Assert.AreEqual(128, pixelOne.g);
            Assert.AreEqual(64, pixelOne.b);
            Assert.AreEqual(255, pixelOne.a);
        }

        [Test]
        public void ComposePixels_ResamplesAMismatchedSource()
        {
            PackSourcePixels source = new PackSourcePixels
            {
                width = 1,
                height = 1,
                pixels = new Color32[] { new Color32(200, 0, 0, 255) }
            };

            PackRequest request = new PackRequest
            {
                resolution = new Vector2Int(2, 2),
                channels = new PackChannelBinding[]
                {
                    new PackChannelBinding { sourceAssetPath = SourcePath, sourceChannel = PackChannelIndex.Red, invert = false, defaultValue = 0f },
                    new PackChannelBinding { sourceAssetPath = null, sourceChannel = -1, invert = false, defaultValue = 0f },
                    new PackChannelBinding { sourceAssetPath = null, sourceChannel = -1, invert = false, defaultValue = 0f },
                    new PackChannelBinding { sourceAssetPath = null, sourceChannel = -1, invert = false, defaultValue = 0f }
                }
            };

            Dictionary<string, PackSourcePixels> sourcesByPath = new Dictionary<string, PackSourcePixels> { { SourcePath, source } };

            bool composed = TexturePackMath.ComposePixels(request, sourcesByPath, 2, 2, out Color32[] packedPixels);

            Assert.IsTrue(composed);
            for (int pixelIndex = 0; pixelIndex < packedPixels.Length; pixelIndex++)
            {
                Assert.AreEqual(200, packedPixels[pixelIndex].r);
            }
        }

        [Test]
        public void ComposePixels_ReturnsFalseWhenSourceIsMissing()
        {
            PackRequest request = new PackRequest
            {
                resolution = new Vector2Int(2, 2),
                channels = new PackChannelBinding[]
                {
                    new PackChannelBinding { sourceAssetPath = SourcePath, sourceChannel = PackChannelIndex.Red, invert = false, defaultValue = 0f },
                    new PackChannelBinding { sourceAssetPath = null, sourceChannel = -1, invert = false, defaultValue = 0f },
                    new PackChannelBinding { sourceAssetPath = null, sourceChannel = -1, invert = false, defaultValue = 0f },
                    new PackChannelBinding { sourceAssetPath = null, sourceChannel = -1, invert = false, defaultValue = 0f }
                }
            };

            Dictionary<string, PackSourcePixels> sourcesByPath = new Dictionary<string, PackSourcePixels>();

            bool composed = TexturePackMath.ComposePixels(request, sourcesByPath, 2, 2, out Color32[] packedPixels);

            Assert.IsFalse(composed);
            Assert.IsNull(packedPixels);
        }
    }
}
