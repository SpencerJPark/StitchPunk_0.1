using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace StitchPunk.Tests
{
    [TestFixture]
    public sealed class TerrainAreaRasterizerTests
    {
        // 100 texels per 100 m tile, so one texel is one metre and centres sit on half metres.
        private const int MaskResolution = 100;

        [Test]
        public void OverlappingAreas_HigherPriorityOwnsTheOverlapRegardlessOfListOrder()
        {
            TerrainAreaStamp highPriority = CreateSquare(-20f, 20f, -10f, 10f, 0f, 1, 5, 1);
            TerrainAreaStamp lowPriority = CreateSquare(0f, 40f, -10f, 10f, 0f, 1, 1, 2);
            byte[] owner = new byte[MaskResolution * MaskResolution];
            byte[] edgeDistance = new byte[MaskResolution * MaskResolution];

            TerrainAreaRasterizer.RasteriseAreaMask(
                new List<TerrainAreaStamp> { highPriority, lowPriority }, new Vector2Int(0, 0), MaskResolution, owner, edgeDistance);

            int overlapTexel = 50 * MaskResolution + 60;
            int lowOnlyTexel = 50 * MaskResolution + 80;
            Assert.AreEqual(1, owner[overlapTexel]);
            Assert.AreEqual(2, owner[lowOnlyTexel]);
        }

        [Test]
        public void EdgeNoise_DifferentSeedsGiveDifferentMasks()
        {
            byte[] ownerSeedOne = RasteriseNoisySquare(1);
            byte[] ownerSeedTwo = RasteriseNoisySquare(2);

            bool anyDifference = false;
            for (int texelIndex = 0; texelIndex < ownerSeedOne.Length; texelIndex++)
            {
                if (ownerSeedOne[texelIndex] != ownerSeedTwo[texelIndex])
                {
                    anyDifference = true;
                    break;
                }
            }
            Assert.IsTrue(anyDifference);
        }

        [Test]
        public void SquareStraddlingATileBorder_FlipsOwnershipAtItsEdgesAndIsOwnedOnBothSidesOfTheSeam()
        {
            TerrainAreaStamp square = CreateSquare(40f, 60f, -10f, 10f, 0f, 1, 1, 1);
            List<TerrainAreaStamp> areas = new List<TerrainAreaStamp> { square };
            byte[] westOwner = new byte[MaskResolution * MaskResolution];
            byte[] eastOwner = new byte[MaskResolution * MaskResolution];
            byte[] unusedEdge = new byte[MaskResolution * MaskResolution];
            TerrainAreaRasterizer.RasteriseAreaMask(areas, new Vector2Int(0, 0), MaskResolution, westOwner, unusedEdge);
            TerrainAreaRasterizer.RasteriseAreaMask(areas, new Vector2Int(1, 0), MaskResolution, eastOwner, unusedEdge);

            int rowAtZeroZ = 50;
            Assert.AreEqual(1, westOwner[rowAtZeroZ * MaskResolution + (MaskResolution - 1)], "last west column");
            Assert.AreEqual(1, eastOwner[rowAtZeroZ * MaskResolution + 0], "first east column");

            // West centres run 40.5 at column 90; east centres run 59.5 at column 9.
            Assert.AreEqual(0, westOwner[rowAtZeroZ * MaskResolution + 89], "west column just outside the left edge");
            Assert.AreEqual(1, westOwner[rowAtZeroZ * MaskResolution + 90], "west column just inside the left edge");
            Assert.AreEqual(1, eastOwner[rowAtZeroZ * MaskResolution + 9], "east column just inside the right edge");
            Assert.AreEqual(0, eastOwner[rowAtZeroZ * MaskResolution + 10], "east column just outside the right edge");
        }

        private static byte[] RasteriseNoisySquare(int seed)
        {
            TerrainAreaStamp square = CreateSquare(-20f, 20f, -20f, 20f, 4f, seed, 1, 1);
            byte[] owner = new byte[MaskResolution * MaskResolution];
            byte[] edgeDistance = new byte[MaskResolution * MaskResolution];
            TerrainAreaRasterizer.RasteriseAreaMask(
                new List<TerrainAreaStamp> { square }, new Vector2Int(0, 0), MaskResolution, owner, edgeDistance);
            return owner;
        }

        private static TerrainAreaStamp CreateSquare(float minX, float maxX, float minZ, float maxZ, float edgeNoiseMetres, int seed, int priority, byte areaIndex)
        {
            TerrainAreaStamp stamp = new TerrainAreaStamp();
            stamp.outlineXZ = new Vector2[]
            {
                new Vector2(minX, minZ),
                new Vector2(maxX, minZ),
                new Vector2(maxX, maxZ),
                new Vector2(minX, maxZ),
            };
            stamp.edgeNoiseMetres = edgeNoiseMetres;
            stamp.seed = seed;
            stamp.priority = priority;
            stamp.areaIndex = areaIndex;
            return stamp;
        }
    }
}
