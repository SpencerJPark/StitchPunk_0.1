# Components — Context

Component files are **pure data structs**. No methods, no logic, no Unity API calls.

---

## Conventions

- `IComponentData` — single value or small fixed set of values on an entity.
- `IBufferElementData` — variable-length list on an entity (e.g. `ActionOption` buffer for scored actions).
- Group related components in one file (e.g. `AIComponents.cs`, `UnitComponents.cs`).
- Never put helper methods or logic inside a component. If you need a helper, put it in the matching `Utils/` file.

---

## Key Component Files

| File | Contains |
|---|---|
| `AIComponents.cs` | ActionOption buffer, SelectedAction, AIState |
| `Brains.cs` | BrainLink, BodyLink (cross-entity references) |
| `Motivations.cs` | Per-motivation need values (Hunger, Energy, etc.) |
| `Interactions.cs` | InteractionTarget, InteractionSlot, assignment state |
| `AnimationComponents.cs` | AnimationLayer (current clip, elapsed time, layer type) |
| `UnitComponents.cs` | UnitType, team, core identity data |
| `UnitVisualComponents.cs` | AnimationTarget references, MaterialPropertyBlock handles |
| `MovementComponents.cs` | Velocity, desired direction, horde offset |
| `SpawnerComponents.cs` | Pending spawn requests, pool references |
| `EntityLibraries.cs` | Singleton blob references (UnitLibrary, AnimationLibrary, etc.) |
