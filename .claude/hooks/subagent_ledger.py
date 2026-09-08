"""Measure a finished subagent's transcript and append one line to .claude/subagent-ledger.tsv.

Runs as the SubagentStop hook (natural stops) and is imported by agent_result_ledger.py (PostToolUse on Agent),
which also covers agents the harness cut off at maxTurns. Never blocks anything: always exit 0.
"""
import collections
import datetime
import json
import os
import sys

BUDGET_OK = 100_000
BUDGET_HARD = 150_000
LEDGER_HEADER = "time\tsession\tagent_id\tagent_type\tmodel\tpeak_tokens\tturns\treads\tguard_denials\ttop_tools\tverdict\n"


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


def main():
    try:
        payload = json.load(sys.stdin)
    except Exception:
        sys.exit(0)
    transcript_path = payload.get("agent_transcript_path")
    if not transcript_path or not os.path.exists(transcript_path):
        sys.exit(0)
    ledger_path = ledger_path_for(payload.get("cwd"))
    agent_id = payload.get("agent_id") or ""
    if any(row.get("agent_id") == agent_id for row in read_rows(ledger_path)):
        sys.exit(0)
    append_row(ledger_path, payload.get("session_id"), agent_id, payload.get("agent_type"), measure(transcript_path))
    sys.exit(0)


if __name__ == "__main__":
    main()
