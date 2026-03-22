# Authoring — Context

Authoring scripts are **MonoBehaviours with nested Baker classes**. They exist only to convert scene/prefab data into ECS entities at bake time. No authoring script runs at runtime.

---

## Baker Pattern

```csharp
public class FooAuthoring : MonoBehaviour {
    public float someValue;

    public class Baker : Baker<FooAuthoring> {
        public override void Bake(FooAuthoring authoring) {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new FooComponent { Value = authoring.someValue });
        }
    }
}
```

- Use `GetEntity(TransformUsageFlags.None)` for static/data-only entities.
- Use `GetEntity(TransformUsageFlags.Dynamic)` for entities that move.
- Call `AddBuffer<T>(entity)` for `IBufferElementData` components.
- Use `DependsOn(so)` when baking from a ScriptableObject reference so incremental baking works correctly.

---

## Unit Prefab Structure

A unit is **two linked prefabs**:

- **Body prefab** — contains the visual hierarchy (layered quads), `UnitAuthoring`, `AnimatorAuthoring`, `UnitMoverAuthoring`, `HealthAuthoring`, `AttackAuthoring`, etc. This is what moves and gets animated.
- **Brain prefab** — contains `CitizenBrainAuthoring` (or equivalent), motivation components, action buffer. This is what makes decisions.

They are linked at bake time via `BrainLinkAuthoring` / `BodyLinkAuthoring`, which store a cross-reference entity on each side.

**When adding a new unit type:**
1. Duplicate an existing brain prefab and swap `CitizenBrainAuthoring` for the appropriate brain authoring.
2. Duplicate an existing body prefab and assign the correct `UnitSO` on `UnitAuthoring`.
3. Add the new `UnitType` enum value.
4. Add a `UnitSO` asset under `Assets/ScriptableObjects/Units/`.
5. Register it in `UnitLibraryAuthoring` so it gets baked into the `UnitLibraryBlob`.
6. Add an entry to the `AnimationLibrarySO` for any new animation clips.

---

## Key Authoring Files

| File | Purpose |
|---|---|
| `UnitAuthoring.cs` | Core unit identity — links to UnitSO, sets UnitType |
| `AnimatorAuthoring.cs` | Links animation library blob to the entity |
| `CitizenBrainAuthoring.cs` | Bakes motivation defaults and brain identity |
| `InteractionAuthoring.cs` | Marks an object as a waypoint interaction target |
| `UnitSpawnerAuthoring.cs` | Configures the spawner with prefab references |
| `UnitLibraryAuthoring.cs` | Bakes all UnitSOs into a unified BlobAsset |
