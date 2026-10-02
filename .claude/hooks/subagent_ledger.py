"""Measure a finished subagent's transcript and append one line to .claude/subagent-ledger.tsv.

Runs as the SubagentStop hook (natural stops) and is imported by agent_result_ledger.py (PostToolUse on Agent),
which also covers agents the harness cut off at maxTurns. Never blocks anything: always exit 0.
"""
import collections
import datetime
import json
import os
import re
import sys

BUDGET_OK = 100_000
BUDGET_HARD = 150_000
LEDGER_HEADER = "time\tsession\tagent_id\tagent_type\tmodel\tpeak_tokens\tturns\treads\tguard_denials\ttop_tools\tverdict\n"

# The real SubagentStop payload carries the transcript under the standard Claude Code hook field
# "transcript_path"; "agent_transcript_path" never existed, so every SubagentStop row was silently dropped.
TRANSCRIPT_PATH_KEYS = ("transcript_path", "agent_transcript_path")
AGENT_ID_FROM_FILENAME_PATTERN = re.compile(r"agent-([0-9a-f]{6,})\.jsonl$", re.IGNORECASE)
ERROR_LOG_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "ledger-errors.log")


def log_error(source, exc):
    # The error log must never itself be the thing that crashes a "never blocks anything" hook.
    try:
        with open(ERROR_LOG_PATH, "a", encoding="utf-8") as handle:
            handle.write("%s\t%s\t%s: %s\n" % (
                datetime.datetime.now().isoformat(timespec="seconds"), source, type(exc).__name__, exc))
    except Exception:
        pass


def measure(transcript_path):
    peak = 0
    turns = 0
    model = ""
    tool_calls = collections.Counter()
    denials = 0
    with open(transcript_path, encoding="utf-8", errors="replace") as handle:
        for line in handle:
            try:
                record = json.loads(line)
            except ValueError:
                continue
            message = record.get("message") or {}
            content = message.get("content")
            if message.get("role") == "assistant":
                usage = message.get("usage") or {}
                context = (usage.get("input_tokens", 0) + usage.get("cache_read_input_tokens", 0)
                           + usage.get("cache_creation_input_tokens", 0))
                if context:
                    peak = max(peak, context)
                    turns += 1
                model = message.get("model") or model
                if isinstance(content, list):
                    for block in content:
                        if isinstance(block, dict) and block.get("type") == "tool_use":
                            tool_calls[block.get("name", "?")] += 1
            elif message.get("role") == "user" and isinstance(content, list):
                for block in content:
                    if isinstance(block, dict) and block.get("type") == "tool_result":
                        body = block.get("content")
                        if isinstance(body, list):
                            text = " ".join(item.get("text", "") for item in body if isinstance(item, dict))
                        else:
                            text = body if isinstance(body, str) else ""
                        if "READ GUARD:" in text:
                            denials += 1
    return {
        "model": model,
        "peak": peak,
        "turns": turns,
        "reads": tool_calls.get("Read", 0),
        "denials": denials,
        "top_tools": ",".join("%sx%d" % (name, count) for name, count in tool_calls.most_common(4)),
        "verdict": "OK" if peak <= BUDGET_OK else ("HIGH" if peak <= BUDGET_HARD else "OVER"),
    }


def ledger_path_for(cwd):
    return os.path.join(cwd or os.getcwd(), ".claude", "subagent-ledger.tsv")


def read_rows(ledger_path):
    if not os.path.exists(ledger_path):
        return []
    with open(ledger_path, encoding="utf-8") as handle:
        lines = [line.rstrip("\n") for line in handle if line.strip()]
    if len(lines) < 2:
        return []
    header = lines[0].split("\t")
    return [dict(zip(header, line.split("\t"))) for line in lines[1:]]


def append_row(ledger_path, session_id, agent_id, agent_type, stats):
    is_new = not os.path.exists(ledger_path)
    with open(ledger_path, "a", encoding="utf-8") as handle:
        if is_new:
            handle.write(LEDGER_HEADER)
        handle.write("\t".join([
            datetime.datetime.now().isoformat(timespec="seconds"),
            (session_id or "")[:8],
            agent_id or "",
            agent_type or "",
            stats["model"],
            str(stats["peak"]),
            str(stats["turns"]),
            str(stats["reads"]),
            str(stats["denials"]),
            stats["top_tools"],
            stats["verdict"],
        ]) + "\n")


def transcript_path_from_payload(payload):
    for key in TRANSCRIPT_PATH_KEYS:
        value = payload.get(key)
        if value:
            return value
    return None


def agent_id_from_payload(payload, transcript_path):
    # The transcript filename ("agent-<id>.jsonl") wins over the payload's agent_id, which was observed
    # to carry a different identifier with no transcript of its own - that mislabels the row and makes
    # dedup miss, so the id must name whatever was actually measured.
    agent_id = ""
    if transcript_path:
        name_match = AGENT_ID_FROM_FILENAME_PATTERN.search(os.path.basename(transcript_path))
        if name_match:
            agent_id = name_match.group(1)
    return agent_id or payload.get("agent_id") or ""


def subagents_directory_from_session_transcript(session_transcript_path):
    # Real subagent transcripts live beside the session transcript: ".../<session-uuid>.jsonl" ->
    # ".../<session-uuid>/subagents/agent-<id>.jsonl". Derived, never hardcoded.
    if not session_transcript_path or not session_transcript_path.endswith(".jsonl"):
        return None
    return os.path.join(session_transcript_path[: -len(".jsonl")], "subagents")


def is_subagent_transcript_path(path):
    return bool(path) and os.path.basename(os.path.dirname(path)) == "subagents" \
        and AGENT_ID_FROM_FILENAME_PATTERN.search(os.path.basename(path)) is not None


def select_real_subagent_transcript(payload, session_transcript_path, recorded_agent_ids):
    # SubagentStop's "transcript_path" is always the ORCHESTRATOR's own transcript, never the
    # subagent that just stopped (that mismatch is what wrote 189k-token "worker" rows). If the
    # payload names the agent directly, trust that; otherwise fall back to the most recently
    # modified subagent transcript not already recorded.
    directory = subagents_directory_from_session_transcript(session_transcript_path)
    if not directory or not os.path.isdir(directory):
        return None
    explicit_agent_id = payload.get("agent_id") or payload.get("agentId")
    if explicit_agent_id:
        candidate_path = os.path.join(directory, "agent-%s.jsonl" % explicit_agent_id)
        if os.path.exists(candidate_path):
            return candidate_path
    candidates = []
    for file_name in os.listdir(directory):
        if not (file_name.startswith("agent-") and file_name.endswith(".jsonl")):
            continue
        name_match = AGENT_ID_FROM_FILENAME_PATTERN.search(file_name)
        if name_match and name_match.group(1) not in recorded_agent_ids:
            candidates.append(os.path.join(directory, file_name))
    if not candidates:
        return None
    candidates.sort(key=os.path.getmtime, reverse=True)
    return candidates[0]


def agent_type_from_meta(transcript_path):
    meta_path = re.sub(r"\.jsonl$", ".meta.json", transcript_path)
    if not os.path.exists(meta_path):
        return ""
    try:
        with open(meta_path, encoding="utf-8") as handle:
            meta = json.load(handle)
        return meta.get("agentType") or ""
    except Exception:
        return ""


def main():
    try:
        payload = json.load(sys.stdin)
    except Exception:
        sys.exit(0)
    try:
        session_transcript_path = transcript_path_from_payload(payload)
        if not session_transcript_path or not os.path.exists(session_transcript_path):
            sys.exit(0)
        ledger_path = ledger_path_for(payload.get("cwd"))
        # Only a row that actually carries metrics counts as recorded. A zero row - written by an older
        # build of the PostToolUse hook at launch time, before the agent had run - must never claim an
        # agent id, or it blocks the real measurement forever and the ledger stays empty.
        recorded_agent_ids = {
            row.get("agent_id") for row in read_rows(ledger_path) if (row.get("peak_tokens") or "0") != "0"
        }

        # Guard against re-measuring the orchestrator's own transcript under a subagent's name:
        # a row attributing parent tokens/turns to a worker is worse than no row, since the ledger
        # drives how future work gets split.
        transcript_path = select_real_subagent_transcript(payload, session_transcript_path, recorded_agent_ids)
        if not is_subagent_transcript_path(transcript_path):
            log_error("subagent_ledger.main", Exception(
                "refusing to measure non-subagent transcript for session_transcript_path=%r (resolved=%r)"
                % (session_transcript_path, transcript_path)))
            sys.exit(0)

        agent_id = agent_id_from_payload(payload, transcript_path)
        if agent_id in recorded_agent_ids:
            sys.exit(0)
        agent_type = payload.get("agent_type") or agent_type_from_meta(transcript_path)
        append_row(ledger_path, payload.get("session_id"), agent_id, agent_type, measure(transcript_path))
    except SystemExit:
        raise
    except Exception as exc:
        log_error("subagent_ledger.main", exc)
    sys.exit(0)


if __name__ == "__main__":
    main()
