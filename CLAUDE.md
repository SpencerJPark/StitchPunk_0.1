# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**Stitch Punk** is a Unity 6000.3.1f1 game built with **DOTS (Data-Oriented Technology Stack)**. It is a real-time strategy/simulation game with unit AI, horde movement, and resource management. All core game logic uses Unity.Entities (ECS), Burst-compiled jobs, and data-driven ScriptableObjects.

## Commands

This is a Unity project — there are no CLI build/test commands. Development is done inside the Unity Editor:

- **Open project**: Open Unity Hub → Add → `C:\Users\spenc\Documents\GitHub\Stitch_Punk`
- **Unity version**: 6000.3.1f1 (install via Unity Hub if missing)
- **Main scene**: `Assets/Scenes/Game.unity`
- **Dev/test scene**: `Assets/Scenes/TestArea.unity`

## Architecture

### ECS Pattern (Authoring → Entity → System)

All gameplay logic follows the DOTS baker pattern:

```
MonoBehaviour (Authoring) → Baker.Bake() → IComponentData (on Entity) → ISystem (processes)
```

- **`Assets/_Scripts/Authoring/`** — MonoBehaviours with nested `Baker` classes that convert scene objects into ECS entities
- **`Assets/_Scripts/Components/`** — Pure data structs implementing `IComponentData` or `IBufferElementData`; no logic
- **`Assets/_Scripts/Systems/`** — All game logic lives here as `ISystem` or `SystemBase` classes

### System Group Execution Order

Systems are explicitly ordered via `[UpdateInGroup]` and `[UpdateAfter/Before]` attributes:

```
PostBakingSystemGroup        → converts ScriptableObjects → BlobAssets
GameManagerSystemGroup       → world-level management (floating origin)
AISystemGroup
  ├── AIAwarenessSystemGroup → perception (range checks)
  ├── AIScoringSystemGroup   → 9 motivation-based scoring systems
  ├── AISelectionSystemGroup → picks highest-scoring action
  └── AIExecutionSystemGroup → executes the chosen action
AnimationSystemGroup
  ├── AnimationAssignmentSystemGroup  → decides which clips to play
  └── AnimationExecutionSystemGroup  → advances keyframes and applies poses
MovementSystemGroup
  ├── MovementRoutingSystemGroup      → flowfield/D* Lite pathfinding
  ├── MovementCoordinatorSystemGroup  → horde formation offsets
  ├── MovementFollowerSystemGroup     → smooth path following
  └── MovementExecutionSystemGroup    → applies movement to transforms
BuildingsSystemGroup         → construction, harvesting, destruction
LateSimulationSystemGroup    → spawn/despawn, health UI
PresentationSystemGroup      → selection outlines
```

### Animation System

The animation system is entirely data-driven via ScriptableObjects, not Unity's Animator:

- **`AnimationClipSO`** — Contains `PartTrack[]` → `Keyframe[]` data (position, rotation, scale, image index, interpolation type, blend mode)
- **`AnimationLibrarySO`** — Lookup table mapping `AnimationType` enum → `AnimationClipSO`
- **`AnimationLibraryBakingSystem`** — Converts the SO data into `BlobAsset<AnimationLibraryBlob>` for Burst-safe access
- **`AnimationLayer` component** — One per entity; stores current clip, elapsed time, and layer type
- **`AnimationLayerType` enum** — 7 layers: `Base`, `Direction`, `Action`, `Face`, `Eyes`, `Mouth`, `Override`
- **`AnimationTarget` enum** — 36+ named body parts (the same enum used in both SOs and components)
- Blend modes (Additive/Override) and 5 interpolation modes are per-keyframe

New animation clips are created as `AnimationClipSO` assets under `Assets/ScriptableObjects/Animations/`, then registered in the `AnimationLibrarySO`.

### AI System (Motivation-Based)

AI uses independent scoring systems that produce action candidates:

- **`MotivationType` enum** — 9 needs: Hunger, Energy, Comfort, Bladder, Fun, Social, Safety, Movement, SelfPreservation
- Each motivation has its own system in `AIScoringSystemGroup` that writes scores to `ActionOption` buffer elements
- `AISelectionSystem` picks from the top 3 scored options (with randomness)
- Units have separate `BrainLink`/`BodyLink` entities; brain holds AI state, body holds physics/animation

### ScriptableObject Pattern

All SOs follow this structure — a typed list with a `Get(EnumType)` lookup:

```csharp
[CreateAssetMenu(fileName = "Name", menuName = "Path/To/Menu")]
public class NameSO : ScriptableObject {
    public List<ElementSO> elements = new List<ElementSO>();
    public ElementSO Get(EnumType type) { /* linear search by .type */ }
}
```

SOs are baked into BlobAssets in `PostBakingSystemGroup` so systems can access them from Burst jobs.

### Key Enums (in `Assets/_Scripts/Data/Enums/`)

| Enum | Location | Purpose |
|---|---|---|
| `AnimationType` | AnimationType.cs | 45+ animation identifiers |
| `AnimationTarget` | AnimationTarget.cs | 36+ body part names |
| `AnimationLayerType` | AnimationLayerType.cs | 7 animation layers |
| `ActionType` | ActionType.cs | 22 AI action types |
| `MotivationType` | MotivationType.cs | 9 AI needs |
| `UnitType` | UnitType.cs | MaleCitizen, FemaleCitizen, MaleZombie, FemaleZombie |
| `BuildingType` | BuildingType.cs | 7 building types |
| `Direction` | Direction.cs | 8-directional compass |

### Singleton Infrastructure (`Assets/_Scripts/Core/BaseClasses/`)

- `Singleton<T>` — Base; destroyed on scene load
- `PersistentSingleton<T>` — Survives scene loads
- `RegulatorSingleton<T>` — Managed lifecycle (destroys duplicates)

MonoBehaviour managers (Camera, Resources, Events) use these base classes.

### Rendering

Custom URP render features in `Assets/_Scripts/Core/RenderFeatures/`:
- **CelShadingFeature** — Cartoon/cel shading
- **SilhouetteOutlineFeature** — Selection highlight outlines
- **RobertsCrossRenderFeature** — Edge detection

### Design Documentation

`Assets/Obsidian/` contains Markdown design docs viewable in [Obsidian](https://obsidian.md). Open the vault at that folder for full context on game design intent.
