# DOTS Animation Toolkit — session handoff

**Written:** 2026-08-01 (rewritten at C3 close; updated after C4.2)
**Last commit:** `582e152`
**State:** **C3 is CLOSED.** Phases done: A, B, C0, C1, C2, C3. **C4 is in progress — phases C4.1 and C4.2 of 9 are done.**

**Verified via the Unity MCP:** compile clean, **205 EditMode + 48 PlayMode = 253, all passing, each in its real mode.**

**C4 progress — read `Docs/AnimationToolkit/Phase_C4_Plan.md` first.** It holds the 14 pieces, the nine phases, the traps carried in from C3, and the test-integrity standard.
- ✅ **C4.1 skeleton** — the four system groups, `ToolkitWorldControl`, `ConfigBootstrapSystem`, 12 tests.
- ✅ **C4.2 binding** — `RigBindingSystem` (§5.3), 7 tests, mutation-verified. Five files already promise this system exists; it rebuilds `RigPartRef` and `RigPartBinding.actorRoot` from `LinkedEntityGroup` after ECB instantiation, re-derives `phase01` per instance, then disables `RigBindingUninitialized`. Until it lands, baked instances are correct but instantiated copies are not.
- ⬜ C4.3 playback core · C4.4 events · C4.5 transform · C4.6 flipbook · C4.7 VAT+bounds · C4.8 LOD · C4.9 acceptance + smoke scene.

---

## Read before doing anything

- `Docs/AnimationToolkit/Phase_B_Architecture.md` — the normative spec. **111 KB — never read whole.** Grep headings, then Read with offset/limit. §5 is C4's territory.
- `Docs/AnimationToolkit/Phase_C3_Gate4.md` — the last gate: verdict, the 10 blocking items, the Resolution table, the "verified clean — do not re-litigate" list, and the A-4 ruling. Read the verified-clean list before re-auditing anything in C3.
- `Phase_C3_Gate4_Reviewer{A_Spec,B_Tests,C_Code}.md` — the three lenses verbatim, with `Library/PackageCache` citations for the Entities behaviour C3 depends on.
- Earlier gates (`Phase_C3_Review.md`, `Phase_C3_ReReview.md`, `Phase_C3_Gate3_Incomplete.md`) are history, superseded by Gate 4.

## The environment is not what older notes say

- **Unity MCP is connected** (`mcp__UnityMCP__*`, HTTP `127.0.0.1:8080`). Compile gate: `refresh_unity` → poll `editor/state` for `is_compiling: false` and `external_changes_dirty: false` → `read_console`. Tests: `run_tests` + `get_test_job`. Anything genuinely visual still needs the owner to look.
- **Always check the discovered test count, not just pass/fail.** `resultState: "Passed"` with `total: 0` is what a vanished suite looks like, and it is how the C3 PlayMode defect survived a whole build step and three static reviewers.
- Fallback when the Editor is closed: grep `Logs/Editor.log` (project-relative — the `%LOCALAPPDATA%` copy is a stub that always greps clean).
- **Grep `Library/PackageCache/<pkg>@<hash>/` before calling any Unity API.** The two worst bugs this package shipped both came from recalling semantics instead of reading them.

---

## C4 — the systems slice

The runtime that makes baked actors actually animate: transform + flipbook end-to-end, events, bounds, LOD. `Runtime/Systems/` now holds the four system groups, `ToolkitWorldControl`, `ConfigBootstrapSystem` and `RigBindingSystem`; the remaining eleven pieces are listed in the C4 plan.

**Start by grepping §5 of the architecture** for the system list and their contracts, then §8 M3 for module ownership and §11.2 for the test obligations.

### Load-bearing facts C4 inherits

- **`RigBindingSystem` is C4's, and five files already promise it exists.** Doc comments in `ActorAuthoring`, `ActorBaker`, `RigBindingBakingSystem`, `ActorStateComponents` and `PartComponents` reference it, now correctly marked forward-looking. It rebuilds `RigPartRef` and `RigPartBinding.actorRoot` from the `LinkedEntityGroup` after ECB instantiation (instantiate does not remap entity references inside dynamic buffers), then disables the spawn-remap tag. **Landed in C4.2.**
- **`PlaybackLayer.previousLoop` is read-only until C4's `CommandApplySystem` writes it.** If C4 forgets, every outgoing clip reverts to its authored loop mode mid-crossfade.
- **`RigPartRef` buffer order is unspecified.** C4 rebuilds it anyway; do not develop a dependency on bake order.
- **Never write `offsetBounds` into `RenderBounds` directly** — it is offset space. `ActorRestBounds` is in actor space and C3 produces it.
- **The sample phase (`SampleSettings.phase01`)** is baked per actor and specified to be re-derived per instance at spawn. See A18 + the closed A-4.
- Dense clip index = position in both `clips` and `sortedClipIds`. `SchemaVersion` is 2 with golden hash `0x7262FF88711EB9F9` pinned to it; a format change bumps both together.

### Hard rules (from `CLAUDE.md` and owner memory — these override defaults)

- Never `var`; never single-letter names; explicit types everywhere; names read like documentation.
- Never `.Run()` a job — `.Schedule()` / `.ScheduleParallel()` assigned to `state.Dependency`.
- `[ReadOnly]` from `Unity.Collections`, never `Unity.Entities`.
- Prefer `ISystem` + `[BurstCompile]`; no managed allocations in Burst jobs.
- Burst log strings: only `G/g/D/d/X/x` specifiers (BC1343); no `+` concatenation (BC1016). `FixedStringNBytes` interpolation **is** supported (Burst 1.8.29).
- A Bursted baking system's diagnostics are invisible to `Application.logMessageReceived` (main-thread only) — use `logMessageReceivedThreaded`.
- `LogAssert.ignoreFailingMessages` set in `[SetUp]` does nothing; UTF disposes that LogScope before the test body runs.

### Process

- Modules **C0–C8** in dependency order, each gated by an adversarial reviewer producing PASS/FAIL. **Gates are launched only when the owner asks.**
- **Commit to `main` after each module passes its gate.** Nothing has been pushed.
- The owner delegates architecture and process calls (stated 2026-08-01) — decide, record the decision with its reasoning and an explicit "what to revert" note, and keep moving. A spec/reality conflict still gets a **written amendment**, never a silent doc edit: that discipline is what three failed gates bought.

### If a gate is needed

Three narrow agents in parallel, one lens each (spec conformance / test integrity / code correctness), each appending to its own scratchpad file **as it goes**, results copied into `Docs/AnimationToolkit/` before the session ends. This shape completed all three lenses at Gate 4; two monolithic reviewers were killed by a watchdog and a third died on a usage limit. **Then run the suite** — Gate 4's most serious finding was invisible to all three readers and took ninety seconds of execution to surface.

---

## Lessons this package keeps re-teaching

1. **Closure is a property of the code, not of the note saying the code changed.** Verify against the shipped tree — never the CHANGELOG, a review doc's own closure table, or a previous session's summary.
2. **Reading the diff is not enough either.** Run the thing.
3. **A test that passes under both the correct and the broken implementation is worse than no test.** Three separate instances have now been found in this module: the deleted phase fixture, the surrogate-pair test, and the PlayMode smoke test that asserted only an assembly name. For any new test, state the mutation it catches.
4. **An amendment can be self-defeating.** A17 was well-reasoned, owner-approved, and its implementation produced the exact outcome it rejected. Check what an amendment *does*, not only what it argues.

## Unrelated host-game bug, still open

`Assets/_Scripts/Editor/StitchPunk.Editor.asmdef` has `"includePlatforms": []`, so editor code compiles into player builds and any player test run fails with ~58 compile errors. One-line fix: `["Editor"]`. Offered twice; the owner has not taken it. Not a package issue — and note the irony that the *correct* fix there is what broke the toolkit's PlayMode suite when applied to a test assembly (A17/A25).

Also: the working tree carries substantial **unrelated host-game shader work** (Painterly graphs, colour ramps, `Assets/Shaders/`). Do not commit it with package changes — stage `Packages/com.stitchpunk.dotsanimationtoolkit`, `Docs/AnimationToolkit`, and this file explicitly.
