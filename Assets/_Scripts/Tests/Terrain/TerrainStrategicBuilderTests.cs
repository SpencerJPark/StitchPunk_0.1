using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace StitchPunk.Tests
{
    [TestFixture]
    public sealed class TerrainStrategicBuilderTests
    {
        private const float RampSlopePerMetre = 0.1f;

        private TerrainStrategicProfile profile;
        private Dictionary<Vector2Int, TerrainTileData> tiles;
        private List<UnityEngine.Object> createdObjects;
        private Color[] layerColours;

        [SetUp]
        public void CreateProfileAndTiles()
        {
            createdObjects = new List<UnityEngine.Object>();
            profile = ScriptableObject.CreateInstance<TerrainStrategicProfile>();
            profile.superChunkTiles = 1;
            profile.coarseCellMetres = 4f;
            profile.colourTexelMetres = 2f;
            createdObjects.Add(profile);
            tiles = new Dictionary<Vector2Int, TerrainTileData>();
            layerColours = new Color[]
            {
                new Color(0.45f, 0.36f, 0.26f, 1f),
                new Color(0.36f, 0.5f, 0.22f, 1f),
                new Color(0.12f, 0.26f, 0.12f, 1f),
                new Color(0.5f, 0.5f, 0.5f, 1f),
            };
        }

        [TearDown]
        public void DestroyCreatedObjects()
        {
            for (int index = 0; index < createdObjects.Count; index++)
            {
                if (createdObjects[index] != null)
                {
                    UnityEngine.Object.DestroyImmediate(createdObjects[index]);
                }
            }
        }

        private void AddRampTile(int tileX, int tileZ)
        {
            TerrainTileData tile = ScriptableObject.CreateInstance<TerrainTileData>();
            createdObjects.Add(tile);
            tile.coordinate = new Vector2Int(tileX, tileZ);
            tile.EnsureHeightArrays();
            float minWorldX = tileX * TerrainTileData.TileSizeMetres - TerrainTileData.TileSizeMetres * 0.5f;
            for (int sampleZ = 0; sampleZ < TerrainTileData.HeightSamplesPerSide; sampleZ++)
            {
                for (int sampleX = 0; sampleX < TerrainTileData.HeightSamplesPerSide; sampleX++)
                {
                    tile.baseHeights[sampleZ * TerrainTileData.HeightSamplesPerSide + sampleX] = (minWorldX + sampleX) * RampSlopePerMetre;
                }
            }
            tiles[tile.coordinate] = tile;
        }

        private static float LuminanceStandardDeviation(TerrainStrategicProfile testProfile, Color[] colours, float grassWeight, float forestWeight)
        {
            List<float> luminances = new List<float>();
            for (int offsetZ = 0; offsetZ < 16; offsetZ++)
            {
                for (int offsetX = 0; offsetX < 16; offsetX++)
                {
                    TerrainStrategicBuilder.StrategicTexelInput input = new TerrainStrategicBuilder.StrategicTexelInput();
                    input.worldX = 4f + offsetX;
                    input.worldZ = 4f + offsetZ;
                    input.grassWeight = grassWeight;
                    input.forestWeight = forestWeight;
                    input.lightMultiplier = 1f;
                    Color32 colour = TerrainStrategicBuilder.ColourAt(testProfile, colours, input);
                    luminances.Add((0.299f * colour.r + 0.587f * colour.g + 0.114f * colour.b) / 255f);
                }
            }
            float mean = 0f;
            for (int index = 0; index < luminances.Count; index++)
            {
                mean += luminances[index];
            }
            mean /= luminances.Count;
            float sumOfSquares = 0f;
            for (int index = 0; index < luminances.Count; index++)
            {
                sumOfSquares += (luminances[index] - mean) * (luminances[index] - mean);
            }
            return Mathf.Sqrt(sumOfSquares / luminances.Count);
        }

        [Test]
        public void HeightsAreIdenticalAlongTheBorderSharedByTwoAdjacentChunks()
        {
            AddRampTile(0, 0);
            AddRampTile(1, 0);

            Mesh left;
            Texture2D leftTexture;
            Mesh right;
            Texture2D rightTexture;
            Assert.IsTrue(TerrainStrategicBuilder.Build(profile, new Vector2Int(0, 0), tiles, 256, layerColours, out left, out leftTexture));
            Assert.IsTrue(TerrainStrategicBuilder.Build(profile, new Vector2Int(1, 0), tiles, 256, layerColours, out right, out rightTexture));
            createdObjects.Add(left);
            createdObjects.Add(leftTexture);
            createdObjects.Add(right);
            createdObjects.Add(rightTexture);

            Vector3[] leftVertices = left.vertices;
            Vector3[] rightVertices = right.vertices;
            int vertexCountPerSide = 26;
            Assert.AreEqual(vertexCountPerSide * vertexCountPerSide, leftVertices.Length);
            for (int indexZ = 0; indexZ < vertexCountPerSide; indexZ++)
            {
                float leftEdgeHeight = leftVertices[indexZ * vertexCountPerSide + vertexCountPerSide - 1].y;
                float rightEdgeHeight = rightVertices[indexZ * vertexCountPerSide].y;
                Assert.AreEqual(leftEdgeHeight, rightEdgeHeight, 0f);
                Assert.AreEqual(50f * RampSlopePerMetre + TerrainTileCreator.GroundTopHeight, leftEdgeHeight, 0.001f);
            }
        }

        [Test]
        public void ForestTexelsHaveHigherLocalContrastThanGrassTexels()
        {
            float forestContrast = LuminanceStandardDeviation(profile, layerColours, 0f, 1f);
            float grassContrast = LuminanceStandardDeviation(profile, layerColours, 1f, 0f);

            Assert.Greater(forestContrast, grassContrast + 0.005f);
        }

        [Test]
        public void WaterTexelsAreBluerThanLandTexels()
        {
            TerrainStrategicBuilder.StrategicTexelInput land = new TerrainStrategicBuilder.StrategicTexelInput();
            land.grassWeight = 1f;
            land.lightMultiplier = 1f;
            TerrainStrategicBuilder.StrategicTexelInput water = land;
            water.waterDepthMetres = 2f;

            Color32 landColour = TerrainStrategicBuilder.ColourAt(profile, layerColours, land);
            Color32 waterColour = TerrainStrategicBuilder.ColourAt(profile, layerColours, water);

            float landBlueness = landColour.b - (landColour.r + landColour.g) * 0.5f;
            float waterBlueness = waterColour.b - (waterColour.r + waterColour.g) * 0.5f;
            Assert.Greater(waterBlueness, landBlueness);
        }

        [Test]
        public void TextureNeverExceedsMaxTexturePixels()
        {
            AddRampTile(0, 0);
            profile.colourTexelMetres = 0.5f;

            Mesh mesh;
            Texture2D texture;
            Assert.IsTrue(TerrainStrategicBuilder.Build(profile, new Vector2Int(0, 0), tiles, 64, layerColours, out mesh, out texture));
            createdObjects.Add(mesh);
            createdObjects.Add(texture);

            Assert.LessOrEqual(texture.width, 64);
            Assert.LessOrEqual(texture.height, 64);
            Assert.Greater(texture.width, 0);
        }

        [Test]
        public void BuildReturnsFalseForAChunkWithNoTiles()
        {
            AddRampTile(5, 5);

            Mesh mesh;
            Texture2D texture;
            bool built = TerrainStrategicBuilder.Build(profile, new Vector2Int(0, 0), tiles, 256, layerColours, out mesh, out texture);

            Assert.IsFalse(built);
            Assert.IsNull(mesh);
            Assert.IsNull(texture);
        }
    }
}
