"""Reads and edits the shared toolkit config (trunk branch, stage noise globs) in state.json."""
from __future__ import annotations

from typing import List, Optional

from worktree_toolkit import git_runner
from worktree_toolkit.context import require_stage, resolve_context
from worktree_toolkit.errors import RefusedError
from worktree_toolkit.state_store import StateLock, load_state, save_state


def update_config(
    working_directory: str,
    trunk_branch: Optional[str] = None,
    added_noise_globs: Optional[List[str]] = None,
    removed_noise_globs: Optional[List[str]] = None,
) -> dict:
    """With no changes requested this only reports the config, so it is safe from any checkout."""
    toolkit_context = resolve_context(working_directory)
    has_changes = bool(trunk_branch or added_noise_globs or removed_noise_globs)
    if has_changes:
        require_stage(toolkit_context)
    with StateLock(toolkit_context.common_git_directory):
        state = load_state(toolkit_context.common_git_directory)
        if trunk_branch:
            if not git_runner.branch_exists(toolkit_context.stage_path, trunk_branch):
                raise RefusedError("trunk branch '{0}' does not exist".format(trunk_branch))
            state.config.trunk_branch = trunk_branch
        for noise_glob in added_noise_globs or []:
            if noise_glob not in state.config.stage_noise_globs:
                state.config.stage_noise_globs.append(noise_glob)
        for noise_glob in removed_noise_globs or []:
            if noise_glob in state.config.stage_noise_globs:
                state.config.stage_noise_globs.remove(noise_glob)
        if has_changes:
            save_state(toolkit_context.common_git_directory, state)
    return {
        "trunkBranch": state.config.trunk_branch,
        "stageNoiseGlobs": list(state.config.stage_noise_globs),
        "librarySeedExclusions": list(state.config.library_seed_exclusions),
    }
