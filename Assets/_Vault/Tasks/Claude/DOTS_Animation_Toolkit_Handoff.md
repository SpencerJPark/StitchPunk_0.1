# DOTS Animation Toolkit — session handoff

**Written:** 2026-08-01
**Last commit:** `bce381e`
**State:** C3's rework is complete and verified green. **The C3 gate has NOT been run** — the review was launched and died on a usage limit after producing two findings out of three reviewers. Both findings were real, were verified, and are fixed. The rest of C3 is unreviewed.

**Read before doing anything:**
- `Docs/AnimationToolkit/Phase_C3_ReReview.md` — the second gate's FAIL, three reviewers verbatim. Still the definitive list of what C3 was rejected for.
- `Docs/AnimationToolkit/Phase_C3_Gate3_Incomplete.md` — the aborted third attempt. Two findings, then nothing.
- `Docs/AnimationToolkit/Phase_B_Architecture.md` — the normative spec. **111 KB — never read whole.** Grep headings, then Read with offset/limit.

---

## Do this first

**Re-run the three-way gate review.** It is the only thing standing between C3 and closed. Everything else below is context for it.

Launch **three narrow agents in parallel**, one per lens — spec conformance, test integrity, code correctness — each appending findings to its own scratchpad file *as it goes*. This shape is not optional: two monolithic reviewers were killed by a watchdog and lost everything, and the third attempt lost Reviewer C entirely to a usage limit. Incremental writes are what survive. **Copy the results into `Docs/AnimationToolkit/` before the session ends; scratchpads do not survive.**

Scope: `git diff 026a902..HEAD` over `Packages/com.stitchpunk.dotsanimationtoolkit` and `Docs/AnimationToolkit`.

**Reviewer C's lens has had zero coverage across the entire third attempt.** Point it hardest at:
1. **Can `ActorBakeFailed` go stale?** `ActorBaker.MarkBakeFailed` writes a `[BakingType]` tag on its three bail-out paths. If that tag survives an incremental re-bake that then *succeeds*, `RigBindingBakingSystem` would suppress a genuine diagnostic forever. I believe Entities reverts a baker's components when it re-runs, but **I did not verify it against `BakedEntityData`/`BakeDependencies` sources.** This is the single most likely place a real defect is hiding.
2. **Did the `IBaker` refactor change the hash value?** It must not — baked data would shift silently. The walk order should be identical (leaf, then `GetParents` in order), but confirm.
3. `AuthoringPathText.TakeTrailingBytes` — surrogate handling, the `candidateIndex > 0` condition, the `.../` marker arithmetic.
4. `ComponentLookup<ActorBakeFailed>.HasComponent` on a zero-sized tag inside a `[BurstCompile]` `IJobEntity` — confirm Burst-legal against Entities source.

---

## Verification status

**Compile: clean.** **All tests pass** — owner ran both tabs 2026-07-31. Counts as of `bce381e`: **205 EditMode, 27 PlayMode** (26 baking acceptance + 1 smoke).

The PlayMode Console legitimately carries 4 toolkit errors and ~15 warnings after a run — several acceptance tests deliberately provoke a diagnostic and assert on it. Each such test declares its expected error count; every other test is held to zero. Do not "fix" those messages.

Three things were confirmed at runtime rather than by inspection, via the diagnostic tally matching the fixtures exactly:
- **A22's tag works** — `"no earlier message explained why"` never printed; without the tag the validation-errors fixture's three parts would each have produced it.
- **Burst `FixedString` interpolation works** — `Rig part 'Actor/VatBody'` came out of the Bursted job.
- **The URP probe shader works** — `binds '_VatBoneTex' to 'StaleBoneTex'` still appears, and that warning is only reachable through a material genuinely declaring `_VatBoneTex`. The B1 evidence survived the rewrite.

---

## What the second gate rejected, and what was done

All six blocking items are addressed. **Verify each against the shipped diff, not against this table** — that instruction has been earned at every gate, and I violated it myself this session (see "Mistakes I made" below).

| Item | Resolution |
|---|---|
| **1** (C-3) | Three doc comments that asserted the opposite of their code, all rewritten. |
| **2** (Finding 5 / C-4) | **Amendment A22**, owner-ratified 2026-07-31. `RigTargetBaker` owns the unknown-target-id error; only managed code can name the object and the rig, say which field to fix, and give a clickable context. New `[BakingType] ActorBakeFailed` tag makes the binding pass's silence conditional on an explicit signal rather than inferred from "no registry" — the real defect under the paperwork. Two guards unreachable by construction deleted. |
| **3** (B4) | §8 M2 OWNS/EXPOSES corrected; §4.6's contradiction resolved by **A24**; **A18** states the `(pathHash >> 8) × 2⁻²⁴` derivation; **A23** records `AnimLod` as opt-in; §1.3's PlayMode platform cell fixed. |
| **4** (B6) | Counts recovered from git: **8 → 106 → 192 → 205**. |
| **5** (B-1) | `ExpectToolkitErrors(n)` + `AssertNoUnexpectedToolkitErrors()` in `[TearDown]`, defaulting to zero. |
| **6** (B-2, B-4) | Stray-bounds test asserts the exact box; rebake test rebuilds from fresh instances with pinned sibling indices. |
| **7** (B-3) | `AuthoringPathText` split out (no `IBaker`, so the EditMode asmdef needs no `Unity.Entities.Hybrid` — §1.3 forbids it), 12 tests covering CJK, surrogates, capacity boundaries. |

**Advisories cleared:** C-5 to C-9, a1–a3, a5, a7, a8, A-1 to A-3, A-6, A-8 to A-11, A-15, Findings 6, 9, 10. **a6** (built-in-pipeline probe shader) fixed and verified, with a new conformance test that fails on `CGPROGRAM`/`CGINCLUDE`/`UnityCG.cginc` anywhere in the package.

**Still open: A-4.** The `>> 8` sample-phase change has no test and **cannot have one** — see below.

---

## Mistakes I made this session, so you can look for their siblings

The aborted gate found two defects. Both were mine, both were introduced *by the rework that was supposed to close the very items they broke*, and both are the same failure mode this project keeps hitting: **a number or a claim that reconciles against my own notes instead of against the tree.**

1. **The 0.3.0 CHANGELOG count** (B6, third gate running). I measured the suite at C2's *first* commit and ignored the three C2 rework commits shipping inside the same unreleased version. C2 actually ends at `ec44226` with 192, not 176. Fixed.
2. **`TwoActorsWhoseNamesDifferOnlyInTheLastCharacter_GetWellSeparatedPhases`**, written to close A-4, discriminated nothing. Evaluating both derivations over 200 container positions showed the pre-A18 masked derivation passes at every one — reverting A18 entirely left the test green. **Deleted, not replaced.**

**Do not rewrite that test.** A discriminating fixture is not constructible: `AuthoringPathHash.Of` walks *leaf first*, so the differing character is hashed first and mixed by ~22 further FNV rounds. The low-bit weakness the shift avoids can only surface when the differing input is mixed **last**, i.e. on the outermost ancestor. `ComputeSamplePhase` carries this as a do-not-write-this warning. A-4 is reopened as untestable by construction; the open question for the gate is whether the shift should stay at all given nothing can observe it.

---

## Hard rules (from `CLAUDE.md` and owner memory — these override defaults)

- Never `var`; never single-letter names; explicit types everywhere; names read like documentation.
- Never `.Run()` a job — `.Schedule()` / `.ScheduleParallel()` assigned to `state.Dependency`.
- `[ReadOnly]` from `Unity.Collections`, never `Unity.Entities`.
- Prefer `ISystem` + `[BurstCompile]`; no managed allocations in Burst jobs.
- Burst log strings: only `G/g/D/d/X/x` specifiers (BC1343); no `+` concatenation (BC1016). `FixedStringNBytes` interpolation **is** supported (Burst 1.8.21+; pinned 1.8.29) despite the stale doc page.

## Environment gotchas

- **No Unity MCP, no Editor bridge.** Never attempt `mcp__unity-mcp__*`. The compile gate is: the owner focuses Unity, then grep `Logs/Editor.log` for `error CS` / `BC`. Anything visual, ask.
- **The Editor log is project-relative:** `Logs/Editor.log`. The `%LOCALAPPDATA%` one is a stub whose last line says so — grepping it always returns clean, which reads as success and is not. Check its mtime against your edits before believing it.
- **Grep `Library/PackageCache/<pkg>@<hash>/` before calling any Unity API.** The two worst bugs in this package — a non-existent `Baker.GetComponentsInChildren<T>(bool)` overload and the `FixedString` byte-vs-character overflow — both came from recalling semantics instead of reading them. Both were one grep away.
- **A Bursted baking system's diagnostics are invisible to `Application.logMessageReceived`** (main-thread only). Use `logMessageReceivedThreaded`.
- **`LogAssert.ignoreFailingMessages` set in `[SetUp]` does nothing** — UTF disposes that `LogScope` before the test body runs.

## Process the product owner mandated

- Build modules **C0–C8** in dependency order. Each is gated by an adversarial reviewer producing PASS/FAIL. **Reviewers are launched only when the owner asks.**
- **Commit to `main` after each module passes its gate.** Nothing has been pushed.
- **Pause after each module** so the owner can run the Editor compile and Test Runner.
- **Phases done:** A, B, C0, C1, C2. **C3 is built, verified green, and awaiting its gate.**
- After C3 closes: **C4, the systems slice** (transform + flipbook end-to-end, events, bounds, LOD).

## Unrelated host-game bug, still open

`Assets/_Scripts/Editor/StitchPunk.Editor.asmdef` has `"includePlatforms": []`, so editor code compiles into player builds and any player test run fails with ~58 compile errors. One-line fix: `["Editor"]`. Offered twice; the owner has not taken it. Not a package issue.

Also note: the working tree carries substantial **unrelated host-game shader work** (Painterly graphs, colour ramps, `Assets/Shaders/`). Do not commit it with package changes — stage `Packages/com.stitchpunk.dotsanimationtoolkit`, `Docs/AnimationToolkit`, and this file explicitly.
