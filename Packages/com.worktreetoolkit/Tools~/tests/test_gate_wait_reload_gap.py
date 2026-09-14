from __future__ import annotations

import json
import os
import threading
import time
import unittest

from tests.repo_fixture import TemporaryRepository
from worktree_toolkit import gate_client, git_runner, state_store


class GateWaitReloadGapTests(unittest.TestCase):
    """Found by the 2026-09-14 broker drive: a passing gate's domain reload silenced the heartbeat and the client quit with exit 3."""

    def setUp(self):
        self.repository = TemporaryRepository()
        self.common_git_directory = git_runner.common_git_directory(self.repository.stage_path)
        self.state_directory = state_store.state_directory(self.common_git_directory)

    def tearDown(self):
        self.repository.cleanup()

    def test_wait_for_result_tolerates_a_reload_length_heartbeat_gap(self):
        heartbeat_path = os.path.join(self.state_directory, gate_client.HEARTBEAT_FILE_NAME)
        with open(heartbeat_path, "w", encoding="utf-8") as heartbeat_file:
            heartbeat_file.write("{}")
        sixty_seconds_ago = time.time() - 60.0
        os.utime(heartbeat_path, (sixty_seconds_ago, sixty_seconds_ago))
        request_id = "reloadgap"
        result_path = os.path.join(self.state_directory, "results", request_id + ".json")

        def write_result_after_short_delay():
            time.sleep(0.2)
            with open(result_path, "w", encoding="utf-8") as result_file:
                json.dump({"requestId": request_id, "verdict": "pass"}, result_file)

        writer_thread = threading.Thread(target=write_result_after_short_delay)
        writer_thread.start()
        try:
            result = gate_client.wait_for_result(self.common_git_directory, request_id, timeout_seconds=5.0, poll_seconds=0.05)
        finally:
            writer_thread.join()

        self.assertEqual("pass", result["verdict"])


if __name__ == "__main__":
    unittest.main()
