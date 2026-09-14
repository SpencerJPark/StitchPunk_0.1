"""Throwaway git repositories for the CLI fixtures; never touches the real project."""
from __future__ import annotations

import os
import shutil
import stat
import subprocess
import tempfile
from typing import Optional


def _run(arguments, working_directory: str) -> str:
    completed_process = subprocess.run(
        ["git"] + list(arguments), cwd=working_directory, capture_output=True, text=True, encoding="utf-8"
    )
    if completed_process.returncode != 0:
        raise RuntimeError("git {0} failed: {1}".format(" ".join(arguments), completed_process.stderr))
    return completed_process.stdout.strip()


def _is_directory_link(path: str) -> bool:
    if os.path.islink(path):
        return True
    is_junction = getattr(os.path, "isjunction", None)
    return bool(is_junction and is_junction(path))


def _remove_read_only_then_retry(function, path, exception_info) -> None:
    # git object files are read-only on Windows, which makes a plain rmtree fail.
    os.chmod(path, stat.S_IWRITE)
    function(path)


class TemporaryRepository:
    """A stage checkout at <root>/stage on branch main with one commit; worktrees go under stage/.claude/worktrees."""

    def __init__(self):
        self.root_directory = tempfile.mkdtemp(prefix="wtk-")
        self.stage_path = os.path.join(self.root_directory, "stage")
        os.makedirs(self.stage_path)
        _run(["init", "-q", "-b", "main"], self.stage_path)
        _run(["config", "user.name", "Fixture"], self.stage_path)
        _run(["config", "user.email", "fixture@example.invalid"], self.stage_path)
        _run(["config", "core.autocrlf", "false"], self.stage_path)
        with open(os.path.join(self.stage_path, ".git", "info", "exclude"), "a", encoding="utf-8") as exclude_file:
            exclude_file.write("\n.claude/worktrees/\n")
        self.commit_file(self.stage_path, "readme.txt", "stage\n", "initial")

    def git(self, working_directory: str, *arguments: str) -> str:
        return _run(list(arguments), working_directory)

    def write_file(self, working_directory: str, relative_path: str, content: str) -> str:
        file_path = os.path.join(working_directory, relative_path)
        os.makedirs(os.path.dirname(file_path) or working_directory, exist_ok=True)
        with open(file_path, "w", encoding="utf-8", newline="\n") as target_file:
            target_file.write(content)
        return file_path

    def commit_file(self, working_directory: str, relative_path: str, content: str, message: str) -> str:
        self.write_file(working_directory, relative_path, content)
        _run(["add", "--", relative_path], working_directory)
        _run(["commit", "-q", "-m", message], working_directory)
        return _run(["rev-parse", "HEAD"], working_directory)

    def worktree_path(self, worktree_id: str) -> str:
        return os.path.join(self.stage_path, ".claude", "worktrees", worktree_id)

    def add_raw_worktree(self, worktree_id: str, branch_name: Optional[str] = None, base_reference: str = "main") -> str:
        """Plain git worktree add, bypassing the toolkit (for adopt-style fixtures)."""
        worktree_path = self.worktree_path(worktree_id)
        _run(["worktree", "add", "-q", "-b", branch_name or "spec/" + worktree_id, worktree_path, base_reference], self.stage_path)
        return worktree_path

    def cleanup(self) -> None:
        # Unlink directory links first: never let a recursive delete reach a link target (trap 1).
        for directory_path, directory_names, _ in os.walk(self.root_directory, topdown=True):
            for directory_name in list(directory_names):
                child_path = os.path.join(directory_path, directory_name)
                if _is_directory_link(child_path):
                    if os.path.islink(child_path) and os.name != "nt":
                        os.unlink(child_path)
                    else:
                        os.rmdir(child_path)
                    directory_names.remove(directory_name)
        shutil.rmtree(self.root_directory, onerror=_remove_read_only_then_retry)
