using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

// One per persistent subscene; bakes every TerrainRoad in the authoring scene into the shared road graph blob.
public class TerrainRoadGraphAuthoring : MonoBehaviour
{
    private const float SampleSpacingMetres = 1f;

    public class Baker : Baker<TerrainRoadGraphAuthoring>
    {
        public override void Bake(TerrainRoadGraphAuthoring authoring)
        {
            TerrainRoad[] roads = Object.FindObjectsByType<TerrainRoad>(FindObjectsInactive.Exclude);
            System.Array.Sort(roads, (TerrainRoad left, TerrainRoad right) => string.CompareOrdinal(left.name, right.name));

            List<Vector3[]> roadPolylines = new List<Vector3[]>(roads.Length);
            foreach (TerrainRoad road in roads)
            {
                DependsOn(road);
                DependsOn(road.Container);
                Vector3[] polyline = SampleRoadWorldPolyline(road);
                if (polyline.Length >= 2)
                {
                    roadPolylines.Add(polyline);
                }
            }

            RoadGraphData graphData = TerrainRoadGraphBuilder.Build(roadPolylines);

            BlobAssetReference<RoadGraphBlob> blob = BuildBlob(graphData);
            AddBlobAsset(ref blob, out Unity.Entities.Hash128 blobHash);
            AddComponent(GetEntity(TransformUsageFlags.None), new RoadGraphSingleton { graph = blob });
        }

        private static Vector3[] SampleRoadWorldPolyline(TerrainRoad road)
        {
            SplineContainer container = road.Container;
            List<Vector3> samples = new List<Vector3>();
            for (int splineIndex = 0; splineIndex < container.Splines.Count; splineIndex++)
            {
                float splineLength = container.CalculateLength(splineIndex);
                int segmentCount = Mathf.Max(1, Mathf.CeilToInt(splineLength / SampleSpacingMetres));
                for (int sampleIndex = 0; sampleIndex <= segmentCount; sampleIndex++)
                {
                    float normalizedTime = (float)sampleIndex / segmentCount;
                    float3 localPosition = container.EvaluatePosition(splineIndex, normalizedTime);
                    samples.Add(road.transform.TransformPoint(localPosition));
                }
            }

            return samples.ToArray();
        }

        private static BlobAssetReference<RoadGraphBlob> BuildBlob(RoadGraphData graphData)
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
