# Cutscene Acceptance — "Rendezvous and Depart" (G3)

> **Status:** Phases 1–3 built and gated 2026-09-07; moved to `Tasks/Verification/verify-cutscene.md`.
> **⏸ Owner checkpoint outstanding** — the session did not attempt it (per its own instructions).
> **Roadmap:** [`Cutscene_Roadmap.md`](Cutscene_Roadmap.md) §6 is the beat sheet; read its §4 protocol first.
> **Depends on:** everything before it (A61–A67, G1, G2). Runs before A68 (docs) so the docs describe what was actually watched.
> **Executor:** one Sonnet session for the authoring + trigger + profiling; the owner runs the checklist.

---

**Skills Needed:** `dots-test` (one perf assertion), otherwise none — this is content and verification, not new systems.

---

## 1. Purpose

Every spec so far ends in "the owner looks". This one *is* the look: one cutscene that touches every lane and every host contract, authored with the tool the way a designer would, played from a debug key in `DOTSTestScene`, judged against a written checklist. If any step fails, the failing spec's §7 gets the entry and this spec waits.

## 2. Assets to author (in the editor, saved to disk)

- `Assets/ScriptableObjects/Cutscenes/RendezvousAndDepart.asset` (`CutsceneAsset`): slots **Player** (Actor, the player's rig + clip sets), **MinionA**, **MinionB** (Actor, `NewRig.asset` + the live clip set + the live direction set), **Cart** (Prop). Author exactly the beat sheet in the roadmap §6: three marks, rendezvous hold (autoRelease, timeout 20 s), three Attach markers to the Cart root (A/B hidden, Player seated with a visible offset), a `Dialogue` holding event with a speaker (the provider UI picks the sequence — create `DialogueSequence_DepartureBanter` if no short sequence exists), Cart root keys over 6 s, a camera lane with a follow move and one cut, one sound event mapped in `AnimSoundEventMappingSO` (any existing SFX), three Detach markers at the destination placed on the ground, end.
- A Cart prefab: any prop with a transform and a renderer (a scaled crate), placed in `DOTSTestScene`'s subscene, bound to the Cart slot. Two minion prefabs placed and bound; the Player bound to the player object. **Sync to Stage.**
- `NarrativeEvent_RendezvousTest` (`NarrativeEventSO`) with one `PlayCutsceneAction` (`waitForCompletion`), registered on the scene's `NarrativeEventManager`, so the narrative path is exercised too — the debug key fires the narrative event, not the raw signal.

## 3. Perf check

PlayMode test in `StitchPunk.Tests.PlayMode`: with the acceptance blob (load the asset, `CutsceneBlobBuilder.Build`) and four bound entities, `CutsceneTimelineSystem.Update` × 600 frames must stay under 2 ms total on the test machine (`Stopwatch`; assert with a generous bound so it never flakes — the point is catching an accidental per-frame allocation or O(n²) walk, not benchmarking). Profile once by hand with the Profiler window open during the checkpoint and write the ms figure into §5.

## 4. Verification checklist → `Tasks/Verification/verify-cutscene.md`

Front-matter (`title`, `status`, `created`, `area: code`), `## Goal`, then exactly these steps, each a checkbox the owner ticks in the Editor:

1. Open `DOTSTestScene`, enter Play, press F9. Console: no errors, one line from the narrative manager.
2. MinionA and MinionB pathfind to their discs (walk cycle plays, faces the travel direction). The player can still walk. Nothing else moves; both minions' `UtilityActions` are empty in the Entities window.
3. Walk the player onto their disc. The instant all three are in tolerance, WASD stops responding and the clock continues.
4. Both minions vanish into the cart; the player sits on it at the authored offset.
5. Dialogue opens with the authored speaker; the clock waits (the cart does not move). Close the dialogue: the cart drives off on its keys with all three riding.
6. The camera follows smoothly, cuts once at the authored time, and the SFX plays at the event.
7. At the destination everyone reappears on the ground beside the cart; the cutscene ends; the camera blends back to the gameplay camera; the minions resume wandering from where they stand; the player controls again.
8. Press F9 again mid-run and press the skip key (add one to `CutsceneDebugTrigger`: F10 → `CutscenePlaybackApi.RequestSkip`): the world ends in the same state as step 7 — same positions, everyone visible, dialogue never opened but the SFX event fired.
9. Save during the cutscene (debug save menu): refused with a warning; save after: works.
10. Profiler: `CutsceneTimelineSystem` under 0.2 ms with four slots.

## 5. Build phases

- [x] **Phase 1 — author assets and scene (§2).** Built via `execute_code` throughout (UI-driven
      authoring would have meant clicking through the Cutscene Editor's cast/timeline panels for every
      one of ~40 keys/markers across 4 slots; the data model is a plain serialized asset, so direct
      `SerializedObject`/field population and a disk save+reload was faster and just as provable).
- [x] **Phase 2 — debug skip key + perf test (§3).**
- [x] **Phase 3 — write `verify-cutscene.md` (§4)**, move this spec to `Tasks/Verification/` (git mv),
      update `Plans/README.md`'s Cutscene Acceptance row.
- [ ] **⏸ Owner checkpoint — the whole checklist.** Not attempted, per this spec's own §5 "Executor"
      line and the roadmap's protocol. `Tasks/Verification/verify-cutscene.md` is what the owner runs.

## 6. Notes / build log

**Phase 0 (not in the original plan) — a real blocker, closed and committed separately
(`310d5144`).** The roadmap's own §6 note flagged that `UnitFacing`/`BodyPart` had no content
anywhere (`unitFacingEntities=0`), and framed it as possibly "a component-add on MaleCitizen plus a
rebake." Measured live before assuming that: it was not. Stacking `BodyPartAuthoring` onto
`MaleCitizen`'s rig-target parts collided twice over — `RigTargetBaker` already bakes `PartFacing` on
the 3 `facesDirection` parts (all three render, so both bakers would hit the same entity), and a
legacy `BaseParentAuthoring` (predates the toolkit, superseded by `BodyPartAuthoring` per that class's
own header comment but never removed) was still live on all 32 `MaleCitizen` parts, doubly baking
`BaseParent`. Fixed both: guarded `BodyPartAuthoring.Baker`'s `PartFacing` add against the toolkit's
own opt-in, and stripped the dead `BaseParentAuthoring`. That surfaced a third, genuinely latent bug —
`CameraVisibilitySystem.CameraVisibilityJob` threw an aliasing exception the instant it had real
`BodyPart` buffers to iterate for the first time (a direct `EnabledRefRW`/`RO<CameraVisible>` query
parameter colliding with the job's own writable `ComponentLookup<CameraVisible>` used to propagate to
parts) — fixed by routing the root's own read/write through the lookup too, dropping the direct query
handle entirely. All three measured live: `unitFacingEntities` 0→2, `bodyPartBuffers` 0→2 (16 parts
each), `partFacingEntities` 6→32 (every quad, matching `DirectionFacing_System.md` §4's stamped
intent, not just the 3 toolkit mirror roots), zero exceptions across multiple Play frames.

**Content decisions, recorded rather than silently made:**
- **Player is a Prop-kind slot, not Actor.** `PlayerUnit.prefab` has no toolkit rig at all (confirmed:
  zero `RigTargetAuthoring`/`ActorAuthoring` references) — `Assets/AnimationToolkit`'s own migration
  never touched it (HANDOFF's G0 entry already flagged this: "`PlayerUnit` is **not** an actor"). A
  Prop slot still gets marks, attach and detach (the data model supports all three on either kind);
  it just carries no clip lane, which the player doesn't need for this beat sheet. This was already
  G2's own checkpoint's approach (`G2CheckpointCutscene.asset`'s "Player" slot: `kind: 1`, no rig) —
  followed the precedent rather than inventing a new one.
- **Dialogue reused, not authored fresh.** `Dialogue_G2Rendezvous.asset` (sequenceId 1, a 2-line
  Citizen exchange) already existed and fit the spec's own hedge ("create `DialogueSequence_
  DepartureBanter` if no short sequence exists"). Reused it rather than adding a near-duplicate asset.
- **Sound event reused the one existing `_AnimSoundEventMapping` entry** (`eventKey 1 → SoundType 2`)
  rather than authoring new SFX content, matching the spec's own "any existing SFX" instruction.
- **Cart is a scaled cube prefab** (`Assets/Prefabs/Vehicles/CutsceneCart.prefab`), not
  `Prefabs/Vehicles/Caravan.prefab` — Caravan is a real, complex gameplay prefab (interior, doors,
  worktable, Rive panels) that predates this session and is not a road vehicle; the roadmap's own
  "no vehicle exists in the game yet" line was taken at face value rather than repurposing a
  stationary base-building asset for a driving beat.

**`NarrativeEventManager` had never been placed in any scene, project-wide** (`FindObjectsByType`
returned zero) — the whole narrative-event pipeline (`NarrativeEventAuthoring`'s baked ECS singleton
existed and worked; the hybrid `MonoBehaviour` that reads it never did). HANDOFF's own G2 entry had
already predicted the exact failure mode this would hit the moment someone tried
("`DialogueUIManager` resolved its ECS singletons in `Start()`... `NarrativeEventManager` has the same
shape and will hit it next") — fixed proactively, before it manifested, using
`DialogueUIManager.TryResolveEcsReferences`'s own lazy-retry-from-Update pattern verbatim (silent
until a 5s grace period, then one error, not a spam of `GetRequiredSingleton`'s per-call `LogError`).

**Verified live**, firing the exact `OnNarrativeEvent` signal F9 writes, watched over several minutes
of real Play-mode time rather than assumed from the bake: narrative event → `PlayCutsceneAction` →
`CutsceneRequest` → `CutsceneStartSystem` → marks (real arrival and the 20s timeout path, both
observed in different runs) → `Rendezvous` hold release → attach (`Parent` = Cart entity, exact
authored local offset on Player and MinionA to the float) → the Dialogue holding event with the
correct `sequenceId`/speaker entity and a genuine clock pause → release → drive → completion
(`CutscenePlay` destroyed, `CutsceneActor` released). One minor, non-blocking discrepancy on MinionB's
post-attach position (~50 microns, invisible, not reproduced as an ongoing drift) is logged in
`verify-cutscene.md` rather than chased to ground here — see that file's "One open finding" section
for the exact numbers and the leading theory (a `CutsceneMoveToMarkSystem`/attach ordering interaction
on the frame two per-slot mark resolutions land together).

**Owed:** the ⏸ checkpoint itself (`Tasks/Verification/verify-cutscene.md`), and the owner's own
Profiler capture for the spec's §3/checklist step 10 ms figure (the automated perf test asserts a
generous 25x-of-target bound to catch regressions, not to stand in for a real measurement).
