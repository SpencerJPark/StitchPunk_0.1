from __future__ import annotations

import os
import time
import unittest

from tests.repo_fixture import TemporaryRepository
from worktree_toolkit import gate_client, git_runner, state_store
from worktree_toolkit.errors import RefusedError


class GateRequiresClaimTests(unittest.TestCase):
    """Found by the 2026-09-14 C2 dry run: a haiku spec-lead gated a hook-made worktree before running claim."""

    def setUp(self):
        self.repository = TemporaryRepository()
        self.common_git_directory = git_runner.common_git_directory(self.repository.stage_path)
        self.worktree_path = self.repository.add_raw_worktree("agent-unclaimed", branch_name="worktree-agent-unclaimed")
        with state_store.StateLock(self.common_git_directory):
            state = state_store.load_state(self.common_git_directory)
            state.worktrees["agent-unclaimed"] = state_store.WorktreeEntry(
                "agent-unclaimed", git_runner.normalize_path(self.worktree_path), "worktree-agent-unclaimed",
                lead=state_store.LeadBinding(status="unclaimed"))
            state_store.save_state(self.common_git_directory, state)
        state_directory = state_store.state_directory(self.common_git_directory)
        with open(os.path.join(state_directory, gate_client.HEARTBEAT_FILE_NAME), "w", encoding="utf-8") as heartbeat_file:
            heartbeat_file.write("{}")
        os.utime(os.path.join(state_directory, gate_client.HEARTBEAT_FILE_NAME), (time.time(), time.time()))

    def tearDown(self):
        self.repository.cleanup()

    def test_enqueue_gate_refuses_a_worktree_whose_lead_never_claimed(self):
        with self.assertRaises(RefusedError):
            gate_client.enqueue_gate(self.worktree_path, [], [], 60.0)
        queue_directory = os.path.join(state_store.state_directory(self.common_git_directory), "queue")
        self.assertEqual([], [name for name in os.listdir(queue_directory) if name.endswith(".json")])


if __name__ == "__main__":
    unittest.main()
