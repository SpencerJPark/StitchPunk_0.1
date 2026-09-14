"""Create, adopt, and remove worktree checkouts, guarding the junction-before-git removal order."""

from __future__ import annotations

import os
import shutil
from typing import Dict, List, Optional

from worktree_toolkit import git_runner, graph_model, links
from worktree_toolkit.context import entry_to_dictionary, require_stage, resolve_context
from worktree_toolkit.errors import RefusedError
from worktree_toolkit.state_store import (
    StateLock,
    ToolkitState,
    WorktreeEntry,
    find_entry_by_path,
    load_state,
    require_entry,
    sanitize_worktree_id,
    save_state,
    utc_now_text,
    validate_worktree_id,
)

# One threshold for both `list` and `adopt`, so a worktree is never stale in one and fresh in the other.
ADOPT_STALE_BEHIND_THRESHOLD = graph_model.STALE_BEHIND_THRESHOLD


def register_worktree(
    state: ToolkitState,
    worktree_id: str,
    path: str,
    branch: str,
    parent_branch: str,
) -> WorktreeEntry:
    if worktree_id in state.worktrees:
        raise RefusedError(f"worktree id '{worktree_id}' is already registered")
    entry = WorktreeEntry(
        worktree_id=worktree_id,
        path=path,
        branch=branch,
        parent_branch=parent_branch,
        created_utc=utc_now_text(),
    )
    state.worktrees[worktree_id] = entry
    return entry


def create_worktree(
    working_directory: str,
    worktree_id: str,
    from_branch: Optional[str] = None,
    mode: str = "source-only",
    branch_name: Optional[str] = None,
) -> dict:
    context = resolve_context(working_directory)
    require_stage(context)
    validated_worktree_id = validate_worktree_id(worktree_id)
    worktree_path = os.path.join(context.stage_path, ".claude", "worktrees", validated_worktree_id)
    with StateLock(context.common_git_directory):
        state = load_state(context.common_git_directory)
        if validated_worktree_id in state.worktrees:
            raise RefusedError(f"worktree id '{validated_worktree_id}' is already registered")
        if os.path.exists(worktree_path):
            raise RefusedError(f"worktree path already exists: {worktree_path}")
        if mode == "own-editor":
            raise RefusedError("own-editor mode arrives in Phase 5")
        target_branch = branch_name or ("spec/" + validated_worktree_id)
        if git_runner.branch_exists(context.repository_root, target_branch):
            raise RefusedError(f"branch '{target_branch}' already exists")
        base_branch = from_branch or state.config.trunk_branch
        git_runner.run_git(
            ["worktree", "add", "-b", target_branch, worktree_path, base_branch],
            context.repository_root,
        )
        entry = register_worktree(state, validated_worktree_id, worktree_path, target_branch, base_branch)
        save_state(context.common_git_directory, state)
    return entry_to_dictionary(entry)


def adopt_worktrees(working_directory: str) -> dict:
    context = resolve_context(working_directory)
    require_stage(context)
    adopted_entries: List[dict] = []
    already_registered_entries: List[dict] = []
    stale_entries: List[dict] = []
    with StateLock(context.common_git_directory):
        state = load_state(context.common_git_directory)
        worktree_records = git_runner.list_worktrees(context.repository_root)[1:]
        for record in worktree_records:
            existing_entry = find_entry_by_path(state, record.path)
            if existing_entry is not None:
                already_registered_entries.append(entry_to_dictionary(existing_entry))
                continue
            base_name = os.path.basename(os.path.normpath(record.path))
            candidate_worktree_id = sanitize_worktree_id(base_name)
            worktree_id = candidate_worktree_id
            collision_suffix = 2
            while worktree_id in state.worktrees:
                worktree_id = f"{candidate_worktree_id}-{collision_suffix}"
                collision_suffix += 1
            branch_name = record.branch or ""
            entry = register_worktree(state, worktree_id, record.path, branch_name, state.config.trunk_branch)
            entry_dictionary = entry_to_dictionary(entry)
            adopted_entries.append(entry_dictionary)
            if branch_name:
                ahead_count, behind_count = git_runner.ahead_behind(
                    context.repository_root, state.config.trunk_branch, branch_name
                )
                if ahead_count == 0 and behind_count >= ADOPT_STALE_BEHIND_THRESHOLD:
                    stale_entries.append(entry_dictionary)
        save_state(context.common_git_directory, state)
    return {
        "adopted": adopted_entries,
        "alreadyRegistered": already_registered_entries,
        "stale": stale_entries,
    }


def remove_worktree(
    working_directory: str,
    worktree_id: str,
    keep_branch: bool = False,
    force: bool = False,
) -> dict:
    context = resolve_context(working_directory)
    require_stage(context)
    with StateLock(context.common_git_directory):
        state = load_state(context.common_git_directory)
        entry = require_entry(state, worktree_id)

        if entry.parked:
            raise RefusedError(f"worktree '{worktree_id}' is parked")
        if state.stage_review_worktree_id == worktree_id:
            raise RefusedError(f"worktree '{worktree_id}' is the stage review worktree")

        worktree_records = git_runner.list_worktrees(context.repository_root)
        matching_record = next(
            (record for record in worktree_records if git_runner.paths_equal(record.path, entry.path)),
            None,
        )
        if matching_record is not None and matching_record.is_locked:
            raise RefusedError(f"worktree '{worktree_id}' is locked: {matching_record.lock_reason}")

        path_exists = os.path.isdir(entry.path)
        if path_exists and not force:
            dirty_count = git_runner.dirty_entry_count(entry.path)
            if dirty_count > 0:
                raise RefusedError(f"worktree '{worktree_id}' has {dirty_count} dirty entries; use force")

        # Step 2 is mandatory before any git call: `git worktree remove --force` deletes the
        # CONTENTS of a junction target still inside the worktree, not just the junction itself.
        unlinked_paths: List[str] = []
        if path_exists:
            for link_path in links.find_directory_links(entry.path):
                links.remove_directory_link(link_path)
                unlinked_paths.append(link_path)
            remaining_links = links.find_directory_links(entry.path)
            if remaining_links:
                raise RefusedError(f"junction links remain inside '{worktree_id}': {remaining_links}")

        if path_exists and entry.mode == "own-editor":
            library_path = os.path.join(entry.path, "Library")
            if os.path.isdir(library_path) and not links.is_directory_link(library_path):
                shutil.rmtree(library_path)

        if path_exists:
            remove_arguments = ["worktree", "remove"]
            if force:
                remove_arguments.append("--force")
            remove_arguments.append(entry.path)
            try:
                git_runner.run_git(remove_arguments, context.repository_root)
            except git_runner.GitCommandError:
                # git for Windows drops its worktree record, then fails "Filename too long" on paths
                # over MAX_PATH and leaves the folder. Finish only in that case; links were removed above.
                still_registered = any(
                    git_runner.paths_equal(record.path, entry.path)
                    for record in git_runner.list_worktrees(context.repository_root)
                )
                if still_registered:
                    raise
                extended_path = entry.path
                if os.name == "nt" and not extended_path.startswith("\\\\?\\"):
                    extended_path = "\\\\?\\" + os.path.abspath(entry.path)
                leftover_links = links.find_directory_links(extended_path)
                if leftover_links:
                    raise RefusedError(f"junction links remain inside '{worktree_id}': {leftover_links}")

                def clear_read_only_and_retry(failed_function, failed_path, exception_information):
                    os.chmod(failed_path, 0o666)
                    failed_function(failed_path)

                shutil.rmtree(extended_path, onerror=clear_read_only_and_retry)
        else:
            git_runner.run_git(["worktree", "prune"], context.repository_root)

        branch_deleted = False
        branch_kept_reason: Optional[str] = None
        if entry.branch and not keep_branch:
            if git_runner.is_ancestor(context.repository_root, entry.branch, state.config.trunk_branch):
                git_runner.run_git(["branch", "-d", entry.branch], context.repository_root)
                branch_deleted = True
            else:
                branch_kept_reason = f"not merged into {state.config.trunk_branch}"

        del state.worktrees[worktree_id]
        save_state(context.common_git_directory, state)

    return {
        "removed": worktree_id,
        "unlinked": unlinked_paths,
        "branchDeleted": branch_deleted,
        "branchKeptReason": branch_kept_reason,
    }
