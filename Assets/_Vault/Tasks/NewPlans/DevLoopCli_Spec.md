# Dev-Loop CLI — offline gate, lint, symbol index (2026-10-02)

Owner, 2026-10-02: "adding a cli tool to the dots animator toolkit so you as an ai can build using it easier …
do you think it would be helpful/cheaper token wise?" Scope answered same day: **dev loop only** — no offline
content authoring (it duplicates the Editor, risks GUID/YAML corruption, and the Editor drive is what finds real
defects: A103's VAT Bake toggle bug passed every fixture).

Honest framing, recorded so it is not re-litigated: the **gate** is the piece that clearly pays, the **index** is the
largest pure-token win but the least certain, and **lint** is the weakest phase. The biggest token sink in this
project — UI captures and the owner's visual review loop — is untouchable by any CLI.

## 1. Measured, 2026-10-02 (probe, not estimate)

Offline Roslyn compile of `com.dotsanimationtoolkit`, Editor closed, scratchpad output:

| Assembly | Files | Errors | Time |
|---|---|---|---|
| `DotsAnimationToolkit.Runtime` | 78 | 0 | 4s |
| `DotsAnimationToolkit.Editor` | 269 | 0 | 6s (18s re-globbed cold) |
| `DotsAnimationToolkit.Tests.EditMode` | 561 | 0 | 4s |

Zero false positives; an injected `int x = "not an int";` was caught as `CS0029`. The gate runs the real Entities
source generators — Unity's harvested rsp carries 315 references, ~40 defines and **18 analyzers** including
`JobEntityGenerator`, `SystemGenerator.*`, `Unity.Entities.Analyzer`, `Unity.UIToolkit.SourceGenerator`.

**Three traps, all paid for in this probe:**

- **Never hand-roll the reference set.** Referencing all 145 `Library/ScriptAssemblies/*.dll` plus every
  `Managed/UnityEngine/*.dll` produced 10 phantom errors from two colliding `AABB` types. Real asmdefs reference
  7–8 assemblies. Harvest `Library/Bee/artifacts/*.dag/<Assembly>.rsp` instead — it is exactly what Unity ran.
  Match `<Assembly>.rsp` exactly: the same dir also holds `<Assembly>.mvfrm.rsp`, a different and wrong file.
- **Redirecting `-out:` must preserve the assembly *name*.** Writing `…Editor.rg.dll` changed the assembly identity,
  `InternalsVisibleTo("DotsAnimationToolkit.Editor")` stopped matching, and 58 phantom `CS0122`s appeared. Redirect
  the directory only.
- **Argument list overflow.** 315 refs + 561 sources exceeds the Windows command line ("Argument list too long",
  silently logged, reported as 0 errors in 42ms). Everything goes through a response file. A suspiciously fast pass
  is a lie.

Also: csc must run with cwd = project root (rsp paths are project-relative), and `cd` must stay inside a subshell —
a bare `cd` moves the session cwd and breaks every relative `.claude/hooks` path.

## 2. Why it pays (and where the numbers are missing)

- **MCP gate** = `refresh_unity` → poll `isCompiling` → `read_console`: 3+ round trips, ~2–4k tokens, 30–60s domain
  reload. **CLI gate** = one call, ~150 tokens, 4–18s, Editor closed. At 8–15 gates a session that is ~20–40k tokens,
  call it 3–6% of a large session. Real, not transformative.
- **The actual money is avoided worker respawns.** CLAUDE.md forbids MCP gating inside agents, so worker 7 of 14
  cannot check its own edit; a CS error surfaces at the orchestrator gate, and a capped agent is never resumed — so
  the fix is a fresh worker and a rebuilt context, 20–60k tokens. One avoided respawn ≈ a whole session of gate
  savings. This is the justification for T6.
- **⚠ No measured baseline exists.** `.claude/subagent-ledger.tsv` has 338 rows and every `peak_tokens`, `turns` and
  `reads` column is `0` — the budget hook has never recorded anything, despite that being its stated purpose
  (`project_subagent_budget_enforcement`). T7 fixes it; without T7 none of the above can ever be proven.

## 3. Decisions (made, do not re-ask)

| Id | Decision |
|---|---|
| D1 | Lives at repo-root `Tools/devloop/`, **not** in `com.dotsanimationtoolkit`. A sellable package should not ship AI dev tooling, the gate is identical for all four packages, and a root-level folder is outside Unity's import entirely. |
| D2 | Python, following `worktree_toolkit`'s layout: entry `Tools/devloop/devloop.py`, package `Tools/devloop/devloop_tools/`. `compile-gate.sh` is bash but has no logic; dag discovery, rsp rewriting and staleness need real code. **No pytest on this machine** (checked 2026-10-02) — tests use `unittest`, as `worktree_toolkit/tests` already does. |
| D3 | Harvest Unity's rsp. Never construct the reference set. |
| D4 | Output redirect changes the directory, never the assembly name. |
| D5 | Sources are re-globbed from the asmdef folder, so files a worker just added are gated. Unity's stale source list is discarded. |
| D6 | Staleness fails loud: any `.asmdef` or `package.json` newer than the harvested rsp → print `GATE STALE — reopen the Editor once` and exit 2. A false pass is worse than no gate. |
| D7 | Package-agnostic: `python Tools/devloop/cli.py gate <package-or-assembly>`. Covers all four packages and the game's own assemblies. |
| D8 | The gate is a **floor**: no Burst `BC####`, no ILPostProcessing, no bake, no runtime. CLAUDE.md's compile gate becomes "CLI gate first, MCP for bake/Burst/runtime" — it does not replace the Editor pass. |
| D9 | `lint` invents **no new rules**. Most of R01–R22 is pixel/layout and ungreppable, and `EditorStyleConformanceTests.cs` (324 lines, Conformance_I/J/K) already ratchets the greppable part. It is a 2-second offline front door to checks that exist, plus the four mechanical repo rules, diff-scoped. |
| D10 | P3 (`where`/`members`/`callers`) is **deferred** until T7 gives real data. 565 files / 54,778 lines behind a 300-line read guard makes it the biggest pure-token win, but a stale index is worse than none. |

## 4. Worker tasks (disjoint files; interfaces pinned below so they parallelise)

Pinned interface, so T1/T2/T3 can be built at once without seeing each other:

```python
# rsp_harvest.py
def find_dag_directory(project_root: str) -> str                       # newest Library/Bee/artifacts/*.dag holding a known dll
def harvest(project_root: str, assembly_name: str) -> HarvestedCompile # .flag_lines, .source_files, .rsp_path
def is_stale(project_root: str, assembly_name: str) -> StalenessVerdict # .is_stale, .reason
# gate.py
def gate_assemblies(project_root: str, assembly_names: list[str]) -> GateReport  # .per_assembly, .overall_passed
```

| Task | Files | Work |
|---|---|---|
| T1 rsp harvest [parallel-safe] | `Tools/devloop/devloop/rsp_harvest.py` | Dag discovery, rsp parse into flag lines vs source lines, source re-glob from the asmdef folder, output-path rewrite preserving the assembly name, staleness verdict per D6. |
| T2 gate [parallel-safe] | `Tools/devloop/devloop/gate.py` | Write the response file, invoke `csc.dll` via Unity's `dotnet.exe` with cwd = project root, dedupe `error CS` lines, emit a ≤10-line verdict (`GATE PASS` / per-assembly `files, errors, seconds` / first 20 unique errors). Exit 0/1/2. |
| T3 cli + config [parallel-safe] | `Tools/devloop/cli.py`, `Tools/devloop/devloop/packages.py` | `gate` / `lint` subcommands; package → assembly-list map for the four packages plus `Assembly-CSharp*`; `UNITY_DATA` override with a clear "set UNITY_DATA" failure. |
| T4 lint [parallel-safe] | `Tools/devloop/devloop/lint.py` | Diff-scoped (`git diff --name-only` default, explicit paths accepted). Rules: no `var`, no single-letter identifiers, no `.Run()` on a job, `EnabledRefRW/RO` named `fooEnabled`, and no `Handles.`/`OnGUI`/`GUILayout` under a package `Editor/`. One line per hit, `file:line  rule  text`. |
| T5 tests [wave 2] | `Tools/devloop/tests/test_gate.py`, `Tools/devloop/tests/test_lint.py` | Fixtures that actually fail when reverted: a stale-asmdef → exit 2; a renamed output → the CS0122 regression; an injected CS0029 → exit 1; one lint hit per rule. No fixtures for argument parsing. |
| T6 wire-in [wave 2, orchestrator] | `CLAUDE.md`, `Tools/devloop/README.md` | CLAUDE.md compile-gate line per D8; the worker-brief checklist gains "run the CLI gate before writing your report" — this is where the respawn savings come from. |
| T7 ledger fix [parallel-safe] | `.claude/hooks/subagent_ledger.py`, `.claude/hooks/agent_result_ledger.py` | Find why `peak_tokens`/`turns`/`reads` write `0` on all 338 rows and make them record. Prerequisite for ever proving §2. |

## 5. Checkpoints
- **C1** — the gate's verdict output shape (owner reads it in every worker report).
- **C2** — after one real spec wave with T6 live: did the gate actually prevent a respawn? If not, lint and index are
  both cut and the gate stays as a plain convenience.

## 6. Log
- 2026-10-03: **Repo-wide conformance done** (owner: "fix the 77 var violations in the movement toolkit and the
  other violations"). 1057 files now lint clean; game + both DOTS toolkits + worktree toolkit all gate clean.
  219 findings fixed in total: movement toolkit 92, animation toolkit 11, game 107, plus 11 that appeared mid-pass.
  - **A misnamed `EnabledRef` is invisible behind `var`.** The rule needs the type written down, so converting
    `foreach (var (...))` tuples to explicit types exposed names that had always been wrong
    (`EnabledRefRW<PlayerInteractable> interactableEnabled`). The count went **up** from 11 to 22 before it went
    down, and the enabled-ref pass has to run *after* the no-var pass. Remember this ordering.
  - **One worker per assembly.** Two workers editing the same assembly each compile the other's half-finished
    edits during their own gate run. Grouping by assembly removed that entirely; where it was unavoidable (the 36
    files of `StitchPunk.Systems`) the briefs said to attribute an error in an unowned file to a peer and not touch
    it, and the workers did exactly that.
  - Three gate/lint defects the work itself exposed, all fixed: the source re-glob reached into **nested
    asmdefs** (`StitchPunk.Tests` swallowed `Tests/PlayMode`, phantom CS0246); one shared temp build directory
    meant parallel gates **overwrote each other's `.ref.dll`**; and the "suspiciously fast" guard was a false
    positive, since a six-file assembly honestly compiles in under a second - it now checks that `-out:` exists
    rather than timing the run.
  - `no-single-letter-names` had 9 more false positives: `return i;` and `instance = this as T;` contain no
    declaration, but any `<word> <letter>;` matched. Keywords are now rejected in the type position.
  - Deliberately preserved: `for`-loop counters, `rhs`/`km` in the D*Lite key maths, and the intentionally
    descending comparison in `VoiceSelectionSystem`'s comparer.
- 2026-10-03: ⚠ **No Editor pass yet.** All of the above is gate-verified only - `error CS####` and nothing more.
  Burst `BC####`, baking and runtime behaviour are unchecked, and ~1050 files changed names or declaration types.
  The EditMode/PlayMode suites and a play-test are still owed before this is trusted in a build.
- 2026-10-02: ⚠ **Real convention drift the lint found, for the owner to schedule:**
  `com.dotsmovementtoolkit` has **77 genuine `no-var` violations** across 12 files (plus 15 single-letter and 4
  enabled-ref). These are true positives - `var registryEntity = ...`, `foreach (var horde in ...)` - so that
  package was simply never held to the no-`var` rule. Mechanical but not trivial; not touched here.
- 2026-10-02: T7 ledger - partially fixed, see §7.
- 2026-10-02: **BUILT.** `Tools/devloop/` ships `gate` and `lint` with 9 unittest fixtures (3 proven to fail when
  broken). All four packages gate clean: animation toolkit 6 assemblies / 561 files in 37s warm, movement 3 in 9s,
  worktree 2 in 5s. Wired into CLAUDE.md per D8, including the worker-brief line that is the whole point.
  P3 (`where`/`members`) still deferred per D10.
  Four defects the drive found that review would not have:
  - **A false pass.** Unity's rsp references siblings as prebuilt `Library/Bee/.../Runtime.ref.dll`, so gating
    `Editor` compiled against the last Editor-built Runtime; editing Runtime+Editor together could pass where Unity
    fails. Fixed by chaining each fresh `.ref.dll` forward and reporting a dependent as `SKIPPED` when its upstream
    failed. Proven by renaming a Runtime method and watching Authoring fail with CS0117.
  - **An exit-code lie.** A missing rsp (an assembly Unity never compiled, e.g. `PlaytestCopilot.Tests.Runtime`)
    escaped as a traceback and exit 1 - read by a caller as "compile errors". Now `GATE UNAVAILABLE` and exit 2.
  - **A lint with no signal.** `no-single-letter-names` fired 141 times, all on conforming code (`for (int i`,
    `float x = rotation.x`). Narrowed to parameters and fields; `no-run-on-jobs` also exempts a PascalCase receiver
    (`HealthScan.Run(context)` is a static call). Package now reports 11 findings, 8 of them real (see below).
  - **A truncated report read as complete.** `format_report` capped at 40 lines in silence, which is how a
    141-finding run was mistaken for 40 of one rule. It now prints a per-rule tally and an explicit omitted count.
- 2026-10-02: ⚠ **Open for the owner, not fixed by me:** the lint's 8 `enabled-ref-naming` findings are genuine
  convention violations - `EnabledRefRW<AnimationCommandPending> commandPendingEnabled` in `Runtime/Api/PlaybackApi.cs`
  (7) and `EnabledRefRW<AnimEventMask> eventMaskEnabled` in `Runtime/Systems/EventWindowSystem.cs` (1), where the
  rule expects the full component name. Renaming them is a **breaking API change** for any caller using named
  arguments on a sellable package, so it is a product call rather than a cleanup. The 3 `int j = i;` hits are
  cosmetic; leave them.
- 2026-10-02: wave 1 spawned — T1 rsp harvest, T2 gate, T3 cli+packages, T4 lint, T7 ledger, five parallel workers
  on disjoint files with the §4 interface pinned in every brief. T5/T6 held for wave 2: tests need the modules to
  exist, and the wire-in text should quote a command already proven to run.
- 2026-10-02: probe + spec. Gate proven on `com.dotsanimationtoolkit` (table in §1), three traps recorded, scope
  narrowed to the dev loop by the owner, D1–D10 settled. Not yet built.

## 7. T7 ledger: what is actually fixed

Three passes, and the honest state is "no longer lying, not yet proven right":

- **Fixed:** `SubagentStop` read a payload key that does not exist (`agent_transcript_path` vs `transcript_path`),
  so it never ran. `PostToolUse` fires on the Agent tool's *async launch* acknowledgment, when the transcript
  exists but is empty - it wrote a zero row at launch that then claimed the agent id, which is how all 346 rows
  ended up empty. It now refuses to write any row whose metrics are all zero, keyed off the data rather than the
  response wording (matching the wording was tried and failed against the real payload shape).
- **Fixed:** rows were briefly populated with the **orchestrator's own** numbers (`claude-opus-5`, 189k, 144
  turns) because `transcript_path` on a `SubagentStop` payload is the parent's transcript. Real subagent
  transcripts are at `<session-uuid>/subagents/agent-<id>.jsonl`, with an `agent-<id>.meta.json` sibling carrying
  `agentType`. A guard now refuses to measure a non-`subagents/` transcript and logs to
  `.claude/hooks/ledger-errors.log`; 5 bogus rows were removed.
- **Fixed:** dedup treated a zero row as "already recorded", so a stale launch row blocked the real measurement
  forever. Only rows with non-zero `peak_tokens` now count as recorded.
- **Fixed:** the row's `agent_id` came from the payload, which carries an identifier that has no transcript of its
  own (`a3af24490c6487388` was written for a measurement actually taken from `agent-aad594dca0bb979fc.jsonl`). The
  transcript filename now wins, so the id always names what was measured.
- **⚠ Not proven:** exactly one real row has been produced end to end (`verifier, claude-sonnet-5, 56389
  tokens, 4 turns`) and it predates the agent_id fix. The id correction needs one more real spawn-and-stop cycle to
  confirm. Until then, treat the ledger as unverified and take subagent token figures from the harness
  notifications, not from the TSV.
- **⚠ Known gap:** `agent_type` is blank on `SubagentStop`-sourced rows unless the `.meta.json` is found. The
  346 historical zero rows are left in place as a record that those agents ran; their metrics are simply unknown.
