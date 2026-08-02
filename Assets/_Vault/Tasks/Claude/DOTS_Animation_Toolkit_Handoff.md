# DOTS Animation Toolkit — session handoff

**Written:** 2026-08-02 (C4.3, C4.4 and C4.5 closed and gated)
**State:** **C3 is CLOSED.** Phases done: A, B, C0, C1, C2, C3. **C4 in progress — C4.1 through C4.5 all done and verified. C4.6 (flipbook) is next.**

**Baseline, verified through the MCP 2026-08-02:** Console clean of `error CS` / `BC`; **208 EditMode + 125 PlayMode, all passing, each in its real mode.** Project-wide EditMode is 266 — the extra 61 belong to the host game's own `StitchPunk.Tests`, so filter by assembly when comparing to this number. Discrimination for C4.3–C4.5 verified by five mutation runs (tables in the C4 plan). Nothing is owed.

## ⚠ Two process notes, both learned the hard way this session

**1. `refresh_unity` returning `success` is not a readiness signal.** Calling `run_tests` straight after it wedged the Editor mid-play-mode-transition (job died with `total: null`, `completed: 0`), needed `manage_editor action=stop` to recover, and left an `Assets/InitTestScene<guid>.unity` behind. Poll `mcpforunity://editor/state` first: `is_compiling: false`, `is_domain_reload_pending: false`, and a `last_domain_reload_after_unix_ms` **newer than the compile you just triggered**. The `stale_status` blocking reason on that resource is snapshot freshness only — it can sit true indefinitely while every substantive field reads idle, so do not wait on `ready_for_tools`.

**2. The MCP bridge can be down while Unity is perfectly healthy.** For two sessions the server answered but no Editor was registered (`instance_count: 0`, `read_console` → `no_unity_session`, `refresh_unity` → 60 s timeout) with the Editor open and importing normally. Only the owner can restore it (Window ▸ MCP For Unity). Check `mcpforunity://instances` before planning a phase — building blind is possible (C4.3 and C4.4 both were) but every unverified assumption compounds.

## C4 progress — read `Docs/AnimationToolkit/Phase_C4_Plan.md` first

It holds the 14 pieces, the nine phases, the traps carried in from C3, and the test-integrity standard.

- ✅ **C4.1 skeleton** — four system groups, `ToolkitWorldControl`, `ConfigBootstrapSystem`, 12 tests.
- ✅ **C4.2 binding** — `RigBindingSystem` (§5.3), 7 tests, **mutation-verified**. Rebuilds `RigPartRef` and `RigPartBinding.actorRoot` from `LinkedEntityGroup` after instantiation, re-derives `phase01` per instance, disables `RigBindingUninitialized`.
- ✅ **C4.3 playback core** — compiled clean, all tests green, **mutation-verified** (three runs; see the C4 plan's table). All four pieces plus their fixtures:
  - `Runtime/Api/AnimationCommandUtil.cs` (Play, Queue, Stop, SetSpeed, SetTime) — unchanged from `7d84051`.
  - `Runtime/Api/PlaybackQuery.cs` — `IsPlaying`, `NormalizedTime` (A26's three-parameter form), `FinishedThisFrame`. The spec no longer documents an API that does not exist.
  - `Runtime/Systems/CommandApplySystem.cs` — two jobs: the stale-event clear (A28) then the command apply. `OrderFirst` in the logic group.
  - `Runtime/Systems/PlaybackTimeSystem.cs` — blend advance, time advance, loop handling, Once completion, queue promotion. `UpdateAfter(CommandApplySystem)`.
  - `Runtime/Components/PlaybackLayer.cs` — **new field `advanceStartTime`** (A27), plus the matching row in `DataContractTests`.
  - Tests: `Tests/PlayMode/PlaybackTestActor.cs` (blob + actor fixture builder, PlayMode-local because the PlayMode asmdef cannot see `TestBlobFactory`), `CommandApplySystemTests.cs`, `PlaybackTimeSystemTests.cs`, `PlaybackQueryTests.cs`, and two structural tests appended to `SystemGroupStructureTests.cs`. Every fixture's doc comment names the mutation it catches, per the C4 standard.
- ✅ **C4.4 events** — `Runtime/Systems/EventEmissionSystem.cs` (marker crossings from `[advanceStartTime, time]` + `ClipFinished`, appends and enables only per A28) and `Tests/PlayMode/EventEmissionSystemTests.cs` (12 fixtures). Writing it exposed a defect in C4.3's queue promotion → **amendment A30**, which changed `PlaybackTimeSystem` and its promotion fixtures. Gated with C4.3.
- ✅ **C4.5 transform technique** — `TransformSampleSystem` + `TransformApplySystem`, 11 fixtures, **mutation-verified** (four predicted failures, no others). The host's dead-scale regression is now pinned by a test: delete the `PostTransformMatrix` write and the authored 2× scale silently reverts to the rest 1.5×, which is precisely how it failed in the host game.
- ⬜ C4.6 flipbook · C4.7 VAT+bounds · C4.8 LOD · C4.9 acceptance + smoke scene.

**C4.8 owes an ordering edge:** `TransformSampleSystem` has no `[UpdateAfter(typeof(AnimLodDistanceSystem))]` because that type does not exist yet. §5.1's diagram orders sampling after LOD — add it when C4.8 lands.

## Amendment A31 — transform keys are offsets from rest (owner-approved 2026-08-02)

C4.5 found the spec and the shipped sampler disagreeing about what a transform key *is*. The spec said offsets from rest (§3.2, §4.6, and the entire rationale of A13); `ClipSampler` implemented absolute local values, with a `ClipSamplerTests` assertion locking that in deliberately. **Resolved in favour of the spec:** `ApplyClipToPose` now takes the rest pose, `Override` writes `rest + key` (scale `rest × key`), `Additive` is unchanged and still anchors to the composited pose below. Under the old reading, re-posing a rig's rest was silently ignored the moment a clip played — fatal for a cutout rig.

**Why it survived three gates, and the rule that follows.** Every `LayerCompositionTests` fixture used a rest pose at the origin with unit scale, where `rest + key` and `key` are the same number. The entire Override semantics were untested while looking thoroughly tested. Reverting the sampler now fails **ten** tests; before C4.5 it failed none.

**The rule:** when a fixture picks a "simple" value for something the code branches on — zero, one, identity, empty — check whether that choice is what makes the assertion pass. That is the third distinct shape of invisible defect C4 has produced, after A28 and A30's "only the next system can see it".

**C4.5 is the first phase that produces visible motion.** The owner has asked to go through **C4.9 together** — that phase's DoD needs them to confirm on-screen clip playback, which Claude cannot verify. Build 4.6–4.8 autonomously; stop at 4.9.

## What C4.3 decided that C4.4 inherits

Four amendments went into §5 of the architecture, all under the owner's standing delegation, all with a "to revert" note. **Two of them change what C4.4 must build:**

- **A28 — `EventEmissionSystem` appends and enables only.** It must **not** clear `AnimEventOutput` and must **not** disable `AnimEventsPending`; `CommandApplySystem` now owns the clear, at the top of the group. As originally specified, §5.4 had `CommandApplySystem` emit `ClipResolveFailed` and §5.5 had a *later* system wipe the buffer — every resolve-failure event was destroyed in the frame it was raised.
- **A27 — the crossing window is `[layer.advanceStartTime, layer.time]`,** read off the layer, on the **current clip only**. Do not recompute the opening edge as `time − dt × speed`: that is wrong on exactly the frames where a Once clip clamps or a queue promotes. The crossfade source deliberately emits no markers (§12 R11).
- A26 (pre-existing) — `PlaybackQuery.NormalizedTime` takes the registry.
- A29 — out-of-range layer index dropped without an event; Queue resolves its clip; Stop clears the queue.
- **A30 (C4.4) — queue promotion is deferred by one advance.** Found only by building the consumer: promoting in the same advance that finished the clip made `ClipFinished` name the follow-up and silently dropped the finishing clip's last-segment markers, which is where hit frames live. `PlaybackTimeSystem` now raises the completion, holds the final pose, and promotes at the top of the next advance.

**The pattern to carry into C4.5–C4.8:** both A28 and A30 are defects that were invisible from inside the system containing them and only surfaced when the *next* system had to consume the output. Neither would have been caught by re-reading the producer, however carefully. When starting a phase, write down what the phase after it will need from you — that is where these live.

## The two traps — how C4.3 handled them

Both are closed, and both are **mutation-verified** rather than merely green — the mutation table is in the C4 plan.

- **`PlaybackLayer.previousLoop` is now written, on both paths.** `CommandApplySystem.ApplyPlay` and `PlaybackTimeSystem.PromoteQueuedClip` each copy the outgoing `loop` into it *before* `layer.loop` is overwritten. Capturing it *after* the overwrite fails `PlayOverACrossfade_CapturesTheModeTheOutgoingClipWasActuallyPlayingUnder` with `Expected: Once, But was: Loop` — the wrong value that a late capture produces, not the `UseClipDefault` a deleted line would leave. The fixture separates the two failure modes, and that was proven, not asserted.
- **`BoundsDirty`** is enabled by `CommandApplySystem` on a Play/Stop that changes `clipIndex`, and by `PlaybackTimeSystem` on queue promotion, Once-completion and blend completion. No change-version filter anywhere. Making either write unconditional fails exactly one fixture per system — `PlayingTheSameClipAgain_DoesNotDirtyTheBounds` and `AnOrdinaryAdvance_DoesNotDirtyTheBounds`.
- **Still untested by construction:** the `BoundsDirty` *disable*. It lives in `RenderBoundsUpdateSystem`, which C4.7 builds. The C4 test-integrity standard names it as the second thing to pin by mutation — do that in C4.7, not before.

## The API trap C4.3 found (read before writing any more `IJobEntity`)

An `EnabledRefRW<T>` parameter **enrols `T` in the query as an `All` component** — enabled-only. Both C4.3 systems write `BoundsDirty`, which is disabled on almost every actor almost every frame; left as the default, both jobs would have matched almost nothing, silently, with no error. Fixed with `[WithPresent(typeof(BoundsDirty), ...)]`. Rule: *if the job ever turns a bit **on**, that component needs `[WithPresent]`.* Recorded in `_Vault/Memories/Code/Gotchas.md`. C4.4–C4.8 will hit this again — `AnimEventsPending`, `AnimVisible`.

---

## Read before doing anything

- `Docs/AnimationToolkit/Phase_B_Architecture.md` — the normative spec. **111 KB — never read whole.** Grep headings, then Read with offset/limit. §5 is C4's territory.
- `Docs/AnimationToolkit/Phase_C3_Gate4.md` — the last gate: verdict, the 10 blocking items, the Resolution table, the "verified clean — do not re-litigate" list, and the A-4 ruling. Read the verified-clean list before re-auditing anything in C3.
- `Phase_C3_Gate4_Reviewer{A_Spec,B_Tests,C_Code}.md` — the three lenses verbatim, with `Library/PackageCache` citations for the Entities behaviour C3 depends on.
- Earlier gates (`Phase_C3_Review.md`, `Phase_C3_ReReview.md`, `Phase_C3_Gate3_Incomplete.md`) are history, superseded by Gate 4.

## The environment is not what older notes say

- **Unity MCP is currently DOWN, and has been for two sessions** — the server answers, the Editor is open and importing, but `mcpforunity://instances` reports `instance_count: 0` and every tool returns `no_unity_session`. `refresh_unity` times out after 60 s waiting for editor readiness. The Editor-side bridge is what is missing; only the owner can restore it. **Check this first, before planning any phase**, because the whole gate depends on it.
- When it is up: compile gate is `refresh_unity` → poll `editor/state` for `is_compiling: false` and `external_changes_dirty: false` → `read_console`. Tests: `run_tests` + `get_test_job`. Anything genuinely visual still needs the owner to look.
- **The live Editor log for this launch is `%LOCALAPPDATA%\Unity\Editor\Editor.log`, not `Logs/Editor.log`.** Older notes say the opposite. Which one is live depends on how the Editor was launched (Hub-launched sessions write to the AppData one); the project-relative copy was last written 2026-08-01. Check `LastWriteTime` on both before trusting either.
- **Always check the discovered test count, not just pass/fail.** `resultState: "Passed"` with `total: 0` is what a vanished suite looks like, and it is how the C3 PlayMode defect survived a whole build step and three static reviewers.
- Fallback when the Editor is closed or the bridge is down: grep whichever `Editor.log` is actually being written (see above), and **check its mtime against your edits** — a log that predates the change means the compile never happened, so say so rather than reporting "clean".
- **Grep `Library/PackageCache/<pkg>@<hash>/` before calling any Unity API.** The two worst bugs this package shipped both came from recalling semantics instead of reading them — and C4.3's `EnabledRefRW` trap was found this way and no other.

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
- **Commit and push to `main` whenever it makes sense — do not wait to be asked** (owner, 2026-08-02, superseding the old "commit only after a module passes its gate"). A phase that compiles clean with its tests green is a checkpoint; so is a coherent slice of one. What has not changed: stage the package, `Docs/AnimationToolkit`, and this handoff **explicitly** — the working tree carries unrelated host-game shader work that must never ride along.
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
