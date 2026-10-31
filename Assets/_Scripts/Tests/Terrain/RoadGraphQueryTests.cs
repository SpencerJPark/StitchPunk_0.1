using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace StitchPunk.Tests
{
    [TestFixture]
    public sealed class RoadGraphQueryTests
    {
        private BlobAssetReference<RoadGraphBlob> junctionBlob;
        private BlobAssetReference<RoadGraphBlob> emptyBlob;

        [SetUp]
        public void CreateBlobs()
        {
            junctionBlob = CreateBlob(TerrainRoadGraphBuilder.Build(new List<Vector3[]>
            {
                SampleLine(new Vector3(-50f, 0f, 0f), new Vector3(50f, 0f, 0f)),
                SampleLine(new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 40f)),
                SampleLine(new Vector3(200f, 0f, 0f), new Vector3(260f, 0f, 0f))
            }));
            emptyBlob = CreateBlob(TerrainRoadGraphBuilder.Build(new List<Vector3[]>()));
        }

        [TearDown]
        public void DisposeBlobs()
        {
            if (junctionBlob.IsCreated)
            {
                junctionBlob.Dispose();
            }
            if (emptyBlob.IsCreated)
            {
                emptyBlob.Dispose();
            }
        }

        [Test]
        public void Route_FromTheLeftEndOfTheMainRoadToTheEndOfTheSideRoad_PassesTheJunction()
        {
            NativeList<float3> waypoints = new NativeList<float3>(Allocator.Temp);
            try
            {
                bool found = RoadGraphQuery.TryFindRoute(ref junctionBlob.Value, new float3(-50f, 0f, 0f), new float3(0f, 0f, 40f), ref waypoints);

                Assert.IsTrue(found);
                Assert.Less(math.distance(waypoints[0], new float3(-50f, 0f, 0f)), 1f);
                Assert.Less(math.distance(waypoints[waypoints.Length - 1], new float3(0f, 0f, 40f)), 1f);
                bool passesJunction = false;
                for (int waypointIndex = 0; waypointIndex < waypoints.Length; waypointIndex++)
                {
                    if (math.distance(waypoints[waypointIndex], float3.zero) <= 2f)
                    {
                        passesJunction = true;
                    }
                }
                Assert.IsTrue(passesJunction);
                Assert.AreEqual(90f, PolylineLength(waypoints), 90f * 0.03f);
            }
            finally
            {
                waypoints.Dispose();
            }
        }

        [Test]
        public void Route_BetweenTwoPointsOnTheSameEdge_IsTheSubSegment()
        {
            NativeList<float3> waypoints = new NativeList<float3>(Allocator.Temp);
            try
            {
                bool found = RoadGraphQuery.TryFindRoute(ref junctionBlob.Value, new float3(-30f, 0f, 2f), new float3(-10f, 0f, -1f), ref waypoints);

                Assert.IsTrue(found);
                Assert.AreEqual(20f, PolylineLength(waypoints), 1f);
                for (int waypointIndex = 0; waypointIndex < waypoints.Length; waypointIndex++)
                {
                    Assert.LessOrEqual(math.abs(waypoints[waypointIndex].z), 3f);
                }
            }
            finally
            {
                waypoints.Dispose();
            }
        }

        [Test]
        public void Route_WithAnEndFartherThanTheSnapDistance_ReturnsFalseAndLeavesTheListUntouched()
        {
            NativeList<float3> waypoints = new NativeList<float3>(Allocator.Temp);
            try
            {
                float3 sentinel = new float3(7f, 8f, 9f);
                waypoints.Add(sentinel);

                bool found = RoadGraphQuery.TryFindRoute(ref junctionBlob.Value, new float3(-40f, 0f, 0f), new float3(0f, 0f, 500f), ref waypoints);

                Assert.IsFalse(found);
                Assert.AreEqual(1, waypoints.Length);
                Assert.IsTrue(waypoints[0].Equals(sentinel));
            }
            finally
            {
                waypoints.Dispose();
            }
        }

        [Test]
        public void Route_BetweenDisconnectedRoads_ReturnsFalse()
        {
            NativeList<float3> waypoints = new NativeList<float3>(Allocator.Temp);
            try
            {
                bool found = RoadGraphQuery.TryFindRoute(ref junctionBlob.Value, new float3(-40f, 0f, 0f), new float3(230f, 0f, 0f), ref waypoints);

                Assert.IsFalse(found);
            }
            finally
            {
                waypoints.Dispose();
            }
        }

        [Test]
        public void Route_OnAnEmptyGraph_ReturnsFalse()
        {
            NativeList<float3> waypoints = new NativeList<float3>(Allocator.Temp);
            try
            {
                bool found = RoadGraphQuery.TryFindRoute(ref emptyBlob.Value, new float3(-40f, 0f, 0f), new float3(40f, 0f, 0f), ref waypoints);

                Assert.IsFalse(found);
            }
            finally
            {
                waypoints.Dispose();
            }
        }

        private static float PolylineLength(NativeList<float3> waypoints)
        {
            float totalLength = 0f;
            for (int waypointIndex = 1; waypointIndex < waypoints.Length; waypointIndex++)
            {
                totalLength += math.distance(waypoints[waypointIndex - 1], waypoints[waypointIndex]);
            }
            return totalLength;
        }

        private static Vector3[] SampleLine(Vector3 start, Vector3 end)
        {
            int segmentCount = Mathf.CeilToInt(Vector3.Distance(start, end));
            Vector3[] samples = new Vector3[segmentCount + 1];
            for (int sampleIndex = 0; sampleIndex <= segmentCount; sampleIndex++)
            {
                samples[sampleIndex] = Vector3.Lerp(start, end, (float)sampleIndex / segmentCount);
            }
            return samples;
        }

        // Mirrors TerrainRoadGraphAuthoring.Baker.BuildBlob, which is private to the baker.
        private static BlobAssetReference<RoadGraphBlob> CreateBlob(RoadGraphData graphData)
        {
            int nodeCount = graphData.nodePositions != null ? graphData.nodePositions.Count : 0;
            int edgeCount = graphData.edges != null ? graphData.edges.Count : 0;

            List<int>[] edgesPerNode = new List<int>[nodeCount];
            for (int nodeIndex = 0; nodeIndex < nodeCount; nodeIndex++)
            {
                edgesPerNode[nodeIndex] = new List<int>();
            }

            int totalPointCount = 0;
            int totalAdjacencyCount = 0;
            for (int edgeIndex = 0; edgeIndex < edgeCount; edgeIndex++)
            {
                RoadEdgeData edgeData = graphData.edges[edgeIndex];
                totalPointCount += edgeData.points.Length;
                edgesPerNode[edgeData.fromNode].Add(edgeIndex);
                totalAdjacencyCount++;
                if (edgeData.toNode != edgeData.fromNode)
                {
                    edgesPerNode[edgeData.toNode].Add(edgeIndex);
                    totalAdjacencyCount++;
                }
            }

            BlobBuilder builder = new BlobBuilder(Allocator.Temp);
            ref RoadGraphBlob root = ref builder.ConstructRoot<RoadGraphBlob>();
            BlobBuilderArray<RoadNode> nodes = builder.Allocate(ref root.nodes, nodeCount);
            BlobBuilderArray<RoadEdge> edges = builder.Allocate(ref root.edges, edgeCount);
            BlobBuilderArray<int> nodeEdgeIndices = builder.Allocate(ref root.nodeEdgeIndices, totalAdjacencyCount);
            BlobBuilderArray<float3> polylinePoints = builder.Allocate(ref root.polylinePoints, totalPointCount);

            int adjacencyCursor = 0;
            for (int nodeIndex = 0; nodeIndex < nodeCount; nodeIndex++)
            {
                List<int> touchingEdges = edgesPerNode[nodeIndex];
                nodes[nodeIndex] = new RoadNode
                {
                    position = graphData.nodePositions[nodeIndex],
                    firstEdgeIndex = adjacencyCursor,
                    edgeCount = touchingEdges.Count
                };
                for (int touchingIndex = 0; touchingIndex < touchingEdges.Count; touchingIndex++)
                {
                    nodeEdgeIndices[adjacencyCursor] = touchingEdges[touchingIndex];
                    adjacencyCursor++;
                }
            }

            int pointCursor = 0;
            for (int edgeIndex = 0; edgeIndex < edgeCount; edgeIndex++)
            {
                RoadEdgeData edgeData = graphData.edges[edgeIndex];
                edges[edgeIndex] = new RoadEdge
                {
                    fromNode = edgeData.fromNode,
                    toNode = edgeData.toNode,
                    firstPointIndex = pointCursor,
                    pointCount = edgeData.points.Length,
                    lengthMetres = edgeData.lengthMetres
                };
                for (int pointIndex = 0; pointIndex < edgeData.points.Length; pointIndex++)
                {
                    polylinePoints[pointCursor] = edgeData.points[pointIndex];
                    pointCursor++;
                }
            }

            BlobAssetReference<RoadGraphBlob> blob = builder.CreateBlobAssetReference<RoadGraphBlob>(Allocator.Persistent);
            builder.Dispose();
            return blob;
        }
    }
}
