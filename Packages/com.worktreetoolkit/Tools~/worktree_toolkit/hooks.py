"""Claude Code hook entry points: create/remove worktrees on session lifecycle, claim/status for leads."""

from __future__ import annotations

import json
import os

from worktree_toolkit import git_runner, lifecycle_ops
from worktree_toolkit.context import entry_to_dictionary, require_linked_worktree, resolve_context
from worktree_toolkit.errors import RefusedError, UsageError
from worktree_toolkit.state_store import (
    LEAD_STATUSES,
    LeadBinding,
    StateLock,
    find_entry_by_path,
    load_state,
    require_entry,
    sanitize_worktree_id,
    save_state,
    validate_worktree_id,
)
from worktree_toolkit.lifecycle_ops import register_worktree


def hook_create(payload_text: str) -> str:
    """WorktreeCreate hook: payload carries cwd/name only (no base) — new worktree branches from trunk."""
    payload = json.loads(payload_text)
    # payload["cwd"] may already be a linked worktree when a lead spawns a nested worker session.
    stage_path = git_runner.main_checkout_path(payload["cwd"])
    worktree_id = sanitize_worktree_id(payload["name"])
    branch_name = "worktree-" + worktree_id
    created_entry_dictionary = lifecycle_ops.create_worktree(stage_path, worktree_id, branch_name=branch_name)

    common_git_directory_path = git_runner.common_git_directory(stage_path)
    with StateLock(common_git_directory_path):
        state = load_state(common_git_directory_path)
        created_entry = require_entry(state, worktree_id)
        created_entry.lead = LeadBinding(status="unclaimed")
        save_state(common_git_directory_path, state)

    return created_entry_dictionary["path"]


def hook_remove(payload_text: str) -> int:
    """WorktreeRemove hook: 0 clears the worktree, 1 tells Claude Code to keep it (unsaved work)."""
    payload = json.loads(payload_text)
    worktree_path = payload["worktree_path"]
    common_git_directory_path = git_runner.common_git_directory(worktree_path)
    state = load_state(common_git_directory_path)
    entry = find_entry_by_path(state, worktree_path)
    if entry is None:
        return 0

    ahead_of_trunk, _behind_of_trunk = git_runner.ahead_behind(
        entry.path, state.config.trunk_branch, entry.branch
    )
    if git_runner.dirty_entry_count(entry.path) > 0 or ahead_of_trunk > 0:
        return 1

    stage_path = git_runner.main_checkout_path(worktree_path)
    lifecycle_ops.remove_worktree(stage_path, entry.worktree_id)
    return 0


def claim_worktree(working_directory: str, spec_id: str, spec_path: str, lead_model: str, worker_model: str) -> dict:
    """A lead claims the worktree it was spawned into: renames its branch to spec/<spec_id> and re-keys state."""
    context = resolve_context(working_directory)
    require_linked_worktree(context)
    validated_spec_id = validate_worktree_id(spec_id)
    claimed_branch_name = "spec/" + validated_spec_id

    with StateLock(context.common_git_directory):
        state = load_state(context.common_git_directory)

        for existing_worktree_id, existing_worktree_entry in state.worktrees.items():
            if existing_worktree_id == validated_spec_id and not git_runner.paths_equal(
                existing_worktree_entry.path, context.repository_root
            ):
                raise RefusedError(f"spec id '{validated_spec_id}' is already registered to another worktree")
        if git_runner.branch_exists(context.working_directory, claimed_branch_name):
            raise RefusedError(f"branch '{claimed_branch_name}' already exists")

        entry = find_entry_by_path(state, context.repository_root)
        if entry is None:
            # Not created through hook_create (or state was lost) — register it under a derived id first.
            derived_worktree_id = validate_worktree_id(os.path.basename(context.repository_root))
            current_branch_name = git_runner.current_branch(context.working_directory) or ""
            entry = register_worktree(
                state, derived_worktree_id, context.repository_root, current_branch_name, state.config.trunk_branch
            )

        git_runner.run_git(["branch", "-m", claimed_branch_name], context.working_directory)

        del state.worktrees[entry.worktree_id]
        entry.worktree_id = validated_spec_id
        entry.branch = claimed_branch_name
        entry.lead = LeadBinding(spec=spec_path, lead_model=lead_model, worker_model=worker_model, status="building")
        state.worktrees[validated_spec_id] = entry

        save_state(context.common_git_directory, state)
        return entry_to_dictionary(entry)


def set_lead_status(working_directory: str, status: str) -> dict:
    """Updates the lead's status for the worktree the caller is standing in."""
    context = resolve_context(working_directory)
    require_linked_worktree(context)
    if status not in LEAD_STATUSES:
        raise UsageError(f"'{status}' is not a recognized lead status")

    with StateLock(context.common_git_directory):
        state = load_state(context.common_git_directory)
        entry = find_entry_by_path(state, context.repository_root)
        if entry is None:
            raise RefusedError("claim this worktree first")

        if entry.lead is None:
            entry.lead = LeadBinding(status=status)
        else:
            entry.lead.status = status

        save_state(context.common_git_directory, state)
        return entry_to_dictionary(entry)
