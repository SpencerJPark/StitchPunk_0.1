"""PostToolUse hook on the Agent tool: measure the finished subagent and hand the orchestrator its ledger line.

Self-sufficient on purpose: SubagentStop does not fire when the harness cuts an agent off at maxTurns (observed
2026-09-08), so this hook locates the transcript from the agentId in the structured tool response and measures it
directly, recording a ledger row if SubagentStop did not already.
"""
import json
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from subagent_ledger import append_row, ledger_path_for, measure, read_rows  # noqa: E402

AGENT_ID_TEXT_PATTERN = re.compile(r"agentId\W{1,4}([0-9a-f]{8,})")
TURN_LIMIT_PATTERN = re.compile(r"stopped at its (\d+)-turn limit")


def project_slug(cwd):
    return re.sub(r"[^A-Za-z0-9]", "-", cwd)


def find_transcript(cwd, session_id, agent_id):
    project_dir = os.path.join(os.path.expanduser("~"), ".claude", "projects", project_slug(cwd))
    file_name = "agent-%s.jsonl" % agent_id
    direct = os.path.join(project_dir, session_id or "", "subagents", file_name)
    if os.path.exists(direct):
        return direct
    for root, directories, files in os.walk(project_dir):
        if file_name in files:
            return os.path.join(root, file_name)
    return None


def response_text_of(tool_response):
    if isinstance(tool_response, dict):
        blocks = tool_response.get("content")
        if isinstance(blocks, list):
            return " ".join(block.get("text", "") for block in blocks if isinstance(block, dict))
        if isinstance(blocks, str):
            return blocks
    return json.dumps(tool_response)


def main():
    try:
        payload = json.load(sys.stdin)
    except Exception:
        sys.exit(0)
    if payload.get("tool_name") != "Agent":
        sys.exit(0)
    cwd = payload.get("cwd") or os.getcwd()
    tool_response = payload.get("tool_response")
    text = response_text_of(tool_response)
    agent_id = tool_response.get("agentId") if isinstance(tool_response, dict) else None
    if not agent_id:
        text_match = AGENT_ID_TEXT_PATTERN.search(json.dumps(tool_response))
        agent_id = text_match.group(1) if text_match else None
    if not agent_id:
        sys.exit(0)
    agent_type = (tool_response.get("agentType") if isinstance(tool_response, dict) else None) \
        or (payload.get("tool_input") or {}).get("subagent_type") or "general-purpose"
    ledger_path = ledger_path_for(cwd)
    row = next((existing for existing in read_rows(ledger_path) if existing.get("agent_id") == agent_id), None)
    if row is None:
        transcript_path = find_transcript(cwd, payload.get("session_id"), agent_id)
        if transcript_path is None:
            sys.exit(0)
        stats = measure(transcript_path)
        append_row(ledger_path, payload.get("session_id"), agent_id, agent_type, stats)
        row = dict(agent_type=agent_type, agent_id=agent_id, peak_tokens=str(stats["peak"]), turns=str(stats["turns"]),
                   reads=str(stats["reads"]), guard_denials=str(stats["denials"]), top_tools=stats["top_tools"], verdict=stats["verdict"])
    capped = TURN_LIMIT_PATTERN.search(text)
    summary = ("Subagent ledger [%s]: %s agent %s peaked at %dk tokens over %s turns, %s Reads, %s read-guard denials, tools %s."
               % (row["verdict"], row["agent_type"], row["agent_id"], int(row["peak_tokens"]) // 1000, row["turns"],
                  row["reads"], row["guard_denials"], row["top_tools"]))
    if capped:
        summary += (" It hit the %s-turn cap: the task was scoped too large. Do NOT SendMessage it to continue (that grows the same context);"
                    " read what it did (git diff), then spawn a fresh worker with only the remaining scope." % capped.group(1))
    elif row["verdict"] != "OK":
        summary += " The task was scoped too large: split the next brief in this family into smaller fresh agents."
    print(json.dumps({"hookSpecificOutput": {"hookEventName": "PostToolUse", "additionalContext": summary}}))
    sys.exit(0)


if __name__ == "__main__":
    main()
