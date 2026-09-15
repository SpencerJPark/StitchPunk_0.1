// Copyright (c) 2026 Spencer Park. All rights reserved.

using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;
using DotsAnimationToolkit.Editor;

namespace DotsAnimationToolkit.Tests.EditMode
{
    /// <summary>Byte-level checks on GifEncoding.Encode's GIF89a container structure.</summary>
    public sealed class GifEncodingTests
    {
        [Test]
        public void Encode_TwoFrames_HasHeaderAndTwoImageDescriptors()
        {
            const int frameWidth = 4;
            const int frameHeight = 4;
            int pixelCount = frameWidth * frameHeight;

            Color32[] firstFrame = new Color32[pixelCount];
            Color32[] secondFrame = new Color32[pixelCount];
            for (int pixelIndex = 0; pixelIndex < pixelCount; pixelIndex++)
            {
                firstFrame[pixelIndex] = new Color32(255, 0, 0, 255);
                secondFrame[pixelIndex] = new Color32(0, 0, 255, 255);
            }

            List<Color32[]> frames = new List<Color32[]> { firstFrame, secondFrame };
            byte[] encodedGif = GifEncoding.Encode(frames, frameWidth, frameHeight, 10);

            Assert.AreEqual((byte)'G', encodedGif[0]);
            Assert.AreEqual((byte)'I', encodedGif[1]);
            Assert.AreEqual((byte)'F', encodedGif[2]);
            Assert.AreEqual((byte)'8', encodedGif[3]);
            Assert.AreEqual((byte)'9', encodedGif[4]);
            Assert.AreEqual((byte)'a', encodedGif[5]);
            Assert.AreEqual(0x3B, encodedGif[encodedGif.Length - 1]);

            int imageDescriptorCount = CountImageDescriptors(encodedGif);
            Assert.AreEqual(2, imageDescriptorCount);
        }

        // Walks the GIF block structure rather than counting raw 0x2C bytes, which also occur in
        // the palette and LZW data. Any malformed or truncated block causes Assert.Fail, not a hang.
        private static int CountImageDescriptors(byte[] gifBytes)
        {
            int readOffset = 13;
            byte packedScreenFields = gifBytes[10];
            if ((packedScreenFields & 0x80) != 0)
            {
                int globalColorTableEntryCount = 1 << ((packedScreenFields & 0x07) + 1);
                readOffset += 3 * globalColorTableEntryCount;
            }

            int imageDescriptorCount = 0;
            while (true)
            {
                if (readOffset >= gifBytes.Length)
                {
                    Assert.Fail("Reached end of buffer without finding a GIF trailer.");
                }

                byte blockIntroducer = gifBytes[readOffset];
                if (blockIntroducer == 0x3B)
                {
                    break;
                }

                if (blockIntroducer == 0x21)
                {
                    readOffset += 2;
                    readOffset = SkipSubBlocks(gifBytes, readOffset);
                    continue;
                }

                if (blockIntroducer == 0x2C)
                {
                    imageDescriptorCount++;
                    readOffset += 9;
                    byte packedImageFields = gifBytes[readOffset - 1];
                    if ((packedImageFields & 0x80) != 0)
                    {
                        int localColorTableEntryCount = 1 << ((packedImageFields & 0x07) + 1);
                        readOffset += 3 * localColorTableEntryCount;
                    }

                    readOffset += 1;
                    readOffset = SkipSubBlocks(gifBytes, readOffset);
                    continue;
                }

                Assert.Fail($"Unexpected block introducer 0x{blockIntroducer:X2} at offset {readOffset}.");
            }

            return imageDescriptorCount;
        }

        private static int SkipSubBlocks(byte[] gifBytes, int readOffset)
        {
            while (true)
            {
                if (readOffset >= gifBytes.Length)
                {
                    Assert.Fail("Reached end of buffer while skipping sub-blocks.");
                }

                byte subBlockLength = gifBytes[readOffset];
                readOffset += 1;
                if (subBlockLength == 0)
                {
                    return readOffset;
                }

                readOffset += subBlockLength;
            }
        }
    }
}
