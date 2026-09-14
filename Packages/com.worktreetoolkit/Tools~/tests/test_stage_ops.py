from __future__ import annotations

import os
import unittest

from tests.repo_fixture import TemporaryRepository
from worktree_toolkit import git_runner, stage_ops
from worktree_toolkit.errors import RefusedError
from worktree_toolkit.state_store import StateLock, WorktreeEntry, load_state, save_state, utc_now_text


def _register_worktree(repository: TemporaryRepository, worktree_id: str, worktree_path: str, branch: str) -> None:
    common_git_directory = git_runner.common_git_directory(repository.stage_path)
    with StateLock(common_git_directory):
        state = load_state(common_git_directory)
        state.worktrees[worktree_id] = WorktreeEntry(
            worktree_id=worktree_id, path=worktree_path, branch=branch, created_utc=utc_now_text()
        )
        save_state(common_git_directory, state)


class StageOpsTests(unittest.TestCase):
    def setUp(self):
        self.repository = TemporaryRepository()
        self.common_git_directory = git_runner.common_git_directory(self.repository.stage_path)

    def tearDown(self):
        self.repository.cleanup()

    def test_review_worktree_then_return_stage_round_trip(self):
        worktree_path = self.repository.add_raw_worktree("alpha")
        _register_worktree(self.repository, "alpha", worktree_path, "spec/alpha")

        review_result = stage_ops.review_worktree(self.repository.stage_path, "alpha")
        self.assertEqual(review_result, {"stageBranch": "spec/alpha", "parkedWorktreeId": "alpha"})

        # A commit made on the stage while it is parked on spec/alpha must reach the worktree on return.
        self.repository.commit_file(self.repository.stage_path, "review-edit.txt", "edited\n", "review edit")

        return_result = stage_ops.return_stage(self.repository.stage_path)
        self.assertEqual(return_result, {"stageBranch": "main", "reattachedWorktreeId": "alpha"})

        self.assertEqual(git_runner.current_branch(self.repository.stage_path), "main")
        self.assertEqual(git_runner.current_branch(worktree_path), "spec/alpha")

        worktree_log = self.repository.git(worktree_path, "log", "--oneline", "-1")
        self.assertIn("review edit", worktree_log)

        state = load_state(self.common_git_directory)
        self.assertFalse(state.worktrees["alpha"].parked)
        self.assertIsNone(state.stage_review_worktree_id)

    def test_review_worktree_only_refuses_when_the_uncommitted_path_is_also_changed_by_the_branch(self):
        # Both sides need shared.txt before the worktree branch diverges, so main can later diff against it.
        self.repository.commit_file(self.repository.stage_path, "shared.txt", "shared\n", "add shared.txt")

        worktree_path = self.repository.add_raw_worktree("beta")
        _register_worktree(self.repository, "beta", worktree_path, "spec/beta")
        self.repository.commit_file(worktree_path, "shared.txt", "changed on branch\n", "branch edits shared.txt")

        self.repository.write_file(self.repository.stage_path, "shared.txt", "dirty on stage\n")

        with self.assertRaises(RefusedError):
            stage_ops.review_worktree(self.repository.stage_path, "beta")

        self.assertEqual(git_runner.current_branch(self.repository.stage_path), "main")
        self.assertEqual(git_runner.current_branch(worktree_path), "spec/beta")

        # Revert the conflicting edit; touch readme.txt instead, which the branch never changed.
        self.repository.write_file(self.repository.stage_path, "shared.txt", "shared\n")
        self.repository.write_file(self.repository.stage_path, "readme.txt", "dirty\n")

        review_result = stage_ops.review_worktree(self.repository.stage_path, "beta")
        self.assertEqual(review_result, {"stageBranch": "spec/beta", "parkedWorktreeId": "beta"})

        readme_path = os.path.join(self.repository.stage_path, "readme.txt")
        with open(readme_path, "r", encoding="utf-8") as readme_file:
            self.assertEqual(readme_file.read(), "dirty\n")


if __name__ == "__main__":
    unittest.main()
