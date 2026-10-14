# Camp Loop — Workstation, Summoning, Revival Rounds, Squads, Gathering, Enemies

> **Status:** 🔨 Phases 0–2 built 2026-10-13 (play pass owed: `Verification/verify-camp-loop.md`); Phases 3–7 not started. Spec written 2026-10-13. Owner answered 15 questions in one session (CL-D1–CL-D15, §11); architecture calls made under the standing delegation (CL-A1–CL-A12). **Phases 1–2 authorized to build unattended**, no Play mode; phases 3–7 wait for the owner's play pass on 1–2.
> **Supersedes in part:** [`PlayerResource_System.md`](PlayerResource_System.md) becomes Phase 1 of this spec, with the amendments in §3. Read that spec for the ledger detail; this one only records what changed.
> **Design source:** owner brief 2026-10-13 (below) + `Spencer/Design_Decisions.md` → RTS Mode, Caravan.

## Context

The owner's pitch, 2026-10-13: you drive a caravan around a wasteland that looks like the English countryside. You can post up anywhere, pull resources from around you, and raise an army of the dead to fight other armies of the dead, bandits and whatever else is out there. Your character can run around and fight, but is very weak, so the game pushes you toward playing it like an RTS even though it is an RPG.

The September audit's verdict holds: *the game has no loop*. This spec is the loop: **post up → gather → raise → fight**.

What already exists and is reused, not rebuilt:
- **Control Units mode:** the `ControlUnits` action map plus the `CinemachineCameraType.ControlUnits` camera, switched in `PlayerInputManager.ApplyActionMapSwitch` (`PlayerInputManager.cs:108`). Today it toggles anywhere on Tab (`OnSwitchControls`).
- **Selection and orders:** `UnitSelectionManager` (click and box select; Move, Attack, Interact, Defend, Follow, Stop, Return; groups via `PlayerMinionGroupsData` + four `PlayerHordeSlot` horde entities; formations). Unplayed: `Verification/verify-minion-orders.md` is open.
- **Revival:** `PlayerReviverSystem` enables `ReviveRequest`, and `ReviveJob` turns a corpse into its `becomesUnitType` and hands it to the player (`Minion` + `PlayerUnitBrain`).
- **Spawning:** `UnitSpawnerSystem` (pool reclaim + instantiate from `UnitPrefabEntry`), then `SpawnStateInitSystem` resets enable bits on `NewlySpawned`. It deliberately spawns **out of camera view**, which is wrong for a summon. See CL-A6.
- **Interaction:** `PlayerTargetingSystem` writes the nearest `PlayerInteractable` into the player's `Target` (5 m). `OnInteractPlayerInput` (E) is consumed by `DialogueStartSystem` and `PlayerPickupSystem`.

**Skills Needed:** `dots-feature-group` (Phase 0 groups), `dots-system-scaffold` (every system), `dots-authoring-baker` (workstation, player bake), `dots-test` (PlayMode fixtures). No `dots-blob-library`: costs ride `UnitDataBlob` and the workstation's own baked component.

---

## 1. Phases

| Phase | What | Status |
|---|---|---|
| 0 | Stage: groups, enums, order tests | build now |
| 1 | **Player resources** (the `PlayerResource_System.md` ledger + HUD) amended: field revive costs a **revival round** | build now |
| 2 | **Workstation**: deploy with a ghost preview, pack up, mount → Control Units, furnace (wood → electricity), craft rounds, repair, summon three unit types, placeholder station menu, station destroyed = lose, playtest scene | build now |
| 3 | **Squads + on-foot command**: Red/Blue squads (two unlocked), cycle the active squad, quick commands, command radius on foot, new summons join the active squad | building 2026-10-13 (CL-D16–D19, CL-A13) |
| 4 | **Gathering**: tree and scrap nodes, harvest → carry → deposit at the workstation, corpse hauling | after 3 |
| 5 | **Bandit camp**: armed humans guarding a camp; their corpses feed field revives | after 4 |
| 6 | **Raid on the station**: periodic bandit wave targeting the workstation; station takes damage | after 5 |
| 7 | **Rival corpse engineer**: an enemy workstation that spawns zombie waves; destroy it and its engineer flees or fights | after 6 |

Phases 3–7 are sketched in §8 so the Phase 1–2 data model does not paint them into a corner. They get their own build plan when they start.

## 2. Architecture

```
PlayerSystemGroup
  PlayerInputSystemGroup        (existing) targeting, equipment input
  WorkstationInputSystemGroup   (NEW, after PlayerInputSystemGroup, before NarrativeSystemGroup)
      WorkstationPlacementSystem   preview position + validity → PlayerWorkstationPlacement
      WorkstationDeploySystem      E while previewing → instantiate the workstation prefab
      WorkstationMountSystem       E on own workstation → mount; dismount request / damage → dismount
  PlayerEquipmentSystemGroup    (existing) PlayerReviverSystem: field revive spends 1 RevivalRound
BuildingsSystemGroup            (existing, empty today; after Movement, before PlayerResource)
      WorkstationFurnaceSystem     wood → electricity on a timer
      WorkstationCraftSystem       CraftRevivalRoundRequest → spend → +RevivalRound
      WorkstationRepairSystem      repair request → spend wood → +Health
      WorkstationSummonSystem      summon requests → spend → queue → tick → PendingMinionSpawn
      WorkstationPackUpSystem      pack-up request → dismount + destroy entity (ledger lives on Player, so it is kept)
      WorkstationDestroyedSystem   Health ≤ 0 → CampLoopGameOver singleton enabled
PlayerResourceSystemGroup       (NEW per PR-D2, after Buildings, before Combat)
      ResourceChangeSystem         apply deltas, clamp, clear
LateSim: SpawnSystemGroup        WorkstationSummonSpawnSystem: PendingMinionSpawn → instantiate at the spawn point + SpawnAsPlayerMinion
         SpawnInitSystemGroup    PlayerMinionSpawnInitSystem (after SpawnStateInitSystem): minion bits on
         SaveSystemGroup         ResourceSaveSync / ResourceLoadSync (Phase 1)
MonoBehaviours (read ECS, write requests): PlayerInputManager (mount → map switch), WorkstationGhostPresenter,
                                           WorkstationMenuUI + ResourceHudUI (UI Toolkit), DebugResourceMenu
```

**Entry patterns (CL-A1):** every request is an **enableable component or buffer on the entity it acts on**: on the `Player` for placement and mount, and on the workstation for craft, repair, summon and pack-up. There is one player and at most one workstation, both well-known, so a buffer beats spawning one-frame signal entities. This matches `ResourceChangeRequest` and the `OnMinion*Command` family.

**CL-A2: the new group.** `WorkstationInputSystemGroup : ComponentSystemGroup`, nested in `PlayerSystemGroup`, `[UpdateAfter(typeof(PlayerInputSystemGroup))] [UpdateBefore(typeof(NarrativeSystemGroup))]`. Reason: the workstation systems must see `Target` (written in `PlayerInputSystemGroup`) and consume E **before** `DialogueStartSystem` and `PlayerPickupSystem` do. The workstation is never a dialogue or item target, so those systems would ignore it anyway, but placement-confirm E must not also open a nearby conversation. Folder: `Systems/PlayerSystemGroup/WorkstationInputSystemGroup/`.

**CL-A3: the economy goes in `BuildingsSystemGroup`.** The workstation is a building, the group is empty and correctly ordered before `PlayerResourceSystemGroup`, so a spend queued in frame N lands in frame N. No new top-level group.

## 3. Phase 1 — Player resources (amendments to `PlayerResource_System.md`)

Build `PlayerResource_System.md` §4–§10 as written, **except**:

| Change | Was | Now | Why |
|---|---|---|---|
| CL-P1 | `ResourceType { None, Wood, ScrapMetal, Corpse, Electricity, Currency }` | append `RevivalRound` (value 6) | CL-D4: field revives consume a crafted round. A ledger row means HUD, save and spend all come free. |
| CL-P2 | `PlayerResourcesSnapshot` five ints | six ints (`revivalRound` added) | follows CL-P1 |
| CL-P3 | field revive charges the undead form's scrap + electricity (PR-D6/PR-D7) | field revive charges **1 `RevivalRound`**, and only when the corpse has a `becomesUnitType` (humans → `PlayerZombie`). `TrySpend` gains a single-resource overload `TrySpendSingle(ref stack, ref requests, ResourceType, int amount, double elapsedTime, ref shortfall, shortfallEnabled)`. The gate stays in `PlayerReviverSystem` (PR-D7 holds). | CL-D4. `SummonCost` is still authored on `UnitSO` and baked to `UnitDataBlob.summonCost`; the **workstation** charges it (Phase 2), with corpses included. |
| CL-P4 | HUD = Rive with a TMP fallback (PR-D9) | **UI Toolkit placeholder** `ResourceHudUI` (one row, six labels, shortfall flashes the short label red for 1 s), built so a Rive artboard can replace it later | CL-D11: functional placeholder now, Rive later |
| CL-P5 | HUD ← DECISIONs (placement, sound, starting ledger) | top-left single row, Bodies first, labelled "Bodies"; flash only, no sound; starting ledger `Wood 10, ScrapMetal 5, Corpse 2, Electricity 5, Currency 0, RevivalRound 3` | owner chose the recommended options throughout; corpse 2 + 3 rounds so both summon paths are testable on frame one |
| CL-P6 | `chargeCorpses` flag on `TrySpend` | kept; the workstation passes `true` | — |

PR-D10 (wood burning) and PR-D11 (corpse collection) were "out of v1". Burning is Phase 2 (`WorkstationFurnaceSystem`); corpse hauling is Phase 4.

## 4. Phase 2 — Data model

**`Assets/_Scripts/Components/Buildings/WorkstationComponents.cs`** (new):
```csharp
// On the workstation entity.
public struct Workstation : IComponentData
{
    public Entity ownerPlayer;          // set at deploy
    public float3 mountLocalOffset;     // where the player stands while mounted
    public float3 summonSpawnLocalOffset;
}
public struct WorkstationFurnace : IComponentData
{
    public bool  isBurning;             // menu toggle, default true
    public float burnIntervalSeconds;   // default 5
    public int   electricityPerWood;    // default 2
    public float burnTimer;
}
public struct WorkstationRecipes : IComponentData   // tunables, baked from WorkstationAuthoring
{
    public int   revivalRoundScrapCost;        // default 1
    public int   revivalRoundElectricityCost;  // default 1
    public int   repairWoodCost;               // default 2
    public int   repairHealthAmount;           // default 25
}
// Buttons in the station menu append to / enable these.
public struct WorkstationSummonRequest : IBufferElementData { public UnitType unitType; }
public struct WorkstationSummonQueueEntry : IBufferElementData { public UnitType unitType; public float remainingSeconds; }
public struct PendingMinionSpawn : IBufferElementData { public UnitType unitType; public float3 position; }
public struct CraftRevivalRoundRequest : IComponentData, IEnableableComponent { public int count; }
public struct WorkstationRepairRequest : IComponentData, IEnableableComponent { }
public struct WorkstationPackUpRequest : IComponentData, IEnableableComponent { }
public struct WorkstationFurnaceToggleRequest : IComponentData, IEnableableComponent { }
// The summon menu: which unit types this station offers. Baked from WorkstationAuthoring.summonableUnits.
public struct WorkstationSummonOption : IBufferElementData { public UnitType unitType; public float buildSeconds; }
```
The station also bakes `Health` (`healthAmountMax` 200), `Faction { Player }` and `PlayerInteractable` (enabled, so `PlayerTargetingSystem` finds it). Damage intake (whatever the damage bus needs on a non-unit) is **Phase 6**. Phase 2 only reads `Health` for repair and for the lose condition, and `DebugResourceMenu` gains a "damage station 50" button so the lose path is testable.

**`Assets/_Scripts/Components/Player/PlayerWorkstationComponents.cs`** (new, on `Player`):
```csharp
public struct WorkstationPrefabReference : IComponentData { public Entity prefab; }  // baked from PlayerControllerAuthoring
public struct PlayerWorkstationLink : IComponentData { public Entity workstation; } // Entity.Null = none deployed
public struct OnDeployWorkstationInput : IComponentData, IEnableableComponent { }   // B key: toggles placement preview
public struct PlayerWorkstationPlacement : IComponentData, IEnableableComponent      // enabled = previewing
{
    public float3 position;
    public bool   isValid;
}
public struct WorkstationMounted : IComponentData, IEnableableComponent             // enabled = mounted
{
    public Entity workstation;
    public int    healthWhenMounted;    // dismount when the player's Health drops below this
}
public struct WorkstationDismountRequest : IComponentData, IEnableableComponent { } // Tab while mounted, or the menu
// Singleton entity, baked by the playtest scene's CampLoopRulesAuthoring.
public struct CampLoopGameOver : IComponentData, IEnableableComponent { }
```
**`SpawnAsPlayerMinion`** (tag, `Components/Spawners/SpawnerComponents.cs`): added by the summon spawner, consumed and removed by `PlayerMinionSpawnInitSystem`.

**Unit roster (CL-D12).** Append `PlayerZombieBrute` and `PlayerZombieRunner` to `UnitType` (append only; the serialized ints of existing assets must not shift). Two new `UnitSO` assets are duplicated from `PlayerZombie.asset` with the same prefab and actor profile:

| Unit | maxHealth | moveSpeed / runSpeed | summonCost (corpse / scrap / electricity) | buildSeconds |
|---|---|---|---|---|
| PlayerZombie (basic) | as authored | as authored | 1 / 1 / 1 | 4 |
| PlayerZombieBrute | ×2 | ×0.7 | 1 / 3 / 2 | 8 |
| PlayerZombieRunner | ×0.6 | ×1.5 | 1 / 1 / 2 | 3 |

The library that feeds `UnitPrefabEntry` must list both. T0 finds the list.

## 5. Phase 2 — Systems

| System | Group / folder | Behaviour |
|---|---|---|
| `WorkstationPlacementSystem` | `WorkstationInputSystemGroup` | `OnDeployWorkstationInput` toggles `PlayerWorkstationPlacement` (refused with a log line if `PlayerWorkstationLink.workstation` exists or the player is mounted). While enabled, the candidate is 3 m along the player's facing; a `PhysicsWorldSingleton` raycast from 5 m above to 5 m below snaps it to ground. `isValid` = the ray hit **and** the hit normal's y ≥ 0.8 **and** no `Workstation` lies within 6 m. |
| `WorkstationDeploySystem` | same, after Placement | Placement enabled + valid + `OnInteractPlayerInput` → instantiate `WorkstationPrefabReference.prefab` at the position, facing the player; set `Workstation.ownerPlayer` and `PlayerWorkstationLink`; disable placement; **consume** `OnInteractPlayerInput`. Invalid + E → consume E, do nothing. |
| `WorkstationMountSystem` | same, after Deploy | Not mounted, `Target` = own workstation, E → enable `WorkstationMounted`, snap the player to the mount point, record `healthWhenMounted`, consume E. Mounted and (`WorkstationDismountRequest`, or player `Health` < `healthWhenMounted`, or the workstation no longer exists) → disable `WorkstationMounted`. |
| `PlayerReviverSystem` (edit) | `PlayerEquipmentSystemGroup` | Phase 1 CL-P3 |
| `WorkstationFurnaceSystem` | `BuildingsSystemGroup` | Toggle request flips `isBurning`. Burning + Wood ≥ 1: tick `burnTimer`; at the interval queue `Wood −1, Electricity +electricityPerWood`. Reads the owner player's ledger through `Workstation.ownerPlayer`. |
| `WorkstationCraftSystem` | same | `CraftRevivalRoundRequest.count` → for each: `TrySpend` scrap + electricity → `RevivalRound +1`; stop at the first failure (shortfall stamped). |
| `WorkstationRepairSystem` | same | Request + Health < max → `TrySpendSingle(Wood, repairWoodCost)` → `Health += repairHealthAmount` (clamped). |
| `WorkstationSummonSystem` | same | Each `WorkstationSummonRequest` whose type is in `WorkstationSummonOption`: look up `summonCost` in `UnitDataLibrary`, `TrySpend(chargeCorpses: true)`, and on success append a queue entry with that option's `buildSeconds`. Requests are cleared. Ticks the **head** of the queue only (one at a time); on zero, appends `PendingMinionSpawn` at the spawn point and pops. |
| `WorkstationPackUpSystem` | same | Request → refused (log) while the summon queue is non-empty. Otherwise enable `WorkstationDismountRequest` on the owner, null `PlayerWorkstationLink`, destroy the workstation entity through an ECB. |
| `WorkstationDestroyedSystem` | same | `Health.healthAmount ≤ 0` → enable the `CampLoopGameOver` singleton (if present). One-shot. |
| `WorkstationSummonSpawnSystem` | `SpawnSystemGroup` | Drains `PendingMinionSpawn`: instantiate the `UnitPrefabEntry.bodyPrefab` for the type at the exact position, add `PoolOwner`, enable `NewlySpawned` + `ActionRequest` (same as `UnitSpawnerSystem`), add `SpawnAsPlayerMinion`. No pool reclaim in v1 (CL-A6). |
| `PlayerMinionSpawnInitSystem` | `SpawnInitSystemGroup`, `[UpdateAfter(typeof(SpawnStateInitSystem))]` | `NewlySpawned` + `SpawnAsPlayerMinion`: enable `Undead`, `Minion`, `PlayerUnitBrain`; disable `UtilityBrain`; remove the tag. These are the bits `ReviveJob` sets on a revived corpse, so a summoned unit and a revived one behave the same. |

**CL-A4: the Tab gate.** With `PlayerControllerAuthoring.requireWorkstationForUnitControl` (default **true**), `PlayerInputManager.OnSwitchControls` on foot does nothing but log "Mount your workstation to command units" (Phase 3 replaces this with the on-foot command layer). In the `ControlUnits` map, Tab enables `WorkstationDismountRequest` instead of switching directly. The map switch itself is driven **from ECS state**: `PlayerInputManager.ObservedUpdate` watches `WorkstationMounted`'s enabled bit and calls `ApplyActionMapSwitch` on change, so damage-dismount and pack-up switch the camera back without any extra wiring. With the flag false, Tab behaves exactly as today (debug escape hatch).

**CL-A5: mount state lives in ECS**, not in the MonoBehaviour. AI, damage and pack-up all need to read it.

**CL-A6: summons bypass `UnitSpawner`.** `UnitSpawnerSystem` spawns out of camera view on purpose and picks random positions, and a summon must appear at the station, visibly. A dedicated drain system is simpler than adding flags to the ambient spawner. Pool reclaim is skipped because pooled bodies of a minion type would carry a dead minion's state. Revisit when the Despawn pool covers minions.

## 6. Phase 2 — MonoBehaviours and UI (UI Toolkit placeholders, CL-D11)

- **`WorkstationGhostPresenter`** (`MonoBehaviours/`): while `PlayerWorkstationPlacement` is enabled, shows a ghost GameObject (assigned in the inspector; a translucent cube is fine) at `position`, tinted green or red by `isValid`; hidden otherwise.
- **`ResourceHudUI`** (`UI/`): a UIDocument with one row: Bodies, Wood, Scrap, Electricity, Rounds, Currency. Shortfall flash per CL-P4.
- **`WorkstationMenuUI`** (`UI/`): visible only while `WorkstationMounted` is enabled. Contents:
  - one Summon button per `WorkstationSummonOption`, labelled with its cost
  - the queue, with a progress bar on the head entry
  - Craft Round (cost label)
  - Repair (cost label, station HP bar)
  - Furnace on/off
  - Pack Up
  - Dismount

  Each button writes its request onto the workstation or player entity. Reads ECS each `LateUpdate`, the way `MinionGroupHUD` does.
- **`CampLoopGameOverUI`** (`UI/`): when `CampLoopGameOver` is enabled, a full-screen "Your workstation was destroyed" panel with a Restart button (`SceneManager.LoadScene` of the active scene).
- **Input:** add `DeployWorkstation` to the `Player` map in `Assets/PlayerControlls.inputactions` (`<Keyboard>/b`, `<Gamepad>/select`), with an `OnDeployWorkstation` handler in `PlayerInputManager` and the binding on the scene's `PlayerInput` events.

## 7. Phase 2 — Playtest scene (CL-D13)

`Assets/Scenes/CampLoop.unity`: a copy of `TestArea.unity`, which keeps the managers, camera rig and `PlayerInput`, with its subscene replaced by a new `Assets/Scenes/SubScenes/CampLoop_Sub.unity` containing:
- the player
- flat countryside ground with physics
- three `MaleCitizen` units (living field-revive targets), with the player's reviver in slot 1
- `CampLoopRulesAuthoring` (bakes the disabled `CampLoopGameOver` singleton)

The workstation prefab is `Assets/Prefabs/Buildings/Workstation.prefab`: a placeholder mesh, `WorkstationAuthoring`, a `MountPoint` child and a `SummonSpawnPoint` child. The scene is dressed by the owner later. Gather nodes and enemies arrive with Phases 4–7. If the copy turns out to drag DOTSTestScene content along, build the scene from `Game.unity`'s manager set instead and record which in §13.

## 8. Phases 3–7 sketch (not built now; recorded so 1–2 fit)

- **Phase 3, squads and on-foot command (CL-D3, CL-D7):**
  - Squads reuse `PlayerHordeSlot` (four slots already exist) and `PlayerMinionGroupsData.unlockedGroupCount` (the test bakes 2).
  - Red = slot 1, Blue = slot 2. Cycle the active squad with Q/E (`CycleGroupUp/Down` already exist in `ControlUnits`, and need mirrors in `Player`).
  - Quick commands target the active squad: Follow, Hold, Attack-my-target, Gather, Return.
  - On foot, only members within `commandRadius` (15 m, shown as a ground ring) hear a command. Mounted, every member hears it.
  - New summons join the active squad.
  - ← DECISION (Phase 3): number keys 1–4 are already `QuickCommand1–4` in `ControlUnits` **and** item slots in `Player`. Assigning a squad with select + 1/2 needs a different key (e.g. Ctrl+1/2, RTS-style) or quick commands move to the D-pad/F-keys. Recommend Ctrl+number to assign and plain number for the quick command.
  - ← DECISION (Phase 3): `ControlUnits` binds Select to the **right** mouse button and Command to the **left**, which is the reverse of most RTS games. Swap?
- **Phase 4, gathering (CL-D8, CL-D9):**
  - `ResourceNode { resourceType, remaining, harvestSeconds, loadSize }` on trees and scrap piles, `PlayerInteractable`-style highlight.
  - The Gather order resolves to a new `GatherBehaviour.asset`: Approach → Harvest anim + wait → carry `loadSize` → Approach the workstation → `RequestDeposit` → loop until the node is empty or the unit is re-ordered.
  - Deposit appends a `ResourceChangeRequest` to the owner player's ledger.
  - Corpses are hauled with the same loop (a corpse is a node with `Corpse` 1 and `remaining` 1, and is destroyed or pooled on pickup).
- **Phase 5, bandit camp:** a new `Bandits` faction (append to `FactionType`), a `Bandit` `UnitSO` on the citizen body with a weapon, and a camp spawner with a guard-radius awareness. Bandits are human, so their corpses are field-revivable.
- **Phase 6, raid:** a timed wave spawner targeting the player's workstation; the station gets damage intake (the T0 of Phase 6 finds what the damage bus needs on a non-unit). The lose screen already exists from Phase 2.
- **Phase 7, rival engineer:**
  - An enemy `Workstation` with `Faction` ≠ Player and an AI summon loop (a timer plus a budget instead of a ledger) that sends waves at the player's station. Its zombies use the `Rottens` faction and are the "rival undead army".
  - Destroying it flips its engineer unit into a Flee-or-Fight decision via the existing Bravery/Flee awareness.
  - The Phase 2 components are already faction-agnostic for this reason (`ownerPlayer` can be `Entity.Null` for AI stations).

## 9. File manifest (Phases 0–2)

**Phase 0 (stage):** `Systems/SystemGroups.cs` (`PlayerResourceSystemGroup`, `WorkstationInputSystemGroup`), `Tests/SystemGroupOrderTests.cs`, `Data/Enums/ResourceType.cs` (new), `Data/Enums/UnitType.cs` (append two).

**Phase 1:** the `PlayerResource_System.md` §8 manifest, with `UI/ResourceHudManager.cs` → `UI/ResourceHudUI.cs` + `UI/ResourceHud.uxml` (+ `.uss`).

**Phase 2, new:**
- `Components/Buildings/WorkstationComponents.cs`
- `Components/Player/PlayerWorkstationComponents.cs`
- `Authoring/Buildings/WorkstationAuthoring.cs`
- `Authoring/CampLoopRulesAuthoring.cs`
- `Systems/PlayerSystemGroup/WorkstationInputSystemGroup/WorkstationPlacementSystem.cs`, `WorkstationDeploySystem.cs`, `WorkstationMountSystem.cs`
- `Systems/BuildingsSystemGroup/WorkstationFurnaceSystem.cs`, `WorkstationCraftSystem.cs`, `WorkstationRepairSystem.cs`, `WorkstationSummonSystem.cs`, `WorkstationPackUpSystem.cs`, `WorkstationDestroyedSystem.cs`
- `Systems/SpawnSystemGroup/WorkstationSummonSpawnSystem.cs`
- `Systems/SpawnInitSystemGroup/PlayerMinionSpawnInitSystem.cs`
- `MonoBehaviours/WorkstationGhostPresenter.cs`
- `UI/WorkstationMenuUI.cs` + `.uxml`, `UI/CampLoopGameOverUI.cs`
- PlayMode fixtures (§10)
- `Prefabs/Buildings/Workstation.prefab`, `ScriptableObjects/Units/PlayerZombieBrute.asset`, `PlayerZombieRunner.asset`
- `Scenes/CampLoop.unity`, `Scenes/SubScenes/CampLoop_Sub.unity`

**Phase 2, edited:** `Authoring/Player/PlayerControllerAuthoring.cs`, `MonoBehaviours/Managers/PlayerInputManager.cs`, `Assets/PlayerControlls.inputactions`, `Components/Spawners/SpawnerComponents.cs`, `MonoBehaviours/DebugResourceMenu.cs` (station damage button), and the unit library asset. Vault: `Systems.md`, `Components.md`, `Contracts.md`, `Gotchas.md`, `Plans/README.md`.

## 10. Fixtures (PlayMode, manual `World`, revert-to-fail each)

- **F1–F3:** as `PlayerResource_System.md` §9, with F2 rewritten for CL-P3. Case A: one round + a citizen corpse → `ReviveRequest` enabled and `RevivalRound −1` queued. Case B: zero rounds → no revive, shortfall `RevivalRound`. Case C: a corpse with no `becomesUnitType` → no spend.
- **F4 `WorkstationSummonTests`:** a basic-zombie request with enough stock → three negative deltas (corpse, scrap, electricity) and one queue entry. Tick past `buildSeconds` → one `PendingMinionSpawn` at the spawn point. Short on scrap → no queue entry, shortfall stamped. A type not in `WorkstationSummonOption` → ignored. Revert-to-fail: drop the affordability gate.
- **F5 `PlayerMinionSpawnInitTests`:** an entity with `NewlySpawned` + `SpawnAsPlayerMinion` + the minion bits disabled → after `SpawnStateInitSystem` + `PlayerMinionSpawnInitSystem`: `Minion`, `Undead` and `PlayerUnitBrain` enabled, `UtilityBrain` disabled, tag gone. Revert-to-fail: remove the `[UpdateAfter]` (SpawnStateInit clobbers the bits) or the enable lines.
- **F6 `WorkstationFurnaceCraftTests`:** furnace converts 1 wood → 2 electricity per interval and stops at zero wood; craft ×2 with stock for one → one round, shortfall stamped. Revert-to-fail: the wood ≥ 1 guard.
- **F7 `WorkstationMountTests`:** E with `Target` = own workstation → mounted and E consumed. Dismount request → unmounted. A player `Health` drop while mounted → unmounted. Revert-to-fail: the damage check.

No fixture for placement raycasts (they need a physics world and a scene) or for UI; those are owner play checks.

## 11. Decisions recorded (do not re-ask)

| Id | Decision (owner, 2026-10-13) |
|---|---|
| CL-D1 | The station is a **deployable workstation**, placed anywhere; it stands in for the caravan posting up. Caravan driving reuses it later. |
| CL-D2 | While mounted, the character stays at the station and is **vulnerable**; taking damage knocks you out of the mode. |
| CL-D3 | Unit control works **on foot too, but within a limited radius of influence**. Quick commands on top of normal orders. Squads (Red, Blue, …) are built by assignment, the active squad is cycled, and quick or normal commands go to it. More squads unlock later; **the test starts with two**. |
| CL-D4 | **Field revive works only on humans, turns them into the basic zombie, and consumes a revival round.** Rounds are **crafted at the station** from resources. **The station summons more varieties** using normal resources. |
| CL-D5 | Deploy with a key and a ghost preview on valid ground; one station at a time; **packing up keeps the stockpile**. |
| CL-D6 | The station has health, is **repaired with wood**, and **destroyed = lose**. Same rules for the rival engineer's station. |
| CL-D7 | On foot = a radius around the player (~15 m, ground ring); mounted = the whole map. Squad assignment = select + key; quick commands Follow / Hold / Attack-my-target / Gather / Return; new summons join the active squad. |
| CL-D8 | Gathering = harvest a node, carry the load back to the station, deposit, repeat. |
| CL-D9 | Resources in the test: wood (trees), scrap (wreckage), corpses (hauled bodies), electricity (burned from wood at the station). |
| CL-D10 | Enemies, in this order: **bandit camp → bandit raid on the station → rival corpse engineer** (an enemy station spawning zombie waves; destroy it and the engineer flees or fights, and can then be killed). The rival undead army comes from the engineer's spawns. |
| CL-D11 | UI = functional placeholder now (UI Toolkit), Rive later. |
| CL-D12 | Roster = basic zombie + two variants on existing art. |
| CL-D13 | A new dedicated playtest subscene. |
| CL-D14 | Build Phases 1–2 unattended, compile-gated, with PlayMode fixtures, committed on `main`. |
| CL-D15 | Editor use allowed (compile, tests, prefab/scene creation), **no Play mode**. Leave the owner's uncommitted files alone. |

| CL-D16 | (2026-10-13, Phase 3) **Number key** with units selected → assign them to that squad; with nothing selected → switch to that squad. **Ctrl+number** always switches. Switching = make the squad active **and** select its members. |
| CL-D17 | **Space** (Control Units) and **Tab** (on foot) cycle to the next squad. Gamepad: North (mounted), East (on foot). |
| CL-D18 | Quick commands = **radial**, held on **Q or middle mouse** (gamepad: left bumper on foot, right-stick click mounted): Follow, Hold, Attack my target, Return (Gather joins in Phase 4). |
| CL-D19 | Control Units mouse **swapped**: left = select, right = command (gamepad triggers unchanged). |
| CL-D20 | Formations fill **front-to-back by arrival order**; units may walk through each other en route (owner: "feels more natural"). |
| CL-D21 | **Follow is persistent** until another order, and followers attack hostiles that come near the player (~8 m); Hold is persistent too. Implemented as `MinionStandingOrder` + `MinionStandingOrderSystem`; explicit orders clear it in `MinionActionSelectionSystem`. |
| CL-D22 | **Spawn rally grows outward from the spawn point** (nearest free slot first), overriding the earlier front-to-back rally fill; move orders keep front-to-back (CL-D20). |

Architecture calls (mine, CL-A1–A6 above, plus):

| Id | Decision |
|---|---|
| CL-A7 | The ledger stays on `Player`, so a pack-up needs no stockpile transfer. A rival station (Phase 7) gets a budget, not a ledger. |
| CL-A8 | One summon builds at a time (head of the queue only). This keeps the queue legible in the menu, and parallel builds can be an upgrade. |
| CL-A9 | Pack-up is refused while the queue is non-empty, rather than refunding: no refund path to get wrong. |
| CL-A10 | Station HP and the lose screen ship in Phase 2, with a debug damage button; real damage intake ships with the raid (Phase 6). |
| CL-A11 | The Tab-anywhere toggle survives behind `requireWorkstationForUnitControl = false` as a debug escape hatch. |
| CL-A12 | Unit-variant numbers in §4 are starting tunables, not design; they live on the SOs and the authoring. |
| CL-A13 | Squads are a game-side `SquadMember { squadIndex }` on each minion, **not** movement-toolkit Hordes: horde members steer by flow field, which would override the per-unit formation slots. `PlayerMinionGroupsData.assignmentGroupIndex` is reused as the active squad. Quick-command logic lives in a `SquadCommandManager` MonoBehaviour beside `UnitSelectionManager`; the pure rules are in `Utils/SquadRules.cs`. |

## 12. Build plan (Phases 0–2)

Orchestrator on `main`; `worker` subagents of at most two files each; `python Tools/devloop/devloop.py gate StitchPunk.*` after every wave; the Unity compile + PlayMode fixtures by the orchestrator through MCP.

- **T0 grounding:** the `UnitPrefabEntry` source list; whether the `Player` entity carries `Health`; `PhysicsWorldSingleton` usage elsewhere in `_Scripts`; `Minion` / `Undead` / `PlayerUnitBrain` / `UtilityBrain` names; the `PlayerResource_System.md` T0 list. Log drift in §13.
- **Phase 0 (orchestrator):** groups, enums, order tests. Gate.
- **Wave 1 (Phase 1):**

  | Task | Files |
  |---|---|
  | W1a | `ResourceComponents.cs` + `ResourceLedger.cs` |
  | W1b | `ResourceChangeSystem.cs` + F1 |
  | W1c | `UnitSO.cs` + `UnitBlob.cs` / `UnitLibraryBakingSystem.cs` (summon cost) |
  | W1d | `PlayerControllerAuthoring.cs` (ledger bake) + `DebugResourceMenu.cs` |
  | W1e | save mirrors (two files) |

  Gate.
- **Wave 2 (Phase 1):**

  | Task | Files |
  |---|---|
  | W2a | `PlayerReviverSystem.cs` + F2 |
  | W2b | F3 + `Contracts.md` |
  | W2c | `ResourceHudUI.cs` + uxml |

  Gate, then the Unity compile + PlayMode run. **Commit "Camp loop phase 1".**
- **Wave 3 (Phase 2 data):** the orchestrator writes both component files, `SpawnAsPlayerMinion` and the `PlayerControllerAuthoring` additions, then gates.
- **Wave 4 (Phase 2 systems, parallel):**

  | Task | Files |
  |---|---|
  | W4a | Placement + Deploy |
  | W4b | Mount + F7 |
  | W4c | Furnace + Craft |
  | W4d | Repair + PackUp |
  | W4e | Summon + F4 |
  | W4f | SummonSpawn + PlayerMinionSpawnInit |
  | W4g | Destroyed + `CampLoopRulesAuthoring` |
  | W4h | `WorkstationAuthoring` |
  | W4i | F5 + F6 |

  Gate.
- **Wave 5 (Phase 2 bridge/UI):**

  | Task | Files |
  |---|---|
  | W5a | `PlayerInputManager.cs` (Tab gate, mount watch, Deploy handler) + the inputactions file |
  | W5b | `WorkstationMenuUI` + uxml |
  | W5c | `WorkstationGhostPresenter` + `CampLoopGameOverUI` |

  Gate.
- **Editor pass (orchestrator, MCP):** compile, console, PlayMode fixtures F1–F7 (+ the existing floor), revert-to-fail; create the two `UnitSO` assets, the workstation prefab and the scene/subscene; wire `PlayerInput` events. No Play mode. **Commit "Camp loop phase 2".**
- **Retire:** move the Phase 1–2 sections to `Tasks/Verification/` with `verify-camp-loop.md`. **The spec stays in `Plans/` while Phases 3–7 are open**: the verify file links back to it. Vault notes, README row, and `Assets/CLAUDE.md` "Built" line.

## 13. Build log

**2026-10-13, Phases 0–2 built unattended** (orchestrator + 21 Sonnet/Haiku workers of two files max, plus one verifier).

**T0 drift**
- The player entity has **no `Health`**. Dismount-on-damage is coded and covered by F7, but stays dormant in game until the player gets health.
- Workstation components went in `Components/Structures/`, which already existed, not `Components/Buildings/`.
- `requireWorkstationForUnitControl` lives on `PlayerInputManager`, not on the player authoring, because only the MonoBehaviour reads it.
- The Deploy key is **polled** (`FindAction("DeployWorkstation").WasPressedThisFrame()`) instead of wired as a PlayerInput Unity Event, so the TestArea/CampLoop `PlayerInput` events needed no scene edit.

**Changes during the build**
- The basic `PlayerZombie` asset's `becomesUnitType` is **itself**, so the field-revive check is now `becomes != None && becomes != corpseType` (humans only, CL-D4). F2 case C uses that real shape.
- `WorkstationFurnaceSystem` missed `IgnoreComponentEnabledState`, which F6 caught. Recorded in Gotchas.
- Verifier follow-ups:
  - A summon whose prefab is missing now logs a warning.
  - A station at 0 HP cannot be repaired.
  - The placement ray rejects hits on units and on the player.

**Gates**
- devloop gate: clean on every assembly.
- Lint: clean on all new paths, after 8 enabled-ref renames.
- Unity console: clean.
- StitchPunk.Tests EditMode: 68/68.
- StitchPunk.Tests.PlayMode: **43/43**, up from 19, with 24 new tests across F1–F7.

**Revert-to-fail.** Burst must be **off**: with Burst on, PlayMode tests ran stale code. Both batches failed exactly their target tests:
- F1 clamp: 3 tests.
- F2 gate: 1.
- F3 load mapping: 2.
- F4 affordability: 1.
- F5 enable bit: 1.
- F6 wood guard: 1.
- F7 damage check: 1.

**Editor assets**
- `PlayerZombieBrute` / `PlayerZombieRunner` UnitSOs, added to `_UnitLibrary`.
- `Prefabs/Buildings/Workstation.prefab` (placeholder), wired on `PlayerUnit.prefab`.
- `UI/CampLoop/CampLoopPanelSettings.asset` and `Materials/CampLoop/WorkstationGhost.mat`.
- `Scenes/CampLoop.unity`: a TestArea copy plus the `CampLoopUI` and ghost presenter objects.
- `Scenes/SubScenes/CampLoop_Sub.unity`: a DOTSTestScene copy without the test spheres, plus `CampLoopRules`. The player starts with the reviver in slot 1, and the spawner makes 3 citizens.
- **Not baked, not played.**

**Owner play pass:** [`../Verification/verify-camp-loop.md`](../Verification/verify-camp-loop.md).
