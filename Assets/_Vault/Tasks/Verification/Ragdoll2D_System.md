# Ragdoll2D Rework — Design Spec

> **Status:** 🔨 built (2026-07-09) — all 4 phases coded; awaiting Editor compile + rebake + play verification (see `verify-ragdoll2d.md`).
> **Raw source:** [`../../Spencer/Ragdoll2D_Rework_Prompt.md`](../../Spencer/Ragdoll2D_Rework_Prompt.md) + the Fable design pass (2026-07-09). Direction chosen: **Option B — procedural 2D ragdoll on a real 3D trajectory** (full physics ragdoll rejected: 50–200 simultaneous corpses, revive reversibility, authored-pose control).

---

**Skills Needed** (project skills in `.claude/skills/` — see [Skills index](../../Memories/Code/Skills.md)):
- `dots-feature-group` — declare `RagdollSystemGroup` in the LateSim pipeline + conformance-test rows (§2, §5)
- `dots-system-scaffold` — reworked `Ragdoll2DSystem` driver + `CorpseCellSystem` (§5)
- `dots-authoring-baker` — `RagdollSimConfigAuthoring` (§4)

*(No `dots-blob-library` — this extends the existing `AttackLibrary` and `PartLibrary` blobs rather than adding a new one.)*

---

## 1. Purpose & v1 scope

Rework the fake ragdoll into a **three-stage procedural ragdoll**: (1) the corpse root flies a real **3D ballistic trajectory** with `CollisionWorld` raycast landing and wall bounces; (2) limbs **flail in the world-XY plane** via a tiny verlet solver (particles inherit root motion, take a landing impulse, collide with a floor line); (3) the flail **hands off to the existing authored settle** (landing zones + `settleSpeed` lerp), so the at-rest look is unchanged. Articulation never leaves the XY plane — depth (Z) throws are pure translation, matching the stacked-quad rig.

**Sacred behaviours preserved:** fall away from the killer (`fallSideSign`), and per-attack authored ragdoll response (now a full profile, not just 3 floats).

**v1 handles:**
- Launch in any 3D direction from the actual kill-source position (melee, thrown items, hazards, AOE).
- Terrain-aware landing (ledges, props) via raycast — no more frozen `groundY`.
- Mid-flight wall/structure **bounce with per-attack restitution** (locked in Q&A — no stop-and-drop interim).
- In-flight limb flail + landing reaction, settling into the authored zone poses.
- Per-attack `RagdollProfileSO` flattened into `AttackBlob` at bake.
- Corpse stacking via a corpse-cell spatial hash (Phase 4 — explicitly cuttable).
- Full revive reversibility (necromancy is the game; `Ragdoll2DReviveSystem` contract holds).

**Out of v1:** per-limb physics bodies / `PhysicsJoint` of any kind; corpses colliding with *living* units; ragdoll plane re-orientation for depth throws (fixed world XY, per Q&A); corpse colliders on a dedicated layer (reserved as the stacking upgrade path if the hash approximation reads too fake).

## 2. Architecture

Pure ECS, no MonoBehaviour bridge (the commented-out `RagDollManager.cs` is deleted). Init/revive stay in `HealthSystemGroup`; the per-frame driver stays **late** — after all gameplay transform writes — but gets a **declared home**: a new `RagdollSystemGroup : GameSceneSystemGroup` in `SystemGroups.cs`, slotted `SpawnInit → Ragdoll → Sound` in `LateSimulationSystemGroup` (before Sound so landing thuds can emit `SoundRequest`s same-frame later). `Ragdoll2DSystem.cs` moves to `Systems/RagdollSystemGroup/` per conformance rule 2; `SystemGroupOrderTests` + `SystemPlacementConformanceTests` get the new rows.

```
Death: DamageEventSystem captures kill* onto Health (CombatReactionSystemGroup)
        │
Ragdoll2DInitSystem (HealthSystemGroup, after DeathSystem)
  fallSideSign + 3D launch velocity from killSourcePosition · enables components,
  seeds RagdollParticle buffer, picks zone targets from PartLibrary
        │
Ragdoll2DSystem (RagdollSystemGroup — LateSim: SpawnInit → Ragdoll → Sound)
  ① FLIGHT  root: integrate float3 velocity, ground raycast, wall bounce  (IJobEntity, ScheduleParallel, CollisionWorld)
  ② FLAIL   limbs: verlet particles in world XY, inherit root motion,
            floor-line constraint, landing impulse                        (same job chain)
  ③ SETTLE  grounded + energy < threshold → blend into existing
            zone-target lerp (settleSpeed) → sleep (zero cost at rest)
        │
Ragdoll2DReviveSystem (HealthSystemGroup) — resets transforms/state, disables everything
```

All three stages run in Burst `IJobEntity` jobs assigned to `state.Dependency`; the `CollisionWorld` is read-only injected exactly as `UnitGravitySystem` does. Per-corpse cost after settle is zero — a `sleeping` flag short-circuits the job (today's system lerps every dead unit forever; this is a fix).

**← DECISION:** exact `RagdollSystemGroup` slot — recommended `[UpdateAfter(SpawnInitSystemGroup)] [UpdateBefore(SoundSystemGroup)]`.

## 3. Entry points

Entry pattern (a) — enableable components on the entity acted on, driven by existing death flow. **No new entry surface**; the rework deepens the existing one:

- **`Dead` (enableable, existing)** — `Ragdoll2DInitSystem` detects `Dead` + not-yet-ragdolling, reads `Health.kill*`, enables and seeds everything. Unchanged shape.
- **`Ragdoll2DLaunch` (enableable, reworked)** — now `{ float3 velocity; float restitution; float spin; byte airborne; byte sleeping; }`. `groundY` deleted (raycast owns landing). Enabled on death, disabled on revive.
- **`Ragdoll2D` / `Ragdoll2DJoint` (enableable, existing)** — lifecycle unchanged; `Ragdoll2D` gains `flailIntensity`, `Ragdoll2DJoint` gains blend-state fields for the flail→settle handoff.
- **Revive** — `Ragdoll2DReviveSystem` contract unchanged: disable + restore initial rotations; additionally resets the `RagdollParticle` buffer and the new launch fields. `Ragdoll2DSpawnInitSystem` keeps force-disabling enableable bits on spawn/pool-reclaim (its `LinkedEntityGroup` scan already covers the new state — extend it to reset `Ragdoll2DLaunch.sleeping/airborne`).

## 4. Data model

**Per-attack profile — `RagdollProfileSO` (new, `_Scripts/Data/SOs/`), flattened at bake:**
- Fields: `launchForceHorizontal`, `launchForceY`, `ragdollForce` (tilt-speed scale, existing semantic), `flailIntensity`, `spin`, `restitution`.
- `AttackSO` gets `public RagdollProfileSO ragdollProfile;` (optional). `AttackLibraryBakingSystem` flattens profile → `AttackBlob`; when null, falls back to the existing inline `ragdollForce/launchForceX/launchForceY` fields (which stay for simple attacks). `AttackBlob` grows the three new floats.
- Same fields ride the existing copy-through: `DamageEvent` → `Health.kill*` → `Ragdoll2DInitSystem`. This keeps hazards/thrown items working (they set the fields directly, no profile needed).

**Kill-source position (contract change — update [`Contracts.md`](../../Memories/Code/Contracts.md)):**
- `DamageEvent.hitSourceX` (float) is **deleted**; `DamageEvent.sourcePosition` (float3, today AOE-only) becomes **always set** by every producer (`AttackRequestSystem`, `ThrownItemHitSystem`, `HazardZoneSystem`, AOE expansion in `DamageResolutionSystem`).
- `Health.killSourceX` → `Health.killSourcePosition` (float3); `Health` gains `killFlailIntensity`, `killSpin`, `killRestitution`.
- Launch: `direction = normalize(victimPos − killSourcePosition)` — horizontal (XZ) component × `launchForceHorizontal` + `up × launchForceY`. `fallSideSign` derives from the X sign exactly as today (fallback −1 when degenerate, e.g. source directly above).

**Per-joint physical params — extend `PartDef` (`PartLibraryBlob.cs`) + `PartDefinitionSO`:**
- `ragdollSegmentLength` (pivot→tip distance for the verlet particle; 0 = default) and `ragdollWeight` (scales inherited motion + landing impulse). Lives beside the existing `defaultSettleSpeed` + `zones` — the blob is already the per-part ragdoll home. `PartLibraryBakingSystem` copies them (default-seeded like the other fields).

**Runtime flail state — `RagdollParticle` (new `IBufferElementData`, on the visual-root entity):**
- `{ Entity jointEntity; float2 tipPosition; float2 tipPreviousPosition; float segmentLength; float weight; float restAngle; }` — one entry per `BodyPartFlags.RagdollJoint` part, seeded by `Ragdoll2DInitSystem` from the `BodyPart` buffer + `PartDef`. Buffer-on-root keeps the solver contiguous (no per-joint chunk hopping); the job writes each joint's `LocalTransform.Rotation` from `atan2(tip − anchor)` via a `ComponentLookup`.
- Each limb = one 1-segment pendulum (anchor = joint pivot following the body pose, tip = free particle). No chains in v1 — matches the rig's flat bend-empty joints.

**Global sim constants — `RagdollSimConfig` (new flat `IComponentData` singleton) + `RagdollConfigSO` + `RagdollSimConfigAuthoring`:**
- Replaces the hardcoded `LAUNCH_GRAVITY = 20` / `LAUNCH_X_DRAG = 2.5` in `Ragdoll2DSystem.cs`. Fields: `gravity`, `horizontalDrag`, `groundRaycastDistance`, `landingImpulseScale`, `sleepEnergyThreshold`, `settleBlendRate`, `verletIterations`, `bounceMinSpeed` (below which a wall hit just stops), `defaultRestitution`.
- Flat floats → plain singleton baked by a simple authoring on the config prefab — **not** a blob library (nothing enum-indexed).
- **← DECISION:** all default values (keep gravity 20? restitution default ~0.3? sleep threshold?). Tuned in Editor during Phase 1/2 verification.

**Collision filters (`ConstGameData.cs` layers, no new constants needed):**
- Ground/landing ray: `GROUND (3) | STRUCTURES (7) | OBJECTS (10)` — OBJECTS makes corpses rest on props for free.
- Flight wall cast: `STRUCTURES (7) | WALLS (8)` — short raycast along `velocity × dt` + skin; on hit `velocity = reflect(velocity, normal) × restitution`, and below `bounceMinSpeed` horizontal velocity zeroes.

## 5. Systems

| System | Group | Change |
|---|---|---|
| `Ragdoll2DInitSystem` | `HealthSystemGroup` (after `DeathSystem`) — unchanged | Reworked: 3D velocity from `killSourcePosition`, seeds `RagdollParticle` buffer (segment length/weight/rest angle from `PartDef` + joint transforms), copies flail/spin/restitution from `Health.kill*`. Zone-target picking unchanged. |
| `Ragdoll2DSystem` | **`RagdollSystemGroup` (new, LateSim)** — file moves to `Systems/RagdollSystemGroup/` | Reworked into the 3-stage driver (§2). Flight + flail + settle as `IJobEntity`/`ScheduleParallel` with `CollisionWorld` read-only. Sleeps settled corpses. |
| `Ragdoll2DReviveSystem` | `HealthSystemGroup` (after `ReviveRequestSystem`) — unchanged | Extended: also resets `RagdollParticle` buffer + new launch fields. |
| `Ragdoll2DSpawnInitSystem` | `SpawnInitSystemGroup` — unchanged | Extended: reset `airborne`/`sleeping` on pool-reclaim. |
| `CorpseCellSystem` (new, Phase 4) | `GameManagerSystemGroup` (world-services charter: spatial hashes live here) | Maintains `NativeParallelMultiHashMap<int2, CorpseCellEntry>` singleton keyed by XZ cell. Corpses register on settle, unregister on revive/despawn. Landing height = ground hit + `stackOffset × corpsesBelow` (clamped). |

`SystemGroups.cs` gains the `RagdollSystemGroup` declaration with explicit `UpdateAfter(SpawnInitSystemGroup)` / `UpdateBefore(SoundSystemGroup)` edges; `SystemGroupOrderTests` + `SystemPlacementConformanceTests` get the matching rows (`dots-feature-group` covers all of this).

## 6. Integration points

- **Combat:** `DamageEvent` field change ripples to every producer — `AttackRequestSystem`, `ThrownItemHitSystem`, `HazardZoneSystem`, `DamageResolutionSystem` (AOE), and `DamageEventSystem` (kill* capture). One-line changes each, but it's a **contract edit → update `Contracts.md`**.
- **Attack data:** `AttackSO` + `AttackBlob` + `AttackLibraryBakingSystem` grow the profile fields. Existing attack SOs need no edits (inline fallback).
- **Character rig:** `PartDefinitionSO`/`PartDef`/`PartLibraryBakingSystem` gain the two per-joint fields; `BodyPartAuthoring`/`CharacterRigBakingSystem` unchanged (joint identity stays `BodyPartFlags.RagdollJoint` — per Q&A, **no** new `RagdollJointAuthoring`).
- **Physics:** read-only `CollisionWorld` queries, same pattern as `UnitGravitySystem` (which already excludes `Dead` roots — no overlap).
- **Save:** none — ragdoll state is transient; a saved-dead unit re-runs init on load. Verify this assumption against `Save_System.md` during build.
- **Sound (future):** landing/bounce events are natural `SoundRequest` emitters — out of scope, but the group ordering (§2) reserves it.
- **Deleted:** `MonoBehaviours/Managers/RagDollManager.cs` (fully commented-out legacy).

## 7. Proposed file manifest

**New:**
- `_Scripts/Data/SOs/RagdollProfileSO.cs`, `_Scripts/Data/SOs/RagdollConfigSO.cs`
- `_Scripts/Components/Units/RagdollParticle.cs` (buffer element) · `RagdollSimConfig` (into `Ragdoll2DComponents.cs`)
- `_Scripts/Authoring/RagdollSimConfigAuthoring.cs`
- `_Scripts/Systems/RagdollSystemGroup/Ragdoll2DSystem.cs` (moved + reworked)
- `_Scripts/Systems/GameManagerSystemGroup/CorpseCellSystem.cs` + `CorpseCell` components (Phase 4)
- Assets: `_RagdollConfig.asset`, starter profiles (e.g. `RagdollProfile_Baseline`, `RagdollProfile_Explosive`)

**Edited:** `Ragdoll2DComponents.cs` (launch rework, new fields) · `Ragdoll2DInitSystem.cs` · `Ragdoll2DReviveSystem.cs` · `Ragdoll2DSpawnInitSystem.cs` · `SystemGroups.cs` · `AttackSO.cs` / `AttackBlobs.cs` / `AttackLibraryBakingSystem.cs` · `DamageEvent.cs` / `DamageBus` producers ×4 / `DamageEventSystem.cs` · `Health` (kill* fields) · `PartDefinitionSO.cs` / `PartLibraryBlob.cs` / `PartLibraryBakingSystem.cs` · `SystemGroupOrderTests.cs` / `SystemPlacementConformanceTests.cs` · vault docs (`Systems.md`, `Contracts.md`, plans README)

**Deleted:** `MonoBehaviours/Managers/RagDollManager.cs`

## 8. Build phases

1. **3D trajectory + real landing + bounce.** `sourcePosition` contract change; `Health.killSourcePosition`; `Ragdoll2DLaunch` → `float3 velocity` + raycast landing + wall bounce with restitution; `RagdollSimConfig` pipeline; `RagdollSystemGroup` formalization (declaration, file move, conformance rows). Joints untouched — settle behaves exactly as today. *Independently shippable; fixes both current gaps (world-X-only launch, frozen `groundY`).*
2. **Verlet limb flail.** `RagdollParticle` buffer + `PartDef` fields; flail stage in the driver (inherit root motion, floor line, landing impulse, spin); energy-based handoff into the existing zone-target settle; sleeping. `flailIntensity` rides the kill* copy-through.
3. **Tuning consolidation.** `RagdollProfileSO` + `AttackSO` reference + bake flattening; starter profile assets; delete `RagDollManager.cs`; docs truth pass (`Systems.md`, `Contracts.md`, `Gotchas.md` if the LinkedEntityGroup/spawn-init story changes).
4. **Corpse stacking** *(cuttable)*. `CorpseCellSystem` + landing-height stack offset. Props already work via the Phase-1 OBJECTS-layer ray.

## 9. Verification

Per phase, in `DOTSTestScene` (compile → rebake → play loop; Spencer verifies visuals — Claude greps `Editor.log` for `error CS`/`BC` only):

1. **P1:** kill a unit with a launch-configured attack near a ledge → corpse arcs and lands on the *lower* ground (inspect `Ragdoll2DLaunch.velocity` in the Entities window while airborne). Kill one into a wall → visible bounce-off. Kill with a depth-ward (Z) attack → corpse translates along Z, articulation stays in XY. Revive mid-flight and post-landing → clean reset both times.
2. **P2:** limbs visibly trail during flight and kick on landing, then settle into the same authored poses as before the rework (side-by-side feel check vs. current behaviour). `RagdollParticle` buffer visible on the visual root. Settled corpses stop appearing in the driver's profiler marker (sleep works).
3. **P3:** two attacks sharing one profile change together when the profile is edited; an attack with a null profile still ragdolls off inline fields.
4. **P4:** kill 5+ units on one spot → visible pile height; revive one from the middle → hash unregisters, no floating corpses. Horde check: AoE-kill ~50 units → no frame spike (profiler: driver job + raycast count).

**Spencer-only:** all feel/tuning (restitution, flail intensity, settle blend), and confirming the at-rest silhouette still matches the current authored look.

## Open decisions (collected)

- [x] §2 — `RagdollSystemGroup` slot: **confirmed** `SpawnInit → Ragdoll → Sound`.
- [x] §4 — `RagdollSimConfig` defaults: **carry over current feel** — gravity 20, drag 2.5, restitution 0.3, bounceMinSpeed 2, groundRaycastDistance 5, landingImpulseScale 1.
- [x] §4 — `spin` semantics: **additive Z spin while airborne, damps on landing**; tilt settles to the nearest full turn so tumbles never unwind.
- [x] §4 — per-joint defaults: **0.5 segment length / 1.0 weight**.
- [x] §8/P4 — **1 m cells, 0.15 stack offset, cap 5; no Corpses layer reserved** (hash-only v1).
- [x] Wall response (earlier Q&A): **bounce with restitution from Phase 1** (no stop-and-drop interim).

## Build deviations (recorded at execution, 2026-07-09)

1. **No `RagdollParticle` buffer.** The 1-segment-pendulum flail is represented in polar form —
   `angularVelocity` + `currentZAngle` on `Ragdoll2DJoint` itself — identical dynamics to the
   positional verlet the spec sketched, with no new buffer component and no extra bake step.
   `segmentLength`/`weight` still bake from `PartDef` as specified.
2. **Config fields renamed to match the model:** `verletIterations`/`settleBlendRate` became
   `flailDamping` (1.5) + `sleepAngularSpeedDeg` (1 deg/s); the settle blend reuses each joint's
   existing `settleSpeed` verbatim, so the at-rest look is byte-identical to v1.
3. **Corpse hash is rebuilt per frame** (world-services reset pattern, like `DamageBus`) instead of
   register-on-settle — revive/despawn unregistration becomes free. Reader safety via
   `CorpseCellSystem.AddJobHandleForReader` (ECB-owner pattern).
4. **`launchForceX` keeps its name** (rather than the spec's `launchForceHorizontal`) — it now means
   "horizontal launch speed away from the kill source in 3D", preserving SO serialization.
5. **Sleeping corpses still write their settled rotations each frame** — `ApplyPoseJob` stomps every
   part `LocalTransform` unconditionally, so a full sleep would snap corpses back to the animated
   pose. Dynamics (raycasts, pendulum math) do stop; only the cheap pose re-write remains.
6. **`RagdollSimConfig` fallback defaults** are compiled into `Ragdoll2DSystem`/`Ragdoll2DInitSystem`
   so the ragdoll keeps working before `RagdollSimConfigAuthoring` is placed in the subscene.
