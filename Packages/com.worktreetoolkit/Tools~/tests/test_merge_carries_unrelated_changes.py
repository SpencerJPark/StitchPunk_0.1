from __future__ import annotations

import os
import unittest

from tests.repo_fixture import TemporaryRepository
from worktree_toolkit import git_runner, merge_ops, state_store
from worktree_toolkit.errors import RefusedError


class MergeCarriesUnrelatedChangesTests(unittest.TestCase):
    """Found by the 2026-09-14 C2 dry run: merge refused on the owner's always-dirty stage even though the branch never touched those files."""

    def setUp(self):
        self.repository = TemporaryRepository()
        self.repository.commit_file(self.repository.stage_path, "shared.txt", "base\n", "add shared")
        self.worktree_path = self.repository.add_raw_worktree("gamma")
        self.repository.commit_file(self.worktree_path, "shared.txt", "branch change\n", "gamma changes shared")
        common_git_directory = git_runner.common_git_directory(self.repository.stage_path)
        with state_store.StateLock(common_git_directory):
            state = state_store.load_state(common_git_directory)
            state.worktrees["gamma"] = state_store.WorktreeEntry(
                "gamma", git_runner.normalize_path(self.worktree_path), "spec/gamma")
            state_store.save_state(common_git_directory, state)

    def tearDown(self):
        self.repository.cleanup()

    def test_merge_refuses_only_when_an_uncommitted_edit_touches_a_file_the_branch_changes(self):
        stage_shared_path = self.repository.write_file(self.repository.stage_path, "shared.txt", "owner edit\n")
        main_before = self.repository.git(self.repository.stage_path, "rev-parse", "main")
        with self.assertRaises(RefusedError):
            merge_ops.merge_worktree(self.repository.stage_path, "gamma")
        self.assertEqual(main_before, self.repository.git(self.repository.stage_path, "rev-parse", "main"))

        self.repository.git(self.repository.stage_path, "checkout", "--", "shared.txt")
        self.repository.write_file(self.repository.stage_path, "readme.txt", "owner edit to an unrelated file\n")
        result = merge_ops.merge_worktree(self.repository.stage_path, "gamma")

        self.assertEqual("gamma", result["merged"])
        with open(stage_shared_path, "r", encoding="utf-8") as shared_file:
            self.assertEqual("branch change\n", shared_file.read())
        with open(os.path.join(self.repository.stage_path, "readme.txt"), "r", encoding="utf-8") as readme_file:
            self.assertEqual("owner edit to an unrelated file\n", readme_file.read())


if __name__ == "__main__":
    unittest.main()
