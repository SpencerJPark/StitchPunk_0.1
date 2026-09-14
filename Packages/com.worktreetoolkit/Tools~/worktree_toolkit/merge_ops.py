"""Rebases a worktree branch onto trunk and fast-forwards the stage; also wires up UnityYAMLMerge."""
from __future__ import annotations

import os
from typing import List

from worktree_toolkit import context, git_runner, state_store
from worktree_toolkit.errors import RefusedError

UNITY_YAML_PATTERNS = ["*.unity", "*.prefab", "*.asset", "*.mat", "*.anim", "*.controller"]


def merge_worktree(working_directory: str, worktree_id: str) -> dict:
    toolkit_context = context.resolve_context(working_directory)
    context.require_stage(toolkit_context)

    with state_store.StateLock(toolkit_context.common_git_directory):
        toolkit_state = state_store.load_state(toolkit_context.common_git_directory)
        worktree_entry = state_store.require_entry(toolkit_state, worktree_id)
        trunk_branch = toolkit_state.config.trunk_branch

        if toolkit_state.stage_review_worktree_id is not None:
            raise RefusedError("a review is recorded on the stage; return the stage before merging")

        stage_current_branch = git_runner.current_branch(toolkit_context.stage_path)
        if stage_current_branch != trunk_branch:
            raise RefusedError("the stage is not on trunk branch '{0}'".format(trunk_branch))

        # A fast-forward carries uncommitted edits the branch never touches and git refuses real overwrites itself,
        # so only a modified path the branch also changes blocks (the owner's stage is never clean; D9b).
        branch_changed_paths_text = git_runner.run_git(
            ["diff", "--name-only", "{0}...{1}".format(trunk_branch, worktree_entry.branch)], toolkit_context.stage_path
        ).standard_output
        branch_changed_paths = {
            changed_path.replace("\\", "/") for changed_path in branch_changed_paths_text.splitlines() if changed_path
        }
        conflicting_stage_modifications = [
            modified_path
            for modified_path in context.filter_noise_paths(
                git_runner.tracked_modifications(toolkit_context.stage_path), toolkit_state.config.stage_noise_globs
            )
            if modified_path.replace("\\", "/") in branch_changed_paths
        ]
        if conflicting_stage_modifications:
            raise RefusedError(
                "stage has uncommitted changes to files '{0}' also changes: {1}".format(
                    worktree_entry.branch, ", ".join(conflicting_stage_modifications))
            )

        if worktree_entry.parked:
            raise RefusedError("worktree '{0}' is parked for review; return it before merging".format(worktree_id))

        if git_runner.dirty_entry_count(worktree_entry.path) > 0:
            raise RefusedError("worktree '{0}' has uncommitted changes".format(worktree_id))

        worktree_current_branch = git_runner.current_branch(worktree_entry.path)
        if worktree_current_branch != worktree_entry.branch:
            raise RefusedError(
                "worktree '{0}' is not on its registered branch '{1}'".format(worktree_id, worktree_entry.branch)
            )

        commits_ahead_of_trunk, _ = git_runner.ahead_behind(toolkit_context.stage_path, trunk_branch, worktree_entry.branch)
        if commits_ahead_of_trunk == 0:
            raise RefusedError("worktree '{0}' has zero commits ahead of trunk".format(worktree_id))

        rebase_result = git_runner.run_git(["rebase", trunk_branch], worktree_entry.path, check=False)
        if rebase_result.exit_code != 0:
            conflicted_files_result = git_runner.run_git(
                ["diff", "--name-only", "--diff-filter=U"], worktree_entry.path, check=False
            )
            conflicted_file_paths = [line for line in conflicted_files_result.standard_output.splitlines() if line]
            git_runner.run_git(["rebase", "--abort"], worktree_entry.path, check=False)
            raise RefusedError(
                "rebase of '{0}' onto '{1}' conflicts in: {2}".format(
                    worktree_entry.branch, trunk_branch, ", ".join(conflicted_file_paths)
                )
            )

        git_runner.run_git(["merge", "--ff-only", worktree_entry.branch], toolkit_context.stage_path)

        if worktree_entry.lead is not None:
            worktree_entry.lead.status = "done"

        trunk_sha_after_merge = git_runner.resolve_commit(toolkit_context.stage_path, trunk_branch)
        state_store.save_state(toolkit_context.common_git_directory, toolkit_state)

    return {
        "merged": worktree_entry.worktree_id,
        "branch": worktree_entry.branch,
        "trunkSha": trunk_sha_after_merge,
        "commitCount": commits_ahead_of_trunk,
    }


def configure_unity_yaml_merge(working_directory: str, unity_yaml_merge_path: str) -> dict:
    git_runner.run_git(["config", "--local", "merge.unityyamlmerge.name", "Unity SmartMerge"], working_directory)
    merge_driver_command = "\"{0}\" merge -p %O %B %A %A".format(unity_yaml_merge_path)
    git_runner.run_git(["config", "--local", "merge.unityyamlmerge.driver", merge_driver_command], working_directory)

    common_git_directory = git_runner.common_git_directory(working_directory)
    attributes_file_path = os.path.join(common_git_directory, "info", "attributes")
    os.makedirs(os.path.dirname(attributes_file_path), exist_ok=True)

    existing_attribute_lines: List[str] = []
    if os.path.exists(attributes_file_path):
        with open(attributes_file_path, "r", encoding="utf-8") as attributes_file:
            existing_attribute_lines = attributes_file.read().splitlines()

    attribute_lines_to_append: List[str] = []
    for unity_yaml_pattern in UNITY_YAML_PATTERNS:
        attribute_line = "{0} merge=unityyamlmerge".format(unity_yaml_pattern)
        if attribute_line not in existing_attribute_lines:
            attribute_lines_to_append.append(attribute_line)

    if attribute_lines_to_append:
        with open(attributes_file_path, "a", encoding="utf-8") as attributes_file:
            for attribute_line in attribute_lines_to_append:
                attributes_file.write(attribute_line + "\n")

    return {"driver": merge_driver_command, "patterns": list(UNITY_YAML_PATTERNS)}
