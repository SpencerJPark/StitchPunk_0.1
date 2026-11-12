using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace StitchPunk.Tests
{
    [TestFixture]
    public sealed class TerrainWorldRecipeBuilderTests
    {
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
        public void SameSeedGivesIdenticalRecipe()
        {
            TerrainGenerationProfile otherProfile = ScriptableObject.CreateInstance<TerrainGenerationProfile>();
            try
            {
                TerrainWorldRecipe first = TerrainWorldRecipeBuilder.Build(1234, 9, profile);
                TerrainWorldRecipe second = TerrainWorldRecipeBuilder.Build(1234, 9, otherProfile);

                Assert.AreEqual(first.mountains.Count, second.mountains.Count);
                for (int mountainIndex = 0; mountainIndex < first.mountains.Count; mountainIndex++)
                {
                    Assert.AreEqual(first.mountains[mountainIndex], second.mountains[mountainIndex]);
                }
                CollectionAssert.AreEqual(first.locations, second.locations);
                Assert.AreEqual(first.roads.Count, second.roads.Count);
                for (int roadIndex = 0; roadIndex < first.roads.Count; roadIndex++)
                {
                    CollectionAssert.AreEqual(first.roads[roadIndex], second.roads[roadIndex]);
                }
                CollectionAssert.AreEqual(first.river, second.river);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(otherProfile);
            }
        }

        [Test]
        public void DifferentSeedsGiveDifferentMountains()
        {
            TerrainWorldRecipe first = TerrainWorldRecipeBuilder.Build(1, 9, profile);
            TerrainWorldRecipe second = TerrainWorldRecipeBuilder.Build(2, 9, profile);

            CollectionAssert.AreNotEqual(first.mountains, second.mountains);
        }

        [Test]
        public void EverySeedKeepsMountainsAwayFromCampAndPlacesLocationsInsideTheWorld()
        {
            for (int seed = 1; seed <= 20; seed++)
            {
                TerrainWorldRecipe recipe = TerrainWorldRecipeBuilder.Build(seed, seed % 2 == 0 ? 7 : 11, profile);

                foreach (TerrainMountainFeature mountain in recipe.mountains)
                {
                    Assert.GreaterOrEqual(TerrainWorldRecipeBuilder.DistanceToSegment(Vector2.zero, mountain.startWorld, mountain.endWorld), 200f - 0.01f, "seed " + seed);
                }
                foreach (Vector2 location in recipe.locations)
                {
                    Assert.LessOrEqual(Mathf.Abs(location.x), recipe.worldHalfExtentMetres, "seed " + seed);
                    Assert.LessOrEqual(Mathf.Abs(location.y), recipe.worldHalfExtentMetres, "seed " + seed);
                }
                foreach (List<Vector3> road in recipe.roads)
                {
                    Assert.IsTrue(IsCampOrLocation(recipe, road[0]), "road start, seed " + seed);
                    Assert.IsTrue(IsCampOrLocation(recipe, road[road.Count - 1]), "road end, seed " + seed);
                }
            }
        }

        [Test]
        public void RiverTraceNeverClimbsAndLeavesThePeak()
        {
            profile.heightLayers.Clear();
            profile.mountains.Clear();
            profile.flatAreas.Clear();
            profile.mountains.Add(new TerrainMountainFeature { startWorld = Vector2.zero, endWorld = Vector2.zero, radiusMetres = 150f, heightMetres = 150f });

            Vector2 source = new Vector2(60f, 30f);
            List<Vector3> river = TerrainWorldRecipeBuilder.TraceRiverDownhill(profile, source, 500f);

            Assert.GreaterOrEqual(river.Count, 2);
            for (int pointIndex = 1; pointIndex < river.Count; pointIndex++)
            {
                float previousHeight = TerrainProceduralGenerator.AnalyticHeightAt(profile, river[pointIndex - 1].x, river[pointIndex - 1].z);
                float currentHeight = TerrainProceduralGenerator.AnalyticHeightAt(profile, river[pointIndex].x, river[pointIndex].z);
                Assert.LessOrEqual(currentHeight, previousHeight + 0.01f);
            }
            Vector3 last = river[river.Count - 1];
            Assert.Greater(new Vector2(last.x, last.z).magnitude, source.magnitude + 20f);
        }

        [Test]
        public void RoadOnFlatGroundIsNearlyStraight()
        {
            profile.heightLayers.Clear();
            profile.mountains.Clear();
            profile.flatAreas.Clear();

            Vector2 from = new Vector2(-300f, -100f);
            Vector2 to = new Vector2(250f, 150f);
            List<Vector3> road = TerrainWorldRecipeBuilder.RouteRoad(profile, from, to, 500f);

            float pathLength = 0f;
            for (int pointIndex = 1; pointIndex < road.Count; pointIndex++)
            {
                pathLength += Vector3.Distance(road[pointIndex - 1], road[pointIndex]);
            }
            float straightDistance = Vector2.Distance(from, to);
            Assert.LessOrEqual(pathLength, straightDistance * 1.1f);
            Assert.GreaterOrEqual(pathLength, straightDistance * 0.99f);
        }

        [Test]
        public void RoadDetoursAroundPeakOnTheStraightLine()
        {
            profile.heightLayers.Clear();
            profile.mountains.Clear();
            profile.flatAreas.Clear();
            profile.mountains.Add(new TerrainMountainFeature { startWorld = Vector2.zero, endWorld = Vector2.zero, radiusMetres = 100f, heightMetres = 200f });

            float peakHeight = TerrainProceduralGenerator.AnalyticHeightAt(profile, 0f, 0f);
            Assert.Greater(peakHeight, 100f);

            List<Vector3> road = TerrainWorldRecipeBuilder.RouteRoad(profile, new Vector2(-300f, 0f), new Vector2(300f, 0f), 500f);

            float highestOnRoad = float.MinValue;
            foreach (Vector3 point in road)
            {
                highestOnRoad = Mathf.Max(highestOnRoad, TerrainProceduralGenerator.AnalyticHeightAt(profile, point.x, point.z));
            }
            Assert.Less(highestOnRoad, peakHeight * 0.5f);
        }

        private static bool IsCampOrLocation(TerrainWorldRecipe recipe, Vector3 point)
        {
            Vector2 flat = new Vector2(point.x, point.z);
            if (flat.magnitude < 0.01f) return true;
            foreach (Vector2 location in recipe.locations)
            {
                if (Vector2.Distance(location, flat) < 0.01f) return true;
            }
            return false;
        }
    }
}
