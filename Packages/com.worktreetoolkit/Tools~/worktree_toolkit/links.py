"""Directory links (NTFS junctions on Windows, symlinks elsewhere) and the only safe way to delete them.

`git worktree remove --force` deletes the contents of every junction inside a worktree (probe P5,
git 2.43.0.windows.1), so every link must be removed with remove_directory_link before git runs.
"""
from __future__ import annotations

import os
from typing import List

from worktree_toolkit import git_runner
from worktree_toolkit.errors import RefusedError

_FILE_ATTRIBUTE_REPARSE_POINT = 0x400


def is_directory_link(path: str) -> bool:
    if os.path.islink(path):
        return True
    is_junction = getattr(os.path, "isjunction", None)
    if is_junction is not None:
        return bool(is_junction(path))
    try:
        file_attributes = getattr(os.lstat(path), "st_file_attributes", 0)
    except OSError:
        return False
    return bool(file_attributes & _FILE_ATTRIBUTE_REPARSE_POINT)


def create_directory_link(link_path: str, target_path: str) -> None:
    if os.path.lexists(link_path):
        raise RefusedError("cannot link '{0}': something already exists there".format(link_path))
    if not os.path.isdir(target_path):
        raise RefusedError("cannot link to '{0}': not a directory".format(target_path))
    os.makedirs(os.path.dirname(os.path.abspath(link_path)), exist_ok=True)
    if os.name == "nt":
        import _winapi  # Junctions need no admin rights, unlike directory symlinks.
        _winapi.CreateJunction(os.path.abspath(target_path), os.path.abspath(link_path))
    else:
        os.symlink(os.path.abspath(target_path), link_path, target_is_directory=True)


def remove_directory_link(link_path: str) -> None:
    if not is_directory_link(link_path):
        raise RefusedError("refusing to remove '{0}': it is not a directory link".format(link_path))
    if os.name == "nt":
        os.rmdir(link_path)  # Removes the reparse point only; the target's contents are untouched.
    else:
        os.unlink(link_path)


def find_directory_links(root_path: str) -> List[str]:
    """Every directory link under root_path; never descends into a link, so a target's own links are not reported."""
    found_links: List[str] = []
    pending_directories: List[str] = [root_path]
    while pending_directories:
        directory_path = pending_directories.pop()
        try:
            directory_entries = list(os.scandir(directory_path))
        except OSError:
            continue
        for directory_entry in directory_entries:
            if is_directory_link(directory_entry.path):
                found_links.append(git_runner.normalize_path(directory_entry.path))
            elif directory_entry.is_dir(follow_symlinks=False):
                pending_directories.append(directory_entry.path)
    return sorted(found_links)


def untracked_package_directories(stage_path: str) -> List[str]:
    """'Packages/<name>' folders holding a package.json that git ignores and does not track (e.g. Rive)."""
    packages_path = os.path.join(stage_path, "Packages")
    if not os.path.isdir(packages_path):
        return []
    package_directories: List[str] = []
    for package_name in sorted(os.listdir(packages_path)):
        package_path = os.path.join(packages_path, package_name)
        if is_directory_link(package_path) or not os.path.isfile(os.path.join(package_path, "package.json")):
            continue
        relative_path = "Packages/" + package_name
        is_ignored = git_runner.run_git(["check-ignore", "-q", relative_path + "/package.json"], stage_path, check=False).exit_code == 0
        is_tracked = git_runner.run_git(["ls-files", "--", relative_path], stage_path).standard_output.strip() != ""
        if is_ignored and not is_tracked:
            package_directories.append(relative_path)
    return package_directories
