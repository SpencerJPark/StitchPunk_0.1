# Stitch Punk — CLAUDE.md

This file is the entry point for Claude Code. **Before working in any folder, read the relevant CONTEXT.md listed below.**

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
| `_Scripts/Components/` | [Components.md](_Docs/Components.md) | IComponentData / IBufferElementData conventions |
| `_Scripts/Systems/` | [Systems.md](_Docs/Systems.md) | System group order, ISystem rules, Burst |
| `_Scripts/Systems/AISystemGroup/` | [Systems_AI.md](_Docs/Systems_AI.md) | Motivation scoring, waypoint interactions, Brain/Body |
| `_Scripts/Systems/AnimationSystemGroup/` | [Systems_Animation.md](_Docs/Systems_Animation.md) | Layered quad animation, clip SO pipeline |
| `_Scripts/Systems/MovementSystemGroup/` | [Systems_Movement.md](_Docs/Systems_Movement.md) | Flowfield, D* Lite, horde formation |
| `_Scripts/Data/` | [Data.md](_Docs/Data.md) | ScriptableObject pattern, BlobAsset baking, enums |
| `_Scripts/Core/` | [Core.md](_Docs/Core.md) | Singletons, legacy files, what to avoid |
| `_Scripts/MonoBehaviours/` | [MonoBehaviours.md](_Docs/MonoBehaviours.md) | Managers, input, camera (non-ECS layer) |

---

## Current Blocker (as of 2026-03-22)

Newly spawned units **do not activate animations** and **do not seek waypoint interactions**. Waypoint interactions are the backbone of the AI system — units should always be seeking and executing interactions when idle. This is the primary in-progress problem.

Relevant files: `UnitSpawnerSystem.cs`, `AnimatorTargetInitSystem.cs`, `InteractionAssignmentSystem.cs`, `MotivationDegregationSystem.cs`.
