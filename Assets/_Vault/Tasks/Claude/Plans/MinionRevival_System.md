# Minion Revival & Life-State System — Design Spec

> **Status:** ✅ spec ready · edit the inline **← DECISION** markers, then hand back to start the build.
> **Raw source:** [`../futureneedsplan.md`](../futureneedsplan.md) → "finish minion systems" / "add change from human to zombie" / "SaveSystem (minions are unique)"

## Context

Today reviving a dead human just flips life-state flags and re-enables the **same** brain — the corpse stands back up still "thinking" like a citizen, on its old faction, swinging its old attacks. This pass turns revival into a proper **human → player-controlled zombie minion** conversion, and cleans up the life-state model that revival rides on.

Two coupled goals:
1. **Deprecate `Alive`.** `Dead` becomes the *sole* life-state enableable: `Dead` enabled = dead, `Dead` disabled = alive. `Alive` is deleted everywhere.
2. **Revive → zombie minion.** Reviving a corpse activates the dormant `SwapBrainRequest` hook to rebuild the unit as its authored zombie form (`PlayerZombie`): swap brain unitType, faction, attack factions, available attacks, and motivations from the enum-indexed `UnitLibrary` blob; enable `Minion`/`Undead`; fire an interrupt so the new brain re-enters action selection. The unit is then selectable and commandable through the **existing** minion control pipeline (box-select, move/attack/interact/follow) with no changes to that pipeline.

**Out of scope (explicit):** new control modes (direct possession / god-mode flyover), polishing the unfinished `Defend` command, and the visual **skin/texture change** to a zombie look — that is owned by the [Unit Design](UnitDesign_System.md) system. The hook is built there as `ChangeDesignRequest` (§5b): once both systems land, `SwapBrainSystem` fills `ChangeDesignRequest.changes` with the explicit zombie skin indices and enables it during conversion, and the new look persists via `PersistedDesign`. This spec does the brain swap only — the re-skin layers in without touching it.

---

**Skills Needed** (project skills in `.claude/skills/` — see [Skills index](../../../Memories/Code/Skills.md)):
- `dots-system-scaffold` — the new `SwapBrainSystem` (ISystem + IJobEntity in `HealthSystemGroup`) (§5)
- `dots-blob-library` — *conventions only*: extend the existing `UnitLibrary` (`UnitDataBlob`) with `becomesUnitType` (a field add to a working blob pipeline, not a new library) (§4)

*(No `dots-authoring-baker` — `HealthAuthoring` is a one-line edit and the rest is prefab/SO wiring in the Editor. No `dots-unit-ai` — no new ActionType/awareness; the conversion drives the existing brain by swapping `unitType`.)*

---

## 1. Purpose & v1 scope

Make revival produce a faithful, controllable zombie minion, and make `Dead` the single life-state truth.

**v1 handles:**
- **`Alive` deprecation** — delete the `Alive` component; every read/write migrates to `Dead` (inverted). `DeathSystem`'s "first death frame" latch moves from `Alive` to a `UnitAction.current == ActionType.Death` guard.
- **`becomesUnitType`** authored on `UnitSO`, baked into `UnitDataBlob`, so each human declares its zombie form (citizens → `PlayerZombie`).
- **`SwapBrainSystem`** (activates the dormant `SwapBrainRequest`): rebuilds `UtilityBrain.unitType`, `UnitData.unitType`, `Faction`, the `AttackFaction` / `AvailableAttack` / `Motivation` buffers from the target unit's `UnitDataBlob` entry, fires `ActionInterruptRequest`, disables itself.
- **`ReviveRequestSystem`** becomes the conversion entry: on a revive it reads the corpse's `becomesUnitType` from the blob, stamps + enables `SwapBrainRequest`, enables `Undead`/`Minion`, resets `UnitAction.current` to `Idle`. The revived unit is now selectable/commandable through the existing pipeline.
- **Pure flag-flip control wiring** — revivable human prefabs are pre-baked (disabled) with `MinionAuthoring` + `UndeadAuthoring` + `canBePlayerControlled = true`, so revival enables existing components with **zero structural changes**.

**Out of v1:** design/texture re-roll (→ [Unit Design](UnitDesign_System.md)); feral (non-player) `Rotten` conversion — the data path supports it (a different `becomesUnitType` + faction) but only the player-reviver path is built; `Defend` command consumption; possession/god-mode camera.

## 2. Architecture

Pure ECS, no MonoBehaviour bridge. All conversion runs in the existing **`HealthSystemGroup`**, where death and revival already live. The control surface (selection, commands) is untouched MonoBehaviour + `MinionActionSelectionSystemGroup` code that already works.

The conversion deliberately reuses **three** existing, partially-dormant mechanisms rather than inventing new ones:
- `SwapBrainRequest { UnitType newUnit }` — already baked **disabled** on every unit by `UnitBakingUtil.BakeRequirements`, currently **unconsumed**. This pass writes its first consumer.
- `UnitLibrary` / `UnitDataBlob` — already enum-indexed by `UnitType` (`FindByUnitType`) and already holds `factionType`, `attackFactions`, `socialFactions`, `motivation`/`randomMotivations`, `attacks`. Everything the swap needs is already there except `becomesUnitType`.
- `ActionInterruptRequest` — the established single teardown path; firing it makes the new brain re-enter selection cleanly (same path revive already used).

```
HealthSystemGroup, per frame:
  DamageApplicationSystem (CombatReactionSystemGroup, earlier) ─ enables Dead at health≤0
  DeathSystem ───────────────── first-death-frame work (latch = UnitAction.current==Death)
  ReviveRequestSystem ───────── on ReviveRequest: heal, Dead→off, Undead/Minion→on,
        │                       UnitAction→Idle, stamp+enable SwapBrainRequest{newUnit},
        │                       re-enable UtilityBrain, fire ActionInterruptRequest
        ▼
  SwapBrainSystem (NEW, UpdateAfter ReviveRequestSystem) ── consume SwapBrainRequest:
        rebuild unitType / Faction / AttackFaction / AvailableAttack / Motivation
        from UnitDataBlob[newUnit]; disable SwapBrainRequest
```

The revived unit becomes a `Minion` (selectable); `PlayerUnitBrain` stays **disabled** until the player issues the first command (existing `UnitSelectionManager` behavior — it enables `PlayerUnitBrain` on command). Until then the zombie runs its autonomous `PlayerZombie` brain.

**← DECISION:** does `SwapBrainSystem` run same-frame after `ReviveRequestSystem` (chosen here — one-frame conversion) or is a one-frame delay acceptable? Same-frame requires the `UnitLibrary` singleton present in `HealthSystemGroup` (it is — baked globally).

## 3. Entry points

- **Request (one-shot, on entity) — revival:** `ReviveRequest : IEnableableComponent` (existing). Enabled by `PlayerReviverSystem` when the player's `OnPlayerReviverEquip` fires on a corpse `Target`. Unchanged trigger; `ReviveRequestSystem` gains the conversion bootstrapping.
- **Request (one-shot, on entity) — brain swap:** `SwapBrainRequest { UnitType newUnit } : IEnableableComponent` (existing, dormant). Now consumed by `SwapBrainSystem`. Generic: any future system (feral turn, take-direct-control, debug) can stamp + enable it to re-key a unit's brain.
- **Config (persistent, in blob):** `UnitDataBlob.becomesUnitType` — the authored zombie form per human, read once per conversion.
- **State flags (persistent, on entity):** `Dead` (sole life-state), `Undead`, `Minion` — all baked, toggled by enable/disable only.

## 4. Data model

No new library. One field added to the existing `UnitLibrary` pipeline + the life-state component removed.

```csharp
// UnitSO.cs — new authored field (defaults to None = "does not convert").
[SearchableEnum] public UnitType becomesUnitType;

// UnitBlob.cs (UnitDataBlob) — new blob field, baked by UnitLibraryBakingSystem.
public UnitType becomesUnitType;

// UnitComponents.cs — DELETE this struct entirely.
// public struct Alive : IComponentData, IEnableableComponent { }
```

`UnitLibraryBakingSystem.CreateUnitLibraryBlob` copies `unitsArray[i].becomesUnitType = unitSO.becomesUnitType;` alongside the existing field copies (one line, no `BlobBuilder` allocation — it's a scalar).

**Motivation rebuild note:** `UnitDataBlob` already carries `motivation[]`, `randomMotivations[]`, `randomMotivationAmount`. `UnitBakingUtil` already exposes a Burst-friendly `PopulateRandomBehaviours(DynamicBuffer<Motivation>, ref BlobArray<NeedType>, int amount, ref Random)` — `SwapBrainSystem` reuses it to refill the buffer. **Gap:** per-need `decayRate` is **not** in `UnitDataBlob` (only on `UnitSO.motivationDecayRates`). Rebuilt zombie motivations will therefore default to `decayRate = 0` (needs don't decay → stay satisfied → zombie is driven by combat/wander, not needs). For a `PlayerZombie` that is acceptable. ← DECISION: accept zero-decay zombie needs (recommended), or add a `decayRate` array to `UnitDataBlob` so rebuilt motivations decay like baked ones.

**Persistence interaction (Save):** a revived minion is a `Minion` (an `IPersist` tag) and is covered by the existing `PersistentSaveSystem` query (`WithAll<Minion, UnitData, LocalTransform>.WithDisabled<Dead>()`). The swap writes `UnitData.unitType`, which save already snapshots — so a saved zombie restores as a zombie. ← DECISION: confirm `MinionRestoreApplySystem` re-applies `Faction`/`AttackFaction`/`AvailableAttack` for restored minions, or that those are re-derived on restore from `UnitData.unitType` (otherwise a loaded zombie could restore with citizen combat data). Cleanest: on restore, if `unitType` is a zombie form, stamp `SwapBrainRequest` so the same rebuild path runs. (Reserved hook — verify in Phase 5, build only if needed.)

## 5. Systems

### `SwapBrainSystem` (NEW)
`Assets/_Scripts/Systems/HealthSystemGroup/SwapBrainSystem.cs` — `[UpdateInGroup(typeof(HealthSystemGroup))]`, `[UpdateAfter(typeof(ReviveRequestSystem))]`, `RequireForUpdate<GameSceneTag>` + `RequireForUpdate<UnitDataLibrary>`.
- Query: units with `SwapBrainRequest` **enabled** + `UtilityBrain` + `UnitData` + `Faction` + the `AttackFaction`/`AvailableAttack`/`Motivation` buffers.
- For each: `int idx = blob.FindByUnitType(req.newUnit);` skip if `< 0`. Then from `blob.units[idx]`:
  - `utilityBrain.unitType = newUnit;` `unitData.unitType = newUnit;`
  - `faction.factionType = entry.factionType;`
  - clear + refill `AttackFaction` from `entry.attackFactions`.
  - clear + refill `AvailableAttack` from `entry.attacks` (action→attack mappings).
  - clear + refill `Motivation` from `entry.motivation` (+ `PopulateRandomBehaviours` for the random pool, seeded `Random` per entity — never seed 0).
  - disable `SwapBrainRequest` (one-shot consume).
- Buffer rewrites are all on the unit's own entity (no cross-entity writes) → `IJobEntity` + `.ScheduleParallel()`, Burst-compatible. **Never `.Run()`.**
- Does **not** fire the interrupt itself — `ReviveRequestSystem` already fires `ActionInterruptRequest` the same frame, and the interrupt's teardown reads the *new* brain when the execution group runs later. ← DECISION: if a non-revive caller stamps `SwapBrainRequest` without an interrupt, should `SwapBrainSystem` fire its own `ActionInterruptRequest`? (Recommended: yes, fire it here so the hook is self-contained; revive firing it too is idempotent.)

### `ReviveRequestSystem` (EDIT)
- `[WithDisabled(typeof(Alive))]` → `[WithPresent(typeof(Dead))]` with a `Dead`-enabled query (it should only fire on corpses), and drop the `EnabledRefRW<Alive>` param + `aliveEnabled.ValueRW = true;` line.
- After `deadEnabled.ValueRW = false;` add: read `becomesUnitType` from `UnitDataLibrary` by current `unitData.unitType`; if it resolves to a real zombie type, `SetComponent(SwapBrainRequest{ newUnit })` + `SetComponentEnabled<SwapBrainRequest>(entity, true)`; enable `Minion`; set `unitAction.current = ActionType.Idle` (clears the `Death` latch). Keep the existing `UtilityBrain` re-enable + `ActionInterruptRequest` fire.

### `DeathSystem` (EDIT)
- Drop `EnabledRefRW<Alive> aliveEnabled` from `DeathJob`.
- Replace the latch `if (!aliveEnabled.ValueRO) return;` with `if (unitAction.current == ActionType.Death) return;` (the job already sets `unitAction.current = ActionType.Death` on the first frame, so this self-latches). Remove the `aliveEnabled.ValueRW = false;` line. ← DECISION: confirm revive's `unitAction.current = Idle` reset (above) reliably clears this latch so a re-killed reanimated minion re-enters death — verify in Phase 4.

### `AttackRequestSystem` (EDIT)
- `victimAlive` currently = `aliveLookup.HasComponent(victim) && aliveLookup.IsComponentEnabled(victim)`. Migrate to a `Dead` lookup: `deadLookup.HasComponent(victim) && !deadLookup.IsComponentEnabled(victim)` (present-and-not-dead). Swap the `ComponentLookup<Alive>` field for `ComponentLookup<Dead>` (`[ReadOnly]`).

### `SpawnStateInitSystem` (EDIT)
- Drop the `Alive` lookup; in the per-spawn block, set `Dead` **disabled** instead of `Alive` enabled (units start alive). `Dead` is already in the lookup set — just ensure it's set false on spawn (it already does this).

## 6. MonoBehaviour bridge

No new bridge. One existing edit:
- **`UnitSelectionManager.HandleCommand` `isHostile` check** (lines 308–309): `HasComponent<Alive> && IsComponentEnabled<Alive>` → `HasComponent<Dead> && !IsComponentEnabled<Dead>` (present-and-not-dead). Same intent, inverted flag.

## 7. Integration points

- **Health / death (`DamageApplicationSystem`, `DeathSystem`, `Ragdoll2DReviveSystem`):** `DamageApplicationSystem` already enables `Dead` at health ≤ 0 (unchanged — it never wrote `Alive`). `Ragdoll2DReviveSystem`'s header comment ("disables Dead / enables Alive") updates to "disables Dead" only.
- **Revival (`PlayerReviverSystem`, `ReviveRequestSystem`):** trigger unchanged; `ReviveRequestSystem` gains conversion bootstrapping (§5).
- **AI brain (`UtilityBrain`, `BrainBlobUtils`, awareness/scoring):** purely `unitType`-keyed — swapping `UtilityBrain.unitType` to `PlayerZombie` immediately routes the unit through the zombie brain's actions; awareness entries for actions the zombie brain lacks are skipped (`GetActionDefIndex < 0`). No AI code changes.
- **Minion control (`UnitSelectionManager`, `MinionActionSelectionSystem`, `UnitBakingUtil.AddPlayerControlled`):** unchanged — a revived `Minion` with `PlayerUnitBrain`/`OnMinion*Command` baked (disabled) plugs straight in. Only the `isHostile` flag-flip above.
- **Combat targeting (`EnemyAwarenessSystem`, `Faction`, `AttackFaction`, `FactionRegistry`):** after the swap the unit's `Faction` + `AttackFaction` reflect `PlayerZombie`, so it targets humans and is targeted by them. ← DECISION: confirm `FactionRegistry` (the per-faction entity multihashmap) is rebuilt each frame from `Faction` (so the changed faction is picked up) — if it's baked once, the swap must also update the registry.
- **Save (`PersistentSaveSystem`, `MinionRestoreApplySystem`, `UnitData`):** see §4 persistence note — `unitType` rides the existing snapshot; restore-time combat-data re-derivation is a reserved hook.
- **Animation (`BillboardSystem`, `UnitData.unitType` → `UnitDataBlob` idle/move anims):** the zombie's idle/move animations come from its `UnitDataBlob` entry via `unitType`, so the swap also changes its resting animation set for free. `BillboardSystem`'s `Alive` comment updates to `Dead`.

## 8. Proposed file manifest

**New:**
- `Assets/_Scripts/Systems/HealthSystemGroup/SwapBrainSystem.cs` — consumes `SwapBrainRequest`, rebuilds brain/faction/buffers from `UnitDataBlob`.

**Edited (code):**
- `Assets/_Scripts/Components/Units/UnitComponents.cs` — **delete** `struct Alive`.
- `Assets/_Scripts/Authoring/Units/HealthAuthoring.cs` — remove the `Alive` add/enable; keep only `Dead` (disabled).
- `Assets/_Scripts/Systems/HealthSystemGroup/DeathSystem.cs` — latch → `UnitAction.current == Death`; drop `Alive`.
- `Assets/_Scripts/Systems/HealthSystemGroup/ReviveRequestSystem.cs` — `[WithAll(Dead)]`; drop `Alive` flip; stamp/enable `SwapBrainRequest`; enable `Minion`; reset `UnitAction`.
- `Assets/_Scripts/Systems/CombatSystemGroup/CombatExecutionSystemGroup/AttackRequestSystem.cs` — `victimAlive` via `Dead` lookup.
- `Assets/_Scripts/Systems/LateSimulationSystemGroup/SpawnInitSystemGroup/SpawnStateInitSystem.cs` — drop `Alive` lookup; set `Dead` false on spawn.
- `Assets/_Scripts/MonoBehaviours/Managers/UnitSelectionManager.cs` — `isHostile` via `Dead`.
- `Assets/_Scripts/Data/SOs/UnitSO.cs` — add `becomesUnitType`.
- `Assets/_Scripts/Data/Structs/UnitBlob.cs` — add `becomesUnitType` to `UnitDataBlob`.
- `Assets/_Scripts/Systems/PostBakingSystemGroup/UnitLibraryBakingSystem.cs` — bake `becomesUnitType`.
- `Assets/_Scripts/Systems/HealthSystemGroup/Ragdoll2DReviveSystem.cs`, `.../AnimationSystemGroup/.../BillboardSystem.cs` — comment-only `Alive`→`Dead` updates (+ clean up commented-out `Alive` blocks in `MinionSelfDefenceSystem`, `MinionAttackOrderSystem`, `MinionOrderExecutionSystem`, `PlayerAttackSystem`).

**Assets / Editor (Spencer):**
- Author a `PlayerZombie` `UnitSO` (faction Player/Undead, `attackFactions` = humans, `attacks` = bite, motivations) and add it to `_UnitLibrary`.
- On each revivable human `UnitSO`: set `becomesUnitType = PlayerZombie`.
- On each revivable human **prefab**: add `MinionAuthoring` + `UndeadAuthoring`, and ensure its `UnitSO.canBePlayerControlled = true` (so `Minion`/`Selected`/`PlayerUnitBrain`/`OnMinion*Command` bake disabled, ready to enable).

## 9. Build phases

1. **`Alive` deprecation** — delete the struct; migrate the 7 active code sites + comments; rebuild. Success: project compiles, units still die (ragdoll) and the existing revive still stands them up (even before conversion logic). This is a self-contained, shippable refactor.
2. **`becomesUnitType` data** — add the `UnitSO` field, `UnitDataBlob` field, and the baking line. Author `PlayerZombie` + set citizens' `becomesUnitType`. Confirm the blob entry resolves via `FindByUnitType`.
3. **`SwapBrainSystem`** — scaffold (`dots-system-scaffold`); rebuild faction/attack/motivation from the blob; consume `SwapBrainRequest`. Test by stamping `SwapBrainRequest` on a live unit via the Entities inspector → watch `UnitData.unitType`, `Faction`, `AttackFaction` change.
4. **Revive → conversion wiring** — `ReviveRequestSystem` stamps `SwapBrainRequest` + enables `Minion`. Revive a corpse in-scene → it stands as a `PlayerZombie`, becomes selectable, fights humans, and is commandable via the existing pipeline.
5. **Persistence round-trip** — save with a revived zombie minion, reload; confirm it restores as a zombie (and resolve the §4/§7 restore-time combat-data DECISION if it restores with stale citizen data).

## 10. Verification

Test in `DOTSTestScene`:
- **Phase 1:** kill a unit (ragdoll plays); select it in the Entities window — `Dead` enabled, no `Alive` component anywhere. Revive (existing reviver) — `Dead` disabled, unit animates again. No console errors; no double-processing of death (the `UnitAction.current == Death` latch holds).
- **Phase 3:** on a live citizen, set `SwapBrainRequest.newUnit = PlayerZombie` + enable it in the inspector. Next frame: `SwapBrainRequest` disabled, `UnitData.unitType`/`UtilityBrain.unitType = PlayerZombie`, `Faction` = the zombie faction, `AttackFaction` lists humans, `AvailableAttack` lists the bite, `Motivation` refilled.
- **Phase 4:** equip the reviver, target a corpse, revive. The corpse rises as a `PlayerZombie`: `Minion` enabled (box-select highlights it), `Undead` enabled, autonomous until commanded. Right-click a human → it paths in and bites. Right-click ground → it moves. Kill the reanimated zombie again → it ragdolls (death latch re-armed by the Phase-4 `UnitAction = Idle` reset).
- **Phase 5:** autosave/manual save with the zombie minion alive → reload → it returns as a zombie minion with correct combat data.
- **Editor-only (Spencer):** authoring the `PlayerZombie` `UnitSO` + `_UnitLibrary` entry, setting `becomesUnitType` per human, and adding `MinionAuthoring`/`UndeadAuthoring` + `canBePlayerControlled` to revivable prefabs.

## Open decisions (collected)
- [ ] §2/§5 — `SwapBrainSystem` runs same-frame after `ReviveRequestSystem` (chosen) vs a one-frame delay.
- [ ] §4 — accept zero-decay rebuilt zombie motivations (recommended) vs add a `decayRate` array to `UnitDataBlob`.
- [ ] §4/§7 — restore-time combat-data: re-derive `Faction`/`AttackFaction`/`AvailableAttack` for loaded zombie minions (e.g. re-stamp `SwapBrainRequest` on restore) vs trust the snapshot.
- [ ] §5 — `SwapBrainSystem` fires its own `ActionInterruptRequest` (recommended, self-contained) vs relies on the revive path firing it.
- [ ] §5 (`DeathSystem`) — confirm revive's `UnitAction.current = Idle` reliably re-arms the death latch for re-killed minions.
- [ ] §7 — confirm `FactionRegistry` is rebuilt per-frame from `Faction` so a swapped faction takes effect (else update the registry in the swap).
- [ ] §1 — `becomesUnitType = None` is the "does not convert" sentinel; confirm no human is accidentally left at `None` when it should convert.
