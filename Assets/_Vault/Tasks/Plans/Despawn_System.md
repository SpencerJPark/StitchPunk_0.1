# Despawn System — Design Spec

> **Status:** ✅ decisions locked 2026-09-15 (§12, under the standing delegation) — ready for a `spec-lead` worktree run as spec id `despawn`; §12 is the build plan, §1–§10 the design.
> **Raw source:** [`../futureneedsplan.md`](../futureneedsplan.md) → "soundsystemgroup / spawn-despawn" area (pooling of units & effects)

---

**Skills Needed** (project skills in `.claude/skills/` — see [Skills index](../../Memories/Code/Skills.md)):
- `dots-system-scaffold` — `DespawnSystem` and `LifetimeSystem` (new `ISystem` + jobs) (§5).
- `dots-authoring-baker` — `LifetimeAuthoring` MonoBehaviour + Baker; the `Despawn`/`DespawnMode` bake is an **edit** to the existing `UnitAuthoring.Baker`, not a new file (§4).
- *(no `dots-blob-library` / `dots-unit-ai` — no SO→Blob library and no AI behaviour involved.)*

---

## 1. Purpose & v1 scope

A single, centralized funnel that tears down entities that no longer need to exist. Any system enables the already-existing `Despawn` enableable component on an entity; once per frame the `DespawnSystem` (in the existing `DespawnSystemGroup`, `LateSimulationSystemGroup`) gathers every entity with `Despawn` enabled, decides **pool vs. destroy**, and applies the structural change in one batched pass — instead of N systems each doing ad-hoc `DestroyEntity` / `AddComponent<Disabled>`.

The decision is driven by a `DespawnMode` field on `Despawn` plus the presence of the existing `PoolOwner` component:
- **Pool-managed entity** (`PoolOwner` present) → returned to the pool by adding `Disabled` (the existing dormant-pool convention `UnitSpawnerSystem` already reclaims from), **unless** the per-type pool cap is exceeded → destroyed instead.
- **Non-pooled entity** → `DestroyEntity` (its `LinkedEntityGroup` children — body parts — go with it).

**v1 handles:**
- **Pooled units** (`PoolOwner`, keyed by `UnitType`) — pooled with a per-type cap; overflow destroyed.
- **Generic catch-all** — any entity that enables `Despawn` with no `PoolOwner` is destroyed. This is what makes future effect/projectile/sound entities "just work" once they set `Despawn`.
- **A real producer:** a `Lifetime { secondsRemaining }` component that ticks down and enables `Despawn` at zero (TTL) — doubles as the demo trigger and the reusable mechanism future timed effects/projectiles use.
- **Overflow trimming:** each pass also scans the dormant pool per `UnitType` and destroys any beyond cap — so even entities pooled by other paths (distance-pooling, see §7) stay bounded.

**Out of v1 (reserved hooks, not built):**
- Dedicated **effect/VFX entity** pooling — no VFX-entity system exists yet (`EffectType` is *gameplay status effects*: OnFire/Poisoned/Healing…, not spawned particles). When VFX entities exist, give them `PoolOwner` (or let them fall through the catch-all) and they funnel through unchanged.
- **Sound entities** — already self-consumed: `PlaySound` one-shots are `DestroyEntity(query)`-ed each frame by `VoiceSelectionSystem`; `LoopingSound` is a persistent component the `AudioManager` frees. Neither routes through Despawn. No change.
- **Death-driven despawn** — `DeathSystem` deliberately keeps dead units as revivable ragdolls/corpses (the necromancy loop). Death is *not* a despawn producer; a later "corpse cleanup after N seconds" feature would just attach a `Lifetime` to an un-revivable corpse.

## 2. Architecture

Pure ECS, no MonoBehaviour bridge (no managed Unity objects involved). Two new systems, both in the **existing** `DespawnSystemGroup` (`LateSimulationSystemGroup`, after Spawn → SpawnInit → Sound, before Save — see `SystemGroups.cs:144`):

```
DespawnSystemGroup (LateSimulationSystemGroup)
  ├─ LifetimeSystem            (OrderFirst)  tick Lifetime → enable Despawn at 0
  ├─ UnitPoolReturnSystem      (existing)    distance-pool: PoolOwner + Disabled
  └─ DespawnSystem             (OrderLast / UpdateAfter UnitPoolReturnSystem)
        1. parallel gather  : classify each [Despawn-enabled] entity → toPool / toDestroy
        2. main-thread pass : Temp ECB — destroy, or Disable up to cap (overflow → destroy)
        3. overflow trim    : per-UnitType dormant count > cap → destroy excess
```

**The "parallel + buffer then delete" model:** an `IJobEntity.ScheduleParallel` over `[WithAll<Despawn>]` (enabled-only by default) writes each entity into one of two `NativeList<…>.ParallelWriter`s (pool / destroy) — no structural changes in the job. `state.Dependency` is completed, then a single **main-thread pass** with a `Temp` `EntityCommandBuffer` applies the structural changes and plays back immediately (mirroring `UnitPoolReturnSystem`'s self-contained ECB style — no reliance on an ECB singleton inside `LateSimulationSystemGroup`).

**← DECISION:** keep `LifetimeSystem` inside `DespawnSystemGroup` (one-frame TTL latency, negligible) vs. ticking it earlier in `SimulationSystemGroup`. Recommended: keep it here so all despawn concerns live in one group.

## 3. Entry points

**Request model — `Despawn` (existing, extended):** an `IEnableableComponent` baked **disabled** on every poolable/despawnable prefab. A system enables it when the entity should go away; `DespawnSystem` reads it, acts, and (for pooled entities) **disables it again** so the dormant entity is clean for reuse.

```csharp
// Components/Spawners/SpawnerComponents.cs  (EXTEND the existing empty tag)
public struct Despawn : IComponentData, IEnableableComponent
{
    public DespawnMode mode;   // Auto / ReturnToPool / ForceDestroy
}

// Data/Enums/DespawnMode.cs  (NEW)
public enum DespawnMode
{
    Auto,          // PoolOwner present → pool; else destroy
    ReturnToPool,  // force pool (no-op/destroy if no PoolOwner)  ← see §5 edge note
    ForceDestroy,  // destroy even if PoolOwner present (e.g. permanent removal)
}
```

**TTL producer — `Lifetime` (new):** the v1 concrete producer; reusable for any timed entity.

```csharp
// Components/Spawners/SpawnerComponents.cs  (NEW)
public struct Lifetime : IComponentData, IEnableableComponent
{
    public float secondsRemaining;   // LifetimeSystem ticks down; enables Despawn at <= 0
}
```
Enableable so a reclaimed pool entity can re-arm (or stay dormant without ticking).

## 4. Data model

No SO→Blob library. All data is small runtime components + one enum.

- **Config:** per-type dormant-pool cap. **← DECISION:** v1 = single global `const int PoolCapPerType` in `DespawnSystem` (recommended start `64`). Promote to a baked `DespawnConfig` singleton (or per-`UnitType` blob) later if types need different caps — leave a `// ← DECISION` marker at the const.
- **Runtime context:** `Despawn.mode`, `Lifetime.secondsRemaining`, existing `PoolOwner.unitType`.
- **No managed registry** — nothing references managed objects.

## 5. Systems

**`LifetimeSystem`** — `DespawnSystemGroup`, `OrderFirst`. `[BurstCompile]`, `ScheduleParallel`.
- Queries `[WithAll<Lifetime>]` (enabled). Decrements `secondsRemaining` by `SystemAPI.Time.DeltaTime`; when `<= 0`, sets `Despawn` enabled (via `EnabledRefRW<Despawn>` — present on the same entities) and disables `Lifetime`.
- Naming: the `EnabledRefRW<Despawn>` param is `despawnEnabled`, `EnabledRefRW<Lifetime>` is `lifetimeEnabled` (per `[[feedback_enabledref_naming]]`).

**`DespawnSystem`** — `DespawnSystemGroup`, `[UpdateAfter(typeof(UnitPoolReturnSystem))]` (so units distance-pooled this frame are visible to the overflow trim). **Not** `[BurstCompile]` on the system (it does main-thread structural changes + `EntityManager` queries), but the gather job **is** Burst.
1. **Gather job** (`ScheduleParallel`, `[WithAll<Despawn>]`): for each entity, read `mode` + `ComponentLookup<PoolOwner>` →
   - pool-bound (`Auto` & has `PoolOwner`, or `ReturnToPool` & has `PoolOwner`) → write `(entity, unitType)` to `_toPool` (`NativeList<PoolCandidate>.ParallelWriter`).
   - destroy-bound (`ForceDestroy`, or no `PoolOwner`) → write `entity` to `_toDestroy` (`NativeList<Entity>.ParallelWriter`).
2. **Complete + main-thread pass** (Temp ECB):
   - Snapshot current dormant counts per `UnitType` from the dormant query `[WithAll<PoolOwner, Disabled>]` + `EntityQueryOptions.IncludeDisabledEntities` (same pattern as `UnitSpawnerSystem._poolQuery`) into a `NativeHashMap<UnitType,int>` (or small `NativeArray` indexed by `(int)UnitType`).
   - `_toDestroy` → `ecb.DestroyEntity(entity)`.
   - `_toPool` → if `count[type] < PoolCapPerType`: `ecb.AddComponent<Disabled>(entity)`, `ecb.SetComponentEnabled<Despawn>(entity, false)` (clean for reuse), `count[type]++`. Else (over cap): `ecb.DestroyEntity(entity)`.
   - **Overflow trim:** for any `UnitType` whose dormant count still exceeds cap (e.g. distance-pooled bulk), destroy the excess dormant entities. **← DECISION:** trim every frame vs. throttle (e.g. every Nth frame / only when over by margin M) to avoid churn. Recommended: trim only the amount over cap, every frame — cheap, bounded.
   - `ecb.Playback(state.EntityManager); ecb.Dispose();` and dispose the NativeLists/map (all `Allocator.TempJob`/`Temp`).

**Gotcha to encode (LinkedEntityGroup + Disabled):** `AddComponent<Disabled>` on a unit parent does **not** auto-disable its `LinkedEntityGroup` children (body parts) — `UnitPoolReturnSystem` already only disables the parent. **← DECISION:** for v1 mirror that (parent-only Disabled, consistent with existing pooling) and add a `[[Gotchas]]` note; OR iterate the `LinkedEntityGroup` buffer and add `Disabled` to each child so pooled units fully stop rendering/simulating. Recommended: match existing behaviour in v1, file the full-group disable as a follow-up if dormant body parts misbehave. `DestroyEntity` already respects `LinkedEntityGroup`, so the destroy path needs no special handling.

## 6. MonoBehaviour bridge
None — no managed Unity objects.

## 7. Integration points

- **`UnitPoolReturnSystem`** (`Systems/SpawnSystemGroup/UnitPoolReturnSystem.cs`) — **stays as-is** (still adds `Disabled` directly for distance-based pooling). `DespawnSystem` runs after it and its overflow-trim is the shared bound that keeps the distance path from growing the pool unbounded.
- **`UnitSpawnerSystem`** — reclaims `Disabled` pool units. The `Despawn` re-disable in §5 keeps reclaimed entities clean (re-enables `NewlySpawned`, etc. on reclaim — unchanged).
- **`PoolOwner` / `NewlySpawned`** (`SpawnerComponents.cs`) — the existing pool-membership + first-frame-init convention. `Despawn` joins them as the teardown half of the same lifecycle.
- **`UnitAuthoring.Baker`** (`Authoring/Units/UnitAuthoring.cs:21-22`) — currently bakes `NewlySpawned` disabled; add `AddComponent<Despawn>` + `SetComponentEnabled<Despawn>(entity, false)` right beside it (default `mode = Auto`). `Lifetime` is **not** baked on units (units pool by distance/cap, not TTL).
- **`DespawnItemRequest`** (`Components/Items/ItemComponents.cs:39`) — pre-existing item-specific despawn; out of scope to migrate in v1, but note it as a candidate to fold into this funnel later (`← DECISION` in the Open list).
- **Save (`SaveSystemGroup`)** — runs `OrderLast`, after Despawn each frame, so a snapshot never sees half-despawned entities. `PersistentLoadSystem` already `DestroyEntity`s owned minions on load (with `LinkedEntityGroup`); no conflict.
- **No `SystemGroups.cs` change** — `DespawnSystemGroup` already exists.

## 8. Proposed file manifest

**New:**
- `Assets/_Scripts/Data/Enums/DespawnMode.cs` — the enum.
- `Assets/_Scripts/Systems/SpawnSystemGroup/DespawnSystemGroup/DespawnSystem.cs` — the funnel.
- `Assets/_Scripts/Systems/SpawnSystemGroup/DespawnSystemGroup/LifetimeSystem.cs` — TTL tick. *(Folder `DespawnSystemGroup/` already exists as a `.meta`; place both systems under it.)*
- `Assets/_Scripts/Authoring/LifetimeAuthoring.cs` — MonoBehaviour + Baker (`TransformUsageFlags.None` unless the entity also needs a transform; the bake adds `Lifetime` + `Despawn` disabled).

**Edited:**
- `Assets/_Scripts/Components/Spawners/SpawnerComponents.cs` — add `mode` field to `Despawn`; add `Lifetime`.
- `Assets/_Scripts/Authoring/Units/UnitAuthoring.cs` — bake `Despawn` (disabled) beside `NewlySpawned`.
- `Assets/_Scripts/Systems/SpawnSystemGroup/UnitPoolReturnSystem.cs` — *(only if §7 overflow ownership shifts; v1 = no change.)*

**Assets:** none (no SOs). A test entity with `LifetimeAuthoring` placed in `DOTSTestScene` for verification.

## 9. Build phases

1. **Data layer:** add `DespawnMode` enum, extend `Despawn` with `mode`, add `Lifetime`. Bake `Despawn` disabled in `UnitAuthoring`. *(Compile gate: clean console.)*
2. **Destroy path end-to-end:** `LifetimeSystem` + `DespawnSystem` with only the **destroy** branch (no PoolOwner). Place a `LifetimeAuthoring` test cube in `DOTSTestScene`. Verify it vanishes after its TTL.
3. **Pool path + cap:** add the `PoolOwner` → `Disabled` branch + per-type dormant count + over-cap destroy. Verify a `Despawn`-enabled unit goes dormant (still in pool) and is reclaimed by `UnitSpawnerSystem`.
4. **Overflow trim:** add the per-`UnitType` dormant-overflow destroy. Verify with a low cap (`← DECISION` temp value) that mass-pooling trims down to cap.
5. **Polish:** confirm `Despawn` re-disable on pool, LinkedEntityGroup handling decision, logging (optional `LogCategory`), update `Systems.md` + `Components.md` + `Gotchas.md`.

## 10. Verification

Play `DOTSTestScene` and use the **Entities Hierarchy / Inspector** window:
- **Phase 2:** a `LifetimeAuthoring` entity with `secondsRemaining = 3` disappears ~3s after Play. Inspector shows `Despawn` flipping enabled the frame before destruction.
- **Phase 3:** enable `Despawn` on a `PoolOwner` unit (toggle in inspector, or via the debug path). Confirm it gains `Disabled` (not destroyed) and that a `UnitSpawner` reclaims it (`NewlySpawned` re-enabled, repositioned).
- **Phase 4:** set a deliberately tiny cap, spawn > cap units, mass-pool them; confirm dormant count settles at cap and the rest are destroyed (Entities window entity count).
- **Per-phase success signal:** entity counts in the Entities window move exactly as predicted; no `Despawn`-enabled entity survives a frame; no leaked dormant entities beyond cap.
- **Spencer-only (Editor):** visual confirmation that pooled units fully disappear (rendering) — informs the LinkedEntityGroup `← DECISION` in §5.

## Open decisions (collected)
- [ ] §2 — `LifetimeSystem` group placement (in `DespawnSystemGroup` vs. `SimulationSystemGroup`). Default: keep in `DespawnSystemGroup`.
- [ ] §3 — `DespawnMode.ReturnToPool` on an entity *without* `PoolOwner`: destroy, or no-op + warn? Default: treat as destroy (can't pool what isn't poolable).
- [ ] §4 — pool cap: global `const` (start 64) vs. baked `DespawnConfig` singleton vs. per-`UnitType` blob. Default: global const for v1.
- [ ] §5 — overflow trim cadence: every frame (amount-over-cap only) vs. throttled. Default: every frame, bounded.
- [ ] §5 — LinkedEntityGroup on pool: parent-only `Disabled` (match existing) vs. disable the whole group. Default: parent-only for v1 + Gotchas note.
- [ ] §7 — fold `DespawnItemRequest` into this funnel now or later. Default: later.
- [ ] §1/§8 — also ship a debug-key trigger alongside `Lifetime`? Default: no (Lifetime + inspector toggle suffice for v1 verification).

---

## 12. Worktree build plan (2026-09-15) — spec id `despawn`

Every ← DECISION above is settled here; §1–§10 stay as the design record. Names were re-verified against
`2464e854` on 2026-09-15; drift is listed first so nobody follows the older paragraphs into a wrong folder.

### 12.1 Drift from the tree

- **Folder:** the group's folder is `Assets/_Scripts/Systems/DespawnSystemGroup/` (top level, folder mirrors group
  — `SystemPlacementConformanceTests` enforces it), **not** `Systems/SpawnSystemGroup/DespawnSystemGroup/`.
  `UnitPoolReturnSystem.cs` already lives there. Both new systems go beside it.
- **`DespawnItemRequest` no longer exists** — §7's fold-in question is void.
- **Nothing enables `Despawn` today** (no `SetComponentEnabled<Despawn>`, `EnabledRefRW<Despawn>` or
  `WithAll<Despawn>` anywhere), so the enum field and the bake are purely additive.
- `Despawn` is declared at `Components/Spawners/SpawnerComponents.cs:3`, `PoolOwner` at `:17`, `NewlySpawned` at
  `:28`; `UnitAuthoring.Baker` bakes `NewlySpawned` disabled at `Authoring/Units/UnitAuthoring.cs:21-22`;
  `Data/Enums/` exists; `UnitSpawnerSystem._poolQuery` uses `EntityQueryOptions.IncludeDisabledEntities`.
- `UnitPoolReturnSystem` uses `var` and a main-thread `foreach` — pre-existing; **leave it untouched**.

### 12.2 Decisions

- **DS-D1** `LifetimeSystem` lives in `DespawnSystemGroup`, `OrderFirst = true`. One-frame TTL latency is fine.
- **DS-D2** `DespawnMode.ReturnToPool` on an entity without `PoolOwner` destroys it (you cannot pool what is not
  poolable). No warning.
- **DS-D3** Pool cap is `private const int PoolCapPerType = 64;` on `DespawnSystem`, with a one-line comment that a
  per-type blob replaces it when two types need different caps.
- **DS-D4** Overflow trim runs every frame and destroys only the amount over cap.
- **DS-D5** Pooling adds `Disabled` to the parent only, matching `UnitPoolReturnSystem`; `Gotchas.md` gains the
  entry. `DestroyEntity` follows `LinkedEntityGroup` on its own.
- **DS-D6** No debug key. `Lifetime` plus a PlayMode fixture is the verification.
- **DS-D7** `DespawnSystem` is not `[BurstCompile]` at the system level (main-thread structural pass); the gather
  job is. `.ScheduleParallel()` into `state.Dependency`, then `state.Dependency.Complete()` before the ECB pass.

### 12.3 Tasks

- [ ] **T0 — Ground (lead).** `git rev-parse --show-toplevel`; claim; grep every name in 12.1; run the game's
  EditMode conformance pair once through the gate to learn the baseline:
  `gate --edit-mode StitchPunk.Tests.SystemPlacementConformanceTests --edit-mode StitchPunk.Tests.SystemGroupOrderTests`.
- [ ] **T1 — Data (one worker, two files).** `Data/Enums/DespawnMode.cs` (Auto / ReturnToPool / ForceDestroy);
  `SpawnerComponents.cs`: `Despawn` gains `public DespawnMode mode;`, new
  `Lifetime : IComponentData, IEnableableComponent { public float secondsRemaining; }`. Commit; T2–T4 gate it.
- [ ] **T2 — Bake (one worker, two files)** `[parallel-safe with T3, T4]`: `UnitAuthoring.Baker` adds `Despawn`
  disabled beside `NewlySpawned` (default `mode = Auto`); new `Authoring/LifetimeAuthoring.cs` (MonoBehaviour with
  `public float seconds = 3f;` + nested `Baker`, `TransformUsageFlags.Dynamic`, adds `Lifetime` enabled and
  `Despawn` disabled). Follow `dots-authoring-baker`.
- [ ] **T3 — `LifetimeSystem` (one worker, one file)** `[parallel-safe]`: `Systems/DespawnSystemGroup/LifetimeSystem.cs`,
  `[UpdateInGroup(typeof(DespawnSystemGroup), OrderFirst = true)]`, `[BurstCompile]`, an `IJobEntity` over
  `RefRW<Lifetime>`, `EnabledRefRW<Lifetime> lifetimeEnabled`, `EnabledRefRW<Despawn> despawnEnabled`; decrements by
  `deltaTime`; at `<= 0` sets `despawnEnabled.ValueRW = true; lifetimeEnabled.ValueRW = false;`. `ScheduleParallel`.
- [ ] **T4 — `DespawnSystem` (one worker, one file)** `[parallel-safe]`: `Systems/DespawnSystemGroup/DespawnSystem.cs`,
  `[UpdateInGroup(typeof(DespawnSystemGroup))] [UpdateAfter(typeof(UnitPoolReturnSystem))]`. Gather job
  (`[WithAll<Despawn>]`, `ComponentLookup<PoolOwner>` read-only, two `NativeList` parallel writers,
  `Allocator.TempJob`), complete, main-thread `EntityCommandBuffer(Allocator.Temp)`: destroy list; pool list under
  cap → `AddComponent<Disabled>` + `SetComponentEnabled<Despawn>(entity, false)`; over cap → destroy; then the
  dormant overflow trim per `UnitType` using a query with `IncludeDisabledEntities`. Playback, dispose everything.
- [ ] **T5 — Fixture (one worker, one new file)** — `Tests/PlayMode/DespawnSystemTests.cs`, namespace
  `StitchPunk.Tests.PlayMode`, manual `World`, systems fetched with `GetOrCreateSystem<T>()` and updated by hand
  (see `DeathAnimationTests.cs` for the pattern). Three tests:
  `Lifetime_ReachingZero_EnablesDespawn_ThenEntityIsDestroyed`;
  `PooledEntity_WithDespawn_GainsDisabled_AndDespawnIsReDisabled`; `PooledEntities_OverCap_AreDestroyed` (cap
  reached by creating 64 + 3 dormant `PoolOwner` entities of one `UnitType`). Revert-to-fail each against the
  branch it pins. **PlayMode: the broker refuses it** — send the stage
  "gate needed: despawn <sha> StitchPunk.Tests.PlayMode.DespawnSystemTests".
- [ ] **T6 — Vault (docs worker)** `[parallel-safe]`: `Systems.md` (a Despawn paragraph under the LateSim order),
  `Components.md` (`Despawn.mode`, `Lifetime`), `Contracts.md` (`Despawn` row: writers = any system /
  `LifetimeSystem`; reader = `DespawnSystem`), `Gotchas.md` (DS-D5). No status in CLAUDE.md files.
- [ ] **T7 — Close (lead).** Every wave gated (`SystemPlacementConformanceTests` + `SystemGroupOrderTests` in each
  gate); a log at the end of this section: commits, gate verdicts, drift, what is unverified (the rebake and the
  in-scene look), then `status despawn ready`. The stage moves this file to `Tasks/Verification/` with a
  `verify-despawn.md` built from §10.

### 12.4 Log

**Phase 0 (stage, 2026-09-15).** Trunk `faebc8eb`; `doctor` clean (git 2.43.0, hooks installed, `brokerAlive` true, no
stage blockers); no worktrees. Compile gate clean. Baseline: `DotsAnimationToolkit.Tests.EditMode` 866 (865 passed,
standing Conformance_A), `.PlayMode` 285 of 285; `StitchPunk.Tests` **65** (63 passed: `SystemPlacementConformanceTests`
`EverySystemFileDeclaresAnUpdateGroup` and `EverySystemTypeCarriesAnExplicitGroupAttribute` failed on the owner's
converted damage stub `Systems/CombatSystemGroup/DamageEventSystemAnimEventSystem.cs`, which had no `[UpdateInGroup]`).
Fixed on the stage in `529e8bcc` (`[UpdateInGroup(typeof(CombatSystemGroup))]`), re-run 65 of 65.
`StitchPunk.Tests.PlayMode` **16** of 16. Floor for this batch: `StitchPunk.Tests` 65 (+ `AttackResolutionTests` from
minion-orders), `StitchPunk.Tests.PlayMode` 16 (+ `DespawnSystemTests`), both with zero failures.

**T0 (lead, 2026-09-15).** Claimed `spec/despawn`. Every 12.1 name re-verified by grep; baseline gate (conformance pair) **9 of 9**.
Drift found beyond 12.1 (6):
- **DS-T3 as written would never fire.** An `EnabledRefRW<Despawn>` parameter enrols `Despawn` as enabled-only (Gotchas), so
  `LifetimeCountdownJob` carries `[WithPresent(typeof(Despawn))]`. Test 1 starts with `Despawn` off to pin it.
- `NativeHashMap` cannot key on `UnitType` (enums are not `IEquatable`) — dormant counts are keyed by `(int)unitType`.
- `NativeList.ParallelWriter` cannot grow — both gather lists take `despawnRequestQuery.CalculateEntityCount()` (enabled-filtered) as capacity.
- `Components.md` listed a non-existent `NeedsAnimatorInit` in `SpawnerComponents.cs` (row + field block) — corrected.
- New edge, not fixed (outside this spec's files): a unit distance-pooled on the frame its `Despawn` is enabled keeps the bit
  in the pool (the gather skips `Disabled`) and despawns again on reclaim; `SpawnStateInitSystem` does not reset `Despawn`.
  Recorded in `Gotchas.md`. Also `SystemGroups.cs:157`'s comment omits Ragdoll (stage-owned).
- Main pass trims existing dormant overflow first, then pools this frame's returns into what is left (same outcome as the §5 wording).

**Commits.** `e996c7db` T1 (lead) · `27c5c1e1` T2+T3 (lead) + T4 (worker) + T6 (two docs workers, lead fixes) · `8709f6fd` T5 fixture (worker).

**Gates.** Conformance pair (`SystemPlacementConformanceTests` + `SystemGroupOrderTests`): T0 baseline 9 of 9; `ec62b9b1` 9 of 9,
compile and Burst clean. `StitchPunk.Tests.PlayMode.DespawnSystemTests` on `ec62b9b1` (stage, by hand): **3 of 3**.

**Revert-to-fail (batched, one mutation commit `fa61e89d`, each test failing for its own reason).** The mutation: `LifetimeSystem`
writes `despawnEnabled` false; `DespawnSystem` drops the `Despawn` re-disable and the dormant-trim `DestroyEntity`. Conformance
pair 9 of 9 on it (so failures are assertions, not compile). Stage verdict: **0 of 3**. Test 1 "should enable Despawn" expected True
was False; test 2 "should re-disable Despawn" expected False was True; test 3 "trim ... to the pool cap" expected 64 was 67.
Hard reset one commit back to `ec62b9b1`; sha256 of both system files matches the pre-mutation hashes.

**Unverified.** The rebake (units' new `Despawn` archetype) and everything in-scene: Lifetime cube vanishing, inspector pooling and
reclaim, dormant cap under distance pooling, and whether root-only `Disabled` leaves body parts visible. The `[WithPresent]` fix is
pinned by test 1 but was not separately mutated.

### For integration

- **Vault truths to check at merge** (the minion-orders lead edits the same notes; take both sides): `Systems.md` tree line and
  `DespawnSystemGroup` table read LifetimeSystem → UnitPoolReturnSystem → DespawnSystem; `Components.md` `SpawnerComponents.cs`
  row and the `## Spawner Components` block list `Despawn`/`Lifetime`/`NewlySpawned` (no `NeedsAnimatorInit`); `Contracts.md` has
  one `Despawn` row after `SaveRequest / LoadRequest`; `Gotchas.md` `## Spawning` gains two entries (root-only pooling; same-frame
  distance-pool keeps `Despawn` on).
- **Rebake:** `UnitAuthoring.Baker` now adds `Despawn` (disabled) to every unit prefab — an archetype change. The owner's next Play
  rebakes; until then baked subscenes lack `Despawn` and `DespawnSystem` simply finds nothing. No scene or asset was edited.
- **Follow-up (not in this spec's files):** add `Despawn` → off to `SpawnStateInitSystem` if reclaimed units vanish on the frame
  they are reclaimed (Gotchas entry). `SystemGroups.cs:157` LateSim comment omits Ragdoll.
- **Play checks for `verify-despawn.md`** (from §10): a `LifetimeAuthoring` cube (seconds 3) in `DOTSTestScene` vanishes about 3 s
  into Play and the Entities window shows it gone; toggling `Despawn` on a spawned unit in the inspector gives it `Disabled` (not
  destroyed) with `Despawn` back off, and a `UnitSpawner` later reclaims it; walking far from a crowd leaves at most 64 dormant
  units per `UnitType`; watch whether pooled units' body parts still render (DS-D5 root-only choice).
