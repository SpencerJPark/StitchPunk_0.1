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

        // A grid anchored at the world origin is the easy case; the offset origin is the one that
        // regresses, because it is what NavGridAuthoring produces whenever the grid is centred.
        private static readonly float3 ZeroOrigin = float3.zero;
        private static readonly float3 OffsetOrigin = new float3(-100f, 0f, -50f);

        [Test]
        public void WorldToGrid_FloorsIntoTheContainingCell()
        {
            int2 cell = PathfindingUtils.WorldToGrid(new float3(3.9f, 0f, 1.1f), NodeSize, ZeroOrigin);
            Assert.AreEqual(new int2(1, 0), cell);
        }

        [Test]
        public void WorldToGrid_HandlesNegativeCoordinates()
        {
            int2 cell = PathfindingUtils.WorldToGrid(new float3(-0.5f, 0f, -0.5f), NodeSize, ZeroOrigin);
            Assert.AreEqual(new int2(-1, -1), cell);
        }

        [Test]
        public void GridToWorld_ReturnsCellCenter()
        {
            float3 center = PathfindingUtils.GridToWorld(new int2(1, 2), NodeSize, ZeroOrigin);
            Assert.AreEqual(new float3(3f, 0f, 5f), center);
        }

        [Test]
        public void WorldToGrid_IsTheInverseOfGridCenter()
        {
            int2 original = new int2(4, 7);
            float3 center = PathfindingUtils.GridToWorld(original, NodeSize, ZeroOrigin);
            int2 roundTripped = PathfindingUtils.WorldToGrid(center, NodeSize, ZeroOrigin);
            Assert.AreEqual(original, roundTripped);
        }

        [Test]
        public void WorldToGrid_IsTheInverseOfGridCenter_WithAnOffsetOrigin()
        {
            int2 original = new int2(4, 7);
            float3 center = PathfindingUtils.GridToWorld(original, NodeSize, OffsetOrigin);
            Assert.AreEqual(new float3(-100f + 9f, 0f, -50f + 15f), center);

            int2 roundTripped = PathfindingUtils.WorldToGrid(center, NodeSize, OffsetOrigin);
            Assert.AreEqual(original, roundTripped);
        }

        [Test]
        public void WorldToGrid_PlacesTheOriginItselfInCellZero()
        {
            Assert.AreEqual(new int2(0, 0), PathfindingUtils.WorldToGrid(OffsetOrigin, NodeSize, OffsetOrigin));
            // One epsilon before the origin belongs to the cell outside the grid, not to cell 0.
            Assert.AreEqual(new int2(-1, -1),
                PathfindingUtils.WorldToGrid(OffsetOrigin - new float3(0.01f, 0f, 0.01f), NodeSize, OffsetOrigin));
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
