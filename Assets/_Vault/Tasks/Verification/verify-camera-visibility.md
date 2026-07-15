---
title: Verify Camera Visibility Gating (CameraVisible tag + out-of-view spawning)
status: active
created: 2026-07-13
area: code
---

## Goal

Confirm the camera-visibility system works end-to-end: off-screen character rigs skip their
presentation work (animation sampling, pose/image-index writes, billboarding) and resume seamlessly
when they re-enter view, and `UnitSpawnerSystem` places spawns outside the player's view.

**Architecture recap:** `CameraVisibilitySystem` (`GameManagerSystemGroup`) flips the `CameraVisible`
enableable tag on rig roots and propagates it to `BodyPart` children, using the existing `CameraView`
singleton (center + viewRadius, written by `AudioManager` each LateUpdate — `viewRadius` is now
computed dynamically from the camera's ground-projected frustum corners). Presentation jobs
chunk-filter on the tag; `AnimationTimeSystem` deliberately stays ungated so clip timers keep
advancing off screen (no pose snap on re-entry). ⚠ **PRESENTATION ONLY** — simulation systems must
never gate on `CameraVisible` (rule in [[RULES]]).

## Setup guide (one-time)

There are almost no new editor assets — the system rides existing scene objects:

- [ ] **AudioManager must be in the scene** (it already is for sound — it writes `CameraView`).
      Two inspector fields matter now:
      - `cameraViewRadius` (default 25) — the **minimum/fallback** radius, used when no frustum
        corner ray hits the ground (camera looking up/parallel).
      - `maxCameraViewRadius` (new, default 120) — ceiling on the computed radius so near-horizontal
        corner rays can't declare the whole map "on screen". If the map cam should cull nothing,
        raise this above the map's diagonal.
      Without an AudioManager, `CameraView` keeps its baked default (center 0, radius 25) — units
      near the origin stay visible, everything else freezes. That's the expected failure mode.
- [ ] **Re-bake** — three bakers changed (`CharacterRigAuthoring`, `BodyPartAuthoring`,
      `ImageIndexAuthoring` now add `CameraVisible`). Re-open the subscene or re-enter Play mode.
- [ ] Tuning knobs, if the defaults feel wrong:
      - Hysteresis paddings: `ENABLE_PADDING` (5) / `DISABLE_PADDING` (10) consts in
        `Systems/GameManagerSystemGroup/CameraVisibilitySystem.cs`.
      - Spawn padding: `SPAWN_VIEW_PADDING` (10) / `SPAWN_POSITION_ATTEMPTS` (8) consts in
        `Systems/SpawnSystemGroup/UnitSpawnerSystem.cs`.

## Steps

### Compile + import (first)
- [ ] Focus Unity; confirm **no compile errors** (`error CS` / Burst `BC`) in the Console.
- [ ] Systems window: `CameraVisibilitySystem` sits inside `GameManagerSystemGroup`
      (SimulationSystemGroup, OrderFirst).
- [ ] EditMode conformance tests still pass (Window ▸ General ▸ Test Runner) — new system file lives
      in the folder matching its group, so `SystemPlacementConformanceTests` should be green.

### Tag plumbing (Entities inspector, Play mode in `DOTSTestScene`)
- [ ] Pick a unit near the camera: root entity has `CameraVisible` **enabled**; each `BodyPart`
      child (quads, joints, sockets) also enabled.
- [ ] Move the camera away (or the unit away) past viewRadius + 10 → root **and all parts** flip to
      disabled. Move back within viewRadius + 5 → all flip enabled.
- [ ] Flicker check: park a unit right at the screen edge and nudge the camera — the tag must NOT
      oscillate every frame (the 5/10 hysteresis band absorbs it).
- [ ] Prefab safety: after spawning units from a spawner, select the source **prefab** entities in
      the Entities hierarchy (Prefab-tagged) — their `CameraVisible` must still be **enabled**.
      (The propagation job guards against writing through stale spawn-frame `BodyPart` refs.)

### Presentation gating
- [ ] Off-screen unit: quads freeze in their last pose (select a part → `LocalTransform` stops
      changing), no `_ImageIndex` material pushes. The unit's `AnimationLayer` **time keeps
      advancing** (root entity inspector) — that's `AnimationTimeSystem` staying ungated, on purpose.
- [ ] Pan the camera back to it quickly → the unit is mid-cycle at the correct pose on the first
      visible frame (no T-pose/snap/stale frame). Walk cycles of units that walked off screen and
      back should look continuous.
- [ ] Simulation unaffected: an off-screen unit still paths, fights, and loses health (watch its
      components in the inspector, or give it a move order off screen and confirm it arrives).
- [ ] Design change off screen: zombify/re-skin a unit while off screen → correct look the frame it
      re-enters view (DesignApplyUtil writes `restPose.baseImageIndex`, so sampling re-derives it).
- [ ] Kill a unit on screen → billboard + ragdoll behave as before (billboard freezes yaw when dead
      — unchanged behavior, now also skipped entirely while off screen).

### Camera coverage (CameraView.viewRadius is dynamic now)
- [ ] Zoom out / switch to the map cam → `CameraView.viewRadius` grows (inspect the singleton),
      clamped at `maxCameraViewRadius`; more units stay animated at wider zooms.
- [ ] WorldMood sanity: combat music still triggers when an attack is visible on screen
      (WorldMoodSystem reads the same singleton — its "camera sees" test just got more accurate).

### Out-of-view spawning
- [ ] Place a `UnitSpawner` whose range straddles the screen edge, trigger it → units appear only in
      the off-screen part of the ring (repeat a few times; with 8 attempts a rare on-screen roll is
      possible only when almost the whole ring is visible).
- [ ] Fully-on-screen spawner (e.g. small range at screen center) → units still spawn (gameplay
      wins; the re-roll gives up after 8 attempts rather than blocking).

### Regression: Animation Editor preview
- [ ] Open `AnimationEditorScene`, play a clip preview → parts still animate. (No AudioManager
      there, so `CameraView` stays center 0 / radius 25 — the preview rig must sit near the origin.
      If a preview rig ever freezes, that radius is why.)

## Notes

New files:
- `Assets/_Scripts/Components/Units/CameraVisibilityComponents.cs` — `CameraVisible` enableable tag.
- `Assets/_Scripts/Systems/GameManagerSystemGroup/CameraVisibilitySystem.cs` — the flip/propagate job.

Edits:
- Bakers: `CharacterRigAuthoring` (root), `BodyPartAuthoring` (parts), `ImageIndexAuthoring`
  (standalone quads — never flipped, tag just keeps them matching the gated queries).
- Gated jobs: `SampleLayeredAnimationJob` (root), `ApplyPoseJob` + `ApplyAnimatedImageIndexJob`
  (parts), `UpdateImageIndexJob` (parts) — all via `[WithAll(typeof(CameraVisible))]`;
  `BillboardJob` gates via a read-only lookup on its `parentEntity` (billboard quads aren't
  `BodyPart` entries).
- `UnitSpawnerSystem` — `RandomSpawnPosition` re-roll against `CameraView` (+`SPAWN_VIEW_PADDING`),
  optional via `TryGetSingleton` so scenes without a camera bridge spawn anywhere.
- `AudioManager` — `ComputeGroundViewRadius` (4 viewport-corner rays vs y=0 plane, clamped),
  new `maxCameraViewRadius` field.
- Docs: RULES.md (presentation-only hard rule), Systems.md, Systems_Animation.md, Components.md,
  Contracts.md (new "Read-only view/state singletons" table).

Gotchas to watch:
- `CameraView` is written in LateUpdate and consumed at the top of the next frame — always one
  frame stale. The hysteresis paddings absorb it; if fast camera snaps show a 1-frame freeze at the
  edge, raise `ENABLE_PADDING`.
- Spawn-frame `BodyPart` buffers hold **prefab** entity refs until `BodyPartInitSystem` rebuilds
  them. The job never writes through Prefab-tagged refs and self-heals part/root drift the next
  frame — so a unit spawned off screen may run presentation for 1–2 frames before going dormant.
  Harmless (it's off screen), just don't "fix" the drift check away.
- If some rig ever animates while clearly off screen: check its parts actually got `CameraVisible`
  (rebake), and that the root has a `BodyPart` buffer + `LocalTransform` (the system query).
- If a NEW presentation system is added later, gate it with `[WithAll(typeof(CameraVisible))]`;
  never gate simulation systems (see [[RULES]]).

When everything passes: move this file to `Assets/_Vault/Tasks/Done/`.
