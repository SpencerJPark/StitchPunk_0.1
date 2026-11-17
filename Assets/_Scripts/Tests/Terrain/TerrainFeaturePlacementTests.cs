using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace StitchPunk.Tests
{
    [TestFixture]
    public sealed class TerrainFeaturePlacementTests
    {
        private const float Tolerance = 1e-3f;

        private static List<Vector2> ThreeEdgePolyline()
        {
            return new List<Vector2>
            {
                new Vector2(0f, 0f), new Vector2(10f, 0f), new Vector2(10f, 10f), new Vector2(0f, 10f),
            };
        }

        private static TerrainAlongPathPlacer SegmentsPlacer(float jitterMetres)
        {
            TerrainAlongPathPlacer placer = new TerrainAlongPathPlacer();
            placer.mode = TerrainAlongPathMode.Segments;
            placer.spacingMetres = 2.5f;
            placer.jitterMetres = jitterMetres;
            return placer;
        }

        private static List<Vector2> OffsetRectangle(float width, float height)
        {
            return new List<Vector2>
            {
                new Vector2(0.1f, 0.1f), new Vector2(width + 0.1f, 0.1f),
                new Vector2(width + 0.1f, height + 0.1f), new Vector2(0.1f, height + 0.1f),
            };
        }

        private static TerrainRowsPlacer RowsPlacer(float directionDegrees, float jitterMetres)
        {
            TerrainRowsPlacer placer = new TerrainRowsPlacer();
            placer.rowSpacingMetres = 1f;
            placer.alongRowSpacingMetres = 0.5f;
            placer.jitterMetres = jitterMetres;
            placer.rowDirectionDegrees = directionDegrees;
            return placer;
        }

        private static int CountDistinctRounded(List<TerrainPlacementResult> results, bool useZ)
        {
            HashSet<int> distinct = new HashSet<int>();
            foreach (TerrainPlacementResult result in results)
            {
                float coordinate = useZ ? result.localXZ.y : result.localXZ.x;
                distinct.Add(Mathf.RoundToInt(coordinate * 100f));
            }

            return distinct.Count;
        }

        [Test]
        public void PointOnSharedTileBorderIsOwnedByExactlyOneTile()
        {
            Rect left = new Rect(0f, 0f, 100f, 100f);
            Rect right = new Rect(100f, 0f, 100f, 100f);
            Vector2 onBorder = new Vector2(100f, 50f);

            Assert.IsFalse(TerrainFeaturePlacement.IsOwnedByTile(onBorder, left));
            Assert.IsTrue(TerrainFeaturePlacement.IsOwnedByTile(onBorder, right));
        }

        [Test]
        public void GapEdgeGetsNoSegmentButStillGetsPostsAndOtherEdgesKeepSegments()
        {
            List<TerrainPlacementResult> results = TerrainFeaturePlacement.PlanAlongPath(
                ThreeEdgePolyline(), false, new List<int> { 1 }, SegmentsPlacer(0f), 1, 7);

            int segmentsOnEdgeOne = 0;
            int segmentsOnEdgeZero = 0;
            int postsOnEdgeOne = 0;
            foreach (TerrainPlacementResult result in results)
            {
                bool startsOnEdgeOne = Mathf.Abs(result.localXZ.x - 10f) < Tolerance && result.localXZ.y < 9.99f;
                if (result.isSegment && startsOnEdgeOne)
                {
                    segmentsOnEdgeOne++;
                }
                if (result.isSegment && Mathf.Abs(result.localXZ.y) < Tolerance && result.localXZ.x < 9.99f)
                {
                    segmentsOnEdgeZero++;
                }
                if (!result.isSegment && startsOnEdgeOne)
                {
                    postsOnEdgeOne++;
                }
            }

            Assert.AreEqual(0, segmentsOnEdgeOne);
            Assert.Greater(postsOnEdgeOne, 0);
            Assert.Greater(segmentsOnEdgeZero, 0);
        }

        [Test]
        public void EverySegmentSpansTwoConsecutivePosts()
        {
            List<TerrainPlacementResult> results = TerrainFeaturePlacement.PlanAlongPath(
                ThreeEdgePolyline(), false, null, SegmentsPlacer(0.3f), 1, 11);

            List<TerrainPlacementResult> posts = new List<TerrainPlacementResult>();
            List<TerrainPlacementResult> segments = new List<TerrainPlacementResult>();
            foreach (TerrainPlacementResult result in results)
            {
                if (result.isSegment)
                {
                    segments.Add(result);
                }
                else
                {
                    posts.Add(result);
                }
            }

            Assert.AreEqual(posts.Count - 1, segments.Count);
            for (int segmentIndex = 0; segmentIndex < segments.Count; segmentIndex++)
            {
                Assert.Less((segments[segmentIndex].localXZ - posts[segmentIndex].localXZ).magnitude, Tolerance);
                Assert.Less((segments[segmentIndex].segmentEndLocalXZ - posts[segmentIndex + 1].localXZ).magnitude, Tolerance);
            }
        }

        [Test]
        public void RowsFollowRowDirectionDegrees()
        {
            List<Vector2> outline = OffsetRectangle(10f, 5f);

            List<TerrainPlacementResult> alongX = TerrainFeaturePlacement.PlanRows(outline, RowsPlacer(0f, 0f), 1, 3);
            List<TerrainPlacementResult> alongZ = TerrainFeaturePlacement.PlanRows(outline, RowsPlacer(90f, 0f), 1, 3);

            // Rows along +X are lines of constant z (5 of them in 5 m); rows along +Z are lines of constant x (10 of them in 10 m).
            Assert.AreEqual(5, CountDistinctRounded(alongX, true));
            Assert.AreEqual(10, CountDistinctRounded(alongZ, false));
        }

        [Test]
        public void PlansAreDeterministicAndPlacementIdsAreUniqueWithinOnePlan()
        {
            List<TerrainPlacementResult> pathFirst = TerrainFeaturePlacement.PlanAlongPath(
                ThreeEdgePolyline(), false, null, SegmentsPlacer(0.4f), 3, 21);
            List<TerrainPlacementResult> pathSecond = TerrainFeaturePlacement.PlanAlongPath(
                ThreeEdgePolyline(), false, null, SegmentsPlacer(0.4f), 3, 21);
            List<Vector2> outline = OffsetRectangle(10f, 5f);
            List<TerrainPlacementResult> rowsFirst = TerrainFeaturePlacement.PlanRows(outline, RowsPlacer(float.NaN, 0.2f), 3, 21);
            List<TerrainPlacementResult> rowsSecond = TerrainFeaturePlacement.PlanRows(outline, RowsPlacer(float.NaN, 0.2f), 3, 21);

            AssertIdentical(pathFirst, pathSecond);
            AssertIdentical(rowsFirst, rowsSecond);
            AssertUniqueIds(pathFirst);
            AssertUniqueIds(rowsFirst);
        }

        private static void AssertIdentical(List<TerrainPlacementResult> first, List<TerrainPlacementResult> second)
        {
            Assert.Greater(first.Count, 0);
            Assert.AreEqual(first.Count, second.Count);
            for (int resultIndex = 0; resultIndex < first.Count; resultIndex++)
            {
                Assert.AreEqual(first[resultIndex].placementId, second[resultIndex].placementId);
                Assert.AreEqual(first[resultIndex].localXZ, second[resultIndex].localXZ);
                Assert.AreEqual(first[resultIndex].yawDegrees, second[resultIndex].yawDegrees);
                Assert.AreEqual(first[resultIndex].placeableIndex, second[resultIndex].placeableIndex);
            }
        }

        private static void AssertUniqueIds(List<TerrainPlacementResult> results)
        {
            HashSet<uint> seen = new HashSet<uint>();
            foreach (TerrainPlacementResult result in results)
            {
                Assert.IsTrue(seen.Add(result.placementId), "placementId repeated within one plan");
            }
        }
    }
}
