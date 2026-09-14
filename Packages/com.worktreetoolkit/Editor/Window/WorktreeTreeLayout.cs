using System;
using System.Collections.Generic;

namespace WorktreeToolkit.Editor
{
    public sealed class WorktreeLayoutNode
    {
        public string nodeId;
        public string parentNodeId;
        public int column;
        public int row;
        public bool isTrunk;
    }

    public static class WorktreeTreeLayout
    {
        public const string TrunkNodeId = "__trunk__";

        // Column = parent column + 1, resolved by matching parentBranch against another worktree's
        // branch (ordinal, non-empty). Rows come from a depth-first walk from the trunk so every node
        // gets a unique row in visit order; a cycle is broken by re-parenting the later-visited node
        // (the one that would revisit an ancestor still on the walk stack) onto the trunk.
        public static List<WorktreeLayoutNode> Compute(WorktreeGraphDto graph)
        {
            List<WorktreeLayoutNode> resultNodes = new List<WorktreeLayoutNode>();

            WorktreeLayoutNode trunkNode = new WorktreeLayoutNode
            {
                nodeId = TrunkNodeId,
                parentNodeId = null,
                column = 0,
                row = 0,
                isTrunk = true,
            };
            resultNodes.Add(trunkNode);

            if (graph == null || graph.worktrees == null || graph.worktrees.Length == 0)
            {
                return resultNodes;
            }

            Dictionary<string, WorktreeNodeDto> nodeByBranch = new Dictionary<string, WorktreeNodeDto>(StringComparer.Ordinal);
            foreach (WorktreeNodeDto worktreeNode in graph.worktrees)
            {
                if (worktreeNode == null || string.IsNullOrEmpty(worktreeNode.branch))
                {
                    continue;
                }

                // A duplicate branch name is not specified; first writer wins so lookups stay deterministic.
                if (!nodeByBranch.ContainsKey(worktreeNode.branch))
                {
                    nodeByBranch.Add(worktreeNode.branch, worktreeNode);
                }
            }

            // parentIdByChildId resolves each worktree's parent node id (trunk or another worktree's id)
            // ignoring cycles, computed up front so the depth-first walk below never has to re-derive it.
            Dictionary<string, string> parentNodeIdByWorktreeId = new Dictionary<string, string>(StringComparer.Ordinal);
            Dictionary<string, List<string>> childWorktreeIdsByParentNodeId = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            childWorktreeIdsByParentNodeId.Add(TrunkNodeId, new List<string>());

            foreach (WorktreeNodeDto worktreeNode in graph.worktrees)
            {
                if (worktreeNode == null || string.IsNullOrEmpty(worktreeNode.id))
                {
                    continue;
                }

                childWorktreeIdsByParentNodeId.TryAdd(worktreeNode.id, new List<string>());
            }

            foreach (WorktreeNodeDto worktreeNode in graph.worktrees)
            {
                if (worktreeNode == null || string.IsNullOrEmpty(worktreeNode.id))
                {
                    continue;
                }

                string parentNodeId = TrunkNodeId;
                if (!string.IsNullOrEmpty(worktreeNode.parentBranch)
                    && nodeByBranch.TryGetValue(worktreeNode.parentBranch, out WorktreeNodeDto parentWorktreeNode)
                    && !string.Equals(parentWorktreeNode.id, worktreeNode.id, StringComparison.Ordinal))
                {
                    parentNodeId = parentWorktreeNode.id;
                }

                parentNodeIdByWorktreeId[worktreeNode.id] = parentNodeId;
                childWorktreeIdsByParentNodeId[parentNodeId].Add(worktreeNode.id);
            }

            foreach (List<string> childWorktreeIds in childWorktreeIdsByParentNodeId.Values)
            {
                childWorktreeIds.Sort(StringComparer.Ordinal);
            }

            Dictionary<string, WorktreeLayoutNode> layoutNodeById = new Dictionary<string, WorktreeLayoutNode>(StringComparer.Ordinal)
            {
                { TrunkNodeId, trunkNode },
            };

            int nextRow = 1;
            HashSet<string> visitedWorktreeIds = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> onWalkStackWorktreeIds = new HashSet<string>(StringComparer.Ordinal);
            Stack<string> pendingWorktreeIds = new Stack<string>();

            // Push in reverse sorted order so a stack-based walk still visits children in ascending id order.
            List<string> trunkChildIds = childWorktreeIdsByParentNodeId[TrunkNodeId];
            for (int childIndex = trunkChildIds.Count - 1; childIndex >= 0; childIndex--)
            {
                pendingWorktreeIds.Push(trunkChildIds[childIndex]);
            }

            while (pendingWorktreeIds.Count > 0)
            {
                string currentWorktreeId = pendingWorktreeIds.Pop();

                if (visitedWorktreeIds.Contains(currentWorktreeId))
                {
                    continue;
                }

                visitedWorktreeIds.Add(currentWorktreeId);

                string resolvedParentNodeId = parentNodeIdByWorktreeId[currentWorktreeId];
                // If the resolved parent is a worktree still awaiting layout (a cycle: it depends on
                // this node too), break the cycle by re-parenting this node onto the trunk instead.
                if (resolvedParentNodeId != TrunkNodeId && !layoutNodeById.ContainsKey(resolvedParentNodeId))
                {
                    resolvedParentNodeId = TrunkNodeId;
                }

                WorktreeLayoutNode parentLayoutNode = layoutNodeById[resolvedParentNodeId];
                WorktreeLayoutNode currentLayoutNode = new WorktreeLayoutNode
                {
                    nodeId = currentWorktreeId,
                    parentNodeId = resolvedParentNodeId,
                    column = parentLayoutNode.column + 1,
                    row = nextRow,
                    isTrunk = false,
                };
                nextRow++;

                layoutNodeById.Add(currentWorktreeId, currentLayoutNode);
                resultNodes.Add(currentLayoutNode);

                if (childWorktreeIdsByParentNodeId.TryGetValue(currentWorktreeId, out List<string> currentChildIds))
                {
                    for (int childIndex = currentChildIds.Count - 1; childIndex >= 0; childIndex--)
                    {
                        if (!visitedWorktreeIds.Contains(currentChildIds[childIndex]))
                        {
                            pendingWorktreeIds.Push(currentChildIds[childIndex]);
                        }
                    }
                }
            }

            // Any worktree never reached by the walk (pure cycle with no trunk-reachable entry point)
            // still needs a layout node; re-parent it onto the trunk and append it deterministically.
            List<string> strandedWorktreeIds = new List<string>();
            foreach (WorktreeNodeDto worktreeNode in graph.worktrees)
            {
                if (worktreeNode == null || string.IsNullOrEmpty(worktreeNode.id))
                {
                    continue;
                }

                if (!visitedWorktreeIds.Contains(worktreeNode.id))
                {
                    strandedWorktreeIds.Add(worktreeNode.id);
                }
            }

            strandedWorktreeIds.Sort(StringComparer.Ordinal);
            foreach (string strandedWorktreeId in strandedWorktreeIds)
            {
                WorktreeLayoutNode strandedLayoutNode = new WorktreeLayoutNode
                {
                    nodeId = strandedWorktreeId,
                    parentNodeId = TrunkNodeId,
                    column = trunkNode.column + 1,
                    row = nextRow,
                    isTrunk = false,
                };
                nextRow++;

                layoutNodeById.Add(strandedWorktreeId, strandedLayoutNode);
                resultNodes.Add(strandedLayoutNode);
            }

            return resultNodes;
        }
    }
}
