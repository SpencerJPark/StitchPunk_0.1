"""Assembly map for the four Unity packages the dev-loop gate and lint operate on.

Project-relative paths are resolved against this file's location, three parents
up (`Tools/devloop/devloop_tools/packages.py` -> repository root), so callers
work from any current working directory.
"""
from __future__ import annotations

import glob
import os
from dataclasses import dataclass

_REPOSITORY_ROOT: str = os.path.abspath(
    os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..")
)


@dataclass
class AssemblyTarget:
    assembly_name: str
    source_roots: list[str]  # project-relative dirs holding the .cs files
    asmdef_path: str  # project-relative


# Package name -> ordered list of (assembly_name, project_relative_source_root).
# Order within a package is dependency order: Runtime, Runtime.Physics, Authoring,
# Editor, then Tests, matching how the real asmdef references chain together.
_PACKAGE_ASSEMBLY_ROOTS: dict[str, list[tuple[str, str]]] = {
    "com.dotsanimationtoolkit": [
        ("DotsAnimationToolkit.Runtime", "Packages/com.dotsanimationtoolkit/Runtime"),
        ("DotsAnimationToolkit.Runtime.Physics", "Packages/com.dotsanimationtoolkit/Runtime.Physics"),
        ("DotsAnimationToolkit.Authoring", "Packages/com.dotsanimationtoolkit/Authoring"),
        ("DotsAnimationToolkit.Editor", "Packages/com.dotsanimationtoolkit/Editor"),
        ("DotsAnimationToolkit.Tests.EditMode", "Packages/com.dotsanimationtoolkit/Tests/EditMode"),
        ("DotsAnimationToolkit.Tests.PlayMode", "Packages/com.dotsanimationtoolkit/Tests/PlayMode"),
    ],
    "com.dotsmovementtoolkit": [
        ("DotsMovementToolkit.Runtime", "Packages/com.dotsmovementtoolkit/Runtime"),
        ("DotsMovementToolkit.Authoring", "Packages/com.dotsmovementtoolkit/Authoring"),
        ("DotsMovementToolkit.Tests.EditMode", "Packages/com.dotsmovementtoolkit/Tests/EditMode"),
    ],
    "com.worktreetoolkit": [
        ("WorktreeToolkit.Editor", "Packages/com.worktreetoolkit/Editor"),
        ("WorktreeToolkit.Tests.Editor", "Packages/com.worktreetoolkit/Tests/Editor"),
    ],
    "playtest-copilot": [
        ("PlaytestCopilot.Runtime", "Packages/playtest-copilot/Runtime"),
        ("PlaytestCopilot.Editor", "Packages/playtest-copilot/Editor"),
        ("PlaytestCopilot.Tests.Editor", "Packages/playtest-copilot/Tests/Editor"),
        ("PlaytestCopilot.Tests.Runtime", "Packages/playtest-copilot/Tests/Runtime"),
    ],
    # The game's own assemblies, so an edit under Assets/_Scripts can be gated too. The order is a
    # real topological sort of the asmdef references (which are GUID-form, not names) - it has to be,
    # because an assembly compiled before its dependency would link the stale Bee copy instead.
    "game": [
        ("StitchPunk.Core", "Assets/_Scripts/Core"),
        ("StitchPunk.Data", "Assets/_Scripts/Data"),
        ("StitchPunk.Components", "Assets/_Scripts/Components"),
        ("StitchPunk.Utils", "Assets/_Scripts/Utils"),
        ("StitchPunk.Authoring", "Assets/_Scripts/Authoring"),
        ("StitchPunk.Systems", "Assets/_Scripts/Systems"),
        ("StitchPunk.MonoBehaviours", "Assets/_Scripts/MonoBehaviours"),
        ("StitchPunk.UI", "Assets/_Scripts/UI"),
        ("StitchPunk.Editor", "Assets/_Scripts/Editor"),
        ("StitchPunk.Tests", "Assets/_Scripts/Tests"),
        ("StitchPunk.Tests.PlayMode", "Assets/_Scripts/Tests/PlayMode"),
    ],
}


def _resolve_asmdef_path(source_root: str) -> str:
    """Globs for the single .asmdef in a source root rather than assuming its filename matches the assembly name."""
    absolute_source_root: str = os.path.join(_REPOSITORY_ROOT, source_root)
    matches: list[str] = glob.glob(os.path.join(absolute_source_root, "*.asmdef"))
    if len(matches) != 1:
        raise FileNotFoundError(
            f"expected exactly one .asmdef in {source_root}, found {len(matches)}: {matches}"
        )
    relative_asmdef_path: str = os.path.relpath(matches[0], _REPOSITORY_ROOT)
    return relative_asmdef_path.replace(os.sep, "/")


def _build_target(assembly_name: str, source_root: str) -> AssemblyTarget:
    return AssemblyTarget(
        assembly_name=assembly_name,
        source_roots=[source_root],
        asmdef_path=_resolve_asmdef_path(source_root),
    )


def known_names() -> list[str]:
    """Every package folder name and every individual assembly name this map recognizes."""
    names: list[str] = list(_PACKAGE_ASSEMBLY_ROOTS.keys())
    for assembly_roots in _PACKAGE_ASSEMBLY_ROOTS.values():
        names.extend(assembly_name for assembly_name, _ in assembly_roots)
    return names


def targets_for(package_or_assembly: str) -> list[AssemblyTarget]:
    """Resolves a package folder name to all its assemblies (dependency order), or a single assembly name to one."""
    if package_or_assembly in _PACKAGE_ASSEMBLY_ROOTS:
        return [
            _build_target(assembly_name, source_root)
            for assembly_name, source_root in _PACKAGE_ASSEMBLY_ROOTS[package_or_assembly]
        ]

    for assembly_roots in _PACKAGE_ASSEMBLY_ROOTS.values():
        for assembly_name, source_root in assembly_roots:
            if assembly_name == package_or_assembly:
                return [_build_target(assembly_name, source_root)]

    raise KeyError(
        f"unknown package or assembly name {package_or_assembly!r}; known names: {known_names()}"
    )
