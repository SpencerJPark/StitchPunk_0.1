---
title: Verify — Ragdoll2D Rework (3D flight + plane-space flail)
status: active
created: 2026-07-09
area: code
---

## Goal

Verify the ragdoll rework end-to-end: corpses launch in real 3D from the kill direction, land on
raycast ground (ledges/props), bounce off walls with restitution, flail in flight, settle into the
same authored zone poses as before, sleep when quiet, stack in piles, and revive cleanly.
Spec + deviations: [Ragdoll2D_System.md](Ragdoll2D_System.md).

## Steps

### Compile + bake gate
- [ ] Focus Unity → clean Console: no `error CS####` / Burst `BC####` (13 edited + 5 new scripts; new folder `Systems/RagdollSystemGroup/`).
- [ ] First import: no duplicate-GUID warnings (5 hand-generated `.meta` files: `RagdollSystemGroup` folder, `RagdollProfileSO.cs`, `RagdollConfigSO.cs`, `RagdollSimConfigAuthoring.cs`, `CorpseCellSystem.cs`).
- [ ] Run EditMode tests (Window ▸ General ▸ Test Runner): `SystemGroupOrderTests` (RagdollSystemGroup added to the LateSim pipeline) + `SystemPlacementConformanceTests` (Ragdoll2DSystem exemption removed) pass.
- [ ] Re-open / rebake the subscene (PartLibrary blob gained `ragdollSegmentLength`/`ragdollWeight`; AttackLibrary blob gained flail/spin/restitution).

### Editor asset + scene wiring (optional for first play — systems have built-in defaults)
- [ ] Create `_RagdollConfig` (`Create ▸ Units ▸ Ragdoll Config`) and add a `RagdollSimConfigAuthoring` GameObject to the game subscene pointing at it.
- [ ] Create a starter profile, e.g. `RagdollProfile_Explosive` (`Create ▸ Units ▸ Ragdoll Profile`: launchForceY ~10, launchForceX ~8, spin ~360, flail ~1.5, restitution ~0.5), and assign it to one test attack's `ragdollProfile`.

### Phase 1 — 3D trajectory, landing, bounce (Play DOTSTestScene)
- [ ] Kill a unit with a launch-configured attack: corpse arcs away from the attacker's actual position (including along Z — hit from screen depth throws depth-ward, articulation stays in the XY plane).
- [ ] Kill a unit near a ledge with sideways launch: corpse falls past the edge and lands on the LOWER ground (old behavior: froze at death-height `groundY`).
- [ ] Kill a unit into a wall/structure: visible bounce-off (restitution), no pass-through.
- [ ] Entities window: `Ragdoll2DLaunch.velocity` is a shrinking float3 while airborne; `airborne` flips to 0 on rest; `sleeping` flips to 1 shortly after everything settles.
- [ ] No-launch attack (launch forces 0): corpse tips over in place exactly like the old system.

### Phase 2 — flail + settle
- [ ] Limbs visibly trail/whip during flight and kick on landing (`Ragdoll2DJoint.angularVelocity` nonzero in flight).
- [ ] After settling, the at-rest pose matches the pre-rework look (authored landing zones — side-by-side feel check).
- [ ] `flailIntensity`/`spin` on the test profile visibly change the tumble (spin ≥ 360 should read as a flip that settles WITHOUT unwinding).

### Phase 3 — profile flattening
- [ ] Two attacks sharing one `RagdollProfileSO` change together after editing the profile + rebake.
- [ ] An attack with `ragdollProfile = None` still ragdolls from its inline fields.

### Phase 4 — corpse stacking + revive
- [ ] Kill 5+ units on one spot: later corpses rest visibly higher (0.15/corpse, cap 5).
- [ ] Revive a corpse mid-flight AND a settled one: both restore pose/rotations cleanly and the unit behaves normally (also confirms the stack hash forgets revived corpses next frame).
- [ ] Pool-reclaim check: kill a unit, let it despawn/pool, respawn — new instance is not pre-ragdolled (spawn-init resets launch flags).

### Scale + perf
- [ ] AoE-kill a horde (~50): no frame spike; profiler shows `RagdollDriveJob` off the main thread; settled corpses drop out of the cost (sleeping short-circuit).

## Notes

- **Save compatibility:** `Health` (IPersist) changed layout (`killSourceX` → `float3 killSourcePosition` + 3 new floats) — old save files with unit health snapshots may not round-trip; a fresh save is expected after this build.
- The flail's "world angle" approximation ignores per-character Y-flip facing; any wrong-direction flail reads as noise and is corrected by the authored settle — tune `flailDamping`/`landingImpulseScale` before judging.
- Landing/bounce SFX are intentionally NOT wired — `RagdollSystemGroup` runs before `SoundSystemGroup` precisely so they can be added same-frame later.
- Corpses resting ON props uses the OBJECTS layer in the ground ray — needs a prop with a collider on layer 10 to observe.
