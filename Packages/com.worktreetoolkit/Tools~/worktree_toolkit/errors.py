"""Every failure the CLI reports maps to one exit code, so callers branch on the code, not the text."""
from __future__ import annotations

from typing import List


class ToolkitError(Exception):
    exit_code = 1


class UsageError(ToolkitError):
    exit_code = 1


class RefusedError(ToolkitError):
    """A precondition failed; nothing was changed."""

    exit_code = 2


class NoBrokerError(ToolkitError):
    exit_code = 3


class GateTimeoutError(ToolkitError):
    exit_code = 4


class GitCommandError(ToolkitError):
    exit_code = 5

    def __init__(self, arguments: List[str], git_exit_code: int, standard_error: str):
        self.arguments = list(arguments)
        self.git_exit_code = git_exit_code
        self.standard_error = standard_error
        super().__init__("git {0} failed ({1}): {2}".format(" ".join(arguments), git_exit_code, standard_error))
