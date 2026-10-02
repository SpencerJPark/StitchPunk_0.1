"""Command-line entry point: `python Tools/devloop/devloop.py <command> [options]`."""
from __future__ import annotations

import os
import sys

# __pycache__ under Tools/ is untracked clutter; the worktree toolkit's entry point does the same.
sys.dont_write_bytecode = True
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from devloop_tools.cli import main  # noqa: E402  (path must be set up first)

if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
