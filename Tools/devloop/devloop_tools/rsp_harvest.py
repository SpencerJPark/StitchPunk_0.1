from __future__ import annotations

import glob
import os
from dataclasses import dataclass


@dataclass
class HarvestedCompile:
    assembly_name: str
    source_rsp_path: str
    flag_lines: list[str]
    source_files: list[str]


@dataclass
class StalenessVerdict:
    is_stale: bool
    reason: str


def find_dag_directory(project_root: str) -> str:
    search_pattern: str = os.path.join(project_root, "Library", "Bee", "artifacts", "*.dag")
    dag_directories: list[str] = glob.glob(search_pattern)
    if not dag_directories:
        raise FileNotFoundError(
            f"No Library/Bee/artifacts/*.dag directory found under {project_root!r}; "
            "reopen the Unity Editor once so it writes compile arguments."
        )
    # More than one dag directory can exist across Unity versions; the newest one is the live build graph.
    dag_directories.sort(key=os.path.getmtime, reverse=True)
    return dag_directories[0]


def _find_response_file_path(dag_directory: str, assembly_name: str) -> str:
    # Match "<AssemblyName>.rsp" exactly: the same dir also holds "<AssemblyName>.mvfrm.rsp", a different, wrong file.
    response_file_path: str = os.path.join(dag_directory, f"{assembly_name}.rsp")
    if not os.path.isfile(response_file_path):
        raise FileNotFoundError(
            f"No response file {response_file_path!r} found; "
            "reopen the Unity Editor once so it writes compile arguments."
        )
    return response_file_path


def _rewrite_output_flag_line(flag_line: str, output_directory: str) -> str:
    flag_name, quoted_original_path = flag_line.split(":", 1)
    original_path: str = quoted_original_path.strip().strip('"')
    original_filename: str = os.path.basename(original_path)
    # Keep the filename exactly: renaming the output assembly changes its identity, so
    # InternalsVisibleTo("DotsAnimationToolkit.Editor") stops matching and phantom CS0122 errors appear.
    rewritten_path: str = os.path.join(output_directory, original_filename).replace("\\", "/")
    return f'{flag_name}:"{rewritten_path}"'


def _sibling_assembly_name_from_reference_flag(flag_line: str) -> str | None:
    """The bare assembly name a `-r:` flag points at (basename, minus .ref.dll/.dll), or None for any other flag."""
    if not flag_line.startswith("-r:"):
        return None
    quoted_path: str = flag_line[len("-r:"):].strip()
    reference_filename: str = os.path.basename(quoted_path.strip('"'))
    for dll_suffix in (".ref.dll", ".dll"):
        if reference_filename.endswith(dll_suffix):
            return reference_filename[: -len(dll_suffix)]
    return reference_filename


def referenced_sibling_assembly_names(flag_lines: list[str]) -> set[str]:
    """Every assembly name this compile's `-r:` flags point at, keyed by basename (siblings and packages alike)."""
    sibling_assembly_names: set[str] = set()
    for flag_line in flag_lines:
        sibling_assembly_name: str | None = _sibling_assembly_name_from_reference_flag(flag_line)
        if sibling_assembly_name is not None:
            sibling_assembly_names.add(sibling_assembly_name)
    return sibling_assembly_names


def redirect_sibling_references(
    flag_lines: list[str], fresh_sibling_ref_dll_paths: dict[str, str]
) -> list[str]:
    # Unity's harvested rsp references a sibling assembly's Library/Bee .ref.dll, which reflects
    # whatever the Editor last built, not edits made in this gate run — compiling against it is
    # exactly the false pass (or phantom failure) this gate exists to catch.
    redirected_flag_lines: list[str] = []
    for flag_line in flag_lines:
        sibling_assembly_name: str | None = _sibling_assembly_name_from_reference_flag(flag_line)
        fresh_ref_dll_path: str | None = (
            fresh_sibling_ref_dll_paths.get(sibling_assembly_name) if sibling_assembly_name else None
        )
        if fresh_ref_dll_path is None:
            redirected_flag_lines.append(flag_line)
        else:
            redirected_flag_lines.append(f'-r:"{fresh_ref_dll_path}"')
    return redirected_flag_lines


def harvest(
    project_root: str,
    assembly_name: str,
    source_roots: list[str],
    output_directory: str,
) -> HarvestedCompile:
    dag_directory: str = find_dag_directory(project_root)
    response_file_path: str = _find_response_file_path(dag_directory, assembly_name)

    with open(response_file_path, "r", encoding="utf-8") as response_file:
        raw_lines: list[str] = [line.rstrip("\r\n") for line in response_file if line.strip()]

    flag_lines: list[str] = []
    for raw_line in raw_lines:
        if raw_line.endswith('.cs"'):
            # Discard Unity's source list entirely; it is re-globbed below so a file just added is gated.
            continue
        if raw_line.startswith("-out:") or raw_line.startswith("-refout:"):
            flag_lines.append(_rewrite_output_flag_line(raw_line, output_directory))
        else:
            flag_lines.append(raw_line)

    source_files: list[str] = []
    for source_root in source_roots:
        absolute_source_root: str = os.path.join(project_root, source_root)
        for walked_directory, subdirectory_names, file_names in os.walk(absolute_source_root):
            # A nested .asmdef carves its subtree out of this assembly, exactly as Unity does. Without
            # this, StitchPunk.Tests swallows Tests/PlayMode and fails on types it cannot legally see.
            subdirectory_names[:] = [
                subdirectory_name for subdirectory_name in subdirectory_names
                if not _directory_declares_its_own_assembly(os.path.join(walked_directory, subdirectory_name))
            ]
            for file_name in file_names:
                if file_name.endswith(".cs"):
                    absolute_source_path: str = os.path.join(walked_directory, file_name).replace("\\", "/")
                    source_files.append(absolute_source_path)
    source_files.sort()

    return HarvestedCompile(
        assembly_name=assembly_name,
        source_rsp_path=response_file_path,
        flag_lines=flag_lines,
        source_files=source_files,
    )


def is_stale(project_root: str, assembly_name: str, asmdef_paths: list[str]) -> StalenessVerdict:
    dag_directory: str = find_dag_directory(project_root)
    response_file_path: str = _find_response_file_path(dag_directory, assembly_name)
    response_file_mtime: float = os.path.getmtime(response_file_path)

    for asmdef_path in asmdef_paths:
        absolute_asmdef_path: str = os.path.join(project_root, asmdef_path)
        package_json_path: str = os.path.join(os.path.dirname(absolute_asmdef_path), "package.json")

        for watched_path in (absolute_asmdef_path, package_json_path):
            if not os.path.isfile(watched_path):
                continue
            if os.path.getmtime(watched_path) > response_file_mtime:
                # A false "fresh" verdict is worse than no gate at all: report stale on any ambiguity.
                return StalenessVerdict(
                    is_stale=True,
                    reason=f"{watched_path} is newer than harvested rsp {response_file_path}",
                )

    return StalenessVerdict(is_stale=False, reason="")


def _directory_declares_its_own_assembly(directory_path: str) -> bool:
    try:
        return any(entry.endswith(".asmdef") for entry in os.listdir(directory_path))
    except OSError:
        return False


def write_response_file(harvested: HarvestedCompile, response_file_path: str) -> None:
    output_lines: list[str] = list(harvested.flag_lines)
    for source_file in harvested.source_files:
        # Quote every path with " as Unity does, one source per line.
        output_lines.append(f'"{source_file}"')

    response_file_directory: str = os.path.dirname(response_file_path)
    if response_file_directory:
        os.makedirs(response_file_directory, exist_ok=True)

    with open(response_file_path, "w", encoding="utf-8", newline="\n") as response_file:
        response_file.write("\n".join(output_lines) + "\n")
