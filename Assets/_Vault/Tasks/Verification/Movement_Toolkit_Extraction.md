---
tags: [plan, movement, pathfinding, package]
related: "[[Systems_Movement]], [[Contracts]], [[RULES]]"
status: built
---

# DOTS Movement Toolkit — package extraction plan (retired — see verify-movement-toolkit.md)

All four phases built 2026-08-29. Deviations from the plan as written, decided during the
build rather than called out in advance:

- **`AIUtils` kept no transitional forwarding wrapper.** The plan's intro describes AIUtils
  keeping thin `BeginPathRequest`/`HaltPathing` wrappers "until game call sites are migrated" —
  since the move (Phase 2 step 5) and the migration happened in the same pass, the wrapper step
  was skipped and all 7 call sites went straight to `MovementAPI`.
- **`NavigationWaypoint` and `UnitSpeedBakingData` both stayed in game code** despite living in
  `MovementComponents.cs` (which the plan says moves wholesale minus only `UnitSpeedBakingData`).
  `NavigationWaypoint`'s only consumers are AI-spine/waypoint-registration game code; no package
  system ever reads it. See [[Systems_Movement]].
- **`HordeRegistry` and `FormationType`** (not named in the plan's file list) moved into the
  package too — both are pure horde/movement concepts with zero other game consumers.
- **`GridSystem`'s literal-duplicate static helpers now delegate to `PathfindingUtils`**
  (`CalculateIndex` ×2, `GetGridPosition`, `IsValidGridPosition`, `OctileDistance`) rather than
  a full rename/merge — satisfies "merge the duplicated members" with zero call-site risk.
  Layer-aware `GridSystem`-only overloads are untouched.
- **`wallCost`/`heavyCost`/`defaultCost` are threaded as real parameters**, not just baked and
  forgotten — every `== ConstGameData.WALL_COST` comparison across the package (including
  several dead/unused static helpers, touched only to hit the "zero `ConstGameData` hits"
  bar) now compares against the settings-sourced byte.
- **Game.unity turned out to hold no baked content.** `GridConfigAuthoring` went into
  `Assets/Scenes/SubScenes/DOTSTestScene.unity` instead (the real DOTS sandbox, loaded via
  `TestArea.unity`'s `SubScene` component) — confirmed live via `execute_code` (a unit's
  `Movement.isMoving` reached `true` under real pathfinding).
- **`HordeSystem` moved to the package's `MovementCoordinatorSystemGroup`**, not a new spot in
  game code — it can no longer live in the game's `GameManagerSystemGroup` (a package system
  can't derive from a game-declared base group). No known behavior depended on the old
  order-first-in-SimulationSystemGroup placement.

Original plan follows unchanged below.

Paste-ready prompt for an agent. Read `CLAUDE.md`, `Assets/_Vault/Memories/Code/RULES.md`,
`Systems_Movement.md`, and `Gotchas.md` before starting. Run the compile gate
(save → `refresh_unity` → poll `isCompiling` → `read_console`) after **every** phase — the
project must never sit broken between phases.

## Goal

Extract the grid/pathfinding/movement stack into `Packages/com.dotsmovementtoolkit`
("DOTS Movement Toolkit", sibling to `com.dotsanimationtoolkit`) so a new DOTS project gets
working movement by installing the package, baking one grid-config authoring, and issuing
`PathRequest`s. Stitch Punk becomes the first consumer. Scope is **internal-quality
extraction** — decoupled, documented, compiling, play-tested — NOT the sellable-toolkit
polish pass (no Samples~, no store docs, no perf rewrite). ← DECISION

## The contract (what the package API is)

- **In:** `PathRequest` (enableable) + `PathfindingAgent` on an entity, set via a static
  `MovementAPI` (today `AIUtils.BeginPathRequest` / `HaltPathing`,
  `Assets/_Scripts/Utils/AIUtils.cs:98-118` — these two move into the package; `AIUtils`
  keeps thin forwarding wrappers until game call sites are migrated, then the wrappers die).
  Any system may also write `Movement.targetPosition` directly (that is how
  `PlayerMoveSystem` works and it stays supported).
- **Out:** `Movement` (position-target + isMoving/isRunning), read by game systems
  (animation, AI). `MovementStuck` — NEW package-owned enableable tag replacing the direct
  `ActionInterruptRequest` write in `PathStuckCheckSystem.cs:83`.
- **Gating:** package systems require the baked `MovementGridSettings` singleton instead of
  `GameSceneTag`. No config in the world ⇒ the whole group idles. This is the generic
  replacement for scene gating. ← DECISION
- **Strategy extensibility:** one strategy = one `PathfindingMode` enum member + one routing
  system (consumes `PathRequest`) + one enableable follower component + one follower system
  (writes `Movement.targetPosition`). Document this recipe in the package README; new
  strategies ship inside the package, so extending the enum is acceptable. ← DECISION

## Phase 0 — scaffold

1. Create `Packages/com.dotsmovementtoolkit/` mirroring the animation toolkit layout
   (see `ls Packages/com.dotsanimationtoolkit`): `package.json`, `Runtime/`, `Authoring/`,
   `Tests/EditMode/`, plus asmdefs `DotsMovementToolkit.Runtime` and
   `DotsMovementToolkit.Authoring`. Runtime references Entities, Burst, Collections,
   Mathematics, Transforms, **Unity.Physics** (grid bake, mover collision, LOS raycasts all
   need it — do not split a Runtime.Physics asmdef; physics is not optional here). ← DECISION
2. Namespace: `DotsMovementToolkit` (game code is global-namespace; consumers add `using`).
3. Add `StitchPunk.*` asmdef references to the new package asmdefs (check
   `Assets/_Scripts/*.asmdef` names first — grep, don't guess).

Gate: compiles with the package empty.

## Phase 1 — move the generic code (mechanical, no behavior change)

Move into `Runtime/` (namespaced, `.meta` files moved WITH each script — a changed GUID
breaks every prefab/scene reference to an authoring MonoBehaviour, and a changed stable
type hash invalidates baked subscenes; both are silent until Play mode):

- `Assets/_Scripts/Systems/MovementSystemGroup/**` EXCEPT `PlayerFollowerSystem.cs` and
  `LocomotionStanceSystem.cs` (game-specific, stay put).
- `Assets/_Scripts/Components/Units/MovementComponents.cs` — EXCEPT `UnitSpeedBakingData`
  (UnitSO coupling, stays in game) — and `PathfindingComponents.cs`.
- `Assets/_Scripts/Utils/PathfindingUtils.cs` + the static helpers on `GridSystem`
  (`GridSystem.cs:167-320`). Merge the duplicated members (WorldToGrid/CalculateIndex/
  Octile/IsValid exist in both) into one `GridMath` static class; keep `PathfindingUtils`
  as the surviving name if that's less churn. ← DECISION
- `Assets/_Scripts/Utils/HordeUtils.cs`, `Systems/GameManagerSystemGroup/HordeSystem.cs`,
  `FormationOffsetSystem.cs` and the horde components — group movement is part of the
  toolkit's pitch. Strip the two game fields first: `Horde.markerEntity`
  (`MovementComponents.cs:63`) moves to a NEW game-side `HordeOrderMarker : IComponentData`
  on the horde entity (fix up `MinionCommandSystem`/`OrderMarkerSystem` — grep
  `markerEntity`); check `behaviorFlags` usage with grep and drop it if dead. ← DECISION
- Authoring → `Authoring/`: `MovementAuthoring.cs`, `PathfindingAuthoring.cs`, and any
  other authoring that only adds package components — find them with
  `grep -l "Movement\|PathfindingAgent\|Horde\|Gravity" Assets/_Scripts/Authoring -r`.
- Package system groups: declare `MovementSystemGroup` (root,
  `[UpdateInGroup(typeof(SimulationSystemGroup))]`) + the four sub-groups inside the
  package, REMOVING them from `Assets/_Scripts/Systems/SystemGroups.cs:101-118`. The root
  group derives from `ComponentSystemGroup`, not `GameSceneSystemGroup`. Game-relative
  ordering moves onto the GAME groups: `ItemSystemGroup` gets
  `[UpdateBefore(typeof(DotsMovementToolkit.MovementSystemGroup))]`, `BuildingsSystemGroup`
  gets `[UpdateAfter(...)]` — you cannot put attributes on the package class from game code.
  Update `SystemGroupOrderTests` accordingly.
- Move `PathfindingUtilsTests.cs` → package `Tests/EditMode/`.

Game-side fallout: add `using DotsMovementToolkit;` where needed — find consumers with
`grep -rl "Movement\|PathRequest\|PathfindingMode\|Horde\|GridSystem\|PathfindingUtils" Assets/_Scripts`.

Gate: compile + **rebake + enter Play mode** (moved components = new stable type hashes;
subscenes must rebake). Run all EditMode tests. Verify a prefab carrying
`MovementAuthoring`/`PathfindingAuthoring` shows no "missing script".

## Phase 2 — cut the remaining game couplings

1. **Grid config authoring.** `GridSystem.OnCreate` hardcodes 100×100/cellSize 2/1 layer
   (`GridSystem.cs:55-74`) and bakes physics-layer indices + cost bytes from
   `ConstGameData` (`GridSystem.cs:147,153`, `ConstGameData.cs:13-26`). Create
   `GridConfigAuthoring` (package `Authoring/`) baking a `MovementGridSettings`
   IComponentData singleton: width, height, layerCount, cellSize, layerHeight, wallLayerMask
   (uint), heavyLayerMask, groundLayerMask, wallCost/heavyCost/defaultCost bytes.
   `GridSystem` reads the singleton; every `ConstGameData.WALL_COST` etc. inside package
   code becomes a settings/parameter read (grep `ConstGameData` under the package — must
   end at zero hits). Add the authoring to the game scene/subscene with today's values.
2. **Gating.** Delete every `RequireForUpdate<GameSceneTag>` inside package systems
   (`PathRequestSystem.cs:13`, `PathStuckCheckSystem.cs:17`, `FormationOffsetSystem.cs:21`
   — grep for stragglers). The root package group's `OnCreate` does
   `RequireForUpdate<MovementGridSettings>` instead. Since the settings singleton is baked
   in the game subscene, scene-gating behavior is preserved.
3. **Death coupling.** `UnitMoverJob` and `UnitGravityJob` carry `WithNone(typeof(Dead))`.
   Make `Movement` and `Gravity` `IEnableableComponent` (enabled by default in bakers, per
   convention); drop the `Dead` filters. Game-side: `DeathSystem` disables both on death,
   revive re-enables (grep `Dead` in DeathSystem/ragdoll code for where). ← DECISION
4. **Stuck event.** `PathStuckCheckSystem` currently enables `ActionInterruptRequest`
   (AI-spine type). Package instead owns enableable `MovementStuck`; add a small game
   bridge system (in the game's StateMachine or Movement folder per folder↔group rule)
   that maps `MovementStuck` → `ActionInterruptRequest` and disables it. Add `MovementStuck`
   to `PathfindingAuthoring`'s baker (disabled).
5. **API move.** `BeginPathRequest`/`HaltPathing` from `AIUtils` → package `MovementAPI`
   static class; migrate call sites (grep `BeginPathRequest\|HaltPathing`).

Gate: compile + rebake + Play. `read_console` clean of `error CS`/`BC`. Run EditMode tests.

## Phase 3 — game bridges + docs

1. Confirm the four game-side systems still sit correctly: `PlayerMoveSystem` +
   `LocomotionStanceSystem` need `[UpdateInGroup]` pointing at the package groups (they can
   reference package types from game code — that direction is fine).
   `UnitSpeedBakingSystem` still writes package `Movement` post-bake — verify.
2. Package `README.md`: the contract section above, the add-a-strategy recipe, the
   grid-config quickstart for a fresh project, and a **Known issues** list (do NOT fix
   these now, they are the future productization pass): D* replan is main-thread and
   full-grid per request (`DStarLiteSystem.ComputeDStarLitePath`), `GridSystem` uses
   `NumBodies` as a change proxy + a `CompleteDependency()` sync (`GridSystem.cs:119,162`),
   flow-field ring-buffer slot reuse can clobber a field still in use
   (`FlowFieldSystem.cs:113-114`), layer support is half-plumbed (cost map is layered,
   flow-field/D* indexing is single-layer), per-entity LOS raycasts every frame in both
   follower systems.
3. Add an empty `MovementSteeringSystemGroup` between Follower and Execution groups — the
   declared slot for future "natural movement" work (arrival easing, avoidance; the unused
   `GetFlowDirectionSmooth` bilinear sampler is the first candidate). ← DECISION
4. Update `Assets/_Vault/Memories/Code/Systems_Movement.md` (now points at the package),
   `Contracts.md` (PathRequest/MovementStuck rows), `Components.md`, and `Systems.md`'s
   group manifest note. Update this plan's frontmatter and move it to `Tasks/Verification/`
   with a `verify-movement-toolkit.md` checklist: compile clean, rebake clean, minions
   path to orders, hordes form up, player walks, stuck units interrupt, stairs still work.

Gate: full pass — compile + rebake + Play, user play-test for the checklist items
(on-screen verification is user-driven).

## Hard rules for the agent

- Never break the game to make the package pure — every phase ends compiling and playing.
- Move `.meta` files with scripts. Never recreate an authoring `.cs` at the new path.
- No `var`, no single-letter names, `.Schedule`/`.ScheduleParallel` only — `RULES.md` binds
  package code too.
- Do not fix the Known-issues list mid-extraction. Extraction and improvement are separate
  passes; mixing them makes the play-test unattributable.
- Commit per phase with explicit paths (never `git add -A`).
