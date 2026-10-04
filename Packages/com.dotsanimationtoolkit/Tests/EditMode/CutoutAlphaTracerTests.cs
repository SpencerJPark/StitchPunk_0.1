// Copyright (c) 2026 Spencer Park. All rights reserved.
using System.Collections.Generic;
using DotsAnimationToolkit.Editor;
using NUnit.Framework;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.EditMode
{
    public class CutoutAlphaTracerTests
    {
        [Test]
        public void FitOutline_Diamond_FitsBudgetAndContainsEverySetPixel()
        {
            Vector2Int size = new Vector2Int(64, 64);
            bool[] mask = new bool[size.x * size.y];
            for (int pixelY = 0; pixelY < size.y; pixelY++)
            {
                for (int pixelX = 0; pixelX < size.x; pixelX++)
                {
                    mask[pixelY * size.x + pixelX] = Mathf.Abs(pixelX - 32) + Mathf.Abs(pixelY - 32) <= 14;
                }
            }

            List<Vector2> outline = CutoutAlphaTracer.FitOutline(mask, size, 8, 2f);

            Assert.LessOrEqual(outline.Count, 8);
            for (int pixelY = 0; pixelY < size.y; pixelY++)
            {
                for (int pixelX = 0; pixelX < size.x; pixelX++)
                {
                    if (!mask[pixelY * size.x + pixelX])
                    {
                        continue;
                    }
                    Assert.IsTrue(CutoutAlphaTracer.ContainsPoint(outline, new Vector2(pixelX + 0.5f, pixelY + 0.5f)), "centre " + pixelX + "," + pixelY);
                    Assert.IsTrue(CutoutAlphaTracer.ContainsPoint(outline, new Vector2(pixelX, pixelY)), "corner " + pixelX + "," + pixelY);
                    Assert.IsTrue(CutoutAlphaTracer.ContainsPoint(outline, new Vector2(pixelX + 1f, pixelY + 1f)), "far corner " + pixelX + "," + pixelY);
                }
            }
        }

        [Test]
        public void FindOverhangs_LayerOutsideQuad_IsReported()
        {
            Vector2Int size = new Vector2Int(16, 16);
            bool[] insideLayer = new bool[size.x * size.y];
            bool[] overhangingLayer = new bool[size.x * size.y];
            for (int pixelY = 4; pixelY < 12; pixelY++)
            {
                for (int pixelX = 4; pixelX < 12; pixelX++)
                {
                    insideLayer[pixelY * size.x + pixelX] = true;
                    overhangingLayer[pixelY * size.x + pixelX] = true;
                }
            }
            overhangingLayer[8 * size.x + 14] = true;
            List<Vector2> quad = new List<Vector2> { new Vector2(3, 3), new Vector2(13, 3), new Vector2(13, 13), new Vector2(3, 13) };

            List<FrameOverhang> overhangs = CutoutAlphaTracer.FindOverhangs(new List<bool[]> { insideLayer, overhangingLayer }, size, quad);

            Assert.AreEqual(1, overhangs.Count);
            Assert.AreEqual(1, overhangs[0].layerIndex);
            Assert.AreEqual(1.5f, overhangs[0].overhangPixels, 0.01f);
        }

        [Test]
        public void ReadSourceMasks_SingleImage_ReturnsOneMaskMatchingItsAlpha()
        {
            Texture2D texture = new Texture2D(8, 8, TextureFormat.RGBA32, false, true);
            try
            {
                Color32[] pixels = new Color32[64];
                pixels[3 * 8 + 2] = new Color32(255, 255, 255, 255);
                pixels[6 * 8 + 5] = new Color32(255, 255, 255, 255);
                texture.SetPixels32(pixels);
                texture.Apply();

                List<bool[]> masks = CutoutAlphaTracer.ReadSourceMasks(texture, 0.1f);

                Assert.AreEqual(1, masks.Count);
                Assert.AreEqual(64, masks[0].Length);
                Assert.IsTrue(masks[0][3 * 8 + 2]);
                Assert.IsTrue(masks[0][6 * 8 + 5]);
                Assert.IsFalse(masks[0][0]);
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }
    }
}
