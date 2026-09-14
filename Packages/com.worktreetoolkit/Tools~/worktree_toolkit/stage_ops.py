"""Moves the stage checkout between trunk, a parked worktree's branch, a stash, and a bare commit."""
from __future__ import annotations

import json
import os
from typing import List, Optional

from worktree_toolkit.context import ToolkitContext, filter_noise_paths, require_stage, resolve_context
from worktree_toolkit.errors import GitCommandError, RefusedError
from worktree_toolkit.git_runner import current_branch, dirty_entry_count, run_git, tracked_modifications
from worktree_toolkit.state_store import (
    StateLock,
    ToolkitState,
    load_state,
    require_entry,
    save_state,
    stage_lock_path,
    utc_now_text,
)


def stage_blockers(context: ToolkitContext, state: ToolkitState) -> List[str]:
    """Human-readable reasons the stage cannot be moved right now; an empty list means it is safe."""
    blockers: List[str] = []
    remaining_tracked_paths = filter_noise_paths(tracked_modifications(context.stage_path), state.config.stage_noise_globs)
    for changed_path in remaining_tracked_paths:
        blockers.append("modified: " + changed_path)
    lock_path = stage_lock_path(context.common_git_directory)
    if os.path.exists(lock_path):
        holder_description = "unknown"
        try:
            with open(lock_path, "r", encoding="utf-8") as lock_file:
                lock_payload = json.load(lock_file)
            holder_description = lock_payload.get("holder", holder_description)
        except (OSError, ValueError):
            pass
        blockers.append("stage busy: " + holder_description)
    return blockers


def review_worktree(working_directory: str, worktree_id: str) -> dict:
    context = resolve_context(working_directory)
    require_stage(context)
    with StateLock(context.common_git_directory):
        state = load_state(context.common_git_directory)
        entry = require_entry(state, worktree_id)
        if state.stage_review_worktree_id is not None:
            raise RefusedError("return the stage first")
        if entry.lead is not None and entry.lead.status in ("building", "gating"):
            raise RefusedError("worktree '{0}' lead is {1}; wait for it to finish".format(worktree_id, entry.lead.status))
        blockers = stage_blockers(context, state)
        if blockers:
            raise RefusedError("stage is not clear: " + "; ".join(blockers))
        if dirty_entry_count(entry.path) > 0:
            raise RefusedError("worktree '{0}' has uncommitted changes".format(worktree_id))
        if current_branch(entry.path) != entry.branch:
            raise RefusedError("worktree '{0}' is not on its registered branch '{1}'".format(worktree_id, entry.branch))
        run_git(["switch", "--detach"], entry.path)
        stage_switch_result = run_git(["switch", entry.branch], context.stage_path, check=False)
        if stage_switch_result.exit_code != 0:
            run_git(["switch", entry.branch], entry.path)
            raise RefusedError(
                "could not move branch '{0}' onto the stage: {1}".format(entry.branch, stage_switch_result.standard_error.strip())
            )
        entry.parked = True
        state.stage_review_worktree_id = worktree_id
        save_state(context.common_git_directory, state)
    return {"stageBranch": entry.branch, "parkedWorktreeId": worktree_id}


def return_stage(working_directory: str) -> dict:
    context = resolve_context(working_directory)
    require_stage(context)
    with StateLock(context.common_git_directory):
        state = load_state(context.common_git_directory)
        if state.stage_review_worktree_id is None:
            raise RefusedError("no review recorded on the stage")
        entry = require_entry(state, state.stage_review_worktree_id)
        remaining_tracked_paths = filter_noise_paths(tracked_modifications(context.stage_path), state.config.stage_noise_globs)
        if remaining_tracked_paths:
            raise RefusedError("stage has tracked modifications: " + ", ".join(remaining_tracked_paths))
        trunk_branch = state.config.trunk_branch
        run_git(["switch", trunk_branch], context.stage_path)
        run_git(["switch", entry.branch], entry.path)
        entry.parked = False
        state.stage_review_worktree_id = None
        save_state(context.common_git_directory, state)
    return {"stageBranch": trunk_branch, "reattachedWorktreeId": entry.worktree_id}


def stash_stage(working_directory: str) -> dict:
    context = resolve_context(working_directory)
    require_stage(context)
    with StateLock(context.common_git_directory):
        if not tracked_modifications(context.stage_path):
            raise RefusedError("nothing to stash in the stage")
        message = "worktree-toolkit/" + utc_now_text()
        run_git(["stash", "push", "-m", message], context.stage_path)
        state = load_state(context.common_git_directory)
        state.stage_stashes.append(message)
        save_state(context.common_git_directory, state)
    return {"stash": message}


def pop_stage(working_directory: str) -> dict:
    context = resolve_context(working_directory)
    require_stage(context)
    with StateLock(context.common_git_directory):
        state = load_state(context.common_git_directory)
        if not state.stage_stashes:
            raise RefusedError("no stash recorded to pop")
        message = state.stage_stashes[-1]
        stash_list_output = run_git(["stash", "list", "--format=%gd%x09%gs"], context.stage_path).standard_output
        matched_reference: Optional[str] = None
        for stash_list_line in stash_list_output.splitlines():
            if not stash_list_line.strip():
                continue
            reference_text, _, subject_text = stash_list_line.partition("\t")
            if subject_text.endswith(message):
                matched_reference = reference_text
                break
        if matched_reference is None:
            raise RefusedError("recorded stash not found in git stash list: " + message)
        run_git(["stash", "pop", matched_reference], context.stage_path)
        state.stage_stashes.remove(message)
        save_state(context.common_git_directory, state)
    return {"popped": message}


def place_commit_on_stage(working_directory: str, commit_sha: str, holder_description: str) -> dict:
    context = resolve_context(working_directory)
    require_stage(context)
    with StateLock(context.common_git_directory):
        state = load_state(context.common_git_directory)
        if state.stage_review_worktree_id is not None:
            raise RefusedError("a worktree review is on the stage; return it first")
        blockers = stage_blockers(context, state)
        if blockers:
            raise RefusedError("stage is not clear: " + "; ".join(blockers))
        lock_path = stage_lock_path(context.common_git_directory)
        lock_payload = {"holder": holder_description, "commitSha": commit_sha, "since": utc_now_text()}
        with open(lock_path, "w", encoding="utf-8") as lock_file:
            json.dump(lock_payload, lock_file)
        try:
            run_git(["switch", "--detach", commit_sha], context.stage_path)
        except GitCommandError:
            os.remove(lock_path)
            raise
    return {"stageDetachedSha": commit_sha}


def restore_trunk_on_stage(working_directory: str) -> dict:
    context = resolve_context(working_directory)
    require_stage(context)
    with StateLock(context.common_git_directory):
        state = load_state(context.common_git_directory)
        trunk_branch = state.config.trunk_branch
        run_git(["switch", trunk_branch], context.stage_path)
        lock_path = stage_lock_path(context.common_git_directory)
        if os.path.exists(lock_path):
            os.remove(lock_path)
    return {"stageBranch": trunk_branch}
