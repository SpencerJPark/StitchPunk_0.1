# Stitch Punk — CLAUDE.md

This file is the entry point for Claude Code. Before working in any folder, read the relevant CONTEXT.md listed below. Be sure to keep these updated in the _Vault directory as these are meant to bee tools for you, so if you can or create a new directorty/script, make sure the docs reflect that to help you further down the line
Do not make any changes until you have 95% confidence in what you need to build. Ask me follow-up questions until you reach that confidence.

**You are going to help me by playing the role of an expert when it comes to coding and dots in game development**
---

Game Overview Context

**Stitch Punk** is a 2.5D real-time strategy game set in the 1900s industrial revolution. The player is a **necro engineer** who reanimates corpses as minions, manages a factory, and navigates a murder mystery. The core loop is RPG-style missions + factory building + trade management.

The world is one where **necromancy-based engineering** is the primary driver of technological advancement — "stitched together tech." Minions are Frankenstein constructs: layered, sewn-together parts.


Always write code explicitly, never use var
As a reminder code using [Readonly] needs to import from Unity.Collection
Preference using EntityJobs were it make sense in systems
**Never call `.Run()` on a job** — always use `.Schedule()` (single-threaded worker) or `.ScheduleParallel()`. This is an absolute rule with no exceptions.

Folder Map — Read Before Working

| Folder | Context File | What's Inside |
|---|---|---|
| `_Scripts/` | [RULES.md](_Vault/Memories/Code/RULES.md) | Hard technical rules for the whole codebase |
| `_Scripts/Authoring/` | [Authoring.md](_Vault/Memories/Code/Authoring.md) | Baker pattern, unit prefab setup, how to wire new units |
| `_Scripts/Authoring/Save/` | [Authoring.md](_Vault/Memories/Code/Authoring.md) | `GameDataAuthoring` — bakes the GameData singleton entity (save, settings) |
| `_Scripts/Components/` | [Components.md](_Vault/Memories/Code/Components.md) | IComponentData / IBufferElementData conventions |
| `_Scripts/Components/Save/` | [Components.md](_Vault/Memories/Code/Components.md) | `GameDataTag`, `SaveRequest`, `LoadRequest`, `AutoSaveTimer`, `PlayTimeTracker`, `GameSettings` |
| `_Scripts/Systems/` | [Systems.md](_Vault/Memories/Code/Systems.md) | System group order, ISystem rules, Burst |
| `_Scripts/Systems/MinionActionSelectionSystemGroup/` | [Systems_AI.md](_Vault/Memories/Code/Systems_AI.md) | Player-guided decision: PlayerOrder → `StateMachine` |
| `_Scripts/Systems/UtilityAISystemGroup/` | [Systems_AI.md](_Vault/Memories/Code/Systems_AI.md) | Utility-guided decision inputs: motivation + awareness populate `UtilityActions` |
| `_Scripts/Systems/StateMachineSystemGroup/` | [Systems_AI.md](_Vault/Memories/Code/Systems_AI.md) | Decision resolution + behavior-command execution (`ConsiderationScoringSystem` + `WinnerSelectionSystem` in `ActionSelectionSystemGroup/`; `BehaviorExecutionSystem` + `BehaviorInterruptSystem` in `ActionExecutionSystemGroup/`) |
| `_Scripts/Systems/PlayerSystemGroup/` | [Systems.md](_Vault/Memories/Code/Systems.md) | Player pipeline: input → narrative → dialogue → equipment |
| `_Scripts/Systems/ItemSystemGroup/` | [Systems.md](_Vault/Memories/Code/Systems.md) | Pickup/equip/consume, thrown items |
| `_Scripts/Systems/AnimationSystemGroup/` | [Systems_Animation.md](_Vault/Memories/Code/Systems_Animation.md) | Layered quad animation, clip SO pipeline |
| `_Scripts/Systems/MovementSystemGroup/` | [Systems_Movement.md](_Vault/Memories/Code/Systems_Movement.md) | Flowfield, D* Lite, horde formation |
| `_Scripts/Systems/BuildingsSystemGroup/` | [Systems.md](_Vault/Memories/Code/Systems.md) | Factory grid + station production loop |
| `_Scripts/Systems/CombatSystemGroup/` | [Systems.md](_Vault/Memories/Code/Systems.md) | Attack execution + combat reactions (threat, knockback-on-death) |
| `_Scripts/Systems/HealthSystemGroup/` | [Systems.md](_Vault/Memories/Code/Systems.md) | Damage, death/revive, life-state |
| `_Scripts/Systems/DesignSystemGroup/` | [Systems.md](_Vault/Memories/Code/Systems.md) | Re-skin / appearance changes (`DesignChangeSystem`) |
| `_Scripts/Systems/SaveSystemGroup/` | [Systems.md](_Vault/Memories/Code/Systems.md) | Play time tracking, auto-save timer, save/load to JSON on disk |
| `_Scripts/Data/` | [Data.md](_Vault/Memories/Code/Data.md) | ScriptableObject pattern, BlobAsset baking, enums, save DTOs |
| `_Scripts/Core/` | [Core.md](_Vault/Memories/Code/Core.md) | Singletons, legacy files, what to avoid |
| `_Scripts/MonoBehaviours/` | [MonoBehaviours.md](_Vault/Memories/Code/MonoBehaviours.md) | Managers, input, camera (non-ECS layer) |

Vault Structure — `_Vault/`

`_Vault/` is an [Obsidian](https://obsidian.md) vault. Open it directly in Obsidian for graph view, backlinks, and full-text search across all project knowledge. Start at `_Vault/Home.md`.

| Directory | Purpose |
|---|---|
| `_Vault/Memories/Code/` | Per-folder context files (read before working in that folder). See [`Skills.md`](_Vault/Memories/Code/Skills.md) — index of the project DOTS scaffolding skills (canonical copies live in `.claude/skills/`) — [`Contracts.md`](_Vault/Memories/Code/Contracts.md) — the cross-feature request/event API index; read it before making one feature touch another — and [`Gotchas.md`](_Vault/Memories/Code/Gotchas.md) — non-obvious pitfalls and silent-failure traps; skim it before debugging a "should-work" bug. |
| `_Vault/Tasks/Active/` | In-flight tasks — create one `.md` per task using `_Template.md` |
| `_Vault/Tasks/Done/` | Completed tasks — move finished tasks here |
| `_Vault/Raw/` | Unstructured ideas — drop notes here; ingest into context files when ready |
| `_Vault/Memories/Marketing/` | Marketing copy, tone of voice, audience notes |

**How Claude should use the Vault:**
- **Before starting work** in any folder → read the context file from the Folder Map above.
- **Tasks** → check `_Vault/Tasks/Active/` for open tasks in your work area. Update status when done.
- **Raw ideas** → if the user pastes a raw idea, save it to `_Vault/Raw/` using the template.
- **After solving a non-obvious problem** → save a note in `_Vault/Memories/Code/` so future sessions skip the re-discovery.
- **Keep memories current** — if you add a new system, component, or directory, update the relevant file in `_Vault/Memories/Code/`.

---

Current Status

**Dialogue system + Narrative event system are built and wired.**

Dialogue editor is complete:
- Drag-and-drop node placement from palette
- Create-new-sequence button (left panel header)
- Refresher node in palette (teal, one-per-tree enforced)
- Bugs fixed: switching trees no longer corrupts connections; duplicate Start/Refresher blocked

Refresher path runtime is complete:
- One `DialogueSequenceSO` holds both paths — Start node for first visit, Refresher node for repeat visits
- `DialogueUIManager` picks the entry point based on `PlayedDialogue` buffer
- `DialogueStartSystem` simplified: always passes primary `sequenceId`; no separate refresher SO needed

Narrative event system is complete:
- ECS: `NarrativeProximitySystem` (proximity triggers), `NarrativeDialogueBridgeSystem` (dialogue→narrative bridge)
- `NarrativeEventSO` with 5 action types: DialogueTrigger, MoveNPC, PlayAnimation, EnableComponent, and custom enableable component toggle
- `NarrativeEventManager` (MonoBehaviour) drives async event execution via UniTask
- `NarrativeIds.cs` registry is ready — populate Events/Entities constants as you build scenes

**AI & Action Architecture — behavior-command state machine (current architecture):**

AI is a **pure decision layer** — awareness systems in `UtilityAISystemGroup` populate the `UtilityActions` buffer, `ConsiderationScoringSystem` scores entries via pre-sampled curves, and `WinnerSelectionSystem` writes the winner into the `StateMachine` component. Execution is a single interpreter:

- `BehaviorExecutionSystem` (`ActionExecutionSystemGroup`) runs the active behavior's `executionSequence` — a `BlobArray<BehaviorCommand>` authored in `BehaviorSO` assets and baked into the enum-indexed `BehaviorLibrary` blob. Commands either block (Approach, WaitTime, FleeFromTarget, LoopUntil) or fire-and-advance (PlayAnimation, PlayActionAnimation, RequestAttack, RequestPickup, ModifyMotivation, ReleaseInteraction, StopAnimation).
- `LoopUntil` jumps back to command index `IntParam` until any ticked `LoopQualifier` flag holds (TargetDead, TargetLost, TargetOutOfRange, TimerExpired, MotivationSatisfied, TargetNotEngagedWithSelf), with always-on timeout + iteration guards (`BehaviorQualifiers` in `_Scripts/Utils/`). This is how continuous melee loops swing-until-dead.
- `RequestAttack` resolves the unit's `AttackType` from its baked `AvailableAttack` buffer (keyed by `stateMachine.action`) and writes a fresh `AttackRequest`; `PlayActionAnimation` resolves the per-unit animation from `UnitDataBlob.actionAnimations`. Behavior assets stay unit-agnostic.
- **Interrupt pipeline (P2) is live.** `WinnerSelectionSystem` never clobbers a live behavior: idle units get direct assignment + `StateMachine.activePriority`; a running behavior is only preempted when the winner outranks it, via `StateMachine.pending*` fields. `BehaviorInterruptSystem` (ActionExecutionSystemGroup OrderFirst, same frame) is the single teardown path for both `ActionInterruptRequest` (death/revive/path-stuck) and pending preemptions — it runs the behavior's `interruptionCleanup` blob (non-blocking commands only, bake-validated), halts pathing, cancels `AttackRequest`, then resets to Idle (interrupt, also clears `UtilityActions`) or swaps in the pending behavior. `StopAnimation` deactivates the Action layer (`AnimationType.None` → `AnimationUtils.ClearLayer`).
- **Fight-or-flight is live (P3a):** `SelfDefenceAwarenessSystem` reads `ThreatEntry` (0.3s flinch delay), targets the highest-threat alive in-range attacker, emits attack options at priority 3 (above combat 2 / wander 1) — preempts via the pending path, never via interrupt. `FleeAwarenessSystem` (CitizenBrain only) offers a Flee option in the same tier 3 — health/bravery consideration curves on `FleeAction.asset` decide fight vs flee at decision points; when health < 30% and `(1 − health) × (1 − bravery) > 0.35`, the flee entry is bumped to priority 4 and breaks off an active fight mid-combat. `IsCombatAction()` (covers Flee) prevents re-trigger loops on both systems. Flee executes via the `FleeFromTarget` command in `FleeBehaviour.asset`.
- **Talk is live (P3b):** `SocialAwarenessSystem` scans `socialFactions` (UnitDataBlob) for the nearest available partner and emits a Talk option (tier 1, utility from the Social-need curve on `TalkAction.asset`). The Talk behavior runs `Approach → RequestSocialResponse → PlayAnimation(Talking, looping) → WaitTime(10s, qualifier TargetDead|TargetNotEngagedWithSelf — first qualifier-as-early-exit on a blocking command) → ModifyMotivation(Social +50) → ReleaseInteraction(60) → StopAnimation`. `RequestSocialResponse` enables `SocialInvite` on the partner via ECB; `SocialResponseSystem` (UtilityAISystemGroup, after awareness) accepts — direct StateMachine assign when idle, pending path when busy — or declines, and always consumes the invite same-frame. `RecentInteraction` cooldowns are now expiry-aware (`cooldownEndTime` checked in Interaction/Social awareness).
- **Sit + Pickup are live (V3):** `SitBehaviour.asset` (`Approach → PlayAnimation(Sit, loop) → WaitTime(8s) → ModifyMotivation(Energy +50) → ReleaseInteraction(60) → StopAnimation`) and `PickupBehaviour.asset` (shared by EquipWeapon/UseHealingItem/Eat/Drink: `Approach → PlayActionAnimation → WaitTime(1s) → RequestPickup → StopAnimation`), wired via SitAction/EquipWeaponAction/UseHealingItemAction/EatAction/DrinkAction into `CitizenBrain`. New `ItemConsumeSystem` (before `ItemEquipSystem`) handles the consumable branch (HealRequest / MotivationChangeRequest + destroy); weapons fall through to the equip path. **Caution:** never put `TargetDead` qualifiers on WaitTime commands targeting chairs/items — missing-Dead-data evaluates true and exits instantly. Sit's interaction source is wired: `Chair.asset` (Sit, Energy/50, range 1.5) is in `_InteractionLibrary`, a Sit chair entity exists in DOTSTestScene, and `InteractionAuthoring.Baker` now bakes `action = authoring.actionType` (it previously baked Idle, which silently killed all interaction awareness).
- **Minion move orders are live (V3 Phase 4):** `StateMachine`/`UtilityActions` carry `targetPosition + hasTargetPosition`; a move order is `ActionType.Wander` with `targetEntity = Null` and `Approach` paths to the raw position. Commands (`OnMinion*Command`) are baked per-minion by `UnitBakingUtil.AddPlayerControlled`, fanned out to selected minions by `UnitSelectionManager` (which also enables `PlayerUnitBrain`), and consumed one-shot by `MinionActionSelectionSystem`. A player order to a new target/position preempts a live same-action behavior.
- The 8 legacy per-action systems were **deleted** from `ActionExecutionSystemGroup/` (semantics preserved in `_Vault/Memories/Code/Systems_AI.md`); the empty `Schedule`/`Weather`/`Enviroment` awareness stub files were deleted (2026-07 cleanup); schedule awareness is a fresh build planned in `_Vault/Tasks/Plans/SchedulesWaypoints_System.md`.

Behavior-recreation status (interrupts, self-defence, flee, talk, sit/pickup, minion orders, sound) is captured inline in this section and in [`_Vault/Memories/Code/Systems_AI.md`](_Vault/Memories/Code/Systems_AI.md); no awareness stubs remain on disk (see line above); Schedule/Weather/Environment awareness are planned fresh builds.

**Item awareness is built:** `ItemAwarenessSystem` scans loose items (`EquiptBy.owner == null`) and emits pickup options — weapon when threatened+unarmed, healing when hurt, food/drink when idle — each with `actionDefIndex` resolved from the unit's brain (entries are skipped for units whose brain lacks the action). Execution goes through `PickupBehaviour` + `RequestPickup`, then branches downstream: `ItemConsumeSystem` consumes consumables (`HealRequest` / `MotivationChangeRequest` + destroy), `ItemEquipSystem` equips weapons. Category/effect data comes from the `ItemLibrary` blob (`ItemSO` → `ItemLibrarySO` → `ItemLibraryBakingSystem`). `_ItemLibrary` now holds None/Rock/Bandage/MedKit/Bread/Water; Feed + Hydrate `EffectSO`s exist (both restore Hunger — there is no Thirst NeedType, and EffectLibrary is enum-indexed so Bandage and MedKit share Healing's value). **Editor setup still required:** place consumable item GameObjects in the scene (`ItemAuthoring`, itemType 6/7/8/9, the Rock object is the model) and wire `handSocket` on the citizen prefab for visual weapon attach.

**Next:** Reintroduce waypoints as a downstream request target in the action system (waypoint entity → awareness scores it → `WaypointAction` tag → `PathRequest` to position).

**Factory System Phase 1 — data layer built, production loop PARKED:** `ProductionSystem` and `FactoryLibraryBakingSystem` are commented out and parked in `Core/Unused/` (moved there 2026-07; they were previously commented out in place). The grid/station components, authoring, and library SO types below still exist, but nothing produces until both files are restored and re-enabled.

Grid + station data layer:
- `FactoryGridConfig` singleton + `FactoryGridCell` buffer — baked by `FactoryGridAuthoring`
- `FactoryStation`, `StationInputSlot`/`StationOutputSlot` buffers, `ProductionProgress` (enableable), `StationWorkerSlot` buffer — baked by `FactoryStationAuthoring`
- `FactoryLibrarySO` → `FactoryLibraryBlob` baked by `FactoryLibraryBakingSystem` (PostBakingSystemGroup)

Production loop:
- `ProductionSystem` in `BuildingsSystemGroup` — `StartProductionJob` checks inputs+workers and starts cycles; `TickProductionJob` ticks elapsed time and writes outputs on completion
- `BuildingsSystemGroup` is now declared in `SystemGroups.cs` (after Movement, before Combat)

Station types for demo: `PrepTable`, `AssemblyBench`, `GalvanicCharger`, `OutputBay`
Item types: `CorpseBody`, `MechScrap`, `ElectricCharge`, `FleshAutomaton`

**Next steps:**
- Create `ProductionRecipeSO` assets for each station (define inputs → outputs + duration)
- Create a `_FactoryLibrary` SO asset pointing to all recipes
- Test: add `FactoryGridAuthoring` + `FactoryLibraryAuthoring` + test stations to a scene; manually populate `StationInputSlot` in ECS inspector and confirm production runs
- Phase 2: Grid placement UI (MonoBehaviour `FactoryPlacementManager`) + camera switching
- Phase 3: Worker carry tasks (`CarryTask` component + `WorkerCarrySystem`) + conveyor belt entities
- Create `ChaseBehaviorSO` + `MeleeAttackBehaviorSO` SO assets in the project
- Create a `FeralZombieConfig` `BrainConfigSO` asset
- Populate `NarrativeIds` and create first `NarrativeEventSO` assets
