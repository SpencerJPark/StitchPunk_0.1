---
title: Verify — Animation Event Timing
status: active
created: 2026-08-29
area: code
---

## Goal

Confirm authored `AnimEventOutput` events now drive the durations that used to be hand timers:
`WaitForAnimEvent`/`WaitForClipFinished` behavior commands exist and interpret correctly, Pickup
runs on `WaitForClipFinished` instead of a guessed `WaitTime 1s`, and combat's hit-timing fires on
the swing clip's authored `Attack` event with `hitTime` demoted to a fallback/timeout. Compiled and
test-run this session with the Editor connected — the play-in-Editor items below are the pass the
spec's §8 calls for, which this session couldn't do itself (owner chose "build straight through" —
see Notes for the checkpoints this skipped).

## Steps

### Compile + tests (done this session)

- [x] Full recompile — console free of `error CS####` after one real fix: the toolkit's generated
  `AnimEvents`/`TargetTags` constants (`Assets/Generated/DotsAnimationToolkit/`) had no asmdef, so
  `StitchPunk.Systems` (a different, non-default assembly) couldn't see `AnimEvents.Attack` (CS0103).
  Added `StitchPunk.Generated.asmdef` there and referenced it from `StitchPunk.Systems.asmdef` — see
  Notes.
- [x] The Burst `BC0101`/`BC1055` hash-cache errors on `UnitAnimationAssignmentSystem` /
  `PlayerAttackSystem` (the `AIUtils.GetAnimationByAction` call, unrelated to the one line this plan
  touched in that file) / `DesignChangeSystem` are the same standing Editor-session Burst JIT issue
  `verify-behaviorcommandsplit.md` recorded earlier today, confirmed pre-existing on files this plan
  never touched — needs an Editor restart, not caused by this work.
- [x] EditMode ▸ `BehaviorCommandCatalogTests` — 3/3 green (`WaitForAnimEvent`/`WaitForClipFinished`
  added to the pinned implemented + blocking sets).
- [x] PlayMode ▸ `BehaviorExecutionSystemTests.ThreeCommandBehavior_AdvancesOneCommandPerTick_ThenCompletes`
  — green (sanity that the dispatch switch's two new arms didn't break the existing interpreter path).

### Regression smoke (owner, needs the Editor + Play mode)

- [ ] **Retime demo (the plan's headline case, §8):** open the Punch clip in the Clip Editor, move
  its authored `Attack` event marker, play a swing in `DOTSTestScene` — the damage moment should move
  with **no `AttackSO` edit**. The `Attack` event doesn't exist on any clip yet (this session only
  confirmed the registry key — see Notes) — **author it on Claw and Punch first**, or the swing will
  just use the `hitTime` fallback (0.3s) and nothing will appear to change when you retime.
- [ ] Pickup completes exactly when its clip ends, at 1× and 2× speed, and while off-screen (logic
  group never gates on visibility — confirm events still fire). Watch for a stuck pickup — if
  `WaitForClipFinished` never completes, check the Action-layer clip is actually `LoopMode.Once`
  (`PickupBehaviour.asset`'s `PlayActionAnimation` step was flipped from `Looping: 1` to `Looping: 0`
  as part of this change — a looping clip never emits `ClipFinished`).
- [ ] An attack with no `Attack` event authored (everything, until you author one) still lands damage —
  confirm the `[Attack] Attack event never arrived ... falling back to hitTime` warning appears in the
  Combat log category the first time.
- [ ] Once an `Attack` event is authored on Claw/Punch: confirm damage lands on the event's frame, not at
  the old fixed 0.3s — and if `hitTime` is left at its default 0.3s while the authored event lands
  later, the timeout will win first (false-early hit). Bump `hitTime` upward once you author the
  event, per the AttackSO tooltip.
- [ ] Interrupt a unit mid-`WaitForClipFinished` (Pickup) and mid-`WaitForAnimEvent` (once anything
  uses it) — confirm cleanup tears down cleanly. Neither command holds state outside `StateMachine`
  (`CommandTimer` only), so this should fall out of the existing interrupt path for free.

## Notes

- **Hit event key — corrected:** this session initially minted a new `Hit`/19 registry entry, not
  realizing the pre-existing, unused `Attack`/18 row (`ProjectSettings/DotsAnimationToolkitAnimEventKeyRegistry.asset`)
  was already meant for exactly this. Owner corrected it same-session: `Hit`/19 was deleted, the
  combat trigger uses `AnimEvents.Attack` (key 18), and `Attack`'s description was updated to state
  its real purpose. `Damage`/17 is still unclaimed/unused. Owner will place the marker on Claw/Punch
  themselves in the Clip Editor — **not done yet**, tracked in the smoke steps above.
- **`Assets/Generated/` asmdef gap:** this is the first game-code consumer of the toolkit's generated
  vocabulary constants (`AnimEvents`/`TargetTags` existed already but nothing referenced them). Fixed
  by adding an asmdef there; recorded in `Systems_AI.md` so the next consumer doesn't rediscover it.
- **Combat trigger is a race, not a bake-time detection:** the spec called for a bake-time warning
  when an attack's clip lacks an `Attack` event, but `AttackBlob` carries no clip reference to check
  against (the attack→clip mapping lives per-unit in `UnitDataBlob.actionAnimations`, resolved by
  `ActionType`, one indirection away). Implemented instead as `AttackRequestSystem` racing the event
  against `elapsed >= hitTime` every frame — whichever fires first wins, `hitFired` guards the
  double-fire — with a **runtime** warning log on the fallback path instead of a bake-time one. This
  is simpler and needs no new plumbing, but means `hitTime` has to stay above the authored event's
  real time for the event to actually win the race (see the AttackSO tooltip) — a bake-time check
  would have been foolproof and might be worth revisiting once every attack clip has its event.
- **Assignment shrink NOT done (spec §5):** `UnitAnimationAssignmentSystem`'s Action-layer auto-assign
  is still load-bearing — only Pickup converted to explicit `PlayActionAnimation` +
  `WaitForClipFinished` ownership this session. `FleeBehaviour` has no explicit action-layer command
  and still depends on the generic `unitAction.current` → clip resolution in that system. Removing
  the branch now would silently break Flee's animation. Deferred until every behavior explicitly owns
  its Action-layer clip.
- **`hitTime` fallback is temporary** (spec §4, owner-decided): delete it as a trigger once every
  attack clip has an authored `Attack` event — don't let this become a permanent second vocabulary.
- **Build cadence:** owner chose to build all 5 phases straight through rather than pause for a
  play-test after Pickup (phase 2) and Combat (phase 3) — so unlike `verify-behaviorcommandsplit.md`,
  none of the regression smoke below has been eyes-on verified yet; treat it as first-look, not
  re-confirmation.
