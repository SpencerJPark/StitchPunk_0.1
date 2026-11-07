using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace StitchPunk.Tests
{
    [TestFixture]
    public sealed class TerrainRoadEditingTests
    {
        private const float Tolerance = 1e-3f;

        [Test]
        public void NearestOnStraightLineReturnsProjectedPointAndFraction()
        {
            List<Vector2> points = new List<Vector2> { new Vector2(0f, 0f), new Vector2(100f, 0f) };
            bool found = TerrainRoadEditing.TryNearestOnPolyline(points, new Vector2(25f, 10f), 20f, out TerrainRoadEditing.PolylineHit hit);

            Assert.IsTrue(found);
            Assert.AreEqual(0, hit.segmentIndex);
            Assert.AreEqual(0.25f, hit.segmentFraction, Tolerance);
            Assert.AreEqual(10f, hit.distancePixels, Tolerance);
            Assert.AreEqual(25f, hit.pointOnLine.x, Tolerance);
            Assert.AreEqual(0f, hit.pointOnLine.y, Tolerance);
        }

        [Test]
        public void NearestOnCornerPicksTheNearerSegment()
        {
            List<Vector2> points = new List<Vector2> { new Vector2(0f, 0f), new Vector2(100f, 0f), new Vector2(100f, 100f) };
            bool found = TerrainRoadEditing.TryNearestOnPolyline(points, new Vector2(95f, 60f), 20f, out TerrainRoadEditing.PolylineHit hit);

            Assert.IsTrue(found);
            Assert.AreEqual(1, hit.segmentIndex);
            Assert.AreEqual(0.6f, hit.segmentFraction, Tolerance);
        }

        [Test]
        public void NearestOnPolylineFailsBeyondMaximumDistance()
        {
            List<Vector2> points = new List<Vector2> { new Vector2(0f, 0f), new Vector2(100f, 0f) };
            Assert.IsFalse(TerrainRoadEditing.TryNearestOnPolyline(points, new Vector2(50f, 30f), 20f, out TerrainRoadEditing.PolylineHit hit));
        }

        [Test]
        public void NearestPointIndexRespectsThresholdAndPicksNearest()
        {
            List<Vector2> points = new List<Vector2> { new Vector2(0f, 0f), new Vector2(50f, 0f), new Vector2(100f, 0f) };

            Assert.AreEqual(-1, TerrainRoadEditing.NearestPointIndex(points, new Vector2(25f, 40f), 10f));
            Assert.AreEqual(1, TerrainRoadEditing.NearestPointIndex(points, new Vector2(55f, 3f), 10f));
        }

        [Test]
        public void PolylineFractionIsMeasuredByIndexAcrossTheWholeLine()
        {
            Assert.AreEqual(0.625f, TerrainRoadEditing.PolylineFraction(5, 2, 0.5f), Tolerance);
        }

        [Test]
        public void EdgeLinesOfStraightPlusXRoadPutLeftAtPositiveZ()
        {
            List<Vector3> centre = new List<Vector3> { new Vector3(0f, 1f, 0f), new Vector3(10f, 1f, 0f) };
            List<Vector3> left = new List<Vector3>();
            List<Vector3> right = new List<Vector3>();
            TerrainRoadEditing.BuildEdgeLines(centre, 2f, left, right);

            Assert.AreEqual(2, left.Count);
            Assert.AreEqual(2f, left[0].z, Tolerance);
            Assert.AreEqual(-2f, right[0].z, Tolerance);
            Assert.AreEqual(2f, left[1].z, Tolerance);
            Assert.AreEqual(1f, left[0].y, Tolerance);
            Assert.AreEqual(10f, left[1].x, Tolerance);
        }

        [Test]
        public void EdgeLinesAtRightAngleCornerUseUnscaledAveragedNormal()
        {
            List<Vector3> centre = new List<Vector3> { new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f), new Vector3(10f, 0f, 10f) };
            List<Vector3> left = new List<Vector3>();
            List<Vector3> right = new List<Vector3>();
            TerrainRoadEditing.BuildEdgeLines(centre, 2f, left, right);

            Vector3 cornerOffset = left[1] - centre[1];
            Assert.AreEqual(2f, cornerOffset.magnitude, Tolerance);
            Assert.AreEqual(-Mathf.Sqrt(2f), cornerOffset.x, Tolerance);
            Assert.AreEqual(Mathf.Sqrt(2f), cornerOffset.z, Tolerance);
            Assert.AreEqual(-cornerOffset.x, (right[1] - centre[1]).x, Tolerance);
        }

        [Test]
        public void EdgeLinesAreClearedBeforeFilling()
        {
            List<Vector3> centre = new List<Vector3> { Vector3.zero, Vector3.right };
            List<Vector3> left = new List<Vector3> { Vector3.one, Vector3.one, Vector3.one };
            List<Vector3> right = new List<Vector3> { Vector3.one };
            TerrainRoadEditing.BuildEdgeLines(centre, 1f, left, right);

            Assert.AreEqual(2, left.Count);
            Assert.AreEqual(2, right.Count);
        }

        [Test]
        public void WidthFromHandleDragSnapsToTenthOfAMetre()
        {
            float width = TerrainRoadEditing.WidthFromHandleDrag(Vector3.zero, new Vector3(1.93f, 5f, 0f), 1f, 12f);
            Assert.AreEqual(3.9f, width, Tolerance);
        }

        [Test]
        public void WidthFromHandleDragClampsToMinimumAndMaximum()
        {
            Assert.AreEqual(1f, TerrainRoadEditing.WidthFromHandleDrag(Vector3.zero, new Vector3(0.1f, 0f, 0f), 1f, 12f), Tolerance);
            Assert.AreEqual(12f, TerrainRoadEditing.WidthFromHandleDrag(Vector3.zero, new Vector3(0f, 0f, 50f), 1f, 12f), Tolerance);
        }

        [Test]
        public void CanDeleteKnotKeepsTwoOpenAndThreeClosed()
        {
            Assert.IsFalse(TerrainRoadEditing.CanDeleteKnot(2, false));
            Assert.IsTrue(TerrainRoadEditing.CanDeleteKnot(3, false));
            Assert.IsFalse(TerrainRoadEditing.CanDeleteKnot(3, true));
            Assert.IsTrue(TerrainRoadEditing.CanDeleteKnot(4, true));
        }

        [Test]
        public void NearestEndKnotIndexPicksTheCloserEnd()
        {
            List<Vector3> knots = new List<Vector3> { new Vector3(0f, 0f, 0f), new Vector3(50f, 0f, 0f), new Vector3(100f, 0f, 0f) };

            Assert.AreEqual(0, TerrainRoadEditing.NearestEndKnotIndex(knots, new Vector3(-10f, 5f, 3f)));
            Assert.AreEqual(2, TerrainRoadEditing.NearestEndKnotIndex(knots, new Vector3(120f, 0f, 0f)));
        }
    }
}
