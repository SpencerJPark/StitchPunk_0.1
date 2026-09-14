"""Copies the toolkit's Claude Code integration files into the stage checkout and wires its hooks."""
from __future__ import annotations

import json
import os
import shutil
from typing import Any, Dict, List

from worktree_toolkit import context
from worktree_toolkit.errors import RefusedError

_HOOK_CREATE_COMMAND = (
    'python "${CLAUDE_PROJECT_DIR}/Packages/com.worktreetoolkit/Tools~/worktree.py" hook-create'
)
_HOOK_REMOVE_COMMAND = (
    'python "${CLAUDE_PROJECT_DIR}/Packages/com.worktreetoolkit/Tools~/worktree.py" hook-remove'
)
_HOOK_EVENT_COMMANDS = {
    "WorktreeCreate": _HOOK_CREATE_COMMAND,
    "WorktreeRemove": _HOOK_REMOVE_COMMAND,
}


def _tools_root() -> str:
    # Locate from this file's own path, never from the stage path being installed into.
    return os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def _require_source_file(source_path: str) -> None:
    if not os.path.isfile(source_path):
        raise RefusedError("missing source file required for install: {0}".format(source_path))


def _copy_source_file(source_path: str, destination_path: str) -> None:
    os.makedirs(os.path.dirname(destination_path), exist_ok=True)
    shutil.copyfile(source_path, destination_path)


def _hook_entry_present(existing_entries: List[Dict[str, Any]], command: str) -> bool:
    for entry in existing_entries:
        for inner_hook in entry.get("hooks", []):
            if inner_hook.get("command") == command:
                return True
    return False


def _merge_hooks(settings: Dict[str, Any]) -> Dict[str, List[str]]:
    hooks_section: Dict[str, Any] = settings.setdefault("hooks", {})
    added_events: List[str] = []
    already_present_events: List[str] = []

    for event_name, command in _HOOK_EVENT_COMMANDS.items():
        event_entries: List[Dict[str, Any]] = hooks_section.setdefault(event_name, [])
        if _hook_entry_present(event_entries, command):
            already_present_events.append(event_name)
            continue
        event_entries.append({"hooks": [{"type": "command", "command": command}]})
        added_events.append(event_name)

    return {"hooksAdded": added_events, "hooksAlreadyPresent": already_present_events}


def install_claude(working_directory: str) -> Dict[str, Any]:
    toolkit_context: context.ToolkitContext = context.resolve_context(working_directory)
    context.require_stage(toolkit_context)

    tools_root: str = _tools_root()
    agent_source_path: str = os.path.join(tools_root, "claude", "agents", "spec-lead.md")
    skill_source_path: str = os.path.join(tools_root, "claude", "skills", "worktree-run", "SKILL.md")
    _require_source_file(agent_source_path)
    _require_source_file(skill_source_path)

    claude_directory: str = os.path.join(toolkit_context.stage_path, ".claude")
    agent_destination_path: str = os.path.join(claude_directory, "agents", "spec-lead.md")
    skill_destination_path: str = os.path.join(
        claude_directory, "skills", "worktree-run", "SKILL.md"
    )
    _copy_source_file(agent_source_path, agent_destination_path)
    _copy_source_file(skill_source_path, skill_destination_path)

    settings_path: str = os.path.join(claude_directory, "settings.json")
    settings: Dict[str, Any]
    if os.path.isfile(settings_path):
        with open(settings_path, "r", encoding="utf-8") as settings_file:
            settings = json.load(settings_file)
    else:
        os.makedirs(claude_directory, exist_ok=True)
        settings = {}

    merge_result: Dict[str, List[str]] = _merge_hooks(settings)

    with open(settings_path, "w", encoding="utf-8") as settings_file:
        json.dump(settings, settings_file, indent=2)
        settings_file.write("\n")

    return {
        "copied": [agent_destination_path, skill_destination_path],
        "hooksAdded": merge_result["hooksAdded"],
        "hooksAlreadyPresent": merge_result["hooksAlreadyPresent"],
        "settingsPath": settings_path,
    }
