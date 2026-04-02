# Stitch Punk — CLAUDE.md

This file is the entry point for Claude Code. **Before working in any folder, read the relevant CONTEXT.md listed below. Be sure to keep these updated in the _Docs directory as these are meant to bee tools for you, so if you can or create a new directorty/script, make sure the docs reflect that to help you further down the line**

**You are going to help me by playing the role of an expert when it comes to coding and dots**
---

## Game Overview

**Stitch Punk** is a 2.5D real-time strategy game set in the 1900s industrial revolution. The player is a **necro engineer** who reanimates corpses as minions, manages a factory, and navigates a murder mystery. The core loop is RPG-style missions + factory building + trade management.

The world is one where **necromancy-based engineering** is the primary driver of technological advancement — "stitched together tech." Minions are Frankenstein constructs: layered, sewn-together parts.

**Factions (demo):** Citizens (neutral/hostile NPCs), Undead (player minions). Future areas will introduce Military, Government, Church, Elites, Rival Factions, etc.

---

## Unit Visual Design

Units are built from **layered quads** — flat meshes stacked in 2.5D space. This enables:
- **GPU instancing** of shared parts (eyes, mouths) across hundreds of on-screen characters
- **Texture arrays** + `MaterialPropertyBlock` for per-unit customization
- **Animation** via a mix of:
  - Flipbook (stepping through texture array indices)
  - Z-axis rotation of individual quads
  - Position/scale movement of quads and empty parent objects

There is no Unity Animator. All animation is driven by `AnimationClipSO` data baked into BlobAssets.

---

## Brain / Body Architecture

Units have two linked entities: a **Brain** (AI state, motivation values, action selection) and a **Body** (physics, animation, visuals). They are linked via `BrainLink` / `BodyLink` components. This split *may be collapsed back into one entity* in a future refactor — keep coupling minimal so it is reversible.

The Brain holds different AI behaviour sets per unit type — a living Citizen and a reanimated Zombie use different brain prefabs with different scoring weights.

---

## Folder Map — Read Before Working

| Folder | Context File | What's Inside |
|---|---|---|
| `_Scripts/` | [RULES.md](_Docs/RULES.md) | Hard technical rules for the whole codebase |
| `_Scripts/Authoring/` | [Authoring.md](_Docs/Authoring.md) | Baker pattern, unit prefab setup, how to wire new units |
| `_Scripts/Authoring/Save/` | [Authoring.md](_Docs/Authoring.md) | `GameDataAuthoring` — bakes the GameData singleton entity (save, settings) |
| `_Scripts/Components/` | [Components.md](_Docs/Components.md) | IComponentData / IBufferElementData conventions |
| `_Scripts/Components/Save/` | [Components.md](_Docs/Components.md) | `GameDataTag`, `SaveRequest`, `LoadRequest`, `AutoSaveTimer`, `PlayTimeTracker`, `GameSettings` |
| `_Scripts/Systems/` | [Systems.md](_Docs/Systems.md) | System group order, ISystem rules, Burst |
| `_Scripts/Systems/AISystemGroup/` | [Systems_AI.md](_Docs/Systems_AI.md) | Motivation scoring, waypoint interactions, Brain/Body |
| `_Scripts/Systems/AnimationSystemGroup/` | [Systems_Animation.md](_Docs/Systems_Animation.md) | Layered quad animation, clip SO pipeline |
| `_Scripts/Systems/MovementSystemGroup/` | [Systems_Movement.md](_Docs/Systems_Movement.md) | Flowfield, D* Lite, horde formation |
| `_Scripts/Systems/SaveSystemGroup/` | [Systems.md](_Docs/Systems.md) | Play time tracking, auto-save timer, save/load to JSON on disk |
| `_Scripts/Data/` | [Data.md](_Docs/Data.md) | ScriptableObject pattern, BlobAsset baking, enums, save DTOs |
| `_Scripts/Core/` | [Core.md](_Docs/Core.md) | Singletons, legacy files, what to avoid |
| `_Scripts/MonoBehaviours/` | [MonoBehaviours.md](_Docs/MonoBehaviours.md) | Managers, input, camera (non-ECS layer) |

---

## Current Status (as of 2026-04-01)

**Save system added.** `SaveSystemGroup` (OrderLast in `LateSimulationSystemGroup`) handles auto-save, manual save, and load via `SaveRequest` / `LoadRequest` enableable components on the GameData entity. Save files are JSON at `Application.persistentDataPath/save_slot_N.json`. See `Systems/SaveSystemGroup/`.

**GameData singleton entity** (`GameDataAuthoring`) is the single baked entity that carries all persistent game data components: `SaveRequest`, `LoadRequest`, `AutoSaveTimer`, `PlayTimeTracker`, `GameSettings`. Place one `GameDataAuthoring` GO in every game scene.

**`GlobalGameData` is now a static class** — no MonoBehaviour, no scene instance needed. All values are `const` (layer indices, pathfinding costs, scoring resolution). Designer-tweakable values (e.g. `animationFrameRate`) live in `GameSettings` on the GameData entity instead.

**Throw system fix.** Items now ignore units within 1.2 units of the throw origin — prevents standing next to a dead body from immediately blocking the throw. Walls are unaffected (no `Health` component).

**Animation fix shipped.** Root cause: `AnimatorTargetInitSystem` used `BaseParent` matching to rebuild the `AnimatorTarget` buffer after spawn. This silently skipped any quad whose `characterRoot` inspector field didn't point to the exact body root GO. Only the eyebrow (the one correctly configured quad) animated.

**Fixes applied:**
- `AnimatorAuthoring.Baker` now populates `AnimatorTarget` at bake time via `GetComponentsInChildren` — fixes scene entities permanently.
- `AnimatorTargetInitSystem` now uses `DynamicBuffer<LinkedEntityGroup>` instead of `BaseParent` matching — reliable for all prefab hierarchies.
- `AnimationTargetNoIndexAuthoring.Baker` now initialises `AnimationTargetPose` to rest pose (was zeros — caused quads to snap to local origin on spawn frame).

**AI behaviors** — needs verification. All wiring looks correct (motivation components, `NeedsAction` explicitly enabled by spawner, `BodyLink`/`BrainLink` cross-links set by spawner). If units still don't seek interactions after the animation fix, check: (1) `awarenessRange` on `CitizenBrainAuthoring` in the brain prefab inspector — must be > 0; (2) motivation starting values vs the `AIScoringCurveSO` curve shape (units with all motivations at 0 may need time to deplete before scores are non-zero).
