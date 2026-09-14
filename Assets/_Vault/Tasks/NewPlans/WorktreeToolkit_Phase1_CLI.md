# Worktree Toolkit — Phase 1 CLI build spec (pinned surfaces for parallel workers)

> Parent: [`WorktreeToolkit_Roadmap.md`](WorktreeToolkit_Roadmap.md) (read §4 decisions, §5 JSON, §7 traps only if a brief says so).
> Root: `Packages/com.worktreetoolkit/Tools~/`. Run fixtures from that folder:
> `python -m unittest tests.test_<module> -v`. No Unity involved.

## 1. Rules for every module

- Python ≥ 3.9 stdlib only, `from __future__ import annotations` first. **No single-letter names, no
  abbreviations** — names read like docs (repo RULES.md applies to Python too). Comments only for a
  *why*; module docstring one or two lines.
- Every git call goes through `git_runner.run_git` (or a helper in it). Never `subprocess` git directly.
- Failures raise from `worktree_toolkit.errors`: `UsageError` (1), `RefusedError` (2, precondition,
  **nothing changed**), `NoBrokerError` (3), `GateTimeoutError` (4), `GitCommandError` (5, raised by
  `run_git`). A refused command must leave git and state exactly as it found them.
- Public functions return a JSON-able `dict` with **camelCase** keys (the CLI prints it). State on
  disk stays snake_case (`state_store` dataclasses).
- Mutate state only inside `with state_store.StateLock(context.common_git_directory):` — load, change,
  save inside the block.
- Fixtures: `unittest`, one `TemporaryRepository()` per test in `setUp`, `cleanup()` in `tearDown`.
  **Only the tests listed for your module.** Each must fail if the behaviour it names is removed —
  check that by temporarily breaking the line, then restore it.

## 2. Already on disk — read-only for workers (signatures)

`errors.py` — the five classes above.

`git_runner.py`
```python
run_git(arguments: List[str], working_directory: str, check: bool = True) -> GitResult  # .exit_code .standard_output .standard_error
normalize_path(path) -> str ; paths_equal(first_path, second_path) -> bool
git_version() -> Tuple[int, int, int]
repository_root(wd) -> str ; git_directory(wd) -> str ; common_git_directory(wd) -> str
is_linked_worktree(wd) -> bool ; main_checkout_path(wd) -> str
list_worktrees(wd) -> List[WorktreeRecord]  # .path .head_sha .branch(short|None) .is_detached .is_locked .lock_reason .is_prunable .is_bare ; [0] is the stage
current_branch(wd) -> Optional[str] ; resolve_commit(wd, reference="HEAD") -> str ; branch_exists(wd, branch_name) -> bool
ahead_behind(wd, base_reference, branch_reference) -> Tuple[int, int]  # (ahead, behind) of branch vs base
merge_base(wd, first, second) -> Optional[str] ; is_ancestor(wd, ancestor, descendant) -> bool
tracked_modifications(wd) -> List[str]  # modified tracked paths, untracked excluded
dirty_entry_count(wd) -> int            # tracked + untracked entries
```

`state_store.py`
```python
PROTOCOL_VERSION = 1 ; LEAD_STATUSES = ("unclaimed","building","gating","ready","failed","done") ; WORKTREE_MODES = ("source-only","own-editor")
@dataclass LeadBinding(spec="", lead_model="", worker_model="", status="unclaimed")
@dataclass WorktreeEntry(worktree_id, path, branch, mode="source-only", parent_branch="main", parked=False, links=[], lead=None, created_utc="")
@dataclass ToolkitConfig(trunk_branch="main", stage_noise_globs=[], library_seed_exclusions=[...])
@dataclass ToolkitState(protocol=1, config, worktrees: Dict[str, WorktreeEntry], stage_stashes: List[str], stage_review_worktree_id: Optional[str])
utc_now_text() -> str ; validate_worktree_id(worktree_id) -> str ; sanitize_worktree_id(raw_name) -> str
state_directory(common) -> str  # creates queue/ results/ ; stage_lock_path(common) -> str
load_state(common) -> ToolkitState ; save_state(common, state) -> None
find_entry_by_path(state, path) -> Optional[WorktreeEntry] ; require_entry(state, worktree_id) -> WorktreeEntry  # RefusedError if missing
class StateLock(common, timeout_seconds=30.0)  # context manager
```

`context.py`
```python
@dataclass ToolkitContext(working_directory, repository_root, common_git_directory, stage_path, is_linked_worktree)
resolve_context(working_directory) -> ToolkitContext
require_stage(context) ; require_linked_worktree(context)   # RefusedError otherwise
filter_noise_paths(changed_paths, noise_globs) -> List[str]
entry_to_dictionary(entry) -> dict  # {id, branch, path, mode, parentBranch, parked, links, lead{spec,leadModel,workerModel,status}|None, createdUtc}
```

`links.py`
```python
is_directory_link(path) -> bool ; create_directory_link(link_path, target_path) ; remove_directory_link(link_path)  # RefusedError if not a link
find_directory_links(root_path) -> List[str]  # never descends into links
untracked_package_directories(stage_path) -> List[str]  # ['Packages/app.rive.rive-unity', ...]
```

`tests/repo_fixture.py` — `TemporaryRepository()`: `.root_directory`, `.stage_path` (branch `main`, one
commit), `.git(wd, *args) -> str`, `.write_file(wd, relative_path, content)`,
`.commit_file(wd, relative_path, content, message) -> sha`, `.worktree_path(id)`,
`.add_raw_worktree(id, branch_name=None, base_reference="main") -> path` (plain git, unregistered),
`.cleanup()`. Register a raw worktree in a test by writing a `WorktreeEntry` under `StateLock`.

## 3. Modules to build (wave 1 — independent of each other)

### 3.1 `graph_model.py` + `tests/test_graph_model.py`
```python
STALE_BEHIND_THRESHOLD = 50
directory_size_megabytes(root_path) -> int   # sum of file sizes, never follows links (links.is_directory_link), skips nothing else
build_graph(working_directory, include_sizes=False) -> dict
```
Output shape (works from the stage **or** a linked worktree):
```json
{ "protocol": 1, "trunk": "main",
  "stage": { "path": "…", "branch": "main|null", "detachedSha": "…|null", "dirtyTracked": 0,
             "busyWith": null, "reviewWorktreeId": null, "stashes": [] },
  "worktrees": [ { "id": "…", "registered": true, "branch": "…|null", "path": "…", "headSha": "…",
    "parentBranch": "main", "forkPointSha": "…|null", "ahead": 0, "behind": 0, "dirty": 0, "parked": false,
    "mode": "source-only", "sizeMegabytes": null, "stale": false, "locked": false, "missing": false,
    "lead": null } ] }
```
- `stage.dirtyTracked` = `len(filter_noise_paths(tracked_modifications(stage), config.stage_noise_globs))`;
  `busyWith` = parsed JSON of `stage_lock_path` if the file exists, else null; `detachedSha` set only when
  the stage HEAD is detached.
- One entry per `list_worktrees()[1:]`. Registered (matched by `find_entry_by_path`) → id, mode, parked,
  parentBranch, lead from state. Unregistered → `id = sanitize_worktree_id(basename(path))`,
  `registered: false`, `parentBranch` = trunk, `lead: null`.
- `missing` = path does not exist (then ahead/behind/dirty are 0 and git is not run inside it).
  `ahead`/`behind` vs `parentBranch` via `ahead_behind`; when the branch is null (detached) compare
  `headSha`. `forkPointSha` = `merge_base(parentBranch, branch-or-headSha)`.
- `stale` = `ahead == 0 and behind >= STALE_BEHIND_THRESHOLD`. `sizeMegabytes` only when `include_sizes`.
- **Tests (2):** (a) a registered worktree `spec/b` created from `spec/a` with `parent_branch="spec/a"` and
  one commit reports `parentBranch "spec/a"`, `ahead 1`, `behind 0`; (b) an unregistered raw worktree
  appears with `registered: false` and a modified tracked file in the stage matching a noise glob is not
  counted in `dirtyTracked` while a non-matching one is.

### 3.2 `stage_ops.py` + `tests/test_stage_ops.py`
```python
stage_blockers(context, state) -> List[str]   # human-readable reasons; [] = safe to move the stage
review_worktree(working_directory, worktree_id) -> dict   # {"stageBranch": branch, "parkedWorktreeId": id}
return_stage(working_directory) -> dict                   # {"stageBranch": trunk, "reattachedWorktreeId": id}
stash_stage(working_directory) -> dict                    # {"stash": "worktree-toolkit/<utc>"}
pop_stage(working_directory) -> dict                      # {"popped": message}
place_commit_on_stage(working_directory, commit_sha, holder_description) -> dict  # {"stageDetachedSha": sha}
restore_trunk_on_stage(working_directory) -> dict         # {"stageBranch": branch}
```
- `stage_blockers`: each tracked modification left after noise filtering ("modified: <path>"), and
  "stage busy: <holder>" when `stage_lock_path` exists.
- `review_worktree`: `require_stage`; `require_entry`; refuse if `stage_review_worktree_id` is set
  ("return the stage first"), if lead status is `building` or `gating`, if `stage_blockers` is
  non-empty, if the worktree has any dirty entry, or if the worktree's current branch ≠ `entry.branch`.
  Then `git switch --detach` **in the worktree**, then `git switch <branch>` **in the stage**. If the
  stage switch fails, run `git switch <branch>` back in the worktree and raise `RefusedError` with git's
  stderr. On success set `parked=True`, `stage_review_worktree_id=id`.
- `return_stage`: `require_stage`; refuse if no review recorded or tracked modifications remain after
  noise filtering; `git switch <trunk>` in the stage; `git switch <branch>` in the worktree (picks up any
  commits made during review); clear `parked` and `stage_review_worktree_id`.
- `stash_stage`: `git stash push -m worktree-toolkit/<utc_now_text()>` (tracked only); refuse when
  there is nothing to stash; append the message to `state.stage_stashes`. `pop_stage`: refuse if the list
  is empty; find the newest recorded message in `git stash list --format=%gd%x09%gs` (the subject ends
  with the message), `git stash pop <ref>`, remove it from the list.
- `place_commit_on_stage`: `require_stage`; refuse when a review is recorded or `stage_blockers` is
  non-empty; write `stage.lock` as JSON `{"holder": holder_description, "commitSha": sha, "since": utc}`
  **after** checking blockers; `git switch --detach <sha>`; on git failure delete the lock and re-raise.
- `restore_trunk_on_stage`: `git switch <trunk>` in the stage, delete `stage.lock` if present.
- **Tests (2):** (a) review → return round trip: stage ends on `main` not detached, the worktree ends on
  `spec/<id>` not detached with a commit made on the stage during review, and state has
  `parked False` / no review id; (b) `review_worktree` raises `RefusedError` when a tracked stage file is
  modified — the stage is still on `main` and the worktree still attached — and succeeds once that
  path matches `config.stage_noise_globs`.

### 3.3 `merge_ops.py` + `tests/test_merge_ops.py`
```python
UNITY_YAML_PATTERNS = ["*.unity", "*.prefab", "*.asset", "*.mat", "*.anim", "*.controller"]
merge_worktree(working_directory, worktree_id) -> dict   # {"merged": id, "branch": b, "trunkSha": sha, "commitCount": n}
configure_unity_yaml_merge(working_directory, unity_yaml_merge_path) -> dict  # {"driver": text, "patterns": [...]}
```
- `merge_worktree`: `require_stage`; refuse if a review is recorded, the stage is not on trunk, stage
  tracked modifications remain after noise filtering, the entry is parked, the worktree has dirty
  entries, its current branch ≠ `entry.branch`, or it has zero commits ahead of trunk.
  Run `git rebase <trunk>` **in the worktree** with `check=False`; on failure collect
  `git diff --name-only --diff-filter=U`, run `git rebase --abort`, raise `RefusedError` naming the files.
  Then `git merge --ff-only <branch>` **in the stage**. Set `lead.status = "done"` when a lead exists.
  Does **not** push and does **not** remove the worktree (the skill does both).
- `configure_unity_yaml_merge`: `git config --local merge.unityyamlmerge.name "Unity SmartMerge"`;
  `git config --local merge.unityyamlmerge.driver '"<path>" merge -p %O %B %A %A'`; append
  `<pattern> merge=unityyamlmerge` to `<common-git-dir>/info/attributes` for each pattern not already
  present (create the file if needed). Idempotent.
- **Tests (2):** (a) worktree commits `a.txt`, stage commits `b.txt` on main → merge succeeds, main has
  both files and `git rev-list --merges main` is empty; (b) both edit `readme.txt` differently →
  `RefusedError`, worktree branch sha unchanged, no `rebase-merge`/`rebase-apply` directory under the
  worktree's git dir, main sha unchanged.

### 3.4 `lifecycle_ops.py` + `tests/test_lifecycle_ops.py`
```python
register_worktree(state, worktree_id, path, branch, parent_branch) -> WorktreeEntry   # caller holds StateLock; RefusedError on duplicate id
create_worktree(working_directory, worktree_id, from_branch=None, mode="source-only", branch_name=None) -> dict  # entry_to_dictionary
adopt_worktrees(working_directory) -> dict   # {"adopted": [...], "alreadyRegistered": [...], "stale": [...]}
remove_worktree(working_directory, worktree_id, keep_branch=False, force=False) -> dict
    # {"removed": id, "unlinked": [paths], "branchDeleted": bool, "branchKeptReason": str|None}
```
- `create_worktree`: `require_stage`; `validate_worktree_id`; refuse if the id is registered, the path
  `<stage>/.claude/worktrees/<id>` exists, `mode == "own-editor"` ("own-editor mode arrives in
  Phase 5"), or the branch exists. Branch = `branch_name or "spec/" + id`; base = `from_branch or trunk`.
  `git worktree add -b <branch> <path> <base>`; register with `parent_branch=base`,
  `created_utc=utc_now_text()`.
- `adopt_worktrees`: `require_stage`; for each `list_worktrees()[1:]` not found by path: id =
  `sanitize_worktree_id(basename)` with `-2`, `-3`… on collision; branch = record branch or `""`;
  parent = trunk; `stale` when the branch exists, is 0 ahead and ≥ 50 behind trunk. **Never removes
  anything.**
- `remove_worktree` — **order is mandatory (roadmap trap 1: `git worktree remove --force` deletes
  junction targets)**:
  1. `require_stage`; `require_entry`; refuse if parked or it is the review worktree; refuse if the
     git record `is_locked` (include `lock_reason`); if the path exists and has dirty entries and not
     `force` → refuse with the count.
  2. `links.remove_directory_link` on every `links.find_directory_links(path)`; re-scan and refuse if any
     link remains. **Never call git with a link still inside.**
  3. If `mode == "own-editor"` and `<path>/Library` exists (and is not a link) → `shutil.rmtree` it.
  4. `git worktree remove [--force] <path>` (`--force` only when `force`); if the path is already
     missing use `git worktree prune` instead.
  5. Unless `keep_branch`: delete the branch with `git branch -d` only if
     `is_ancestor(branch, trunk)`; otherwise keep it with reason "not merged into <trunk>".
  6. Unregister under `StateLock`.
- **Tests (2):** (a) a registered worktree holding `Nested/Deep` → junction to a directory with
  `precious.txt` plus an untracked file, removed with `force=True` → worktree path gone, entry
  unregistered, **`precious.txt` still exists**, `unlinked` lists the link; (b) a worktree with one
  unmerged commit, removed with `force=True` → `branchDeleted False`, branch still exists.

### 3.5 `gate_client.py` + `tests/test_gate_client.py`
```python
HEARTBEAT_FILE_NAME = "broker-heartbeat.json" ; HEARTBEAT_MAX_AGE_SECONDS = 10.0
broker_is_alive(common_git_directory) -> bool    # heartbeat file exists and its mtime is ≤ 10 s old
enqueue_gate(working_directory, edit_mode_fixtures, play_mode_fixtures, timeout_seconds) -> dict  # the request dict
wait_for_result(common_git_directory, request_id, timeout_seconds, poll_seconds=1.0) -> dict       # the result dict
run_gate(working_directory, edit_mode_fixtures, play_mode_fixtures, timeout_seconds=900, poll_seconds=1.0) -> dict
```
- `enqueue_gate`: `require_linked_worktree`; entry = `find_entry_by_path(state, context.repository_root)`
  or refuse ("claim this worktree first"); refuse if parked or dirty ("commit before gating"); raise
  `NoBrokerError` if `not broker_is_alive` (**before** writing anything). Request:
  `{"protocol": 1, "requestId": uuid4().hex, "worktreeId", "commitSha": resolve_commit(HEAD),
  "editModeFixtures", "playModeFixtures", "timeoutSeconds", "createdUtc", "phase": "queued"}` written
  atomically (`.tmp` + `os.replace`) to `<state_directory>/queue/<requestId>.json`; set lead status
  `gating` if a lead exists.
- `wait_for_result`: poll `<state_directory>/results/<requestId>.json`; return its parsed JSON. Raise
  `NoBrokerError` if the broker heartbeat has been dead for longer than `HEARTBEAT_MAX_AGE_SECONDS`
  during the wait; `GateTimeoutError` past `timeout_seconds`.
- `run_gate`: enqueue, wait, then set lead status back to `building` (also on error, in `finally`), and
  return the result.
- **Tests (2):** (a) no heartbeat → `NoBrokerError` and the queue directory is empty; (b) a fake broker
  thread refreshes the heartbeat, waits for a queue file, writes
  `{"requestId": id, "verdict": "pass", ...}` to results → `run_gate(..., poll_seconds=0.05)` returns
  verdict `pass` and lead status is `building` afterwards.

### 3.6 Claude files (text) — `Tools~/claude/agents/spec-lead.md` + `Tools~/claude/skills/worktree-run/SKILL.md`
Contents are specified in §5 of this file.

## 4. Wave 2 (after wave 1 is green) — `hooks.py`, `cli.py` + `worktree.py`, `install.py`, `Documentation~/worktree-toolkit.md`

### 4.1 `hooks.py` + `tests/test_hooks.py`
```python
hook_create(payload_text: str) -> str    # returns the created worktree path (the CLI prints only this line)
hook_remove(payload_text: str) -> int    # 0 = removed, 1 = keep
claim_worktree(working_directory, spec_id, spec_path, lead_model, worker_model) -> dict  # entry_to_dictionary
set_lead_status(working_directory, status) -> dict
```
- Probe P2: the `WorktreeCreate` payload carries only `session_id, transcript_path, cwd, prompt_id,
  hook_event_name, name` — **no `base`**. `hook_create`: stage = `main_checkout_path(payload.cwd)`;
  id = `sanitize_worktree_id(name)`; `lifecycle_ops.create_worktree(stage, id, branch_name="worktree-" + id)`
  (base = trunk); set lead `LeadBinding(status="unclaimed")`; return the path.
- `hook_remove` (payload `worktree_path`): unknown path → 0 without touching anything; registered and
  (dirty or ahead of trunk > 0) → 1; else `remove_worktree(stage, id)` → 0.
- `claim_worktree`: `require_linked_worktree`; `validate_worktree_id(spec_id)`; entry by
  `repository_root` (register it if absent); refuse if `spec_id` is registered to another path or
  `spec/<spec_id>` exists; `git branch -m spec/<spec_id>` in the worktree; re-key the entry to `spec_id`
  with `branch`, `lead=LeadBinding(spec_path, lead_model, worker_model, "building")`.
- **Tests (2):** (a) `hook_create` on a fixture payload returns a path that exists and is a linked
  worktree on `worktree-<id>`; (b) `claim_worktree` from inside it renames the branch to `spec/<id>` and
  re-keys state.

### 4.2 `cli.py` + `worktree.py`
`worktree.py` inserts its own folder on `sys.path` and calls `worktree_toolkit.cli.main(sys.argv[1:])`.
`cli.main` → argparse subcommands exactly as roadmap §5's table, each with `--json`. Default output is a
short human summary; `--json` prints the dict with `indent=1`. `ToolkitError` → message on stderr (and
`{"error": message, "exitCode": n}` on stdout with `--json`), return its `exit_code`.
`stage-commit <sha> --holder <text>` → `stage_ops.place_commit_on_stage`; `restore-trunk` →
`stage_ops.restore_trunk_on_stage` (added 2026-09-14 so an orchestrator can gate by MCP without a broker).
`hook-create` reads stdin and prints **only** the path (no JSON, no extra lines). `hook-remove` returns
the hook's code. `doctor` returns `{"gitVersion", "gitOk" (≥ 2.38), "python", "stage", "brokerAlive",
"hooksInstalled", "stageBlockers"}` and never refuses. Test: `tests/test_cli.py` — `hook-create` stdout
is exactly one line.

### 4.3 `install.py`
`install_claude(working_directory) -> dict`: `require_stage`; copy `Tools~/claude/agents/spec-lead.md` →
`<stage>/.claude/agents/`, `Tools~/claude/skills/worktree-run/SKILL.md` → `<stage>/.claude/skills/worktree-run/`;
merge into `<stage>/.claude/settings.json` hooks `WorktreeCreate` →
`python "${CLAUDE_PROJECT_DIR}/Packages/com.worktreetoolkit/Tools~/worktree.py" hook-create` and `WorktreeRemove` →
`... hook-remove`. Use `${CLAUDE_PROJECT_DIR}`, never an absolute path or a relative one: `settings.json` is tracked and
shared across machines, and a relative path breaks whenever the session cwd moves (it did, 2026-09-14). Keep every
existing hook and skip entries already present. Idempotent. No fixture beyond a quick manual run in a temp repo.

## 5. Claude file contents (task 3.6)

**`spec-lead.md`** frontmatter: `name: spec-lead`, description ("Leads one spec in its own git
worktree: claims it, runs worker waves, commits, gates through the Worktree Toolkit broker. Spawned only
by /worktree-run."), `isolation: worktree`, `maxTurns: 80`, `disallowedTools: mcp__UnityMCP__*`. No
`model` line (the skill passes it per spawn). Body, ≤ 60 lines:
1. First command: `git rev-parse --show-toplevel` — that Windows path is the worktree; use it for every
   file path you or your workers touch. **Never use `pwd`** (roadmap trap 8).
2. `python Packages/com.worktreetoolkit/Tools~/worktree.py claim <spec-id> --spec <path> --lead-model <m> --worker-model <m>`.
3. Read the spec; cut work into waves of ≤ 2-file `worker` tasks; spawn workers with `model: <worker model>`
   and the worktree's absolute path in every brief; CLAUDE.md's brief checklist applies.
4. After each wave: commit (explicit paths, never `git add -A`) → `worktree.py gate --edit-mode <touched fixtures>`.
   Exit 3 (no broker): `SendMessage` the parent "gate needed: <spec-id> <sha> <fixtures>" and wait for its
   reply. Failures: fix with a fresh worker, re-gate; after three failed gates send the parent the result
   and stop.
5. Finish: `worktree.py status <spec-id> ready`; report ≤ 30 lines (commits, gate verdicts, what is
   unverified, owner checkpoints). At turn 70 stop and write the report with a handoff.
6. Never: touch Unity MCP, move the stage, merge, push, or edit outside the worktree.

**`worktree-run/SKILL.md`** frontmatter `name: worktree-run`, description ("Run one or more specs in
parallel git worktrees with a spec-lead each, then review on stage and merge — the Worktree Toolkit
workflow."). Body, ≤ 90 lines, sections:
- **Preflight:** `worktree.py doctor --json`; stop and tell the owner on stage blockers, git < 2.38, hooks
  not installed (`worktree.py install-claude`), or no broker (gates will come to you by message).
- **Ask:** for each spec, `AskUserQuestion` "Lead model for <spec>?" and "Worker model for <spec>?"
  (options opus / sonnet / haiku / fable, recommended first); ≤ 4 questions per call → two specs per call.
- **Spawn:** one `Agent` per spec, `subagent_type: spec-lead`, `model: <lead model>`, background, prompt =
  spec id, spec path, worker model. Specs run in parallel.
- **While running:** answer `gate needed` messages by gating via Unity MCP (swap with
  `place_commit_on_stage` semantics through the CLI, compile gate, restore trunk) when no broker runs;
  otherwise stay quiet. Show status with `worktree.py list`.
- **Review:** when a lead reports ready, tell the owner; on "review <id>" run `worktree.py review <id>`
  (stage stash first only with consent), and `worktree.py return` when done.
- **Merge:** on the owner's "merge <id>": `worktree.py merge <id>` → push trunk → `worktree.py remove <id>`.
  Conflicts: spawn a fresh spec-lead on the same worktree to resolve, then retry.
- **Never:** `git worktree remove --force` by hand (junction trap), `AssetDatabase.SaveAssets`, merging
  without the owner's word.
