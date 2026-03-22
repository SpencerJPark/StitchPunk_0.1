# AISystemGroup — Context

The AI system is **motivation-based scoring**. Units do not follow a behaviour tree or state machine — they score every possible action every tick and execute the highest scorer.

---

## Pipeline

```
AIAwarenessSystemGroup
  MotivationDegradationSystem  — ticks down all 9 motivation values over time
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

---

## Motivations (MotivationType enum)

9 values: `Hunger`, `Energy`, `Comfort`, `Bladder`, `Fun`, `Social`, `Safety`, `Movement`, `SelfPreservation`.

Each degrades over time (rate configured in `AIScoringCurveSO`). When a motivation drops low enough its scoring system gives high scores to relevant interactions, pulling the unit toward satisfying it.

---

## Waypoint Interactions — Backbone of AI

**Interactions are the core of how units behave.** An interaction is any object in the world with `InteractionAuthoring` — a bed, toilet, chair, workbench, etc. Each interaction has:
- A `MotivationType` it satisfies
- One or more `InteractionSlot`s (capacity)
- A position the unit must walk to

Units should **always** have an active interaction target when idle. If a newly spawned unit is not seeking interactions, check:
1. `InteractionAssignmentSystem` — is the unit's entity being queried? Does it have the required components?
2. `MotivationDegradationSystem` — are motivations initialised with non-zero values so scoring produces results?
3. `AnimatorTargetInitSystem` — are animation targets initialised before the animation system runs?

---

## Brain / Body Split

- **Brain entity** — holds `Motivations`, `ActionOption` buffer, `SelectedAction`, AI state flags, `BodyLink` (points to body entity).
- **Body entity** — holds transform, animation layers, health, movement, `BrainLink` (points to brain entity).

Scoring systems query Brain entities. Execution systems use `BrainLink`/`BodyLink` to reach the other side when needed.

This split *may be merged* in a future refactor. Keep cross-entity lookups isolated so collapsing them is easy.

---

## Adding a New Brain Type

1. Create a new authoring script based on `CitizenBrainAuthoring.cs`.
2. Adjust default motivation values and scoring weights for the new unit personality.
3. Create a new brain prefab and assign it in the unit spawner.
4. The body prefab is shared — only the brain prefab changes per AI personality.
