"""Where a command is running from, and the stage/worktree guards every command starts with."""
from __future__ import annotations

import fnmatch
from dataclasses import dataclass
from typing import List, Optional

from worktree_toolkit import git_runner
from worktree_toolkit.errors import RefusedError
from worktree_toolkit.state_store import WorktreeEntry


@dataclass
class ToolkitContext:
    working_directory: str
    repository_root: str
    common_git_directory: str
    stage_path: str
    is_linked_worktree: bool


def resolve_context(working_directory: str) -> ToolkitContext:
    return ToolkitContext(
        working_directory=git_runner.normalize_path(working_directory),
        repository_root=git_runner.repository_root(working_directory),
        common_git_directory=git_runner.common_git_directory(working_directory),
        stage_path=git_runner.main_checkout_path(working_directory),
        is_linked_worktree=git_runner.is_linked_worktree(working_directory),
    )


def require_stage(context: ToolkitContext) -> None:
    if context.is_linked_worktree:
        raise RefusedError("this command moves the stage; run it from the stage checkout ({0}), not a linked worktree".format(context.stage_path))


def require_linked_worktree(context: ToolkitContext) -> None:
    if not context.is_linked_worktree:
        raise RefusedError("this command runs inside a linked worktree, not the stage checkout")


def filter_noise_paths(changed_paths: List[str], noise_globs: List[str]) -> List[str]:
    """Drops paths matching any glob; paths and globs compare with forward slashes, case-sensitively."""
    remaining_paths: List[str] = []
    for changed_path in changed_paths:
        slash_path = changed_path.replace("\\", "/")
        if not any(fnmatch.fnmatchcase(slash_path, noise_glob) for noise_glob in noise_globs):
            remaining_paths.append(changed_path)
    return remaining_paths


def entry_to_dictionary(entry: WorktreeEntry) -> dict:
    lead_dictionary: Optional[dict] = None
    if entry.lead is not None:
        lead_dictionary = {
            "spec": entry.lead.spec,
            "leadModel": entry.lead.lead_model,
            "workerModel": entry.lead.worker_model,
            "status": entry.lead.status,
        }
    return {
        "id": entry.worktree_id,
        "branch": entry.branch,
        "path": entry.path,
        "mode": entry.mode,
        "parentBranch": entry.parent_branch,
        "parked": entry.parked,
        "links": list(entry.links),
        "lead": lead_dictionary,
        "createdUtc": entry.created_utc,
    }
