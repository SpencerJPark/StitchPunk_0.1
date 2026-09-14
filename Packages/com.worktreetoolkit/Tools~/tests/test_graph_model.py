from __future__ import annotations

import unittest

from tests.repo_fixture import TemporaryRepository
from worktree_toolkit import git_runner, graph_model, state_store


class GraphModelTests(unittest.TestCase):
    def setUp(self):
        self.repository = TemporaryRepository()

    def tearDown(self):
        self.repository.cleanup()

    def test_registered_worktree_reports_parent_branch_ahead_and_behind(self):
        self.repository.git(self.repository.stage_path, "branch", "spec/a")
        worktree_path = self.repository.add_raw_worktree("spec-b", branch_name="spec/b", base_reference="spec/a")
        self.repository.commit_file(worktree_path, "change.txt", "content\n", "b work")
        common_git_directory = git_runner.common_git_directory(self.repository.stage_path)
        with state_store.StateLock(common_git_directory):
            state = state_store.load_state(common_git_directory)
            state.worktrees["spec-b"] = state_store.WorktreeEntry(
                worktree_id="spec-b", path=worktree_path, branch="spec/b", parent_branch="spec/a",
            )
            state_store.save_state(common_git_directory, state)

        graph = graph_model.build_graph(self.repository.stage_path)

        matching_entries = [entry for entry in graph["worktrees"] if entry["id"] == "spec-b"]
        self.assertEqual(len(matching_entries), 1)
        worktree_entry = matching_entries[0]
        self.assertEqual(worktree_entry["parentBranch"], "spec/a")
        self.assertEqual(worktree_entry["ahead"], 1)
        self.assertEqual(worktree_entry["behind"], 0)

    def test_unregistered_worktree_appears_and_noise_glob_filters_dirty_tracked(self):
        self.repository.add_raw_worktree("spec-c")
        common_git_directory = git_runner.common_git_directory(self.repository.stage_path)
        with state_store.StateLock(common_git_directory):
            state = state_store.load_state(common_git_directory)
            state.config.stage_noise_globs = ["*.log"]
            state_store.save_state(common_git_directory, state)
        self.repository.write_file(self.repository.stage_path, "notes.log", "noise\n")
        self.repository.git(self.repository.stage_path, "add", "notes.log")
        self.repository.write_file(self.repository.stage_path, "readme.txt", "changed\n")
        self.repository.git(self.repository.stage_path, "add", "readme.txt")

        graph = graph_model.build_graph(self.repository.stage_path)

        self.assertTrue(any(entry["registered"] is False for entry in graph["worktrees"]))
        self.assertEqual(graph["stage"]["dirtyTracked"], 1)


if __name__ == "__main__":
    unittest.main()
