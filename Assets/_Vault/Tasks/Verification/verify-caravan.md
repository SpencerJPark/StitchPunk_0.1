---
title: Verify — Caravan (drive, park, crash, caravan ledger, summon exits, chimney)
status: active
created: 2026-10-26
area: code
---

## Goal

Drive the caravan around, park it into camp, run some bandits over, and watch a zombie walk out and a brute burst out in smoke. Spec and as-built notes: [`Caravan_System.md`](Caravan_System.md) (§12 lists where the build departs from the plan).

What was checked when it was built:
- **Compile:** clean.
- **Tests:** EditMode 12/12, including the new `CaravanDriveMathTests`. All three of its tests were mutated and watched failing. PlayMode 30/30 across the eight ledger/workstation fixtures the ledger move touched.
- **Bake:** checked by baking `CaravanSim.prefab` into a throwaway world. Every caravan component is present, plus a kinematic physics body.
- **Not yet run:** Play mode. Every on-screen item below is unverified.

## Before you start

- [ ] **Restart the Editor once.** The console shows Burst internal errors ("Could not find type … in `AssemblyNameReferenceAndMetadata`"). That's the stale Burst JIT cache from Gotchas.md, not the new code. Until a restart the affected systems run managed, which works but is slower.
- [ ] Open **`Assets/Scenes/CampLoop.unity`** and re-enter Play (the subscene gained `CaravanSim`, and the unit library gained `caravanExitStyle`, so it needs a rebake).
- [ ] The placeholder workstation is now redundant in this scene: the caravan links on the first frame, so B logs "already deployed". To test the hut, use a scene without `CaravanSim`.

## Play checks

### Phase 0–1: drive and park
- [ ] On the first frame the caravan's back doors swing open (parked = camp).
- [ ] Walk to the **back** of the caravan (behind the doors) and press E: the workstation opens (the menu, as before).
- [ ] Walk up to **either side or the front**, even right by the horse, and press E: the doors close, you sit on the driver's seat, and the map switches to Vehicle.
- [ ] It should feel like your old `VehicleController` (CV-D17): push a direction and the caravan **heads that way on the map** (world axes, like walking), pulling away slowly (about 6 s to full speed) and settling faster when you let go (about 2 s). There's no reverse: pull back and it swings round.
- [ ] Turns are lazy and wide (the heading eases round), and the body swings after it. You can still creep round from a standstill, just slower.
- [ ] The wheels roll with speed, the front axle points where it's heading, and the body bounces with speed and leans into turns.
- [ ] Tune the feel on **`ScriptableObjects/Vehicles/Caravan.asset`** (the VehicleProfileSO: driving, crash, ride, and the per-direction driver seat and horse offsets). Changes need a rebake (re-enter Play).
- [ ] As the caravan turns, the horse shifts between its eight per-direction positions (the old `HorseCaravanOffset` values).
- [ ] Drive into a wall or building: it stops dead and doesn't clip through. Back away freely.
- [ ] Drive over uneven ground: the caravan stays on the ground.
- [ ] Press E at speed: refused ("Slow down to park the caravan." in the console). Stop and press E: you're put down beside the driver's side (front left), the doors open, and you're back on foot.
- [ ] Queue a summon, then try to board: refused ("Finish summoning before driving off.").

### Phase 2: the caravan holds the stock
- [ ] The HUD numbers don't change when the caravan links on the first frame.
- [ ] Gather wood: the deposit at the back of the caravan raises the HUD.
- [ ] A field revive still spends a round, even far from the caravan (CV-Q8).
- [ ] Save, change the stock, load: the stock comes back.
- [ ] Pack Up in the station menu does nothing but log "Caravan: drive off to pack up camp".

### Phase 3: crashing
- [ ] Drive slowly (under 2 m/s) into bandits: they're pushed aside, with no damage.
- [ ] Drive at full speed through bandits or citizens: each hit damages them, and the caravan visibly slows per hit. Its health ticks down in the station menu.
- [ ] At full speed every unit you hit is **flung on an arc**, topples over sideways on screen with its arms and legs flailing, lands flat, lies there for 1–2 s and **stands back up where it landed** (fake ragdoll, CV-D15). Under 2 m/s they're only pushed aside.
- [ ] Your own minions get the same fling and get-up, and their health stays full.
- [ ] A kill (by the caravan or a sword) flings the corpse, which **stays down**. Reviving it stands it back up.
- [ ] A unit hit dead centre ends up clear of the caravan, not stuck inside it. If it does get stuck, raise `shoveDistance` on `CaravanSim`.

### Broken caravan (CV-D13/CV-D14)
- [ ] Use the debug menu (or crash a lot) to take the caravan to 0 HP while driving: you're thrown off, the console says it's broken, the body slumps and smokes black.
- [ ] Try to board: refused. Queue a summon: nothing is spent and nothing builds. The furnace and crafting still work.
- [ ] Repair at the work table (wood): the smoke stops, it drives again, and summoning resumes.
- [ ] Go down yourself while it's working: knocked out, then you wake at the caravan. Go down while it's broken: game over.

### Phase 4: summon exits
- [ ] Summon a **Zombie** or **Runner**: it appears inside, walks to the back edge, hops down in an arc, then walks to the rally formation.
- [ ] Summon a **Brute**: the doors swing shut, the caravan shakes for about a second, the doors slam open past fully open and bounce back, a smoke cloud bursts out of the back, and the brute is standing in the smoke when it clears.
- [ ] Queue two summons: the second doesn't start building until the first one's exit has finished.

### Phase 5: chimney
- [ ] With the furnace burning and wood > 0, the chimney smokes, both parked and while driving.
- [ ] Toggle the furnace off, or run out of wood: the smoke stops (the last puffs drift away).

## Known gaps (not bugs)
- The horse doesn't animate: it's a Rive panel (spec CV-O2).
- There's no hop-down clip yet: `CaravanHopDown` is listed in `Spencer/Animations_Needed.md`. The knock-down is the code-driven fake ragdoll until units get real ragdoll rigs. Units without a rig (none today) still get the old stun, which only pins movement.
