from __future__ import annotations

import os
import unittest

from tests.repo_fixture import TemporaryRepository
from worktree_toolkit import git_runner, links, lifecycle_ops
from worktree_toolkit.state_store import StateLock, load_state, save_state


class LifecycleOpsRemoveWorktreeTests(unittest.TestCase):
    def setUp(self):
        self.repository = TemporaryRepository()

    def tearDown(self):
        self.repository.cleanup()

    def _register_entry(self, worktree_id, path, branch, parent_branch):
        common_git_directory = git_runner.common_git_directory(self.repository.stage_path)
        with StateLock(common_git_directory):
            state = load_state(common_git_directory)
            lifecycle_ops.register_worktree(state, worktree_id, path, branch, parent_branch)
            save_state(common_git_directory, state)

    def test_remove_worktree_unlinks_junction_before_git_removal_and_keeps_junction_target(self):
        target_directory = os.path.join(self.repository.root_directory, "target")
        os.makedirs(target_directory)
        precious_file_path = os.path.join(target_directory, "precious.txt")
        with open(precious_file_path, "w", encoding="utf-8") as precious_file:
            precious_file.write("keep")

        worktree_path = self.repository.add_raw_worktree("alpha")
        junction_path = os.path.join(worktree_path, "Nested", "Deep")
        os.makedirs(os.path.dirname(junction_path))
        links.create_directory_link(junction_path, target_directory)
        # An untracked file inside the worktree (not the junction target) makes the worktree dirty,
        # so the removal below needs force=True to pass the dirty-entry refusal.
        self.repository.write_file(worktree_path, "untracked.txt", "scratch")

        self._register_entry("alpha", worktree_path, branch="", parent_branch="main")

        result = lifecycle_ops.remove_worktree(
            self.repository.stage_path, "alpha", keep_branch=True, force=True
        )

        self.assertFalse(os.path.exists(worktree_path))
        self.assertTrue(os.path.exists(precious_file_path))
        self.assertEqual(
            [os.path.normcase(os.path.abspath(junction_path))],
            [os.path.normcase(os.path.abspath(path)) for path in result["unlinked"]],
        )

        common_git_directory = git_runner.common_git_directory(self.repository.stage_path)
        state_after_removal = load_state(common_git_directory)
        self.assertNotIn("alpha", state_after_removal.worktrees)

    def test_remove_worktree_keeps_unmerged_branch(self):
        worktree_path = self.repository.add_raw_worktree("beta", branch_name="beta-branch")
        self.repository.commit_file(worktree_path, "feature.txt", "feature data", "add feature")

        self._register_entry("beta", worktree_path, branch="beta-branch", parent_branch="main")

        result = lifecycle_ops.remove_worktree(
            self.repository.stage_path, "beta", keep_branch=False, force=True
        )

        self.assertFalse(result["branchDeleted"])
        self.assertEqual("not merged into main", result["branchKeptReason"])
        self.assertTrue(git_runner.branch_exists(self.repository.stage_path, "beta-branch"))


if __name__ == "__main__":
    unittest.main()
