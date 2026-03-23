# AISystemGroup — Context

The AI system is **motivation-based scoring**. Units do not follow a behaviour tree or state machine — they score every possible action every tick and execute the highest scorer.

---

## Pipeline

```
AIAwarenessSystemGroup
  MotivationDegregationSystem  — ticks down all 9 motivation values over time
  SpatialHashSystem            — indexes nearby entities for range checks

AIScoringSystemGroup           — one system per motivation, each writes ActionOption
  HungerScoringSystem          — scores Eat interactions by hunger need
  EnergyScoringSystem          — scores Sleep/Rest interactions
  ComfortScoringSystem
  BladderScoringSystem
  FunScoringSystem
  SocialScoringSystem
  SafetyScoringSystem
  MovementScoringSystem
  SelfPreservationSystem       — scores flee/defend based on health + threat

AISelectionSystemGroup
  ActionSelectionSystem        — picks from top 3 scored options (adds randomness)
  InteractionAssignmentSystem  — assigns the unit to a specific interaction slot

AIExecutionSystemGroup
  GenericInteractionExecutionSystem — moves unit to waypoint, triggers interaction
  BathroomExecutionSystem           — specialised execution for bladder interaction
```

### File Paths (relative to `_Scripts/Systems/AISystemGroup/`)

| System | File |
|---|---|
| `MotivationDegregationSystem` | `AIAwarenessSystemGroup/MotivationDegregationSystem.cs` |
| `SpatialHashSystem` | `AIAwarenessSystemGroup/SpatialHashSystem.cs` |
| `HungerScoringSystem` | `AIScoringSystemGroup/HungerScoringSystem.cs` |
| `EnergyScoringSystem` | `AIScoringSystemGroup/EnergyScoringSystem.cs` |
| `ComfortScoringSystem` | `AIScoringSystemGroup/ComfortScoringSystem.cs` |
| `BladderScoringSystem` | `AIScoringSystemGroup/BladderScoringSystem.cs` |
| `FunScoringSystem` | `AIScoringSystemGroup/FunScoringSystem.cs` |
| `SocialScoringSystem` | `AIScoringSystemGroup/SocialScoringSystem.cs` |
| `SafetyScoringSystem` | `AIScoringSystemGroup/SafetyScoringSystem.cs` |
| `MovementScoringSystem` | `AIScoringSystemGroup/MovementScoringSystem.cs` |
| `SelfPreservationSystem` | `AIScoringSystemGroup/SelfPreservationSystem.cs` |
| `ActionSelectionSystem` | `AISelectionSystemGroup/ActionSelectionSystem.cs` |
| `InteractionAssignmentSystem` | `AISelectionSystemGroup/InteractionAssignmentSystem.cs` |
| `GenericInteractionExecutionSystem` | `AIExecutionSystemGroup/GenericInteractionExecutionSystem.cs` |
| `BathroomExecutionSystem` | `AIExecutionSystemGroup/BathroomExecutionSystem.cs` |

---

## Motivations (MotivationType enum)

9 values: `Hunger`, `Energy`, `Comfort`, `Bladder`, `Fun`, `Social`, `Safety`, `Movement`, `SelfPreservation`.

Each degrades over time (rate configured in `AIScoringCurveSO`). When a motivation drops low enough its scoring system gives high scores to relevant interactions, pulling the unit toward satisfying it.

> ⚠ **Each motivation is a separate `IComponentData` struct** (e.g. `HungerMotivation`, `EnergyMotivation`). There is no single unified "Motivations" component. Each scoring system queries for exactly one motivation struct.

---

## Waypoint Interactions — Backbone of AI

**Interactions are the core of how units behave.** An interaction is any object in the world with `InteractionAuthoring` — a bed, toilet, chair, workbench, etc. Each interaction has:
- A `MotivationType` it satisfies
- One or more `InteractionSlot`s (capacity)
- A position the unit must walk to

Units should **always** have an active interaction target when idle. If a newly spawned unit is not seeking interactions, check:
1. `InteractionAssignmentSystem` — is the unit's entity being queried? Does it have `NeedsAction` **enabled**?
2. `MotivationDegregationSystem` — are motivations initialised with non-zero values so scoring produces results?
3. `AnimatorTargetInitSystem` — are animation targets initialised before the animation system runs?
4. `UnitSpawnerSystem` — confirms `NeedsAction` is explicitly enabled after ECB.Instantiate (enabled bits are not reliably copied).

---

## Brain / Body Split

- **Brain entity** — holds `IsBrain` tag, motivation components, `ActionOption` buffer, `SelectedAction`, `NeedsAction`, AI state flags, `BodyLink` (points to body entity).
- **Body entity** — holds `HasBrain` tag, transform, animation layers, health, movement, `BrainLink` (points to brain entity).

> ⚠ `BrainLink` is **not baked**. The body prefab has no `BrainLinkAuthoring`. `BrainLink` is added via ECB by `UnitSpawnerSystem` at spawn time. Do not assume it exists on prefab entities.

Scoring systems query Brain entities. Execution systems use `BrainLink`/`BodyLink` to reach the other side when needed.

This split *may be merged* in a future refactor. Keep cross-entity lookups isolated so collapsing them is easy.

---

## Adding a New Brain Type

1. Create a new authoring script based on `CitizenBrainAuthoring.cs`.
2. Adjust default motivation values and scoring weights for the new unit personality.
3. Create a new brain prefab and assign it in the unit spawner.
4. The body prefab is shared — only the brain prefab changes per AI personality.
