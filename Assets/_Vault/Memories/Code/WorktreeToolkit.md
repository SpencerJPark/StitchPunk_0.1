---
tags: [memory, code, tooling, git, worktree]
related: "[[Editor]], [[Skills]]"
---

# Packages/com.worktreetoolkit — Worktree Toolkit

Third embedded package (after the animation and movement toolkits). Parallel AI specs run in git
worktrees; the owner reviews one by swapping it into the open Editor ("the stage"). Plan and
decisions: `Assets/_Vault/Tasks/NewPlans/WorktreeToolkit_Roadmap.md`; module-level build spec:
`WorktreeToolkit_Phase1_CLI.md` beside it.

## What exists (and how to see it)

Built 2026-09-14 with the Editor closed: Phase 1 only, the Python CLI. No C# exists yet, so Unity
has nothing to compile in this package. List the modules with
`ls Packages/com.worktreetoolkit/Tools~/worktree_toolkit/` and the commands with
`python Packages/com.worktreetoolkit/Tools~/worktree.py --help`.

Run the fixtures (temp repos only, never the real project):
`(cd "Packages/com.worktreetoolkit/Tools~" && python -m unittest discover -s tests -t .)`.
**Always use the subshell parentheses** (trap 4).

## Rules

- All git logic lives in `Tools~/worktree_toolkit/` and runs through `git_runner.run_git`. The
  future Editor window and gate broker shell out to `worktree.py --json`; never re-implement a git
  query in C#.
- State lives in `<git-common-dir>/worktree-toolkit/` (`state.json`, `queue/`, `results/`,
  `stage.lock`, `broker-heartbeat.json`). It is never tracked; every worktree sees the same copy.
- Exit codes: 1 usage · 2 refused (nothing changed) · 3 no broker · 4 gate timeout · 5 git failed.
- `.gitignore` carries `!/Packages/com.worktreetoolkit/`. Without it the `/[Pp]ackages/*/` rule
  silently drops every new file.

## Traps (each cost real time on 2026-09-14)

1. **`git worktree remove --force` deletes the contents of every NTFS junction inside the worktree**,
   at any depth (git 2.43.0.windows.1; plain `remove` refuses instead). Python 3.12 `shutil.rmtree`
   does not follow junctions. `lifecycle_ops.remove_worktree` unlinks every link first and re-scans
   before git runs; do not reorder it.
2. **The `WorktreeCreate` hook payload has no `base` field** — only `session_id, transcript_path, cwd,
   prompt_id, hook_event_name, name` — whatever the docs say. The hook picks trunk itself.
3. **Git Bash `pwd` gives `/tmp/...`, and the Write tool resolves that to `C:\tmp`**, outside the
   worktree. Agents take their worktree path from `git rev-parse --show-toplevel`.
4. **A bare `cd` in a Bash call moves the whole session's cwd.** Every `.claude/hooks` entry is a
   relative path, so the ledger failed, and the worker read guard blocked Bash/Read for six workers
   (python exits 2 on a missing script, which is a blocking hook code). Use `(cd x && cmd)`.
5. `spec-lead` has `isolation: worktree`, so spawning another one for an *existing* worktree creates a
   new worktree instead. Use a plain `worker` with the absolute path for conflict fixes.
6. **Windows MAX_PATH:** with `core.longpaths` unset, `git worktree remove` drops git's record, then
   fails `Filename too long` and leaves the folder. `remove_worktree` finishes that one case with a
   `\\?\` rmtree (fixture `test_remove_long_paths`). `worktree.py` sets `sys.dont_write_bytecode` so
   agents running the CLI never leave `__pycache__` in worktrees.
7. **`EditorWindow.CreateGUI` is not virtual** (reflection on 6000.5: no member). Unity calls it by
   name, like `OnGUI`. `public override void CreateGUI()` is CS0115; write `public void CreateGUI()`.
8. **Focusing the Editor mid-wave imports half-finished work.** A worker's new file references a
   type another worker hasn't written yet, so the focus-triggered compile errors, and those errors
   block `run_tests` from starting ("tests did not start within timeout"). Gate a wave only after
   every worker in it has reported.
9. **`WorktreeCliClient.RunAsync` must resolve everything on the main thread.** `EditorPrefs.GetString`
   (the Python-path override) and `Application.dataPath` throw "can only be called from the main
   thread" on a `Task.Run` thread. The window's `ContinueWith` swallowed the fault, so its
   refresh-in-progress flag stuck true and the window stayed empty with no error.
10. **Never add to `EditorApplication.delayCall` from a thread-pool continuation.** Even with the fault
    fixed and the CLI returning correct JSON in 551 ms, the callback was silently lost and the window
    stayed empty. The window now stores its pending `Task`s in fields and drains completed ones in its
    `EditorApplication.update` handler, on the main thread, before the has-focus check.
11. **An `EditorWindow`'s private fields survive a domain reload; non-serializable ones come back null.**
    `hasRequestedStateDirectory` came back `true` while the `Task` and `GateRequestStore` came back null,
    so the queue never resolved again. A bare `[Serializable]` DTO came back as an all-defaults
    instance. Mark per-session bookkeeping `[System.NonSerialized]`.
12. **Small-model leads skip steps.** In the C2 dry run a haiku `spec-lead` gated before running
    `claim`, so the hook-made worktree was still on `worktree-agent-…` with no spec. Enforce order in
    the CLI (`gate` refuses unclaimed worktrees), not just in agent instructions.
13. **The owner's stage is never clean.** Every stage-moving command (review, return, stage-commit,
    merge) refuses only on uncommitted edits to files the move changes (D9b). A blanket "no tracked
    modifications" rule blocked the whole workflow in practice. git itself still refuses real
    overwrites.
14. **Window captures in this Linear-colour project need a linear RenderTexture.** A default
    `new RenderTexture(w, h, 24, ARGB32)` for `GUIView.GrabPixels` brightens everything (a 22/255
    canvas came out mid-grey), which would have sent the redesign chasing wrong colours. Use
    `RenderTextureReadWrite.Linear` and a linear `Texture2D`.
15. **Built-in 16 px icons stretched to tile size look pixelated** (owner, round 2). Tile symbols are
    Painter2D vector glyphs (`WorktreeGlyphs`); built-in icons stay at their native 16 pt.
16. **A C# `'\\'` literal written through a Bash heredoc into Python lost its escaping twice.** Build
    the characters with `chr(92)` and verify the byte count before trusting the edit.
17. **Bare fixture names gated nothing and reported a pass** (A93–A95 batch, 2026-09-14). `TestCapture`
    anchored `^<name>`, but Unity matches `groupNames` against the full name, namespace first; and the empty
    root suite counted as "1 passed". Fixed at the batch close: the filter matches the name as a whole dotted
    segment anywhere, an empty suite counts as nothing, and `GateBroker` refuses a gate whose named fixtures ran
    zero tests. Leads still pass namespace-qualified names and compare the passed count with what they wrote.
18. **Parallel leads' gates are refused with "Unity is compiling"** while another gate's compile runs. It is
    a retry, not a failure; the lead gets `refused` and gates again.
19. **Leads gate only their own fixtures, so package-wide conformance first runs on trunk.** In the A93–A95
    batch Conformance_D (host asset paths) failed at integration on two branches. Put
    `DotsAnimationToolkit.Tests.EditMode.PackagingConformanceTests` in every lead's wave gate.
20. **The stage moves under you.** A lead's gate detaches the stage (`busyWith: gate <id>`). Check
    `list --json` `stage.busyWith` is null before a merge, an execute_code, a refresh or a test run on the stage.
