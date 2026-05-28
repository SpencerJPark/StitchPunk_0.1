---
tags: [memory, code, systems, ai, action, decision, orchestration]
related: "[[Systems]], [[Components]], [[Systems_Movement]]"
---

# AI & Action Architecture — Context

## Overview: Three-Layer Separation

```
Decision Layer    → How does a unit choose what to do?
Selection Layer   → Unified picker from scored options
Orchestration     → Actions coordinate requests to downstream systems
```

AI is a **decision layer only** — it never moves a unit, fires an attack, or plays an animation. It writes scored `ActionOption` entries to a buffer. The selection and execution layers are shared by both player-guided and AI-guided units.

---

## Decision Layer

### MinionActionSelectionSystemGroup

Runs before `AIActionSelectionSystemGroup`. Handles **player-guided entities** (`PlayerControlled` enabled).

- Reads `PlayerOrder` component set by `MinionCommandSystem` (in `PlayerEquipmentSystemGroup`)
- Writes `ActionOption` buffer entries directly from the player's command
- Entities in this group skip the AI scoring pipeline entirely

### AIActionSelectionSystemGroup

Handles **utility-guided entities** (`PlayerControlled` disabled). Contains two sub-groups:

#### AIMotivationSystemGroup

Motivation decay and personality context. Runs before `AIAwarenessSystemGroup`.

| System | Purpose |
|---|---|
| `MotivationDecaySystem` (OrderFirst) | Resets `contextMultiplier` to 1.0, decays `value` by `decayRate × deltaTime` |
| `MotivationChangeRequestSystem` | Applies batched `MotivationChangeRequest` entries (Add / Set mode) |
| `PersonalityContextSystem` | Writes `contextMultiplier` per motivation from `Personality` traits (socialAffinity, wanderlust, gluttony) |

#### AIAwarenessSystemGroup

Perception + option generation. Runs before `AIScoringSystemGroup`.

| System | Purpose |
|---|---|
| `ClearOptionsSystem` (OrderFirst) | Clears the `ActionOption` buffer |
| `ThreatDecaySystem` (OrderFirst) | Prunes stale `ThreatEntry` records before consumers read. Dead/despawned attackers removed immediately; everything else decays via `ThreatEntry.staleTimer` (refreshed on each hit by `ThreatUpdateSystem`, TTL const `THREAT_TTL` ≈ 4s) so active combat never expires but a disengaged enemy is forgotten |
| `EnemyAwarenessSystem` | Detects hostiles → sets BloodLust = 100, injects attack options (priority 2) |
| `SelfDefenceAwarenessSystem` | Detects incoming threats → injects fight-back options (priority 2); fires `ActionInterruptRequest` when not already in combat |
| `FleeAwarenessSystem` | Decision-point flee: only when `ActionRequest` is enabled **and** the unit has active threats (`ThreatEntry`). Sets SelfPreservation = 100 and injects a Flee option (priority 2) scored `(1 − healthRatio) × (1 − bravery)`. No interrupt, no bracket tracking — melee-single units re-decide each swing |
| `InteractionAwarenessSystem` | Spatial-hash query per motivation → injects interaction options with `advertisedDelta = blob.restorationAmount` |
| `SocialAwarenessSystem` | Finds nearby friendly NPCs → injects Talk option with `advertisedDelta = 40f` |
| `ItemAwarenessSystem` | Scans loose items (`EquiptBy.owner == Entity.Null`) within `Awareness.range` (direct query scan, no spatial hash). Emits pickup options: **weapon** when threatened + unarmed (`SelfDefence`, priority 2; suppressed if `UnitEquipt.equiptItemEntity` set), **healing** when `health < 100%` (`SelfPreservation`, priority 0, utility scaled by `1 − healthRatio`), **food/drink** when no threat & not in combat (priority 0, `advertisedDelta = restorationAmount`). Item category + effect read from the `ItemLibrary` blob keyed by `ItemType` |
| `EnvironmentalAwarenessSystem` | Environment context — stub |

#### AIScoringSystemGroup

Scores and prunes the `ActionOption` buffer.

| System | Purpose |
|---|---|
| `MotivationScoringSystem` | **Need-delta formula**: `advertisedDelta != 0` → `(curve(current) − curve(future)) × utilityScore × contextMultiplier`; `advertisedDelta == 0` → `curve(value) × utilityScore × contextMultiplier` (fallback for combat context-flags) |
| `ActionPrioritySystem` | Keeps only the highest-priority tier; lower tiers are removed |

---

## Selection Layer — ActionSelectionSystemGroup

Unified selection — runs after both decision groups, regardless of whether the options came from player commands or AI scoring.

**`ActionSelectionSystem`** — three-stage job pipeline:

1. **Filter to highest priority tier** — eliminates lower-priority options so combat always beats idle
2. **Sort by score (descending)**
3. **Random pick from top 3** — prevents AI indecision; reduces re-evaluation cost

After selection:
- Writes `CurrentAction` (actionType + targetEntity)
- Disables `ActionRequest` (prevents re-running selection this cycle)
- Calls `SelectionFunctions` Burst function pointer table to `SetComponentEnabled` the correct action tag

**`SelectionFunctions.cs`** — Burst-compiled static methods, one per `ActionType`, stored in a jump table indexed by `(int)ActionType`. Enables the corresponding enableable tag component on the entity.

---

## Orchestration Layer — ActionExecutionSystemGroup

Actions are **coordinators, not executors**. An action system runs while its enableable tag component is active and follows this pattern:

```
1. Validate preconditions (target alive? in range?)
2. Out of range → enable PathRequest (MovementSystemGroup handles it)
3. In range → halt path, enable AttackRequest or other downstream request
4. On completion → re-enable ActionRequest to restart the cycle
```

### Current Action Systems

| System | Tag | Pattern |
|---|---|---|
| `MeleeSingleActionSystem` | `MeleeSingleAction` | Validate → path → halt → enable `AttackRequest` → enable `ActionTimer` (single shot) |
| `MeleeContinuousActionSystem` | `MeleeContinuousAction` | Same as single, but re-fires `AttackRequest` each time `ActionTimer` expires |
| `FleeActionSystem` | `FleeAction` | Run (`Movement.isRunning`) to a multi-hop away-from-attacker waypoint chain (nearest waypoint each hop, up to 4, until beyond the attacker's `Awareness.range`); re-enables `ActionRequest` on arrival so flee re-chains until threat decays |
| `WanderExecutionSystem` | `WanderAction` | Pick random nearby point → path → idle briefly → re-enable `ActionRequest` |
| `SitActionSystem` | `SitAction` | Path to interaction entity → play sit animation → countdown → apply satisfaction → re-enable `ActionRequest`. **Reference for all interaction actions.** |
| `PickupItemActionSystem` | `PickupItemAction` | Path to target item → arrive (`ItemBlob.pickupRange`) → animate + `ActionTimer` → on completion branch by `ItemCategory`: **weapon** = replicate equip linking (set `EquiptBy`/`AttachedTo`, enable `EquipAction` + `AttachItemRequest`; `ItemEquipSystem` finalises the slot), **healing** = enable `HealRequest{healAmount}`, **food/drink** = add `MotivationChangeRequest` then destroy item via ECB. Single-threaded `.Schedule()` (writes item entities via `ComponentLookup`). Serves the `EquipWeapon` / `UseHealingItem` / `Eat` / `Drink` ActionTypes |
| `ActionInterruptSystem` | — | OrderFirst; detects `ActionInterruptRequest`, disables active action tag, halts path, re-enables `ActionRequest` |
| `ActionTimerSystem` | `ActionTimer` | Ticks down `time` — action systems check expiry themselves |

### Interruption System

`ActionInterruptRequest` (IEnableableComponent) is baked onto all AI units by `UnitBakingUtil.BakeRequirements`.

**Who enables it:** `MinionCommandSystem` (player override), damage systems *(future)*, threat detection *(future)*

`ActionInterruptSystem` runs OrderFirst in `ActionExecutionSystemGroup`:
1. Disables the active action tag via switch on `CurrentAction.actionType`
2. Halts pathing, clears `ArrivedAtTarget`
3. For sit: enables `SitReleaseRequest` if unit was mid-sit (occupant cleanup)
4. Re-enables `ActionRequest` unless `PlayerControlled` is enabled

**Adding interrupt support to a new action:** add a case to `ActionInterruptSystem.DeactivateActionTag`.

### Environmental Awareness Pipeline

`SpatialHashSystem` (in `GameManagerSystemGroup`) rebuilds `SpatialHashRegistry` each frame. Keys each `InteractionProvider` entity by `(cell, MotivationType)` using its `MotivationSatisfaction` buffer.

`EnvironmentalAwarenessSystem` (in `AIAwarenessSystemGroup`) queries the hash per NPC motivation need, filters by `Interaction.allowedUnitTypeMask` (0 = any; bit N = `UnitType` N), adds `ActionOption` scored by distance.

### Interaction Action Pattern (per-type, not generic)

Each `ActionType` that maps to an environmental interaction gets its own execution system — no shared generic system. `SitActionSystem` is the reference.

**States use `ArrivedAtTarget` as flag:** disabled = pathing; enabled = executing interaction.

**Occupant flow:** `ValidateInteractionJob` increments on selection. Decrement via `SitReleaseRequest` + `SitReleaseJob` (single-threaded after parallel `.Complete()`).

### Downstream Request Types

| Request | Who enables it | Who consumes it |
|---|---|---|
| `PathRequest` | Any action needing movement | `MovementSystemGroup` |
| `AttackRequest` | Melee/ranged action systems | `AttackResolutionSystem` (CombatSystemGroup) |
| `SitReleaseRequest` | `SitJob` on completion / `ActionInterruptSystem` on interrupt | `SitReleaseJob` (decrements `occupantCount`) |

---

## Component State Machine

```
ActionRequest (enabled)                  → selection pipeline runs this frame
ActionInterruptRequest (enabled)         → ActionInterruptSystem will abort current action
Action tag (e.g. SitAction)             → that action's execution system is active
ArrivedAtTarget (enabled)               → unit has reached target; in "executing" phase
PathRequest (enabled)                   → MovementSystemGroup picks this up
AttackRequest (enabled)                 → CombatResolutionSystemGroup picks this up
SitReleaseRequest (enabled)             → SitReleaseJob will decrement occupantCount this frame
```

---

## Adding a New Interaction Action

1. Confirm `ActionType.Foo` exists in `Data/Enums/AiEnums.cs`
2. Add tag: `public struct FooAction : IComponentData, IEnableableComponent {}` (in `AiComponents.cs`)
3. Add `FooEnable` to `SelectionFunctions.cs` (takes `bool enabled`, sets it on the tag)
4. Register in **both** function tables — they share the same delegate but serve opposite purposes:
   - `ActionSelectionSystem.OnCreate` → `_functionTable[(int)ActionType.Foo] = ... FooEnable` (enables on selection)
   - `ActionInterruptSystem.OnCreate` → same line (disables on interrupt, passing `false`)
5. Wire in brain bakers: `UnitBakingUtil.AddAction<TBaker, FooAction>` + `FooReleaseRequest` if needed
6. Write `FooActionSystem` in `ActionExecutionSystemGroup/` — copy `SitActionSystem` as the template
7. For occupant management: add a `FooReleaseRequest` + `FooReleaseJob` following the Sit pattern

---

## Key Files

| File | Path | Role |
|---|---|---|
| `SystemGroups.cs` | `Systems/` | Declares all group ordering |
| `ActionSelectionSystem.cs` | `Systems/ActionSystemGroup/ActionSelectionSystemGroup/` | Top-3 random pick + state setup |
| `SelectionFunctions.cs` | `Systems/ActionSystemGroup/ActionSelectionSystemGroup/` | Burst function pointer table |
| `MeleeSingleActionSystem.cs` | `Systems/ActionSystemGroup/ActionExecutionSystemGroup/` | Reference implementation |
| `MeleeContinuousActionSystem.cs` | `Systems/ActionSystemGroup/ActionExecutionSystemGroup/` | Reference implementation |
| `EnemyAwarenessSystem.cs` | `Systems/AIActionSelectionSystemGroup/AIAwarenessSystemGroup/` | Hostile detection → attack options |
| `MotivationScoringSystem.cs` | `Systems/AIActionSelectionSystemGroup/AIScoringSystemGroup/` | Utility scoring |
| `ActionPrioritySystem.cs` | `Systems/AIActionSelectionSystemGroup/AIScoringSystemGroup/` | Tier bonuses + pruning |
| `AiComponents.cs` | `Components/AI/` | ActionRequest, CurrentAction, ActionOption buffer, Motivation buffer, all action tags |
| `AttackComponents.cs` | `Components/Units/` | AttackRequest, CombatTarget, AvailableAttack buffer |
