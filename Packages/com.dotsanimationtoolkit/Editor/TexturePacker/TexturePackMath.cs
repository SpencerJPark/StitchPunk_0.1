// Copyright (c) 2026 Spencer Park. All rights reserved.

using System.Collections.Generic;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>
    /// Pure channel-packing math: composes packed pixels from a request and a source lookup,
    /// with no dependency on UnityEditor or asset loading.
    /// </summary>
    public static class TexturePackMath
    {
        public static bool ComposePixels(PackRequest request, IReadOnlyDictionary<string, PackSourcePixels> sourcesByPath, int width, int height, out Color32[] packedPixels)
        {
            packedPixels = new Color32[width * height];

            // Every channel starts at its flat default; wired channels overwrite it below.
            for (int channelIndex = 0; channelIndex < PackChannelIndex.Count; channelIndex++)
            {
                PackChannelBinding channel = request.channels[channelIndex];
                if (channel.IsWired)
                {
                    continue;
                }

                // Invert applies to sampled values only. An unwired channel is exactly its slider,
                // so a stale invert toggle can never silently flip a flat default.
                byte defaultByte = ToByte(channel.defaultValue);
                for (int pixelIndex = 0; pixelIndex < packedPixels.Length; pixelIndex++)
                {
                    WriteChannel(ref packedPixels[pixelIndex], channelIndex, defaultByte);
                }
            }

            for (int channelIndex = 0; channelIndex < PackChannelIndex.Count; channelIndex++)
            {
                PackChannelBinding channel = request.channels[channelIndex];
                if (!channel.IsWired)
                {
                    continue;
                }

                if (!sourcesByPath.TryGetValue(channel.sourceAssetPath, out PackSourcePixels source))
                {
                    packedPixels = null;
                    return false;
                }

                bool sizesMatch = source.width == width && source.height == height;
                for (int pixelY = 0; pixelY < height; pixelY++)
                {
                    for (int pixelX = 0; pixelX < width; pixelX++)
                    {
                        // An exact size match copies the byte straight across; anything else
                        // is bilinearly resampled, so mixed-resolution sources just work.
                        byte sampledValue = sizesMatch
                            ? ReadChannel(source.pixels[pixelY * width + pixelX], channel.sourceChannel)
                            : SampleChannelBilinear(source, channel.sourceChannel, pixelX, pixelY, width, height);

                        if (channel.invert)
                        {
                            sampledValue = (byte)(255 - sampledValue);
                        }

                        WriteChannel(ref packedPixels[pixelY * width + pixelX], channelIndex, sampledValue);
                    }
                }
            }

            return true;
        }

        public static void IsolateChannel(Color32[] pixels, int channelIndex)
        {
            for (int pixelIndex = 0; pixelIndex < pixels.Length; pixelIndex++)
            {
                Color32 pixel = pixels[pixelIndex];
                byte channelValue = channelIndex switch
                {
                    PackChannelIndex.Red => pixel.r,
                    PackChannelIndex.Green => pixel.g,
                    PackChannelIndex.Blue => pixel.b,
                    _ => pixel.a
                };
                pixels[pixelIndex] = new Color32(channelValue, channelValue, channelValue, 255);
            }
        }

        public static byte SampleChannelBilinear(PackSourcePixels source, int sourceChannel, int destinationX, int destinationY, int destinationWidth, int destinationHeight)
        {
            float normalizedX = (destinationX + 0.5f) / destinationWidth;
            float normalizedY = (destinationY + 0.5f) / destinationHeight;

            float sampleX = normalizedX * source.width - 0.5f;
            float sampleY = normalizedY * source.height - 0.5f;

            int leftColumn = Mathf.FloorToInt(sampleX);
            int bottomRow = Mathf.FloorToInt(sampleY);
            float horizontalFraction = sampleX - leftColumn;
            float verticalFraction = sampleY - bottomRow;

            int clampedLeft = Mathf.Clamp(leftColumn, 0, source.width - 1);
            int clampedRight = Mathf.Clamp(leftColumn + 1, 0, source.width - 1);
            int clampedBottom = Mathf.Clamp(bottomRow, 0, source.height - 1);
            int clampedTop = Mathf.Clamp(bottomRow + 1, 0, source.height - 1);

            float bottomLeft = ReadChannel(source.pixels[clampedBottom * source.width + clampedLeft], sourceChannel);
            float bottomRight = ReadChannel(source.pixels[clampedBottom * source.width + clampedRight], sourceChannel);
            float topLeft = ReadChannel(source.pixels[clampedTop * source.width + clampedLeft], sourceChannel);
            float topRight = ReadChannel(source.pixels[clampedTop * source.width + clampedRight], sourceChannel);

            float bottomBlend = Mathf.LerpUnclamped(bottomLeft, bottomRight, horizontalFraction);
            float topBlend = Mathf.LerpUnclamped(topLeft, topRight, horizontalFraction);
            float blended = Mathf.LerpUnclamped(bottomBlend, topBlend, verticalFraction);

            return (byte)Mathf.Clamp(Mathf.RoundToInt(blended), 0, 255);
        }

        public static byte ReadChannel(Color32 pixel, int channelIndex)
        {
            return channelIndex switch
            {
                PackChannelIndex.Red => pixel.r,
                PackChannelIndex.Green => pixel.g,
                PackChannelIndex.Blue => pixel.b,
                _ => pixel.a
            };
        }

        public static void WriteChannel(ref Color32 pixel, int channelIndex, byte value)
        {
            switch (channelIndex)
            {
                case PackChannelIndex.Red: pixel.r = value; break;
                case PackChannelIndex.Green: pixel.g = value; break;
                case PackChannelIndex.Blue: pixel.b = value; break;
                default: pixel.a = value; break;
            }
        }

        public static byte ToByte(float normalizedValue)
        {
            return (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(normalizedValue) * 255f), 0, 255);
        }
    }
}
