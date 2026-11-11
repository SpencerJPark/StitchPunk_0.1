using NUnit.Framework;
using UnityEngine;

namespace StitchPunk.Tests
{
    [TestFixture]
    public sealed class TerrainProceduralGeneratorTests
    {
        private const int SamplesPerSide = TerrainTileData.HeightSamplesPerSide;

        private TerrainGenerationProfile profile;

        [SetUp]
        public void CreateProfile()
        {
            profile = ScriptableObject.CreateInstance<TerrainGenerationProfile>();
        }

        [TearDown]
        public void DestroyProfile()
        {
            UnityEngine.Object.DestroyImmediate(profile);
        }

        [Test]
        public void AdjacentTilesShareTheirEdgeExactly()
        {
            profile.heightLayers.Clear();
            profile.heightLayers.Add(new TerrainHeightLayer { frequencyPerMetre = 0.013f, amplitudeMetres = 8f, octaves = 4, lacunarity = 2f, gain = 0.5f });
            profile.heightLayers.Add(new TerrainHeightLayer { frequencyPerMetre = 0.071f, amplitudeMetres = 1.5f, octaves = 2, lacunarity = 2.1f, gain = 0.45f });
            profile.mountainCentreWorld = new Vector2(60f, 40f);
            profile.mountainRadiusMetres = 90f;
            profile.mountainHeightMetres = 50f;

            float[] origin = GenerateTile(new Vector2Int(0, 0));
            float[] east = GenerateTile(new Vector2Int(1, 0));
            float[] north = GenerateTile(new Vector2Int(0, 1));

            for (int sampleIndex = 0; sampleIndex < SamplesPerSide; sampleIndex++)
            {
                Assert.AreEqual(origin[sampleIndex * SamplesPerSide + 100], east[sampleIndex * SamplesPerSide + 0]);
                Assert.AreEqual(origin[100 * SamplesPerSide + sampleIndex], north[0 * SamplesPerSide + sampleIndex]);
            }
        }

        [Test]
        public void SameSeedTwice_GivesIdenticalHeightsAndMask()
        {
            float[] firstHeights = GenerateTile(new Vector2Int(2, -1));
            float[] secondHeights = GenerateTile(new Vector2Int(2, -1));
            CollectionAssert.AreEqual(firstHeights, secondHeights);

            Color32[] firstMask = new Color32[64 * 64];
            Color32[] secondMask = new Color32[64 * 64];
            TerrainProceduralGenerator.GenerateBaseMask(profile, new Vector2Int(2, -1), 64, firstMask);
            TerrainProceduralGenerator.GenerateBaseMask(profile, new Vector2Int(2, -1), 64, secondMask);
            CollectionAssert.AreEqual(firstMask, secondMask);

            profile.seed = profile.seed + 7;
            float[] differentSeedHeights = GenerateTile(new Vector2Int(2, -1));
            CollectionAssert.AreNotEqual(firstHeights, differentSeedHeights);
        }

        [Test]
        public void MountainPeakSitsAtItsCentreAtTheConfiguredHeight()
        {
            profile.heightLayers.Clear();
            profile.baseHeightMetres = 0f;
            profile.mountainCentreWorld = new Vector2(20f, 30f);
            profile.mountainRadiusMetres = 60f;
            profile.mountainHeightMetres = 300f;

            Assert.AreEqual(300f, TerrainProceduralGenerator.AnalyticHeightAt(profile, 20f, 30f), 1f);

            float[] heights = GenerateTile(new Vector2Int(0, 0));
            float maximumHeight = float.MinValue;
            for (int sampleIndex = 0; sampleIndex < heights.Length; sampleIndex++)
            {
                maximumHeight = Mathf.Max(maximumHeight, heights[sampleIndex]);
            }
            Assert.AreEqual(300f, maximumHeight, 1f);
            Assert.AreEqual(300f, heights[80 * SamplesPerSide + 70], 1f);
        }

        [Test]
        public void FlatProfile_GivesAFlatTile()
        {
            profile.heightLayers.Clear();
            profile.baseHeightMetres = 2.5f;
            profile.mountainRadiusMetres = 0f;
            // Only the grass-everywhere rule: the default forest rule is noise-driven and would legitimately mix colours on flat ground.
            profile.biomeRules.RemoveRange(1, profile.biomeRules.Count - 1);

            float[] heights = GenerateTile(new Vector2Int(0, 0));
            for (int sampleIndex = 0; sampleIndex < heights.Length; sampleIndex++)
            {
                Assert.AreEqual(2.5f, heights[sampleIndex]);
            }

            Color32[] mask = new Color32[32 * 32];
            TerrainProceduralGenerator.GenerateBaseMask(profile, new Vector2Int(0, 0), 32, mask);
            for (int texelIndex = 1; texelIndex < mask.Length; texelIndex++)
            {
                Assert.AreEqual(mask[0], mask[texelIndex]);
            }
        }

        [Test]
        public void BiomeSlopeRule_PaintsStoneOnASteepMountainFlank()
        {
            profile.heightLayers.Clear();
            profile.baseHeightMetres = 0f;
            profile.mountainCentreWorld = Vector2.zero;
            profile.mountainRadiusMetres = 100f;
            profile.mountainHeightMetres = 100f;

            // Tile (0,0) spans world -50..50, so texel 90 of 100 is near x = 40 and z = 0.5
            Color32[] mask = new Color32[100 * 100];
            TerrainProceduralGenerator.GenerateBaseMask(profile, new Vector2Int(0, 0), 100, mask);
            Assert.AreEqual(TerrainSplatCodec.ColourOfLayer(TerrainSplatLayer.Stone), mask[50 * 100 + 90]);

            // Tile (2,0) spans world 150..250, texel 50 is near x = 200
            Color32[] flatMask = new Color32[100 * 100];
            TerrainProceduralGenerator.GenerateBaseMask(profile, new Vector2Int(2, 0), 100, flatMask);
            Assert.AreNotEqual(TerrainSplatCodec.ColourOfLayer(TerrainSplatLayer.Stone), flatMask[50 * 100 + 50]);
        }

        private void MakeFlatNoiselessProfile()
        {
            profile.heightLayers.Clear();
            profile.baseHeightMetres = 0f;
            profile.landLiftMetres = 0f;
            profile.mountainRadiusMetres = 0f;
            profile.flattenBelowMetres = -1000f;
            profile.mountains = new System.Collections.Generic.List<TerrainMountainFeature>();
            profile.flatAreas = new System.Collections.Generic.List<TerrainFlatArea>();
        }

        private float HeightAt(float worldX, float worldZ)
        {
            return TerrainProceduralGenerator.AnalyticHeightAt(profile, worldX, worldZ);
        }

        [Test]
        public void Ridge_RaisesSegmentMidpointByItsHeight_AndNothingBeyondRadius()
        {
            MakeFlatNoiselessProfile();
            float heightWithoutRidge = HeightAt(50f, 0f);
            float farHeightWithoutRidge = HeightAt(50f, 40f);

            profile.mountains.Add(new TerrainMountainFeature
            {
                startWorld = new Vector2(0f, 0f),
                endWorld = new Vector2(100f, 0f),
                radiusMetres = 20f,
                heightMetres = 30f
            });

            Assert.AreEqual(heightWithoutRidge + 30f, HeightAt(50f, 0f), 1e-3f);
            Assert.AreEqual(farHeightWithoutRidge, HeightAt(50f, 40f));
        }

        [Test]
        public void OverlappingPeaks_TakeTheMaximumNotTheSum()
        {
            MakeFlatNoiselessProfile();
            profile.mountains.Add(new TerrainMountainFeature { startWorld = Vector2.zero, endWorld = Vector2.zero, radiusMetres = 50f, heightMetres = 20f });
            profile.mountains.Add(new TerrainMountainFeature { startWorld = Vector2.zero, endWorld = Vector2.zero, radiusMetres = 50f, heightMetres = 35f });

            Assert.AreEqual(35f, HeightAt(0f, 0f), 1e-3f);
        }

        [Test]
        public void FlatArea_PinsHeightInsideFlatRadius_AndLeavesBeyondBlendEndUntouched()
        {
            MakeFlatNoiselessProfile();
            profile.mountains.Add(new TerrainMountainFeature { startWorld = Vector2.zero, endWorld = Vector2.zero, radiusMetres = 80f, heightMetres = 60f });
            float untouchedFar = HeightAt(70f, 0f);
            float untouchedNear = HeightAt(5f, 0f);
            Assert.AreNotEqual(5f, untouchedNear);

            profile.flatAreas.Add(new TerrainFlatArea { centreWorld = Vector2.zero, flatRadiusMetres = 10f, blendEndRadiusMetres = 40f, heightMetres = 5f });

            Assert.AreEqual(5f, HeightAt(5f, 0f));
            Assert.AreEqual(5f, HeightAt(0f, 0f));
            Assert.AreEqual(untouchedFar, HeightAt(70f, 0f));
        }

        [Test]
        public void NullMountainAndFlatAreaLists_DoNotThrow()
        {
            MakeFlatNoiselessProfile();
            profile.mountains = null;
            profile.flatAreas = null;

            Assert.DoesNotThrow(() => HeightAt(12f, 34f));
        }

        private float[] GenerateTile(Vector2Int tileCoordinate)
        {
            float[] heights = new float[TerrainTileData.HeightSampleCount];
            TerrainProceduralGenerator.GenerateHeights(profile, tileCoordinate, heights);
            return heights;
        }
    }
}
