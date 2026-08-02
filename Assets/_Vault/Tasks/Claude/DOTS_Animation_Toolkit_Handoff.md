# DOTS Animation Toolkit — session handoff

**Written:** 2026-08-01 (updated after Gate 4)
**Last commit:** `bce381e`
**State:** **Gate 4 ran to completion — all three lenses reported, and the suite was then run through the Unity MCP.** Verdict **FAIL: 10 blocking items.** Nine are documentation/test-input fixes. The tenth was found by *running* the tests and is the serious one: **the PlayMode suite does not exist** — all 27 "PlayMode" tests execute in EditMode, and a project-wide PlayMode run discovers zero tests. All three lenses read that asmdef and none caught it; static review could not. The four "do this first" items that had zero coverage across the entire third attempt are now all answered with `Library/PackageCache` citations.

**Verified by execution (2026-08-01, via `mcp__UnityMCP__run_tests`):** 205 EditMode pass, 27 "PlayMode" pass *while running in EditMode*, 0 discovered in PlayMode. Total 232, all green — but the mode split every doc claims is wrong.

**Read before doing anything:**
- `Docs/AnimationToolkit/Phase_C3_Gate4.md` — **start here.** The consolidated Gate 4 verdict: the 10 blocking items, the four priority answers, the verified-clean list (do not re-litigate), and the A-4 ruling.
- `Phase_C3_Gate4_ReviewerA_Spec.md` / `..._ReviewerB_Tests.md` / `..._ReviewerC_Code.md` — the three lenses verbatim.
- `Docs/AnimationToolkit/Phase_C3_ReReview.md` — the second gate's FAIL. Superseded by Gate 4 but retained for history.
- `Docs/AnimationToolkit/Phase_C3_Gate3_Incomplete.md` — the aborted third attempt.
- `Docs/AnimationToolkit/Phase_B_Architecture.md` — the normative spec. **111 KB — never read whole.** Grep headings, then Read with offset/limit.

---



## Do this first

> **UPDATE 2026-08-01: all 10 items are fixed and verified.** Suite green in its real modes — **205 EditMode + 29 PlayMode = 234**. See the Resolution table in `Docs/AnimationToolkit/Phase_C3_Gate4.md`. **One thing needs the owner: ratify or reject amendment A25** (below). C3 is otherwise ready to close.

**~~Fix Gate 4's 10 blocking items, then C3 closes.~~** *(Done — kept for context.)* The full list with file:line citations is in `Docs/AnimationToolkit/Phase_C3_Gate4.md`. In short:

1. **Seven documentation fixes (Reviewer A).** The recurring one: "four diagnostics" in `Phase_B_Architecture.md:358` and `CHANGELOG.md:37` when there are three — contradicted by A22 twenty lines away in both files. **Third occurrence of the CHANGELOG-count defect across three gates.** Plus a stale ownership comment on the public `RigTargetAuthoring`, the A18↔A-4 contradiction, a false "nothing shipped uses reflection" claim, and F18's bad premise for making `RigPartBakeLink`/`ActorBakeFailed` public (same assembly, internal callers — they can be internal).
2. **One test fix (Reviewer B).** `RenderPath_OnAPathOfSurrogatePairs_NeverEmitsALoneSurrogate` (`AuthoringPathTests.cs:151-184`) is non-discriminating — **a fresh instance of the exact A-4 failure mode, in this same module.** Delete the step-back at `AuthoringPathText.cs:106-109` and it still passes. Fix: an input whose retained region starts at an *odd* offset inside a pair.
3. **`RigBindingSystem` doesn't exist yet** (`Runtime/Systems/` is empty) but seven doc comments reference it in the present tense, and it is the sole stated justification for two conclusions. Mark them forward-looking — it is C4's system.
4. **Restore the PlayMode suite — do this before C4.** `aacde42` set `"includePlatforms": ["Editor"]` on `StitchPunk.AnimationToolkit.Tests.PlayMode.asmdef:16`; an editor-only assembly is classified as EditMode, so the whole 27-test suite silently moved modes. Revert to `[]` and confirm PlayMode discovers 27. Then fix `PlayModeAssemblySmokeTest.cs:16-20`, which was supposed to guard exactly this and instead asserts only the assembly's *name* — equally true in EditMode, a third instance of the non-discriminating-test failure mode. Replace it with something that observes the mode (`Application.isPlaying` inside a `[UnityTest]`). **This gates C4:** the systems slice needs a real player-loop tick, and written against this asmdef its tests would run in EditMode too.

**Do not re-run the gate lenses on what they cleared.** `Phase_C3_Gate4.md` has a "verified clean — do not re-litigate" section: the test *counts* (232 total, numerically right for the first time — though the mode split is not, see item 4), amendments A18/A22/A23/A24, §1.3's Hybrid prohibition, the error harness including the threaded-log race, the rebake test, and hard-rule conformance.

**The four priority items are answered** — see Gate 4 §"do this first". Headline: `ActorBakeFailed` **cannot** go stale (proven against `BakerState.Revert` / `BakedEntityData.cs:538-558`, including the asset-only-dependency path), and `AuthoringPathHash.Of` is byte-for-byte input-identical (the derivation change at the call site is A18, deliberate, and the package is unreleased so nothing shifts under a consumer).

**A-4 is ruled and closed:** keep the shift; do not write the test; cut the 14-line comment at `ActorBaker.cs:524-541` to two and move the reasoning into the spec.

### ⚠ The one open decision: amendment A25

**A17 was self-defeating and needs the owner to confirm its replacement.** A17 (owner-approved, 2026-07-30) set `includePlatforms: ["Editor"]` on the PlayMode test assembly so it would not falsely claim player-side capability, and explicitly rejected "move the suite to EditMode" as too much normative surgery. An editor-only assembly *is* an EditMode assembly to the Test Framework — so A17 performed the move it rejected, silently, and the PlayMode suite ceased to exist for a whole build step.

**A25** (recorded without the owner, A22 precedent) sets it back to `[]`. The trade: `[]` overstates player capability only under a player test build that no §8 bullet requires; `["Editor"]` destroys the suite's mode under the run actually performed. A25 in `Phase_B_Architecture.md` §1.3 names exactly what to revert if you disagree — but note that reverting means owning that the acceptance tests are EditMode tests, which requires rewriting §8 M2 and §11.2 and adding `Unity.Entities.Hybrid` + `Unity.Transforms` to §1.3's EditMode list. That is the surgery A17 declined.

### If a future gate is needed

The three-narrow-agents-in-parallel shape **worked** — all three lenses completed for the first time in four attempts, where two monolithic reviewers were killed by a watchdog and a third died on a usage limit. Keep it: one agent per lens, each appending to its own scratchpad file *as it goes*, and copy results into `Docs/AnimationToolkit/` before the session ends — scratchpads do not survive. Snapshot partials mid-flight; it costs nothing.

---

## Verification status

**Compile: clean.** **All 232 tests pass** — re-run 2026-08-01 via `mcp__UnityMCP__run_tests`, superseding the owner's 2026-07-31 manual run.

⚠ **The 205/27 split below is how the suite is *labelled*, not how it runs.** Actual discovery at `bce381e`: **232 EditMode, 0 PlayMode.** The 27 acceptance tests (26 baking + 1 smoke) live in the PlayMode assembly and pass, but execute in EditMode — blocking item 4 above. The owner's 2026-07-31 "both tabs green" reading is consistent with this: a PlayMode tab that discovers zero tests reports Passed.

The Console legitimately carries 4 toolkit errors and ~15 warnings after a run — several acceptance tests deliberately provoke a diagnostic and assert on it. Each such test declares its expected error count; every other test is held to zero. Do not "fix" those messages.

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

- **Unity MCP is connected** (verified 2026-08-01): `mcp__UnityMCP__*` over HTTP at `127.0.0.1:8080`. The compile gate is `mcp__UnityMCP__read_console`; `refresh_unity` triggers the recompile, `run_tests`/`get_test_job` drive the Test Runner. The old `mcp__unity-mcp__*` stdio relay is dead and removed — do not fall back to it. Anything genuinely visual, still ask.
- **Fallback when the Editor is closed or the MCP is unreachable:** grep `Logs/Editor.log` — project-relative. The `%LOCALAPPDATA%` one is a stub whose last line says so — grepping it always returns clean, which reads as success and is not. Check its mtime against your edits before believing it.
- **Grep `Library/PackageCache/<pkg>@<hash>/` before calling any Unity API.** The two worst bugs in this package — a non-existent `Baker.GetComponentsInChildren<T>(bool)` overload and the `FixedString` byte-vs-character overflow — both came from recalling semantics instead of reading them. Both were one grep away.
- **A Bursted baking system's diagnostics are invisible to `Application.logMessageReceived`** (main-thread only). Use `logMessageReceivedThreaded`.
- **`LogAssert.ignoreFailingMessages` set in `[SetUp]` does nothing** — UTF disposes that `LogScope` before the test body runs.

## Process the product owner mandated

- Build modules **C0–C8** in dependency order. Each is gated by an adversarial reviewer producing PASS/FAIL. **Reviewers are launched only when the owner asks.**
- **Commit to `main` after each module passes its gate.** Nothing has been pushed.
- **Pause after each module** so the owner can run the Editor compile and Test Runner.
- **Phases done:** A, B, C0, C1, C2. **C3 is built, verified green, and gated — Gate 4 returned FAIL on 10 blocking items (see above); C3 closes once they are fixed.**
- After C3 closes: **C4, the systems slice** (transform + flipbook end-to-end, events, bounds, LOD).

## Unrelated host-game bug, still open

`Assets/_Scripts/Editor/StitchPunk.Editor.asmdef` has `"includePlatforms": []`, so editor code compiles into player builds and any player test run fails with ~58 compile errors. One-line fix: `["Editor"]`. Offered twice; the owner has not taken it. Not a package issue.

Also note: the working tree carries substantial **unrelated host-game shader work** (Painterly graphs, colour ramps, `Assets/Shaders/`). Do not commit it with package changes — stage `Packages/com.stitchpunk.dotsanimationtoolkit`, `Docs/AnimationToolkit`, and this file explicitly.
