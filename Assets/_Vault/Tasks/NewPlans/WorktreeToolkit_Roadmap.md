# Worktree Toolkit — parallel AI specs in one Unity project, reviewed by swapping the Editor

> **Status:** 🔨 Phase 1 (CLI) built 2026-09-14 with the Editor closed — 15 fixtures green, P1/P2/P5 probed;
> P3/P4/P6/P7 and Phases 2–5 need the Editor. New embedded package `Packages/com.worktreetoolkit`, `0.1.0`.
> **Executor:** one Editor-connected orchestrator (the only session with Unity MCP) + `worker`
> subagents per wave. Phase 0 probes run first and can change sections 4–5.
> **Why now:** parallel sessions today share one Editor and one Library ("one Editor, one driver"
> — the 2.2 GB log incident), and the one worktree that exists
> (`.claude/worktrees/a72-editor-visual-unification-spec`) is 139 commits behind `main` with
> nothing new on it. It also has no Rive package, so Unity could not compile it.

## 1. What it delivers

- **A node window** (`Window ▸ Worktree Toolkit`). The trunk (`main`) is the root on the left, and
  every worktree branch forks rightwards from its fork point. A branch of a branch forks from its
  parent. The branch currently **on stage** (checked out in the folder your Editor has open) is
  drawn in full colour, and every other node is greyed. Click a node → **Put on stage** swaps the
  Editor to that branch for review; **Return** puts the trunk back. Each node shows branch, spec,
  lead/worker model, lead status, ahead/behind, uncommitted count, disk size and mode.
- **A command-line tool** (`Tools~/worktree.py`, Python 3 stdlib, JSON output) that does every git
  operation. The window, the Editor gate broker and Claude all call it. It works with the Editor
  closed and on another machine.
- **A gate broker in the Editor.** A spec lead in a worktree runs `worktree.py gate`. The broker
  puts that commit on stage, compiles, runs the named fixtures, writes a result, and returns the
  stage to trunk when the queue is empty. Leads never touch MCP.
- **A Claude skill `/worktree-run`.** You name the specs; it asks the **lead model and worker model
  per spec**, spawns one `spec-lead` subagent per spec in its own worktree (each lead spawns its
  own workers), and tells you when each is ready. Review and merge happen from Claude or the window.
- **Optional own-Editor mode per worktree** (off by default): a seeded Library plus linked heavy
  untracked packages, so a machine with the RAM can run gates in parallel instead of queued.

## 2. Owner calls recorded 2026-09-14 — do not re-ask

| # | Call |
|---|---|
| O1 | Making a branch active **swaps it into the Editor you have open** (not a second Editor). |
| O2 | **One central AI owns the Editor.** One subagent per spec, which spawns its own subagents. The owner is **asked the model for each spec lead and for its workers**. Multiple Editors are an option; **default is one**, so lower-spec devices can use the tool. |
| O3 | What to share versus duplicate is delegated: "best balance of memory use and performance" (D5). |
| O4 | **Third embedded package**, independent of the game, like the animation/movement toolkits. |

## 3. Measured facts this plan stands on (2026-09-14)

| Fact | Value | Consequence |
|---|---|---|
| Tracked `Assets/` | 62 MB (Rive 27, Audio 12, Textures 9) | Per-folder sparse checkout saves almost nothing today → deferred (D5). |
| `Library/` | 4,115 MB: PackageCache 1,620 · BurstCache 1,216 · Bee 481 · Artifacts 403 | A Library is the real cost of a worktree; single-Editor mode needs **none**. |
| `Packages/app.rive.rive-unity` | 504 MB, **gitignored** (`/[Pp]ackages/*/`) | A fresh worktree cannot compile without it → link it in own-Editor mode. |
| Existing worktree size | 68 MB, no Library, no Rive | Source-only worktrees are cheap. |
| git | 2.43.0.windows.1; `core.symlinks=false`; `.claude/worktrees/` excluded in `.git/info/exclude` | Per-worktree config and `sparse.expectFilesOutsideOfPatterns` both available. |
| Python | 3.12.10 on PATH (hooks already use it) | CLI language (D2). |
| `UnityYAMLMerge.exe` | present under `Hub/Editor/6000.5.0f1/Editor/Data/Tools/` | Local merge driver for YAML assets (D8). |
| Free disk | C: 307 GB | Own-Editor mode affordable, not the default. |
| Claude Code subagents | nest 3 deep by default; `isolation: worktree` is inherited by nested agents; subagents can `SendMessage` their parent mid-run; per-spawn `model` param; `WorktreeCreate` hook prints the path on stdout's first line | O2 maps onto built-in features (D6, D7, D11). |
| Claude Code isolation | blocks edits, cwd and git redirects **into the main checkout** from an isolated agent | Leads cannot swap the stage themselves → the broker does it (D7). |

## 4. Decisions (delegated calls — recorded, reversible)

- **D1 Name.** `com.worktreetoolkit`, display name "Worktree Toolkit". Needs
  `!/Packages/com.worktreetoolkit/` in `.gitignore`. That un-ignore is the trap the gitignore
  comment already warns about: forget it and new files are silently never committed.
- **D2 One implementation of git logic: `Tools~/worktree.py`.** `~` folders are excluded from
  Unity compilation. The Editor window and broker shell out to it and parse its `--json` output;
  no C# re-implements a git query. The CLI is `worktree.py`, not `wt`, because `wt` is Windows
  Terminal. Python is found as `python3` → `python` → `py -3`, and the path is overridable in
  Project Settings.
- **D3 State lives in `<git-common-dir>/worktree-toolkit/`** (`state.json`, `queue/`, `results/`,
  `stage.lock`). It is never tracked and every worktree sees the same copy.
- **D4 Stage / park model.**
  - **Stage** = the main checkout the Editor has open.
  - **Review** (`review <id>`): the worktree switches to a detached HEAD at the same commit
    ("parked": files unchanged, no reimport), then the stage runs `git switch spec/<id>`. You can
    commit fixes on stage.
  - **Return:** the stage runs `git switch <trunk>` and the worktree re-attaches to its branch.
  - **Gates** put the commit on stage **detached** (`git switch --detach <sha>`). Detaching is legal
    even while the branch is checked out in the worktree, so gates need no park.
  - Swapping back is cheap because Library's artifact cache is keyed by content hash.
- **D5 Isolation (O3).**
  - **Source-only (default):** tracked files only, no Library, no links (~68 MB). Unity never
    opens it in single-Editor mode, so it needs nothing else.
  - **Own-Editor (opt-in per node):** Library seeded by copying from stage minus `BurstCache`,
    `ShaderCache`, `Search` and `*Captures` (~2.5 GB, before P6 measures it). Every gitignored
    package folder under stage `Packages/` that holds a `package.json` is linked with an NTFS
    junction (symlink on macOS/Linux).
  - **Linked tracked folders** (the original per-folder toggle idea) are **deferred**: all of
    tracked `Assets/` is 62 MB. Revisit when any tracked folder passes 500 MB.
- **D6 Worktrees are created by a `WorktreeCreate` hook.** The hook runs `worktree.py hook-create`:
  worktree under `.claude/worktrees/<name>`, base = **local** trunk HEAD, registered as unclaimed,
  path printed as stdout's only line. The hook input carries no spec, so the lead's first command
  is `worktree.py claim <spec-id>`. That renames the branch to `spec/<spec-id>` and binds spec,
  lead model and worker model. A `WorktreeRemove` hook exits non-zero (keeps the directory) for any
  worktree with unmerged commits.
- **D7 Gates run in the Editor broker, not in the orchestrator's context.**
  - The lead commits, then runs `worktree.py gate --edit-mode <fixtures>`, which enqueues a request
    and blocks on the result.
  - The broker is `[InitializeOnLoad]` in the stage Editor and drains the queue one request at a
    time. In own-Editor mode the worktree's own Editor drains that worktree's requests.
  - If no broker is alive (exit 3), the lead `SendMessage`s the orchestrator, which gates by MCP
    exactly as today.
  - The orchestrator is messaged on escalation only: repeated failure, Burst cache corruption, or a
    rebake/play need.
- **D8 Merge = rebase in the worktree, then `git merge --ff-only` on trunk.**
  - This keeps `main`'s current linear, per-task history.
  - `UnityYAMLMerge` is registered as a **local** (`.git/config`) merge driver for
    `*.unity *.prefab *.asset *.mat *.anim *.controller`.
  - Conflicts go back to the lead (or a fresh lead). The orchestrator pushes after the merge.
  - The worktree and branch are removed after a successful merge.
- **D9 The stage is only moved when it is safe.**
  - Refuse if tracked files are modified (configurable noise globs, e.g.
    `Assets/SceneDependencyCache/**`), if any open scene is dirty, if Play mode is on, or if a gate
    holds `stage.lock`.
  - The window offers **Stash as `worktree-toolkit/<time>`**; return offers to pop it.
  - **Never `AssetDatabase.SaveAssets`** (it flushes the owner's unsaved editor state, per the A84
    note).
- **D10 Window tech.** UI Toolkit, with edges drawn by `generateVisualContent` + `Painter2D`. No
  GraphView (experimental, and nodes are laid out, not dragged), no IMGUI/Handles. Tree layout is a
  pure C# class. Styling follows the Editor Visual Language conventions (symbols over words, boxed
  cards, one palette) **by copying the look, never referencing the animation toolkit**.
- **D11 `spec-lead` agent** (`Tools~/claude/agents/spec-lead.md`, installed by `worktree.py
  install-claude`):
  - Frontmatter: `isolation: worktree`, `disallowedTools: mcp__UnityMCP__*`, `maxTurns: 80` (write
    the handoff at 70).
  - Model comes from the per-spawn `model` param; workers get the owner's worker model the same
    way.
  - CLAUDE.md's "worker or verifier only" rule gains this third type, used only by
    `/worktree-run`.
- **D12 No review while a lead is running.** Lead status `building` or `gating` → `review` refuses.
  A parked worktree refuses `gate` and commits from its lead.
- **D13 Removal order is fixed:** unlink every junction (`os.rmdir` on the link itself) → delete
  the seeded Library → `git worktree remove` → delete the branch **only if merged into trunk**.

## 5. Pinned surfaces (tasks code against these, so they can run in parallel)

**CLI** — `python Packages/com.worktreetoolkit/Tools~/worktree.py <command> [--json]`.
Exit codes: `0` ok · `1` usage · `2` refused (precondition; reason on stderr and in JSON) ·
`3` no broker · `4` timeout · `5` git command failed.

| Command | Runs from | Does |
|---|---|---|
| `doctor` | anywhere | git ≥ 2.38, python, hooks installed, broker heartbeat, stage cleanliness |
| `list` | anywhere | the graph model below |
| `create <id> [--from <branch>] [--mode source-only\|own-editor]` | stage | manual creation (the window's **+**) |
| `hook-create` / `hook-remove` | Claude Code hooks | D6 |
| `claim <id> --spec <path> --lead-model <m> --worker-model <m>` | inside a worktree | binds and renames the branch |
| `status <id> <building\|gating\|ready\|failed\|done>` | inside a worktree | lead status shown on the node |
| `gate [--edit-mode <fixture>...] [--play-mode <fixture>...] [--timeout 900]` | inside a worktree | D7; refuses if uncommitted |
| `review <id>` / `return` | stage (or the broker on its behalf) | D4 + D9 |
| `stash-stage` / `pop-stage` | stage | D9 |
| `stage-commit <sha> --holder <text>` / `restore-trunk` | stage (the broker, or the orchestrator gating by MCP when no broker runs) | D4 gate staging: detach the stage at a commit under `stage.lock`, then back to trunk |
| `merge <id>` | stage | D8 |
| `remove <id> [--keep-branch]` | stage | D13 |
| `adopt` | stage | registers pre-existing worktrees (e.g. a72) and flags stale ones; **never deletes** |
| `install-claude` | stage | copies skill + agent + hook entries into the project `.claude/` |
| `config [--trunk B] [--add-noise-glob G]… [--remove-noise-glob G]…` | stage (read: anywhere) | trunk branch + D9 noise globs |
| `yaml-merge-driver <UnityYAMLMerge path>` | stage | D8 local merge driver |

Commands marked *stage* refuse (exit 2) when run from a linked worktree, so an isolated lead can
never move the stage.

**`list --json`**
```json
{ "protocol": 1, "trunk": "main",
  "stage": { "path": "…", "branch": "main", "detachedSha": null, "dirtyTracked": 0, "busyWith": null, "stashes": [] },
  "worktrees": [ { "id": "a92", "branch": "spec/a92", "path": ".claude/worktrees/…", "headSha": "…",
    "parentBranch": "main", "forkPointSha": "…", "ahead": 3, "behind": 0, "dirty": 0, "parked": false,
    "mode": "source-only", "sizeMegabytes": 68, "stale": false,
    "lead": { "spec": "Assets/_Vault/…", "leadModel": "opus", "workerModel": "sonnet", "status": "gating" } } ] }
```

**Gate request** (`queue/<requestId>.json`) — the broker rewrites `phase` as it goes, so a domain
reload resumes from disk:
```json
{ "protocol": 1, "requestId": "…", "worktreeId": "a92", "commitSha": "…", "editModeFixtures": [], "playModeFixtures": [],
  "timeoutSeconds": 900, "createdUtc": "…", "phase": "queued|staging|compiling|testing|returning|done" }
```
**Gate result** (`results/<requestId>.json`)
```json
{ "requestId": "…", "verdict": "pass|compile-errors|burst-errors|test-failures|refused|timeout",
  "compilerErrors": [ { "file": "…", "line": 0, "code": "CS0103", "message": "…" } ], "burstErrors": [],
  "tests": { "passed": 0, "failed": 0, "failures": [ { "name": "…", "message": "…" } ] }, "refusedReason": null }
```

## 6. Package layout

```
Packages/com.worktreetoolkit/
  package.json                          unity 6000.5, no package dependencies
  Tools~/worktree.py                    entry; argparse → commands/
  Tools~/worktree_toolkit/              git_runner.py  state_store.py  graph_model.py  stage_ops.py
                                        links.py  gate_client.py  hooks.py  merge_ops.py  install.py
  Tools~/tests/                         unittest against temp repos (no Unity)
  Tools~/claude/                        skills/worktree-run/SKILL.md  agents/spec-lead.md  hooks.json
  Editor/WorktreeToolkit.Editor.asmdef  includePlatforms: [Editor]
  Editor/Cli/                           WorktreeCliClient.cs  WorktreeGraphDto.cs  PythonLocator.cs
  Editor/Window/                        WorktreeGraphWindow.cs  WorktreeTreeLayout.cs  WorktreeNodeElement.cs
                                        WorktreeEdgeLayer.cs  GateQueuePanel.cs  WorktreeToolkit.uss
  Editor/Broker/                        GateBroker.cs  StageSwapGuard.cs  CompileCapture.cs  TestCapture.cs  GateRequestStore.cs
  Editor/OwnEditor/                     OwnEditorLauncher.cs  (Phase 5)
  Tests/Editor/                         WorktreeTreeLayoutTests.cs
  Documentation~/worktree-toolkit.md
```

Window mock (root left, forks right, the on-stage node lit, the others greyed):
```
┌ Worktree Toolkit ─────────────────────────────── ⟳  ＋  ⚙   queue ▮1 ┐
│ ◉ main  ·  stage clean                                              │
│ ───────●───────────────┬──────▶ ┌ spec/a92 ───────────── opus▸sonnet ┐│
│                        │        │ ▲3 ▼0  ✎0  68 MB   ⧗ gating         ││
│                        │        └──────────────────── [▶ stage] ⋯ ───┘│
│                        └──────▶ ┌ spec/a93 ─────────── sonnet▸haiku ┐ │
│                                 │ ▲7 ▼2  ✎0  68 MB   ✓ ready          │ │
│                                 └─────────────────── [▶ stage] ⋯ ───┘ │
└──────────────────────────────────────────────────────────────────────┘
```

## 7. Traps to design around (put in the vault note when built)

1. **`git worktree remove --force` deletes the contents of every junction inside the worktree**
   (P5, measured 2026-09-14 on git 2.43.0.windows.1, top-level and nested links alike). Without
   `--force`, git refuses (exit 128, untracked files). Python 3.12 `shutil.rmtree` does *not*
   follow junctions, and `os.rmdir` on the link then `--force` leaves the target intact. So D13's
   unlink-first order is mandatory. Documentation must warn that a hand-run `--force` remove on an
   own-Editor worktree wipes the stage's Rive package. Claude Code's own removal is safe since
   v2.1.205.
2. **Domain reload wipes the broker's managed state.** Capture compiler messages in
   `CompilationPipeline.assemblyCompilationFinished` and **write them to the result file
   immediately**, because that callback fires in the old domain. Persist `phase` before every step.
3. **Burst errors are not `CompilerMessage`s.** Capture them from
   `Application.logMessageReceivedThreaded`. Burst can also keep running stale code after a
   byte-identical swap back (`feedback_burst_stale_after_revert`), so gates vouch for compile
   errors, not Burst runtime behaviour.
4. **Unity writes files on its own** (`EditorBuildSettings.asset`, `SceneDependencyCache`, the
   anim-event registry — all dirty in today's status). Without noise globs, D9 refuses every swap.
5. **git switch under a live Editor.** Wrap it in `AssetDatabase.DisallowAutoRefresh()` →
   git → `AllowAutoRefresh()` + `Refresh()` so Unity never imports a half-switched tree.
6. **`claim` renames a branch Claude Code created and locked.** Rename, don't recreate, and
   probe it (P2).
7. **Protocol skew.** A worktree runs its own copy of `worktree.py`. Every queue file carries
   `protocol`, and the broker refuses a mismatch with a readable reason.
8. **Git Bash `pwd` paths escape the worktree on Windows** (P2). `/tmp/...` from Git Bash is
   `%TEMP%`, but the Write tool reads it as `C:\tmp`. `spec-lead` and worker briefs name the
   worktree by its Windows absolute path.
9. **MAX_PATH on remove** (end-to-end probe). With `core.longpaths` unset, git for Windows drops
   the worktree record, then fails `Filename too long` and leaves the folder. `remove_worktree`
   now finishes that case with a `\\?\` rmtree after re-checking for links, and `worktree.py` no
   longer writes `__pycache__`. Stitch Punk today: 86-character worktree prefix + 147-character
   longest tracked path = 233 of 260, but own-Editor Libraries will pass the limit, so `doctor`
   reports `longPathsEnabled`.

## 8. Phase 0 — probes (orchestrator, before any code; results go into section 4)

- **P1 ✅ 2026-09-14** (nested `claude -p`, haiku, scratch repo): an `isolation: worktree`
  subagent ran a Python script that calls git internally — no refusal.
- **P2 ✅ 2026-09-14** with a `WorktreeCreate` hook:
  - the lead landed in the printed path;
  - `git branch -m spec/probe` worked (hook-made worktrees carry no Claude lock);
  - the commit landed on the renamed branch;
  - the nested subagent's cwd was the same worktree;
  - the `WorktreeRemove` hook did not fire for a worktree with changes.
  - **Findings:**
    - the hook payload is `session_id, transcript_path, cwd, prompt_id, hook_event_name, name`,
      with **no `base` or `isolation`** despite the docs, so the hook picks the base itself;
    - the nested agent's `pwd` came back as Git Bash `/tmp/...`, and its Write tool resolved that
      to `C:\tmp\...`, **outside the worktree**. Briefs must hand agents the Windows path from
      `git rev-parse --show-toplevel`, never `pwd` (trap 8).
  - Not yet probed: the periodic sweep.
- **P3** With a dirty in-memory ScriptableObject open, run `DisallowAutoRefresh` → `git switch` →
  `Refresh`. Record whether Unity prompts, reloads or overwrites.
- **P4** `CompilationPipeline.RequestScriptCompilation()` with no script changes: does
  `compilationFinished` still fire? This decides how the broker detects "nothing to compile".
- **P5 ✅ 2026-09-14** `git worktree remove --force` wipes junction targets (any depth); plain
  remove refuses; `shutil.rmtree` (3.12) and unlink-first are safe → trap 1.
- **P6** First open to idle for an own-Editor worktree: no seed, full seed, seed minus
  PackageCache. Record GB and minutes and pick D5's seed set from the numbers.
- **P7** Stage swap cost `main` → spec → `main` with a script-only diff: seconds to idle each way.

## 9. Build phases

Waves follow the parallel-small rule: one gate per wave, tasks code against section 5, and files
are disjoint.

**Phase 1 — CLI core** ✅ built 2026-09-14 (no Unity; gate = `python -m unittest` in `Tools~/tests`, 16 green)
- **End-to-end probe ✅** (nested `claude -p`, haiku, throwaway repo with a copy of the toolkit):
  - `install-claude` → the `${CLAUDE_PROJECT_DIR}` hook created the worktree;
  - the `spec-lead` claimed `hello`, committed, got `gate` exit 3, `SendMessage`d "gate needed", got
    the parent's verdict back, and ran `status ready`;
  - `merge` fast-forwarded `main` with no merge commit;
  - `remove` failed on MAX_PATH (trap 9), which is now fixed and has a fixture.
- Added beyond the table: `config`, `yaml-merge-driver`, `stage-commit` / `restore-trunk`, and
  `doctor.longPathsEnabled`. **Not installed into this repo's `.claude/`**: that changes worktree
  creation for every session, so it is the owner's call at C2.
- [parallel-safe] `git_runner.py` · `state_store.py` · `graph_model.py` (`list`) · `links.py`
  (junction/symlink create + unlink) · `stage_ops.py` (`review`/`return`/stash) ·
  `merge_ops.py` · `hooks.py` (`hook-create`/`hook-remove`/`claim`/`status`) — one worker each.
- Orchestrator: `worktree.py` argparse wiring, `doctor`, `adopt`, `.gitignore` un-ignore,
  `package.json`.
- Fixtures (each must fail with its fix reverted):
  - review→return round trip leaves both checkouts on their original refs;
  - review refuses on a dirty tracked file but passes on a noise-glob file;
  - remove never deletes a junction target;
  - `hook-create` prints exactly one line;
  - `merge` refuses a non-fast-forward.

**Phase 2 — window** (compile gate + one layout fixture)
- [parallel-safe] `WorktreeCliClient` + DTOs + `PythonLocator` · `WorktreeTreeLayout` (+ fixture:
  a branch of a branch nests under its parent, siblings don't overlap) · `WorktreeNodeElement` ·
  `WorktreeEdgeLayer` · `WorktreeToolkit.uss`.
- Then `WorktreeGraphWindow` wiring:
  - poll `list` every 3 s while focused;
  - node actions stage / return / merge / remove / reveal, each with a confirm dialog that states
    what git will do.
- ⏸ **C1 owner checkpoint:** open the window against `adopt`ed worktrees (a72 included). Questions:
  the greyed look, card contents, left-to-right vs top-down, and whether a72 should be removed.

**Phase 3 — gate broker**
- [parallel-safe] `GateRequestStore` · `CompileCapture` · `TestCapture` (TestRunnerApi, filtered
  by fixture) · `StageSwapGuard` (D9 + trap 5) · `gate_client.py` (`gate` command, heartbeat wait)
  · `GateQueuePanel`.
- Then `GateBroker` (state machine over `phase`, heartbeat file every 2 s, return-to-trunk when the
  queue drains).
- Drive: enqueue a gate from a scratch worktree with a deliberate `CS0103`, then fixed, and check
  both verdicts. **Scratch assets only; never SaveAssets.**

**Phase 4 — Claude integration**
- [parallel-safe] `Tools~/claude/skills/worktree-run/SKILL.md` · `agents/spec-lead.md` ·
  `install.py` · `Documentation~/worktree-toolkit.md` · vault `Memories/Code/WorktreeToolkit.md`.
- Orchestrator: install into this repo's `.claude/`, and add `spec-lead` to CLAUDE.md's Subagent
  Delegation section.
- Skill flow:
  - `doctor` → owner names specs → `AskUserQuestion` per spec (lead model, worker model; ≤ 4
    questions per batch, so two specs per batch) → spawn `spec-lead` per spec in the background;
  - each lead: `claim` → worker waves → commit per wave → `gate` → `status ready` → ≤ 30-line
    report;
  - orchestrator tells the owner which nodes are ready → owner reviews (window or "put a92 on
    stage") → "merge a92" → `merge` → push → `remove`.
- ⏸ **C2 owner checkpoint:** two throwaway one-file specs run end to end in parallel: both gated,
  one reviewed on stage, both merged, both worktrees gone, `main` history linear.

**Phase 5 — own-Editor mode (opt-in)**
- `links.py` package junctions (already built) + Library seed in `create --mode own-editor` +
  `OwnEditorLauncher` (`Unity.exe -projectPath`) + broker scoped to its own worktree's queue +
  skill routes that lead's escalations with `unity_instance`.
- Window: a mode toggle on the node with the disk cost shown before confirming.
- ⏸ **C3:** one spec in its own Editor gates while the stage Editor stays on `main` for the owner.

**Deferred:** linked tracked folders (D5 trigger); remote/cloud leads (a cloud session cannot
message back yet).

## 10. Open after planning

- Phase 0 has not run; P1, P2 and P3 can each overturn a decision (D7, D6, D9 in that order).
- The stale a72 worktree is left in place; C1 asks the owner whether to remove it.
