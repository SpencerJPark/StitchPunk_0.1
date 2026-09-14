from __future__ import annotations

import os
import shutil
import unittest

from tests.repo_fixture import TemporaryRepository
from worktree_toolkit import git_runner, lifecycle_ops, state_store


def _extended(path: str) -> str:
    return "\\\\?\\" + os.path.abspath(path)


@unittest.skipUnless(os.name == "nt", "MAX_PATH failure is specific to git for Windows")
class RemoveLongPathTests(unittest.TestCase):
    """Found by the 2026-09-14 end-to-end probe: remove failed with 'Filename too long' and left an orphaned entry."""

    def setUp(self):
        self.repository = TemporaryRepository()
        lifecycle_ops.create_worktree(self.repository.stage_path, "longpath")
        self.worktree_path = self.repository.worktree_path("longpath")

    def tearDown(self):
        if os.path.exists(_extended(self.worktree_path)):
            shutil.rmtree(_extended(self.worktree_path), ignore_errors=True)
        git_runner.run_git(["worktree", "prune"], self.repository.stage_path, check=False)
        self.repository.cleanup()

    def test_remove_finishes_when_git_cannot_delete_a_path_over_max_path(self):
        deep_directory = self.worktree_path
        while len(deep_directory) < 300:
            deep_directory = os.path.join(deep_directory, "d" * 40)
        os.makedirs(_extended(deep_directory))
        with open(_extended(os.path.join(deep_directory, "leftover.txt")), "w", encoding="utf-8") as leftover_file:
            leftover_file.write("untracked")

        result = lifecycle_ops.remove_worktree(self.repository.stage_path, "longpath", force=True)

        self.assertEqual("longpath", result["removed"])
        self.assertFalse(os.path.exists(_extended(self.worktree_path)))
        common_directory = git_runner.common_git_directory(self.repository.stage_path)
        self.assertNotIn("longpath", state_store.load_state(common_directory).worktrees)


if __name__ == "__main__":
    unittest.main()
