---
title: Verify Save and Title
status: awaiting-playtest
created: 2026-11-13
area: Save, UI, Boot
---

# Verify: Save and Title

Spec: [SaveAndTitle_System.md](SaveAndTitle_System.md). The title flow is the `TitleFlow` prefab, and the pause menu, save indicator and quicksave keys are the `GameMenus` prefab. Both are placed in `Game.unity` and `CampLoop.unity`. `Game.unity` has no world content yet, so run every check in `CampLoop.unity`. Items marked *(Claude ✓)* were already checked over MCP on 2026-11-17/18; re-check them by eye.

## Goal

A save restores the exact moment: a unit that is mid-swing is still mid-swing after a load. If the component layout changed, the save falls back to the progress layer. The title freezes the world behind it, and Continue is near-instant.

## Moment layer (CampLoop.unity)

- [ ] Turn on `StitchPunk ▸ Skip Title in Editor`. Play CampLoop and start a fight.
- [ ] Press F5: the corner gear spins, then "Saved" appears. The Console shows `[PersistentSaveSystem] Moment capture N ms`. This is the hitch; record it.
- [ ] *(Claude ✓ pose/clock only, no fight)* Wait 5 s and press F9. The Console shows `[MomentSnapshot] Committed moment`, the same swings land at the same moment and projectiles are still in flight.
- [ ] After the load, physics still runs at normal speed: no frozen ragdolls and no fast-forward burst. This tests the fixed-step clock realignment.
- [ ] Walk 2+ tiles away and back: loaded units are not unloaded with their tile, because they belong to no section by design.
- [ ] Add a field to a saved component (for example `Health`), recompile, then load the old quicksave. The Console shows `Component layout changed: …`, the player position and resources are kept, and units come back through the minion path. The load list shows "Older version: units reset" on that save. Revert the field afterwards.

## Title (Game.unity, or CampLoop with the TitleFlow prefab)

- [ ] *(Claude ✓)* Turn Skip off and press Play: the title appears within 2 s and nothing in the world moves for 30 s (`[SimulationHold] Holding the simulation`).
- [ ] Press any button: Main shows Continue with the thumbnail, location, play time and date. The spinner clears after `[BootFlow] preload N ms`.
- [ ] *(Claude ✓ 10 ms)* Continue: `[BootFlow] continue->control N ms` is under 500 ms, and play lands on the newest save.
- [ ] New Game with saves present asks for confirmation first, and the saves are kept.
- [ ] Load Game ▸ Delete asks for confirmation. Six autosaves leave five folders under `%USERPROFILE%/AppData/LocalLow/<company>/<product>/saves/`.
- [ ] Pause ▸ Save ▸ New save, then Pause ▸ Return to Title (the warning appears only if the last save is over 60 s old), then Continue. It is still instant.
- [ ] Walk the whole menu with only a pad: the first button has focus on open, and B always goes up one screen.
- [ ] Try saving during a cutscene: you get the toast "Can't save during a cutscene".
- [ ] With the title up, the gameplay HUDs (resources, workstation panel, HP bar) are hidden; they come back after Continue or New Game.
- [ ] Pause ▸ Options opens Settings, and only one pause menu is ever on screen.
- [ ] After a load, the camera follows the player, mouse aim works, and selection/interaction highlights work: these used to hold the pre-load player entity.
- [ ] **Duplicate-player risk:** the player is authored in the CampLoop subscene. After a load, force that subscene to unload and reload (or walk far enough that it streams out and back): there must still be exactly one player.
