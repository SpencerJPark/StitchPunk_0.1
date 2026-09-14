"""Builds the read-only worktree graph (stage + every linked worktree) the CLI and Editor render."""
from __future__ import annotations

import json
import os
from typing import List, Optional

from worktree_toolkit import context, git_runner, links, state_store

STALE_BEHIND_THRESHOLD = 50


def directory_size_megabytes(root_path: str) -> int:
    """Sums file sizes under root_path in megabytes; never descends into a directory link."""
    total_size_bytes = 0
    pending_directories: List[str] = [root_path]
    while pending_directories:
        current_directory = pending_directories.pop()
        try:
            directory_entries = list(os.scandir(current_directory))
        except OSError:
            continue
        for directory_entry in directory_entries:
            if links.is_directory_link(directory_entry.path):
                continue
            if directory_entry.is_dir(follow_symlinks=False):
                pending_directories.append(directory_entry.path)
            else:
                try:
                    total_size_bytes += directory_entry.stat(follow_symlinks=False).st_size
                except OSError:
                    continue
    return int(total_size_bytes / (1024 * 1024))


def _describe_worktree(
    worktree_record: git_runner.WorktreeRecord,
    state: state_store.ToolkitState,
    trunk_branch: str,
    include_sizes: bool,
) -> dict:
    entry = state_store.find_entry_by_path(state, worktree_record.path)
    is_registered = entry is not None
    worktree_id = (
        entry.worktree_id if is_registered
        else state_store.sanitize_worktree_id(os.path.basename(worktree_record.path.rstrip(os.sep)))
    )
    parent_branch = entry.parent_branch if is_registered else trunk_branch
    is_missing = not os.path.exists(worktree_record.path)
    lead_dictionary = context.entry_to_dictionary(entry)["lead"] if is_registered else None

    ahead = 0
    behind = 0
    fork_point_sha: Optional[str] = None
    dirty_count = 0
    size_megabytes: Optional[int] = None

    if not is_missing:
        # A detached worktree has no branch name to diff with, so fall back to its commit sha.
        comparison_reference = worktree_record.branch if worktree_record.branch is not None else worktree_record.head_sha
        ahead, behind = git_runner.ahead_behind(worktree_record.path, parent_branch, comparison_reference)
        fork_point_sha = git_runner.merge_base(worktree_record.path, parent_branch, comparison_reference)
        dirty_count = git_runner.dirty_entry_count(worktree_record.path)
        if include_sizes:
            size_megabytes = directory_size_megabytes(worktree_record.path)

    return {
        "id": worktree_id,
        "registered": is_registered,
        "branch": worktree_record.branch,
        "path": worktree_record.path,
        "headSha": worktree_record.head_sha,
        "parentBranch": parent_branch,
        "forkPointSha": fork_point_sha,
        "ahead": ahead,
        "behind": behind,
        "dirty": dirty_count,
        "parked": entry.parked if is_registered else False,
        "mode": entry.mode if is_registered else state_store.WORKTREE_MODES[0],
        "sizeMegabytes": size_megabytes,
        "stale": ahead == 0 and behind >= STALE_BEHIND_THRESHOLD,
        "locked": worktree_record.is_locked,
        "missing": is_missing,
        "lead": lead_dictionary,
    }


def build_graph(working_directory: str, include_sizes: bool = False) -> dict:
    toolkit_context = context.resolve_context(working_directory)
    state = state_store.load_state(toolkit_context.common_git_directory)

    stage_branch = git_runner.current_branch(toolkit_context.stage_path)
    stage_detached_sha = git_runner.resolve_commit(toolkit_context.stage_path) if stage_branch is None else None
    stage_dirty_tracked_paths = context.filter_noise_paths(
        git_runner.tracked_modifications(toolkit_context.stage_path), state.config.stage_noise_globs
    )

    stage_lock_path = state_store.stage_lock_path(toolkit_context.common_git_directory)
    busy_with: Optional[dict] = None
    if os.path.exists(stage_lock_path):
        with open(stage_lock_path, "r", encoding="utf-8") as stage_lock_file:
            busy_with = json.load(stage_lock_file)

    stage_dictionary = {
        "path": toolkit_context.stage_path,
        "branch": stage_branch,
        "detachedSha": stage_detached_sha,
        "dirtyTracked": len(stage_dirty_tracked_paths),
        "busyWith": busy_with,
        "reviewWorktreeId": state.stage_review_worktree_id,
        "stashes": list(state.stage_stashes),
    }

    worktree_dictionaries: List[dict] = [
        _describe_worktree(worktree_record, state, state.config.trunk_branch, include_sizes)
        for worktree_record in git_runner.list_worktrees(working_directory)[1:]
    ]

    return {
        "protocol": state.protocol,
        "trunk": state.config.trunk_branch,
        "stage": stage_dictionary,
        "worktrees": worktree_dictionaries,
    }
