---
title: Verify — Camp Loop Phases 1–2 (resources, workstation, summoning)
status: active
created: 2026-10-13
area: code
---

## Goal

Play the first half of the loop: **post up → raise**. Deploy a workstation, mount it into Control Units mode, burn wood, craft revival rounds, field-revive a citizen, summon all three unit types, repair, pack up, and lose. Spec: [`../Plans/CampLoop_System.md`](../Plans/CampLoop_System.md). It stays in `Plans/` because Phases 3–7 are still open.

Built 2026-10-13 unattended:
- **Compile:** clean.
- **PlayMode:** 43/43, of which 24 tests are new across seven fixtures.
- **Revert-to-fail:** every new fixture was proven able to fail, with Burst switched off.
- **Not yet run:** Play mode itself. Every on-screen item below is unverified.

## Before you start

- [ ] Open **`Assets/Scenes/CampLoop.unity`**. It is a copy of TestArea pointed at the new subscene `SubScenes/CampLoop_Sub.unity`, which is DOTSTestScene minus the test spheres.
- [ ] **Rebake:** the player archetype, `UnitDataBlob` and the unit library all changed. Reopen the subscene once, or just enter Play.
- [ ] `PlayerUnit.prefab` now references `Prefabs/Buildings/Workstation.prefab`. That prefab is a placeholder cube with a furnace cylinder, and is yours to dress.

## Play checks

**Resources (Phase 1)**
- [ ] The top-left HUD reads `Bodies 2 · Wood 10 · Scrap 5 · Electricity 5 · Rounds 3 · Currency 0`.
- [ ] The DebugResourceMenu (OnGUI, on `CampLoopUI`): +10 / −10 changes the HUD, and −10 floors at 0. "Set cap 20" then +10 a few times clamps at 20.
- [ ] Field revive: kill a citizen, select item slot 1 (the reviver), target the corpse and use it. The zombie rises and Rounds drops by 1.
- [ ] Set Rounds to 0 with the menu and revive again. Nothing rises, the Rounds label flashes red, and the corpse is still targetable.
- [ ] A dead *zombie* cannot be field-revived, and costs no round (humans only, CL-D4).
- [ ] Save, change counts, load: the counts come back.

**Workstation (Phase 2)**
- [ ] On foot, Tab no longer enters Control Units. The console logs "Mount your workstation (E)…".
- [ ] Press **B**: a translucent ghost appears 3 m ahead, green on flat ground and red on slopes or within 6 m of another station. Press B again to cancel.
- [ ] B, then **E** on green: the workstation appears. A second B is refused while one is deployed.
- [ ] Walk up and press **E**: you snap to the station's front, and the camera and controls switch to Control Units. The right-side Workstation menu appears.
- [ ] **Furnace ON:** about every 5 s, Wood −1 and Electricity +2. Toggling it OFF stops it.
- [ ] **Craft revival round:** Scrap −1, Electricity −1, Rounds +1. With no scrap, Scrap flashes red.
- [ ] **Summon** basic, Brute and Runner:
  - [ ] The cost leaves the HUD immediately, including 1 Body.
  - [ ] The progress bar fills.
  - [ ] The unit appears at the station's front.
  - [ ] It is selectable and obeys move and attack orders like a revived zombie.
  - [ ] The Brute is slow and tanky; the Runner is fast and fragile.
- [ ] Summoning with 0 Bodies does nothing, and Bodies flashes.
- [ ] **Tab** (or the Dismount button) returns you to the character.
- [ ] **Pack Up** with a summon queued is refused, with a console line. With an empty queue the station vanishes, you're dismounted and the HUD counts are unchanged. You can then redeploy elsewhere.
- [ ] **Lose:** "damage station" is not in the debug menu yet, so set the station's `Health.healthAmount` to 0 in the Entities inspector. "Your workstation was destroyed" appears, and Restart reloads.
- [ ] **Repair** after some damage: Wood −2, station HP +25. At full HP nothing is spent.

**Follow-up fixes (2026-10-13, after your first look)**
- [ ] With the **custom cursor**, hovering a station-menu button tints it, and clicking presses it. Clicking anywhere on the panel does *not* send selected minions a move order. Right-click-drag starting on the panel does not start a box select.
- [ ] Summons now **spawn**: `PlayerZombie.asset` had no prefab. All three zombie types now use MaleCitizen prefab variants (`Prefabs/Units/PlayerZombie*.prefab`), and look like citizens until they get their own art.
- [ ] **Billboarding:** citizens and zombies stay visible while walking in every direction (NewRig now has an actor-root billboard at your package default, Screen Aligned). The player stays visible too, via a small game-side `VisualBillboardSystem` on the player's `Visual` child, since the player has no rig.
- [ ] The console is free of `Can't call HasSingleton<CampLoopGameOver>` (fixed).
- [ ] **Formation cycling moved off Tab** to **C** (gamepad: left-stick click), as a `ControlUnits/CycleFormation` input action. Tab in Control Units now only dismounts.

## Feel questions (your call; answers feed Phase 3)

- [ ] Is B a good deploy key? Is E-to-confirm placement fine, or should it be click-to-place?
- [ ] Do the starting numbers feel right (5 summons' worth of scrap, 3 rounds)?
- [ ] Phase 3 ← DECISION: **number keys** are both quick commands (Control Units) and item slots (on foot). Recommendation: Ctrl+1/2 to assign Red/Blue, plain number for the quick command.
- [ ] Phase 3 ← DECISION: Control Units has **Select on right mouse button and Command on left**, the reverse of most RTS games. Swap them?

## Known gaps (by design or deferred)

- The player has no `Health`, so dismount-on-damage (CL-D2) is coded and tested but cannot fire in game until the player gets health.
- Raids are Phase 6, so nothing damages the station yet. Use the inspector to test losing.
- Workstation state is not saved. After a load the station is gone but the ledger survives, so you can redeploy.
- Summoned minions do not join a squad yet (Phase 3).
