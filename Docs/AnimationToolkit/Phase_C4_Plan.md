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
  - **Built blind, gated afterwards.** The MCP was unreachable for the whole build (`instance_count: 0` with the Editor open and healthy), so C4.3 and C4.4 were both written without a compiler; the owner restored the bridge afterwards and both were gated together. Numbers below are from that run.
  - Amendments recorded in §5: **A26** (`PlaybackQuery.NormalizedTime` takes the registry — pre-existing), **A27** (`PlaybackLayer.advanceStartTime`; the crossfade source emits no markers → §12 R11), **A28** (the `AnimEventOutput` clear moves from `EventEmissionSystem` to `CommandApplySystem`), **A29** (out-of-range layer index dropped silently; Queue resolves; Stop clears the queue).
  - **A28 changes C4.4's contract:** `EventEmissionSystem` **appends and enables only**. It must not clear the buffer and must not disable `AnimEventsPending`.
  - **A27 changes C4.4's inputs:** the crossing window is `[advanceStartTime, time]` on the current clip, read from the layer — not recomputed from `dt × speed`.
  - `PlaybackLayer` gained a field, so `DataContractTests.ActorRootComponents_MatchTheSection52Inventory` was updated in the same commit. If the field is reverted, that row goes with it.
  - **One API trap found and avoided by reading Entities source first:** an `EnabledRefRW<T>` parameter enrols `T` in an `IJobEntity` query as an **All** component, i.e. enabled-filtered. Both systems here take `EnabledRefRW<BoundsDirty>`, which is disabled on almost every actor almost every frame — left as the default, both jobs would have silently matched almost nothing, with no error of any kind. Fixed with `[WithPresent(...)]`; recorded in the host vault's `Gotchas.md`.
  - Four things that had to be written without a compiler and turned out fine: `[BurstCompile]` on a static class with no `[BurstCompile]` methods (`AnimationCommandUtil`); `float.NaN` as a default parameter value; `[WithPresent(...)]` alongside an `EnabledRefRW<T>` parameter; and `SystemAPI.QueryBuilder()` + `RequireAnyForUpdate(NativeArray<EntityQuery>)` inside a `[BurstCompile] OnCreate`. All four are now proven patterns for C4.4–C4.8.
  - **Discrimination verified by mutation, three runs, 2026-08-02.** Each mutation was compiled and the full PlayMode suite run against it; each produced exactly the predicted failure and no others, and the suite returned to 114/114 after every revert.

    | # | Mutation | Failed | Reported |
    |---|---|---|---|
    | A | `CommandApplySystem.ApplyPlay`: `previousLoop = command.loop` (i.e. captured *after* the overwrite) | `PlayOverACrossfade_CapturesTheModeTheOutgoingClipWasActuallyPlayingUnder` | `Expected: Once, But was: Loop` |
    | B | `PlaybackTimeSystem.PromoteQueuedClip`: `previousLoop = queuedLoop` | `APromotionWithABlend_CapturesTheOutgoingLoopMode` | `Expected: Once, But was: Loop` |
    | C | `BoundsDirty` made unconditional in **both** systems | `PlayingTheSameClipAgain_DoesNotDirtyTheBounds` **and** `AnOrdinaryAdvance_DoesNotDirtyTheBounds`, one per system | `Expected: False, But was: True` |

    Mutation A is the one that matters most: the reported value was `Loop`, not the `UseClipDefault` a deleted line would leave, which is what the fixture was built to separate. The trap that went unwritten for three build steps is now pinned by a test that provably fails when it regresses.
- **C4.4 — events. ✅ DONE 2026-08-02.** `EventEmissionSystem` + 12 fixtures in `EventEmissionSystemTests`, plus the queue-promotion fixtures in `PlaybackTimeSystemTests` reworked for A30. Gated through the MCP alongside C4.3: Console clean of `error CS`/`BC`, **205 EditMode + 114 PlayMode, all passing, each in its real mode** (project-wide EditMode is 266 — the extra 61 are the host game's own suite).
  - **A30 — queue promotion is deferred by one advance.** Building the emission system exposed a defect in C4.3's promotion: promoting in the same advance that finished the clip meant `ClipFinished` named the *follow-up*, and every marker in the finishing clip's last segment was collected against the wrong timeline and dropped. `PlaybackTimeSystem` now raises the completion, holds the final pose, and promotes at the top of the next advance. Costs one extra frame of the final pose on hard-cut queues only. Recorded in §5.4 with the rejected `finishedClip`-field alternative.
  - This is the second time in C4 that a defect was invisible from inside the system that contained it and only appeared when the *next* system had to consume its output — A28 was the first. Both were found by writing the consumer, not by re-reading the producer.
  - **Gate-process note.** The first PlayMode run of this gate wedged the Editor mid-play-mode-transition and had to be recovered with `manage_editor action=stop`; it also left an `Assets/InitTestScene<guid>.unity` behind, since deleted. Cause: `run_tests` was called immediately after `refresh_unity`, while the domain reload was still in flight, despite `refresh_unity` returning `success`. **Its return value is not a readiness signal.** Always poll `mcpforunity://editor/state` for `is_compiling: false` **and** a `last_domain_reload_after_unix_ms` newer than the compile before starting a job. The `stale_status` blocking reason on that resource is only snapshot freshness and can be ignored when every substantive field reads idle.
  - `EventEmissionSystem` **appends and enables only**, per A28. The two fixtures that pin it (`AResolveFailureRaisedEarlierInTheFrame_SurvivesEmission`, `EventsFromTheFrameBefore_AreGoneByTheNextEmission`) run `CommandApplySystem` and `PlaybackTimeSystem` in order alongside it, because neither contract is observable from one system alone.
- **C4.5 — transform technique. ✅ DONE 2026-08-02.** `TransformSampleSystem` + `TransformApplySystem` + 11 fixtures in `TransformTechniqueTests`. **205 EditMode + 125 PlayMode, all passing.** Console clean.
  - **Mutation-verified**, one run, four predicted failures and no others: deleting the `PostTransformMatrix` write fails `Apply_PutsNonUniformScaleInThePostTransformMatrix` (reporting `1.5` — the *rest* scale still sitting in the matrix, which is exactly the host's dead-scale symptom: the authored 2× silently does nothing); deleting `LocalTransform.Scale = 1f` fails `Apply_PinsLocalTransformScaleToOne` (`3.0` survives to double-apply); hardcoding target index 0 fails `EachPart_ReadsItsOwnTargetsTracks` (the hand reports the shoulder's `3.0`); deleting the `ShouldSample` guard fails `AQuantizedActor_SkipsFramesBetweenItsSampleTicks`.
  - `PlaybackTestActor` gained transform-track specs and an `AddPart` builder whose rest poses are **deliberately non-identity** — offset, rotated, non-uniformly scaled. See the open question below for why that matters more than it looks.
  - `TransformSampleSystem` carries no `UpdateAfter(AnimLodDistanceSystem)` edge yet: that type does not exist until C4.8. **C4.8 must add it** (§5.1's diagram orders sampling after LOD).

### ✅ RESOLVED — `Override` track semantics → amendment A31 (owner-approved 2026-08-02)

**Outcome: keys are offsets from the rest pose, and the sampler was changed to match the spec.** `ApplyClipToPose` now takes the rest pose; an `Override` track writes `rest + key` (scale `rest × key`), `Additive` still anchors to the composited pose below. The deciding consequence: under the absolute reading, re-posing a rig's rest — moving a shoulder, re-proportioning a limb — is silently ignored the moment any clip plays, which defeats the point of a cutout rig. It also leaves A13 and `offsetBounds` correct as written instead of requiring both to be rewritten.

**Verified by mutation, and this is the important part.** Reverting all four `Override` anchors to the old absolute behaviour now fails **ten** tests — 4 EditMode (`SamplePose_OverrideTrack_OffsetsItsMaskedChannelsFromRest`, `CompositeLayers_UpperOverride_AnchorsToRestNotToTheLayerBelow`, `CompositeLayers_AdditiveUpperLayer_StillAnchorsToTheCompositedResult`, `CompositeLayers_OverrideScale_MultipliesTheRestScale`) and 6 PlayMode. Before this phase the same change failed **nothing**. `Apply_PreservesNegativeScaleSoPartsCanFlip` correctly stayed green, its track having no scale channel.

The historical record of the conflict is kept below, because the *reason it survived three gates* is the reusable lesson.

---

Building C4.5 surfaced a **contradiction between the normative spec and the shipped, gated sampler.** It did not affect the two systems built here — both call `ClipSampler.CompositeLayers` and are correct under either reading — but it decided what an animator's keys *mean*, so it went to the owner.

**The spec says transform keys are offsets from the rest pose.** §3.2 types `position` as "x/y local offset"; §4.6 line 519 says an unkeyed target "in offset space sits at its rest pose, i.e. offset zero"; amendment A13 exists *entirely* because "transform keys hold local offsets from a target's rest pose, and rest poses live on the prefab", which is why `offsetBounds` is offset space and `ActorRestBounds` had to be invented.

**The shipped sampler treats them as absolute local values.** `ClipSampler.ApplyClipToPose` does `pose.localPosition.x = sampledPosition.x` for an `Override` track — the rest position is seeded and then discarded. `ClipSamplerTests.SamplePose_OverrideTrack_ReplacesOnlyItsMaskedChannels` locks this in deliberately: rest `rotationZ` is 0.5, the key is 1.5, and the assertion is 1.5 — not 2.0.

Both are load-bearing and they cannot both be right. Consequences:

| | Override = absolute (shipped) | Override = rest + key (spec) |
|---|---|---|
| Authoring | every clip must key each part's full local position | clips are deltas; re-posing the rig's rest updates every clip coherently |
| Re-proportioning a rig | silently ignored the moment a clip plays | works, which is the point of a cutout rig |
| `offsetBounds` / A13 | premise is wrong — keyed boxes are already actor-space | correct as written |
| Cost to change | one sampler branch, one EditMode assertion, and any clip authored so far | none |

**Why no C1/C2 gate caught it:** every fixture in `LayerCompositionTests` uses a rest pose at the origin with unit scale, where the two readings produce identical numbers — the exact "passes under both the correct and the broken implementation" shape this module keeps re-teaching. Only `ClipSamplerTests` used a non-identity rest, and it encoded the shipped behaviour rather than the spec's.

Deliberately **not** resolved unilaterally: it changed the authoring model rather than an implementation detail, and §12 R10 already flags semantic changes of this kind as host-migration cost.

**The reusable lesson.** Two of C4's three defects (A28, A30) were invisible from inside the system that contained them and only appeared when the *next* system consumed their output. This one is the third kind: invisible because **every fixture used the identity case**. An origin rest pose with unit scale makes `rest + key` and `key` the same number, so the entire Override semantics were untested while looking thoroughly tested. When a fixture picks a "simple" value for something the code branches on — zero, one, identity, empty — check whether that choice is what makes the assertion pass.
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
