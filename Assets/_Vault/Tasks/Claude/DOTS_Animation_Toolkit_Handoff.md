# DOTS Animation Toolkit — session handoff

**Written:** 2026-07-31
**State:** Build step **C3 is OPEN**. Its re-review came back FAIL from all three reviewers.
**Last commit:** `2bb8f32`
**Read before doing anything:** `Docs/AnimationToolkit/Phase_C3_ReReview.md` (the three full reviewer reports, verbatim) and `Docs/AnimationToolkit/Phase_B_Architecture.md` (the normative spec — 111 KB, **never read whole**; grep headings, then Read with offset/limit).

---

## What this project is

A commercially sellable Unity DOTS animation UPM package, `com.stitchpunk.dotsanimationtoolkit`, developed as an embedded package at `Packages/com.stitchpunk.dotsanimationtoolkit/`. AAA bar: no placeholders, stubs, or TODOs.

**Process the product owner mandated** (do not quietly drop it):
- Build modules **C0–C8** in dependency order.
- Each module is gated by an **adversarial Reviewer** producing a PASS/FAIL verdict. Reviewers are launched only when the owner asks.
- **Commit to `main` after each module passes its gate.**
- **Pause after each module** so the owner can run the Editor compile and Test Runner.

**Phases done:** A (audit), B (architecture), C0 (skeleton), C1 (M3 data slice), C2 (M1 authoring slice). **C3 (M2 entity baking) is in rework and not closed.**

---

## Where C3 stands

The original C3 gate rejected with six blocking items **B1–B6** and ten advisories **A1–A10**. A rework pass claimed all six closed. It had not closed B2 or B3; a self-audit caught those two, and the independent re-review then found B4 and B6 also still open.

**The lesson, and it has now recurred at every gate:** closure is a property of the code, not of the note saying the code changed. Verify each item against the shipped diff. Do not trust the CHANGELOG, the review doc's own tables, or a previous session's summary — including this one.

### Genuinely closed (verified independently by Reviewer A)
- **B1** — the §4.4 material↔texture comparison branch is now reachable via the new `Tests/PlayMode/VatMaterialProbe.shader`, plus the zero-warning negative case. Reviewer B tried to break it and could not.
- **B2** — part archetype now pinned: `PostTransformMatrix`, `AnimVisible`, flipbook and VAT technique components; root archetype exact in both directions.
- **B5** — code side: PlayMode asmdef is `"includePlatforms": ["Editor"]`, §1.3 amended (A17), C0 conformance updated.
- All six §8 M2 acceptance bullets map to real assertions (re-derived independently). C3 correctly omits M2's VAT-texture bullets — §9 puts `VatTextureBaker` in C6.

### Fixed in `2bb8f32`, still needs a compile + test pass
- **`AuthoringPathHash.PathOf` threw at bake on non-ASCII hierarchy paths.** All three reviewers found it independently. It budgeted 110 *characters* against a `FixedString128Bytes` capacity of 125 *UTF-8 bytes*; the constructor's `CheckCopyError` throws under `ENABLE_UNITY_COLLECTIONS_CHECKS` (always on in the Editor, the only place baking runs). Now budgeted in bytes, truncated from the left so the leaf survives, surrogate-safe, and copied via `CopyFromTruncated` so it can never throw. **No test covers it yet — write one** (see below).

---

## Blocking work remaining — do these before C3 can close

Ordered by my judgement of severity. Reviewer letters map to the reports in `Phase_C3_ReReview.md`.

### 1. Three doc comments assert the opposite of the code they document (A-3, C-3)
- `RigTargetBaker.cs` ~169-174 says *"A target id the rig does not declare is **not** reported here: §4.1 gives that error to `RigBindingBakingSystem`"* — twenty lines below the code that does report it.
- Also `ActorBaker.cs` ~370-371 and `RigBindingBakingSystem.cs` ~97-99.

### 2. A §4.1 error was relocated between systems with no amendment (A-3, C-4)
`RigTargetBaker` now reports the unknown-target error itself and withholds `RigPartBakeLink`, which makes the `RigBindingBakingSystem` branch that §4.1 normatively owns **unreachable**. Reviewer C traced that **three of the four Bursted error paths are now dead**, and that the A9 change silenced the only realistic one (actor entity present, `ClipRegistry` absent).

**This is the one genuine design decision left, and it is the product owner's call.** Two coherent options:
- **(a)** Bless the managed-baker location — it can name the object and pass a clickable log context — record an amendment, and delete the dead Bursted branches.
- **(b)** Revert to spec and report from the Bursted pass.

My recommendation is **(a)**, but it must be *recorded as an amendment*, not resolved silently. Silent resolution of spec/reality conflicts is the failure mode that has sunk C1, C2 and C3.

### 3. B4 was never done — 3 of its 5 items untouched (A-1)
The architecture diff edits exactly four places. §8 M2 and §4.6 were never edited. Still outstanding:
- §8 M2 **EXPOSES** lists 4 `RigTargetAuthoring` fields; 7 ship.
- §8 M2 **OWNS** omits `RigPartBakeLink` and `StartingLayerState`.
- §4.6 (~line 484) still says `ActorRestBounds` carries the `offsetBounds` union — a contradiction C4 will read.
- A18 never states the `>> 8` phase derivation (B4 item 4, half-closed).

### 4. B6 — shipped test counts still wrong (A-2)
`Documentation~/index.md:71` says "164 EditMode tests"; actual is **192**. `CHANGELOG.md:107` says "66". This is C2's advisory D9 open for a **third** gate.

### 5. `LogAssert.ignoreFailingMessages = true` spans the whole bake (B-1, C advisory)
In `BakingTestWorld.Bake`. It strips UTF's automatic unexpected-error failure, and only ~5 of 26 acceptance tests assert on `ToolkitErrors` themselves — so ~21 would stay green with the bake spewing toolkit errors on every part. A real fix for a real problem that overshot. Suggested remedy: a `TearDown` assertion that `ToolkitErrors.Count` equals an expected value, defaulting to 0 with per-test opt-in.

### 6. Two tests cannot fail the way they claim (B-2, B-4)
- `AStrayPartDoesNotEnlargeTheRestBounds` asserts only `Max.x < 50f`; a zero box passes. Assert the exact box the no-stray test already pins.
- `TwoActorsFromOneClipSet_..._AndTheSameOneOnRebake` re-bakes the **same GameObject instances**, and instance IDs are stable for an object's lifetime — so an instance-id-derived phase passes it. Amendment **A18 is effectively unpinned**. Rebuild fresh instances with identical names and sibling indices.

### 7. `PathOf` has no test at all
It runs on every bound part. Cover: a deep non-ASCII path (must not throw, must truncate), leaf preservation, the null case, and surrogate pairs.

---

## Advisories

~30 across the three reports, in `Phase_C3_ReReview.md`. Ones worth pulling forward:
- **C-7** `ClipRegistryBuilder.BuildInvocationCount` — its doc claims nothing at runtime can see it, which is **false**: the Authoring asmdef has empty `includePlatforms`, so it ships in players and public `Build` mutates it there. Guard with `#if UNITY_EDITOR`.
- **C-9** Both path walks read ancestor names and sibling indices with **no bake dependency**, so an ancestor rename makes incremental and clean bakes diverge — contradicting `ComputeSamplePhase`'s "pure function of the source" claim.
- **C-5** `GetComponent<Transform>` also triggers `DependOnParentTransformHierarchy`, so every ancestor edit rebakes every part, while `CaptureRestPose` reads only local TRS. Over-invalidation, not incorrectness.
- **A-(d)** §5.2 lists `AnimLod` unconditionally while a new test pins its absence as conformant — a conflict now codified in a test rather than amended.
- **B-10** a test named `..._LogsExactlyOneWarning` asserts two.

---

## Hard rules (from `CLAUDE.md` and owner memory — these override defaults)

- Never `var`; never single-letter names; explicit types everywhere; names read like documentation.
- Never `.Run()` a job — `.Schedule()` / `.ScheduleParallel()` assigned to `state.Dependency`.
- `[ReadOnly]` from `Unity.Collections`, never `Unity.Entities`.
- Prefer `ISystem` + `[BurstCompile]`; no managed allocations in Burst jobs.
- Burst log strings: only `G/g/D/d/X/x` specifiers (BC1343); no `+` concatenation (BC1016).

## Environment gotchas that cost this session real time

- **There is no Unity MCP and no Editor bridge.** Never attempt `mcp__unity-mcp__*`. The compile gate is: the owner focuses Unity and reports, or grep `C:/Users/spenc/AppData/Local/Unity/Editor/Editor.log` for `error CS` / `BC`. Anything visual, ask.
- **Unity package sources are on disk at `Library/PackageCache/<pkg>@<hash>/`. Grep them to confirm an API before calling it.** This session shipped a call to a `Baker.GetComponentsInChildren<T>(bool)` overload that does not exist, and the whole `PathOf` bug came from recalling `FixedString` semantics instead of reading them. Both were one grep away.
- **A Bursted baking system's diagnostics are invisible to `Application.logMessageReceived`** — it is main-thread only. Use `logMessageReceivedThreaded`, as UTF's own `LogScope` does. Any future log-capturing harness (C4's included) must do this.
- **`LogAssert.ignoreFailingMessages` set in `[SetUp]` does nothing.** UTF wraps each setup method in its own `LogScope` and disposes it before the test body runs.
- **Reviewers must be split and must write findings incrementally.** Two monolithic reviewers were killed by a 600 s no-progress watchdog and lost everything. Three narrow parallel agents, each appending to its own scratchpad file as it goes, works.
- **Scratchpad files do not survive a session.** Copy anything durable into the repo, as `Phase_C3_ReReview.md` now is.

## Test suites

Run from **Window ▸ General ▸ Test Runner**; there is no headless runner.
- EditMode: 192 tests.
- PlayMode: 26 tests, **Editor-only by design** (amendment A17) — Unity's baking pipeline has no player-side equivalent, so "Run all tests (Player)" cannot execute it.

## Unrelated host-game bug, still open

`Assets/_Scripts/Editor/StitchPunk.Editor.asmdef` has `"includePlatforms": []`, so editor code compiles into player builds and any player test run fails with ~58 compile errors. One-line fix: `["Editor"]`. Offered; the owner has not taken it. Not a package issue.

---

## Suggested first moves for the next session

1. Ask the owner to decide **item 2** (where the unknown-target error lives) — it is the only blocking item needing a product decision, and everything in `RigTargetBaker`/`RigBindingBakingSystem` depends on the answer.
2. While waiting, do items 3, 4, 6 and 7 — all unambiguous.
3. Then item 1 (doc comments), which item 2's answer partly rewrites anyway.
4. Then item 5.
5. Re-run the three-way review on the full C3 diff, then hand back for compile + Test Runner.
6. Only then C4 (systems slice), C5–C8.
