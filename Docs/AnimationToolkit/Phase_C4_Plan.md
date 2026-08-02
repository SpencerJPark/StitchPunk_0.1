# Phase C4 — the systems slice (M3): build plan

**Opened:** 2026-08-01, immediately after C3 closed at `7183ff8`.
**Spec:** `Phase_B_Architecture.md` §5 (runtime architecture), §8 M3 (ownership), §11.2 (test obligations), §9 build plan row C4.

**DoD (from §9):** M3 PlayMode acceptance green; Burst-clean; a host-shaped smoke scene (subscene with one cutout actor) animates in this repo.
**Evidence:** test run; Console clean of `error CS`/`BC`; **owner-confirmed on-screen clip playback** — the one item in this module Claude cannot verify alone.

---

## What already exists (do not rebuild)

C1/C2 shipped the entire data layer. `Runtime/` holds all components, blobs, identity, and **both pure-function libraries** C4's systems are supposed to call:

- `Runtime/Sampling/ClipSampler.cs` (504 lines) — `SamplePose`, `CompositeLayers`, `RestToPose`, `LerpPose`, easing, PingPong reflection, LoopMode time mapping, `IdentityAtlasRect`.
- `Runtime/Sampling/EventWrapMath.cs` (224 lines) — `CollectCrossings`, wrap-correct including multi-wrap and reverse.
- Every component in §5.2, `ClipRegistryUtil`, `ReservedEventKeys`.

**`Runtime/Systems/` is empty. That is the whole of C4**, plus two API helper classes and one host-control helper.

§5.11 is a structural guarantee, not advice: **every system below calls `ClipSampler`. No system re-implements sampling.** Sampler divergence is what the audit found in the host game (§3.4); the single-sampler rule is how this package makes it unrepresentable.

## To build

| # | Piece | Spec | Notes |
|---|---|---|---|
| 1 | `AnimationToolkitSystemGroup` + Binding / Logic / Presentation child groups | §5.1 | No scene gating, no host tags, **no Before/After edges** on the outer group — hosts order themselves against it. |
| 2 | `ToolkitWorldControl.SetEnabled(World, bool)` | §5.1 | The supported way a host gates the feature wholesale. |
| 3 | `ConfigBootstrapSystem` | §5.2, §5.12 | Creates `AnimationToolkitConfig` with defaults if absent. Structural work in `OnCreate` only. |
| 4 | `RigBindingSystem` | §5.3 | Rebuilds `RigPartRef` + `RigPartBinding.actorRoot` from `LinkedEntityGroup`; disables `RigBindingUninitialized`. Re-derives `phase01` per instance. |
| 5 | `AnimationCommandUtil`, `PlaybackQuery` | §5.4 | Burst-compatible public API. Games never touch buffers by hand. |
| 6 | `CommandApplySystem` | §5.4 | Clip resolve, `previous*` demotion **before** `layer.loop` is overwritten, `BoundsDirty` on clipIndex change. |
| 7 | `PlaybackTimeSystem` | §5.4 | Time/loop/pingpong/blend/finish/queue; `BoundsDirty` on promotion, Once-completion, blend completion. |
| 8 | `EventEmissionSystem` | §5.5 | Clear → emit crossings + `ClipFinished` → enable `AnimEventsPending`. |
| 9 | `TransformSampleSystem` | §5.6 | Bottom-up layer composite, all bound tracks apply (no first-match break), sample-rate quantization via `phase01`. |
| 10 | `TransformApplySystem` | §5.6 | `LocalTransform` + `PostTransformMatrix` — the live-scale path that fixes the host's dead-scale bug. |
| 11 | `SpriteMaterialSystem` | §5.7 | Single write pose → property. No `-1` guard: it is dead code by construction. |
| 12 | `VatMaterialSystem` | §5.8 | `_VatFrameA/B/_VatBlend` from the driving layer. |
| 13 | `RenderBoundsUpdateSystem` | §5.8 | Gated on `BoundsDirty`, **never** a change filter on `PlaybackLayer`. Unions `offsetBounds` translated by `ActorRestBounds`. Sole reset path. |
| 14 | `AnimLodDistanceSystem` | §5.10 | Optional, default-disabled. CPU presentation only — never timers, never events. |

## Phasing

Each phase ends compile-clean with its tests green, verified through the Unity MCP (`refresh_unity` → `editor/state` → `read_console` → `run_tests`, **checking the discovered count, not just pass/fail**).

- **C4.1 — skeleton. ✅ DONE 2026-08-01.** Groups, `ToolkitWorldControl`, `ConfigBootstrapSystem`, 12 tests in `SystemGroupStructureTests`. Verified: **205 EditMode + 41 PlayMode = 246, all passing.** One API trap found and avoided by reading Entities source first: `EntityManager` has **no** `SetName` — it lives on the nested `Debug` struct — so the bootstrap uses `CreateSingleton<T>(T, FixedString64Bytes)`, which builds the archetype in one step and applies the name behind Entities' own `DOTS_DISABLE_DEBUG_NAMES` guard.
- **C4.2 — binding. ✅ DONE 2026-08-01.** `RigBindingSystem` + 7 tests. **Discrimination verified by mutation**: commenting out `partRefs.Clear()` and the `phase01` re-derivation failed exactly the two tests written for them (6 parts instead of 3; both phases stuck at the baked 0.25) and no others. 205 EditMode + 48 PlayMode = 253.
- **C4.3 — playback core. ✅ DONE 2026-08-02.** All four pieces — `AnimationCommandUtil`, `PlaybackQuery`, `CommandApplySystem`, `PlaybackTimeSystem` — plus 53 fixtures across `CommandApplySystemTests`, `PlaybackTimeSystemTests`, `PlaybackQueryTests`, `PlaybackTestActor`, and two additions to `SystemGroupStructureTests`. **Compiled clean and all tests green.**
  - **Verified by the owner in the Editor, not through the MCP.** The Unity MCP was unreachable for this entire phase (`mcpforunity://instances` → `instance_count: 0` with the Editor open, healthy and importing normally), so `read_console` / `run_tests` never ran. Corroborated from the live Editor log: no `error CS` or `BC` lines, a completed Test Runner job, no NUnit failure markers. **What that means for the next session: the discovered test count was never read.** The four static unknowns listed below all cleared, but if anything about this phase looks wrong later, re-run the suite and check `total`, not just `resultState` — this is the one C4 phase without that evidence on record.
  - Amendments recorded in §5: **A26** (`PlaybackQuery.NormalizedTime` takes the registry — pre-existing), **A27** (`PlaybackLayer.advanceStartTime`; the crossfade source emits no markers → §12 R11), **A28** (the `AnimEventOutput` clear moves from `EventEmissionSystem` to `CommandApplySystem`), **A29** (out-of-range layer index dropped silently; Queue resolves; Stop clears the queue).
  - **A28 changes C4.4's contract:** `EventEmissionSystem` **appends and enables only**. It must not clear the buffer and must not disable `AnimEventsPending`.
  - **A27 changes C4.4's inputs:** the crossing window is `[advanceStartTime, time]` on the current clip, read from the layer — not recomputed from `dt × speed`.
  - `PlaybackLayer` gained a field, so `DataContractTests.ActorRootComponents_MatchTheSection52Inventory` was updated in the same commit. If the field is reverted, that row goes with it.
  - **One API trap found and avoided by reading Entities source first:** an `EnabledRefRW<T>` parameter enrols `T` in an `IJobEntity` query as an **All** component, i.e. enabled-filtered. Both systems here take `EnabledRefRW<BoundsDirty>`, which is disabled on almost every actor almost every frame — left as the default, both jobs would have silently matched almost nothing, with no error of any kind. Fixed with `[WithPresent(...)]`; recorded in the host vault's `Gotchas.md`.
  - Four things that had to be written without a compiler and turned out fine: `[BurstCompile]` on a static class with no `[BurstCompile]` methods (`AnimationCommandUtil`); `float.NaN` as a default parameter value; `[WithPresent(...)]` alongside an `EnabledRefRW<T>` parameter; and `SystemAPI.QueryBuilder()` + `RequireAnyForUpdate(NativeArray<EntityQuery>)` inside a `[BurstCompile] OnCreate`. All four are now proven patterns for C4.4–C4.8.
  - **Still owed: the mutation run.** The C4 standard is that a test which passes under both the correct and the broken implementation is worse than no test. C4.2 earned its ✅ by commenting out the two lines its tests named and watching exactly those two tests fail. C4.3's suite is green but has never been shown to *discriminate*. The two to mutate first are the `previousLoop` capture (move it below `layer.loop = command.loop`, in both `CommandApplySystem.ApplyPlay` and `PlaybackTimeSystem.PromoteQueuedClip`) and the `BoundsDirty` conditionals (make them unconditional).
- **C4.4 — events. 🔨 WRITTEN, NOT COMPILE-VERIFIED.** `EventEmissionSystem` + 12 fixtures in `EventEmissionSystemTests`, plus the queue-promotion fixtures in `PlaybackTimeSystemTests` reworked for A30. The MCP was still down when this was written; the owner gated C4.3 by hand and this phase is waiting on the same.
  - **A30 — queue promotion is deferred by one advance.** Building the emission system exposed a defect in C4.3's promotion: promoting in the same advance that finished the clip meant `ClipFinished` named the *follow-up*, and every marker in the finishing clip's last segment was collected against the wrong timeline and dropped. `PlaybackTimeSystem` now raises the completion, holds the final pose, and promotes at the top of the next advance. Costs one extra frame of the final pose on hard-cut queues only. Recorded in §5.4 with the rejected `finishedClip`-field alternative.
  - This is the second time in C4 that a defect was invisible from inside the system that contained it and only appeared when the *next* system had to consume its output — A28 was the first. Both were found by writing the consumer, not by re-reading the producer.
  - `EventEmissionSystem` **appends and enables only**, per A28. The two fixtures that pin it (`AResolveFailureRaisedEarlierInTheFrame_SurvivesEmission`, `EventsFromTheFrameBefore_AreGoneByTheNextEmission`) run `CommandApplySystem` and `PlaybackTimeSystem` in order alongside it, because neither contract is observable from one system alone.
- **C4.5 — transform technique.** Sample + apply. First end-to-end visible motion.
- **C4.6 — flipbook.** `SpriteMaterialSystem`.
- **C4.7 — VAT + bounds.** `VatMaterialSystem`, `RenderBoundsUpdateSystem`.
- **C4.8 — LOD.** `AnimLodDistanceSystem`.
- **C4.9 — acceptance + smoke scene.** The §11.2 PlayMode suite and the host-shaped subscene. Owner confirms on-screen playback.

## Traps carried in from C3, to design against rather than rediscover

- **`PlaybackLayer.previousLoop` is written by `CommandApplySystem` (C4.3) or the crossfade is wrong.** Nothing writes it today. If C4 forgets, every outgoing clip reverts to its authored loop mode mid-crossfade — a pop in exactly the transition the blend exists to smooth.
- **The `previous*` demotion must happen before `layer.loop` is overwritten.** Spec §5.4 says so explicitly; the ordering is the whole reason `previousLoop` exists.
- **`RenderBoundsUpdateSystem` must not use a change-version filter on `PlaybackLayer`** — `PlaybackTimeSystem` writes `time` into it every frame for every active actor, so the filter degenerates to always-true. `BoundsDirty` is the signal; disabling it is the sole reset path.
- **Never write `offsetBounds` into `RenderBounds` directly** — it is offset space; translate by `ActorRestBounds` (actor space) first.
- **`RigPartRef` order is unspecified.** C4.2 rebuilds it; nothing may depend on bake order.
- **Timers are never gated on `AnimVisible`.** Logic group ungated, presentation group gated. Off-screen actors keep exact time and keep firing events.
- **`AnimLod` is opt-in (A23).** Its absence is conformant; do not widen queries in a way that assumes it.

## Test-integrity standard for this module

Gate 4 found three non-discriminating tests in C3 — the deleted phase fixture, the surrogate-pair test, and a PlayMode smoke test that asserted only an assembly name while the whole suite ran in the wrong mode. For every test written in C4:

**State the mutation it catches.** If deleting the line the test names leaves it green, the test is not coverage. Where a property is untestable by construction, say so in the spec and do not write a fixture that pretends otherwise (the A-4 precedent).

Two specific things to pin, because both have already failed silently once in this package:
- The `previousLoop` copy — mutate it away and a test must fail.
- The `BoundsDirty` reset — deleting the disable must fail a test, not merely make bounds recompute more often.
