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

- **The trigger key is `H`, not F9.** The spec originally named F9; it does nothing at all when
  pressed because Unity's own Editor reserves bare F9 as the built-in shortcut for
  `Profiling/Profiler/RecordToggle` (confirmed against all 1121 shortcuts `ShortcutManager.instance`
  registers project-wide) and consumes the keypress before the running game's Input System ever sees
  it. F11 was tried next and also failed — a live diagnostic (logging every key Unity's Input System
  actually saw) proved the physical F11 key was registering as `Key.Home`, a laptop keyboard sharing
  the F-row with Home/End/PgUp/PgDn without Fn-lock. Backtick/backslash were tried after that and
  worked as keys, but a real bug (next bullet) made the trigger look broken again regardless of which
  key fired it. Settled on **`H`** to fire, **backslash ( \\ )** to skip, **semicolon ( ; )** to dump
  actor state (see below) — plain keys, no Editor shortcut, no Fn sharing.
- **A real bug, not another key problem: the press handler could fire every frame instead of once,
  permanently locking player input.** A stray scoping issue put the narrative-event fire outside its
  `wasPressedThisFrame` guard, so once triggered it kept re-firing on every subsequent `Update` —
  each frame's fresh `ExecuteEventAsync` set `CutsceneActiveTag` again right after the previous
  call's `finally` had cleared it, so WASD went dead for good once the cutscene ended. Fixed, plus a
  re-entry guard: `FireNarrativeEvent` now no-ops while `ActiveNarrativeEvent` or an unconsumed
  `OnNarrativeEvent` is still set, so a second press can never stack a second run.
- **`H` fires the narrative event, not a raw signal.** `CutsceneDebugTrigger` fires
  `NarrativeIds.Events.RendezvousTest` through `NarrativeEventManager`, which runs
  `NarrativeEvent_RendezvousTest`'s `PlayCutsceneAction` — the whole narrative path is exercised, not
  just the toolkit's own playback.
- **Semicolon ( ; ) dumps live actor state to the console** — for the narrative singleton and every
  entity currently carrying `CutsceneActor` (enabled or not): every gate that can stop a unit moving
  (`CutsceneActor`, `Movement`, `Dead`, pathing/agent/StateMachine state, marks), plus health/killedBy/
  faction/position. Built to answer "why is this NPC still frozen after the cutscene ended" without
  guessing — read it in the order the field comment on `DescribeActor` in `CutsceneDebugTrigger.cs`
  lays out.
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

1. [ ] Open `DOTSTestScene`, enter Play, press `H` (not F9 — see Setup). Console: no errors, one line
       from the narrative manager.
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
8. [ ] Press `H` again mid-run and press the skip key (backslash, \ ): the world ends in the same
       state as step 7 — same positions, everyone visible, dialogue never opened but the SFX event
       fired.
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

Three real, unrelated bugs turned up debugging the trigger before it actually worked — all in the
*new* debug tooling this session added, not the cutscene systems themselves. (Two parallel sessions
worked this independently — a Sonnet 5 session through the key-collision chain, an Opus 5 session
through to the actual input-lockup bug; both are recorded here rather than only one.)

- **`CutsceneMarkDebugVisualizer.Update()` created a fresh `EntityQuery` every frame and never
  disposed it** — 60 leaked queries a second for as long as Play mode ran. `CutsceneDebugTrigger` had
  the same flaw on its three queries, just gated per-keypress instead of per-frame. Both now build
  their queries once (cached, rebuilt only if the `World` itself changes) and dispose them in
  `OnDestroy`. A real leak, but not what was blocking the key.
- **F9 was a Unity Editor shortcut and F11 collided with this laptop's Home key** (both above).
- **The actual blocker: a scoping bug let a fire re-trigger the narrative event every frame instead
  of once**, permanently locking player input once it happened (Setup, above). This is the one that
  mattered — everything before it was real, but none of it was *the* bug.

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

## Session update (2026-09-07, second pass) — three real bugs found and fixed, checklist not yet re-walked clean

The owner ran the checklist against the H/backslash/semicolon build above and found four things
wrong. Three were real, confirmed via `execute_code` live driving (not just read from source), and
are fixed. The fourth needs a clean checklist walk to confirm.

- **No camera follow — fixed.** `CutsceneCameraBridge` (the only reader of the toolkit's
  `CutsceneCameraPose` singleton) existed only in the old `CutsceneG1Checkpoint.unity`, never added
  to `TestArea.unity`. `CameraManager.cutsceneCam` was also never wired to an actual vcam in either
  scene (the field was added to `CameraManager.cs` after the prefab was last saved). Added a new
  `CutsceneCinemachine` vcam under `View/` (no Follow/RotationComposer — the bridge owns its
  transform directly) wired to `cutsceneCam`, and added `CutsceneCameraBridge` onto
  `Managers/CutsceneDebug` next to `CutsceneDebugTrigger`, in `TestArea.unity`.
- **Riders reappear at the pickup mark instead of the cart's stop — fixed, took two tries.**
  Root cause in `CutsceneTimelineSystem` (`com.dotsanimationtoolkit`): a rider's own root-key lane
  (`CutsceneMarkMerge`) only ever holds one merged key, sitting at the pre-ride pickup mark — nothing
  is ever authored for "while riding" or "after being dropped off," since that motion belongs to the
  host. `ApplyPose`'s skip-check (`attachedHostSlotIndex >= 0`) only protects a slot *while* attached;
  the first fix attempt only suppressed resampling for the exact detach frame, but every frame *after*
  detach still resampled that same stale key and re-corrupted the position. Real fix: a persistent
  `hasEverDetached` flag on `CutsceneSlotRuntimeState`, set once in `ApplyMarkerToSlotState` on Detach,
  checked in `ApplyPose` — once a slot has ever finished a ride, its root lane is retired for the rest
  of the cutscene. Verified live: player and both minions land within ~1-2 units of the cart's actual
  stop, not the pickup marks.
- **10-20s delay before control returns — fixed, was real, not a camera-confusion illusion.**
  `RendezvousAndDepart.asset`'s MinionA/MinionB walk-cycle clip block was authored with
  `duration = 21` (should be ~6.6, matching the actual ride). Segment length is driven by the
  *longest* thing authored in it (`CutsceneBlobBuilder.ComputeContentEndSeconds`), so this dragged the
  whole final segment out to 20.5s regardless of when the cart visually stops. Fixed the two clip
  blocks to `duration = 6.6`; final segment is now 6.1s. This also explains why the position fix
  looked broken on the first retest: with ~14 seconds of needless extra segment time, normal AI
  wander had already carried the minions far from the drop-off point before anyone looked.
- **Billboarding never on — fixed, but this is a standing project-wide gap, not a cutscene bug.**
  `AnimationToolkitCameraData` (the singleton `BillboardResolveSystem` requires to run at all) was
  never written anywhere in the project — confirmed live, zero entities carried it, in or out of the
  cutscene. This exact gap was flagged in `Assets/_Vault/Tasks/Verification/verify-billboarding.md`
  on 2026-08-17 (`ToolkitCameraSync` did it for the deleted demo scenes; "the real game will need its
  own writer when the toolkit is eventually adopted") and never closed when MaleCitizen was migrated
  onto the toolkit's rig. Added `AnimationToolkitCameraBridge.cs`
  (`Assets/_Scripts/MonoBehaviours/Managers/`), writing `position`/`forward` from `Camera.main` every
  `LateUpdate` — `Camera.main` already reflects whichever Cinemachine vcam is blended in, so one
  writer covers every camera. Wired onto `CameraManager` in `TestArea.unity`. Verified live:
  `resolvedRotation` on both minions' `MaleUnitVisual` billboard root is now a real, non-identity,
  camera-tracking value. Incidentally, `NewRig.asset`'s `MaleUnitVisual` billboard root entry
  (stableId 3546645462, ScreenAligned) was sitting dirty-but-unsaved in the Editor before this
  session — a blanket `AssetDatabase.SaveAssets()` call made while fixing the clip-duration bug
  flushed it to disk as a side effect. Not something this session authored, but it is real, wanted
  data (it's exactly what the billboard bridge fix needed to have something to rotate), not a
  regression.

**Not yet done: a clean, full checklist walk-through with all four fixes in place.** Every fix above
was verified individually and live (via `execute_code`, not the owner's own play-test), but nobody
has walked all 10 checklist steps end-to-end since. That's the next concrete step — see the handoff
prompt below.

## Next work

1. **Walk the full 10-step checklist above, clean, with all four fixes in place.** Should take ~10-15s
   of cutscene runtime now instead of ~30s (delay fix). Log any NEW failure against the spec that owns
   it (roadmap's `Cutscene_Roadmap.md` §3 table) — everything above is fixed, not guessed at, but this
   session verified pieces individually via `execute_code`, never the whole thing back-to-back through
   the real `H` key with a human watching.
2. **A68** — docs/release amendment, next on the critical path once this checklist passes.
