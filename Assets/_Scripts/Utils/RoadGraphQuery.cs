using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;

public static class RoadGraphQuery
{
    public const float MaxSnapDistanceMetres = 30f;
    private const float DuplicatePointMetres = 0.01f;

    private struct RoadSnap
    {
        public int edgeIndex;
        public int segmentIndex;
        public float3 point;
        public float distanceAlongEdge;
        public float distanceToRoadXZ;
    }

    [BurstCompile]
    public static bool TryFindRoute(ref RoadGraphBlob graph, in float3 fromWorld, in float3 toWorld, ref NativeList<float3> outWaypoints)
    {
        if (graph.edges.Length == 0)
        {
            return false;
        }

        RoadSnap startSnap = FindNearestSnap(ref graph, fromWorld);
        RoadSnap goalSnap = FindNearestSnap(ref graph, toWorld);
        if (startSnap.edgeIndex < 0 || goalSnap.edgeIndex < 0
            || startSnap.distanceToRoadXZ > MaxSnapDistanceMetres
            || goalSnap.distanceToRoadXZ > MaxSnapDistanceMetres)
        {
            return false;
        }

        if (startSnap.edgeIndex == goalSnap.edgeIndex)
        {
            outWaypoints.Clear();
            AppendPoint(ref outWaypoints, startSnap.point);
            AppendSameEdgeSlice(ref graph, startSnap, goalSnap, ref outWaypoints);
            AppendPoint(ref outWaypoints, goalSnap.point);
            return true;
        }

        return TryRouteAcrossNodes(ref graph, startSnap, goalSnap, ref outWaypoints);
    }

    private static RoadSnap FindNearestSnap(ref RoadGraphBlob graph, float3 worldPoint)
    {
        RoadSnap best = new RoadSnap { edgeIndex = -1, distanceToRoadXZ = float.MaxValue };
        for (int edgeIndex = 0; edgeIndex < graph.edges.Length; edgeIndex++)
        {
            RoadEdge edge = graph.edges[edgeIndex];
            float lengthBeforeSegment = 0f;
            for (int segmentIndex = 0; segmentIndex < edge.pointCount - 1; segmentIndex++)
            {
                float3 segmentStart = graph.polylinePoints[edge.firstPointIndex + segmentIndex];
                float3 segmentEnd = graph.polylinePoints[edge.firstPointIndex + segmentIndex + 1];
                float2 startXZ = segmentStart.xz;
                float2 segmentVectorXZ = segmentEnd.xz - startXZ;
                float segmentLengthSquaredXZ = math.lengthsq(segmentVectorXZ);
                float fraction = segmentLengthSquaredXZ > 1e-8f
                    ? math.saturate(math.dot(worldPoint.xz - startXZ, segmentVectorXZ) / segmentLengthSquaredXZ)
                    : 0f;
                float3 snapPoint = math.lerp(segmentStart, segmentEnd, fraction);
                float distanceXZ = math.distance(snapPoint.xz, worldPoint.xz);
                float segmentLength = math.distance(segmentStart, segmentEnd);

                if (distanceXZ < best.distanceToRoadXZ)
                {
                    best.edgeIndex = edgeIndex;
                    best.segmentIndex = segmentIndex;
                    best.point = snapPoint;
                    best.distanceAlongEdge = lengthBeforeSegment + fraction * segmentLength;
                    best.distanceToRoadXZ = distanceXZ;
                }

                lengthBeforeSegment += segmentLength;
            }
        }

        return best;
    }

    private static void AppendPoint(ref NativeList<float3> waypoints, float3 point)
    {
        if (waypoints.Length > 0 && math.distance(waypoints[waypoints.Length - 1], point) < DuplicatePointMetres)
        {
            return;
        }

        waypoints.Add(point);
    }

    private static void AppendSameEdgeSlice(ref RoadGraphBlob graph, RoadSnap startSnap, RoadSnap goalSnap, ref NativeList<float3> waypoints)
    {
        int firstPointIndex = graph.edges[startSnap.edgeIndex].firstPointIndex;
        if (startSnap.distanceAlongEdge <= goalSnap.distanceAlongEdge)
        {
            for (int pointOffset = startSnap.segmentIndex + 1; pointOffset <= goalSnap.segmentIndex; pointOffset++)
            {
                AppendPoint(ref waypoints, graph.polylinePoints[firstPointIndex + pointOffset]);
            }
        }
        else
        {
            for (int pointOffset = startSnap.segmentIndex; pointOffset > goalSnap.segmentIndex; pointOffset--)
            {
                AppendPoint(ref waypoints, graph.polylinePoints[firstPointIndex + pointOffset]);
            }
        }
    }

    private static void AppendWholeEdge(ref RoadGraphBlob graph, int edgeIndex, bool travelFromToTo, ref NativeList<float3> waypoints)
    {
        RoadEdge edge = graph.edges[edgeIndex];
        if (travelFromToTo)
        {
            for (int pointOffset = 0; pointOffset < edge.pointCount; pointOffset++)
            {
                AppendPoint(ref waypoints, graph.polylinePoints[edge.firstPointIndex + pointOffset]);
            }
        }
        else
        {
            for (int pointOffset = edge.pointCount - 1; pointOffset >= 0; pointOffset--)
            {
                AppendPoint(ref waypoints, graph.polylinePoints[edge.firstPointIndex + pointOffset]);
            }
        }
    }

    private static bool TryRouteAcrossNodes(ref RoadGraphBlob graph, RoadSnap startSnap, RoadSnap goalSnap, ref NativeList<float3> outWaypoints)
    {
        int nodeCount = graph.nodes.Length;
        NativeArray<float> costSoFar = new NativeArray<float>(nodeCount, Allocator.Temp);
        NativeArray<float> estimatedTotal = new NativeArray<float>(nodeCount, Allocator.Temp);
        NativeArray<bool> isOpen = new NativeArray<bool>(nodeCount, Allocator.Temp);
        NativeArray<int> cameFromNode = new NativeArray<int>(nodeCount, Allocator.Temp);
        NativeArray<int> cameFromEdge = new NativeArray<int>(nodeCount, Allocator.Temp);
        // For seeded nodes: true when the start edge is left through its fromNode end.
        NativeArray<bool> seededThroughFromEnd = new NativeArray<bool>(nodeCount, Allocator.Temp);
        for (int nodeIndex = 0; nodeIndex < nodeCount; nodeIndex++)
        {
            costSoFar[nodeIndex] = float.MaxValue;
            estimatedTotal[nodeIndex] = float.MaxValue;
            cameFromNode[nodeIndex] = -1;
            cameFromEdge[nodeIndex] = -1;
        }

        RoadEdge startEdge = graph.edges[startSnap.edgeIndex];
        RoadEdge goalEdge = graph.edges[goalSnap.edgeIndex];
        float3 goalPoint = goalSnap.point;

        float costToStartFromEnd = startSnap.distanceAlongEdge;
        float costToStartToEnd = startEdge.lengthMetres - startSnap.distanceAlongEdge;
        costSoFar[startEdge.toNode] = costToStartToEnd;
        seededThroughFromEnd[startEdge.toNode] = false;
        estimatedTotal[startEdge.toNode] = costToStartToEnd + math.distance(graph.nodes[startEdge.toNode].position, goalPoint);
        isOpen[startEdge.toNode] = true;
        if (costToStartFromEnd < costSoFar[startEdge.fromNode])
        {
            costSoFar[startEdge.fromNode] = costToStartFromEnd;
            seededThroughFromEnd[startEdge.fromNode] = true;
            estimatedTotal[startEdge.fromNode] = costToStartFromEnd + math.distance(graph.nodes[startEdge.fromNode].position, goalPoint);
            isOpen[startEdge.fromNode] = true;
        }

        float bestTotalCost = float.MaxValue;
        int bestEndNode = -1;
        bool bestEntersGoalEdgeAtFromEnd = false;

        while (true)
        {
            int currentNode = -1;
            float lowestEstimate = float.MaxValue;
            for (int nodeIndex = 0; nodeIndex < nodeCount; nodeIndex++)
            {
                if (isOpen[nodeIndex] && estimatedTotal[nodeIndex] < lowestEstimate)
                {
                    lowestEstimate = estimatedTotal[nodeIndex];
                    currentNode = nodeIndex;
                }
            }

            if (currentNode < 0 || lowestEstimate >= bestTotalCost)
            {
                break;
            }

            isOpen[currentNode] = false;
            float currentCost = costSoFar[currentNode];

            if (currentNode == goalEdge.fromNode)
            {
                float candidateCost = currentCost + goalSnap.distanceAlongEdge;
                if (candidateCost < bestTotalCost)
                {
                    bestTotalCost = candidateCost;
                    bestEndNode = currentNode;
                    bestEntersGoalEdgeAtFromEnd = true;
                }
            }

            if (currentNode == goalEdge.toNode)
            {
                float candidateCost = currentCost + goalEdge.lengthMetres - goalSnap.distanceAlongEdge;
                if (candidateCost < bestTotalCost)
                {
                    bestTotalCost = candidateCost;
                    bestEndNode = currentNode;
                    bestEntersGoalEdgeAtFromEnd = false;
                }
            }

            RoadNode node = graph.nodes[currentNode];
            for (int adjacencyOffset = 0; adjacencyOffset < node.edgeCount; adjacencyOffset++)
            {
                int edgeIndex = graph.nodeEdgeIndices[node.firstEdgeIndex + adjacencyOffset];
                RoadEdge edge = graph.edges[edgeIndex];
                int neighbourNode = edge.fromNode == currentNode ? edge.toNode : edge.fromNode;
                float tentativeCost = currentCost + edge.lengthMetres;
                if (tentativeCost < costSoFar[neighbourNode])
                {
                    costSoFar[neighbourNode] = tentativeCost;
                    cameFromNode[neighbourNode] = currentNode;
                    cameFromEdge[neighbourNode] = edgeIndex;
                    estimatedTotal[neighbourNode] = tentativeCost + math.distance(graph.nodes[neighbourNode].position, goalPoint);
                    isOpen[neighbourNode] = true;
                }
            }
        }

        bool routeFound = bestEndNode >= 0;
        if (routeFound)
        {
            NativeList<int> traversedEdgesReversed = new NativeList<int>(16, Allocator.Temp);
            int walkNode = bestEndNode;
            while (cameFromEdge[walkNode] >= 0)
            {
                traversedEdgesReversed.Add(cameFromEdge[walkNode]);
                walkNode = cameFromNode[walkNode];
            }

            int seedNode = walkNode;
            outWaypoints.Clear();
            AppendPoint(ref outWaypoints, startSnap.point);

            int startEdgeFirstPoint = startEdge.firstPointIndex;
            if (seededThroughFromEnd[seedNode])
            {
                for (int pointOffset = startSnap.segmentIndex; pointOffset >= 0; pointOffset--)
                {
                    AppendPoint(ref outWaypoints, graph.polylinePoints[startEdgeFirstPoint + pointOffset]);
                }
            }
            else
            {
                for (int pointOffset = startSnap.segmentIndex + 1; pointOffset < startEdge.pointCount; pointOffset++)
                {
                    AppendPoint(ref outWaypoints, graph.polylinePoints[startEdgeFirstPoint + pointOffset]);
                }
            }

            int currentRouteNode = seedNode;
            for (int listIndex = traversedEdgesReversed.Length - 1; listIndex >= 0; listIndex--)
            {
                int edgeIndex = traversedEdgesReversed[listIndex];
                RoadEdge edge = graph.edges[edgeIndex];
                bool travelFromToTo = edge.fromNode == currentRouteNode;
                AppendWholeEdge(ref graph, edgeIndex, travelFromToTo, ref outWaypoints);
                currentRouteNode = travelFromToTo ? edge.toNode : edge.fromNode;
            }

            int goalEdgeFirstPoint = goalEdge.firstPointIndex;
            if (bestEntersGoalEdgeAtFromEnd)
            {
                for (int pointOffset = 0; pointOffset <= goalSnap.segmentIndex; pointOffset++)
                {
                    AppendPoint(ref outWaypoints, graph.polylinePoints[goalEdgeFirstPoint + pointOffset]);
                }
            }
            else
            {
                for (int pointOffset = goalEdge.pointCount - 1; pointOffset > goalSnap.segmentIndex; pointOffset--)
                {
                    AppendPoint(ref outWaypoints, graph.polylinePoints[goalEdgeFirstPoint + pointOffset]);
                }
            }

            AppendPoint(ref outWaypoints, goalSnap.point);
            traversedEdgesReversed.Dispose();
        }

        costSoFar.Dispose();
        estimatedTotal.Dispose();
        isOpen.Dispose();
        cameFromNode.Dispose();
        cameFromEdge.Dispose();
        seededThroughFromEnd.Dispose();
        return routeFound;
    }
}
