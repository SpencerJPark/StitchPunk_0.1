"""Argparse entry point: maps CLI subcommands onto the toolkit's operation modules."""
from __future__ import annotations

import argparse
import json
import os
import sys
from typing import Any, Dict, List, Optional

from worktree_toolkit import context, errors, gate_client, git_runner, graph_model, hooks
from worktree_toolkit import install, lifecycle_ops, merge_ops, stage_ops, state_store


def _build_doctor_result(working_directory: str) -> Dict[str, Any]:
    """Every field degrades to a value on failure instead of raising - doctor must never refuse."""
    result: Dict[str, Any] = {}
    try:
        git_version_tuple = git_runner.git_version()
        result["gitVersion"] = ".".join(str(version_part) for version_part in git_version_tuple)
        result["gitOk"] = git_version_tuple >= (2, 38, 0)
    except Exception as version_error:
        result["gitVersion"] = None
        result["gitOk"] = False
        result["error"] = str(version_error)

    result["python"] = sys.version.split()[0]

    try:
        long_paths_value = git_runner.run_git(
            ["config", "--get", "core.longpaths"], working_directory, check=False
        ).standard_output.strip()
        # Without it git for Windows fails checkout and remove on paths over 260 characters.
        result["longPathsEnabled"] = long_paths_value.lower() == "true"
    except Exception:
        result["longPathsEnabled"] = False

    toolkit_context: Optional[context.ToolkitContext] = None
    try:
        toolkit_context = context.resolve_context(working_directory)
        result["stage"] = toolkit_context.stage_path
    except Exception as context_error:
        result["stage"] = None
        result["error"] = str(context_error)

    if toolkit_context is None:
        result["brokerAlive"] = False
        result["hooksInstalled"] = False
        result["stageBlockers"] = []
        return result

    try:
        result["brokerAlive"] = gate_client.broker_is_alive(toolkit_context.common_git_directory)
    except Exception as broker_error:
        result["brokerAlive"] = False
        result["error"] = str(broker_error)

    settings_path = os.path.join(toolkit_context.stage_path, ".claude", "settings.json")
    try:
        with open(settings_path, "r", encoding="utf-8") as settings_file:
            settings_document = json.load(settings_file)
        # Parse rather than substring-match: the raw file escapes the quote before hook-create.
        create_hook_commands = [
            hook.get("command", "")
            for hook_group in settings_document.get("hooks", {}).get("WorktreeCreate", [])
            for hook in hook_group.get("hooks", [])
        ]
        result["hooksInstalled"] = any(command.endswith("worktree.py\" hook-create") for command in create_hook_commands)
    except Exception:
        result["hooksInstalled"] = False

    try:
        toolkit_state = state_store.load_state(toolkit_context.common_git_directory)
        result["stageBlockers"] = stage_ops.stage_blockers(toolkit_context, toolkit_state)
    except Exception as blockers_error:
        result["stageBlockers"] = []
        result["error"] = str(blockers_error)

    return result


def _print_result(result: Dict[str, Any], json_output: bool) -> None:
    if json_output:
        print(json.dumps(result, indent=1))
        return
    for key, value in result.items():
        if isinstance(value, list):
            item_texts = [
                json.dumps(item, separators=(",", ":")) if isinstance(item, (dict, list)) else str(item)
                for item in value
            ]
            print("{0}: {1}".format(key, ", ".join(item_texts)))
        elif isinstance(value, dict):
            print("{0}: {1}".format(key, json.dumps(value, separators=(",", ":"))))
        else:
            print("{0}: {1}".format(key, value))


def _command_doctor(arguments: argparse.Namespace, working_directory: str) -> int:
    _print_result(_build_doctor_result(working_directory), arguments.json)
    return 0


def _command_list(arguments: argparse.Namespace, working_directory: str) -> int:
    result = graph_model.build_graph(working_directory, arguments.sizes)
    _print_result(result, arguments.json)
    return 0


def _command_create(arguments: argparse.Namespace, working_directory: str) -> int:
    result = lifecycle_ops.create_worktree(
        working_directory, arguments.id, arguments.from_branch, arguments.mode
    )
    _print_result(result, arguments.json)
    return 0


def _command_adopt(arguments: argparse.Namespace, working_directory: str) -> int:
    result = lifecycle_ops.adopt_worktrees(working_directory)
    _print_result(result, arguments.json)
    return 0


def _command_remove(arguments: argparse.Namespace, working_directory: str) -> int:
    result = lifecycle_ops.remove_worktree(
        working_directory, arguments.id, arguments.keep_branch, arguments.force
    )
    _print_result(result, arguments.json)
    return 0


def _command_hook_create(arguments: argparse.Namespace, working_directory: str) -> int:
    payload_text = sys.stdin.read()
    hook_path = hooks.hook_create(payload_text)
    print(hook_path)
    return 0


def _command_hook_remove(arguments: argparse.Namespace, working_directory: str) -> int:
    payload_text = sys.stdin.read()
    return hooks.hook_remove(payload_text)


def _command_claim(arguments: argparse.Namespace, working_directory: str) -> int:
    result = hooks.claim_worktree(
        working_directory, arguments.id, arguments.spec, arguments.lead_model, arguments.worker_model
    )
    _print_result(result, arguments.json)
    return 0


def _command_status(arguments: argparse.Namespace, working_directory: str) -> int:
    toolkit_context = context.resolve_context(working_directory)
    toolkit_state = state_store.load_state(toolkit_context.common_git_directory)
    registered_entry = state_store.find_entry_by_path(toolkit_state, toolkit_context.repository_root)
    if registered_entry is None or registered_entry.worktree_id != arguments.id:
        raise errors.RefusedError(
            "worktree id '{0}' does not match the entry registered for this worktree".format(arguments.id)
        )
    result = hooks.set_lead_status(working_directory, arguments.status)
    _print_result(result, arguments.json)
    return 0


def _command_gate(arguments: argparse.Namespace, working_directory: str) -> int:
    result = gate_client.run_gate(
        working_directory, arguments.edit_mode or [], arguments.play_mode or [], arguments.timeout
    )
    _print_result(result, arguments.json)
    return 0 if result.get("verdict") == "pass" else 2


def _command_review(arguments: argparse.Namespace, working_directory: str) -> int:
    result = stage_ops.review_worktree(working_directory, arguments.id)
    _print_result(result, arguments.json)
    return 0


def _command_return(arguments: argparse.Namespace, working_directory: str) -> int:
    result = stage_ops.return_stage(working_directory)
    _print_result(result, arguments.json)
    return 0


def _command_stash_stage(arguments: argparse.Namespace, working_directory: str) -> int:
    result = stage_ops.stash_stage(working_directory)
    _print_result(result, arguments.json)
    return 0


def _command_pop_stage(arguments: argparse.Namespace, working_directory: str) -> int:
    result = stage_ops.pop_stage(working_directory)
    _print_result(result, arguments.json)
    return 0


def _command_stage_commit(arguments: argparse.Namespace, working_directory: str) -> int:
    result = stage_ops.place_commit_on_stage(working_directory, arguments.sha, arguments.holder)
    _print_result(result, arguments.json)
    return 0


def _command_restore_trunk(arguments: argparse.Namespace, working_directory: str) -> int:
    result = stage_ops.restore_trunk_on_stage(working_directory)
    _print_result(result, arguments.json)
    return 0


def _command_merge(arguments: argparse.Namespace, working_directory: str) -> int:
    result = merge_ops.merge_worktree(working_directory, arguments.id)
    _print_result(result, arguments.json)
    return 0


def _command_yaml_merge_driver(arguments: argparse.Namespace, working_directory: str) -> int:
    result = merge_ops.configure_unity_yaml_merge(working_directory, arguments.unity_yaml_merge_path)
    _print_result(result, arguments.json)
    return 0


def _command_install_claude(arguments: argparse.Namespace, working_directory: str) -> int:
    result = install.install_claude(working_directory)
    _print_result(result, arguments.json)
    return 0


def _command_where(arguments: argparse.Namespace, working_directory: str) -> int:
    resolved_context = context.resolve_context(working_directory)
    result: Dict[str, Any] = {
        "stagePath": resolved_context.stage_path,
        "commonGitDirectory": resolved_context.common_git_directory,
        "stateDirectory": state_store.state_directory(resolved_context.common_git_directory),
        "protocol": state_store.PROTOCOL_VERSION,
    }
    _print_result(result, arguments.json)
    return 0


def _command_changed_paths(arguments: argparse.Namespace, working_directory: str) -> int:
    resolved_context = context.resolve_context(working_directory)
    git_result = git_runner.run_git(
        ["diff", "--name-only", "HEAD", arguments.reference], resolved_context.stage_path
    )
    changed_paths = [
        line.replace("\\", "/") for line in git_result.standard_output.splitlines() if line
    ]
    _print_result({"paths": changed_paths}, arguments.json)
    return 0


def _extract_global_cwd_option(argument_list: List[str]) -> "tuple[Optional[str], List[str]]":
    """Pulls --cwd out by hand: argparse's subparser-default clobbers a shared dest set at the
    top level (a known argparse gotcha), and --cwd must work whether it precedes or follows the
    subcommand name."""
    working_directory_value: Optional[str] = None
    remaining_arguments: List[str] = []
    index = 0
    while index < len(argument_list):
        current_argument = argument_list[index]
        if current_argument == "--cwd":
            working_directory_value = argument_list[index + 1]
            index += 2
        elif current_argument.startswith("--cwd="):
            working_directory_value = current_argument.split("=", 1)[1]
            index += 1
        else:
            remaining_arguments.append(current_argument)
            index += 1
    return working_directory_value, remaining_arguments


def _build_argument_parser() -> argparse.ArgumentParser:
    common_arguments_parser = argparse.ArgumentParser(add_help=False)
    common_arguments_parser.add_argument("--json", action="store_true")

    # --cwd/--json live only on the subparsers (usage is `worktree <command> [--json]`, options
    # after the subcommand) - sharing the same dest with the top-level parser would let the
    # subparser's default silently overwrite a value the top-level parser already collected.
    top_level_parser = argparse.ArgumentParser(prog="worktree")
    subcommand_parsers = top_level_parser.add_subparsers(dest="command")

    doctor_parser = subcommand_parsers.add_parser("doctor", parents=[common_arguments_parser])
    doctor_parser.set_defaults(command_handler=_command_doctor)

    list_parser = subcommand_parsers.add_parser("list", parents=[common_arguments_parser])
    list_parser.add_argument("--sizes", action="store_true")
    list_parser.set_defaults(command_handler=_command_list)

    create_parser = subcommand_parsers.add_parser("create", parents=[common_arguments_parser])
    create_parser.add_argument("id")
    create_parser.add_argument("--from", dest="from_branch", default=None)
    create_parser.add_argument("--mode", default="source-only", choices=["source-only", "own-editor"])
    create_parser.set_defaults(command_handler=_command_create)

    adopt_parser = subcommand_parsers.add_parser("adopt", parents=[common_arguments_parser])
    adopt_parser.set_defaults(command_handler=_command_adopt)

    remove_parser = subcommand_parsers.add_parser("remove", parents=[common_arguments_parser])
    remove_parser.add_argument("id")
    remove_parser.add_argument("--keep-branch", action="store_true")
    remove_parser.add_argument("--force", action="store_true")
    remove_parser.set_defaults(command_handler=_command_remove)

    hook_create_parser = subcommand_parsers.add_parser("hook-create", parents=[common_arguments_parser])
    hook_create_parser.set_defaults(command_handler=_command_hook_create)

    hook_remove_parser = subcommand_parsers.add_parser("hook-remove", parents=[common_arguments_parser])
    hook_remove_parser.set_defaults(command_handler=_command_hook_remove)

    claim_parser = subcommand_parsers.add_parser("claim", parents=[common_arguments_parser])
    claim_parser.add_argument("id")
    claim_parser.add_argument("--spec", required=True)
    claim_parser.add_argument("--lead-model", required=True)
    claim_parser.add_argument("--worker-model", required=True)
    claim_parser.set_defaults(command_handler=_command_claim)

    status_parser = subcommand_parsers.add_parser("status", parents=[common_arguments_parser])
    status_parser.add_argument("id")
    status_parser.add_argument("status", choices=list(state_store.LEAD_STATUSES))
    status_parser.set_defaults(command_handler=_command_status)

    gate_parser = subcommand_parsers.add_parser("gate", parents=[common_arguments_parser])
    gate_parser.add_argument("--edit-mode", action="append", default=[])
    gate_parser.add_argument("--play-mode", action="append", default=[])
    gate_parser.add_argument("--timeout", type=float, default=900)
    gate_parser.set_defaults(command_handler=_command_gate)

    review_parser = subcommand_parsers.add_parser("review", parents=[common_arguments_parser])
    review_parser.add_argument("id")
    review_parser.set_defaults(command_handler=_command_review)

    return_parser = subcommand_parsers.add_parser("return", parents=[common_arguments_parser])
    return_parser.set_defaults(command_handler=_command_return)

    stash_stage_parser = subcommand_parsers.add_parser("stash-stage", parents=[common_arguments_parser])
    stash_stage_parser.set_defaults(command_handler=_command_stash_stage)

    pop_stage_parser = subcommand_parsers.add_parser("pop-stage", parents=[common_arguments_parser])
    pop_stage_parser.set_defaults(command_handler=_command_pop_stage)

    stage_commit_parser = subcommand_parsers.add_parser("stage-commit", parents=[common_arguments_parser])
    stage_commit_parser.add_argument("sha")
    stage_commit_parser.add_argument("--holder", required=True)
    stage_commit_parser.set_defaults(command_handler=_command_stage_commit)

    restore_trunk_parser = subcommand_parsers.add_parser("restore-trunk", parents=[common_arguments_parser])
    restore_trunk_parser.set_defaults(command_handler=_command_restore_trunk)

    merge_parser = subcommand_parsers.add_parser("merge", parents=[common_arguments_parser])
    merge_parser.add_argument("id")
    merge_parser.set_defaults(command_handler=_command_merge)

    yaml_merge_driver_parser = subcommand_parsers.add_parser(
        "yaml-merge-driver", parents=[common_arguments_parser]
    )
    yaml_merge_driver_parser.add_argument("unity_yaml_merge_path")
    yaml_merge_driver_parser.set_defaults(command_handler=_command_yaml_merge_driver)

    install_claude_parser = subcommand_parsers.add_parser("install-claude", parents=[common_arguments_parser])
    install_claude_parser.set_defaults(command_handler=_command_install_claude)

    config_parser = subcommand_parsers.add_parser("config", parents=[common_arguments_parser])
    config_parser.add_argument("--trunk", default=None)
    config_parser.add_argument("--add-noise-glob", action="append", default=[])
    config_parser.add_argument("--remove-noise-glob", action="append", default=[])
    config_parser.set_defaults(command_handler=_command_config)

    where_parser = subcommand_parsers.add_parser("where", parents=[common_arguments_parser])
    where_parser.set_defaults(command_handler=_command_where)

    changed_paths_parser = subcommand_parsers.add_parser("changed-paths", parents=[common_arguments_parser])
    changed_paths_parser.add_argument("reference")
    changed_paths_parser.set_defaults(command_handler=_command_changed_paths)

    return top_level_parser


def _command_config(arguments: argparse.Namespace, working_directory: str) -> int:
    from worktree_toolkit import config_ops

    result = config_ops.update_config(
        working_directory, arguments.trunk, arguments.add_noise_glob, arguments.remove_noise_glob
    )
    _print_result(result, arguments.json)
    return 0


def main(arguments: List[str]) -> int:
    working_directory_option, remaining_arguments = _extract_global_cwd_option(arguments)
    parser = _build_argument_parser()
    parsed_arguments = parser.parse_args(remaining_arguments)

    if not hasattr(parsed_arguments, "command_handler"):
        parser.print_help()
        return 1

    working_directory = working_directory_option or os.getcwd()
    json_output = getattr(parsed_arguments, "json", False)

    try:
        return parsed_arguments.command_handler(parsed_arguments, working_directory)
    except errors.ToolkitError as toolkit_error:
        print(str(toolkit_error), file=sys.stderr)
        if json_output:
            print(json.dumps({"error": str(toolkit_error), "exitCode": toolkit_error.exit_code}))
        return toolkit_error.exit_code
