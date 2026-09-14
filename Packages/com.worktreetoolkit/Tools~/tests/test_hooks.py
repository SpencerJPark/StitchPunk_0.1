from __future__ import annotations

import json
import unittest

from tests.repo_fixture import TemporaryRepository
from worktree_toolkit import git_runner, hooks
from worktree_toolkit.state_store import load_state


class HooksTests(unittest.TestCase):
    def setUp(self):
        self.repository = TemporaryRepository()

    def tearDown(self):
        self.repository.cleanup()

    def _create_payload_text(self):
        return json.dumps(
            {
                "session_id": "session-1",
                "transcript_path": "/tmp/transcript.jsonl",
                "cwd": self.repository.stage_path,
                "prompt_id": "prompt-1",
                "hook_event_name": "WorktreeCreate",
                "name": "agent-abc123",
            }
        )

    def test_hook_create_returns_path_of_a_linked_worktree_on_the_expected_branch(self):
        created_path = hooks.hook_create(self._create_payload_text())

        self.assertTrue(git_runner.is_linked_worktree(created_path))
        self.assertEqual(git_runner.current_branch(created_path), "worktree-agent-abc123")

    def test_claim_worktree_renames_branch_and_rekeys_state_under_the_spec_id(self):
        created_path = hooks.hook_create(self._create_payload_text())

        claimed_entry_dictionary = hooks.claim_worktree(
            created_path,
            spec_id="spec-1",
            spec_path="Assets/_Vault/Tasks/Plans/Spec1_System.md",
            lead_model="claude-opus",
            worker_model="claude-sonnet",
        )

        self.assertEqual(git_runner.current_branch(created_path), "spec/spec-1")
        self.assertEqual(claimed_entry_dictionary["id"], "spec-1")
        self.assertEqual(claimed_entry_dictionary["branch"], "spec/spec-1")
        self.assertEqual(claimed_entry_dictionary["lead"]["status"], "building")

        common_git_directory_path = git_runner.common_git_directory(self.repository.stage_path)
        state_after_claim = load_state(common_git_directory_path)
        self.assertIn("spec-1", state_after_claim.worktrees)
        self.assertNotIn("agent-abc123", state_after_claim.worktrees)


if __name__ == "__main__":
    unittest.main()
