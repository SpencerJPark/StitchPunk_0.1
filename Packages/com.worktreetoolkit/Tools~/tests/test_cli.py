"""cli.py entry point: hook-create must print exactly one path line on stdin."""
from __future__ import annotations

import json
import os
import subprocess
import sys
import unittest

from tests.repo_fixture import TemporaryRepository

_TOOLS_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
_WORKTREE_SCRIPT_PATH = os.path.join(_TOOLS_ROOT, "worktree.py")


class HookCreateCliTests(unittest.TestCase):
    def setUp(self) -> None:
        self.repository = TemporaryRepository()

    def tearDown(self) -> None:
        self.repository.cleanup()

    def test_hook_create_prints_one_path_line_that_exists(self) -> None:
        payload_text = json.dumps(
            {
                "cwd": self.repository.stage_path,
                "name": "agent-cli1",
                "hook_event_name": "WorktreeCreate",
                "session_id": "x",
            }
        )
        completed_process = subprocess.run(
            [sys.executable, _WORKTREE_SCRIPT_PATH, "--cwd", self.repository.stage_path, "hook-create"],
            input=payload_text,
            capture_output=True,
            text=True,
        )
        self.assertEqual(0, completed_process.returncode, completed_process.stderr)
        output_lines = completed_process.stdout.strip().splitlines()
        self.assertEqual(1, len(output_lines))
        self.assertTrue(os.path.exists(output_lines[0]))


if __name__ == "__main__":
    unittest.main()
