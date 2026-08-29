---
title: Verify — DOTS Movement Toolkit extraction
status: active
created: 2026-08-29
area: code
---

## Goal

Verify the movement/pathfinding/horde stack extracted cleanly into
`Packages/com.dotsmovementtoolkit` with no behavior change: minions still path, hordes still
form up, the player still walks, stuck units still interrupt, stairs still work, and death/revive
still stop and restart movement correctly. Spec + build deviations:
[Movement_Toolkit_Extraction.md](Movement_Toolkit_Extraction.md).

## Steps

### Compile + bake gate (done during the build, re-check after any further edit)
- [x] Compile clean after each phase (`error CS####` / Burst `BC####`) — verified via
      `read_console` after every phase.
- [x] EditMode tests: `StitchPunk.Tests` + `DotsMovementToolkit.Tests.EditMode`, 61/61 passing
      after Phase 1 and again after Phase 2.
- [x] Play mode entered/exited with zero console errors/warnings (Phase 1 gate, Game.unity;
      Phase 2 gate, TestArea.unity → DOTSTestScene subscene).
- [x] `execute_code` confirmed `NavGridSettings`/`NavGridConfig` singleton exist at
      runtime and a real unit's `Movement.isMoving` reaches `true` under live pathfinding.
- [ ] Re-open / rebake `DOTSTestScene.unity` from a clean editor session (moved components have
      new stable type hashes — confirm no stale-bake artifacts survive a fresh domain reload).

### Play-test (user-driven — needs eyes on the running game)
- [ ] Minions path to move/attack/interact orders (D* Lite individual pathfinding).
- [ ] A horde of 3+ minions forms up and moves together (flow field + `FormationOffsetSystem`).
- [ ] Cycling a horde's formation (`UnitSelectionManager.CycleFormationForSelectedHordes`) visibly
      changes Line/Square/Circle/Blob arrangement.
- [ ] The player character walks normally (`PlayerMoveSystem` — unaffected by the extraction,
      confirm no regression).
- [ ] A unit walking into a dead-end / blocked path fires the stuck interrupt within ~4s
      (`PathStuckCheckSystem` → `MovementStuck` → `MovementStuckBridgeSystem` →
      `ActionInterruptRequest` → the unit re-decides instead of standing frozen forever).
- [ ] Stairs/layer transitions still work if the test scene has any (`StairTransitionSystem` —
      layer support is called out as half-plumbed in the package README's Known Issues, so this
      may already be a known-limited feature, not a regression to chase).
- [ ] **Death stops movement:** kill a unit mid-path — it stops moving and gravity stops
      (`Movement`/`Gravity` disabled), the corpse doesn't slide or float, and the ragdoll takes
      over cleanly.
- [ ] **Revive restarts movement:** revive that corpse — it can walk and fall again
      (`Movement`/`Gravity` re-enabled by `ReviveRequestSystem`).
- [ ] **Pool-reclaim doesn't inherit death:** despawn a dead unit, force a respawn/pool-reclaim
      of the same slot — the new unit moves and falls immediately (`SpawnStateInitSystem`'s
      `Movement`/`Gravity` reset actually fires; this is the specific trap this pass added a
      fix for and has *not* been empirically play-tested, only reasoned through).
- [ ] Order-destination markers still show/hide correctly for player-issued group moves
      (`OrderMarkerSystem` now reads the split-out `HordeOrderMarker` instead of `Horde.markerEntity`).

### Nav grid debug view (added 2026-08-29, never seen running)
- [ ] Set **Debug Display Mode = FullGrid** on the `MovementGridConfig` GameObject's
      `NavGridAuthoring`, enter Play mode: tiles cover the grid footprint, walls read red,
      heavy-terrain cells read amber, open floor reads faint green.
- [ ] The tiles are visible in **both** the Game view and the Scene view, and sit above the
      ground rather than z-fighting with it (raise `debugHeightOffset` if they shimmer).
- [ ] **ObstaclesOnly** shows only walls/heavy cells — the open floor disappears.
- [ ] **Live update:** spawn or destroy a physics obstacle mid-play — the affected cells flash
      cyan and settle into their new colour. If nothing flashes, check whether the obstacle
      actually changed physics `NumBodies` (see Notes below) before blaming the view.
- [ ] **Off** costs nothing: set it back to Off in play, tiles vanish and stay gone.
- [ ] Outside Play mode, selecting the `MovementGridConfig` GameObject draws the footprint +
      lattice gizmo, and it lines up with the actual level geometry (the grid is anchored at
      world origin, not at the GameObject).

## Notes

- Death/revive and pool-reclaim were verified by code inspection (matching the file's existing
  `WithPresent` pattern for `PathRequest` etc.) and a clean compile/test pass, but **not**
  exercised live in Play mode during the build — flag first if anything in the death/revive
  checklist above misbehaves.
- The package's Known Issues (D* Lite replan cost, `NavGridSystem`'s `NumBodies` change-proxy sync,
  flow-field ring-buffer reuse, half-plumbed layer support, per-entity LOS raycasts, several
  dead static helpers) are pre-existing behavior carried over unchanged — see the package
  README, not this checklist, and are explicitly out of scope for this pass.
- The debug view can only be as fresh as the cost map, and `NavGridSystem` rebuilds that only
  when physics `NumBodies` changes. An obstacle that *moves* without a body being added or
  removed will not refresh either the cost map or the view — that's the pre-existing change-proxy
  limitation above, not a debug-view bug.
- `NavGridAuthoring` lives on a `MovementGridConfig` GameObject in `DOTSTestScene.unity`. A
  second consumer scene/subscene needs its own `NavGridAuthoring` instance — there is
  (deliberately) no fallback default if one is missing; the toolkit just idles.
