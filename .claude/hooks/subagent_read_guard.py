"""PreToolUse guard for subagents: refuse whole-file reads of big files so one agent's context stays under ~100k.

Measured 2026-09-08: whole-file Read and Bash cat/sed reads put 15M of the 18M chars that entered subagent
contexts. Exit 2 blocks the call and feeds stderr back to the agent as the correction.
"""
import json
import os
import re
import sys

BIG_FILE_LINES = 300
MAX_RANGE_LINES = 250
SOURCE_EXT = r"\.(cs|md|py|js|ts|json|uss|uxml|asset|txt|yaml|yml|shader|hlsl|asmdef|cginc|tsv|csv)"
BINARY_EXT = (".png", ".jpg", ".jpeg", ".gif", ".pdf", ".ipynb")


def deny(message):
    sys.stderr.write("READ GUARD: " + message + "\n")
    sys.exit(2)


def count_lines(path):
    try:
        with open(path, "rb") as handle:
            return sum(1 for _ in handle)
    except OSError:
        return None


def check_read(tool_input, cwd):
    file_path = tool_input.get("file_path") or ""
    if file_path.lower().endswith(BINARY_EXT):
        return
    msys_drive = re.match(r"^/([a-zA-Z])/(.*)$", file_path)
    if msys_drive:
        file_path = msys_drive.group(1).upper() + ":/" + msys_drive.group(2)
    resolved = file_path if os.path.isabs(file_path) else os.path.join(cwd, file_path)
    line_count = count_lines(resolved)
    limit = tool_input.get("limit")
    if limit is not None and int(limit) > MAX_RANGE_LINES:
        deny("limit %s exceeds %d lines. Grep -n for the member you need and read at most %d lines around it."
             % (limit, MAX_RANGE_LINES, MAX_RANGE_LINES))
    if limit is None and line_count is not None and line_count > BIG_FILE_LINES:
        deny("%s is %d lines; whole-file reads are not allowed in a subagent. Grep -n for the member you need, "
             "then Read with offset and limit (max %d lines). Never re-read a range you already have."
             % (os.path.basename(file_path), line_count, MAX_RANGE_LINES))


SEGMENT_SPLIT = re.compile(r"&&|\|\||;|\n")
SEGMENT_HEAD = re.compile(r"^\s*(?:cd\s+(?:\"[^\"]*\"|'[^']*'|\S+)\s*)?(?P<cmd>[\w.\-]+)")
NUMERIC_RANGE = re.compile(r"(\d+)\s*,\s*(\d+|\$)\s*p")
PATTERN_RANGE = re.compile(r"/[^/]+/\s*,\s*/[^/]*/")
HEAD_TAIL_COUNT = re.compile(r"\b(?:head|tail)\s+(?:-n\s*|-c\s*|-)(\d+)")
DUMP_COMMANDS = ("cat", "type", "Get-Content", "gc", "bat", "less", "more")
FILTER_STAGE = re.compile(r"(head|tail|sed|grep|wc|awk|rg|cut|sort|uniq)\b")


def check_bash(tool_input):
    command = tool_input.get("command") or ""
    for segment in SEGMENT_SPLIT.split(command):
        redirect_free = segment.replace("2>", "").replace(">&", "")
        if "<<" in segment or ">" in redirect_free:
            continue  # heredoc or redirect: a write, not a read
        head = SEGMENT_HEAD.match(segment)
        first = head.group("cmd") if head else ""
        stages = [stage.strip() for stage in segment.split("|")]
        filtered = len(stages) > 1 and FILTER_STAGE.match(stages[1]) is not None
        reads_source_file = re.search(SOURCE_EXT + r"\b", stages[0]) is not None
        if first in DUMP_COMMANDS and reads_source_file and not filtered:
            deny("'%s' of a source file dumps the whole file into your context. Use Read with offset/limit "
                 "(max %d lines), or pipe through grep -n / sed -n with a numeric range." % (first, MAX_RANGE_LINES))
        if first == "git" and re.search(r"\bgit\s+show\s+\S*:", segment) and not filtered:
            deny("'git show REF:path' dumps a whole file. Pipe it through sed -n with a numeric range of at most "
                 "%d lines, or use git diff for what changed." % MAX_RANGE_LINES)
        for match in HEAD_TAIL_COUNT.finditer(segment):
            if int(match.group(1)) > MAX_RANGE_LINES:
                deny("head/tail of %s lines exceeds the %d-line cap for one read." % (match.group(1), MAX_RANGE_LINES))
        if re.search(r"\b(sed|awk)\b", segment):
            for match in NUMERIC_RANGE.finditer(segment):
                start, end = match.group(1), match.group(2)
                if end == "$" or int(end) - int(start) + 1 > MAX_RANGE_LINES:
                    deny("sed range %s,%s exceeds %d lines. Grep -n for the member first, then read at most "
                         "%d lines around it." % (start, end, MAX_RANGE_LINES, MAX_RANGE_LINES))
            if PATTERN_RANGE.search(segment) and reads_source_file:
                deny("pattern-to-pattern sed/awk ranges are unbounded. Grep -n for the two anchors, then use a "
                     "numeric range of at most %d lines." % MAX_RANGE_LINES)


def main():
    try:
        payload = json.load(sys.stdin)
    except Exception:
        sys.exit(0)
    tool_name = payload.get("tool_name")
    tool_input = payload.get("tool_input") or {}
    cwd = payload.get("cwd") or os.getcwd()
    if tool_name == "Read":
        check_read(tool_input, cwd)
    elif tool_name == "Bash":
        check_bash(tool_input)
    sys.exit(0)


if __name__ == "__main__":
    main()
