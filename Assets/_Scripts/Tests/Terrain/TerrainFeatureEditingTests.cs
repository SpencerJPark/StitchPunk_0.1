using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace StitchPunk.Tests
{
    [TestFixture]
    public sealed class TerrainFeatureEditingTests
    {
        private const float Tolerance = 1e-3f;

        private static List<Vector2> ConcaveLOutline()
        {
            return new List<Vector2>
            {
                new Vector2(0f, 0f), new Vector2(20f, 0f), new Vector2(20f, 10f),
                new Vector2(10f, 10f), new Vector2(10f, 20f), new Vector2(0f, 20f),
            };
        }

        [Test]
        public void BowTieIsSelfIntersectingButSquareAndConcaveLAreNot()
        {
            List<Vector2> bowTie = new List<Vector2>
            {
                new Vector2(0f, 0f), new Vector2(10f, 10f), new Vector2(10f, 0f), new Vector2(0f, 10f),
            };
            List<Vector2> square = TerrainFeatureEditing.BoxOutline(new Vector2(0f, 0f), new Vector2(10f, 10f));

            Assert.IsTrue(TerrainFeatureEditing.IsSelfIntersecting(bowTie));
            Assert.IsFalse(TerrainFeatureEditing.IsSelfIntersecting(square));
            Assert.IsFalse(TerrainFeatureEditing.IsSelfIntersecting(ConcaveLOutline()));
        }

        [Test]
        public void ContainsPointHandlesTheNotchOfAConcaveL()
        {
            List<Vector2> outline = ConcaveLOutline();

            Assert.IsTrue(TerrainFeatureEditing.ContainsPoint(outline, new Vector2(5f, 5f)));
            Assert.IsTrue(TerrainFeatureEditing.ContainsPoint(outline, new Vector2(5f, 15f)));
            Assert.IsFalse(TerrainFeatureEditing.ContainsPoint(outline, new Vector2(15f, 15f)));
        }

        [Test]
        public void BoxOutlineIgnoresCornerOrderAndIsCounterClockwise()
        {
            List<Vector2> forward = TerrainFeatureEditing.BoxOutline(new Vector2(2f, 3f), new Vector2(12f, 8f));
            List<Vector2> reversed = TerrainFeatureEditing.BoxOutline(new Vector2(12f, 8f), new Vector2(2f, 3f));

            Assert.AreEqual(4, forward.Count);
            Assert.AreEqual(forward, reversed);
            Assert.AreEqual(50f, TerrainFeatureEditing.AreaSquareMetres(forward), Tolerance);
            Assert.AreEqual(new Vector2(2f, 3f), forward[0]);
            Assert.AreEqual(new Vector2(12f, 3f), forward[1]);
        }

        [Test]
        public void AreaOfTheConcaveLIsThreeHundred()
        {
            Assert.AreEqual(300f, TerrainFeatureEditing.AreaSquareMetres(ConcaveLOutline()), Tolerance);
        }

        [Test]
        public void NearestSegmentBesideTheSecondEdgeReturnsOneAndProjectedPoint()
        {
            int segmentIndex = TerrainFeatureEditing.NearestSegmentIndex(ConcaveLOutline(), new Vector2(25f, 5f), out Vector2 closestPoint);

            Assert.AreEqual(1, segmentIndex);
            Assert.AreEqual(20f, closestPoint.x, Tolerance);
            Assert.AreEqual(5f, closestPoint.y, Tolerance);
        }
    }
}
