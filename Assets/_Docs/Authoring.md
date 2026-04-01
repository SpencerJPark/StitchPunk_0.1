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
| `Ragdoll2DRootAuthoring.cs` | Place on root body entity only. Drag in `visualChild` and `joints` list. Baker writes `Ragdoll2DConfig` + `Ragdoll2DJointRef` buffer to root; `Ragdoll2DBakingSystem` then adds ragdoll components to the child entities |
| `ItemAuthoring.cs` | Bakes item identity + `ThrownItem` with per-item `throwSpeed`, `throwArc`, `throwDamage` |
| `PlayerAuthoring.cs` | Bakes player entity; assign `aimIndicator` child GO for the aim arrow visual |

---

## Cross-Entity Baking Pattern

A Baker can **only** call `AddComponent` / `AddBuffer` on the entity returned by `GetEntity()` for its **own** GameObject. Calling these on a different GO's entity throws `InvalidOperationException: Entity doesn't belong to the current authoring component`.

**Pattern for distributing components to child entities at bake time:**

1. Baker writes only to its own root entity (config + entity refs).
2. A `[WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]` system in `PostBakingSystemGroup` reads the config, iterates child entities, and calls `em.AddComponentData` on them.
3. Collect adds into a `NativeList` during the query — **do not call `em.AddComponentData` inside `SystemAPI.Query` iteration** (structural change during query = exception).

`Ragdoll2DRootAuthoring` + `Ragdoll2DBakingSystem` is the reference implementation of this pattern.
