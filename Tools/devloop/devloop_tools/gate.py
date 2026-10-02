"""Offline Roslyn compile gate: replaces the Unity MCP refresh/poll/read_console round trip.

Compiles each assembly target of a package with the Unity-bundled csc.dll directly, outside
the Editor, and prints a short pass/fail/stale verdict a subagent can read in one glance.
"""

from __future__ import annotations

import glob
import os
import re
import subprocess
import tempfile
import time
from dataclasses import dataclass, field

from devloop_tools import packages, rsp_harvest

_ERROR_LINE_PATTERN: re.Pattern[str] = re.compile(r"error CS\d+")
_WARNING_LINE_PATTERN: re.Pattern[str] = re.compile(r"warning CS\d+")

# A compile under one second with zero errors almost always means the response file
# was never actually read (e.g. an empty or truncated @rsp argument) rather than a
# genuinely fast pass, so callers must flag it instead of trusting it.
_SUSPICIOUSLY_FAST_SECONDS: float = 1.0


@dataclass
class AssemblyGateResult:
    assembly_name: str
    file_count: int
    error_lines: list[str] = field(default_factory=list)
    warning_count: int = 0
    seconds: float = 0.0
    skipped_reason: str = ""


@dataclass
class GateReport:
    per_assembly: list[AssemblyGateResult]
    overall_passed: bool
    requested_target: str = ""
    stale_reason: str = ""


def _find_csc_path(unity_data_directory: str) -> str:
    pinned_csc_path: str = f"{unity_data_directory}/DotNetSdk/sdk/8.0.318/Roslyn/bincore/csc.dll"
    if os.path.isfile(pinned_csc_path):
        return pinned_csc_path

    # Fall back to the newest installed SDK rather than guessing a version string.
    candidate_csc_paths: list[str] = glob.glob(
        f"{unity_data_directory}/DotNetSdk/sdk/*/Roslyn/bincore/csc.dll"
    )
    if not candidate_csc_paths:
        return ""
    candidate_csc_paths.sort(key=os.path.getmtime, reverse=True)
    return candidate_csc_paths[0]


def _compile_one_assembly(
    project_root: str,
    dotnet_executable_path: str,
    csc_dll_path: str,
    harvested_compile: rsp_harvest.HarvestedCompile,
    response_file_path: str,
) -> AssemblyGateResult:
    rsp_harvest.write_response_file(harvested_compile, response_file_path)

    start_time: float = time.monotonic()
    completed_process: subprocess.CompletedProcess[str] = subprocess.run(
        [dotnet_executable_path, csc_dll_path, "-nologo", f"@{response_file_path}"],
        # Paths inside the response file are project-relative, so cwd must be the project root
        # or csc reports every source and reference as missing.
        cwd=project_root,
        capture_output=True,
        text=True,
    )
    elapsed_seconds: float = time.monotonic() - start_time

    compiler_output: str = completed_process.stdout + completed_process.stderr
    unique_error_lines: list[str] = []
    seen_error_lines: set[str] = set()
    warning_count: int = 0
    for output_line in compiler_output.splitlines():
        if _ERROR_LINE_PATTERN.search(output_line) and output_line not in seen_error_lines:
            seen_error_lines.add(output_line)
            unique_error_lines.append(output_line)
        elif _WARNING_LINE_PATTERN.search(output_line):
            warning_count += 1

    skipped_reason: str = ""
    if elapsed_seconds < _SUSPICIOUSLY_FAST_SECONDS and not unique_error_lines:
        skipped_reason = (
            f"suspiciously fast ({elapsed_seconds:.2f}s) clean pass — the response file was "
            "likely not actually read; treat as unverified, not a real compile"
        )

    return AssemblyGateResult(
        assembly_name=harvested_compile.assembly_name,
        file_count=len(harvested_compile.source_files),
        error_lines=unique_error_lines,
        warning_count=warning_count,
        seconds=elapsed_seconds,
        skipped_reason=skipped_reason,
    )


def gate(project_root: str, package_or_assembly: str) -> GateReport:
    assembly_targets: list[packages.AssemblyTarget] = packages.targets_for(package_or_assembly)

    asmdef_paths: list[str] = [target.asmdef_path for target in assembly_targets]
    staleness_verdict: rsp_harvest.StalenessVerdict = rsp_harvest.is_stale(
        project_root, assembly_targets[0].assembly_name, asmdef_paths
    )
    if staleness_verdict.is_stale:
        return GateReport(
            per_assembly=[],
            overall_passed=False,
            requested_target=package_or_assembly,
            stale_reason=staleness_verdict.reason,
        )

    unity_data_directory: str = os.environ.get(
        "UNITY_DATA", "C:/Program Files/Unity/Hub/Editor/6000.5.0f1/Editor/Data"
    )
    dotnet_executable_path: str = f"{unity_data_directory}/DotNetSdk/dotnet.exe"
    csc_dll_path: str = _find_csc_path(unity_data_directory)
    if not csc_dll_path:
        return GateReport(
            per_assembly=[],
            overall_passed=False,
            requested_target=package_or_assembly,
            stale_reason="set UNITY_DATA to a Unity 6000.5 install",
        )

    build_output_directory: str = os.path.join(tempfile.gettempdir(), "devloop_gate_build")
    os.makedirs(build_output_directory, exist_ok=True)

    # Assembly name -> this run's freshly built .ref.dll, so a later sibling compiles against
    # what was just rebuilt rather than Unity's stale Library/Bee copy (the false-pass bug).
    fresh_sibling_ref_dll_paths: dict[str, str] = {}
    # Assembly names that were attempted this run and did not produce a trustworthy ref.dll;
    # anything depending on one of these must be skipped, never silently fall back to Bee's copy.
    unavailable_sibling_assembly_names: set[str] = set()

    per_assembly_results: list[AssemblyGateResult] = []
    for assembly_target in assembly_targets:
        harvested_compile: rsp_harvest.HarvestedCompile = rsp_harvest.harvest(
            project_root,
            assembly_target.assembly_name,
            assembly_target.source_roots,
            build_output_directory,
        )

        failed_upstream_sibling_name: str = next(
            (
                sibling_assembly_name
                for sibling_assembly_name in rsp_harvest.referenced_sibling_assembly_names(
                    harvested_compile.flag_lines
                )
                if sibling_assembly_name in unavailable_sibling_assembly_names
            ),
            "",
        )
        if failed_upstream_sibling_name:
            unavailable_sibling_assembly_names.add(assembly_target.assembly_name)
            per_assembly_results.append(
                AssemblyGateResult(
                    assembly_name=assembly_target.assembly_name,
                    file_count=len(harvested_compile.source_files),
                    skipped_reason=(
                        f"upstream assembly {failed_upstream_sibling_name} failed to compile this "
                        "run; compiling against its stale ref.dll would be a false pass"
                    ),
                )
            )
            continue

        harvested_compile.flag_lines = rsp_harvest.redirect_sibling_references(
            harvested_compile.flag_lines, fresh_sibling_ref_dll_paths
        )
        response_file_path: str = os.path.join(
            build_output_directory, f"{assembly_target.assembly_name}.rsp"
        )
        assembly_result: AssemblyGateResult = _compile_one_assembly(
            project_root, dotnet_executable_path, csc_dll_path, harvested_compile, response_file_path
        )
        per_assembly_results.append(assembly_result)

        if assembly_result.error_lines or assembly_result.skipped_reason:
            unavailable_sibling_assembly_names.add(assembly_target.assembly_name)
        else:
            fresh_sibling_ref_dll_paths[assembly_target.assembly_name] = os.path.join(
                build_output_directory, f"{assembly_target.assembly_name}.ref.dll"
            ).replace("\\", "/")

    overall_passed: bool = all(
        not result.error_lines and not result.skipped_reason for result in per_assembly_results
    )
    return GateReport(
        per_assembly=per_assembly_results,
        overall_passed=overall_passed,
        requested_target=package_or_assembly,
    )


def format_report(report: GateReport) -> str:
    if report.stale_reason:
        return (
            "GATE STALE - reopen the Editor once so it rewrites compile arguments\n"
            f"  {report.stale_reason}"
        )

    verdict_label: str = "GATE PASS" if report.overall_passed else "GATE FAIL"
    report_lines: list[str] = [f"{verdict_label}  {report.requested_target}"]

    for assembly_result in report.per_assembly:
        if assembly_result.skipped_reason:
            report_lines.append(
                f"  {assembly_result.assembly_name:<28}  SKIPPED - {assembly_result.skipped_reason}"
            )
            continue
        report_lines.append(
            f"  {assembly_result.assembly_name:<28} {assembly_result.file_count:>4} files"
            f"   {len(assembly_result.error_lines)} errors   {assembly_result.seconds:.1f}s"
        )

    if not report.overall_passed:
        all_error_lines: list[str] = []
        seen_error_lines: set[str] = set()
        for assembly_result in report.per_assembly:
            for error_line in assembly_result.error_lines:
                if error_line not in seen_error_lines:
                    seen_error_lines.add(error_line)
                    all_error_lines.append(error_line)
        for error_line in all_error_lines[:20]:
            report_lines.append(f"  {error_line}")

    return "\n".join(report_lines)
