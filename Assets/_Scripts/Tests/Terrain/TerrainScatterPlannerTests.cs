using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace StitchPunk.Tests
{
    [TestFixture]
    public sealed class TerrainScatterPlannerTests
    {
        private sealed class FlatSampler : ITerrainScatterSampler
        {
            public float height = 5f;
            public float dirt = 1f;
            public float grass = 0f;
            public float forest = 0f;
            public float stone = 0f;
            public Rect blockedRect = new Rect(0f, 0f, 0f, 0f);
            public Rect steepRect = new Rect(0f, 0f, 0f, 0f);

            public float HeightAt(float worldX, float worldZ)
            {
                return height;
            }

            public Vector3 NormalAt(float worldX, float worldZ)
            {
                if (steepRect.Contains(new Vector2(worldX, worldZ)))
                {
                    float sixtyDegrees = 60f * Mathf.Deg2Rad;
                    return new Vector3(Mathf.Sin(sixtyDegrees), Mathf.Cos(sixtyDegrees), 0f);
                }
                return Vector3.up;
            }

            public bool TryGetBiome(float worldX, float worldZ, out float outDirt, out float outGrass, out float outForest, out float outStone)
            {
                outDirt = dirt;
                outGrass = grass;
                outForest = forest;
                outStone = stone;
                return !blockedRect.Contains(new Vector2(worldX, worldZ));
            }
        }

        private static readonly Rect HundredMetreRect = new Rect(0f, 0f, 100f, 100f);

        private static TerrainScatterRule CreateRule(float spacing, float density)
        {
            return new TerrainScatterRule
            {
                onDirt = 1f,
                onGrass = 1f,
                onForest = 1f,
                onStone = 1f,
                slopeRangeDegrees = new Vector2(0f, 90f),
                heightRange = new Vector2(-1000f, 1000f),
                minSpacingMetres = spacing,
                scaleRange = new Vector2(0.8f, 1.2f),
                yawJitterDegrees = 360f,
                densityPer100SquareMetres = density,
                clusterNoiseScale = 0f,
                clusterThreshold = 0f,
                alignToNormal = false,
            };
        }

        private static void AssertPairwiseSpacing(List<TerrainScatterPlacement> placements, float spacing)
        {
            for (int first = 0; first < placements.Count; first++)
            {
                for (int second = first + 1; second < placements.Count; second++)
                {
                    float deltaX = placements[first].position.x - placements[second].position.x;
                    float deltaZ = placements[first].position.z - placements[second].position.z;
                    Assert.GreaterOrEqual(Mathf.Sqrt(deltaX * deltaX + deltaZ * deltaZ), spacing - 1e-4f);
                }
            }
        }

        [Test]
        public void SameInputs_GiveIdenticalPlacements()
        {
            FlatSampler sampler = new FlatSampler();
            List<TerrainScatterRule> rules = new List<TerrainScatterRule> { CreateRule(2f, 5f) };

            List<TerrainScatterPlacement> first = TerrainScatterPlanner.Plan(rules, sampler, HundredMetreRect, 7, null);
            List<TerrainScatterPlacement> second = TerrainScatterPlanner.Plan(rules, sampler, HundredMetreRect, 7, null);
            List<TerrainScatterPlacement> other = TerrainScatterPlanner.Plan(rules, sampler, HundredMetreRect, 8, null);

            Assert.Greater(first.Count, 0);
            Assert.AreEqual(first.Count, second.Count);
            for (int index = 0; index < first.Count; index++)
            {
                Assert.AreEqual(first[index].ruleIndex, second[index].ruleIndex);
                Assert.AreEqual(first[index].position, second[index].position);
                Assert.AreEqual(first[index].normal, second[index].normal);
                Assert.AreEqual(first[index].yawDegrees, second[index].yawDegrees);
                Assert.AreEqual(first[index].scale, second[index].scale);
            }

            bool differs = other.Count != first.Count || other[0].position != first[0].position;
            Assert.IsTrue(differs);
        }

        [Test]
        public void NoTwoPlacementsAreCloserThanTheirSpacing()
        {
            List<TerrainScatterRule> rules = new List<TerrainScatterRule> { CreateRule(4f, 20f) };
            List<TerrainScatterPlacement> placements = TerrainScatterPlanner.Plan(rules, new FlatSampler(), HundredMetreRect, 3, null);

            Assert.Greater(placements.Count, 0);
            AssertPairwiseSpacing(placements, 4f);
        }

        [Test]
        public void ObstaclesAreKeptClear()
        {
            List<TerrainScatterRule> rules = new List<TerrainScatterRule> { CreateRule(2f, 10f) };
            List<TerrainScatterObstacle> obstacles = new List<TerrainScatterObstacle>
            {
                new TerrainScatterObstacle { position = new Vector3(50f, 0f, 50f), radius = 10f },
            };
            List<TerrainScatterPlacement> placements = TerrainScatterPlanner.Plan(rules, new FlatSampler(), HundredMetreRect, 5, obstacles);

            Assert.Greater(placements.Count, 0);
            for (int index = 0; index < placements.Count; index++)
            {
                float deltaX = placements[index].position.x - 50f;
                float deltaZ = placements[index].position.z - 50f;
                Assert.GreaterOrEqual(Mathf.Sqrt(deltaX * deltaX + deltaZ * deltaZ), 11f - 1e-4f);
            }
        }

        [Test]
        public void BlockedBiomeAndSteepSlopes_GetNoPlacements()
        {
            FlatSampler sampler = new FlatSampler
            {
                blockedRect = new Rect(0f, 0f, 40f, 100f),
                steepRect = new Rect(60f, 0f, 40f, 100f),
            };
            TerrainScatterRule rule = CreateRule(1f, 20f);
            rule.slopeRangeDegrees = new Vector2(0f, 35f);
            List<TerrainScatterPlacement> placements = TerrainScatterPlanner.Plan(
                new List<TerrainScatterRule> { rule }, sampler, HundredMetreRect, 11, null);

            Assert.Greater(placements.Count, 0);
            for (int index = 0; index < placements.Count; index++)
            {
                Vector2 position = new Vector2(placements[index].position.x, placements[index].position.z);
                Assert.IsFalse(sampler.blockedRect.Contains(position));
                Assert.IsFalse(sampler.steepRect.Contains(position));
            }
        }

        [Test]
        public void DensityIsRoughlyHonouredOnOpenGround()
        {
            List<TerrainScatterRule> rules = new List<TerrainScatterRule> { CreateRule(0.5f, 2f) };
            List<TerrainScatterPlacement> placements = TerrainScatterPlanner.Plan(rules, new FlatSampler(), HundredMetreRect, 13, null);

            Assert.That(placements.Count, Is.InRange(160, 240));
        }

        [Test]
        public void BiomeWeightZeroForTheOnlyPresentBiome_GivesNothing()
        {
            FlatSampler sampler = new FlatSampler { dirt = 0f, grass = 1f };
            TerrainScatterRule rule = CreateRule(1f, 10f);
            rule.onGrass = 0f;
            List<TerrainScatterPlacement> placements = TerrainScatterPlanner.Plan(
                new List<TerrainScatterRule> { rule }, sampler, HundredMetreRect, 2, null);

            Assert.AreEqual(0, placements.Count);
        }

        [Test]
        public void RulesShareTheSpacingGrid()
        {
            List<TerrainScatterRule> rules = new List<TerrainScatterRule> { CreateRule(5f, 1f), CreateRule(5f, 1f) };
            List<TerrainScatterPlacement> placements = TerrainScatterPlanner.Plan(rules, new FlatSampler(), HundredMetreRect, 17, null);

            bool hasFirstRule = false;
            bool hasSecondRule = false;
            for (int index = 0; index < placements.Count; index++)
            {
                hasFirstRule |= placements[index].ruleIndex == 0;
                hasSecondRule |= placements[index].ruleIndex == 1;
            }
            Assert.IsTrue(hasFirstRule);
            Assert.IsTrue(hasSecondRule);
            AssertPairwiseSpacing(placements, 5f);
        }
    }
}
