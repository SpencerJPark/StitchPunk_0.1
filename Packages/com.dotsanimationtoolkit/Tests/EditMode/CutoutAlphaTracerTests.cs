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
    }
}
