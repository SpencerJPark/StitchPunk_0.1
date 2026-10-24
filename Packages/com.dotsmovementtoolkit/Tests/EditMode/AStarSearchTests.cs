using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

namespace DotsMovementToolkit.Tests.EditMode
{
    // The search and string-pulling are the whole of "characters take a clean, sensible path": each test
    // pins one rule a visual pass would only catch as a unit clipping a wall or wading through mud.
    [TestFixture]
    public sealed class AStarSearchTests
    {
        private const byte WallCost = byte.MaxValue;
        private const byte HeavyCost = 50;
        private const int GridSize = 10;

        private NativeArray<byte> costs;
        private NativeArray<NavGridStairConnection> stairs;
        private NativeList<int> nodePath;
        private NativeList<AStarPathWaypoint> waypoints;

        [SetUp]
        public void SetUp()
        {
            nodePath = new NativeList<int>(64, Allocator.Persistent);
            waypoints = new NativeList<AStarPathWaypoint>(16, Allocator.Persistent);
        }

        [TearDown]
        public void TearDown()
        {
            if (costs.IsCreated) costs.Dispose();
            if (stairs.IsCreated) stairs.Dispose();
            nodePath.Dispose();
            waypoints.Dispose();
        }

        [Test]
        public void FindNodePath_RoutesThroughTheGapInAWall_AndNeverEntersAWallCell()
        {
            AStarGridView grid = CreateGrid(1);
            FillCells(grid, new int2(5, 0), new int2(5, 8), 0, WallCost);

            bool reachedGoal = AStarSearch.FindNodePath(grid.NodeIndex(new int2(1, 1), 0), grid.NodeIndex(new int2(8, 1), 0), grid, ref nodePath);

            Assert.IsTrue(reachedGoal);
            bool crossedAtTheGap = false;
            for (int pathIndex = 0; pathIndex < nodePath.Length; pathIndex++)
            {
                Assert.AreNotEqual(WallCost, grid.costs[nodePath[pathIndex]], "path entered a wall cell");
                crossedAtTheGap |= grid.NodeCell(nodePath[pathIndex]).Equals(new int2(5, 9));
            }
            Assert.IsTrue(crossedAtTheGap);
        }

        [Test]
        public void FindNodePath_NeverCutsDiagonallyPastAWallCorner()
        {
            AStarGridView grid = CreateGrid(1);
            FillCells(grid, new int2(1, 0), new int2(1, 0), 0, WallCost);

            AStarSearch.FindNodePath(grid.NodeIndex(new int2(0, 0), 0), grid.NodeIndex(new int2(1, 1), 0), grid, ref nodePath);

            Assert.AreEqual(3, nodePath.Length, "a direct diagonal step would clip the wall's corner");
            Assert.AreEqual(new int2(0, 1), grid.NodeCell(nodePath[1]));
        }

        [Test]
        public void BuildWaypoints_OnAnOpenGrid_IsOneStraightLineToTheTarget()
        {
            AStarGridView grid = CreateGrid(1);
            float3 start = new float3(0.5f, 0f, 0.5f);
            float3 target = new float3(8.5f, 0f, 6.5f);

            AStarSearch.FindNodePath(grid.NodeIndex(new int2(0, 0), 0), grid.NodeIndex(new int2(8, 6), 0), grid, ref nodePath);
            AStarSearch.BuildWaypoints(nodePath, start, target, true, grid, ref waypoints);

            Assert.AreEqual(1, waypoints.Length);
            Assert.AreEqual(target, waypoints[0].position);
        }

        [Test]
        public void BuildWaypoints_AroundAWall_KeepsFewCornersAndEverySegmentStaysOffTheWall()
        {
            AStarGridView grid = CreateGrid(1);
            FillCells(grid, new int2(5, 0), new int2(5, 8), 0, WallCost);
            float3 start = new float3(1.5f, 0f, 1.5f);
            float3 target = new float3(8.5f, 0f, 1.5f);

            AStarSearch.FindNodePath(grid.NodeIndex(new int2(1, 1), 0), grid.NodeIndex(new int2(8, 1), 0), grid, ref nodePath);
            AStarSearch.BuildWaypoints(nodePath, start, target, true, grid, ref waypoints);

            // A one-cell gap needs a corner on each side of it plus the gap itself, then the target.
            Assert.LessOrEqual(waypoints.Length, 4, "string-pulling should leave only the corners at the gap");
            AssertEverySegmentIsClear(grid, start, 1);
        }

        [Test]
        public void BuildWaypoints_NeverShortcutsThroughHeavyTerrainThePathWentAround()
        {
            AStarGridView grid = CreateGrid(1);
            FillCells(grid, new int2(3, 0), new int2(6, 7), 0, HeavyCost);
            float3 start = new float3(1.5f, 0f, 1.5f);
            float3 target = new float3(8.5f, 0f, 1.5f);

            AStarSearch.FindNodePath(grid.NodeIndex(new int2(1, 1), 0), grid.NodeIndex(new int2(8, 1), 0), grid, ref nodePath);
            AStarSearch.BuildWaypoints(nodePath, start, target, true, grid, ref waypoints);

            AssertEverySegmentIsClear(grid, start, 1);
        }

        [Test]
        public void FindNodePath_ClimbsAStairToReachTheUpperFloor()
        {
            AStarGridView grid = CreateGrid(2);
            stairs = new NativeArray<NavGridStairConnection>(1, Allocator.Persistent);
            stairs[0] = new NavGridStairConnection
            {
                gridPosition = new int2(5, 5),
                fromLayer = 0,
                toLayer = 1,
                entryWorldPosition = new float3(5.5f, 0f, 5.5f),
                exitWorldPosition = new float3(5.5f, 3f, 5.5f),
                bidirectional = true,
            };
            grid.stairs = stairs;

            bool reachedGoal = AStarSearch.FindNodePath(grid.NodeIndex(new int2(1, 1), 0), grid.NodeIndex(new int2(8, 8), 1), grid, ref nodePath);
            AStarSearch.BuildWaypoints(nodePath, new float3(1.5f, 0f, 1.5f), new float3(8.5f, 3f, 8.5f), true, grid, ref waypoints);

            Assert.IsTrue(reachedGoal);
            int layerChanges = 0;
            for (int pathIndex = 1; pathIndex < nodePath.Length; pathIndex++)
            {
                if (grid.NodeLayer(nodePath[pathIndex]) == grid.NodeLayer(nodePath[pathIndex - 1]))
                    continue;
                layerChanges++;
                Assert.AreEqual(new int2(5, 5), grid.NodeCell(nodePath[pathIndex]), "changed floor away from the stair");
            }
            Assert.AreEqual(1, layerChanges);
            Assert.AreEqual(1, waypoints[waypoints.Length - 1].layer);
        }

        private AStarGridView CreateGrid(int layerCount)
        {
            costs = new NativeArray<byte>(GridSize * GridSize * layerCount, Allocator.Persistent);
            for (int cellIndex = 0; cellIndex < costs.Length; cellIndex++)
                costs[cellIndex] = 1;
            if (!stairs.IsCreated)
                stairs = new NativeArray<NavGridStairConnection>(0, Allocator.Persistent);
            return new AStarGridView
            {
                width = GridSize,
                height = GridSize,
                layerCount = layerCount,
                cellSize = 1f,
                layerHeight = 3f,
                gridOrigin = float3.zero,
                wallCost = WallCost,
                costs = costs,
                stairs = stairs,
            };
        }

        private static void FillCells(AStarGridView grid, int2 minimumCell, int2 maximumCell, int layer, byte cost)
        {
            NativeArray<byte> gridCosts = grid.costs;
            for (int cellY = minimumCell.y; cellY <= maximumCell.y; cellY++)
                for (int cellX = minimumCell.x; cellX <= maximumCell.x; cellX++)
                    gridCosts[grid.NodeIndex(new int2(cellX, cellY), layer)] = cost;
        }

        // Samples the walked line rather than reusing IsSegmentClear, which also rejects grazing a cell's
        // corner point: a legal diagonal step between two path cells touches exactly that.
        private void AssertEverySegmentIsClear(AStarGridView grid, float3 start, int maxCost)
        {
            const int SamplesPerSegment = 200;
            float2 segmentStart = grid.WorldToCellSpace(start);
            for (int waypointIndex = 0; waypointIndex < waypoints.Length; waypointIndex++)
            {
                float2 segmentEnd = grid.WorldToCellSpace(waypoints[waypointIndex].position);
                for (int sampleIndex = 0; sampleIndex < SamplesPerSegment; sampleIndex++)
                {
                    float2 sample = math.lerp(segmentStart, segmentEnd, (sampleIndex + 0.5f) / SamplesPerSegment);
                    byte cost = grid.costs[grid.NodeIndex((int2)math.floor(sample), 0)];
                    Assert.LessOrEqual(cost, maxCost, $"segment {waypointIndex} walks through a wall or a costlier cell at {sample}");
                }
                segmentStart = segmentEnd;
            }
        }
    }
}
