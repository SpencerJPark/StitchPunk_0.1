// Copyright (c) 2026 Spencer Park. All rights reserved.
using System.Collections.Generic;
using DotsAnimationToolkit.Editor;
using NUnit.Framework;
using UnityEngine;

namespace DotsAnimationToolkit.Tests.EditMode
{
    public class CutoutIslandFinderTests
    {
        static void FillBlock(bool[] mask, Vector2Int size, RectInt block)
        {
            for (int pixelY = block.y; pixelY < block.yMax; pixelY++)
            {
                for (int pixelX = block.x; pixelX < block.xMax; pixelX++)
                {
                    mask[pixelY * size.x + pixelX] = true;
                }
            }
        }

        [Test]
        public void TwoBlobsWithinJoinDistance_AreOneIsland()
        {
            Vector2Int size = new Vector2Int(64, 32);
            bool[] mask = new bool[size.x * size.y];
            FillBlock(mask, size, new RectInt(10, 10, 6, 6));
            FillBlock(mask, size, new RectInt(18, 10, 6, 6));

            List<CutoutIsland> joined = CutoutIslandFinder.FindIslands(mask, size, 3, 1);
            List<CutoutIsland> separate = CutoutIslandFinder.FindIslands(mask, size, 0, 1);

            Assert.AreEqual(1, joined.Count);
            Assert.AreEqual(new RectInt(10, 10, 14, 6), joined[0].boundsPixels);
            Assert.AreEqual(72, joined[0].opaquePixelCount);
            Assert.AreEqual(2, separate.Count);
        }

        [Test]
        public void FourEqualBlobsInARow_SuggestOneFlipbookOfFourCellsInOrder()
        {
            Vector2Int size = new Vector2Int(128, 32);
            bool[] mask = new bool[size.x * size.y];
            for (int blobIndex = 0; blobIndex < 4; blobIndex++)
            {
                FillBlock(mask, size, new RectInt(8 + blobIndex * 24, 8, 10, 12));
            }

            List<CutoutIsland> islands = CutoutIslandFinder.FindIslands(mask, size, 3, 1);
            List<CutoutSheetFlipbookSuggestion> suggestions = CutoutIslandFinder.SuggestSheetFlipbooks(islands, CutoutIslandFinder.DefaultSizeTolerancePixels);

            Assert.AreEqual(4, islands.Count);
            Assert.AreEqual(1, suggestions.Count);
            Assert.AreEqual(4, suggestions[0].cells.Count);
            for (int cellIndex = 0; cellIndex < suggestions[0].cells.Count; cellIndex++)
            {
                Assert.AreEqual(suggestions[0].cells[0].size, suggestions[0].cells[cellIndex].size);
                if (cellIndex > 0)
                {
                    Assert.Greater(suggestions[0].cells[cellIndex].x, suggestions[0].cells[cellIndex - 1].x);
                }
            }
        }

        [Test]
        public void LShapedPiece_TracedOutlineLeavesTheNotchOut()
        {
            Vector2Int size = new Vector2Int(120, 120);
            bool[] mask = new bool[size.x * size.y];
            FillBlock(mask, size, new RectInt(0, 0, 120, 30));
            FillBlock(mask, size, new RectInt(0, 0, 30, 120));
            Vector2 insideTheNotch = new Vector2(60f, 60f);

            List<Vector2> traced = CutoutAlphaTracer.FitTracedOutline(mask, size, 12, 2f);

            Assert.IsFalse(CutoutAlphaTracer.ContainsPoint(traced, insideTheNotch));
            Assert.IsTrue(CutoutAlphaTracer.ContainsPoint(traced, new Vector2(15f, 100f)));
            Assert.IsTrue(CutoutAlphaTracer.ContainsPoint(traced, new Vector2(100f, 15f)));
        }

        [Test]
        public void NeighbourBlobInsideTheRect_IsDroppedByKeepLargestIsland()
        {
            Vector2Int size = new Vector2Int(60, 60);
            bool[] mask = new bool[size.x * size.y];
            FillBlock(mask, size, new RectInt(0, 0, 40, 40));
            FillBlock(mask, size, new RectInt(52, 52, 8, 8));

            bool[] isolated = CutoutIslandFinder.KeepLargestIsland(mask, size, CutoutIslandFinder.DefaultJoinDistancePixels);

            Assert.IsTrue(isolated[10 * size.x + 10]);
            Assert.IsFalse(isolated[55 * size.x + 55]);
        }
    }
}
