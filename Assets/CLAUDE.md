# Stitch Punk — CLAUDE.md

This file is the entry point for Claude Code. **Before working in any folder, read the relevant CONTEXT.md listed below. Be sure to keep these updated in the _Docs directory as these are meant to bee tools for you, so if you can or create a new directorty/script, make sure the docs reflect that to help you further down the line**

**You are going to help me by playing the role of an expert when it comes to coding and dots in game development**
---

## Game Overview Context

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

## Coding Conventions

Always write code explicitly, never use var
As a reminder code using [Readonly] needs to import from Unity.Collection
Preference using EntityJobs were it make sense in systems

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

## Current Status (as of 2026-04-04)

**Save system added.** `SaveSystemGroup` (OrderLast in `LateSimulationSystemGroup`) handles auto-save, manual save, and load via `SaveRequest` / `LoadRequest` enableable components on the GameData entity. Save files are JSON at `Application.persistentDataPath/save_slot_N.json`. See `Systems/SaveSystemGroup/`.

**GameData singleton entity** (`GameDataAuthoring`) is the single baked entity that carries all persistent game data components: `SaveRequest`, `LoadRequest`, `AutoSaveTimer`, `PlayTimeTracker`, `GameSettings`. Place one `GameDataAuthoring` GO in every game scene.

**`GlobalGameData` is now a static class** — no MonoBehaviour, no scene instance needed. All values are `const` (layer indices, pathfinding costs, scoring resolution). Designer-tweakable values (e.g. `animationFrameRate`) live in `GameSettings` on the GameData entity instead.

**Throw system fix.** Items now ignore units within 1.2 units of the throw origin — prevents standing next to a dead body from immediately blocking the throw. Walls are unaffected (no `Health` component).

**Animation fix shipped.** Root cause: `AnimatorTargetInitSystem` used `BaseParent` matching to rebuild the `AnimatorTarget` buffer after spawn. This silently skipped any quad whose `characterRoot` inspector field didn't point to the exact body root GO. Only the eyebrow (the one correctly configured quad) animated.

**Fixes applied:**
- `AnimatorAuthoring.Baker` now populates `AnimatorTarget` at bake time via `GetComponentsInChildren` — fixes scene entities permanently.
- `AnimatorTargetInitSystem` now uses `DynamicBuffer<LinkedEntityGroup>` instead of `BaseParent` matching — reliable for all prefab hierarchies.
- `AnimationTargetNoIndexAuthoring.Baker` now initialises `AnimationTargetPose` to rest pose (was zeros — caused quads to snap to local origin on spawn frame).

**Revival mechanic wired.** Player can equip a Reviver (`ItemType.Reviver`) in a slot. `PlayerEquipmentInputSystem` → `OnPlayerReviverEquipt` → `PlayerReviverSystem` enables `Revive` on the player's current `Target`. `ReviveSystem` (HealthSystemGroup) restores health and flips Dead/Alive/Undead. `PlayerEquipmentAuthoring` bakes the enableable event components onto the player entity.

**Minion command system — COMPLETE.** All 6 steps shipped:
1. `PlayerControlled` + `PlayerOrder` in `AIComponents.cs`; `ZombieBrainAuthoring` bakes them disabled
2. `PlayerMinionCommandComponents.cs` (`OnMinionMoveCommand`, `OnMinionInteractCommand`) baked disabled on player via `PlayerAuthoring`
3. `UnitSelectionManager` rewritten — box/click selects `Minion`-enabled bodies only; writes command components to player entity on right-click
4. `MinionCommandSystem` in `PlayerEquipmentSystemGroup` — fans orders to selected brains, issues `PathRequest` on bodies
5. `[WithDisabled(typeof(PlayerControlled))]` added to `ActionSelectionJob`
6. `MinionAutoCounterSystem` in `CombatReactionSystemGroup` — releases `PlayerControlled` when minion takes a hit

**Brain swap on revive — COMPLETE.** `SwapBrainSystem` (HealthSystemGroup, after ReviveSystem): destroys old citizen brain, instantiates zombie brain prefab from `UnitPrefabEntry` (keyed by `UnitType.MaleZombie` / `FemaleZombie`), cross-links body ↔ new brain, enables `Minion` on body. `SwapBrainRequest` (enableable) is baked disabled on all units with `UndeadAuthoring`; `PlayerReviverSystem` enables it alongside `Revive`.

**⚠ BUG — NPC AI broken after minion command system.** Citizens no longer seek interactions from the start.
- **Root cause:** `[WithDisabled(typeof(PlayerControlled))]` on `ActionSelectionJob` only matches entities that *have* `PlayerControlled` present AND disabled. Citizens never had `PlayerControlled` baked → they are excluded from the query entirely.
- **Fix:** Add `PlayerControlled` (disabled) to `BrainBakeHelper.AddRequirements` so every brain type has the component. `ActionSelectionJob`'s filter will then correctly pass citizens (disabled) and skip player-controlled minions (enabled).

**⚠ NEEDED — Rive update + minion selection UI.**
- Rive package needs updating to the newest version before `UnitSelectionBoxUI` can be verified.
- Minion selection indicator UI needs work so players can see which minions are selected and issue commands clearly. Wire up `Selected` visual and verify `SelectedVisualSystem` works with minion bodies.

**AI behaviors** — needs verification. All wiring looks correct (motivation components, `NeedsAction` explicitly enabled by spawner, `BodyLink`/`BrainLink` cross-links set by spawner). After the `PlayerControlled` bake fix above is applied, re-verify: (1) `awarenessRange` on `CitizenBrainAuthoring` in the brain prefab inspector — must be > 0; (2) motivation starting values vs the `AIScoringCurveSO` curve shape (units with all motivations at 0 may need time to deplete before scores are non-zero).
