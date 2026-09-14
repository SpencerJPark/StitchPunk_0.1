"""Command-line entry point: `python worktree.py <command> [options]`."""
from __future__ import annotations

import os
import sys

# __pycache__ inside a worktree is untracked clutter and, on deep paths, pushes git past MAX_PATH on remove.
sys.dont_write_bytecode = True
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from worktree_toolkit.cli import main  # noqa: E402  (path must be set up first)

if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
