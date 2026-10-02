"""Argument parsing front door for the offline Unity compile gate and convention lint.

Resolves the project root from this file's location, not the process's current
working directory, so `devloop.py` behaves the same regardless of the caller's cwd.
"""
from __future__ import annotations

import argparse
import os
import sys

from devloop_tools import gate, lint

_PROJECT_ROOT: str = os.path.abspath(
    os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..")
)

# Exit codes callers depend on: 0 pass, 1 real findings/compile errors,
# 2 the check could not run honestly (stale compile args, missing Unity install, unknown package).
_EXIT_CODE_PASS: int = 0
_EXIT_CODE_FINDINGS: int = 1
_EXIT_CODE_COULD_NOT_RUN: int = 2


def _run_gate_command(package_or_assembly: str) -> int:
    try:
        gate_report = gate.gate(_PROJECT_ROOT, package_or_assembly)
    except KeyError as unknown_package_or_assembly_error:
        print(str(unknown_package_or_assembly_error), file=sys.stderr)
        return _EXIT_CODE_COULD_NOT_RUN
    except FileNotFoundError as missing_compile_arguments_error:
        # An assembly Unity never compiled has no harvested rsp. That is "cannot check", not "compile errors" -
        # letting it surface as a traceback exits 1, which a caller reads as a real failure.
        print(f"GATE UNAVAILABLE  {missing_compile_arguments_error}", file=sys.stderr)
        return _EXIT_CODE_COULD_NOT_RUN

    print(gate.format_report(gate_report))

    # A non-empty stale_reason means the gate could not run honestly; never report a false pass for that.
    if gate_report.stale_reason:
        return _EXIT_CODE_COULD_NOT_RUN
    if gate_report.overall_passed:
        return _EXIT_CODE_PASS
    return _EXIT_CODE_FINDINGS


def _run_lint_command(paths: list[str]) -> int:
    lint_report = lint.lint_paths(_PROJECT_ROOT, paths)
    print(lint.format_report(lint_report))
    return _EXIT_CODE_PASS if lint_report.passed else _EXIT_CODE_FINDINGS


def main(argv: list[str]) -> int:
    argument_parser = argparse.ArgumentParser(
        prog="devloop",
        description="Offline Unity compile gate and convention lint for this project's packages.",
    )
    subparsers = argument_parser.add_subparsers(dest="command", required=True)

    gate_subparser = subparsers.add_parser(
        "gate", help="run the offline compile gate against a package or a single assembly"
    )
    gate_subparser.add_argument(
        "package_or_assembly",
        help="a package folder name (e.g. com.dotsanimationtoolkit) or a single assembly name",
    )

    lint_subparser = subparsers.add_parser(
        "lint", help="run the convention lint over explicit paths, or the current git diff when none are given"
    )
    lint_subparser.add_argument(
        "paths",
        nargs="*",
        help="project-relative file or directory paths to lint; omit to lint the current git diff",
    )

    parsed_arguments = argument_parser.parse_args(argv)

    if parsed_arguments.command == "gate":
        return _run_gate_command(parsed_arguments.package_or_assembly)
    if parsed_arguments.command == "lint":
        return _run_lint_command(parsed_arguments.paths)

    argument_parser.error(f"unknown command {parsed_arguments.command!r}")
    return _EXIT_CODE_COULD_NOT_RUN
