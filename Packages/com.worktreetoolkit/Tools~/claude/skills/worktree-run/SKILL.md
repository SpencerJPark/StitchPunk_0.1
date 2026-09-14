---
name: worktree-run
description: Run one or more specs in parallel git worktrees with a spec-lead each, then review on stage and merge — the Worktree Toolkit workflow.
---

Run each spec as its own isolated `spec-lead` agent in its own git worktree, then review and
merge the finished work back onto the stage (the trunk checkout).

## Preflight
Run `python Packages/com.worktreetoolkit/Tools~/worktree.py doctor --json`. Stop and tell the
owner before spawning anything if it reports a stage blocker, git older than 2.38, hooks not
installed (fix with `worktree.py install-claude`), or no broker running — with no broker, gate
requests come to you as messages instead of resolving on their own.

## Ask
For each spec, ask two `AskUserQuestion`s: "Lead model for <spec>?" and "Worker model for
<spec>?", options `opus` / `sonnet` / `haiku` / `fable` with the first marked recommended. One
`AskUserQuestion` call holds at most 4 questions, so batch at most two specs per call.

## Spawn
Spawn one `Agent` per spec: `subagent_type: spec-lead`, `model: <lead model>`, run in
background. Prompt = the spec id, the spec's file path, and the chosen worker model. Specs run
in parallel — do not wait on one lead before spawning the next.

## While running
If a broker is running, stay quiet — it resolves gates. With no broker, answer a lead's
"gate needed: <id> <sha> <fixtures>" message yourself, one gate at a time:
1. `python Packages/com.worktreetoolkit/Tools~/worktree.py stage-commit <sha> --holder "gate <id>"`
   (exit 2 = stage not safe: tell the owner, do not force it).
2. Unity MCP compile gate (refresh → wait for compile → read_console), then run the named
   fixtures only.
3. `python Packages/com.worktreetoolkit/Tools~/worktree.py restore-trunk` — always, pass or fail.
4. `SendMessage` the lead the verdict with the error lines verbatim.
Show progress with `python Packages/com.worktreetoolkit/Tools~/worktree.py list`.

## Review
When a lead reports `ready`, tell the owner. On the owner's "review <id>": stash the stage
only with the owner's explicit consent, then run
`python Packages/com.worktreetoolkit/Tools~/worktree.py review <id>`. When the owner is done
looking, run `python Packages/com.worktreetoolkit/Tools~/worktree.py return`.

## Merge
On the owner's "merge <id>": run
`python Packages/com.worktreetoolkit/Tools~/worktree.py merge <id>`, push trunk, then
`python Packages/com.worktreetoolkit/Tools~/worktree.py remove <id>`. On a conflict (exit 2
naming files), tell the owner, then spawn a `worker` — not a `spec-lead`, whose isolation would
create a new worktree — with the worktree's absolute path from `list --json`, to run
`git rebase <trunk>` there and resolve the named files; retry the merge after it commits.

## Never
- Never run `git worktree remove --force` by hand: git deletes the *contents* of every linked
  folder inside the worktree (e.g. the stage's gitignored packages). Only `worktree.py remove`,
  which unlinks first.
- Never call `AssetDatabase.SaveAssets` yourself.
- Never merge without the owner's explicit word, even if every gate passed.
