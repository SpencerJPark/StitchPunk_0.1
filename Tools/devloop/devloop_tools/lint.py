"""Offline, 2-second check of this repo's five MECHANICAL C# conventions, scoped to the diff.

Exists so a subagent's brief can say "run the lint" instead of carrying prose rules. Deliberately
covers only mechanical conventions a regex can see — the rest of the style guide is pixel geometry
already covered by an EditMode test, and a lint that reports things the project does not actually
forbid trains people to ignore it.
"""

from __future__ import annotations

import os
import re
import subprocess
from dataclasses import dataclass, field

# Matches `var` only as a whole word, never inside `variable`, `varying`, or `advar`. `var` is
# banned so every declaration states its explicit type — names and types are the documentation.
_VAR_KEYWORD_PATTERN: re.Pattern[str] = re.compile(r"\bvar\b")

# Local/field/parameter declarations: a type token followed by a single-letter identifier, then a
# character that cannot continue an identifier (so `T value` passes but `T v` does not). The type
# token excludes a bare `<` so this does not also match a generic type parameter list like `Foo<T>`.
# Narrowed scope: this still flags single-letter parameters, fields, and general locals (e.g.
# `int n = 5;`) — the real defects — but two idioms the codebase universally and correctly uses are
# exempted below: `for` loop counters (`for (int i = 0; ...)`) and vector/quaternion component
# locals named `x`/`y`/`z`/`w` (e.g. `float x = rotation.x;`).
_SINGLE_LETTER_DECLARATION_PATTERN: re.Pattern[str] = re.compile(
    r"(?<![A-Za-z0-9_.])"
    r"[A-Za-z_][A-Za-z0-9_<>\[\],\.\s]*[A-Za-z0-9_>\]]"
    r"[ \t]+"
    r"(?P<name>[A-Za-z])"
    r"(?=[ \t]*[=;,)\]])"
)

# A single-letter generic type parameter declaration, e.g. `class Foo<T>` or `void Bar<T>(...)`;
# these are intentionally exempt from no-single-letter-names and must not be flagged.
_GENERIC_TYPE_PARAMETER_DECLARATION_PATTERN: re.Pattern[str] = re.compile(
    r"<\s*[A-Za-z](\s*,\s*[A-Za-z])*\s*>"
)

# The initializer clause of a `for` loop, e.g. `int i = 0, j = 1` in `for (int i = 0, j = 1; ...)`;
# single-letter counters declared here are exempt from no-single-letter-names.
_FOR_LOOP_INITIALIZER_PATTERN: re.Pattern[str] = re.compile(r"\bfor\b\s*\(([^;]*);")

# Single-letter local names that mirror a vector/quaternion component being read or written
# (`rotation.x`, `direction.y`, ...); renaming these to something longer would only make the code
# read worse, so they are exempt from no-single-letter-names wherever they are declared.
_VECTOR_OR_QUATERNION_COMPONENT_NAMES: frozenset[str] = frozenset({"x", "y", "z", "w"})

# `.Run()` on a job is banned outright; jobs must `.Schedule()` / `.ScheduleParallel()` into
# `state.Dependency` so Burst can actually parallelize instead of blocking the main thread.
# The receiver separates a job from an ordinary static call: job instances are camelCase locals while
# types are PascalCase, so `HealthScan.Run(context)` is a static method and must not be reported.
_JOB_RUN_CALL_PATTERN: re.Pattern[str] = re.compile(r"([A-Za-z_][A-Za-z0-9_]*)\s*\.Run\s*\(")

# Captures an EnabledRefRW<Foo>/EnabledRefRO<Foo> parameter's component type and its parameter
# name, so the expected `fooEnabled` name can be derived and compared against what was written.
_ENABLED_REF_PARAMETER_PATTERN: re.Pattern[str] = re.compile(
    r"EnabledRef(?:RW|RO)\s*<\s*([A-Za-z_][A-Za-z0-9_]*)\s*>\s+([A-Za-z_][A-Za-z0-9_]*)"
)

# Package editor code must draw gizmo-style lines with meshes in duringSceneGui and pick with
# HandleUtility instead of immediate-mode Handles/OnGUI/GUILayout, per Conformance_E.
_PACKAGE_EDITOR_BANNED_API_PATTERN: re.Pattern[str] = re.compile(
    r"\bHandles\.|\bOnGUI\b|\bGUILayout\b"
)

_MAXIMUM_OFFENDING_LINE_LENGTH: int = 100
_MAXIMUM_FINDING_LINES_IN_REPORT: int = 40


@dataclass
class LintFinding:
    file_path: str
    line_number: int
    rule_id: str
    text: str


@dataclass
class LintReport:
    findings: list[LintFinding] = field(default_factory=list)
    files_checked: int = 0
    passed: bool = True


def _strip_comments_and_literals(source_line: str) -> str:
    """Blanks out string/char literals and `//` line comments so they cannot trigger a false hit.

    A verbatim `@"..."` or interpolated `$"..."` literal is blanked the same as a plain string;
    this is a single-line pass, so a literal or comment spanning multiple lines is not tracked.
    """
    result_characters: list[str] = []
    character_index: int = 0
    line_length: int = len(source_line)
    while character_index < line_length:
        current_character: str = source_line[character_index]
        if current_character == "/" and character_index + 1 < line_length and source_line[character_index + 1] == "/":
            break
        if current_character in ("\"", "'"):
            quote_character: str = current_character
            literal_start_index: int = character_index
            character_index += 1
            while character_index < line_length:
                if source_line[character_index] == "\\" and quote_character == "\"":
                    character_index += 2
                    continue
                if source_line[character_index] == quote_character:
                    character_index += 1
                    break
                character_index += 1
            result_characters.append(" " * (character_index - literal_start_index))
            continue
        result_characters.append(current_character)
        character_index += 1
    return "".join(result_characters)


def _strip_block_comments(source_text: str) -> str:
    """Blanks `/* ... */` spans across the whole file, preserving line breaks for numbering."""
    return re.sub(
        r"/\*.*?\*/",
        lambda match: "\n" * match.group(0).count("\n"),
        source_text,
        flags=re.DOTALL,
    )


def _expected_enabled_parameter_name(component_type_name: str) -> str:
    """`Foo` -> `fooEnabled`: first letter lowercased, `Enabled` appended."""
    return component_type_name[0].lower() + component_type_name[1:] + "Enabled"


def _lint_file_text(file_path: str, source_text: str) -> list[LintFinding]:
    findings: list[LintFinding] = []
    is_package_editor_file: bool = bool(re.search(r"(^|[/\\])Packages[/\\][^/\\]+[/\\]Editor[/\\]", file_path))

    uncommented_text: str = _strip_block_comments(source_text)
    source_lines: list[str] = uncommented_text.splitlines()

    for line_index, raw_line in enumerate(source_lines):
        line_number: int = line_index + 1
        code_only_line: str = _strip_comments_and_literals(raw_line)
        offending_text: str = raw_line.strip()[:_MAXIMUM_OFFENDING_LINE_LENGTH]

        if _VAR_KEYWORD_PATTERN.search(code_only_line):
            findings.append(LintFinding(file_path, line_number, "no-var", offending_text))

        generic_parameter_spans: list[tuple[int, int]] = [
            match.span() for match in _GENERIC_TYPE_PARAMETER_DECLARATION_PATTERN.finditer(code_only_line)
        ]
        for_loop_initializer_spans: list[tuple[int, int]] = [
            match.span(1) for match in _FOR_LOOP_INITIALIZER_PATTERN.finditer(code_only_line)
        ]
        for match in _SINGLE_LETTER_DECLARATION_PATTERN.finditer(code_only_line):
            name_start, name_end = match.span("name")
            declared_name: str = match.group("name")
            inside_generic_parameter_list: bool = any(
                span_start <= name_start and name_end <= span_end for span_start, span_end in generic_parameter_spans
            )
            inside_for_loop_initializer: bool = any(
                span_start <= name_start and name_end <= span_end
                for span_start, span_end in for_loop_initializer_spans
            )
            is_vector_or_quaternion_component_name: bool = declared_name in _VECTOR_OR_QUATERNION_COMPONENT_NAMES
            if (
                not inside_generic_parameter_list
                and not inside_for_loop_initializer
                and not is_vector_or_quaternion_component_name
            ):
                findings.append(LintFinding(file_path, line_number, "no-single-letter-names", offending_text))

        for job_run_match in _JOB_RUN_CALL_PATTERN.finditer(code_only_line):
            if not job_run_match.group(1)[:1].isupper():
                findings.append(LintFinding(file_path, line_number, "no-run-on-jobs", offending_text))
                break

        for enabled_ref_match in _ENABLED_REF_PARAMETER_PATTERN.finditer(code_only_line):
            component_type_name, actual_parameter_name = enabled_ref_match.group(1), enabled_ref_match.group(2)
            expected_parameter_name: str = _expected_enabled_parameter_name(component_type_name)
            if actual_parameter_name != expected_parameter_name:
                findings.append(
                    LintFinding(
                        file_path,
                        line_number,
                        "enabled-ref-naming",
                        f"{offending_text}  (expected '{expected_parameter_name}')",
                    )
                )

        if is_package_editor_file and _PACKAGE_EDITOR_BANNED_API_PATTERN.search(code_only_line):
            findings.append(LintFinding(file_path, line_number, "no-handles-in-package-editor", offending_text))

    return findings


def _git_diff_scoped_csharp_files(project_root: str) -> list[str]:
    """Union of unstaged and staged changed paths against HEAD, filtered to existing `.cs` files."""
    changed_paths: set[str] = set()
    for diff_arguments in (["git", "diff", "--name-only", "HEAD"], ["git", "diff", "--cached", "--name-only"]):
        completed_process = subprocess.run(
            diff_arguments, cwd=project_root, capture_output=True, text=True, check=False
        )
        for relative_path in completed_process.stdout.splitlines():
            if relative_path:
                changed_paths.add(relative_path)

    existing_csharp_files: list[str] = []
    for relative_path in sorted(changed_paths):
        if relative_path.endswith(".cs") and os.path.isfile(os.path.join(project_root, relative_path)):
            existing_csharp_files.append(relative_path)
    return existing_csharp_files


def _walk_csharp_files_under(project_root: str, relative_directory: str) -> list[str]:
    discovered_files: list[str] = []
    absolute_directory: str = os.path.join(project_root, relative_directory)
    for directory_path, _subdirectory_names, file_names in os.walk(absolute_directory):
        for file_name in file_names:
            if file_name.endswith(".cs"):
                absolute_file_path: str = os.path.join(directory_path, file_name)
                discovered_files.append(os.path.relpath(absolute_file_path, project_root))
    return discovered_files


def lint_paths(project_root: str, paths: list[str]) -> LintReport:
    """Lints the given paths (files or directories), or the current git diff when `paths` is empty."""
    resolved_relative_paths: list[str]
    if paths:
        resolved_relative_paths = []
        for requested_path in paths:
            absolute_requested_path: str = (
                requested_path if os.path.isabs(requested_path) else os.path.join(project_root, requested_path)
            )
            if os.path.isdir(absolute_requested_path):
                relative_directory: str = os.path.relpath(absolute_requested_path, project_root)
                resolved_relative_paths.extend(_walk_csharp_files_under(project_root, relative_directory))
            elif absolute_requested_path.endswith(".cs") and os.path.isfile(absolute_requested_path):
                resolved_relative_paths.append(os.path.relpath(absolute_requested_path, project_root))
    else:
        resolved_relative_paths = _git_diff_scoped_csharp_files(project_root)

    all_findings: list[LintFinding] = []
    for relative_file_path in resolved_relative_paths:
        absolute_file_path = os.path.join(project_root, relative_file_path)
        with open(absolute_file_path, "r", encoding="utf-8", errors="replace") as file_handle:
            source_text: str = file_handle.read()
        all_findings.extend(_lint_file_text(relative_file_path.replace(os.sep, "/"), source_text))

    return LintReport(
        findings=all_findings, files_checked=len(resolved_relative_paths), passed=not all_findings
    )


def format_report(report: LintReport) -> str:
    """Terse pass/fail line for a subagent report; at most one line per finding, capped at 40."""
    if report.passed:
        return f"LINT PASS  {report.files_checked} files"

    distinct_finding_file_paths: set[str] = {finding.file_path for finding in report.findings}
    findings_per_rule: dict[str, int] = {}
    for finding in report.findings:
        findings_per_rule[finding.rule_id] = findings_per_rule.get(finding.rule_id, 0) + 1
    rule_tally: str = ", ".join(
        f"{rule_id} {count}" for rule_id, count in sorted(findings_per_rule.items(), key=lambda pair: -pair[1])
    )
    report_lines: list[str] = [
        f"LINT FAIL  {len(report.findings)} findings in {len(distinct_finding_file_paths)} files",
        f"  by rule: {rule_tally}",
    ]
    for finding in report.findings[:_MAXIMUM_FINDING_LINES_IN_REPORT]:
        report_lines.append(f"{finding.file_path}:{finding.line_number}  {finding.rule_id}  {finding.text}")
    omitted_finding_count: int = len(report.findings) - _MAXIMUM_FINDING_LINES_IN_REPORT
    if omitted_finding_count > 0:
        # Truncating in silence reads as "that was all of them" - how a 141-finding run got mistaken for 40.
        report_lines.append(f"  ... {omitted_finding_count} more findings not shown")
    return "\n".join(report_lines)
