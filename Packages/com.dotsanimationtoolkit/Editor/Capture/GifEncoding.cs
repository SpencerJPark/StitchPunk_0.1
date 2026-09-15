// Copyright (c) 2026 Spencer Park. All rights reserved.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    /// <summary>Self-contained GIF89a encoder: one sampled median-cut global palette, LZW, looping forever.</summary>
    public static class GifEncoding
    {
        private const int PaletteSize = 256;
        private const int ClearCode = 256;
        private const int EndCode = 257;
        private const int FirstFreeCode = 258;
        private const int MaxCodeCount = 4096;
        private const int LzwHashTableSize = 8191;

        private static readonly byte[] Gif89aSignature =
        {
            (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a'
        };

        private static readonly byte[] NetscapeApplicationIdentifier =
        {
            (byte)'N', (byte)'E', (byte)'T', (byte)'S', (byte)'C', (byte)'A', (byte)'P', (byte)'E', (byte)'2', (byte)'.', (byte)'0'
        };

        public static byte[] Encode(IReadOnlyList<Color32[]> frames, int width, int height, int delayCentiseconds)
        {
            if (frames == null || frames.Count == 0)
            {
                throw new ArgumentException("frames must be non-null and non-empty.", nameof(frames));
            }

            if (width < 1 || width > 65535 || height < 1 || height > 65535)
            {
                throw new ArgumentException("width and height must both be in the range [1, 65535].");
            }

            int expectedPixelCount = width * height;
            for (int frameIndex = 0; frameIndex < frames.Count; frameIndex++)
            {
                Color32[] frame = frames[frameIndex];
                if (frame == null || frame.Length != expectedPixelCount)
                {
                    throw new ArgumentException($"frame {frameIndex} must contain exactly {expectedPixelCount} pixels.", nameof(frames));
                }
            }

            bool useTransparency = AnyPixelIsTransparent(frames);
            int targetPaletteColorCount = useTransparency ? PaletteSize - 1 : PaletteSize;
            int paletteStartIndex = useTransparency ? 1 : 0;

            List<Color32> sampledPixels = CollectSamplePixels(frames, useTransparency);
            Color32[] paletteColors = new Color32[PaletteSize];
            int populatedColorCount = BuildMedianCutPalette(sampledPixels, targetPaletteColorCount, paletteColors, paletteStartIndex);

            short[] rgb555ToPaletteIndex = new short[32768];
            for (int lookupSlot = 0; lookupSlot < rgb555ToPaletteIndex.Length; lookupSlot++)
            {
                rgb555ToPaletteIndex[lookupSlot] = -1;
            }

            List<byte> output = new List<byte>(1024 + frames.Count * expectedPixelCount / 2);
            WriteHeaderAndGlobalPalette(output, width, height, paletteColors);
            WriteNetscapeLoopExtension(output);

            byte gceePacked = (byte)(useTransparency ? 0x09 : 0x04);
            int clampedDelayCentiseconds = delayCentiseconds < 0 ? 0 : delayCentiseconds;
            byte[] frameIndices = new byte[expectedPixelCount];

            for (int frameIndex = 0; frameIndex < frames.Count; frameIndex++)
            {
                // Source pixels arrive bottom row first (Texture2D.GetPixels32 order); GIF rows are written top first.
                BuildFrameIndices(frames[frameIndex], width, height, useTransparency, paletteColors, paletteStartIndex, populatedColorCount, rgb555ToPaletteIndex, frameIndices);
                WriteGraphicsControlExtension(output, gceePacked, clampedDelayCentiseconds);
                WriteImageDescriptorAndData(output, width, height, frameIndices);
            }

            output.Add(0x3B);
            return output.ToArray();
        }

        private static bool AnyPixelIsTransparent(IReadOnlyList<Color32[]> frames)
        {
            for (int frameIndex = 0; frameIndex < frames.Count; frameIndex++)
            {
                Color32[] frame = frames[frameIndex];
                for (int pixelIndex = 0; pixelIndex < frame.Length; pixelIndex++)
                {
                    if (frame[pixelIndex].a < 128)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        // Palette from a sample, not every pixel; median-cut's sort dominates cost, so shrink the input first.
        private static List<Color32> CollectSamplePixels(IReadOnlyList<Color32[]> frames, bool opaqueOnly)
        {
            List<Color32> sampledPixels = new List<Color32>();
            CollectSamplePixelsWithStep(frames, opaqueOnly, 3, 7, sampledPixels);

            if (sampledPixels.Count < PaletteSize)
            {
                sampledPixels.Clear();
                CollectSamplePixelsWithStep(frames, opaqueOnly, 1, 1, sampledPixels);
            }

            return sampledPixels;
        }

        private static void CollectSamplePixelsWithStep(IReadOnlyList<Color32[]> frames, bool opaqueOnly, int frameStep, int pixelStep, List<Color32> destination)
        {
            for (int frameIndex = 0; frameIndex < frames.Count; frameIndex += frameStep)
            {
                Color32[] frame = frames[frameIndex];
                for (int pixelIndex = 0; pixelIndex < frame.Length; pixelIndex += pixelStep)
                {
                    Color32 pixel = frame[pixelIndex];
                    if (opaqueOnly && pixel.a < 128)
                    {
                        continue;
                    }

                    destination.Add(pixel);
                }
            }
        }

        private static int BuildMedianCutPalette(List<Color32> sampledPixels, int targetColorCount, Color32[] destinationPalette, int destinationStartIndex)
        {
            if (sampledPixels.Count == 0 || targetColorCount <= 0)
            {
                return 0;
            }

            List<List<Color32>> buckets = new List<List<Color32>> { sampledPixels };
            while (buckets.Count < targetColorCount)
            {
                int splitBucketIndex = FindWidestBucketIndex(buckets);
                if (splitBucketIndex < 0)
                {
                    break;
                }

                List<Color32> bucketToSplit = buckets[splitBucketIndex];
                int widestChannel = FindWidestChannel(bucketToSplit);
                bucketToSplit.Sort((left, right) => GetChannelValue(left, widestChannel).CompareTo(GetChannelValue(right, widestChannel)));

                int medianPosition = bucketToSplit.Count / 2;
                List<Color32> lowerHalf = bucketToSplit.GetRange(0, medianPosition);
                List<Color32> upperHalf = bucketToSplit.GetRange(medianPosition, bucketToSplit.Count - medianPosition);
                buckets[splitBucketIndex] = lowerHalf;
                buckets.Add(upperHalf);
            }

            int populatedColorCount = buckets.Count;
            for (int bucketIndex = 0; bucketIndex < populatedColorCount; bucketIndex++)
            {
                destinationPalette[destinationStartIndex + bucketIndex] = AverageColor(buckets[bucketIndex]);
            }

            return populatedColorCount;
        }

        private static int FindWidestBucketIndex(List<List<Color32>> buckets)
        {
            int bestBucketIndex = -1;
            int bestChannelRange = -1;
            for (int bucketIndex = 0; bucketIndex < buckets.Count; bucketIndex++)
            {
                List<Color32> bucket = buckets[bucketIndex];
                if (bucket.Count < 2)
                {
                    continue;
                }

                int widestChannel = FindWidestChannel(bucket);
                int channelRange = GetChannelRange(bucket, widestChannel);
                if (channelRange > bestChannelRange)
                {
                    bestChannelRange = channelRange;
                    bestBucketIndex = bucketIndex;
                }
            }

            return bestBucketIndex;
        }

        private static int FindWidestChannel(List<Color32> bucket)
        {
            int redRange = GetChannelRange(bucket, 0);
            int greenRange = GetChannelRange(bucket, 1);
            int blueRange = GetChannelRange(bucket, 2);

            if (redRange >= greenRange && redRange >= blueRange)
            {
                return 0;
            }

            return greenRange >= blueRange ? 1 : 2;
        }

        private static int GetChannelRange(List<Color32> bucket, int channel)
        {
            byte minimumValue = 255;
            byte maximumValue = 0;
            for (int pixelIndex = 0; pixelIndex < bucket.Count; pixelIndex++)
            {
                byte channelValue = GetChannelValue(bucket[pixelIndex], channel);
                if (channelValue < minimumValue)
                {
                    minimumValue = channelValue;
                }

                if (channelValue > maximumValue)
                {
                    maximumValue = channelValue;
                }
            }

            return maximumValue - minimumValue;
        }

        private static byte GetChannelValue(Color32 color, int channel)
        {
            switch (channel)
            {
                case 0:
                    return color.r;
                case 1:
                    return color.g;
                default:
                    return color.b;
            }
        }

        private static Color32 AverageColor(List<Color32> bucket)
        {
            long redSum = 0;
            long greenSum = 0;
            long blueSum = 0;
            for (int pixelIndex = 0; pixelIndex < bucket.Count; pixelIndex++)
            {
                Color32 pixel = bucket[pixelIndex];
                redSum += pixel.r;
                greenSum += pixel.g;
                blueSum += pixel.b;
            }

            int pixelCount = bucket.Count;
            byte averageRed = (byte)(redSum / pixelCount);
            byte averageGreen = (byte)(greenSum / pixelCount);
            byte averageBlue = (byte)(blueSum / pixelCount);
            return new Color32(averageRed, averageGreen, averageBlue, 255);
        }

        private static int GetPaletteIndexForColor(Color32 color, Color32[] paletteColors, int paletteStartIndex, int populatedColorCount, short[] rgb555ToPaletteIndex)
        {
            int rgb555Key = ((color.r >> 3) << 10) | ((color.g >> 3) << 5) | (color.b >> 3);
            short cachedPaletteIndex = rgb555ToPaletteIndex[rgb555Key];
            if (cachedPaletteIndex >= 0)
            {
                return cachedPaletteIndex;
            }

            int nearestPaletteIndex = paletteStartIndex;
            int nearestDistance = int.MaxValue;
            for (int paletteOffset = 0; paletteOffset < populatedColorCount; paletteOffset++)
            {
                int candidateIndex = paletteStartIndex + paletteOffset;
                Color32 candidateColor = paletteColors[candidateIndex];
                int redDelta = color.r - candidateColor.r;
                int greenDelta = color.g - candidateColor.g;
                int blueDelta = color.b - candidateColor.b;
                int distance = (redDelta * redDelta) + (greenDelta * greenDelta) + (blueDelta * blueDelta);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestPaletteIndex = candidateIndex;
                }
            }

            rgb555ToPaletteIndex[rgb555Key] = (short)nearestPaletteIndex;
            return nearestPaletteIndex;
        }

        private static void BuildFrameIndices(Color32[] frame, int width, int height, bool useTransparency, Color32[] paletteColors, int paletteStartIndex, int populatedColorCount, short[] rgb555ToPaletteIndex, byte[] destinationIndices)
        {
            for (int outputRow = 0; outputRow < height; outputRow++)
            {
                int sourceRow = height - 1 - outputRow;
                int sourceRowOffset = sourceRow * width;
                int destinationRowOffset = outputRow * width;
                for (int columnIndex = 0; columnIndex < width; columnIndex++)
                {
                    Color32 pixel = frame[sourceRowOffset + columnIndex];
                    byte paletteIndex;
                    if (useTransparency && pixel.a < 128)
                    {
                        paletteIndex = 0;
                    }
                    else
                    {
                        paletteIndex = (byte)GetPaletteIndexForColor(pixel, paletteColors, paletteStartIndex, populatedColorCount, rgb555ToPaletteIndex);
                    }

                    destinationIndices[destinationRowOffset + columnIndex] = paletteIndex;
                }
            }
        }

        private static void WriteHeaderAndGlobalPalette(List<byte> output, int width, int height, Color32[] paletteColors)
        {
            output.AddRange(Gif89aSignature);
            WriteUInt16LittleEndian(output, width);
            WriteUInt16LittleEndian(output, height);
            output.Add(0xF7);
            output.Add(0x00);
            output.Add(0x00);

            for (int colorIndex = 0; colorIndex < PaletteSize; colorIndex++)
            {
                Color32 color = paletteColors[colorIndex];
                output.Add(color.r);
                output.Add(color.g);
                output.Add(color.b);
            }
        }

        private static void WriteNetscapeLoopExtension(List<byte> output)
        {
            output.Add(0x21);
            output.Add(0xFF);
            output.Add(0x0B);
            output.AddRange(NetscapeApplicationIdentifier);
            output.Add(0x03);
            output.Add(0x01);
            output.Add(0x00);
            output.Add(0x00);
            output.Add(0x00);
        }

        private static void WriteGraphicsControlExtension(List<byte> output, byte packed, int delayCentiseconds)
        {
            output.Add(0x21);
            output.Add(0xF9);
            output.Add(0x04);
            output.Add(packed);
            WriteUInt16LittleEndian(output, delayCentiseconds);
            output.Add(0x00);
            output.Add(0x00);
        }

        private static void WriteImageDescriptorAndData(List<byte> output, int width, int height, byte[] frameIndices)
        {
            output.Add(0x2C);
            WriteUInt16LittleEndian(output, 0);
            WriteUInt16LittleEndian(output, 0);
            WriteUInt16LittleEndian(output, width);
            WriteUInt16LittleEndian(output, height);
            output.Add(0x00);
            output.Add(8);

            EncodeLzwAndWriteSubBlocks(output, frameIndices);

            output.Add(0x00);
        }

        private static void WriteUInt16LittleEndian(List<byte> output, int value)
        {
            output.Add((byte)(value & 0xFF));
            output.Add((byte)((value >> 8) & 0xFF));
        }

        private static void EncodeLzwAndWriteSubBlocks(List<byte> output, byte[] pixelIndices)
        {
            int[] hashKeys = new int[LzwHashTableSize];
            short[] hashCodes = new short[LzwHashTableSize];
            ResetLzwHashTable(hashKeys);

            byte[] subBlockBytes = new byte[255];
            int subBlockLength = 0;
            int bitBuffer = 0;
            int bitCount = 0;
            int codeWidth = 9;
            int nextCode = FirstFreeCode;

            EmitLzwCode(output, subBlockBytes, ref subBlockLength, ref bitBuffer, ref bitCount, ClearCode, codeWidth);

            int currentPrefixCode = pixelIndices[0];
            for (int pixelIndex = 1; pixelIndex < pixelIndices.Length; pixelIndex++)
            {
                int currentPixelValue = pixelIndices[pixelIndex];
                int existingCode = LookupLzwCode(hashKeys, hashCodes, currentPrefixCode, currentPixelValue);
                if (existingCode >= 0)
                {
                    currentPrefixCode = existingCode;
                    continue;
                }

                EmitLzwCode(output, subBlockBytes, ref subBlockLength, ref bitBuffer, ref bitCount, currentPrefixCode, codeWidth);

                // Widen after the emit and before the insert, as giflib does: decoders add their entry one code
                // behind the encoder, so widening after the insert corrupts every frame whose table passes 512 codes.
                if (nextCode >= (1 << codeWidth) && codeWidth < 12)
                {
                    codeWidth++;
                }

                if (nextCode < MaxCodeCount - 1)
                {
                    InsertLzwCode(hashKeys, hashCodes, currentPrefixCode, currentPixelValue, nextCode);
                    nextCode++;
                }
                else
                {
                    EmitLzwCode(output, subBlockBytes, ref subBlockLength, ref bitBuffer, ref bitCount, ClearCode, codeWidth);
                    ResetLzwHashTable(hashKeys);
                    nextCode = FirstFreeCode;
                    codeWidth = 9;
                }

                currentPrefixCode = currentPixelValue;
            }

            EmitLzwCode(output, subBlockBytes, ref subBlockLength, ref bitBuffer, ref bitCount, currentPrefixCode, codeWidth);
            if (nextCode >= (1 << codeWidth) && codeWidth < 12)
            {
                codeWidth++;
            }
            EmitLzwCode(output, subBlockBytes, ref subBlockLength, ref bitBuffer, ref bitCount, EndCode, codeWidth);

            if (bitCount > 0)
            {
                AppendByteToSubBlock(output, subBlockBytes, ref subBlockLength, (byte)(bitBuffer & 0xFF));
            }

            if (subBlockLength > 0)
            {
                output.Add((byte)subBlockLength);
                for (int byteIndex = 0; byteIndex < subBlockLength; byteIndex++)
                {
                    output.Add(subBlockBytes[byteIndex]);
                }
            }
        }

        private static void ResetLzwHashTable(int[] hashKeys)
        {
            for (int hashSlot = 0; hashSlot < hashKeys.Length; hashSlot++)
            {
                hashKeys[hashSlot] = -1;
            }
        }

        private static int LookupLzwCode(int[] hashKeys, short[] hashCodes, int prefixCode, int pixelValue)
        {
            int key = (prefixCode << 8) | pixelValue;
            int hashSlot = key % LzwHashTableSize;
            while (hashKeys[hashSlot] != -1)
            {
                if (hashKeys[hashSlot] == key)
                {
                    return hashCodes[hashSlot];
                }

                hashSlot++;
                if (hashSlot == LzwHashTableSize)
                {
                    hashSlot = 0;
                }
            }

            return -1;
        }

        private static void InsertLzwCode(int[] hashKeys, short[] hashCodes, int prefixCode, int pixelValue, int code)
        {
            int key = (prefixCode << 8) | pixelValue;
            int hashSlot = key % LzwHashTableSize;
            while (hashKeys[hashSlot] != -1)
            {
                hashSlot++;
                if (hashSlot == LzwHashTableSize)
                {
                    hashSlot = 0;
                }
            }

            hashKeys[hashSlot] = key;
            hashCodes[hashSlot] = (short)code;
        }

        private static void EmitLzwCode(List<byte> output, byte[] subBlockBytes, ref int subBlockLength, ref int bitBuffer, ref int bitCount, int code, int width)
        {
            bitBuffer |= code << bitCount;
            bitCount += width;
            while (bitCount >= 8)
            {
                AppendByteToSubBlock(output, subBlockBytes, ref subBlockLength, (byte)(bitBuffer & 0xFF));
                bitBuffer >>= 8;
                bitCount -= 8;
            }
        }

        private static void AppendByteToSubBlock(List<byte> output, byte[] subBlockBytes, ref int subBlockLength, byte value)
        {
            subBlockBytes[subBlockLength] = value;
            subBlockLength++;
            if (subBlockLength == 255)
            {
                output.Add(255);
                for (int byteIndex = 0; byteIndex < 255; byteIndex++)
                {
                    output.Add(subBlockBytes[byteIndex]);
                }

                subBlockLength = 0;
            }
        }
    }
}
