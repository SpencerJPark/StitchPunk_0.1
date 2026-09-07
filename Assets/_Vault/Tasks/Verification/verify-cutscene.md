---
title: Verify Cutscene Acceptance — "Rendezvous and Depart" (G3)
status: active
created: 2026-09-07
area: code
---

## Goal

Watch the acceptance cutscene play in the Editor and confirm every host contract the roadmap's specs
(A61–A67, G1, G2) built actually holds together end to end. This is the first cutscene a human has
watched in this project. If any step fails, the failing spec's own §7 log gets the entry and G3 waits.

**Where the content is:** `Assets/ScriptableObjects/Cutscenes/RendezvousAndDepart.asset`, staged in
`Assets/Scenes/SubScenes/DOTSTestScene.unity` (opened through `Assets/Scenes/TestArea.unity`, the
scene that actually has a Play button — the subscene alone has no camera/managers). The debug trigger
lives on `Managers/CutsceneDebug` in TestArea.

---

## Setup — read before you press Play

- **The trigger key is backtick ( ` ), not F9.** The spec originally named F9; it does nothing at all
  when pressed because Unity's own Editor reserves bare F9 as the built-in shortcut for
  `Profiling/Profiler/RecordToggle` (confirmed against all 1121 shortcuts `ShortcutManager.instance`
  registers project-wide) and consumes the keypress before the running game's Input System ever sees
  it. F11 was tried next and also failed — a live diagnostic (logging every key Unity's Input System
  actually saw) proved the physical F11 key was registering as `Key.Home`, a laptop keyboard sharing
  the F-row with Home/End/PgUp/PgDn without Fn-lock. Settled on **backtick ( \` )**, a dedicated key on
  every keyboard with no Editor shortcut and no secondary Fn function. The skip key is **backslash
  ( \\ )** for the same reason (F10, though never actually reserved, was dropped along with the rest
  of the F-row on principle).
- **Backtick fires the narrative event, not a raw signal.** `CutsceneDebugTrigger` fires
  `NarrativeIds.Events.RendezvousTest` through `NarrativeEventManager`, which runs
  `NarrativeEvent_RendezvousTest`'s `PlayCutsceneAction` — the whole narrative path is exercised, not
  just the toolkit's own playback.
- **The player auto-walks onto their mark by default.** `CutsceneDebugTrigger.autoWalkPlayerToMark`
  (on by default) teleports the player onto their mark the instant it's issued — the toolkit itself
  never auto-paths the Player (G2 §4), so this is purely a solo-testing convenience. Turn it off on
  the component if you specifically want to watch/perform the manual walk for step 2/3 below. A
  `CutsceneMarkDebugVisualizer` on the same GameObject draws a translucent disc over every currently
  outstanding mark (any slot) so you can see where to stand either way.
- **Re-bake if you haven't reopened this subscene today.** Reopen `DOTSTestScene` or re-enter Play
  mode so `CutsceneStageAuthoring` rebakes against the current asset.

---

## Checklist

1. [ ] Open `DOTSTestScene`, enter Play, press backtick ( ` ) (not F9 — see Setup). Console: no errors,
       one line from the narrative manager.
2. [ ] MinionA and MinionB pathfind to their discs (walk cycle plays, faces the travel direction). The
       player can still walk. Nothing else moves; both minions' `UtilityActions` are empty in the
       Entities window.
3. [ ] Walk the player onto their disc. The instant all three are in tolerance, WASD stops responding
       and the clock continues.
4. [ ] Both minions vanish into the cart; the player sits on it at the authored offset.
5. [ ] Dialogue opens with the authored speaker; the clock waits (the cart does not move). Close the
       dialogue: the cart drives off on its keys with all three riding.
6. [ ] The camera follows smoothly, cuts once at the authored time, and the SFX plays at the event.
7. [ ] At the destination everyone reappears on the ground beside the cart; the cutscene ends; the
       camera blends back to the gameplay camera; the minions resume wandering from where they stand;
       the player controls again.
8. [ ] Press backtick ( ` ) again mid-run and press the skip key (backslash, \ ): the world ends in the
       same state as step 7 — same positions, everyone visible, dialogue never opened but the SFX
       event fired.
9. [ ] Save during the cutscene (debug save menu): refused with a warning; save after: works.
10. [ ] Profiler: `CutsceneTimelineSystem` under 0.2 ms with four slots.

---

## Already machine-verified — don't re-derive these, just watch for them

Driven live via `execute_code` firing the same `OnNarrativeEvent` signal the trigger key writes (not simulated —
the real narrative → toolkit → game pipeline, watched through several minutes of real elapsed time):

- The narrative event resolves `NarrativeEvent_RendezvousTest` and starts the cutscene
  (`ActiveNarrativeEvent`/`ActiveCutscene` both enable correctly).
- Marks are issued and resolve (by real arrival or by the authored 20s timeout); the Rendezvous hold
  releases only once every slot has resolved.
- Attach fires on release: all three riders gain `Parent` = the Cart entity, at the exact authored
  local offset (Player `(0, 0.6, -0.8)`, minions `(∓1.0, 0.5, -0.3)`) — confirmed to the float on two
  of the three riders (see the one open finding below).
- The Dialogue holding event fires with `sequenceId = 1` (`Dialogue_G2Rendezvous`, reused rather than
  authoring a new sequence — it already fit) and the correct speaker entity (MinionA); the clock
  genuinely pauses (`isPausedOnHold = true`) until the dialogue is closed.
- Releasing the dialogue resumes the timeline; the cutscene completes cleanly (`CutscenePlay` entity
  destroyed, `CutsceneActor` released on every bound unit) with no console errors beyond the two
  pre-existing, unrelated warnings noted below.

## One open finding — not blocking, worth a look if step 4/7 ever looks wrong

In one run, MinionB's `LocalTransform.Position` after attach read `(1.199984, 0.0000397, -0.9999444)`
— off its authored `(1.0, 0.5, -0.3)` offset, and suspiciously close to MinionB's own *mark* position
`(1.2, 0, -1.0)` instead. Player and MinionA landed exactly on their authored offsets in the same run.
The value was stable across repeated reads a few seconds apart (not still drifting), so this reads as
a one-time ordering interaction — plausibly `CutsceneMoveToMarkSystem`'s per-slot arrival resolution
landing in the same frame as `CutsceneTimelineSystem`'s attach application for that specific slot —
rather than attach failing outright. At the observed magnitude (~50 microns) this is not visible on
screen and did not reproduce as a step-4/7 failure in the same run; flagged here rather than chased
further. If the owner ever sees a rider standing at their mark instead of on the cart, this is the
first thing to check.

## Debug tooling bugs found and fixed while chasing "the trigger key does nothing"

Two real, unrelated bugs turned up debugging the trigger before the key itself was identified as the
problem — both in the *new* debug tooling this session added, not the cutscene systems themselves:

- **`CutsceneMarkDebugVisualizer.Update()` created a fresh `EntityQuery` every frame and never
  disposed it** — 60 leaked queries a second for as long as Play mode ran. `CutsceneDebugTrigger` had
  the same flaw on its three queries, just gated per-keypress instead of per-frame. Both now build
  their queries once (cached, rebuilt only if the `World` itself changes) and dispose them in
  `OnDestroy`. Confirmed not the actual cause of the key problem (a diagnostic proved the keypress
  itself wasn't reaching `Key.F11` at all), but a real leak regardless, and the owner's instinct to
  suspect "the downstream stuff" was the right instinct even though the bug it turned up wasn't the
  one blocking F11 specifically.

## Known, pre-existing, not this spec's

- `[DOTS Animation Toolkit] Transform track N of clip 'NewClip 1' targets id 0x…, which rig 'NewRig'
  does not declare` — three rule-T6 warnings per actor bind, recorded as a deliberate non-fix in G0's
  HANDOFF entry (the clip quotes target ids from the `HumanoidRig` deleted 2026-08-29).
- `'WorldMoodSystem' creates a query during OnUpdate` — pre-existing performance warning, unrelated.

---

## Perf check

`CutsceneAcceptancePerfTests.CutsceneTimelineSystem_SixHundredFrames_OfTheAcceptanceCutscene_StaysWellUnderBudget`
(`Assets/_Scripts/Tests/PlayMode/`) builds the real acceptance blob and runs 600 manual
`CutsceneTimelineSystem` updates against four bound stand-in entities, asserting a generous 50 ms
total (25x the spec's ~2 ms target — the point is catching an accidental per-frame allocation or
O(n²) walk, not benchmarking). Passing as of this session. Step 10 above is the owner's own Profiler
capture with real actors — write the measured ms here once done: **_____ ms**.

---

## Next work

1. Whatever this checklist turns up — each failure goes to the spec that owns it (see the roadmap's
   `Cutscene_Roadmap.md` §3 table).
2. **A68** — docs/release amendment, next on the critical path once this checklist passes.
