// Assets/_Scripts/Units/AI/PathFinding/DStarPlanner.cs
using System;
using System.Collections.Generic;
using UnityEngine;
using Pathfinding; // for DStarLite<T> and Node<T>

/// <summary>
/// Thin adapter that maps your NavMeshGraph (poly-centers + adjacency)
/// into a D* Lite problem. Uses node INDEX (int) as D* node payload to keep
/// mapping simple and fast.
/// 
/// Requirements on NavMeshGraph (Option A helpers):
/// - int FindClosestNodeIndex(Vector3 worldPos)
/// - IEnumerable<int> GetNeighbors(int nodeIndex)
/// - Vector3 GetNodePosition(int nodeIndex)
/// </summary>
public sealed class DStarPlanner
{
    // Public surface
    public NavMeshGraph Graph { get; }
    public int StartIndex { get; private set; }
    public int GoalIndex  { get; private set; }

    // D* core
    private readonly List<Node<int>> dstarNodes = new();
    private readonly Dictionary<int, Node<int>> idxToDNode = new();
    private DStarLite<int> core;

    public DStarPlanner(NavMeshGraph graph, int startIndex, int goalIndex)
    {
        if (graph == null) throw new ArgumentNullException(nameof(graph));
        Graph = graph;

        // Basic sanity
        if (startIndex < 0 || goalIndex < 0)
            throw new ArgumentException("Start/Goal indices must be valid non-negative indices.");

        StartIndex = startIndex;
        GoalIndex  = goalIndex;

        BuildDStarNodes();
        core = new DStarLite<int>(
            start: idxToDNode[StartIndex],
            goal : idxToDNode[GoalIndex],
            allNodes: dstarNodes
        );
        core.Initialize();
        core.ComputeShortestPath();
    }

    /// <summary>
    /// Re-seed start/goal (e.g., if your agent moved to another polygon)
    /// and recompute policy.
    /// </summary>
    public void ResetStartGoal(int newStartIndex, int newGoalIndex)
    {
        if (!idxToDNode.ContainsKey(newStartIndex) || !idxToDNode.ContainsKey(newGoalIndex))
            throw new ArgumentException("Start/Goal index out of range of the current graph.");

        StartIndex = newStartIndex;
        GoalIndex  = newGoalIndex;

        core = new DStarLite<int>(
            start: idxToDNode[StartIndex],
            goal : idxToDNode[GoalIndex],
            allNodes: dstarNodes
        );
        core.Initialize();
        core.ComputeShortestPath();
    }

    /// <summary>
    /// If an edge cost/topology changed around 'changedIndex', notify D* Lite to update incrementally.
    /// Call this after changing walkability or adding/removing links in your NavMeshGraph-local representation.
    /// </summary>
    public void NotifyLocalChange(int changedIndex)
    {
        if (!idxToDNode.TryGetValue(changedIndex, out var node)) return;
        core.RecalculateNode(node);
    }

    /// <summary>
    /// Return the neighbor of 'fromIndex' that follows the current D* policy best
    /// (minimized cost(from, nb) + G(nb)). If no neighbors or unreachable, returns fromIndex.
    /// </summary>
    public int NextTowardGoal(int fromIndex)
    {
        if (!idxToDNode.TryGetValue(fromIndex, out var fromDNode))
            return fromIndex;

        float bestScore = float.PositiveInfinity;
        int   bestNb    = fromIndex;

        Vector3 fromPos = Graph.GetNodePosition(fromIndex);

        foreach (int nb in Graph.GetNeighbors(fromIndex))
        {
            if (!idxToDNode.TryGetValue(nb, out var nbDNode)) continue;

            Vector3 nbPos = Graph.GetNodePosition(nb);
            float stepCost = Vector2.Distance(new Vector2(fromPos.x, fromPos.z), new Vector2(nbPos.x, nbPos.z));
            float score = stepCost + nbDNode.G; // classic one-step lookahead using labels

            if (score < bestScore)
            {
                bestScore = score;
                bestNb = nb;
            }
        }

        return bestNb;
    }

    /// <summary>
    /// Convenience: build a list of world positions (poly centers) from current Start to Goal
    /// by repeatedly taking NextTowardGoal. Safe-capped to avoid infinite loops.
    /// </summary>
    public List<Vector3> BuildWorldPath(int maxHops = 4096)
    {
        var path = new List<Vector3>();
        if (!idxToDNode.ContainsKey(StartIndex) || !idxToDNode.ContainsKey(GoalIndex))
            return path;

        int cur = StartIndex;
        path.Add(Graph.GetNodePosition(cur));

        int hops = 0;
        while (cur != GoalIndex && hops++ < maxHops)
        {
            int next = NextTowardGoal(cur);
            if (next == cur) break; // stuck/unreachable
            cur = next;
            path.Add(Graph.GetNodePosition(cur));
        }

        return path;
    }

    // ---------- Internals ----------

    private void BuildDStarNodes()
    {
        dstarNodes.Clear();
        idxToDNode.Clear();

        int nodeCount = GraphNodeCount();
        if (nodeCount <= 0) return;

        // 1) Create D* nodes for each graph node index
        for (int i = 0; i < nodeCount; i++)
        {
            var dnode = new Node<int>(
                data: i,
                cost: (a, b) => CostIndex(a.Data, b.Data),
                heuristic: (a, b) => HeuristicIndex(a.Data, b.Data)
            );
            dstarNodes.Add(dnode);
            idxToDNode[i] = dnode;
        }

        // 2) Wire neighbors
        for (int i = 0; i < nodeCount; i++)
        {
            var dn = idxToDNode[i];
            var list = new List<Node<int>>();
            foreach (int nb in Graph.GetNeighbors(i))
            {
                if (idxToDNode.TryGetValue(nb, out var nbD))
                    list.Add(nbD);
            }
            dn.Neighbors = list;
        }
    }

    private int GraphNodeCount()
    {
        // If you have Graph.Nodes.Count, use that; otherwise infer via a helper count.
        // Here we probe by scanning neighbors until GetNodePosition throws. Prefer exposing Nodes.Count.
        // Better: add `public int NodeCount => Nodes.Count;` to NavMeshGraph.
        if (Graph is null) return 0;

        // Assume you've added a property on your graph:
        // public int NodeCount { get; }
        var nodeCountProp = Graph.GetType().GetProperty("NodeCount");
        if (nodeCountProp != null)
        {
            return (int)nodeCountProp.GetValue(Graph);
        }

        // Fallback: try a commonly used property `Nodes` (List-like)
        var nodesProp = Graph.GetType().GetProperty("Nodes");
        if (nodesProp != null)
        {
            var nodesObj = nodesProp.GetValue(Graph) as System.Collections.ICollection;
            if (nodesObj != null) return nodesObj.Count;
        }

        // If neither exists, you should add one. We cannot reliably infer here.
        Debug.LogError("NavMeshGraph should expose NodeCount or Nodes.Count. Please add one of them.");
        return 0;
    }

    private float CostIndex(int aIdx, int bIdx)
    {
        var A = Graph.GetNodePosition(aIdx);
        var B = Graph.GetNodePosition(bIdx);
        return Vector2.Distance(new Vector2(A.x, A.z), new Vector2(B.x, B.z));
    }

    private float HeuristicIndex(int aIdx, int bIdx)
    {
        // Straight-line in XZ
        return CostIndex(aIdx, bIdx);
    }
}
