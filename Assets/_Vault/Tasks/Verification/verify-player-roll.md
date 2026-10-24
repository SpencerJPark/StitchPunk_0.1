---
title: Verify — Player Dodge Roll
status: active
created: 2026-10-23
area: player, combat
---

## Goal

Confirm the player's dodge roll feels right and does what [`PlayerRoll_System.md`](PlayerRoll_System.md) says: a committed ease-out burst, invulnerable for the first 80%, a buffered 0.2s cooldown, cancels your own swing, queues an attack on landing, and passes through hostile units that body-block you while walking. Built 2026-10-23. Proven so far by `StitchPunk.Tests.PlayMode.PlayerRollTests` (distance, i-frame window, buffer hold and drop: 4/4) and the placement conformance tests. **Nobody has rolled in the game yet.**

## Before you start

- [ ] **Rebake:** reopen the `CampLoop_Sub` subscene or enter Play. The player prefab gained new baked components.
- [ ] **Know the visual limit:** `PlayerUnit.prefab`'s `Visual` has **no renderer** (its only mesh is the selection ring at scale 0). Squash/stretch, spin and afterimages act on whatever renders under `Visual`, so they show nothing until the player has a body. Drop any placeholder sprite quad under `Visual` to judge them.
- [ ] Sounds `RollWhoosh` / `RollLand` have no `SoundSO` yet, so the roll is silent until you add one to the `SoundLibrarySO`.

## Steps — `Scenes/CampLoop.unity`, Play mode

- [ ] Press **Space** (or gamepad **South**) while walking: the roll goes the way you're pushing, fast and then slowing, about 3 units.
- [ ] Press it standing still: the roll goes the way you face.
- [ ] Roll in all 8 directions; roll into a wall and slide along it rather than stopping dead.
- [ ] Mash roll: a steady roll, pause, roll rhythm, never a stall or a double roll.
- [ ] Press roll just before landing: the next roll comes out right after the short cooldown. Press very early in a roll: no second roll.
- [ ] Walk into a **zombie or bandit**: it blocks you. Walk into **your own minion**: you pass through.
- [ ] Roll through a zombie pack and come out the far side. End a roll inside a zombie: you ease out with no snap.
- [ ] Let a zombie swing land mid-roll: no damage. Roll late so the swing lands in the last fifth: you take the hit.
- [ ] Start a swing and roll before it lands: the swing never hits.
- [ ] Press attack right at the end of a roll: the swing comes out on landing. Press attack early in a roll: nothing.
- [ ] Hold aim and roll: aim drops and the roll isn't steered by aim.
- [ ] Dust puffs at the start (kicking backward) and at the landing.
- [ ] With a placeholder body under `Visual`: wind-up squash, a stretched spin, a springy landing, and three fading ghosts along the path.
- [ ] Console stays clean through all of it.

## Tuning

All numbers are on `PlayerUnit.prefab` → `PlayerControllerAuthoring` → **Roll** (distance, duration, curve, i-frame cutoff, cooldown, buffers, afterimages, spin, pivot height, body-block radius and push speed). Rebake after changing them.
