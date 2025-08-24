// Assets/_Scripts/Navigation/NavMeshGraph.cs
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace PathFinding
{
    /// <summary>
    /// Compact graph built from the scene NavMesh. Nodes are triangle centroids;
    /// edges connect triangles that share an edge. Built once per scene load.
    /// </summary>
    [Serializable]
    public sealed class NavMeshGraph
    {
        [Serializable]
        public struct Node
        {
            /// <summary>Triangle centroid (Y preserved from navmesh).</summary>
            public Vector3 position;
            /// <summary>Index into edges array where this node's edges start.</summary>
            public int startEdge;
            /// <summary>Number of outgoing edges for this node (≤ 3 for a triangle mesh).</summary>
            public short edgeCount;
        }

        [Serializable]
        public struct Edge
        {
            /// <summary>Index of neighbor node.</summary>
            public int to;
            /// <summary>Planar (XZ) cost between triangle centroids.</summary>
            public float cost;
        }

        [SerializeField] private Node[] _nodes = Array.Empty<Node>();
        [SerializeField] private Edge[] _edges = Array.Empty<Edge>();
        [SerializeField] private int _version = 0;

        /// <summary>Read-only access to nodes.</summary>
        public IReadOnlyList<Node> Nodes => _nodes;
        /// <summary>Read-only access to edges.</summary>
        public IReadOnlyList<Edge> Edges => _edges;
        /// <summary>Monotonic build counter.</summary>
        public int Version => _version;
        /// <summary>Total node count (fast for planners).</summary>
        public int NodeCount => _nodes?.Length ?? 0;

        /// <summary>
        /// Builds the graph from the current scene NavMesh triangulation.
        /// Call this at scene load (after NavMesh is ready).
        /// </summary>
        public void BuildFromNavMesh()
        {
            var triangulation = NavMesh.CalculateTriangulation();
            var verts = triangulation.vertices;
            var indices = triangulation.indices;
            int triCount = indices.Length / 3;

            if (triCount <= 0)
            {
                _nodes = Array.Empty<Node>();
                _edges = Array.Empty<Edge>();
                _version++;
                return;
            }

            // 1) Compute triangle centroids (node positions)
            var centers = new Vector3[triCount];
            for (int t = 0; t < triCount; t++)
            {
                int i0 = indices[t * 3 + 0];
                int i1 = indices[t * 3 + 1];
                int i2 = indices[t * 3 + 2];
                Vector3 a = verts[i0];
                Vector3 b = verts[i1];
                Vector3 c = verts[i2];
                centers[t] = (a + b + c) / 3f; // preserve Y from NavMesh
            }

            // 2) Build adjacency via undirected edge map (min,max vertex index)
            var edgeMap = new Dictionary<(int, int), int>(triCount * 3);
            var neighborLists = new List<int>[triCount];
            for (int t = 0; t < triCount; t++) neighborLists[t] = new List<int>(3);

            for (int t = 0; t < triCount; t++)
            {
                int va = indices[t * 3 + 0];
                int vb = indices[t * 3 + 1];
                int vc = indices[t * 3 + 2];

                Connect(va, vb, t);
                Connect(vb, vc, t);
                Connect(vc, va, t);
            }

            void Connect(int v1, int v2, int triIdx)
            {
                int lo = v1 < v2 ? v1 : v2;
                int hi = v1 < v2 ? v2 : v1;
                var key = (lo, hi);

                if (edgeMap.TryGetValue(key, out int otherTri))
                {
                    if (otherTri != triIdx)
                    {
                        neighborLists[triIdx].Add(otherTri);
                        neighborLists[otherTri].Add(triIdx);
                    }
                }
                else
                {
                    edgeMap[key] = triIdx;
                }
            }

            // 3) Flatten adjacency to Node/Edge arrays
            var nodes = new Node[triCount];

            int totalEdges = 0;
            for (int t = 0; t < triCount; t++) totalEdges += neighborLists[t].Count;

            var edges = new Edge[totalEdges];
            int edgeWrite = 0;

            for (int t = 0; t < triCount; t++)
            {
                int startEdgeIndex = edgeWrite;

                foreach (int nb in neighborLists[t])
                {
                    float cost = Vector2.Distance(
                        new Vector2(centers[t].x, centers[t].z),
                        new Vector2(centers[nb].x, centers[nb].z)
                    );
                    edges[edgeWrite++] = new Edge { to = nb, cost = cost };
                }

                nodes[t] = new Node
                {
                    position = centers[t],
                    startEdge = startEdgeIndex,
                    edgeCount = (short)neighborLists[t].Count
                };
            }

            _nodes = nodes;
            _edges = edges;
            _version++;
        }

        // ----------------------
        // Helper API for planners
        // ----------------------

        /// <summary>Get world position of a node (triangle centroid).</summary>
        public Vector3 GetNodePosition(int nodeIndex)
        {
            if (_nodes == null || nodeIndex < 0 || nodeIndex >= _nodes.Length)
                throw new IndexOutOfRangeException($"GetNodePosition: index {nodeIndex} out of range.");
            return _nodes[nodeIndex].position;
        }

        /// <summary>Enumerate neighbor node indices for a given node.</summary>
        public IEnumerable<int> GetNeighbors(int nodeIndex)
        {
            if (_nodes == null || nodeIndex < 0 || nodeIndex >= _nodes.Length)
                yield break;

            var n = _nodes[nodeIndex];
            int start = n.startEdge;
            int count = n.edgeCount;

            for (int i = 0; i < count; i++)
                yield return _edges[start + i].to;
        }

        /// <summary>
        /// Linear search for closest node by planar (XZ) distance.
        /// Use at agent/goal placement time. O(N) but robust.
        /// </summary>
        public int FindClosestNodeIndex(Vector3 worldPos)
        {
            if (_nodes == null || _nodes.Length == 0) return -1;

            Vector2 p = new Vector2(worldPos.x, worldPos.z);
            float bestDist2 = float.PositiveInfinity;
            int bestIdx = -1;

            for (int i = 0; i < _nodes.Length; i++)
            {
                Vector3 c = _nodes[i].position;
                float dx = p.x - c.x;
                float dz = p.y - c.z;
                float d2 = dx * dx + dz * dz;
                if (d2 < bestDist2)
                {
                    bestDist2 = d2;
                    bestIdx = i;
                }
            }
            return bestIdx;
        }

        /// <summary>
        /// Try to find the closest node within a maximum planar radius.
        /// Returns false if none are within <paramref name="maxXZDistance"/>.
        /// </summary>
        public bool TryFindClosestNodeIndex(Vector3 worldPos, float maxXZDistance, out int nodeIndex)
        {
            nodeIndex = -1;
            if (_nodes == null || _nodes.Length == 0) return false;

            float maxDist2 = maxXZDistance * maxXZDistance;
            Vector2 p = new Vector2(worldPos.x, worldPos.z);

            float bestDist2 = maxDist2;
            int bestIdx = -1;

            for (int i = 0; i < _nodes.Length; i++)
            {
                Vector3 c = _nodes[i].position;
                float dx = p.x - c.x;
                float dz = p.y - c.z;
                float d2 = dx * dx + dz * dz;
                if (d2 <= bestDist2)
                {
                    bestDist2 = d2;
                    bestIdx = i;
                }
            }

            if (bestIdx >= 0)
            {
                nodeIndex = bestIdx;
                return true;
            }
            return false;
        }
    }
}