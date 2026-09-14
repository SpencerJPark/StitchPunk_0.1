using System.Collections.Generic;
using NUnit.Framework;
using WorktreeToolkit.Editor;

namespace WorktreeToolkit.Tests.Editor
{
    [TestFixture]
    public sealed class WorktreeTreeLayoutTests
    {
        [Test]
        public void Compute_BranchOfABranch_SitsOneColumnRightOfItsParentWithUniqueRows()
        {
            WorktreeNodeDto parentWorktreeNode = new WorktreeNodeDto
            {
                id = "worktree-parent",
                branch = "feature/parent",
                parentBranch = "main",
            };
            WorktreeNodeDto childWorktreeNode = new WorktreeNodeDto
            {
                id = "worktree-child",
                branch = "feature/child",
                parentBranch = "feature/parent",
            };
            WorktreeGraphDto graph = new WorktreeGraphDto
            {
                trunk = "main",
                worktrees = new[] { parentWorktreeNode, childWorktreeNode },
            };

            List<WorktreeLayoutNode> layoutNodes = WorktreeTreeLayout.Compute(graph);

            WorktreeLayoutNode parentLayoutNode = layoutNodes.Find(node => node.nodeId == "worktree-parent");
            WorktreeLayoutNode childLayoutNode = layoutNodes.Find(node => node.nodeId == "worktree-child");

            Assert.AreEqual(parentLayoutNode.column + 1, childLayoutNode.column);

            HashSet<int> distinctRows = new HashSet<int>();
            foreach (WorktreeLayoutNode layoutNode in layoutNodes)
            {
                Assert.IsTrue(distinctRows.Add(layoutNode.row), $"Row {layoutNode.row} was reused by node {layoutNode.nodeId}.");
            }
        }

        [Test]
        public void Compute_UnknownParentBranch_FallsBackToTrunkAtColumnOne()
        {
            WorktreeNodeDto orphanWorktreeNode = new WorktreeNodeDto
            {
                id = "worktree-orphan",
                branch = "feature/orphan",
                parentBranch = "does-not-exist",
            };
            WorktreeGraphDto graph = new WorktreeGraphDto
            {
                trunk = "main",
                worktrees = new[] { orphanWorktreeNode },
            };

            List<WorktreeLayoutNode> layoutNodes = WorktreeTreeLayout.Compute(graph);
            WorktreeLayoutNode orphanLayoutNode = layoutNodes.Find(node => node.nodeId == "worktree-orphan");

            Assert.AreEqual(WorktreeTreeLayout.TrunkNodeId, orphanLayoutNode.parentNodeId);
            Assert.AreEqual(1, orphanLayoutNode.column);
        }
    }
}
