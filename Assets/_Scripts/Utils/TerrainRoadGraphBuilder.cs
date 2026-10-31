using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public struct RoadEdgeData
{
    public int fromNode;
    public int toNode;
    public float3[] points;
    public float lengthMetres;
}

public struct RoadGraphData
{
    public List<float3> nodePositions;
    public List<RoadEdgeData> edges;
}

// Pure polyline-to-graph conversion: finds road crossings and touching ends, splits roads into node-to-node edges.
public static class TerrainRoadGraphBuilder
{
    private const float DistinctPointEpsilonMetres = 1e-4f;
    private const float VertexReuseFraction = 1e-3f;
    private const float MinimumEdgeLengthMetres = 0.01f;
    private const float MinimumSelfLoopLengthMetres = 3f;
    private const float DegenerateSegmentEpsilon = 1e-9f;

    private struct JunctionEvent
    {
        public int roadA;
        public int segmentA;
        public float fractionA;
        public int roadB;
        public int segmentB;
        public float fractionB;
        public float3 position;
    }

    private struct NearSegmentPair
    {
        public int segmentA;
        public int segmentB;
        public float distance;
        public float fractionA;
        public float fractionB;
    }

    private struct Insertion
    {
        public int segment;
        public float fraction;
        public int eventIndex;
    }

    public static RoadGraphData Build(IReadOnlyList<Vector3[]> roadPolylines, float simplifyToleranceMetres = 0.5f, float junctionMergeMetres = 2f)
    {
        List<List<float3>> roads = CollectDistinctRoads(roadPolylines);
        int roadCount = roads.Count;

        List<JunctionEvent> events = new List<JunctionEvent>();
        for (int roadIndexA = 0; roadIndexA < roadCount; roadIndexA++)
        {
            for (int roadIndexB = roadIndexA + 1; roadIndexB < roadCount; roadIndexB++)
            {
                FindJunctionEventsBetweenRoads(roads, roadIndexA, roadIndexB, junctionMergeMetres, events);
            }
        }

        int eventCount = events.Count;
        int[] unionParents = new int[eventCount + 2 * roadCount];
        for (int itemIndex = 0; itemIndex < unionParents.Length; itemIndex++)
        {
            unionParents[itemIndex] = itemIndex;
        }

        for (int eventIndexA = 0; eventIndexA < eventCount; eventIndexA++)
        {
            for (int eventIndexB = eventIndexA + 1; eventIndexB < eventCount; eventIndexB++)
            {
                if (DistanceXZ(events[eventIndexA].position, events[eventIndexB].position) < junctionMergeMetres)
                {
                    Union(unionParents, eventIndexA, eventIndexB);
                }
            }
        }

        List<Insertion>[] insertionsPerRoad = new List<Insertion>[roadCount];
        for (int roadIndex = 0; roadIndex < roadCount; roadIndex++)
        {
            insertionsPerRoad[roadIndex] = new List<Insertion>();
        }
        for (int eventIndex = 0; eventIndex < eventCount; eventIndex++)
        {
            JunctionEvent junctionEvent = events[eventIndex];
            insertionsPerRoad[junctionEvent.roadA].Add(new Insertion { segment = junctionEvent.segmentA, fraction = junctionEvent.fractionA, eventIndex = eventIndex });
            insertionsPerRoad[junctionEvent.roadB].Add(new Insertion { segment = junctionEvent.segmentB, fraction = junctionEvent.fractionB, eventIndex = eventIndex });
        }

        List<List<float3>> splitRoads = new List<List<float3>>(roadCount);
        List<List<int>> vertexItems = new List<List<int>>(roadCount);
        for (int roadIndex = 0; roadIndex < roadCount; roadIndex++)
        {
            List<float3> splitPoints = new List<float3>();
            List<int> splitItems = new List<int>();
            InsertJunctionVertices(roads[roadIndex], insertionsPerRoad[roadIndex], unionParents, splitPoints, splitItems);
            splitRoads.Add(splitPoints);
            vertexItems.Add(splitItems);
        }

        for (int roadIndex = 0; roadIndex < roadCount; roadIndex++)
        {
            List<int> splitItems = vertexItems[roadIndex];
            int lastVertex = splitItems.Count - 1;
            if (splitItems[0] < 0)
            {
                splitItems[0] = eventCount + 2 * roadIndex;
            }
            if (splitItems[lastVertex] < 0)
            {
                splitItems[lastVertex] = eventCount + 2 * roadIndex + 1;
            }
        }

        // Endpoints that did not land on a junction vertex join a nearby junction or another road's loose endpoint.
        for (int roadIndex = 0; roadIndex < roadCount; roadIndex++)
        {
            for (int endIndex = 0; endIndex < 2; endIndex++)
            {
                int vertexIndex = endIndex == 0 ? 0 : splitRoads[roadIndex].Count - 1;
                int itemIndex = eventCount + 2 * roadIndex + endIndex;
                if (vertexItems[roadIndex][vertexIndex] != itemIndex)
                {
                    continue;
                }

                float3 endpointPosition = splitRoads[roadIndex][vertexIndex];
                for (int eventIndex = 0; eventIndex < eventCount; eventIndex++)
                {
                    if (DistanceXZ(endpointPosition, events[eventIndex].position) < junctionMergeMetres)
                    {
                        Union(unionParents, itemIndex, eventIndex);
                    }
                }
                for (int otherRoadIndex = roadIndex + 1; otherRoadIndex < roadCount; otherRoadIndex++)
                {
                    for (int otherEndIndex = 0; otherEndIndex < 2; otherEndIndex++)
                    {
                        int otherVertexIndex = otherEndIndex == 0 ? 0 : splitRoads[otherRoadIndex].Count - 1;
                        int otherItemIndex = eventCount + 2 * otherRoadIndex + otherEndIndex;
                        if (vertexItems[otherRoadIndex][otherVertexIndex] != otherItemIndex)
                        {
                            continue;
                        }
                        if (DistanceXZ(endpointPosition, splitRoads[otherRoadIndex][otherVertexIndex]) < junctionMergeMetres)
                        {
                            Union(unionParents, itemIndex, otherItemIndex);
                        }
                    }
                }
            }
        }

        RoadGraphData graph = new RoadGraphData
        {
            nodePositions = new List<float3>(),
            edges = new List<RoadEdgeData>()
        };

        Dictionary<int, int> nodeIdByRoot = new Dictionary<int, int>();
        List<float3> nodePositionSums = new List<float3>();
        List<int> nodeMemberCounts = new List<int>();
        for (int eventIndex = 0; eventIndex < eventCount; eventIndex++)
        {
            AddItemToNode(unionParents, eventIndex, events[eventIndex].position, nodeIdByRoot, nodePositionSums, nodeMemberCounts);
        }
        for (int roadIndex = 0; roadIndex < roadCount; roadIndex++)
        {
            for (int endIndex = 0; endIndex < 2; endIndex++)
            {
                int vertexIndex = endIndex == 0 ? 0 : splitRoads[roadIndex].Count - 1;
                int itemIndex = eventCount + 2 * roadIndex + endIndex;
                if (vertexItems[roadIndex][vertexIndex] == itemIndex)
                {
                    AddItemToNode(unionParents, itemIndex, splitRoads[roadIndex][vertexIndex], nodeIdByRoot, nodePositionSums, nodeMemberCounts);
                }
            }
        }
        for (int nodeIndex = 0; nodeIndex < nodePositionSums.Count; nodeIndex++)
        {
            graph.nodePositions.Add(nodePositionSums[nodeIndex] / nodeMemberCounts[nodeIndex]);
        }

        for (int roadIndex = 0; roadIndex < roadCount; roadIndex++)
        {
            BuildEdgesForRoad(splitRoads[roadIndex], vertexItems[roadIndex], unionParents, nodeIdByRoot, graph, simplifyToleranceMetres);
        }

        return graph;
    }

    private static List<List<float3>> CollectDistinctRoads(IReadOnlyList<Vector3[]> roadPolylines)
    {
        List<List<float3>> roads = new List<List<float3>>();
        for (int roadIndex = 0; roadIndex < roadPolylines.Count; roadIndex++)
        {
            Vector3[] source = roadPolylines[roadIndex];
            if (source == null)
            {
                continue;
            }

            List<float3> distinctPoints = new List<float3>(source.Length);
            for (int pointIndex = 0; pointIndex < source.Length; pointIndex++)
            {
                float3 point = source[pointIndex];
                if (distinctPoints.Count == 0 || math.distance(distinctPoints[distinctPoints.Count - 1], point) > DistinctPointEpsilonMetres)
                {
                    distinctPoints.Add(point);
                }
            }
            if (distinctPoints.Count >= 2)
            {
                roads.Add(distinctPoints);
            }
        }
        return roads;
    }

    private static void FindJunctionEventsBetweenRoads(List<List<float3>> roads, int roadIndexA, int roadIndexB, float junctionMergeMetres, List<JunctionEvent> events)
    {
        List<float3> roadA = roads[roadIndexA];
        List<float3> roadB = roads[roadIndexB];
        if (!BoundsOverlapXZ(roadA, roadB, junctionMergeMetres))
        {
            return;
        }

        List<NearSegmentPair> nearPairs = new List<NearSegmentPair>();
        Dictionary<long, int> pairIndexByKey = new Dictionary<long, int>();
        for (int segmentA = 0; segmentA < roadA.Count - 1; segmentA++)
        {
            for (int segmentB = 0; segmentB < roadB.Count - 1; segmentB++)
            {
                float distance = ClosestPointsOnSegmentsXZ(roadA[segmentA], roadA[segmentA + 1], roadB[segmentB], roadB[segmentB + 1], out float fractionA, out float fractionB);
                if (distance < junctionMergeMetres)
                {
                    pairIndexByKey[SegmentPairKey(segmentA, segmentB)] = nearPairs.Count;
                    nearPairs.Add(new NearSegmentPair { segmentA = segmentA, segmentB = segmentB, distance = distance, fractionA = fractionA, fractionB = fractionB });
                }
            }
        }

        // One junction per contiguous run of near segment pairs, so a long parallel overlap does not spawn many nodes.
        bool[] visited = new bool[nearPairs.Count];
        Stack<int> pending = new Stack<int>();
        for (int startIndex = 0; startIndex < nearPairs.Count; startIndex++)
        {
            if (visited[startIndex])
            {
                continue;
            }

            int bestIndex = startIndex;
            visited[startIndex] = true;
            pending.Push(startIndex);
            while (pending.Count > 0)
            {
                int currentIndex = pending.Pop();
                NearSegmentPair current = nearPairs[currentIndex];
                if (current.distance < nearPairs[bestIndex].distance)
                {
                    bestIndex = currentIndex;
                }
                for (int offsetA = -1; offsetA <= 1; offsetA++)
                {
                    for (int offsetB = -1; offsetB <= 1; offsetB++)
                    {
                        if (pairIndexByKey.TryGetValue(SegmentPairKey(current.segmentA + offsetA, current.segmentB + offsetB), out int neighbourIndex) && !visited[neighbourIndex])
                        {
                            visited[neighbourIndex] = true;
                            pending.Push(neighbourIndex);
                        }
                    }
                }
            }

            NearSegmentPair best = nearPairs[bestIndex];
            float3 pointOnA = math.lerp(roadA[best.segmentA], roadA[best.segmentA + 1], best.fractionA);
            float3 pointOnB = math.lerp(roadB[best.segmentB], roadB[best.segmentB + 1], best.fractionB);
            events.Add(new JunctionEvent
            {
                roadA = roadIndexA,
                segmentA = best.segmentA,
                fractionA = best.fractionA,
                roadB = roadIndexB,
                segmentB = best.segmentB,
                fractionB = best.fractionB,
                position = (pointOnA + pointOnB) * 0.5f
            });
        }
    }

    private static void InsertJunctionVertices(List<float3> road, List<Insertion> insertions, int[] unionParents, List<float3> outputPoints, List<int> outputItems)
    {
        int vertexCount = road.Count;
        int[] originalVertexItems = new int[vertexCount];
        for (int vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
        {
            originalVertexItems[vertexIndex] = -1;
        }

        List<Insertion> interiorInsertions = new List<Insertion>();
        for (int insertionIndex = 0; insertionIndex < insertions.Count; insertionIndex++)
        {
            Insertion insertion = insertions[insertionIndex];
            int reusedVertex = -1;
            if (insertion.fraction < VertexReuseFraction)
            {
                reusedVertex = insertion.segment;
            }
            else if (insertion.fraction > 1f - VertexReuseFraction)
            {
                reusedVertex = insertion.segment + 1;
            }

            if (reusedVertex < 0)
            {
                interiorInsertions.Add(insertion);
            }
            else if (originalVertexItems[reusedVertex] < 0)
            {
                originalVertexItems[reusedVertex] = insertion.eventIndex;
            }
            else
            {
                Union(unionParents, originalVertexItems[reusedVertex], insertion.eventIndex);
            }
        }

        interiorInsertions.Sort((Insertion left, Insertion right) =>
        {
            int segmentComparison = left.segment.CompareTo(right.segment);
            if (segmentComparison != 0)
            {
                return segmentComparison;
            }
            int fractionComparison = left.fraction.CompareTo(right.fraction);
            return fractionComparison != 0 ? fractionComparison : left.eventIndex.CompareTo(right.eventIndex);
        });

        int interiorCursor = 0;
        for (int vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
        {
            outputPoints.Add(road[vertexIndex]);
            outputItems.Add(originalVertexItems[vertexIndex]);
            if (vertexIndex >= vertexCount - 1)
            {
                continue;
            }

            float lastFraction = -1f;
            while (interiorCursor < interiorInsertions.Count && interiorInsertions[interiorCursor].segment == vertexIndex)
            {
                Insertion insertion = interiorInsertions[interiorCursor];
                interiorCursor++;
                if (lastFraction >= 0f && insertion.fraction - lastFraction < VertexReuseFraction)
                {
                    Union(unionParents, outputItems[outputItems.Count - 1], insertion.eventIndex);
                    continue;
                }
                lastFraction = insertion.fraction;
                outputPoints.Add(math.lerp(road[vertexIndex], road[vertexIndex + 1], insertion.fraction));
                outputItems.Add(insertion.eventIndex);
            }
        }
    }

    private static void BuildEdgesForRoad(List<float3> points, List<int> vertexItems, int[] unionParents, Dictionary<int, int> nodeIdByRoot, RoadGraphData graph, float simplifyToleranceMetres)
    {
        int previousMarkedVertex = -1;
        for (int vertexIndex = 0; vertexIndex < points.Count; vertexIndex++)
        {
            if (vertexItems[vertexIndex] < 0)
            {
                continue;
            }
            if (previousMarkedVertex >= 0)
            {
                int fromNode = nodeIdByRoot[Find(unionParents, vertexItems[previousMarkedVertex])];
                int toNode = nodeIdByRoot[Find(unionParents, vertexItems[vertexIndex])];

                List<float3> edgePoints = new List<float3>();
                for (int pointIndex = previousMarkedVertex; pointIndex <= vertexIndex; pointIndex++)
                {
                    edgePoints.Add(points[pointIndex]);
                }
                edgePoints[0] = graph.nodePositions[fromNode];
                edgePoints[edgePoints.Count - 1] = graph.nodePositions[toNode];

                float3[] simplified = SimplifyDouglasPeucker(edgePoints, simplifyToleranceMetres);
                float length = PolylineLength(simplified);
                bool tooShort = length < MinimumEdgeLengthMetres;
                bool tinyLoop = fromNode == toNode && length < MinimumSelfLoopLengthMetres;
                if (!tooShort && !tinyLoop)
                {
                    graph.edges.Add(new RoadEdgeData { fromNode = fromNode, toNode = toNode, points = simplified, lengthMetres = length });
                }
            }
            previousMarkedVertex = vertexIndex;
        }
    }

    private static void AddItemToNode(int[] unionParents, int itemIndex, float3 position, Dictionary<int, int> nodeIdByRoot, List<float3> nodePositionSums, List<int> nodeMemberCounts)
    {
        int root = Find(unionParents, itemIndex);
        if (!nodeIdByRoot.TryGetValue(root, out int nodeId))
        {
            nodeId = nodePositionSums.Count;
            nodeIdByRoot[root] = nodeId;
            nodePositionSums.Add(float3.zero);
            nodeMemberCounts.Add(0);
        }
        nodePositionSums[nodeId] += position;
        nodeMemberCounts[nodeId]++;
    }

    private static float3[] SimplifyDouglasPeucker(List<float3> points, float toleranceMetres)
    {
        int pointCount = points.Count;
        bool[] keep = new bool[pointCount];
        keep[0] = true;
        keep[pointCount - 1] = true;

        Stack<int2> ranges = new Stack<int2>();
        ranges.Push(new int2(0, pointCount - 1));
        while (ranges.Count > 0)
        {
            int2 range = ranges.Pop();
            float farthestDistance = -1f;
            int farthestIndex = -1;
            for (int pointIndex = range.x + 1; pointIndex < range.y; pointIndex++)
            {
                float distance = DistanceToSegment(points[pointIndex], points[range.x], points[range.y]);
                if (distance > farthestDistance)
                {
                    farthestDistance = distance;
                    farthestIndex = pointIndex;
                }
            }
            if (farthestIndex >= 0 && farthestDistance > toleranceMetres)
            {
                keep[farthestIndex] = true;
                ranges.Push(new int2(range.x, farthestIndex));
                ranges.Push(new int2(farthestIndex, range.y));
            }
        }

        List<float3> kept = new List<float3>();
        for (int pointIndex = 0; pointIndex < pointCount; pointIndex++)
        {
            if (keep[pointIndex])
            {
                kept.Add(points[pointIndex]);
            }
        }
        return kept.ToArray();
    }

    private static float PolylineLength(float3[] points)
    {
        float length = 0f;
        for (int pointIndex = 1; pointIndex < points.Length; pointIndex++)
        {
            length += math.distance(points[pointIndex - 1], points[pointIndex]);
        }
        return length;
    }

    private static float DistanceToSegment(float3 point, float3 segmentStart, float3 segmentEnd)
    {
        float3 segment = segmentEnd - segmentStart;
        float lengthSquared = math.dot(segment, segment);
        if (lengthSquared <= DegenerateSegmentEpsilon)
        {
            return math.distance(point, segmentStart);
        }
        float fraction = math.clamp(math.dot(point - segmentStart, segment) / lengthSquared, 0f, 1f);
        return math.distance(point, segmentStart + segment * fraction);
    }

    private static float ClosestPointsOnSegmentsXZ(float3 startA, float3 endA, float3 startB, float3 endB, out float fractionA, out float fractionB)
    {
        float2 originA = startA.xz;
        float2 originB = startB.xz;
        float2 directionA = endA.xz - originA;
        float2 directionB = endB.xz - originB;
        float2 offset = originA - originB;
        float lengthSquaredA = math.dot(directionA, directionA);
        float lengthSquaredB = math.dot(directionB, directionB);
        float projectionBOnOffset = math.dot(directionB, offset);

        if (lengthSquaredA <= DegenerateSegmentEpsilon && lengthSquaredB <= DegenerateSegmentEpsilon)
        {
            fractionA = 0f;
            fractionB = 0f;
        }
        else if (lengthSquaredA <= DegenerateSegmentEpsilon)
        {
            fractionA = 0f;
            fractionB = math.clamp(projectionBOnOffset / lengthSquaredB, 0f, 1f);
        }
        else
        {
            float projectionAOnOffset = math.dot(directionA, offset);
            if (lengthSquaredB <= DegenerateSegmentEpsilon)
            {
                fractionB = 0f;
                fractionA = math.clamp(-projectionAOnOffset / lengthSquaredA, 0f, 1f);
            }
            else
            {
                float directionDot = math.dot(directionA, directionB);
                float denominator = lengthSquaredA * lengthSquaredB - directionDot * directionDot;
                fractionA = denominator > DegenerateSegmentEpsilon
                    ? math.clamp((directionDot * projectionBOnOffset - projectionAOnOffset * lengthSquaredB) / denominator, 0f, 1f)
                    : 0f;
                fractionB = (directionDot * fractionA + projectionBOnOffset) / lengthSquaredB;
                if (fractionB < 0f)
                {
                    fractionB = 0f;
                    fractionA = math.clamp(-projectionAOnOffset / lengthSquaredA, 0f, 1f);
                }
                else if (fractionB > 1f)
                {
                    fractionB = 1f;
                    fractionA = math.clamp((directionDot - projectionAOnOffset) / lengthSquaredA, 0f, 1f);
                }
            }
        }

        return math.distance(originA + directionA * fractionA, originB + directionB * fractionB);
    }

    private static bool BoundsOverlapXZ(List<float3> roadA, List<float3> roadB, float marginMetres)
    {
        float2 minimumA = new float2(float.MaxValue);
        float2 maximumA = new float2(float.MinValue);
        float2 minimumB = new float2(float.MaxValue);
        float2 maximumB = new float2(float.MinValue);
        for (int pointIndex = 0; pointIndex < roadA.Count; pointIndex++)
        {
            minimumA = math.min(minimumA, roadA[pointIndex].xz);
            maximumA = math.max(maximumA, roadA[pointIndex].xz);
        }
        for (int pointIndex = 0; pointIndex < roadB.Count; pointIndex++)
        {
            minimumB = math.min(minimumB, roadB[pointIndex].xz);
            maximumB = math.max(maximumB, roadB[pointIndex].xz);
        }
        return math.all(minimumA - marginMetres <= maximumB) && math.all(minimumB - marginMetres <= maximumA);
    }

    private static float DistanceXZ(float3 first, float3 second)
    {
        return math.distance(first.xz, second.xz);
    }

    private static long SegmentPairKey(int segmentA, int segmentB)
    {
        return ((long)segmentA << 32) | (uint)segmentB;
    }

    private static int Find(int[] unionParents, int itemIndex)
    {
        while (unionParents[itemIndex] != itemIndex)
        {
            unionParents[itemIndex] = unionParents[unionParents[itemIndex]];
            itemIndex = unionParents[itemIndex];
        }
        return itemIndex;
    }

    // The lower root always wins so node numbering stays deterministic.
    private static void Union(int[] unionParents, int itemA, int itemB)
    {
        int rootA = Find(unionParents, itemA);
        int rootB = Find(unionParents, itemB);
        if (rootA == rootB)
        {
            return;
        }
        if (rootA < rootB)
        {
            unionParents[rootB] = rootA;
        }
        else
        {
            unionParents[rootA] = rootB;
        }
    }
}
