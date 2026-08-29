---
tags: [memory, code, systems, ai, action, decision, orchestration, statemachine]
related: "[[Systems]], [[Components]], [[Systems_Movement]]"
---

# AI & Action Architecture — Context

> Updated 2026-06 after the Utility AI Refactor V3.
> The old ActionOption/per-action-system architecture is **gone** — the 8 legacy execution
> systems (MeleeSingle/MeleeContinuous/Talk/PickupItem/Sit/Wander/Flee/Interact) were deleted.
> Execution is the behavior-command state machine described below.

## Overview: Pipeline

```
UtilityAISystemGroup
  ├── UtilityMotivationSystemGroup        (decay, MotivationChangeRequest, etc.)
  └── UtilityAwarenessSystemGroup         (awareness → UtilityActions buffer entries)
        SocialResponseSystem         (after awareness — consumes SocialInvite)

MinionActionSelectionSystemGroup     (player orders → UtilityActions, isPlayerOrdered=true)

StateMachineSystemGroup
  ├── ActionSelectionSystemGroup
  │     ConsiderationScoringSystem   (blob curves → totalUtility, priority)
  │     WinnerSelectionSystem        (OrderLast — picks winner → StateMachine)
  └── ActionExecutionSystemGroup
        BehaviorInterruptSystem      (OrderFirst — interrupts + pending swaps)
        BehaviorExecutionSystem      (walks BehaviorConfigBlob → emits requests)
```

AI is a **decision layer only** — awareness writes scored options; a single interpreter
(`BehaviorExecutionSystem`) executes whatever wins by walking the authored command sequence.

## Decision Layer — awareness systems (UtilityAwarenessSystemGroup)

All append `UtilityActions` entries. Every emission MUST set `actionDefIndex` via
`BrainBlobUtils.GetActionDefIndex(ref blob, brain.unitType, actionType)` and **skip the Add when
< 0** (unit's brain lacks the action). `ConsiderationScoringSystem` fills `priority` from the
actionDef (`max(actionDef.priority, entry.priority)` — awareness may pre-bump it).

| System | Emits |
|---|---|
| `ClearOptionsSystem` (OrderFirst) | clears the buffer (skips dead units) |
| `ThreatDecaySystem` (OrderFirst) | prunes stale `ThreatEntry` |
| `EnemyAwarenessSystem` | attack options (priority 2) |
| `SelfDefenceAwarenessSystem` | fight-back at priority 3 after 0.3s flinch (`ThreatEntry`) |
| `FleeAwarenessSystem` | Flee at tier 3; bumped to 4 when health<30% & `(1−hp)×(1−bravery)>0.35` (citizens only) |
| `InteractionAwarenessSystem` | interact options from the interaction spatial hash (keyed by `satisfiedNeed`) |
| `SocialAwarenessSystem` | Talk option, tier 1 |
| `ItemAwarenessSystem` | EquipWeapon (threatened+unarmed), UseHealingItem (hurt), Eat/Drink (idle) — from `ItemLibrary`/`EffectLibrary` blobs |
| `Schedule`/`Weather`/`Enviroment` AwarenessSystem | stubs — future phases |

## UtilityActions / StateMachine (Components/AI/UtilityAiComponents.cs)

`UtilityActions`: targetEntity, **targetPosition + hasTargetPosition** (raw position target —
explicit bool because float3.zero is a legal position), actionType, priority, totalUtility,
actionDefIndex, needsValidation, isPlayerOrdered.

`StateMachine`: action, activeBehavior, targetEntity, **targetPosition/hasTargetPosition**,
currentPhase, CurrentCommandIndex/CommandTimer/LoopTimer/LoopIterations, currentStance,
activePriority, and `pending*` mirrors (incl. pendingTargetPosition/pendingHasTargetPosition).

## Selection — WinnerSelectionSystem

1. `isPlayerOrdered` entry wins unconditionally (priority recorded as `int.MaxValue`).
2. Else: highest priority tier gate → highest totalUtility within the tier.
3. Same-action guard: won't restart an identical action — EXCEPT a player order to a different
   target/position, which re-targets (safe: orders are one-shot consumed).
4. Idle (`activeBehavior == None`) → direct assign (incl. position fields). Live behavior →
   `pending*` fields only when winner outranks `activePriority`; `BehaviorInterruptSystem`
   performs the swap same-frame.

## Execution — BehaviorExecutionSystem

Walks the active behavior's `executionSequence` (`BlobArray<BehaviorCommand>` from `BehaviorSO`
assets baked into the enum-indexed `BehaviorLibrary` blob). `BehaviorExecutionSystem.cs` is now a
thin job shell (phase machine, command-index advance, dispatch switch) — **per-command logic lives
in `Utils/BehaviorCommands/*.cs`** (2026-08-29 split, one static handler class per family:
`MovementCommands`, `WaitLoopCommands`, `AnimationCommands`, `ItemCommands`, `RequestCommands`,
`MiscCommands`, `WaitForAnimEventCommand`, `WaitForClipFinishedCommand`, plus the `BehaviorCommandContext`
struct bundling lookups/ECB/blob refs). Add a new `BehaviorCommandType` arm there, not inline in the
switch. Blocking commands: Approach, WaitTime, FleeFromTarget, LoopUntil, WaitForAnimEvent,
WaitForClipFinished (each owns its own `CurrentCommandIndex`/timer advancement). Fire-and-advance:
PlayAnimation, PlayActionAnimation, RequestAttack, RequestPickup, ModifyMotivation, ReleaseInteraction,
StopAnimation, RequestSocialResponse, PlaySound. `BehaviorInterruptSystem.RunCleanupCommand` reuses the
`ModifyMotivation`/`ReleaseInteraction`/`StopAnimation` handlers directly (no more duplicated bodies).

### Animation event timing (2026-08-29, `AnimationEventTiming_System.md`)

Authored `AnimEventOutput` events (toolkit, `com.dotsanimationtoolkit`) are the timing source for AI/combat
durations that used to be hand timers guessing at a clip's length — retiming a clip in the Clip Editor now
retimes the gameplay for free.

- **1-frame latency contract, accepted permanently:** `AnimationToolkitSystemGroup` runs *after*
  `StateMachineSystemGroup`/`CombatSystemGroup`, and `EventEmissionSystem` clears+re-emits `AnimEventOutput`
  during its own update — so `BehaviorExecutionSystem`/`AttackRequestSystem` (earlier in the frame) always
  read last frame's events. 16ms at 60fps is invisible; do not reorder groups to "fix" this.
- **`WaitForAnimEvent { IntParam = eventKey, LayerIndex }`** — completes when the unit's `AnimEventOutput`
  buffer holds `(uint)IntParam` on `LayerIndex` this frame (gated on `AnimEventsPending`, so event-less
  frames cost nothing). Event keys are the project's generated `AnimEvents.*` constants
  (`Assets/Generated/DotsAnimationToolkit/AnimEvents.cs`, sourced from the
  `ProjectSettings/DotsAnimationToolkitAnimEventKeyRegistry.asset` registry — edit names/add rows there,
  then regenerate; never hand-author a raw key number in a new behavior). `Assets/Generated/` has no
  asmdef of its own by default — game code referencing `AnimEvents`/`TargetTags` needs the assembly it's
  compiled into referenced from the consumer's asmdef; `Assets/Generated/DotsAnimationToolkit/StitchPunk.Generated.asmdef`
  (added 2026-08-29, first real consumer) covers both generated files and is referenced by
  `StitchPunk.Systems.asmdef`.
- **`WaitForClipFinished { LayerIndex }`** — completes on `ReservedEventKeys.ClipFinished` for that layer;
  also completes on `ClipResolveFailed` (a missing clip can't hang a behavior). The clip on that layer must
  be `LoopMode.Once` — a looping clip never fires `ClipFinished`.
- Both carry `Duration` as a timeout (0 = none) — same safety-rail pattern as `WaitTime`/`LoopUntil`; on
  timeout they complete anyway with a warning log.
- **Pickup converted** (`PickupBehaviour.asset`): `PlayActionAnimation` (now `Looping: 0`) →
  `WaitForClipFinished(LayerIndex: Action)` → `RequestPickup` → `StopAnimation`. Talk/Sit deliberately did
  **not** convert — their `WaitTime` durations are a design choice (Sit's 8s), not clip cover.
- **Combat Hit trigger** (`AttackRequestSystem`): fires the `DamageBus` enqueue on `AnimEvents.Attack`
  (key 18, Action layer — the registry's pre-existing `Attack` entry; do not add a second `Hit` key, this
  is it) instead of a fixed `elapsed >= hitTime`. `AttackBlob.hitTime` is now the fallback/timeout ceiling —
  races against the event each frame (`elapsed += deltaTime`; either the event or `elapsed >= hitTime` fires
  first, `hitFired` guards a double-fire). A clip with no `Attack` event authored always lands damage via the
  timeout; a clip that has one should keep `hitTime` comfortably above the event's real time so the event
  decides, not the timer. **Temporary dual path** — delete `hitTime`-as-trigger once every attack clip has
  an `Attack` event authored (one milestone out; do not let this drift into a permanent second vocabulary).
  `PlayerAttackSystem`'s cooldown uses the authored `cooldown` value only now — the old
  `max(cooldown, hitTime + 0.05)` floor is gone (cooldown is game feel, not clip sync).
- **Assignment shrink deferred:** `UnitAnimationAssignmentSystem`'s Action-layer auto-assign (drives the
  clip from `unitAction.current` when no behavior command owns the layer) is still load-bearing —
  `FleeBehaviour` has no explicit `PlayActionAnimation`/`StopAnimation` and depends on it. The plan's "shrink
  to Base-layer-only" is NOT done; it only becomes safe once every behavior explicitly owns its Action-layer
  clip the way Pickup and MeleeSingle/MeleeContinuous already do.

- **Approach**: paths to `targetEntity` (waypoint scatter, moving-target repath) — or, when
  `targetEntity == Null && hasTargetPosition`, paths once to the raw `targetPosition` (no repath).
  `targetEntity == Null` with no position → Complete.
- **RequestPickup**: re-validates the item is still loose (EquipBy.owner null or self), claims it
  (EquipBy/AttachedTo), enables `PickupRequest` + `AttachItemRequest` on the item.
- **WaitTime qualifier caution**: qualifiers use missing-data-evaluates-TRUE semantics — never put
  `TargetDead` on a WaitTime whose target is a chair/item (no Dead component → instant exit).
- Behavior Complete → reset to Idle, clear target entity AND position fields.

## Interrupts — BehaviorInterruptSystem (OrderFirst in execution group)

Single teardown path for `ActionInterruptRequest` (death/revive/path-stuck → reset to Idle, clear
options + position fields) and pending preemptions (runs old behavior's `interruptionCleanup` —
non-blocking commands only, bake-validated — then swaps pending → active incl. position fields).
`SocialResponseSystem` writes StateMachine directly (accept while idle) or via pending; both paths
clear the position fields.

## Player / minion command pipeline (Phase 4, current)

- `UnitBakingUtil.AddPlayerControlled` bakes `PlayerUnitBrain` (disabled) + all five
  `OnMinion*Command` components (disabled) onto any unit with `unitSo.canBePlayerControlled`.
- `UnitSelectionManager.HandleCommand()` (MonoBehaviour) fans the command out to **each selected
  minion** (`Selected`+`Minion` query): sets data, enables the command, enables `PlayerUnitBrain`.
  Right-click hostile = Attack; interactable = Interact; ground = Move; Shift+ground = Defend
  (fanned out but not yet consumed); F (held) = Follow.
- `MinionActionSelectionSystem` translates enabled commands into `isPlayerOrdered` options and
  **consumes them one-shot** (`SetComponentEnabled(unit, false)` — lookups are read-write).
  Move = `ActionType.Wander` + `targetEntity Null` + `targetPosition`; Follow = Wander targeting
  the player entity (re-enabled every frame while F held).

## Brain control split — UtilityBrain = decision, StateMachine = execution

The AI is two independently-gated halves. **`UtilityBrain` (enableable) gates ONLY the decision /
awareness half** — awareness systems, `ConsiderationScoringSystem`, `MotivationChangeRequestSystem`
keep `[WithAll(typeof(UtilityBrain))]`. **The execution + shared selection/clear half must run even
when `UtilityBrain` is disabled**, so it gates `[WithPresent(typeof(UtilityBrain))]` (matches
present-but-disabled, keeps `in/ref UtilityBrain` readable): `BehaviorExecutionSystem`,
`BehaviorInterruptSystem`, `WinnerSelectionSystem`, `ClearOptionsSystem`. `SwapBrainSystem` is also
`WithPresent` so a conversion runs with the brain off.

- **Death = blank slate:** `DeathSystem` disables `UtilityBrain` and clears `ThreatEntry` /
  `MotivationChangeRequest` / `RecentInteraction` + resets `AttackRequest`. The StateMachine half
  still executes the death behavior (that's why execution is `WithPresent`).
- **Revive = player control:** `ReviveRequestSystem` enables `PlayerUnitBrain` + `Minion`, **never
  re-enables `UtilityBrain`** — a minion is driven by player commands, not utility AI. A corpse
  whose `becomesUnitType == None` is **not revivable** (request consumed, stays dead).
- **`MinionActionSelectionSystem`** now gates `[WithAll(PlayerUnitBrain)]` + `WithPresent(UtilityBrain)`.
- **`MinionSelfDefenceAwarenessSystem`** (`MinionActionSelectionSystemGroup`, gated
  `[WithAll(PlayerUnitBrain)][WithDisabled(UtilityBrain)]`) emits self-defence at priority 3.
  Player orders run at `activePriority = int.MaxValue`, so self-defence only fires when the minion is
  uncommanded — "self-defend unless ordered" with no extra state.
- *Limitation:* `MotivationChangeRequest` is not drained for minions (system stays UtilityBrain-gated);
  benign for zero-decay zombies, cleared on death.

## Item pickup flow (Phase 3, current)

`ItemAwareness` option → PickupBehaviour (`Approach 1.5 → PlayActionAnimation (Once) →
WaitForClipFinished(Action) → RequestPickup → StopAnimation`) → item gets `PickupRequest`:
- `ItemConsumeSystem` (ItemEquipSystemGroup, **before** ItemEquipSystem): Consumable category →
  `HealRequest` (EffectType.Healing) or `MotivationChangeRequest` per effect behaviour on the
  owner, disables both requests, destroys the item via ECB.
- `ItemEquipSystem`: anything still flagged (weapons) → links UnitEquip/EquipSocket.

## Authored assets (Assets/ScriptableObjects/Structures/)

- Behaviors (`BehaviorSO`, keyed by BehaviorType): Wander, MeleeContinuous, MeleeSingle, Flee,
  Talk, **Sit** (`Approach 1.35 → PlayAnimation Sit loop → WaitTime 8s → ModifyMotivation Energy
  +50 → ReleaseInteraction 60 → StopAnimation`), **Pickup** (shared by all four item actions).
- Actions (`UtilityActionSO`): WanderAction, FleeAction, TalkAction, MeleeContinuous/SingleAction,
  **SitAction, EquipWeaponAction (priority 2), UseHealingItemAction, EatAction, DrinkAction**.
- `CitizenBrain` (unitType 2) holds all of the above; `RotterBrain` (unitType 4) is combat+wander
  only. `_BehaviorLibrary` / `_BrainLibrary` feed `BehaviorLibraryBakingSystem` /
  `BrainLibraryBakingSystem` (PostBakingSystemGroup).

## Adding a new behavior domain

1. `BehaviorType.Foo` in `AiEnums.cs` (append-only) + `ActionType` if needed.
2. Author `FooBehaviour.asset` (BehaviorSO): executionSequence + non-blocking interruptionCleanup.
3. Author `FooAction.asset` (UtilityActionSO): actionType, priority tier, considerations
   (Motivation ratio = `(value+100)/200`, 1 = satisfied → use inverse curves for needs).
4. Add behavior to `_BehaviorLibrary.asset`, action to the relevant Brain asset.
5. Emit options from an awareness system (set actionDefIndex, skip when < 0).
6. New command types go in `BehaviorCommandType` + a static handler in `Utils/BehaviorCommands/`
   (join an existing family file or add one) + the `BehaviorExecutionSystem.RunExecute` switch case
   that calls it (+ `BehaviorInterruptSystem.RunCleanupCommand` if legal in cleanup — reuse the same
   handler rather than re-inlining it) + `BehaviorCommandCatalog.IsImplemented`/`IsBlocking`.

## Key Files

| File | Role |
|---|---|
| `Systems/SystemGroups.cs` | group ordering |
| `Components/AI/UtilityAiComponents.cs` | UtilityActions, StateMachine, Motivation, ThreatEntry |
| `Systems/StateMachineSystemGroup/ActionSelectionSystemGroup/ConsiderationScoringSystem.cs` | scoring |
| `Systems/StateMachineSystemGroup/ActionSelectionSystemGroup/WinnerSelectionSystem.cs` | winner → StateMachine |
| `Systems/StateMachineSystemGroup/ActionExecutionSystemGroup/BehaviorExecutionSystem.cs` | command interpreter |
| `Systems/StateMachineSystemGroup/ActionExecutionSystemGroup/BehaviorInterruptSystem.cs` | teardown/preemption |
| `Systems/MinionActionSelectionSystemGroup/MinionActionSelectionSystem.cs` | player orders |
| `Systems/MinionActionSelectionSystemGroup/MinionSelfDefenceAwarenessSystem.cs` | minion self-defence when uncommanded |
| `Systems/ItemSystemGroup/ItemEquipSystemGroup/ItemConsumeSystem.cs` | consumable branch |
| `Utils/BrainBlobUtils.cs`, `Utils/BehaviorQualifiers.cs`, `Utils/AIUtils.cs` | helpers |
| `Utils/BehaviorCommands/*.cs` | per-family command handlers + `BehaviorCommandContext` (2026-08-29 split) |
| `Utils/BehaviorCommandCatalog.cs` | `IsImplemented`/`IsBlocking` — the one source of truth both bake validation and the interpreter consult |
| `Data/SOs/BehaviorSO.cs`, `Data/SOs/UtilityActionSO.cs`, `Data/SOs/BrainSO.cs` | authoring SOs |
| `Tests/PlayMode/BehaviorExecutionSystemTests.cs` | first PlayMode World fixture over the interpreter — command-index progression |
