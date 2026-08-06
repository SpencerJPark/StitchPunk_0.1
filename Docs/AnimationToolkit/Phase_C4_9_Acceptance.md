# C4.9 — the M3 acceptance list, clause by clause

**Opened:** 2026-08-05, after C4.8 closed at `c25c996` (221 EditMode + 162 PlayMode, all passing).

**What this file is.** §8 M3's ACCEPTANCE entry is a single prose paragraph, and §11.2 says the PlayMode
suite is "the M3 acceptance list (§8) verbatim". C4.9's first deliverable is therefore *not* a new suite
— most of the list was built, phase by phase, alongside the system each clause describes. The honest job
is the diff. This is that diff: every clause of §8 M3's PlayMode list and of §11.3's runtime rows, mapped
to the fixture that pins it, or marked as a gap and closed.

**The rule applied when deciding "covered".** A clause is covered only if some existing fixture would
**fail** if that clause stopped holding. A fixture that merely touches the same code is not coverage —
that is the standard the rest of this module is built on and it is not relaxed here. Where a clause was
covered only incidentally (the right assertion living in a fixture aimed at something else), it is
recorded as covered *and* where, so a future reader does not delete it by accident.

---

## §8 M3 — PlayMode (World tests)

| # | Acceptance clause | Status | Pinned by |
|---|---|---|---|
| 1 | Play → layer active, correct clipIndex | ✅ covered | `CommandApplySystemTests.Play_ResolvesTheClipAndActivatesTheLayer` |
| 2 | Blend: mid-blend pose is the lerp of both samples | ✅ covered | `AnimationLodTests.AtLevelZero_TheSameCrossfadeStillLerps` — asserts pose 50 = lerp(0, 100, 0.5) through `TransformSampleSystem`. Lives in the LOD file because it is the contrast case for the level-2 snap, but it is this clause. |
| 3 | Queue promotes on finish with crossfade | ✅ covered | `PlaybackTimeSystemTests.AFinishedClipWithAQueuedFollowUp_PromotesItOnTheNextAdvance` (promotion) + `APromotionWithABlend_CapturesTheOutgoingLoopMode` (asserts `Blending`, `previousClipIndex`, `blendDuration`) |
| 4 | Stop fade-out deactivates | ✅ covered | `PlaybackTimeSystemTests.AStopFade_DeactivatesTheLayerWhenItCompletes`; `CommandApplySystemTests.StopWithABlend_KeepsTheLayerActiveWhileTheOldClipFadesOut` |
| 5 | `ClipFinished` emitted exactly once per Once-completion | ✅ covered | `EventEmissionSystemTests.ACompletion_IsReportedOnceNotOnEveryFrameAfterwards`; `AClipThatFinishedAndDeactivated_StillReportsItsCompletion` |
| 6 | `ClipResolveFailed` on unknown id, layer stays inactive | ✅ covered | `CommandApplySystemTests.PlayOfAnUnknownClip_LeavesTheLayerAloneAndReportsIt`; `QueueOfAnUnknownClip_LeavesTheSlotEmptyAndReportsIt` |
| 7 | Events cleared next frame, `AnimEventsPending` toggles correctly | ✅ covered | `EventEmissionSystemTests.EventsFromTheFrameBefore_AreGoneByTheNextEmission`, `EmittingAnEvent_EnablesThePendingFlag`, `EmittingNothing_LeavesThePendingFlagAlone`; `CommandApplySystemTests.AnActorWithNoNewEvents_HasItsPendingFlagCleared` |
| 8 | ECB-instantiated actor re-binds parts **and animates** | ⬜ **gap → closed** | `RigBindingSystemTests` covered the rebind only, and never sampled. New: `RuntimeAcceptanceTests.AnEcbInstantiatedActor_RebindsItsPartsAndThenAnimatesThem`. **Building it produced amendment A35** — see below. |
| 9 | `RenderBounds` on clip change; `BoundsDirty` raised by Play / queue-promotion / finish / blend-completion, cleared by the write; a time-only frame leaves both untouched | ✅ covered, all six sub-clauses | Raise: `CommandApplySystemTests.PlayingTheSameClipAgain_DoesNotDirtyTheBounds` (its guard asserts the first Play *does* raise), `PlaybackTimeSystemTests.APromotion_DirtiesTheBounds`, `AOnceCompletionWithNoQueue_DirtiesTheBounds`, `ACompletedBlend_DirtiesTheBounds`. Clear: `RenderBoundsUpdateSystemTests.TheDirtyTag_IsClearedByTheWrite`. Time-only: `AFrameThatOnlyAdvancesTime_LeavesBoundsUntouched` + `PlaybackTimeSystemTests.AnOrdinaryAdvance_DoesNotDirtyTheBounds`. Value: `ActorBounds_AreTheRestBoxGrownByTheClipOffsets` |
| 10 | `AnimVisible` disabled → `TargetPose` frozen **while `time` keeps advancing**; re-enable → next-frame refresh | ⬜ **gap → closed** | `TransformTechniqueTests.AnInvisibleActor_IsNotSampled` runs the sample system alone, so it cannot distinguish "presentation is gated" from "the whole actor is gated", and never re-enables. New: `AnInvisibleActor_FreezesItsPoseButNotItsClock` + `AReappearingActor_RefreshesOnItsFirstVisibleFrame` |
| 11 | LOD 2 mid-blend swap → blend completes on schedule (§5.10) | ⬜ **gap → closed** | The pieces existed (`AtLevelTwo_ACrossfadeIsRenderedAsAHardCut`, `ACompletedBlend_ReleasesTheSourceSlot`) but no fixture drove a level change *across* a blend, so "blend timers keep advancing at every level" — asserted in prose in `AnimationLodPolicy.SnapsBlendWeights`' own doc comment, which names §11.2 as its test — was executed by nothing. New: `ALodSwapMidBlend_LeavesTheBlendRunningAndCompletesItOnSchedule` |
| 12 | Sample-rate phase spreads two actors onto different sample frames | ⬜ **gap → closed** | `RigBindingSystemTests.TwoInstancesOfOnePrefab_GetDifferentSamplePhases` asserts the phase *values* differ; `TransformTechniqueTests.AQuantizedActor_SkipsFramesBetweenItsSampleTicks` covers one actor skipping. Neither can see the property the phase exists for, which no single actor has. New: `TwoActorsAHalfPhaseApart_SampleOnAlternatingFrames` |
| 13 | Burst gate: all systems compile under Burst with safety checks on, no `BC` in test-run logs | ✅ covered by process, not by a fixture | The standing compile gate (`read_console(types=["error"])` clean of `error CS` / `BC` before every run). Deliberately not a fixture: a test asserting "no compiler error occurred" can only pass, since a `BC` error prevents the suite from running at all. |

## §11.3 — product-owner edge cases, the halves that are C4's

| Edge case | Runtime half | Status | Pinned by |
|---|---|---|---|
| Empty clip | Playing it holds rest pose; `ClipFinished` fires at `duration` for Once | ⬜ **gap → closed** | New: `AnEmptyClip_HoldsTheRestPose_AndStillReportsItsCompletion`. (V10 validation + bake halves are M1/M2, closed in C2/C3.) |
| Single-frame clip | VAT `frameCount = 1` clamps addressing, no out-of-range row read — *PlayMode property values* | ⬜ **gap → closed** | New: `ASingleFrameVatClip_ClampsToItsOwnRow`. (The EditMode layout maths half is M2's, closed in C1.) |
| LOD swap mid-blend | Timer advances through the swap; final weight = 1 exactly at `blendDuration` | ⬜ **gap → closed** | Same fixture as row 11 above — both assertions are in it. |
| Zero-bone mesh | — | n/a to C4 | M2/`VatTextureBaker`, EditMode, scheduled C6 |
| Hot reload of authoring assets | — | n/a to C4 | M5 preview + M2 bake-hash, scheduled C6/C7 |

---

## Net result

**Seven new fixtures**, all in `Tests/PlayMode/RuntimeAcceptanceTests.cs`. Nothing already covered was
restated: re-asserting a covered clause would grow the test count without growing coverage, and would put
two fixtures in the position of failing together for one defect.

What the seven have in common is that **each spans more than one system**, which is why none of them fell
out of a single build step — the binding group plus the whole chain; the ungated logic group beside the
gated presentation group; the blend timer in one group and the snap in another. That is the same pattern
that produced A28, A30 and A31: a defect is usually invisible from inside the system that contains it.

PlayMode count: **162 → 169.** Final state: **221 EditMode + 169 PlayMode, all passing**, Console clean of
`error CS` / `BC`.

## Discrimination — mutation-verified, four runs, six mutations

Every mutation below was compiled and the full PlayMode suite run against it; the suite returned to
169/169 after each revert. **No fixture in this file is unverified.**

| # | Mutation | Predicted | Actually failed | Reported |
|---|---|---|---|---|
| M1 | `TransformSampleSystem`: `ShouldSample(…, sampleSettings.phase01)` → `0f` | phase spread only | `TwoActorsAHalfPhaseApart_SampleOnAlternatingFrames`, **alone** | `expected sampled=True, But was: False` at t=0.05 |
| M2 | `VatMaterialSystem`: `clamp(…, vatFrameCount - 1)` → `vatFrameCount` | single-frame VAT only | `ASingleFrameVatClip_ClampsToItsOwnRow`, **alone** | `Expected: 7.0, But was: 8.0` — the next clip's first row, exactly the defect |
| M3 | `AnimationLodPolicy.SnapsBlendWeights`: `>= 2` → `> 2` | LOD-swap + the existing level-2 fixture | `ALodSwapMidBlend_…` **and** `AnimationLodTests.AtLevelTwo_ACrossfadeIsRenderedAsAHardCut` | `Expected: 100.5, But was: 50.5` / `Expected: 100.0, But was: 50.0` |
| M4 | `PlaybackTimeSystem.AdvancePlaybackJob` gains `[WithAll(typeof(AnimVisible))]` — the §5.9 trap | both visibility fixtures | `AnInvisibleActor_FreezesItsPoseButNotItsClock`, `AReappearingActor_RefreshesOnItsFirstVisibleFrame`, **and nothing else** | time `Expected: 0.5, But was: 0.1`; pose `Expected: 6.5, But was: 2.5` |
| M5 | `TransformSampleSystem`: `restPose = restPoseLookup[…]` → `default` | empty clip, ECB spawn, + the transform suite | 14 fixtures incl. `AnEmptyClip_…` and `AnEcbInstantiatedActor_…` | `Expected: 0.5, But was: 0.0`; `Expected: 3.0, But was: 2.5` |

**M4 is the important one.** "Timers are never gated on `AnimVisible`" is listed in the C4 plan as a trap
to design against, and it was pinned by **nothing** before C4.9 — no existing fixture failed under M4. The
failure value `2.5` on the re-appearance fixture is exactly the "resumed from where it froze" outcome its
doc comment predicts, which is what makes it a three-way discriminator rather than a two-way one.

**M1 and M2 each failed exactly one fixture**, which is the cleanest possible result: those two clauses now
have coverage that exists nowhere else in the package.

### One fixture-integrity defect found in this file's own first draft

`AnEmptyClip_HoldsTheRestPose_AndStillReportsItsCompletion` originally asserted "the pose equals the rest
pose" against a part whose `TargetPose` **`PlaybackTestActor.AddPart` had already seeded from the rest
pose** — exactly as `RigTargetBaker` does. "Holds the rest pose" and "was never written at all" are the
same numbers, so the fixture would have passed against a system that published nothing. This is
shape #1 from §11, the same trap three C4.6 sprite fixtures fell into. Fixed by scribbling every channel
with `-999` first, so a pass now requires a real write.

### Two limits recorded rather than papered over

- **`ALodSwapMidBlend_…`'s timer-continuity assertions (steps 2–3) have no reachable single-line
  mutation.** The architecture already separates the blend timer (logic group, `PlaybackTimeSystem`) from
  the snapped weight (`ClipSampler`, which receives layers by value and cannot write them), so "the snap
  stopped the timer" is currently unrepresentable. M3 proves the fixture catches a *snap* defect; the
  timer half is a regression guard against a future refactor that gives the sampler write access or moves
  the snap into the logic group. Stated here rather than claimed as coverage it does not have.
- **`AnEcbInstantiatedActor_…` does not discriminate the part rebuild** — see A35. It fails under M5, so it
  genuinely exercises the spawn→sample→apply chain, but no mutation of `RigBindingSystem` can fail it,
  because Entities performs the remap first.

---

## Amendment A35, which building row 8 produced

Recorded in full at §5.3 of `Phase_B_Architecture.md`. In short: **§5.3's stated rationale for
`RigBindingSystem` is false for Entities 6.5.** `Instantiate` *does* remap entity references inside
dynamic buffers when the target is a member of the instantiated `LinkedEntityGroup`
(`InstantiateEntitiesGroup` → `PatchEntitiesForPrefab`, which is passed the archetype's buffer patches).
A baked actor arrives at the system already bound to its own parts, so the part rebuild is redundant on
the production path.

It was found the way this module keeps finding things: the fixture was written to assert the mis-binding
as a *guard*, on the strength of the spec, and the guard failed with the instance already correctly bound
— before any system had run.

**The reusable lesson, and a new entry for the §11 test-integrity standard.** `RigBindingSystemTests` has
seven fixtures aimed directly at this system and none of them could see it, because its hand-built actors
start with an **empty** `RigPartRef` buffer — only `RigBindingBakingSystem` fills that buffer, and those
fixtures deliberately bypass the bakers. So they exercise *first-time binding of an unbound actor*, not
*re-binding a mis-bound copy*. Deleting the rebuild does fail four of them, but every failure reads
`Expected: 2, But was: 0` — never filled, rather than wrongly filled.

That is a **sixth shape of non-discriminating test**, and unlike the first five it is not visible by
reading the fixture:

> **The fixture exercises the system under a precondition production never presents.** Every assertion in
> it is true and meaningful; only the belief that they cover the shipped path is wrong. The first five
> shapes were about the *values* a fixture chose. This one is about the *state it started from*.

**Decision: the rebuild stays** — it is load-bearing for any actor reaching the system by another route
(a pooling pass that re-parents parts and re-enables the tag is the case `ReBindingAnActorTwice…`
anticipates), it costs one `LinkedEntityGroup` walk once per spawn, and `phase01` re-derivation plus the
tag disable must happen there regardless. The spec's *rationale* is corrected, not its code. Revert note
and evidence are in A35.

**Owed, deliberately not done here:** `RigBindingSystemTests` should gain a fixture that starts from a
*populated* buffer, so C4.2's suite covers the path production takes. That is a test-integrity gap, not a
defect, and folding a rewrite of C4.2 into the acceptance step would blur what C4.9 verified.

---

## Second deliverable — the host-shaped smoke scene

§9's C4 row wants "a host-shaped smoke scene (subscene with one cutout actor) [that] animates in this
repo", with **user-confirmed on-screen clip playback** as the evidence. That is the one item in this
module Claude cannot verify: there is no screenshot path and no headless build.

**Built, and awaiting the owner's eyes.**

| Artefact | Path |
|---|---|
| Scene to open | `Assets/Scenes/AnimationToolkitSmoke.unity` |
| Subscene | `Assets/Scenes/SubScenes/AnimationToolkitSmokeSubScene.unity` |
| Builder (re-runnable) | `Assets/AnimationToolkitSmoke/Editor/SmokeSceneBuilder.cs`, menu **Tools ▸ DOTS Animation Toolkit ▸ Build Smoke Scene** |
| Generated assets | `Assets/AnimationToolkitSmoke/Generated/` — rig, clip, clip set, three materials |

The actor is a three-quad cutout: a `Torso` that bobs on Y, and `LeftArm` / `RightArm` that counter-swing
on Z, from one 2-second looping clip on layer 0. **The arms are given opposite phase deliberately** — one
moving part proves only that something moved, whereas two moving in opposition prove each part read *its
own* target's track, which is the exact failure the source audit found in the host game (`BodyPartInitSystem`)
and the one a glance at the screen can actually catch.

No toolkit shader is involved: build step C5 owns those, and the transform technique under test drives
`LocalTransform` and `PostTransformMatrix` rather than any material property, so plain URP materials keep
this a test of C4 rather than of C5.

The builder is a committed, idempotent menu item rather than a one-shot script, because the artefact is
verified by eye — and when an actor looks wrong the useful question is "what exactly was it built from".
It lives under `Assets/` in its own Editor-only assembly so it can never reach the package or a player
build; the package's own shipped samples are a `Samples~` concern belonging to C8.

### What building it found — amendment A36

The first run **aborted on its own validation guard**, which is the guard doing its job:

```
[SmokeSceneBuilder] Error V07: Clip 'SmokeWave' has a VAT source but set 'SmokeClipSet'
                    references no VAT texture set.
```

The clip has no VAT source. `ClipAsset.vatSource` is a plain `[Serializable]` class field rather than a
`[SerializeReference]` one, and **Unity cannot serialize null for one** — it writes a default block and
materialises a non-null instance on load. The saved asset proves it:

```yaml
vatSource:
  sourceClip: {fileID: 0}
  sampleFps: 30
  loopSafe: 0
```

V07 asked `clip.vatSource == null`, so **every clip asset that has ever been saved and re-read reads as
VAT-sourced**. V07 is an Error, so `ClipRegistryBuilder` throws, **no registry is baked at all**, and every
actor in the game holds its rest pose forever. That is a shipping-blocking defect for the first real user
of this package, and the entire 221-fixture suite was blind to it because every fixture builds clips with
`CreateInstance` and never writes one to disk — where the field genuinely *is* null. The two existing V07
fixtures went further and asserted the broken reading directly.

Fixed semantically (a VAT source counts only when it names a `sourceClip`), both fixtures re-aimed at a
source that names an `AnimationClip`, and a new fixture pins the case that was broken. Full detail and the
rejected `[SerializeReference]` alternative are in A36 at §3.5.

**A35 and A36 are the same shape, two days apart:** a rule or system exercised only under a precondition
production never presents — an unbound part buffer in one, an in-memory asset in the other. Both surfaced
the moment something built *real* assets instead of fixtures. The generalisation, recorded as owed in A36:
**a suite that constructs every input in memory has no coverage of the serializer, and the serializer is
part of the authoring contract.** §11.1 should grow a small disk-round-trip tier — M1/M6 scope, not C4's.

### What the owner needs to confirm

Open `Assets/Scenes/AnimationToolkitSmoke.unity`, press Play, and check:

1. The three quads are **visible** (red torso, blue left arm, green right arm).
2. The torso **bobs up and down**, smoothly and continuously looping.
3. The two arms **rotate in opposite directions** — not in lockstep, and not both following the torso.
4. Nothing in the Console.

Item 3 is the one that matters most; 2 without 3 would mean every part is reading target 0.
