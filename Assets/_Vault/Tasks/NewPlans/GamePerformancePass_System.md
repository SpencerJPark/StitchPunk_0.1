# Game Performance Pass — Design Spec (PP)

> **Status:** 🟡 specced 2026-10-19, not built. Comes from a read-only review where every finding was checked against the code. False alarms are recorded in §6 so nobody re-checks them.
> **Read first:** `Memories/Code/Systems.md`, `Memories/Code/Systems_AI.md`, `Memories/Code/Gotchas.md`, and [`../Plans/CrowdScaleAwareness_System.md`](../Plans/CrowdScaleAwareness_System.md) (PP-P5 builds it).
> **Decisions:** all settled below (PP-D1–PP-D8) under the delegated-architecture rule. Do not re-ask. The owner is only needed for PP-P0 and the Play-mode checks.

---

## 1. Purpose & scope

Nobody has profiled the game. This pass takes a baseline, removes per-frame work that is wasted for certain, then makes AI awareness scale with crowd size. Bigger changes only happen if the profile shows they are worth it.

**In scope:** game simulation systems under `Assets/_Scripts/Systems/` and unit SO assets.
**Out of scope:** the animation/movement toolkit packages, rendering/URP, the Editor tools, and adding Burst to systems that only run on events (§6).

---

## 2. Verified findings (2026-10-19 — re-verify line numbers before editing)

| # | Finding | Evidence | Phase |
|---|---|---|---|
| F1 | No profile exists. `Memories/Code/Performance.md` does not exist. | vault grep | P0 |
| F2 | Zombie units scan awareness every frame. `ThinkScheduleSystem` throttles units by `ThinkRate.interval`, but `UnitSO.thinkInterval` defaults to `0` (= every frame). Only `Bandit.asset` and `MaleCitizen.asset` set it (`0.25`). `PlayerZombie`, `PlayerZombieBrute`, `PlayerZombieRunner` and `Rotter` scan every frame. | `ThinkScheduleSystem.cs:33`, `UnitSO.cs:27`, `UnitBakingUtil.cs:19` | P1 |
| F3 | Waypoints are static (they bake `LocalTransform` since 786e23fb), yet `WaypointRegistrationSystem` rebuilds `waypointCells` **every frame** and then calls `.Complete()`. `InteractionSpatialHashSystem.OnUpdate` also clears `waypointCells` just before `WaypointRegistrationSystem` clears it again. | `WaypointRegistrationSystem.cs:22-30`, `InteractionSpatialHashSystem.cs:43` | P2 |
| F4 | `CorpseCellSystem` rebuilds the `CorpseCells` map on the main thread every frame and **nothing reads it**. No reference to `CorpseCells` or `AddJobHandleForReader` exists outside its own file, in `Assets/` or `Packages/`. | grep | P3 |
| F5 | `RagdollLaunchInitSystem` (main thread, not Burst-compiled) visits **every** `Dead` corpse every frame and skips the ones already ragdolling with `IsComponentEnabled`. Cost grows with the number of corpses. | `RagdollLaunchInitSystem.cs:35-44` | P4 |
| F6 | `EnemyAwarenessSystem`, `SocialAwarenessSystem` and `AllyAlertSystem` loop over the whole-faction `FactionRegistry` multimap, so cost is units × faction size. `FactionRegistrySystem` rebuilds that registry on the main thread every frame and is already marked `// to be deleted`. | `EnemyAwarenessSystem.cs:105`, `SocialAwarenessSystem.cs:103`, `AllyAlertSystem.cs:124`, `FactionRegistrySystem.cs:51` | P5 |
| F7 | `DamageResolutionSystem` calls `state.Dependency.Complete()` every frame, **before** it knows whether any damage event exists. The underlying cause is that `DamageEventSystem` drains `resolved` on the main thread. | `DamageResolutionSystem.cs:48`, `DamageEventSystem.cs:73` | P6 (gated) |
| F8 | `InteractionSpatialHashSystem` calls `.Complete()` every frame. **This is a correctness guard:** the maps live in a singleton, which ECS does not track for job dependencies, and the awareness jobs read them. Leave it alone unless the profile shows it matters. | `InteractionSpatialHashSystem.cs:61` | P6 (gated) |

---

## 3. Decisions (settled)

- **PP-D1 — Baseline first.** P0 runs before any code phase, so each later phase can be compared with real numbers. P1–P4 do not depend on the result, so a Sonnet session may build them before P0 if the owner is not available. P5–P6 wait for P0.
- **PP-D2 — Zombie think interval = `0.2`.** It is a little faster than Bandit's `0.25` because zombies are the player's combat units. Response time is still covered: `ThinkScheduleSystem` re-thinks the moment a unit goes idle, and `AllyAlert` resets `timeUntilNextThink` to 0. `Player.asset` is excluded because the player does not run the utility brain.
- **PP-D3 — Waypoint map rebuilds only on structural change.** Use `state.EntityManager.GetComponentOrderVersion<NavigationWaypoint>()` (verified at `Library/PackageCache/com.unity.entities@*/Unity.Entities/EntityManager.cs:4241`). Store the last version in a system field and rebuild only when the version changes. Subscene streaming adds or removes waypoints, which bumps the order version, so that case is covered.
- **PP-D4 — Remove the duplicate clear, not the owner.** Delete `waypointCells.Clear()` from `InteractionSpatialHashSystem`. `WaypointRegistrationSystem` owns that map.
- **PP-D5 — Turn off `CorpseCellSystem`, don't delete it.** Add `[DisableAutoCreation]` with a one-line comment pointing here. Its header describes a planned use (corpse stacking, pending a ragdoll play-test), so the code stays.
- **PP-D6 — Ragdoll launch query filters on the enabled bit.** Replace `.WithPresent<RagdollActor>()` with `.WithDisabled<RagdollActor>()`. Drop the `HasComponent` and `IsComponentEnabled` checks inside the loop, because the query now guarantees both. Already-ragdolling corpses drop out at the chunk level.
- **PP-D7 — CrowdScaleAwareness uses one combined `unitCells` map**, which is that spec's own recommendation. It also covers the `AllyAlertSystem` broadcast and `MinionStandingOrderSystem`, so `FactionRegistrySystem` can then be deleted. AF-P6 (perception cache) absorbs this work. If AF-P6 is specced first, P5 is built as its first phase instead.
- **PP-D8 — The damage drain rework is gated on the profile.** P6 only goes ahead if the P0/P4 profile shows the wait at `DamageResolutionSystem.cs:48` or `InteractionSpatialHashSystem.cs:61` taking more than ~0.3 ms of main-thread time in the camp scene. Otherwise record "measured, not worth it" in `Performance.md` and close the phase.

---

## 4. Phases

Each task names its files. **[parallel-safe]** means its files are separate from every other task in the same phase. Run the compile gate after every `.cs` task: `python Tools/devloop/devloop.py gate StitchPunk.Systems` (or the assembly that owns the file). Lint: `python Tools/devloop/devloop.py lint`.

### PP-P0 — Baseline profile *(owner, Play mode)*

- **T0.1** In `Assets/Scenes/Game.unity`, at the bandit camp, with at least 20 zombies and 10 bandits in combat, capture 300 frames in the Unity Profiler (Timeline + Hierarchy, deep profiling **off**).
- **T0.2** Record in a new note, `Assets/_Vault/Memories/Code/Performance.md`: the scene setup, unit counts, total frame ms, the top 10 systems by ms, and the main-thread wait time for `DamageResolutionSystem`, `InteractionSpatialHashSystem`, `WaypointRegistrationSystem`, `FactionRegistrySystem` and `CorpseCellSystem`. Also record the `BodyPart` archetype's chunk capacity from the Entities Archetypes window (the open question in §5).
- **Checkpoint C0:** the numbers are written down. Keep the note to measurements and traps. No lists of systems or files (they go stale).

### PP-P1 — Zombie think rate *(asset only)*

- **T1.1** [parallel-safe] Set `thinkInterval: 0.2` in `Assets/ScriptableObjects/Units/PlayerZombie.asset`, `PlayerZombieBrute.asset`, `PlayerZombieRunner.asset` and `Rotter.asset`. Set it through the Editor inspector or by editing the YAML field. It sits next to the existing fields, as in `Bandit.asset:21`.
- **Verify:** rebake (re-enter Play mode). Zombies still pick up targets, join an ally alarm and switch targets after a kill without a visible pause.

### PP-P2 — Static waypoint map

- **T2.1** [parallel-safe] `Assets/_Scripts/Systems/GameManagerSystemGroup/WaypointRegistrationSystem.cs`: add `private int lastWaypointOrderVersion;`, set it to `-1` in `OnCreate`, and wrap the Clear/schedule/Complete in `if (orderVersion != lastWaypointOrderVersion)` (PP-D3). On frames where nothing changed, the system makes no jobs and does no wait.
- **T2.2** [parallel-safe] `Assets/_Scripts/Systems/GameManagerSystemGroup/InteractionSpatialHashSystem.cs`: delete the `waypointCells.Clear()` line (PP-D4). Leave the creation and disposal of `waypointCells` here.
- **Trap:** with T2.1 in place, T2.2 is **required**. Otherwise `InteractionSpatialHashSystem` empties the map every frame and nothing rebuilds it, and `NavigationAwarenessSystem` quietly finds no waypoints.
- **Verify:** in Play mode, citizens and guards still walk their waypoint routes. The Profiler shows `WaypointRegistrationSystem` close to 0 ms after the first frame.

### PP-P3 — Turn off the unused corpse map

- **T3.1** [parallel-safe] `Assets/_Scripts/Systems/GameManagerSystemGroup/CorpseCellSystem.cs`: add `[DisableAutoCreation]` and the comment `// Off until a reader exists — see Tasks/NewPlans/GamePerformancePass_System.md PP-D5.` Before doing this, re-grep `CorpseCells|CorpseCellSystem` across `Assets/` and `Packages/`. If a reader has appeared since 2026-10-19, skip this task and note it.
- **Verify:** compile gate passes. In Play mode, deaths still ragdoll and corpses still settle.

### PP-P4 — Ragdoll launch query

- **T4.1** [parallel-safe] `Assets/_Scripts/Systems/HealthSystemGroup/RagdollLaunchInitSystem.cs`: apply PP-D6. Do not change how the launch impulse or torque is calculated.
- **Verify:** in Play mode, a kill still launches the body in the direction the hit came from. A revived unit (`RagdollReviveSystem` disables `RagdollActor`) still ragdolls again on its next death.
- **Checkpoint C1 (owner):** one Play-mode pass covering P1–P4, then re-profile with the P0 setup and add a "after P1–P4" column to `Performance.md`.

### PP-P5 — Crowd-scale awareness *(after C1)*

- Build [`../Plans/CrowdScaleAwareness_System.md`](../Plans/CrowdScaleAwareness_System.md) phases 1–3 as written, with PP-D7 settling its ← DECISION, then add:
- **T5.x** Convert `RaiseAlarmBroadcastJob` in `AllyAlertSystem.cs` and `MinionStandingOrderSystem.cs` to `unitCells`.
- **T5.y** Once nothing reads `FactionRegistry` (grep `FactionRegistry` returns only its own declaration and system), delete `FactionRegistrySystem.cs` and the `FactionRegistry` component.
- **Regression check (that spec's own):** in a small scene, the same targets are picked before and after the change.
- **Checkpoint C2:** re-profile and add a column to `Performance.md`.

### PP-P6 — Main-thread waits *(gated on PP-D8)*

Only if the C1/C2 profile clears the PP-D8 threshold. Then write a short follow-up spec, because the options touch the combat contract:
- move `DamageEventSystem`'s drain into a job and replace the `Complete()` with the bus's handle pattern, or
- give `SpatialHashRegistry` a reader-handle pair (the `CorpseCellSystem.AddJobHandleForReader` pattern) so `InteractionSpatialHashSystem` stops waiting.

Read `Contracts.md` before either option.

---

## 5. Open, measure-only

- **`BodyPart` `[InternalBufferCapacity(32)]`** (`BodyPartComponents.cs:14`): 32 × (Entity + `AnimationTarget` + `UnitPartId` + flags) is about 512+ bytes in-chunk per character. Change it only if the P0 archetype view shows few characters per chunk. Lowering it moves overflow to the heap, so pick the new capacity from the real maximum part count per rig.

## 6. Checked and dismissed — do not re-check

- `DamageBusSystem.cs:51` `producerHandle.Complete()` waits on **last** frame's producers, which have already finished. It costs nothing.
- `CorpseCellSystem` reader waits: no reader has ever registered, so the handle is always empty. (P3 turns the whole system off.)
- `DespawnSystem.cs:46`: only waits when there is something to despawn.
- `InteractionHighlightSystem.cs:116`: only runs when the player's interaction target changes.
- `PlayerUnequipSystem`: returns at line 33 unless the player drops or throws.
- Runtime systems without Burst (`UnitSpawnerSystem`, `BodyPartInitSystem`, `ItemAttachSystem`, `AiThrowItemSystem`, `RagdollReviveSystem`, `PlayerKnockoutSystem`, `WorkstationCraftSystem`, `Workstation*`, `PlayerMinionSpawnInitSystem`): all only do work on an event or input, so a query that matches nothing is cheap. The exception is F5 (P4).

## 7. Separate, not performance

Several systems call `RequireForUpdate<GameSceneTag>()` themselves (`SocialAwarenessSystem`, `AllyAlertSystem`, `FactionRegistrySystem`, `InteractionSpatialHashSystem`, and others), which breaks the group-level gating rule in CLAUDE.md. That is a conventions clean-up and does not belong in this pass.

## 8. File manifest

**Edited:** `WaypointRegistrationSystem.cs`, `InteractionSpatialHashSystem.cs`, `CorpseCellSystem.cs`, `RagdollLaunchInitSystem.cs`, four unit `.asset` files. P5 adds the files listed in CrowdScaleAwareness, plus `AllyAlertSystem.cs` and `MinionStandingOrderSystem.cs`.
**Deleted (P5):** `FactionRegistrySystem.cs`, `FactionRegistry` component.
**New:** `Assets/_Vault/Memories/Code/Performance.md`.
