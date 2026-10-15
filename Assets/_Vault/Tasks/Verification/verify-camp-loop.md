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
- [ ] **Billboarding:** citizens, all three zombie types and the player stay visible while walking in every direction. A game-side `VisualBillboard` sits on each prefab's `Visual` child, using your package's screen-aligned math. The package's rig billboard can't apply yet: `MaleCitizen.profile.asset` and its rig were deleted in `c07dfd75`, so units bake no toolkit actor data. Switch over once units have a profile again.
- [ ] The console is free of `Can't call HasSingleton<CampLoopGameOver>` (fixed).
- [ ] **Formation cycling moved off Tab** to **C** (gamepad: left-stick click), as a `ControlUnits/CycleFormation` input action. Tab in Control Units now only dismounts.

- [ ] **Formations:** a ground move order spreads the selection into a rounded formation about 1.2 m apart. **Arrival order fills it front-to-back:** units expected to arrive first (by distance and speed) take the front of the circle in the direction of travel, and stragglers take the back, so nobody walks through units already standing. Within a row, units pair left-to-right. Summons walk to the station's `RallyPoint` (5 m in front, movable on the prefab) and fill the far side first, then back toward the station. Units stop within 0.4 m of their slot (Wander's stop distance went from 1.5 m to 0.4 m, which also affects citizens).

**Phase 3 — squads (built 2026-10-13, CL-D16–D19)**
- [ ] The squad bar at the top centre shows **Red** and **Blue** with member counts; the active squad is highlighted.
- [ ] Control Units mouse: **left click selects / box-selects, right click commands**.
- [ ] Select units and press **1** or **2**: they join Red or Blue and the counts update. **3** and **4** are locked and log a line.
- [ ] With nothing selected, press **1** or **2**: that squad becomes active and its units are selected. **Ctrl+1/2** does the same even with units selected.
- [ ] **Space** (mounted) and **Tab** (on foot) cycle Red → Blue → Red, selecting the squad each time.
- [ ] Hold **Q** or **middle mouse**: a radial opens. Move the mouse up/right/down/left to Follow / Attack my target / Return / Hold, then release to issue. The console logs `[Squad] <command> -> N members of squad K`.
- [ ] **On foot**, the radial shows a cyan ring of about 15 m; only members inside it obey. **Mounted**, the whole squad obeys.
- [ ] Your aim doesn't spin while the radial is open on foot, and comes back after.
- [ ] New summons join the **active** squad. Field-revived zombies start in no squad (press a number to assign them).
- [ ] Number keys on foot are still item slots and don't touch squads.

**Standing orders and the spawn rally (2026-10-13, CL-D21/D22)**
- [ ] Summons now **grow outward from the spawn point**: the first stops just outside it, and each later one takes the next free slot further out. Later ones may walk past earlier ones, which is fine per you.
- [ ] **Follow** (radial up, or F + right-click) **stays on** until another order. The squad keeps its own slots around you as you walk, and re-paths when you move more than ~1 m. Any move, attack, interact, stop or return order cancels it.
- [ ] While following, an enemy coming **within ~8 m of you** is attacked; the squad goes back to following once it's dead or has left the area (beyond ~13 m). Your character has no health yet, so "you were attacked" can't be detected; enemies getting close is the trigger.
- [ ] **Hold** (radial left, or Shift + right-click on ground) also persists. Units stand at their spot (spread around the click for Shift) and attack enemies within 5 m, then walk back. Hold was previously a no-op: nothing consumed its order.

**Phase 4 — gathering (built 2026-10-13, CL-D23–D26)**
- [ ] `CampLoop_Sub` has **5 trees** (east of the start, 10 wood each) and **3 scrap piles** (west, 8 scrap each). Prefabs are in `Prefabs/ResourceNodes/`, placeholder shapes for you to dress.
- [ ] Deploy the workstation first; without it, a gather order logs "Deploy your workstation first".
- [ ] Select units and **right-click a tree**: they walk over, harvest for ~3 s, carry 1 wood to the station's front, and Wood +1 in the HUD. They repeat until the tree is gone (it disappears at 0), then move to the nearest tree within 15 m.
- [ ] Same with a **scrap pile**, giving Scrap.
- [ ] **Corpses:** right-click a dead citizen. One unit carries it back for **Bodies +1**, and the corpse vanishes. Your own dead minions can't be hauled.
- [ ] Radial **Gather** wedge (five wedges now; Gather sits lower-right): sends the active squad to the nearest node or corpse within 20 m of the cursor (mounted) or of you (on foot).
- [ ] Any new order, or Follow/Hold, stops gathering. Gatherers walk through each other (expected).

**Phase 5: bandit camp (built 2026-10-14/15, CL-D27–D30)**
- [ ] **BanditCamp**, about 30 m north of the start: two tents, a campfire, 5 Bandits (citizen body, ~100 HP, melee), a scrap crate (10) and a strongbox (5 currency).
- [ ] Bandits mill around within ~10 m. Walk your character or minions within ~12 m and they attack. Lead them away: past ~25 m from camp they break off and walk home.
- [ ] Your zombies now treat Bandits (and Rottens) as hostile. Self-defence, Follow's "defend me" and Hold all engage them.
- [ ] Before the camp is cleared, the crate and strongbox can't be gathered (right-click does nothing). After the last bandit dies, the console logs `[BanditCamp] Camp cleared — loot unlocked.` and they become haulable: Scrap +10 and Currency +5 over several trips.
- [ ] Dead bandits can be field-revived (humans → PlayerZombie) or hauled for Bodies.
- [ ] **Your health:** an HP bar sits bottom-left. Bandits can hurt you. At 0 HP, a "Knocked out — waking at the workstation… N" overlay counts down 5 s, then you're at the station with full HP. With no station deployed you get the lose screen.
- [ ] Taking a hit while mounted knocks you off the station.

## Feel questions (your call; answers feed Phase 3)

- [ ] Is B a good deploy key? Is E-to-confirm placement fine, or should it be click-to-place?
- [ ] Do the starting numbers feel right (5 summons' worth of scrap, 3 rounds)?
- [x] Number keys: answered (CL-D16, D18): a number assigns the selection to a squad; quick commands moved to a radial.
- [x] Mouse buttons: answered (CL-D19), swapped.

## Known gaps (by design or deferred)

- ~~The player has no `Health`~~ Wrong: `PlayerUnit` always had `Health` through its `BaseUnit` parent prefab (found 2026-10-15). Dismount-on-damage has been live all along, and knockout (Phase 5) now handles 0 HP.
- Raids are Phase 6, so nothing damages the station yet. Use the inspector to test losing.
- Workstation state is not saved. After a load the station is gone but the ledger survives, so you can redeploy.
- Summoned minions do not join a squad yet (Phase 3).
