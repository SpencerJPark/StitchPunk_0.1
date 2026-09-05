# Cutscene Acceptance — "Rendezvous and Depart" (G3)

> **Status:** ✅ spec ready, not built. Written 2026-09-04.
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

- [ ] **Phase 1 — author assets and scene (§2).** Done in the Editor by the session through the real UI where possible (Place / Bind / Sync), `execute_code` where the UI would be slower; every asset saved and reloaded before trusting it (HANDOFF §9 point 4).
- [ ] **Phase 2 — debug skip key + perf test (§3).**
- [ ] **Phase 3 — write `verify-cutscene.md` (§4)**, move this spec to `Tasks/Verification/` (git mv), update `Plans/README.md`.
- [ ] **⏸ Owner checkpoint — the whole checklist.** Each failed step names the spec that owns it; the session ends with that list.

## 6. Notes / build log
