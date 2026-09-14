"""Every git invocation goes through run_git so a failure always carries its arguments and stderr."""
from __future__ import annotations

import os
import re
import subprocess
from dataclasses import dataclass
from typing import List, Optional, Tuple

from worktree_toolkit.errors import GitCommandError


@dataclass
class GitResult:
    exit_code: int
    standard_output: str
    standard_error: str


@dataclass
class WorktreeRecord:
    path: str
    head_sha: Optional[str]
    branch: Optional[str]
    is_detached: bool
    is_locked: bool
    lock_reason: Optional[str]
    is_prunable: bool
    is_bare: bool


def _git_environment() -> dict:
    environment = dict(os.environ)
    environment["GIT_TERMINAL_PROMPT"] = "0"
    # A pager or an editor waiting on input would hang every non-interactive caller.
    environment["GIT_PAGER"] = "cat"
    environment["GIT_EDITOR"] = "true"
    return environment


def run_git(arguments: List[str], working_directory: str, check: bool = True) -> GitResult:
    completed_process = subprocess.run(
        ["git"] + list(arguments),
        cwd=working_directory,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        env=_git_environment(),
    )
    result = GitResult(completed_process.returncode, completed_process.stdout, completed_process.stderr)
    if check and result.exit_code != 0:
        raise GitCommandError(list(arguments), result.exit_code, result.standard_error.strip())
    return result


def normalize_path(path: str) -> str:
    return os.path.normpath(os.path.abspath(path))


def paths_equal(first_path: str, second_path: str) -> bool:
    return os.path.normcase(normalize_path(first_path)) == os.path.normcase(normalize_path(second_path))


def git_version() -> Tuple[int, int, int]:
    version_text = run_git(["--version"], os.getcwd()).standard_output
    version_match = re.search(r"(\d+)\.(\d+)\.(\d+)", version_text)
    if version_match is None:
        return (0, 0, 0)
    return (int(version_match.group(1)), int(version_match.group(2)), int(version_match.group(3)))


def repository_root(working_directory: str) -> str:
    return normalize_path(run_git(["rev-parse", "--show-toplevel"], working_directory).standard_output.strip())


def git_directory(working_directory: str) -> str:
    return normalize_path(run_git(["rev-parse", "--absolute-git-dir"], working_directory).standard_output.strip())


def common_git_directory(working_directory: str) -> str:
    common_directory_text = run_git(
        ["rev-parse", "--path-format=absolute", "--git-common-dir"], working_directory
    ).standard_output.strip()
    return normalize_path(common_directory_text)


def is_linked_worktree(working_directory: str) -> bool:
    return not paths_equal(git_directory(working_directory), common_git_directory(working_directory))


def list_worktrees(working_directory: str) -> List[WorktreeRecord]:
    porcelain_text = run_git(["worktree", "list", "--porcelain"], working_directory).standard_output
    worktree_records: List[WorktreeRecord] = []
    for block_text in porcelain_text.strip().split("\n\n"):
        if not block_text.strip():
            continue
        record = WorktreeRecord(
            path="", head_sha=None, branch=None, is_detached=False,
            is_locked=False, lock_reason=None, is_prunable=False, is_bare=False,
        )
        for line_text in block_text.splitlines():
            keyword, _, value_text = line_text.partition(" ")
            if keyword == "worktree":
                record.path = normalize_path(value_text)
            elif keyword == "HEAD":
                record.head_sha = value_text
            elif keyword == "branch":
                record.branch = value_text[len("refs/heads/"):] if value_text.startswith("refs/heads/") else value_text
            elif keyword == "detached":
                record.is_detached = True
            elif keyword == "locked":
                record.is_locked = True
                record.lock_reason = value_text or None
            elif keyword == "prunable":
                record.is_prunable = True
            elif keyword == "bare":
                record.is_bare = True
        worktree_records.append(record)
    return worktree_records


def main_checkout_path(working_directory: str) -> str:
    return list_worktrees(working_directory)[0].path


def current_branch(working_directory: str) -> Optional[str]:
    branch_name = run_git(["branch", "--show-current"], working_directory).standard_output.strip()
    return branch_name or None


def resolve_commit(working_directory: str, reference: str = "HEAD") -> str:
    return run_git(["rev-parse", "--verify", reference + "^{commit}"], working_directory).standard_output.strip()


def branch_exists(working_directory: str, branch_name: str) -> bool:
    return run_git(["show-ref", "--verify", "--quiet", "refs/heads/" + branch_name], working_directory, check=False).exit_code == 0


def ahead_behind(working_directory: str, base_reference: str, branch_reference: str) -> Tuple[int, int]:
    """Returns (ahead, behind): commits only on branch_reference, commits only on base_reference."""
    count_text = run_git(
        ["rev-list", "--left-right", "--count", base_reference + "..." + branch_reference], working_directory
    ).standard_output.split()
    return (int(count_text[1]), int(count_text[0]))


def merge_base(working_directory: str, first_reference: str, second_reference: str) -> Optional[str]:
    result = run_git(["merge-base", first_reference, second_reference], working_directory, check=False)
    return result.standard_output.strip() or None


def is_ancestor(working_directory: str, ancestor_reference: str, descendant_reference: str) -> bool:
    result = run_git(["merge-base", "--is-ancestor", ancestor_reference, descendant_reference], working_directory, check=False)
    if result.exit_code not in (0, 1):
        raise GitCommandError(["merge-base", "--is-ancestor", ancestor_reference, descendant_reference], result.exit_code, result.standard_error.strip())
    return result.exit_code == 0


def _porcelain_paths(status_text: str) -> List[str]:
    changed_paths: List[str] = []
    entries = status_text.split("\0")
    entry_index = 0
    while entry_index < len(entries):
        entry_text = entries[entry_index]
        entry_index += 1
        if len(entry_text) < 4:
            continue
        status_code = entry_text[:2]
        changed_paths.append(entry_text[3:])
        # Renames and copies carry the original path as the next NUL-separated field.
        if status_code[0] in ("R", "C"):
            entry_index += 1
    return changed_paths


def tracked_modifications(working_directory: str) -> List[str]:
    status_text = run_git(["status", "--porcelain=v1", "-z", "--untracked-files=no"], working_directory).standard_output
    return _porcelain_paths(status_text)


def dirty_entry_count(working_directory: str) -> int:
    status_text = run_git(["status", "--porcelain=v1", "-z", "--untracked-files=normal"], working_directory).standard_output
    return len(_porcelain_paths(status_text))
