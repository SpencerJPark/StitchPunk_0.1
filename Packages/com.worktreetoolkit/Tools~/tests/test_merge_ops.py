from __future__ import annotations

import os
import unittest

from tests.repo_fixture import TemporaryRepository
from worktree_toolkit import git_runner, merge_ops, state_store
from worktree_toolkit.errors import RefusedError


class MergeOpsTests(unittest.TestCase):
    def setUp(self):
        self.repository = TemporaryRepository()
        self.common_git_directory = git_runner.common_git_directory(self.repository.stage_path)

    def tearDown(self):
        self.repository.cleanup()

    def _register_worktree(self, worktree_id: str, worktree_path: str, branch_name: str) -> None:
        with state_store.StateLock(self.common_git_directory):
            toolkit_state = state_store.load_state(self.common_git_directory)
            toolkit_state.worktrees[worktree_id] = state_store.WorktreeEntry(
                worktree_id=worktree_id,
                path=worktree_path,
                branch=branch_name,
                parent_branch="main",
                created_utc=state_store.utc_now_text(),
            )
            state_store.save_state(self.common_git_directory, toolkit_state)

    def test_merge_worktree_fast_forwards_trunk_with_both_sides_commits(self):
        worktree_path = self.repository.add_raw_worktree("alpha")
        self.repository.commit_file(worktree_path, "a.txt", "a\n", "add a")
        self.repository.commit_file(self.repository.stage_path, "b.txt", "b\n", "add b")
        self._register_worktree("alpha", worktree_path, "spec/alpha")

        merge_result = merge_ops.merge_worktree(self.repository.stage_path, "alpha")

        self.assertEqual(merge_result["merged"], "alpha")
        self.assertEqual(merge_result["branch"], "spec/alpha")
        self.assertEqual(merge_result["commitCount"], 1)
        self.assertTrue(os.path.exists(os.path.join(self.repository.stage_path, "a.txt")))
        self.assertTrue(os.path.exists(os.path.join(self.repository.stage_path, "b.txt")))
        merge_commits_on_main = self.repository.git(self.repository.stage_path, "rev-list", "--merges", "main")
        self.assertEqual(merge_commits_on_main, "")

    def test_merge_worktree_refuses_on_conflict_and_leaves_git_unchanged(self):
        worktree_path = self.repository.add_raw_worktree("beta")
        self.repository.commit_file(worktree_path, "readme.txt", "worktree change\n", "edit readme in worktree")
        self.repository.commit_file(self.repository.stage_path, "readme.txt", "stage change\n", "edit readme in stage")
        self._register_worktree("beta", worktree_path, "spec/beta")

        worktree_sha_before_merge = self.repository.git(worktree_path, "rev-parse", "HEAD")
        main_sha_before_merge = self.repository.git(self.repository.stage_path, "rev-parse", "HEAD")

        with self.assertRaises(RefusedError):
            merge_ops.merge_worktree(self.repository.stage_path, "beta")

        self.assertEqual(self.repository.git(worktree_path, "rev-parse", "HEAD"), worktree_sha_before_merge)
        self.assertEqual(self.repository.git(self.repository.stage_path, "rev-parse", "HEAD"), main_sha_before_merge)
        worktree_git_directory = git_runner.git_directory(worktree_path)
        self.assertFalse(os.path.exists(os.path.join(worktree_git_directory, "rebase-merge")))
        self.assertFalse(os.path.exists(os.path.join(worktree_git_directory, "rebase-apply")))


if __name__ == "__main__":
    unittest.main()
