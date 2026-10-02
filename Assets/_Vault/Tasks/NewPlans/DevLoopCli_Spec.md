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
| D2 | Python, following `worktree_toolkit`'s layout. `compile-gate.sh` is bash but has no logic; dag discovery, rsp rewriting and staleness need real code. |
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
| T5 tests [parallel-safe] | `Tools/devloop/tests/test_gate.py`, `Tools/devloop/tests/test_lint.py` | Fixtures that actually fail when reverted: a stale-asmdef → exit 2; a renamed output → the CS0122 regression; an injected CS0029 → exit 1; one lint hit per rule. No fixtures for argument parsing. |
| T6 wire-in [parallel-safe] | `CLAUDE.md`, `Tools/devloop/README.md` | CLAUDE.md compile-gate line per D8; the worker-brief checklist gains "run the CLI gate before writing your report" — this is where the respawn savings come from. |
| T7 ledger fix [parallel-safe] | `.claude/hooks/subagent_ledger.py`, `.claude/hooks/agent_result_ledger.py` | Find why `peak_tokens`/`turns`/`reads` write `0` on all 338 rows and make them record. Prerequisite for ever proving §2. |

## 5. Checkpoints
- **C1** — the gate's verdict output shape (owner reads it in every worker report).
- **C2** — after one real spec wave with T6 live: did the gate actually prevent a respawn? If not, lint and index are
  both cut and the gate stays as a plain convenience.

## 6. Log
- 2026-10-02: probe + spec. Gate proven on `com.dotsanimationtoolkit` (table in §1), three traps recorded, scope
  narrowed to the dev loop by the owner, D1–D10 settled. Not yet built.
