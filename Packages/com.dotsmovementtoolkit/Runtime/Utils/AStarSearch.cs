using Unity.Collections;
using Unity.Mathematics;

namespace DotsMovementToolkit
{
    // Everything the A* search reads about the grid, so the search is a pure function EditMode tests can call.
    public struct AStarGridView
    {
        public int width;
        public int height;
        public int layerCount;
        public float cellSize;
        public float layerHeight;
        public float3 gridOrigin;
        public byte wallCost;
        [ReadOnly] public NativeArray<byte> costs;
        [ReadOnly] public NativeArray<NavGridStairConnection> stairs;

        public int CellsPerLayer => width * height;
        public int NodeCount => width * height * layerCount;

        public int NodeIndex(int2 cell, int layer) => layer * CellsPerLayer + cell.y * width + cell.x;
        public int2 NodeCell(int node) => new int2(node % width, (node % CellsPerLayer) / width);
        public int NodeLayer(int node) => node / CellsPerLayer;

        public bool IsInside(int2 cell, int layer)
            => cell.x >= 0 && cell.y >= 0 && cell.x < width && cell.y < height && layer >= 0 && layer < layerCount;

        public bool IsWalkable(int2 cell, int layer)
            => IsInside(cell, layer) && costs[NodeIndex(cell, layer)] != wallCost;

        public float2 WorldToCellSpace(float3 worldPosition)
            => (worldPosition.xz - gridOrigin.xz) / cellSize;

        public int ClampedLayerAt(float3 worldPosition)
            => math.clamp(NavGridSystem.GetLayer(worldPosition, layerHeight, gridOrigin), 0, layerCount - 1);
    }

    // Grid A* (8-way, no corner cutting, stair links between layers) plus string-pulling, which removes
    // every path corner that has a clear, no-costlier straight line past it.
    public static class AStarSearch
    {
        private struct OpenEntry
        {
            public float fScore;
            public int node;
        }

        // Fills nodePath start→goal. When the goal is unreachable it paths to the explored node closest
        // to the goal instead and returns false, so an agent still heads the right way.
        public static bool FindNodePath(int startNode, int goalNode, in AStarGridView grid, ref NativeList<int> nodePath)
        {
            nodePath.Clear();
            int nodeCount = grid.NodeCount;
            NativeArray<float> gScores = new NativeArray<float>(nodeCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            NativeArray<int> parents = new NativeArray<int>(nodeCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            NativeArray<bool> closed = new NativeArray<bool>(nodeCount, Allocator.Temp);
            NativeList<OpenEntry> openHeap = new NativeList<OpenEntry>(256, Allocator.Temp);
            for (int nodeIndex = 0; nodeIndex < nodeCount; nodeIndex++)
            {
                gScores[nodeIndex] = float.MaxValue;
                parents[nodeIndex] = -1;
            }

            int2 goalCell = grid.NodeCell(goalNode);
            gScores[startNode] = 0f;
            float closestHeuristic = OctileDistance(grid.NodeCell(startNode), goalCell);
            int closestNode = startNode;
            PushOpen(ref openHeap, new OpenEntry { fScore = closestHeuristic, node = startNode });

            bool reachedGoal = false;
            while (openHeap.Length > 0)
            {
                OpenEntry current = PopOpen(ref openHeap);
                if (closed[current.node])
                    continue;
                closed[current.node] = true;

                if (current.node == goalNode)
                {
                    reachedGoal = true;
                    break;
                }

                int2 currentCell = grid.NodeCell(current.node);
                int currentLayer = grid.NodeLayer(current.node);
                float currentHeuristic = OctileDistance(currentCell, goalCell);
                if (currentHeuristic < closestHeuristic)
                {
                    closestHeuristic = currentHeuristic;
                    closestNode = current.node;
                }

                float currentGScore = gScores[current.node];
                for (int offsetY = -1; offsetY <= 1; offsetY++)
                {
                    for (int offsetX = -1; offsetX <= 1; offsetX++)
                    {
                        if (offsetX == 0 && offsetY == 0)
                            continue;
                        int2 neighborCell = currentCell + new int2(offsetX, offsetY);
                        if (!grid.IsWalkable(neighborCell, currentLayer))
                            continue;
                        bool isDiagonal = offsetX != 0 && offsetY != 0;
                        // No corner cutting: a diagonal step needs both orthogonal cells open, or the
                        // agent would clip the wall's corner.
                        if (isDiagonal
                            && (!grid.IsWalkable(currentCell + new int2(offsetX, 0), currentLayer)
                                || !grid.IsWalkable(currentCell + new int2(0, offsetY), currentLayer)))
                            continue;

                        int neighborNode = grid.NodeIndex(neighborCell, currentLayer);
                        float stepCost = (isDiagonal ? math.SQRT2 : 1f) * math.max(1, (int)grid.costs[neighborNode]);
                        RelaxNeighbor(current.node, neighborNode, currentGScore + stepCost, goalCell, grid,
                            ref gScores, ref parents, in closed, ref openHeap);
                    }
                }

                if (grid.stairs.IsCreated)
                {
                    for (int stairIndex = 0; stairIndex < grid.stairs.Length; stairIndex++)
                    {
                        NavGridStairConnection stair = grid.stairs[stairIndex];
                        if (!stair.gridPosition.Equals(currentCell))
                            continue;
                        int otherLayer;
                        if (currentLayer == stair.fromLayer)
                            otherLayer = stair.toLayer;
                        else if (stair.bidirectional && currentLayer == stair.toLayer)
                            otherLayer = stair.fromLayer;
                        else
                            continue;
                        if (!grid.IsWalkable(currentCell, otherLayer))
                            continue;

                        int neighborNode = grid.NodeIndex(currentCell, otherLayer);
                        float climbCost = math.max(1f, math.distance(stair.entryWorldPosition, stair.exitWorldPosition) / grid.cellSize);
                        RelaxNeighbor(current.node, neighborNode, currentGScore + climbCost * math.max(1, (int)grid.costs[neighborNode]),
                            goalCell, grid, ref gScores, ref parents, in closed, ref openHeap);
                    }
                }
            }

            int endNode = reachedGoal ? goalNode : closestNode;
            for (int pathNode = endNode; pathNode != -1; pathNode = parents[pathNode])
                nodePath.Add(pathNode);
            for (int frontIndex = 0, backIndex = nodePath.Length - 1; frontIndex < backIndex; frontIndex++, backIndex--)
                (nodePath[frontIndex], nodePath[backIndex]) = (nodePath[backIndex], nodePath[frontIndex]);

            gScores.Dispose();
            parents.Dispose();
            closed.Dispose();
            openHeap.Dispose();
            return reachedGoal;
        }

        // Turns a node path into the corners an agent walks. A corner is dropped when the straight line
        // past it stays on open cells no costlier than the path it replaces, so shortcuts never cut
        // through heavy terrain the search deliberately went around. Layer changes always keep both ends.
        public static void BuildWaypoints(
            in NativeList<int> nodePath, float3 startPosition, float3 finalPosition, bool finalPositionIsExact,
            in AStarGridView grid, ref NativeList<AStarPathWaypoint> waypoints)
        {
            waypoints.Clear();
            int startLayer = grid.ClampedLayerAt(startPosition);
            if (nodePath.Length == 0)
            {
                waypoints.Add(new AStarPathWaypoint { position = finalPosition, layer = startLayer });
                return;
            }

            int lastIndex = nodePath.Length - 1;
            float2 anchor = grid.WorldToCellSpace(startPosition);
            // The agent's own cell is never tested: an agent standing in a cell marked as wall must
            // still be able to walk out of it.
            bool anchorIsAgent = true;
            int maxCostSinceAnchor = 0;

            for (int pathIndex = 1; pathIndex <= lastIndex; pathIndex++)
            {
                int node = nodePath[pathIndex];
                int previousNode = nodePath[pathIndex - 1];
                int layer = grid.NodeLayer(node);
                if (layer != grid.NodeLayer(previousNode))
                {
                    AddCorner(previousNode, startPosition.y, startLayer, grid, ref waypoints);
                    AddCorner(node, startPosition.y, startLayer, grid, ref waypoints);
                    anchor = CellCenter(node, grid);
                    anchorIsAgent = false;
                    maxCostSinceAnchor = grid.costs[node];
                    continue;
                }

                int candidateMaxCost = math.max(maxCostSinceAnchor, grid.costs[node]);
                float2 segmentEnd = pathIndex == lastIndex && finalPositionIsExact
                    ? grid.WorldToCellSpace(finalPosition)
                    : CellCenter(node, grid);
                if (IsSegmentClear(anchor, segmentEnd, layer, candidateMaxCost, anchorIsAgent, grid))
                {
                    maxCostSinceAnchor = candidateMaxCost;
                    continue;
                }

                // The agent cannot see this node, so its predecessor is a real corner. Right next to the
                // agent the predecessor is the agent's own cell, so keep this node as the first corner.
                int cornerNode = pathIndex == 1 ? node : previousNode;
                AddCorner(cornerNode, startPosition.y, startLayer, grid, ref waypoints);
                anchor = CellCenter(cornerNode, grid);
                anchorIsAgent = false;
                maxCostSinceAnchor = math.max((int)grid.costs[cornerNode], (int)grid.costs[node]);
            }

            int finalLayer = grid.NodeLayer(nodePath[lastIndex]);
            float3 finalWaypoint = finalPositionIsExact
                ? finalPosition
                : CornerPosition(nodePath[lastIndex], startPosition.y, startLayer, grid);
            if (waypoints.Length > 0 && math.distancesq(waypoints[waypoints.Length - 1].position.xz, finalWaypoint.xz) < 0.0001f)
                waypoints.RemoveAt(waypoints.Length - 1);
            waypoints.Add(new AStarPathWaypoint { position = finalWaypoint, layer = finalLayer });
        }

        // Walks every cell the segment touches (cell space), including both side cells where it passes
        // exactly through a corner. Fails on a wall, a cell costlier than maxCost, or the grid edge.
        public static bool IsSegmentClear(float2 fromCellSpace, float2 toCellSpace, int layer, int maxCost, bool skipFirstCell, in AStarGridView grid)
        {
            int2 cell = (int2)math.floor(fromCellSpace);
            int2 endCell = (int2)math.floor(toCellSpace);
            if (!skipFirstCell && !IsCellPassable(cell, layer, maxCost, grid))
                return false;

            float2 direction = toCellSpace - fromCellSpace;
            int2 step = new int2(direction.x > 0f ? 1 : direction.x < 0f ? -1 : 0, direction.y > 0f ? 1 : direction.y < 0f ? -1 : 0);
            float2 tDelta = new float2(
                step.x != 0 ? math.abs(1f / direction.x) : float.MaxValue,
                step.y != 0 ? math.abs(1f / direction.y) : float.MaxValue);
            float2 tMax = new float2(
                step.x > 0 ? (cell.x + 1 - fromCellSpace.x) * tDelta.x : step.x < 0 ? (fromCellSpace.x - cell.x) * tDelta.x : float.MaxValue,
                step.y > 0 ? (cell.y + 1 - fromCellSpace.y) * tDelta.y : step.y < 0 ? (fromCellSpace.y - cell.y) * tDelta.y : float.MaxValue);

            int maxSteps = math.abs(endCell.x - cell.x) + math.abs(endCell.y - cell.y) + 1;
            for (int stepIndex = 0; stepIndex < maxSteps && !cell.Equals(endCell); stepIndex++)
            {
                if (math.abs(tMax.x - tMax.y) < 1e-5f)
                {
                    if (!IsCellPassable(cell + new int2(step.x, 0), layer, maxCost, grid)
                        || !IsCellPassable(cell + new int2(0, step.y), layer, maxCost, grid))
                        return false;
                    cell += step;
                    tMax += tDelta;
                }
                else if (tMax.x < tMax.y)
                {
                    cell.x += step.x;
                    tMax.x += tDelta.x;
                }
                else
                {
                    cell.y += step.y;
                    tMax.y += tDelta.y;
                }

                if (!IsCellPassable(cell, layer, maxCost, grid))
                    return false;
            }
            // Float drift that misses the end cell counts as blocked, never as a free shortcut.
            return cell.Equals(endCell);
        }

        // Nearest open cell to a blocked one (a target standing inside a wall cell), searched in rings.
        public static bool TryFindNearestWalkableCell(int2 cell, int layer, int maxRadius, in AStarGridView grid, out int2 walkableCell)
        {
            walkableCell = cell;
            if (grid.IsWalkable(cell, layer))
                return true;
            for (int radius = 1; radius <= maxRadius; radius++)
            {
                float bestDistanceSquared = float.MaxValue;
                for (int offsetY = -radius; offsetY <= radius; offsetY++)
                {
                    for (int offsetX = -radius; offsetX <= radius; offsetX++)
                    {
                        if (math.max(math.abs(offsetX), math.abs(offsetY)) != radius)
                            continue;
                        int2 candidate = cell + new int2(offsetX, offsetY);
                        float distanceSquared = offsetX * offsetX + offsetY * offsetY;
                        if (distanceSquared < bestDistanceSquared && grid.IsWalkable(candidate, layer))
                        {
                            bestDistanceSquared = distanceSquared;
                            walkableCell = candidate;
                        }
                    }
                }
                if (bestDistanceSquared < float.MaxValue)
                    return true;
            }
            return false;
        }

        public static float OctileDistance(int2 firstCell, int2 secondCell)
        {
            int2 difference = math.abs(firstCell - secondCell);
            return math.max(difference.x, difference.y) + (math.SQRT2 - 1f) * math.min(difference.x, difference.y);
        }

        private static bool IsCellPassable(int2 cell, int layer, int maxCost, in AStarGridView grid)
        {
            if (!grid.IsInside(cell, layer))
                return false;
            byte cost = grid.costs[grid.NodeIndex(cell, layer)];
            return cost != grid.wallCost && cost <= maxCost;
        }

        private static void RelaxNeighbor(
            int fromNode, int neighborNode, float tentativeGScore, int2 goalCell, in AStarGridView grid,
            ref NativeArray<float> gScores, ref NativeArray<int> parents, in NativeArray<bool> closed, ref NativeList<OpenEntry> openHeap)
        {
            if (closed[neighborNode] || tentativeGScore >= gScores[neighborNode])
                return;
            gScores[neighborNode] = tentativeGScore;
            parents[neighborNode] = fromNode;
            PushOpen(ref openHeap, new OpenEntry
            {
                fScore = tentativeGScore + OctileDistance(grid.NodeCell(neighborNode), goalCell),
                node = neighborNode,
            });
        }

        private static float2 CellCenter(int node, in AStarGridView grid) => (float2)grid.NodeCell(node) + 0.5f;

        // Corners on the agent's own floor keep its height, so a grid origin below the terrain never
        // drags the move target into the ground; other floors are offset by whole layers.
        private static float3 CornerPosition(int node, float startHeight, int startLayer, in AStarGridView grid)
        {
            int2 cell = grid.NodeCell(node);
            int layer = grid.NodeLayer(node);
            return new float3(
                grid.gridOrigin.x + (cell.x + 0.5f) * grid.cellSize,
                startHeight + (layer - startLayer) * grid.layerHeight,
                grid.gridOrigin.z + (cell.y + 0.5f) * grid.cellSize);
        }

        private static void AddCorner(int node, float startHeight, int startLayer, in AStarGridView grid, ref NativeList<AStarPathWaypoint> waypoints)
        {
            float3 position = CornerPosition(node, startHeight, startLayer, grid);
            if (waypoints.Length > 0 && math.distancesq(waypoints[waypoints.Length - 1].position, position) < 0.0001f)
                return;
            waypoints.Add(new AStarPathWaypoint { position = position, layer = grid.NodeLayer(node) });
        }

        private static void PushOpen(ref NativeList<OpenEntry> openHeap, OpenEntry entry)
        {
            openHeap.Add(entry);
            int childIndex = openHeap.Length - 1;
            while (childIndex > 0)
            {
                int parentIndex = (childIndex - 1) / 2;
                if (openHeap[parentIndex].fScore <= openHeap[childIndex].fScore)
                    break;
                (openHeap[parentIndex], openHeap[childIndex]) = (openHeap[childIndex], openHeap[parentIndex]);
                childIndex = parentIndex;
            }
        }

        private static OpenEntry PopOpen(ref NativeList<OpenEntry> openHeap)
        {
            OpenEntry top = openHeap[0];
            int lastIndex = openHeap.Length - 1;
            openHeap[0] = openHeap[lastIndex];
            openHeap.RemoveAt(lastIndex);
            int parentIndex = 0;
            int count = openHeap.Length;
            while (true)
            {
                int leftIndex = parentIndex * 2 + 1;
                if (leftIndex >= count)
                    break;
                int rightIndex = leftIndex + 1;
                int smallerIndex = rightIndex < count && openHeap[rightIndex].fScore < openHeap[leftIndex].fScore ? rightIndex : leftIndex;
                if (openHeap[parentIndex].fScore <= openHeap[smallerIndex].fScore)
                    break;
                (openHeap[parentIndex], openHeap[smallerIndex]) = (openHeap[smallerIndex], openHeap[parentIndex]);
                parentIndex = smallerIndex;
            }
            return top;
        }
    }
}
