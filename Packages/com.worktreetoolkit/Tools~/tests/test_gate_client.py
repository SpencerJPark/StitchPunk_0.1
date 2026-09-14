from __future__ import annotations

import json
import os
import threading
import time
import unittest

from tests.repo_fixture import TemporaryRepository
from worktree_toolkit import gate_client, git_runner, state_store
from worktree_toolkit.errors import NoBrokerError


class _FakeBroker(threading.Thread):
    """Stands in for the (not-yet-built) Unity Editor broker: refreshes the heartbeat and answers the first queued request."""

    def __init__(self, common_git_directory: str, poll_seconds: float):
        super().__init__()
        self.common_git_directory = common_git_directory
        self.poll_seconds = poll_seconds
        self.stop_event = threading.Event()

    def run(self):
        heartbeat_path = os.path.join(state_store.state_directory(self.common_git_directory), gate_client.HEARTBEAT_FILE_NAME)
        queue_directory = os.path.join(state_store.state_directory(self.common_git_directory), "queue")
        results_directory = os.path.join(state_store.state_directory(self.common_git_directory), "results")
        while not self.stop_event.is_set():
            with open(heartbeat_path, "w", encoding="utf-8") as heartbeat_file:
                heartbeat_file.write("{}")
            queued_names = [name for name in os.listdir(queue_directory) if name.endswith(".json")]
            if queued_names:
                request_id = queued_names[0][: -len(".json")]
                result_path = os.path.join(results_directory, request_id + ".json")
                with open(result_path, "w", encoding="utf-8") as result_file:
                    json.dump({"requestId": request_id, "verdict": "pass"}, result_file)
                return
            time.sleep(self.poll_seconds)


class GateClientTests(unittest.TestCase):
    def setUp(self):
        self.repository = TemporaryRepository()
        self.common_git_directory = git_runner.common_git_directory(self.repository.stage_path)

    def tearDown(self):
        self.repository.cleanup()

    def _register_worktree(self, worktree_id: str, worktree_path: str, lead_status: str = None) -> None:
        with state_store.StateLock(self.common_git_directory):
            state = state_store.load_state(self.common_git_directory)
            lead_binding = None
            if lead_status is not None:
                lead_binding = state_store.LeadBinding(spec="s", lead_model="m", worker_model="w", status=lead_status)
            state.worktrees[worktree_id] = state_store.WorktreeEntry(
                worktree_id=worktree_id,
                path=worktree_path,
                branch="spec/" + worktree_id,
                lead=lead_binding,
            )
            state_store.save_state(self.common_git_directory, state)

    def test_enqueue_gate_raises_no_broker_error_and_leaves_the_queue_empty(self):
        worktree_path = self.repository.add_raw_worktree("alpha")
        self._register_worktree("alpha", worktree_path)

        with self.assertRaises(NoBrokerError):
            gate_client.enqueue_gate(worktree_path, [], [], timeout_seconds=5)

        queue_directory = os.path.join(state_store.state_directory(self.common_git_directory), "queue")
        self.assertEqual([], os.listdir(queue_directory))

    def test_run_gate_returns_broker_verdict_and_restores_lead_status_to_building(self):
        worktree_path = self.repository.add_raw_worktree("bravo")
        self._register_worktree("bravo", worktree_path, lead_status="building")

        broker_thread = _FakeBroker(self.common_git_directory, poll_seconds=0.02)
        broker_thread.start()
        try:
            result = gate_client.run_gate(worktree_path, ["FixtureA"], [], timeout_seconds=5, poll_seconds=0.05)
        finally:
            broker_thread.stop_event.set()
            broker_thread.join(timeout=2)

        self.assertEqual("pass", result["verdict"])
        with state_store.StateLock(self.common_git_directory):
            state_after = state_store.load_state(self.common_git_directory)
        self.assertEqual("building", state_after.worktrees["bravo"].lead.status)


if __name__ == "__main__":
    unittest.main()
