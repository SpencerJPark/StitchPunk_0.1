# DOTS Animation Toolkit — session handoff

**Written:** 2026-07-31 (second session of the day; supersedes the earlier handoff)
**State:** Build step **C3 is OPEN**. Every blocking item from the re-review is addressed; the package **compiles clean** and the **PlayMode suite runs green**. The EditMode suite compiles but has not been run — see "What you must do first".
**Read before doing anything:** `Docs/AnimationToolkit/Phase_C3_ReReview.md` (the three reviewer reports, verbatim) and `Docs/AnimationToolkit/Phase_B_Architecture.md` (the normative spec — 111 KB, **never read whole**; grep headings, then Read with offset/limit).

---

## What this project is

A commercially sellable Unity DOTS animation UPM package, `com.stitchpunk.dotsanimationtoolkit`, developed as an embedded package at `Packages/com.stitchpunk.dotsanimationtoolkit/`. AAA bar: no placeholders, stubs, or TODOs.

**Process the product owner mandated** (do not quietly drop it):
- Build modules **C0–C8** in dependency order.
- Each module is gated by an **adversarial Reviewer** producing a PASS/FAIL verdict. Reviewers are launched only when the owner asks.
- **Commit to `main` after each module passes its gate.**
- **Pause after each module** so the owner can run the Editor compile and Test Runner.

**Phases done:** A (audit), B (architecture), C0 (skeleton), C1 (M3 data slice), C2 (M1 authoring slice). **C3 (M2 entity baking) is in its second rework and not closed.**

---

## Verification status (2026-07-31, `Logs/Editor.log` written 19:40)

**Compile: clean.** Zero `error CS` / `error BC`. Both test assemblies were IL-post-processed, which only happens on a successful compile — so `AuthoringPathTests.cs`, the `IBaker` parameter on `AuthoringPathHash`, and the `#if UNITY_EDITOR` guard on `BuildInvocationCount` all build.

**PlayMode suite: green, and verified to have run end to end.** Zero NUnit assertion output (`But was:`, `AssertionException`) and zero unexpected exceptions anywhere in the log. The bake diagnostics tally *exactly* against the fixtures, which is what proves the whole suite ran rather than part of it:

| Message | Count | Expected from |
|---|---|---|
| `Rig target 'Stray' … id 999 … does not declare` | 2 | the unknown-target test + the stray-bounds test |
| `Rig part 'Actor/VatBody' claims target id 100 …` | 1 | the duplicate-claim fixture |
| `Clip set 'Set' cannot be baked …` | 1 | the validation-errors test |
| `marks layer 0 as active by default … seeds no starting clip` | 12 | the 10 `CreateActorRoot("Actor")` fixtures + `ClampedActor` + `RatedActor` |
| three VAT material warnings | 3 | the three material fixtures |

Four errors total, matching the four `ExpectToolkitErrors(1)` declarations exactly. Three things are confirmed *at runtime* by that table, not just by reading:

1. **A22's tag works.** `"has no usable clip registry, and no earlier message explained why"` never printed. Without `ActorBakeFailed` being written, the validation-errors fixture's three parts would each have produced it.
2. **Burst `FixedString` interpolation works**, and the diagnostic names a real path — `Rig part 'Actor/VatBody'`, emitted from the Bursted job via the Burst runtime log callback.
3. **The new dependency-taking path walk produces correct text**, since that path came out of `IBaker.GetName`/`GetParents` rather than off `Transform`.

**Still to run: the EditMode tab.** It compiles, but nothing in the log shows it executing, so the 204 tests — including the 12 new `AuthoringPathTests` — are unverified. That is the one gap left before the gate.

---

## What this session changed

All six blocking items from the C3 re-review, plus the advisory backlog. Verify each against the shipped diff rather than against this list — that instruction has been earned three gates running.

### The one decision made without the owner

**Item 2 — who owns the unknown-target-id error.** The previous handoff put this to the owner with two options and recommended (a): bless the managed-baker location. The owner was away, having asked for work to continue. **(a) is recorded as amendment A22** in `Phase_B_Architecture.md` §4.1, with an explicit paragraph saying it was recorded without the owner and naming exactly what to revert if they prefer (b).

A22 also fixes the real defect Reviewer C found underneath the paperwork (C-4). `RigBindingBakingSystem` used to stay silent whenever a part's actor carried no `ClipRegistry` — correct only because each of `ActorBaker`'s bail-outs happened to log first, a coupling nothing enforced. `ActorBaker` now writes a new `[BakingType] ActorBakeFailed` tag when it stops, and the binding pass suppresses **only** on that tag. An unexplained missing registry is now reported. The two guards that were unreachable by construction are deleted; the target-id guard survives, reworded as what it actually now is — a check that the rig asset and the registry built from it agree.

### Blocking items

| Item | What was done |
|---|---|
| **1** (C-3) | The three doc comments that asserted the opposite of their code — `RigTargetBaker.ResolveTargetKind`, `ActorBaker.ComputeActorRestBounds`, `ResolveRigPartBindingsJob` — all rewritten to describe the A22 split. |
| **2** (Finding 5 / C-4) | A22 as above. |
| **3** (B4) | §8 M2 OWNS gains `StartingLayerState`, `RigPartBakeLink`, `ActorBakeFailed`, `AuthoringPathHash`; EXPOSES lists all seven `RigTargetAuthoring` fields; §4.6's `offsetBounds` contradiction resolved by **A24**; **A18** now states the `(pathHash >> 8) × 2⁻²⁴` derivation. Also §1.3's PlayMode platform cell corrected in place (Finding 10) and **A23** added for `AnimLod` (advisory a1). |
| **4** (B6) | Counts recovered from git history rather than guessed: **8 → 106 → 176 → 204** cumulative EditMode. `index.md` said 164, now 204 + 28 PlayMode. The CHANGELOG's "96" and "66" were wrong under *both* readings; now "98 new — 106 in the suite" and "70 new — 176 in the suite". |
| **5** (B-1) | `BakingTestWorld.ExpectToolkitErrors(n)` + `AssertNoUnexpectedToolkitErrors()`, called from `[TearDown]` and defaulting to zero. The four tests that legitimately provoke an error declare it. Skipped when the body already failed, so the real failure is what the runner reports. |
| **6** (B-2, B-4) | The stray-bounds test asserts the exact box (a zero box used to pass). The rebake test destroys the hierarchy and rebuilds it from **fresh instances** with identical names and pinned sibling indices under a stable container — an instance-id derivation now fails it. |
| **7** (B-3) | `AuthoringPathText` split out of `AuthoringPathHash` (no `IBaker`, so the EditMode asmdef needs no `Unity.Entities.Hybrid` — §1.3 forbids it), with 12 tests: CJK, surrogate pairs, exact-capacity and one-over boundaries, leaf preservation, empty names, null. |

### Advisories cleared

C-5/A-13 (dead null branch deleted), C-6 (dead mask dropped, rationale corrected — the old one described a scenario this hash does not have), C-7 (`#if UNITY_EDITOR` + `Interlocked`), C-8 (class doc), **C-9** (both path walks now take name and parent-chain bake dependencies through `IBaker.GetName`/`GetParents`), a2, a3, a5 (the doc oversold what the store hit saves), a7, a8, A-1, A-2, A-3, A-4, A-6, A-8, A-9, A-10, A-11, A-15, Finding 6 (the order-dependent pin now accepts either claimant), Finding 9 (`AssertToolkitWarningsMatching` counts by emitter-specific text).

**Sibling reordering is the one acknowledged gap in C-9:** Entities exposes no sibling-index bake dependency. Recorded in A18 and in the class remarks. It affects only `phase01`.

### Deliberately left open

**a6 / A-14 — `Tests/PlayMode/VatMaterialProbe.shader` is built-in-pipeline `CGPROGRAM`/`UnityCG.cginc` inside a URP-only package**, and ships in the tarball. M4's C5 acceptance ("all shaders compile for the URP target with zero warnings-as-errors") will sweep it up. The fix is to rewrite it against URP's ShaderLibrary.

I did not do it. Shaders compile only in the Editor, and I had no compile signal; this file is the *sole* evidence that B1 is closed (it is what makes the §4.4 mismatch branch reachable at all), so trading a verified-working artifact for an unverified one to close an advisory about a future gate is a bad trade to make unsupervised. **Do it with the Editor open, and confirm `AVatPartBoundToTheWrongTexture_LogsTheSection44Mismatch` and `AVatPartBoundToTheBakedTexture_WarnsAboutNothing` still pass.** `ActorBakeFixture.CreateVatCapableMaterial` asserts `Shader.Find` is non-null, so a botched rewrite fails loudly rather than silently.

---

## Hard rules (from `CLAUDE.md` and owner memory — these override defaults)

- Never `var`; never single-letter names; explicit types everywhere; names read like documentation.
- Never `.Run()` a job — `.Schedule()` / `.ScheduleParallel()` assigned to `state.Dependency`.
- `[ReadOnly]` from `Unity.Collections`, never `Unity.Entities`.
- Prefer `ISystem` + `[BurstCompile]`; no managed allocations in Burst jobs.
- Burst log strings: only `G/g/D/d/X/x` specifiers (BC1343); no `+` concatenation (BC1016). `FixedStringNBytes` interpolation **is** supported (Burst 1.8.21+; pinned 1.8.29) despite the stale doc page.

## Environment gotchas that have cost real time

- **There is no Unity MCP and no Editor bridge.** Never attempt `mcp__unity-mcp__*`. The compile gate is: the owner focuses Unity and reports, or grep `Logs/Editor.log` for `error CS` / `BC`. Anything visual, ask.
- **The Editor log is project-relative.** `%LOCALAPPDATA%/Unity/Editor/Editor.log` is a stub that says so in its last line; the real log is `Logs/Editor.log`.
- **Unity package sources are on disk at `Library/PackageCache/<pkg>@<hash>/`. Grep them to confirm an API before calling it.** Two of this project's worst bugs — a non-existent `Baker.GetComponentsInChildren<T>(bool)` overload and the whole `FixedString` byte-vs-character overflow — came from recalling semantics instead of reading them. Both were one grep away.
- **A Bursted baking system's diagnostics are invisible to `Application.logMessageReceived`** — it is main-thread only. Use `logMessageReceivedThreaded`, as UTF's own `LogScope` does. Any future log-capturing harness (C4's included) must do this.
- **`LogAssert.ignoreFailingMessages` set in `[SetUp]` does nothing.** UTF wraps each setup method in its own `LogScope` and disposes it before the test body runs.
- **Reviewers must be split and must write findings incrementally.** Two monolithic reviewers were killed by a 600 s no-progress watchdog and lost everything. Three narrow parallel agents, each appending to its own scratchpad file as it goes, works.
- **Scratchpad files do not survive a session.** Copy anything durable into the repo, as `Phase_C3_ReReview.md` is.

## Test suites

Run from **Window ▸ General ▸ Test Runner**; there is no headless runner.
- EditMode: **204** tests.
- PlayMode: **28** tests (27 baking acceptance + 1 assembly smoke), **Editor-only by design** (amendment A17) — Unity's baking pipeline has no player-side equivalent, so "Run all tests (Player)" cannot execute it.

## Unrelated host-game bug, still open

`Assets/_Scripts/Editor/StitchPunk.Editor.asmdef` has `"includePlatforms": []`, so editor code compiles into player builds and any player test run fails with ~58 compile errors. One-line fix: `["Editor"]`. Offered; the owner has not taken it. Not a package issue.

---

## Suggested next moves

1. **Run the EditMode tab.** Compile and PlayMode are already verified green (see above); EditMode is the remaining gap.
2. Show the owner **A22** and get an explicit yes or no. It is the only thing in this rework that was their call and not mine.
3. Fix **a6** (the probe shader) with the Editor open.
4. Re-run the three-way review on the full C3 diff (`git diff 026a902..HEAD`), then hand back for the gate.
5. Only then C4 (systems slice), C5–C8.
