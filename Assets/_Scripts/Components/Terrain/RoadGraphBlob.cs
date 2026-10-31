using Unity.Entities;
using Unity.Mathematics;

// firstEdgeIndex/edgeCount slice RoadGraphBlob.nodeEdgeIndices.
public struct RoadNode
{
    public float3 position;
    public int firstEdgeIndex;
    public int edgeCount;
}

// Points polylinePoints[firstPointIndex .. firstPointIndex + pointCount) run from fromNode to toNode.
public struct RoadEdge
{
    public int fromNode;
    public int toNode;
    public int firstPointIndex;
    public int pointCount;
    public float lengthMetres;
}

public struct RoadGraphBlob
{
    public BlobArray<RoadNode> nodes;
    public BlobArray<RoadEdge> edges;
    // Per node, the indices of the edges touching it, grouped by node: the adjacency the route search walks.
    public BlobArray<int> nodeEdgeIndices;
    public BlobArray<float3> polylinePoints;
}

public struct RoadGraphSingleton : IComponentData
{
    public BlobAssetReference<RoadGraphBlob> graph;
}
