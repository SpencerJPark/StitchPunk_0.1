using DotsMovementToolkit;
using NUnit.Framework;
using Unity.Mathematics;

namespace DotsMovementToolkit.Tests.EditMode
{
    // Grid <-> world conversions and flat-index math underpin both FlowField and D* Lite. An off-by-one
    // here corrupts pathfinding in ways that are painful to spot at runtime, so the round-trips are pinned.
    [TestFixture]
    public sealed class PathfindingUtilsTests
    {
        private const float NodeSize = 2f;

        [Test]
        public void WorldToGrid_FloorsIntoTheContainingCell()
        {
            int2 cell = PathfindingUtils.WorldToGrid(new float3(3.9f, 0f, 1.1f), NodeSize);
            Assert.AreEqual(new int2(1, 0), cell);
        }

        [Test]
        public void WorldToGrid_HandlesNegativeCoordinates()
        {
            int2 cell = PathfindingUtils.WorldToGrid(new float3(-0.5f, 0f, -0.5f), NodeSize);
            Assert.AreEqual(new int2(-1, -1), cell);
        }

        [Test]
        public void GridToWorld_ReturnsCellCenter()
        {
            float3 center = PathfindingUtils.GridToWorld(new int2(1, 2), NodeSize);
            Assert.AreEqual(new float3(3f, 0f, 5f), center);
        }

        [Test]
        public void WorldToGrid_IsTheInverseOfGridCenter()
        {
            int2 original = new int2(4, 7);
            float3 center = PathfindingUtils.GridToWorld(original, NodeSize);
            int2 roundTripped = PathfindingUtils.WorldToGrid(center, NodeSize);
            Assert.AreEqual(original, roundTripped);
        }

        [Test]
        public void CalculateIndex_AndIndexToGrid_RoundTrip()
        {
            int width = 16;
            int2 original = new int2(5, 9);
            int flatIndex = PathfindingUtils.CalculateIndex(original, width);
            Assert.AreEqual(5 + 9 * 16, flatIndex);

            int2 roundTripped = PathfindingUtils.IndexToGrid(flatIndex, width);
            Assert.AreEqual(original, roundTripped);
        }

        [Test]
        public void CalculateIndex_OverloadsAgree()
        {
            int fromInt2 = PathfindingUtils.CalculateIndex(new int2(3, 4), 10);
            int fromComponents = PathfindingUtils.CalculateIndex(3, 4, 10);
            Assert.AreEqual(fromInt2, fromComponents);
        }

        [Test]
        public void IsValidPosition_RejectsOutOfBounds()
        {
            Assert.IsTrue(PathfindingUtils.IsValidPosition(new int2(0, 0), 8, 8));
            Assert.IsTrue(PathfindingUtils.IsValidPosition(new int2(7, 7), 8, 8));
            Assert.IsFalse(PathfindingUtils.IsValidPosition(new int2(-1, 0), 8, 8));
            Assert.IsFalse(PathfindingUtils.IsValidPosition(new int2(8, 0), 8, 8));
            Assert.IsFalse(PathfindingUtils.IsValidPosition(new int2(0, 8), 8, 8));
        }
    }
}
