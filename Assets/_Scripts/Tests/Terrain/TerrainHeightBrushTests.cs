using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace StitchPunk.Tests
{
    [TestFixture]
    public sealed class TerrainHeightBrushTests
    {
        private const int SamplesPerSide = TerrainTileData.HeightSamplesPerSide;

        private readonly List<TerrainTileData> createdTiles = new List<TerrainTileData>();
        private readonly Dictionary<Vector2Int, TerrainTileData> tilesByCoordinate = new Dictionary<Vector2Int, TerrainTileData>();

        [TearDown]
        public void DestroyCreatedTiles()
        {
            foreach (TerrainTileData tile in createdTiles)
            {
                UnityEngine.Object.DestroyImmediate(tile);
            }
            createdTiles.Clear();
            tilesByCoordinate.Clear();
        }

        [Test]
        public void BrushStraddlingTwoTiles_LeavesTheSharedSeamRowIdentical()
        {
            TerrainTileData westTile = CreateTile(new Vector2Int(0, 0));
            TerrainTileData eastTile = CreateTile(new Vector2Int(1, 0));

            TerrainHeightBrush.Raise(new Vector2(50f, 0f), 12f, 0.5f, 2f, LookUpTile);

            bool anySeamSampleNonZero = false;
            for (int sampleZ = 0; sampleZ < SamplesPerSide; sampleZ++)
            {
                float westValue = westTile.handHeightDelta[sampleZ * SamplesPerSide + 100];
                float eastValue = eastTile.handHeightDelta[sampleZ * SamplesPerSide + 0];
                Assert.AreEqual(westValue, eastValue, "Seam row sample z=" + sampleZ);
                if (westValue != 0f)
                {
                    anySeamSampleNonZero = true;
                }
            }
            Assert.IsTrue(anySeamSampleNonZero, "The brush never touched the seam, so the comparison proved nothing.");
        }

        [Test]
        public void Flatten_ReachesTheTargetHeightAtTheBrushCentre()
        {
            TerrainTileData tile = CreateTile(new Vector2Int(0, 0));
            for (int sampleZ = 0; sampleZ < SamplesPerSide; sampleZ++)
            {
                for (int sampleX = 0; sampleX < SamplesPerSide; sampleX++)
                {
                    tile.baseHeights[sampleZ * SamplesPerSide + sampleX] = sampleX * 0.1f;
                }
            }

            TerrainHeightBrush.Flatten(Vector2.zero, 10f, 1f, 1f, 7.5f, LookUpTile);

            Assert.AreEqual(7.5f, tile.HeightAt(50, 50), 0.001f);
        }

        [Test]
        public void Smooth_NeverLeavesTheMinMaxOfItsNeighbourhood()
        {
            const float Epsilon = 1e-4f;
            TerrainTileData tile = CreateTile(new Vector2Int(0, 0));
            System.Random random = new System.Random(3);
            for (int index = 0; index < tile.baseHeights.Length; index++)
            {
                tile.baseHeights[index] = (float)random.NextDouble() * 10f;
            }
            float[] heightsBefore = new float[tile.baseHeights.Length];
            for (int sampleZ = 0; sampleZ < SamplesPerSide; sampleZ++)
            {
                for (int sampleX = 0; sampleX < SamplesPerSide; sampleX++)
                {
                    heightsBefore[sampleZ * SamplesPerSide + sampleX] = tile.HeightAt(sampleX, sampleZ);
                }
            }

            TerrainHeightBrush.Smooth(Vector2.zero, 15f, 1f, 1f, LookUpTile);

            int checkedSamples = 0;
            for (int sampleZ = 36; sampleZ <= 64; sampleZ++)
            {
                for (int sampleX = 36; sampleX <= 64; sampleX++)
                {
                    float offsetX = sampleX - 50;
                    float offsetZ = sampleZ - 50;
                    if (Mathf.Sqrt(offsetX * offsetX + offsetZ * offsetZ) > 14f)
                    {
                        continue;
                    }
                    float minimum = float.MaxValue;
                    float maximum = float.MinValue;
                    for (int neighbourZ = sampleZ - 1; neighbourZ <= sampleZ + 1; neighbourZ++)
                    {
                        for (int neighbourX = sampleX - 1; neighbourX <= sampleX + 1; neighbourX++)
                        {
                            float before = heightsBefore[neighbourZ * SamplesPerSide + neighbourX];
                            minimum = Mathf.Min(minimum, before);
                            maximum = Mathf.Max(maximum, before);
                        }
                    }
                    float after = tile.HeightAt(sampleX, sampleZ);
                    Assert.GreaterOrEqual(after, minimum - Epsilon, "Below neighbourhood at " + sampleX + "," + sampleZ);
                    Assert.LessOrEqual(after, maximum + Epsilon, "Above neighbourhood at " + sampleX + "," + sampleZ);
                    checkedSamples++;
                }
            }
            Assert.Greater(checkedSamples, 0);
        }

        [Test]
        public void Brush_NeverWritesBaseRiverOrRoadLayers()
        {
            TerrainTileData tile = CreateTile(new Vector2Int(0, 0));

            TerrainHeightBrush.Raise(Vector2.zero, 12f, 0.5f, 2f, LookUpTile);
            TerrainHeightBrush.Noise(Vector2.zero, 12f, 0.5f, 7, 1.5f, 0.2f, LookUpTile);
            TerrainHeightBrush.Smooth(Vector2.zero, 12f, 0.5f, 1f, LookUpTile);

            AssertAllZero(tile.baseHeights, "baseHeights");
            AssertAllZero(tile.riverHeightDelta, "riverHeightDelta");
            AssertAllZero(tile.roadHeightDelta, "roadHeightDelta");
            bool handLayerChanged = Array.Exists(tile.handHeightDelta, value => value != 0f);
            Assert.IsTrue(handLayerChanged, "The brush wrote nothing at all, so the zero checks proved nothing.");
        }

        private TerrainTileData CreateTile(Vector2Int coordinate)
        {
            TerrainTileData tile = ScriptableObject.CreateInstance<TerrainTileData>();
            tile.coordinate = coordinate;
            tile.EnsureHeightArrays();
            createdTiles.Add(tile);
            tilesByCoordinate[coordinate] = tile;
            return tile;
        }

        private TerrainTileData LookUpTile(Vector2Int coordinate)
        {
            TerrainTileData tile;
            return tilesByCoordinate.TryGetValue(coordinate, out tile) ? tile : null;
        }

        private static void AssertAllZero(float[] values, string layerName)
        {
            for (int index = 0; index < values.Length; index++)
            {
                if (values[index] != 0f)
                {
                    Assert.Fail(layerName + " was written at index " + index);
                }
            }
        }
    }
}
