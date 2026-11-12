#if UNITY_EDITOR
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace StitchPunk.Tests
{
    [TestFixture]
    public sealed class TerrainAreaFillerTests
    {
        private static List<TerrainScatterPlacement> PlacementsWithRule(int count, int ruleIndex)
        {
            List<TerrainScatterPlacement> placements = new List<TerrainScatterPlacement>();
            for (int placementIndex = 0; placementIndex < count; placementIndex++)
            {
                TerrainScatterPlacement placement = new TerrainScatterPlacement();
                placement.ruleIndex = ruleIndex;
                placements.Add(placement);
            }
            return placements;
        }

        [Test]
        public void InteriorPlacementBeyondDemotionDistanceBecomesCard()
        {
            List<TerrainScatterPlacement> placements = PlacementsWithRule(1, 0);
            List<int> interactable = new List<int>();
            List<int> cards = new List<int>();
            int demoted;
            TerrainAreaFiller.SplitTiers(placements, new float[] { 20f }, new TerrainScatterTier[] { TerrainScatterTier.Interactable },
                12f, 100, interactable, cards, out demoted);
            Assert.AreEqual(0, interactable.Count);
            Assert.AreEqual(1, cards.Count);
            Assert.AreEqual(1, demoted);
        }

        [Test]
        public void InteractableCapIsNeverExceeded()
        {
            List<TerrainScatterPlacement> placements = PlacementsWithRule(10, 0);
            float[] distances = new float[10];
            for (int index = 0; index < 10; index++)
            {
                distances[index] = 1f + index * 0.5f;
            }
            List<int> interactable = new List<int>();
            List<int> cards = new List<int>();
            int demoted;
            TerrainAreaFiller.SplitTiers(placements, distances, new TerrainScatterTier[] { TerrainScatterTier.Interactable },
                12f, 4, interactable, cards, out demoted);
            Assert.AreEqual(4, interactable.Count);
            Assert.AreEqual(6, cards.Count);
            Assert.AreEqual(6, demoted);
        }

        [Test]
        public void CapKeepsPlacementsNearestTheEdge()
        {
            List<TerrainScatterPlacement> placements = PlacementsWithRule(8, 0);
            float[] distances = new float[] { 9f, 2f, 8f, 1f, 7f, 4f, 3f, 6f };
            List<int> interactable = new List<int>();
            List<int> cards = new List<int>();
            int demoted;
            TerrainAreaFiller.SplitTiers(placements, distances, new TerrainScatterTier[] { TerrainScatterTier.Interactable },
                12f, 4, interactable, cards, out demoted);
            interactable.Sort();
            CollectionAssert.AreEqual(new int[] { 1, 3, 5, 6 }, interactable);
        }

        [Test]
        public void DressingPlacementsAreCardsAndNotCountedAsDemoted()
        {
            List<TerrainScatterPlacement> placements = PlacementsWithRule(3, 1);
            List<int> interactable = new List<int>();
            List<int> cards = new List<int>();
            int demoted;
            TerrainAreaFiller.SplitTiers(placements, new float[] { 1f, 2f, 3f },
                new TerrainScatterTier[] { TerrainScatterTier.Interactable, TerrainScatterTier.Dressing },
                12f, 100, interactable, cards, out demoted);
            Assert.AreEqual(0, interactable.Count);
            Assert.AreEqual(3, cards.Count);
            Assert.AreEqual(0, demoted);
        }
    }
}
#endif
