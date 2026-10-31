using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace StitchPunk.Tests
{
    [TestFixture]
    public sealed class TerrainRoadRasterizerTests
    {
        private const int MaskResolution = 128;
        private const int HeightSamplesPerSide = 101;

        [Test]
        public void StraightRoad_CoverageIsSymmetricAboutItsCentreline()
        {
            TerrainRoadStamp road = CreateRoad(new Vector3(-80f, 0f, 0f), new Vector3(80f, 0f, 0f), 6f, 1);
            byte[] coverage = TerrainRoadRasterizer.RasteriseRoadMask(
                new List<TerrainRoadStamp> { road }, new Vector2Int(0, 0), MaskResolution);

            bool foundFullRow = false;
            for (int row = 0; row < MaskResolution; row++)
            {
                for (int column = 0; column < MaskResolution; column++)
                {
                    Assert.AreEqual(
                        coverage[row * MaskResolution + column],
                        coverage[(MaskResolution - 1 - row) * MaskResolution + column],
                        "row " + row + " column " + column);
                    if (coverage[row * MaskResolution + column] == 255)
                    {
                        foundFullRow = true;
                    }
                }
            }
            Assert.IsTrue(foundFullRow);
            Assert.AreEqual(0, coverage[0 * MaskResolution + 64]);
        }

        [Test]
        public void RoadCrossingATileBorder_GivesTheSameCoverageEitherSideOfTheSeam()
        {
            TerrainRoadStamp road = CreateRoad(new Vector3(-30f, 0f, 10f), new Vector3(130f, 0f, 10f), 5f, 1);
            List<TerrainRoadStamp> roads = new List<TerrainRoadStamp> { road };
            byte[] westCoverage = TerrainRoadRasterizer.RasteriseRoadMask(roads, new Vector2Int(0, 0), MaskResolution);
            byte[] eastCoverage = TerrainRoadRasterizer.RasteriseRoadMask(roads, new Vector2Int(1, 0), MaskResolution);

            bool westHasRoad = false;
            bool eastHasRoad = false;
            for (int row = 0; row < MaskResolution; row++)
            {
                byte westEdge = westCoverage[row * MaskResolution + (MaskResolution - 1)];
                byte eastEdge = eastCoverage[row * MaskResolution + 0];
                Assert.AreEqual(westEdge, eastEdge, "row " + row);
                westHasRoad |= westEdge > 0;
                eastHasRoad |= eastEdge > 0;
            }
            Assert.IsTrue(westHasRoad);
            Assert.IsTrue(eastHasRoad);
        }

        [Test]
        public void CoverageWithEdgeNoise_IsStableAcrossCalls()
        {
            TerrainRoadStamp road = CreateRoad(new Vector3(-80f, 0f, 5f), new Vector3(80f, 0f, -5f), 6f, 1);
            road.edgeNoiseMetres = 0.5f;
            List<TerrainRoadStamp> roads = new List<TerrainRoadStamp> { road };

            byte[] firstCall = TerrainRoadRasterizer.RasteriseRoadMask(roads, new Vector2Int(0, 0), MaskResolution);
            byte[] secondCall = TerrainRoadRasterizer.RasteriseRoadMask(roads, new Vector2Int(0, 0), MaskResolution);

            CollectionAssert.AreEqual(firstCall, secondCall);
        }

        [Test]
        public void HeightCarve_IsZeroOutsideWidthPlusShoulder_AndFullInsideTheBed()
        {
            const float halfWidth = 2f;
            const float carveShoulder = 6f;
            TerrainRoadStamp road = CreateRoad(new Vector3(-80f, 7f, 0f), new Vector3(80f, 7f, 0f), 4f, 1);
            road.carveHeight = true;
            road.carveShoulderMetres = carveShoulder;
            float[] terrainHeights = CreateFlatHeights(5f);
            float[] delta = new float[HeightSamplesPerSide * HeightSamplesPerSide];

            TerrainRoadRasterizer.ApplyHeightCarve(
                new List<TerrainRoadStamp> { road }, new Vector2Int(0, 0), terrainHeights, delta);

            bool foundShoulderSample = false;
            for (int sampleZ = 0; sampleZ < HeightSamplesPerSide; sampleZ++)
            {
                float worldZ = -50f + sampleZ;
                float distance = Mathf.Abs(worldZ);
                for (int sampleX = 0; sampleX < HeightSamplesPerSide; sampleX++)
                {
                    float value = delta[sampleZ * HeightSamplesPerSide + sampleX];
                    if (distance <= halfWidth)
                    {
                        Assert.AreEqual(2f, value, 1e-4f, "bed z " + worldZ);
                    }
                    else if (distance > halfWidth + carveShoulder)
                    {
                        Assert.AreEqual(0f, value, "outside z " + worldZ);
                    }
                    else if (distance < halfWidth + carveShoulder)
                    {
                        Assert.Greater(value, 0f, "shoulder z " + worldZ);
                        Assert.Less(value, 2f, "shoulder z " + worldZ);
                        foundShoulderSample = true;
                    }
                }
            }
            Assert.IsTrue(foundShoulderSample);
        }

        [Test]
        public void HeightCarve_GivesIdenticalDeltaAtTheSharedSeamRow()
        {
            TerrainRoadStamp road = CreateRoad(new Vector3(-30f, 7f, 10f), new Vector3(130f, 7f, 10f), 4f, 1);
            road.carveHeight = true;
            road.carveShoulderMetres = 6f;
            List<TerrainRoadStamp> roads = new List<TerrainRoadStamp> { road };
            float[] terrainHeights = CreateFlatHeights(5f);
            float[] westDelta = new float[HeightSamplesPerSide * HeightSamplesPerSide];
            float[] eastDelta = new float[HeightSamplesPerSide * HeightSamplesPerSide];

            TerrainRoadRasterizer.ApplyHeightCarve(roads, new Vector2Int(0, 0), terrainHeights, westDelta);
            TerrainRoadRasterizer.ApplyHeightCarve(roads, new Vector2Int(1, 0), terrainHeights, eastDelta);

            bool foundNonZero = false;
            for (int sampleZ = 0; sampleZ < HeightSamplesPerSide; sampleZ++)
            {
                float westValue = westDelta[sampleZ * HeightSamplesPerSide + 100];
                float eastValue = eastDelta[sampleZ * HeightSamplesPerSide + 0];
                Assert.AreEqual(westValue, eastValue, "sz " + sampleZ);
                foundNonZero |= westValue != 0f;
            }
            Assert.IsTrue(foundNonZero);
        }

        [Test]
        public void SmoothPointHeights_AveragesAJaggedLine()
        {
            const int pointCount = 41;
            Vector3[] points = new Vector3[pointCount];
            for (int index = 0; index < pointCount; index++)
            {
                points[index] = new Vector3(index, index % 2 == 0 ? 0f : 10f, 3f);
            }

            TerrainRoadRasterizer.SmoothPointHeights(points, 10f);

            for (int index = 0; index < pointCount; index++)
            {
                Assert.AreEqual((float)index, points[index].x);
                Assert.AreEqual(3f, points[index].z);
                if (index > 0 && index < pointCount - 1)
                {
                    Assert.AreEqual(5f, points[index].y, 1.5f, "index " + index);
                }
            }
        }

        [Test]
        public void HigherPriorityRoadOwnsTheOverlap()
        {
            TerrainRoadStamp lowPriorityRoad = CreateRoad(new Vector3(-80f, 0f, 0f), new Vector3(80f, 0f, 0f), 6f, 1);
            lowPriorityRoad.surfaceLayer = TerrainSplatLayer.Stone;
            TerrainRoadStamp highPriorityRoad = CreateRoad(new Vector3(0f, 0f, -80f), new Vector3(0f, 0f, 80f), 6f, 2);
            highPriorityRoad.surfaceLayer = TerrainSplatLayer.Grass;
            List<TerrainRoadStamp> roads = new List<TerrainRoadStamp> { lowPriorityRoad, highPriorityRoad };
            byte[] coverage = new byte[MaskResolution * MaskResolution];
            byte[] ownerRoadIndex = new byte[MaskResolution * MaskResolution];

            TerrainRoadRasterizer.RasteriseRoadMask(roads, new Vector2Int(0, 0), MaskResolution, coverage, ownerRoadIndex);

            int centreTexel = 63 * MaskResolution + 63;
            Assert.AreEqual(255, coverage[centreTexel]);
            Assert.AreEqual(1, ownerRoadIndex[centreTexel]);
        }

        private static TerrainRoadStamp CreateRoad(Vector3 start, Vector3 end, float widthMetres, int priority)
        {
            TerrainRoadStamp road = new TerrainRoadStamp();
            road.points = new Vector3[] { start, end };
            road.widthMetres = widthMetres;
            road.shoulderMetres = 0f;
            road.edgeNoiseMetres = 0f;
            road.carveShoulderMetres = 0f;
            road.carveHeight = false;
            road.carveBlendCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
            road.surfaceLayer = TerrainSplatLayer.Dirt;
            road.priority = priority;
            return road;
        }

        private static float[] CreateFlatHeights(float height)
        {
            float[] heights = new float[HeightSamplesPerSide * HeightSamplesPerSide];
            for (int index = 0; index < heights.Length; index++)
            {
                heights[index] = height;
            }
            return heights;
        }
    }
}
