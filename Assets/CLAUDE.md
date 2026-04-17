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

Folder Map — Read Before Working

| Folder | Context File | What's Inside |
|---|---|---|
| `_Scripts/` | [RULES.md](_Vault/Memories/Code/RULES.md) | Hard technical rules for the whole codebase |
| `_Scripts/Authoring/` | [Authoring.md](_Vault/Memories/Code/Authoring.md) | Baker pattern, unit prefab setup, how to wire new units |
| `_Scripts/Authoring/Save/` | [Authoring.md](_Vault/Memories/Code/Authoring.md) | `GameDataAuthoring` — bakes the GameData singleton entity (save, settings) |
| `_Scripts/Components/` | [Components.md](_Vault/Memories/Code/Components.md) | IComponentData / IBufferElementData conventions |
| `_Scripts/Components/Save/` | [Components.md](_Vault/Memories/Code/Components.md) | `GameDataTag`, `SaveRequest`, `LoadRequest`, `AutoSaveTimer`, `PlayTimeTracker`, `GameSettings` |
| `_Scripts/Systems/` | [Systems.md](_Vault/Memories/Code/Systems.md) | System group order, ISystem rules, Burst |
| `_Scripts/Systems/AISystemGroup/` | [Systems_AI.md](_Vault/Memories/Code/Systems_AI.md) | Motivation scoring, waypoint interactions, Brain/Body |
| `_Scripts/Systems/AnimationSystemGroup/` | [Systems_Animation.md](_Vault/Memories/Code/Systems_Animation.md) | Layered quad animation, clip SO pipeline |
| `_Scripts/Systems/MovementSystemGroup/` | [Systems_Movement.md](_Vault/Memories/Code/Systems_Movement.md) | Flowfield, D* Lite, horde formation |
| `_Scripts/Systems/SaveSystemGroup/` | [Systems.md](_Vault/Memories/Code/Systems.md) | Play time tracking, auto-save timer, save/load to JSON on disk |
| `_Scripts/Data/` | [Data.md](_Vault/Memories/Code/Data.md) | ScriptableObject pattern, BlobAsset baking, enums, save DTOs |
| `_Scripts/Core/` | [Core.md](_Vault/Memories/Code/Core.md) | Singletons, legacy files, what to avoid |
| `_Scripts/MonoBehaviours/` | [MonoBehaviours.md](_Vault/Memories/Code/MonoBehaviours.md) | Managers, input, camera (non-ECS layer) |

Vault Structure — `_Vault/`

`_Vault/` is an [Obsidian](https://obsidian.md) vault. Open it directly in Obsidian for graph view, backlinks, and full-text search across all project knowledge. Start at `_Vault/Home.md`.

| Directory | Purpose |
|---|---|
| `_Vault/Memories/Code/` | Per-folder context files (read before working in that folder) |
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

**Unified AI Brain System — Phase 1 (data layer) complete:**

Architecture: single entity per unit, brain type as a `BrainType` enum on a `Brain` component. All brain configs live in `BrainLibraryBlob` keyed by enum value. Brain swap = cheap value change, no entity destruction.

New data layer:
- `Brain` component (BrainType enum) + `MotivationState` (9 floats) on every AI unit
- `BrainConfigSO` — one per brain type, inline motivation + behavior config (no sub-assets)
- `BrainLibrarySO` → `BrainLibraryBlob` baked by `BrainLibraryBakingSystem`
- `ActionOption` buffer redesigned: `score`, `ActionCategory`, `targetEntity`, `targetPosition`
- `SwapBrainSystem` — no entity ops, just changes `Brain.activeBrain` + resets `MotivationState`
- `BrainAuthoring` — single authoring component for all unit types

Brain types defined: Citizen, Guard, FeralZombie, PlayerZombie, Panic, Merchant, Character.

**Phase 2 complete:** Pre-pass + generic scorer architecture implemented.
- `Motivation : IBufferElementData` — unified buffer (value, decayRate, contextMultiplier) replaces 9 separate XxxMotivation components
- `MotivationDecaySystem` — single buffer-iterating job (resets contextMultiplier + applies decay)
- `SelfPreservationPrePassSystem` — health < 30% → contextMultiplier = 2.5 on SelfPreservation
- `SafetyPrePassSystem` — threats present → contextMultiplier = 2.0 on Safety
- `MotivationScoringSystem` — single generic scorer (replaces 8 per-motivation systems)
- `Behaviour : IBufferElementData` — buffer for behaviour entries (Wander, Chase, MeleeAttack, Flee)
- `InteractionValue : IComponentData` — generic multiplier baked on all interaction entities
- `BrainBakeHelper.AddHumanMotivations` — now populates Motivation buffer (9 entries with rates)
- `BrainBakeHelper.AddCitizenBehaviours` — adds Wander + Flee entries to Behaviour buffer

**Factory System Phase 1 is built (ECS data layer + production loop):**

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
