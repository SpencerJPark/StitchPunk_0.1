"""Toolkit state lives in <git-common-dir>/worktree-toolkit/ so every worktree and the Editor see one copy."""
from __future__ import annotations

import dataclasses
import datetime
import json
import os
import re
import time
from dataclasses import dataclass, field
from typing import Dict, List, Optional

from worktree_toolkit.errors import RefusedError, UsageError

PROTOCOL_VERSION = 1
STATE_DIRECTORY_NAME = "worktree-toolkit"
STATE_FILE_NAME = "state.json"
STATE_LOCK_FILE_NAME = "state.lock"
STAGE_LOCK_FILE_NAME = "stage.lock"
LEAD_STATUSES = ("unclaimed", "building", "gating", "ready", "failed", "done")
WORKTREE_MODES = ("source-only", "own-editor")
DEFAULT_LIBRARY_SEED_EXCLUSIONS = ["BurstCache", "ShaderCache", "Search", "*Captures", "APIUpdater"]
STALE_LOCK_SECONDS = 120.0
_WORKTREE_ID_PATTERN = re.compile(r"^[a-z0-9][a-z0-9-]{0,40}$")


@dataclass
class LeadBinding:
    spec: str = ""
    lead_model: str = ""
    worker_model: str = ""
    status: str = "unclaimed"


@dataclass
class WorktreeEntry:
    worktree_id: str
    path: str
    branch: str
    mode: str = "source-only"
    parent_branch: str = "main"
    parked: bool = False
    links: List[str] = field(default_factory=list)
    lead: Optional[LeadBinding] = None
    created_utc: str = ""


@dataclass
class ToolkitConfig:
    trunk_branch: str = "main"
    stage_noise_globs: List[str] = field(default_factory=list)
    library_seed_exclusions: List[str] = field(default_factory=lambda: list(DEFAULT_LIBRARY_SEED_EXCLUSIONS))


@dataclass
class ToolkitState:
    protocol: int = PROTOCOL_VERSION
    config: ToolkitConfig = field(default_factory=ToolkitConfig)
    worktrees: Dict[str, WorktreeEntry] = field(default_factory=dict)
    stage_stashes: List[str] = field(default_factory=list)
    stage_review_worktree_id: Optional[str] = None


def utc_now_text() -> str:
    return datetime.datetime.now(datetime.timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")


def validate_worktree_id(worktree_id: str) -> str:
    if not _WORKTREE_ID_PATTERN.match(worktree_id or ""):
        raise UsageError("worktree id '{0}' must be lowercase letters, digits and hyphens (max 41)".format(worktree_id))
    return worktree_id


def sanitize_worktree_id(raw_name: str) -> str:
    sanitized_name = re.sub(r"[^a-z0-9-]+", "-", raw_name.lower()).strip("-")[:41]
    return sanitized_name or "worktree"


def state_directory(common_git_directory: str) -> str:
    directory_path = os.path.join(common_git_directory, STATE_DIRECTORY_NAME)
    for subdirectory_name in ("", "queue", "results"):
        os.makedirs(os.path.join(directory_path, subdirectory_name), exist_ok=True)
    return directory_path


def state_file_path(common_git_directory: str) -> str:
    return os.path.join(state_directory(common_git_directory), STATE_FILE_NAME)


def stage_lock_path(common_git_directory: str) -> str:
    return os.path.join(state_directory(common_git_directory), STAGE_LOCK_FILE_NAME)


def _known_fields(dataclass_type, raw_dictionary: dict) -> dict:
    field_names = {dataclass_field.name for dataclass_field in dataclasses.fields(dataclass_type)}
    return {key: value for key, value in raw_dictionary.items() if key in field_names}


def _entry_from_dictionary(raw_entry: dict) -> WorktreeEntry:
    entry_fields = _known_fields(WorktreeEntry, raw_entry)
    raw_lead = entry_fields.get("lead")
    entry_fields["lead"] = LeadBinding(**_known_fields(LeadBinding, raw_lead)) if isinstance(raw_lead, dict) else None
    return WorktreeEntry(**entry_fields)


def load_state(common_git_directory: str) -> ToolkitState:
    file_path = state_file_path(common_git_directory)
    if not os.path.exists(file_path):
        return ToolkitState()
    with open(file_path, "r", encoding="utf-8") as state_file:
        raw_state = json.load(state_file)
    if raw_state.get("protocol") != PROTOCOL_VERSION:
        raise RefusedError(
            "state.json protocol {0} does not match this CLI's protocol {1}; update the toolkit in this checkout".format(
                raw_state.get("protocol"), PROTOCOL_VERSION))
    return ToolkitState(
        protocol=PROTOCOL_VERSION,
        config=ToolkitConfig(**_known_fields(ToolkitConfig, raw_state.get("config") or {})),
        worktrees={
            worktree_id: _entry_from_dictionary(raw_entry)
            for worktree_id, raw_entry in (raw_state.get("worktrees") or {}).items()
        },
        stage_stashes=list(raw_state.get("stage_stashes") or []),
        stage_review_worktree_id=raw_state.get("stage_review_worktree_id"),
    )


def save_state(common_git_directory: str, state: ToolkitState) -> None:
    file_path = state_file_path(common_git_directory)
    temporary_path = file_path + ".tmp"
    with open(temporary_path, "w", encoding="utf-8") as state_file:
        json.dump(dataclasses.asdict(state), state_file, indent=1, sort_keys=True)
    os.replace(temporary_path, file_path)


def find_entry_by_path(state: ToolkitState, worktree_path: str) -> Optional[WorktreeEntry]:
    normalized_target = os.path.normcase(os.path.normpath(os.path.abspath(worktree_path)))
    for entry in state.worktrees.values():
        if os.path.normcase(os.path.normpath(os.path.abspath(entry.path))) == normalized_target:
            return entry
    return None


def require_entry(state: ToolkitState, worktree_id: str) -> WorktreeEntry:
    entry = state.worktrees.get(worktree_id)
    if entry is None:
        raise RefusedError("no registered worktree '{0}' (run adopt to register existing worktrees)".format(worktree_id))
    return entry


class StateLock:
    """Cross-process mutex around read-modify-write of state.json; a lock older than 120 s is treated as abandoned."""

    def __init__(self, common_git_directory: str, timeout_seconds: float = 30.0):
        self.lock_path = os.path.join(state_directory(common_git_directory), STATE_LOCK_FILE_NAME)
        self.timeout_seconds = timeout_seconds
        self.file_descriptor: Optional[int] = None

    def __enter__(self) -> "StateLock":
        deadline = time.monotonic() + self.timeout_seconds
        while True:
            try:
                self.file_descriptor = os.open(self.lock_path, os.O_CREAT | os.O_EXCL | os.O_WRONLY)
                os.write(self.file_descriptor, "{0} {1}".format(os.getpid(), utc_now_text()).encode("utf-8"))
                return self
            except FileExistsError:
                try:
                    if time.time() - os.path.getmtime(self.lock_path) > STALE_LOCK_SECONDS:
                        os.remove(self.lock_path)
                        continue
                except FileNotFoundError:
                    continue
                if time.monotonic() > deadline:
                    raise RefusedError("state.lock is held by another worktree command: " + self.lock_path)
                time.sleep(0.1)

    def __exit__(self, exception_type, exception_value, traceback) -> None:
        if self.file_descriptor is not None:
            os.close(self.file_descriptor)
            self.file_descriptor = None
        try:
            os.remove(self.lock_path)
        except FileNotFoundError:
            pass
