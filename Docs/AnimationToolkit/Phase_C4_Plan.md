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
- **C4.3 — playback core. 🔨 IN PROGRESS.** `AnimationCommandUtil` written but **not compile-verified** (Editor disconnected); `PlaybackQuery` / `CommandApplySystem` / `PlaybackTimeSystem` not started; no tests yet. Amendment **A26** recorded in §5.4 — `PlaybackQuery.NormalizedTime` takes the registry, because `PlaybackLayer` stores seconds and carries no duration. The densest phase; the `previousLoop` ordering trap lives here.
- **C4.4 — events.** `EventEmissionSystem`.
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
