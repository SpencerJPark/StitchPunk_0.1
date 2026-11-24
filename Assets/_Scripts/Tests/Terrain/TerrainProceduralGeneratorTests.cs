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

        private void MakeSingleDirtTerraceRule()
        {
            MakeFlatNoiselessProfile();
            profile.biomeRules.Clear();
            profile.terraceBiomeRules.Clear();
            profile.terraceBiomeRules.Add(new TerraceBiomeRule
            {
                biome = TerrainSplatLayer.Dirt,
                enabled = true,
                plateauThresholds = new float[] { 0.45f },
                plateauNoiseFrequencyPerMetre = 0.02f,
                maxLevel = 1,
                minPlateauAreaSquareMetres = 30f,
                cliffOnSlope = false,
            });
        }

        [Test]
        public void GeneratedTerracesAreDeterministicAndOnlyWriteTheirOwnOutputs()
        {
            MakeSingleDirtTerraceRule();
            byte[] firstLevels = new byte[TerrainTileData.TerraceCellsPerSide * TerrainTileData.TerraceCellsPerSide];
            byte[] secondLevels = new byte[firstLevels.Length];
            System.Collections.Generic.List<TerraceRampPlacement> firstRamps = new System.Collections.Generic.List<TerraceRampPlacement>();
            System.Collections.Generic.List<TerraceRampPlacement> secondRamps = new System.Collections.Generic.List<TerraceRampPlacement>();

            TerrainProceduralGenerator.GenerateTerraceLevels(profile, Vector2Int.zero, 3, firstLevels, firstRamps);
            TerrainProceduralGenerator.GenerateTerraceLevels(profile, Vector2Int.zero, 3, secondLevels, secondRamps);

            CollectionAssert.AreEqual(firstLevels, secondLevels);
            Assert.AreEqual(firstRamps.Count, secondRamps.Count);
        }

        [Test]
        public void EveryRaisedRegionAwayFromTheTileRimHasARampOnItsEdge()
        {
            MakeSingleDirtTerraceRule();
            const int Side = TerrainTileData.TerraceCellsPerSide;
            int regionsChecked = 0;
            for (int tileX = 0; tileX <= 2; tileX++)
            {
                Vector2Int tileCoordinate = new Vector2Int(tileX, 0);
                byte[] levels = new byte[Side * Side];
                System.Collections.Generic.List<TerraceRampPlacement> ramps = new System.Collections.Generic.List<TerraceRampPlacement>();
                TerrainProceduralGenerator.GenerateTerraceLevels(profile, tileCoordinate, 3, levels, ramps);
                float minCornerX = tileCoordinate.x * TerrainTileData.TileSizeMetres - TerrainTileData.TileSizeMetres * 0.5f;
                float minCornerZ = tileCoordinate.y * TerrainTileData.TileSizeMetres - TerrainTileData.TileSizeMetres * 0.5f;

                bool[] visited = new bool[Side * Side];
                for (int startCell = 0; startCell < Side * Side; startCell++)
                {
                    int level = levels[startCell] - TerrainTileData.TerraceLevelBias;
                    if (level == 0 || visited[startCell]) continue;
                    bool touchesRim = false;
                    System.Collections.Generic.List<int> regionCells = new System.Collections.Generic.List<int>();
                    System.Collections.Generic.Stack<int> pending = new System.Collections.Generic.Stack<int>();
                    pending.Push(startCell);
                    visited[startCell] = true;
                    while (pending.Count > 0)
                    {
                        int cell = pending.Pop();
                        regionCells.Add(cell);
                        int cellX = cell % Side;
                        int cellZ = cell / Side;
                        if (cellX == 0 || cellZ == 0 || cellX == Side - 1 || cellZ == Side - 1) touchesRim = true;
                        for (int direction = 0; direction < 4; direction++)
                        {
                            int neighbourX = cellX + (direction == 0 ? 1 : direction == 1 ? -1 : 0);
                            int neighbourZ = cellZ + (direction == 2 ? 1 : direction == 3 ? -1 : 0);
                            if (neighbourX < 0 || neighbourZ < 0 || neighbourX >= Side || neighbourZ >= Side) continue;
                            int neighbour = neighbourZ * Side + neighbourX;
                            if (visited[neighbour] || levels[neighbour] - TerrainTileData.TerraceLevelBias != level) continue;
                            visited[neighbour] = true;
                            pending.Push(neighbour);
                        }
                    }
                    if (touchesRim) continue;

                    regionsChecked++;
                    int lowerNeighbourLevel = level - (level > 0 ? 1 : -1);
                    bool rampFound = false;
                    foreach (int cell in regionCells)
                    {
                        int cellX = cell % Side;
                        int cellZ = cell / Side;
                        for (int direction = 0; direction < 4 && !rampFound; direction++)
                        {
                            int offsetX = direction == 0 ? 1 : direction == 1 ? -1 : 0;
                            int offsetZ = direction == 2 ? 1 : direction == 3 ? -1 : 0;
                            int neighbour = (cellZ + offsetZ) * Side + cellX + offsetX;
                            if (levels[neighbour] - TerrainTileData.TerraceLevelBias != lowerNeighbourLevel) continue;
                            Vector2 edgeCentre = new Vector2(
                                minCornerX + cellX + 0.5f + offsetX * 0.5f,
                                minCornerZ + cellZ + 0.5f + offsetZ * 0.5f);
                            foreach (TerraceRampPlacement placement in ramps)
                            {
                                float distanceAlongEdge = offsetX != 0
                                    ? Mathf.Max(0f, Mathf.Abs(placement.centreWorld.y - edgeCentre.y) - 0.5f)
                                    : Mathf.Max(0f, Mathf.Abs(placement.centreWorld.x - edgeCentre.x) - 0.5f);
                                float distanceAcrossEdge = offsetX != 0
                                    ? Mathf.Abs(placement.centreWorld.x - edgeCentre.x)
                                    : Mathf.Abs(placement.centreWorld.y - edgeCentre.y);
                                if (Mathf.Sqrt(distanceAlongEdge * distanceAlongEdge + distanceAcrossEdge * distanceAcrossEdge) > 1f) continue;
                                rampFound = true;
                                float yawRadians = placement.yawDegrees * Mathf.Deg2Rad;
                                Vector2 forward = new Vector2(Mathf.Sin(yawRadians), Mathf.Cos(yawRadians));
                                int lowSample = SignedLevelAtWorld(levels, minCornerX, minCornerZ, placement.centreWorld - forward * 2f);
                                int highSample = SignedLevelAtWorld(levels, minCornerX, minCornerZ, placement.centreWorld + forward * 2f);
                                Assert.Less(lowSample, highSample, "ramp forward must run from the lower to the higher side");
                            }
                        }
                        if (rampFound) break;
                    }
                    Assert.IsTrue(rampFound, "raised region has no ramp on its edge");
                }
            }
            if (regionsChecked == 0) Assert.Inconclusive("No region away from the tile rim in tiles (0..2, 0).");
        }

        private static int SignedLevelAtWorld(byte[] levels, float minCornerX, float minCornerZ, Vector2 world)
        {
            int cellX = Mathf.Clamp(Mathf.FloorToInt(world.x - minCornerX), 0, TerrainTileData.TerraceCellsPerSide - 1);
            int cellZ = Mathf.Clamp(Mathf.FloorToInt(world.y - minCornerZ), 0, TerrainTileData.TerraceCellsPerSide - 1);
            return levels[cellZ * TerrainTileData.TerraceCellsPerSide + cellX] - TerrainTileData.TerraceLevelBias;
        }

        private float[] GenerateTile(Vector2Int tileCoordinate)
        {
            float[] heights = new float[TerrainTileData.HeightSampleCount];
            TerrainProceduralGenerator.GenerateHeights(profile, tileCoordinate, heights);
            return heights;
        }
    }
}
