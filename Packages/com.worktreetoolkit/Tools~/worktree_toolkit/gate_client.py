"""Client side of the file-queue gate protocol: enqueue a request for the Unity Editor broker and poll for its verdict."""
from __future__ import annotations

import json
import os
import time
import uuid
from typing import List

from worktree_toolkit import context, git_runner, state_store
from worktree_toolkit.errors import GateTimeoutError, NoBrokerError, RefusedError

HEARTBEAT_FILE_NAME = "broker-heartbeat.json"
HEARTBEAT_MAX_AGE_SECONDS = 10.0
# A successful gate compile is followed by a domain reload that silences the heartbeat for 30-60 s (probe P7).
HEARTBEAT_MAX_AGE_DURING_GATE_SECONDS = 240.0


def _heartbeat_file_path(common_git_directory: str) -> str:
    return os.path.join(state_store.state_directory(common_git_directory), HEARTBEAT_FILE_NAME)


def broker_is_alive(common_git_directory: str, maximum_age_seconds: float = HEARTBEAT_MAX_AGE_SECONDS) -> bool:
    heartbeat_file_path = _heartbeat_file_path(common_git_directory)
    try:
        heartbeat_age_seconds = time.time() - os.path.getmtime(heartbeat_file_path)
    except OSError:
        return False
    return heartbeat_age_seconds <= maximum_age_seconds


def _write_json_atomically(file_path: str, payload: dict) -> None:
    # A crashed writer must never leave a half-written queue/result file for the other side to read.
    temporary_file_path = file_path + ".tmp"
    with open(temporary_file_path, "w", encoding="utf-8") as temporary_file:
        json.dump(payload, temporary_file)
    os.replace(temporary_file_path, file_path)


def enqueue_gate(
    working_directory: str,
    edit_mode_fixtures: List[str],
    play_mode_fixtures: List[str],
    timeout_seconds: float,
) -> dict:
    toolkit_context = context.resolve_context(working_directory)
    context.require_linked_worktree(toolkit_context)

    with state_store.StateLock(toolkit_context.common_git_directory):
        state = state_store.load_state(toolkit_context.common_git_directory)
        entry = state_store.find_entry_by_path(state, toolkit_context.repository_root)
        # The C2 dry run found a lead that gated before claiming; an unclaimed gate has no spec to report against.
        if entry is None or entry.lead is None or not entry.lead.spec:
            raise RefusedError(
                "claim this worktree first: worktree.py claim <spec-id> --spec <path> --lead-model <model> --worker-model <model>")
        if entry.parked or git_runner.dirty_entry_count(toolkit_context.working_directory) > 0:
            raise RefusedError("commit before gating")
        if not broker_is_alive(toolkit_context.common_git_directory):
            raise NoBrokerError("no live broker heartbeat; open the Unity Editor broker before gating")

        request: dict = {
            "protocol": 1,
            "requestId": uuid.uuid4().hex,
            "worktreeId": entry.worktree_id,
            "commitSha": git_runner.resolve_commit(toolkit_context.working_directory),
            "editModeFixtures": list(edit_mode_fixtures),
            "playModeFixtures": list(play_mode_fixtures),
            "timeoutSeconds": timeout_seconds,
            "createdUtc": state_store.utc_now_text(),
            "phase": "queued",
        }
        queue_directory = os.path.join(state_store.state_directory(toolkit_context.common_git_directory), "queue")
        _write_json_atomically(os.path.join(queue_directory, request["requestId"] + ".json"), request)

        if entry.lead is not None:
            entry.lead.status = "gating"
        state_store.save_state(toolkit_context.common_git_directory, state)

        return request


def wait_for_result(
    common_git_directory: str,
    request_id: str,
    timeout_seconds: float,
    poll_seconds: float = 1.0,
) -> dict:
    result_file_path = os.path.join(state_store.state_directory(common_git_directory), "results", request_id + ".json")
    deadline_time = time.time() + timeout_seconds
    while True:
        if os.path.isfile(result_file_path):
            with open(result_file_path, "r", encoding="utf-8") as result_file:
                return json.load(result_file)
        if not broker_is_alive(common_git_directory, HEARTBEAT_MAX_AGE_DURING_GATE_SECONDS):
            raise NoBrokerError("broker heartbeat went stale while waiting for a gate result")
        if time.time() >= deadline_time:
            raise GateTimeoutError("gate result did not arrive within {0} seconds".format(timeout_seconds))
        time.sleep(poll_seconds)


def run_gate(
    working_directory: str,
    edit_mode_fixtures: List[str],
    play_mode_fixtures: List[str],
    timeout_seconds: float = 900,
    poll_seconds: float = 1.0,
) -> dict:
    toolkit_context = context.resolve_context(working_directory)
    request = enqueue_gate(working_directory, edit_mode_fixtures, play_mode_fixtures, timeout_seconds)
    try:
        return wait_for_result(toolkit_context.common_git_directory, request["requestId"], timeout_seconds, poll_seconds)
    finally:
        with state_store.StateLock(toolkit_context.common_git_directory):
            state = state_store.load_state(toolkit_context.common_git_directory)
            entry = state_store.find_entry_by_path(state, toolkit_context.repository_root)
            if entry is not None and entry.lead is not None:
                entry.lead.status = "building"
                state_store.save_state(toolkit_context.common_git_directory, state)
