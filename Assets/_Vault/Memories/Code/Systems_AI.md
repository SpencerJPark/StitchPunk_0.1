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
UtilityAISystemGroup                 (SENSE + OFFER)
  ├── UtilityMotivationSystemGroup        (ThinkSchedule OrderFirst, decay, MotivationChangeRequest)
  └── UtilityAwarenessSystemGroup         (ClearOptions/ThreatDecay/TargetOccupancy OrderFirst → awareness → UtilityActions)
        SocialResponseSystem         (after awareness — consumes SocialInvite)

MinionActionSelectionSystemGroup     (player orders → UtilityActions, isPlayerOrdered=true)

ActionSelectionSystemGroup           (DECIDE — top-level since AF-D12, 2026-10-17)
  ConsiderationScoringSystem         (blob curves → totalUtility, priority)
  WinnerSelectionSystem              (OrderLast — picks winner → StateMachine, via StateMachineTransitions)
  TargetReservationSystem            (OrderLast, after WinnerSelection — exclusive-target arbitration, reverts losers)

StateMachineSystemGroup              (EXECUTE)
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
actionDef (`max(actionDef.priority, entry.priority)`). **Plain detection emits priority 0** (the authored
tier wins); awareness sets a tier only as a *context bump*, always by name from `AIPriorityTier`
(`Data/Enums/AIPriorityTier.cs` — the one ladder: Idle 0, Ambient 1, Engage 2, Defend 3, Critical 4,
Leash 5). Never write a raw tier number in a system.

| System | Emits |
|---|---|
| `ClearOptionsSystem` (OrderFirst) | clears the buffer (skips dead units) |
| `ThreatDecaySystem` (OrderFirst) | prunes stale `ThreatEntry` |
| `EnemyAwarenessSystem` | attack options at the attack actions' authored tier (melee assets = Defend 3) |
| `SelfDefenceAwarenessSystem` | fight-back bumped to Defend after the 0.3s flinch (`ThreatEntry.reactionDelay`, ticked by `ThreatDecaySystem` since AF-D9 — readers never tick it) |
| `FleeAwarenessSystem` | Flee at tier 3; bumped to 4 when health<30% & `(1−hp)×(1−bravery)>0.35` (citizens only) |
| `InteractionAwarenessSystem` | interact options from the interaction spatial hash (keyed by `satisfiedNeed`) |
| `SocialAwarenessSystem` | Talk option, tier 1 |
| `ItemAwarenessSystem` | EquipWeapon (threatened+unarmed), UseHealingItem (hurt), Eat/Drink (idle) — from `ItemLibrary`/`EffectLibrary` blobs |
| `Schedule`/`Weather`/`Enviroment` AwarenessSystem | stubs — future phases |
| `GuardPostAwarenessSystem` | leash (fixed 2026-10-19): judges the *target's* distance to the anchor. Combat inside the leash is kept even if the guard ran past it. A guard walking home (tier 5) turns back only for a hostile within `REENGAGE_LEASH_FRACTION` (0.8) of the leash, by enabling its own `ActionInterruptRequest`, because a tier-3 fight can't preempt the tier-5 return |

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
   `pending*` fields only when winner outranks `activePriority` **and** is >= the live behaviour's
   `BehaviorSO.minimumInterruptPriority` (AF-D5; player orders ignore it); `BehaviorInterruptSystem`
   performs the swap same-frame. **Tiers are the only preemption path** (owner, AF-D1) — a better
   same-tier option waits for the live behaviour to finish.
5. Every StateMachine start/pending/swap/idle write goes through `Utils/StateMachineTransitions.cs`
   (AF-D13) — WinnerSelection, SocialResponse, BehaviorInterrupt, the Complete phase and arbitration.
   A new StateMachine field gets reset there, nowhere else.

## Exclusive targets — reservations (AF-D6/D7, 2026-10-17)

- **Derived, never stored:** `TargetOccupancySystem` (awareness OrderFirst) rebuilds the `TargetOccupancy`
  singleton each frame — target → every unit whose StateMachine active OR pending target is it. Nothing
  releases a reservation: a unit that finishes, is interrupted, dies or retargets drops out next frame.
- **Capacity** (`Utils/TargetReservationUtils.cs`): an `Interaction` uses its `InteractionSO.maxOccupants`
  (0 = unlimited, for shared spots; an ActionType with no InteractionSO bakes 0) **only for units whose
  action equals the interaction's action** (Sitting on a chair). An `Item` holds 1; anything else is never
  reserved. Extend capacity rules there only (attack tokens would go here).
- **Trap (fixed 2026-10-19):** Bandit and all PlayerZombie prefabs are *variants of MaleCitizen*, which
  carries `InteractionAuthoring(Talk)`, so every unit has an `Interaction`. Before the action match, every
  unit was a one-attacker target. A second attacker was reverted every frame, and the ungated combat systems
  re-offered the same target, so units froze and never broke off tasks to fight. Never make reservability
  depend on "has an Interaction component" alone.
- **Trap (fixed 2026-10-19), units froze after walking somewhere:** no unit has toolkit animation data
  since MaleCitizen's actor profile was removed, so `WaitForClipFinished` (Pickup, Duration 0) waited
  forever. Both anim-wait commands now complete at once when the unit has no `AnimEventOutput` buffer, and
  fall back to `WaitForClipFinishedCommand.DEFAULT_TIMEOUT_SECONDS` (5 s) when Duration is unauthored.
- **Trap (fixed 2026-10-19), nobody ever wandered:** `WaypointAuthoring` baked `TransformUsageFlags.WorldSpace`
  (LocalToWorld only), but registration, NavigationAwareness and Approach/Flee read `LocalTransform`, so the
  waypoint hash was always empty. It now bakes `Dynamic`. Keep waypoints unparented, because under a parent
  LocalTransform is parent-local.
- **Offer filter:** Interaction/Item awareness skip targets that are `IsFullForUnit`.
- **Arbitration:** `TargetReservationSystem` (single-threaded on purpose) treats a reservable target the
  unit is not yet listed on as a new claim; first in iteration order wins, player orders always accept
  (may exceed capacity). Losers: fresh start → `ResetToIdle`, pending → `ClearPending`; both get
  `ThinkRate.timeUntilNextThink = 0`, so they re-pick next frame with the full target filtered out.
- This is also what makes `ItemCommands.RunRequestPickup`'s check-then-write safe in the parallel
  execution job — only one unit can be targeting a loose item.

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

- `UnitBakingUtil.AddPlayerControlled` bakes `PlayerUnitBrain` (disabled) + all seven
  `OnMinion*Command` components (disabled) onto any unit with `unitSo.canBePlayerControlled` —
  archetype change, needs a rebake.
- `UnitSelectionManager.HandleCommand()` (MonoBehaviour) fans the command out to **each selected
  minion** (`Selected`+`Minion` query): sets data, enables the command, enables `PlayerUnitBrain`.
  Right-click hostile = Attack; interactable = Interact; ground = Move; Shift+ground = Defend
  (fanned out but not yet consumed); F (held) = Follow; X (held) = Stop; R (held) = ReturnToPlayer.
  Trap: `HandleCommand` only runs on a frame where `OnCommandPlayerInput` is enabled, so F/X/R are
  "key held while issuing a command", not standalone key presses.
- `MinionActionSelectionSystem` translates enabled commands into `isPlayerOrdered` options and
  **consumes them one-shot** (`SetComponentEnabled(unit, false)` — lookups are read-write).
  Move = `ActionType.Wander` + `targetEntity Null` + `targetPosition`; Follow = Wander targeting
  the player entity (re-enabled every frame while F held). Attack resolves via
  `AIUtils.ResolveOrderedAttack` — first `AvailableAttack` entry (baked priority order, no range
  fit) with an action def in this brain; no match/no buffer refuses the order (command disabled,
  no option emitted) and logs a Warning `[MinionOrder] Unit <index> refused Attack <target>: no
  AvailableAttack entry has an action def for <UnitType name>`.
- Stop (`OnMinionStopCommand`) clears this frame's `UtilityActions` and enables
  `ActionInterruptRequest`; runs last in the arm order so it beats anything emitted the same
  frame. ReturnToPlayer (`OnMinionReturnCommand`) is a one-shot Wander to the Player's
  `LocalTransform.Position` read once in `OnUpdate`, `hasTargetPosition = true`,
  `isPlayerOrdered = true`.

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
- **Cutscene = a third gate, `CutsceneActor`** (`CutsceneSystemGroup`, [[Contracts]]): while it is
  enabled the unit is off AI and off pathing, *except* that `CutsceneMoveToMarkSystem` deliberately
  pathfinds it to the cutscene's marks — a cutscene walking its cast uses the game's own legs
  (G2, `Tasks/NewPlans/CutsceneInteractions_System.md`). The `Player` is never pathed this way.

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
3. Author `FooAction.asset` (UtilityActionSO): actionType, priority tier (`AIPriorityTier`), considerations
   (Motivation ratio = `value/100` since AF-D10 — the old `(value+100)/200` left half the axis dead and every
   Motivation curve was remapped exactly; 1 = satisfied → use inverse curves for needs. Weight is 0–1
   importance, `lerp(1, curve, weight)`; the makeup factor is applied per consideration).
4. Add behavior to `_BehaviorLibrary.asset`; put the action in a `BehaviourModuleSO` (a module is a
   *bundle* — `actions` list, AF-D2) and add the module to the brain. A brain that should treat the
   action at another tier (e.g. an aggressive brain that drops tasks to fight) adds a
   `BrainSO.priorityOverrides` entry (ActionType → tier, inherited, most-derived wins; the bake emits a
   separate action def per (action, priority), AF-D3). A behaviour lower tiers must not break off sets
   `BehaviorSO.minimumInterruptPriority`.
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
| `Systems/ActionSelectionSystemGroup/ConsiderationScoringSystem.cs` | scoring |
| `Systems/ActionSelectionSystemGroup/WinnerSelectionSystem.cs` | winner → StateMachine |
| `Systems/ActionSelectionSystemGroup/TargetReservationSystem.cs` | exclusive-target arbitration |
| `Systems/UtilityAISystemGroup/UtilityAwarenessSystemGroup/TargetOccupancySystem.cs` | occupancy singleton |
| `Utils/StateMachineTransitions.cs`, `Utils/TargetReservationUtils.cs`, `Data/Enums/AIPriorityTier.cs` | transitions, capacity rules, tier ladder |
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

## Think rate, alarm and AI throws (SmartHumanAI H2/H3, 2026-10-17)
- `ThinkScheduleSystem` (UtilityMotivation, OrderFirst) owns `ActionRequest`. **Every brain unit bakes a `ThinkRate` since AF-D8** (`interval <= 0` = every frame, which zombies keep until SmartHumanAI H6). Busy units think every `interval`; idle units think the frame they become idle, then every `min(interval, ThinkRate.IDLE_THINK_INTERVAL = 0.1s)`, not every frame. Request-gated awareness therefore runs at ~4 Hz for humans. Ungated systems (SelfDefence, Flee, GuardPost, AllyAlert response) still run every frame; the rule for staying ungated is O(own threats) cheap AND must react mid-behaviour. Force a re-think with `timeUntilNextThink = 0`.
- `AllyAlertSystem` runs after Enemy/SelfDefence and before GuardPost: RaiseAlarm spotters broadcast an `AllyAlert`, and alerted allies emit attacks at priority 3.
- **Priority 4 is now shared** by critical flee, the mid-fight weapon grab (ItemAwareness) and Throw (ThrowAwareness). The GuardPost leash at 5 still beats all of them.
- `RequestThrow` command → `AiThrowRequest` → `AiThrowItemSystem` (ItemEquip group, before ItemEquip) → the existing `ThrownItemSystem`/`ThrownItemHitSystem`.
