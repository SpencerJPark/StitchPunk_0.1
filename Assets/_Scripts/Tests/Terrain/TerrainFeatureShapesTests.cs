using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace StitchPunk.Tests
{
    [TestFixture]
    public sealed class TerrainFeatureShapesTests
    {
        [Test]
        public void CircleKnotsAreEightPointsAllAtTheRadius()
        {
            Vector2 centre = new Vector2(5f, -3f);
            List<Vector2> knots = TerrainFeatureShapes.CircleKnots(centre, 12f);

            Assert.AreEqual(8, knots.Count);
            foreach (Vector2 knot in knots)
            {
                Assert.AreEqual(12f, Vector2.Distance(centre, knot), 1e-3f);
            }
        }

        [Test]
        public void StraightEdgedAreaIsAllowedAndClosed()
        {
            bool closed;
            bool allowed = TerrainFeatureShapes.IsAllowed(
                TerrainFeatureShapeKind.Straight, TerrainFeatureShapeSupport.ClosedArea, out closed);

            Assert.IsTrue(allowed);
            Assert.IsTrue(closed);
        }

        [Test]
        public void CircleIsNotAllowedForOpenPathOnlySupport()
        {
            bool closed;
            bool allowed = TerrainFeatureShapes.IsAllowed(
                TerrainFeatureShapeKind.Circle, TerrainFeatureShapeSupport.OpenPath, out closed);

            Assert.IsFalse(allowed);
        }
    }
}
