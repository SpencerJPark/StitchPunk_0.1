---
name: dots-authoring-baker
description: Scaffold a Unity DOTS MonoBehaviour + nested Baker pair for the Stitch Punk project following its authoring conventions — correct `TransformUsageFlags`, explicit `AddComponent` / `SetComponentEnabled` / `AddBuffer` ordering, `DependsOn(so)` when a ScriptableObject is referenced, and a cross-entity baking system in `PostBakingSystemGroup` when the baker needs to touch child entities. Use this skill whenever the user says "add authoring for X", "write a baker for Y", "create a MonoBehaviour that bakes Z", "I need to wire a new prefab into ECS", or any request that creates a new file under `Assets/_Scripts/Authoring/`. Also use when fixing an existing baker that mis-uses `TransformUsageFlags` or throws the "entity doesn't belong to the current authoring component" error.
---

# dots-authoring-baker

## What this skill does

Writes a new `Foo` MonoBehaviour + nested `Baker` class (or, when the baker needs to touch child entities, also writes a paired `FooBakingSystem` in `PostBakingSystemGroup`). This is the most bug-prone layer in the Stitch Punk codebase — wrong `TransformUsageFlags`, missed `SetComponentEnabled`, and cross-entity writes throw either at bake or the first frame after entering play mode, and the errors are often confusing.

## When to use it

Trigger whenever a new prefab, config, or scene marker needs to be converted to ECS entities. Examples:
- "Add a `CorpseCartAuthoring` that bakes a cart with a `ProductionProgress` enableable and an `InventorySlot` buffer."
- "Wire a `SpawnerMarkerAuthoring` MonoBehaviour so level designers can drop a spawner in the scene."
- "Write a baker for my new `TrapSO` ScriptableObject so the trap data gets onto the entity."
- "I need to bake ragdoll components onto the child joints of a unit — what pattern should I use?"

Don't trigger for pure system work (use `dots-system-scaffold` instead) or for ScriptableObject definitions alone (the baker portion is the trigger).

## What to read first

1. `Assets/_Vault/Memories/Code/Authoring.md` — the project's canonical authoring doc: the baker pattern, `TransformUsageFlags` rules, cross-entity pattern, key files table.
2. `Assets/_Vault/Memories/Code/RULES.md` — hard conventions (no `var`, explicit types, no logic in authoring beyond Bake).
3. `Assets/_Vault/Memories/Code/Gotchas.md` — especially the "Baker can only AddComponent on its own GO's entity" trap and the "structural changes during query iteration" trap.
4. `Assets/_Vault/Memories/Code/Components.md` if the new baker touches components you don't recognise yet — the enableable-component list is there.

## TransformUsageFlags — pick once, pick right

This is the single most-confused choice in bakers.

- `TransformUsageFlags.None` — the entity has NO transform components. Use for pure data/config entities (library holders, registries, settings singletons). Example: `GameDataAuthoring`.
- `TransformUsageFlags.Dynamic` — the entity has `LocalTransform` + `LocalToWorld` and can move at runtime. Use for units, projectiles, items, anything simulated. This is the default for 80% of bakers.
- `TransformUsageFlags.Renderable` — read-only transform, baked once from the scene. Use for static props that will never move. Rare.
- `TransformUsageFlags.NonUniformScale` — add this flag on top of Dynamic/Renderable if the GO has a non-uniform Unity scale that must be preserved.

If the GO needs to move, use `Dynamic`. If it will be a pure data holder (like a library entity), use `None`. When in doubt, Dynamic.

## The anatomy of a baker

Every baker in this project follows the same layered structure:

```csharp
using Unity.Entities;
using UnityEngine;

public class FooAuthoring : MonoBehaviour
{
    [Header("Section Label")]
    [Tooltip("What the field means, shown in inspector")]
    public float someValue = 1.5f;

    public int count;

    public FooSO configSo;

    public class Baker : Baker<FooAuthoring>
    {
        public override void Bake(FooAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            // 1) Plain component adds (always-on data)
            AddComponent(entity, new Foo
            {
                someValue = authoring.someValue,
                count     = authoring.count,
            });

            // 2) Enableable components — add, then explicitly set the enable state
            AddComponent<FooInProgress>(entity);
            SetComponentEnabled<FooInProgress>(entity, false);

            // 3) Buffers — add and optionally populate
            DynamicBuffer<FooEntry> entries = AddBuffer<FooEntry>(entity);
            for (int entryIndex = 0; entryIndex < authoring.count; entryIndex++)
                entries.Add(new FooEntry { value = entryIndex });

            // 4) If you referenced a ScriptableObject, declare the bake dependency
            if (authoring.configSo != null)
            {
                DependsOn(authoring.configSo);
                AddComponent(entity, new FooConfigReference
                {
                    config = authoring.configSo,   // UnityObjectRef<FooSO> ideally
                });
            }
        }
    }
}
```

Exemplars by difficulty:
- `Assets/_Scripts/Authoring/Units/HealthAuthoring.cs` — the smallest, cleanest baker (good starting point).
- `Assets/_Scripts/Authoring/Structures/FactoryStationAuthoring.cs` — buffer + enableable + worker-slot population loop.
- `Assets/_Scripts/Authoring/AI/Interactions/InteractionAuthoring.cs` — multiple enableables, a populated buffer derived from an array inspector field.

## Enableable components — `AddComponent` + `SetComponentEnabled` is one unit

Enableable components (`IEnableableComponent`) always add in TWO lines, not one: `AddComponent<T>(entity)` then `SetComponentEnabled<T>(entity, bool)`. Don't skip the second line — the default enable state is **true**, so forgetting to disable a component that's supposed to start disabled is a silent bug.

Rule of thumb by role:
- Request/command components (e.g., `SaveRequest`, `Heal`, `Revive`, `PickupTask`): start **disabled**.
- State tag components (e.g., `Alive`, `Dead`, `InteractionProvider`): set to the **initial state** — `Alive` enabled, `Dead` disabled.
- Presence flags baked by `UnitAuthoring` (e.g., `NewlySpawned`): baked **disabled**; enabled by the spawner at runtime.

When in doubt, check `Assets/_Vault/Memories/Code/Components.md` for that component's role, or grep for an existing baker that uses it.

## Buffers — `AddBuffer` returns the buffer so you can fill it

```csharp
DynamicBuffer<FooEntry> buffer = AddBuffer<FooEntry>(entity);
for (int entryIndex = 0; entryIndex < authoring.length; entryIndex++)
    buffer.Add(new FooEntry { /* ... */ });
```

If you don't need to populate it at bake time (many buffers are populated at runtime), just call `AddBuffer<T>(entity)` and ignore the return value — the slot is created.

## ScriptableObject fields — always `DependsOn(so)`

If the baker reads any field off a ScriptableObject reference, call `DependsOn(authoring.theSo)` before using its data. This registers the SO as a bake dependency so Unity re-bakes the entity when the SO changes. Skipping this causes stale baked data after editing the SO.

If you're baking SO data into a BlobAsset, that is a **separate skill** — see `dots-blob-library`. In that case the authoring stays minimal and a dedicated `PostBakingSystemGroup` system does the blob work.

## Cross-entity baking — when you must, use `PostBakingSystemGroup`

A `Baker<T>` may only call `AddComponent` / `AddBuffer` on the entity returned by `GetEntity()` for its **own** GameObject. If you need to write components onto child entities (e.g., ragdoll joints, per-quad animation targets), the baker stores the refs on its own entity and a `[WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]` system in `PostBakingSystemGroup` distributes them.

Reference implementation: `Ragdoll2DRootAuthoring` + `Ragdoll2DBakingSystem`.

**Critical detail** — inside the baking system, you cannot call `em.AddComponentData` while iterating `SystemAPI.Query`. Collect the (entity, component) pairs into a `NativeList<(Entity, TComponent)>` first, then apply them after the loop. See `Gotchas.md` "Structural changes are not allowed during SystemAPI.Query iteration".

## Naming and conventions

- Class name: `FooAuthoring` — always the `Authoring` suffix.
- File path: `Assets/_Scripts/Authoring/<Category>/FooAuthoring.cs` — match the existing category folders (`Units/`, `Items/`, `Structures/`, `AI/`, `AI/Interactions/`, `Animation/`, `Tags/`, `Save/`, `EntityLibraries/`, etc.).
- Inspector fields: `public` with `[Header]` / `[Tooltip]` — the authoring file is the only place in this codebase where designer-facing inspector metadata belongs.
- Baker class: **nested** inside the authoring, named `Baker` (not `FooAuthoringBaker`). The nested name already scopes it.
- **No `var`. Explicit types.** `Entity entity = GetEntity(...)`. Applies to bakers too.
- **No logic in authoring beyond Bake.** No `Update`, no `OnEnable`. If you find yourself wanting to write runtime logic here, it belongs in a system.
- **Do not declare `IComponentData` / `IBufferElementData` structs in the authoring file.** Components live in `Assets/_Scripts/Components/`, one (or a few closely-related) per file, and are `public struct` so every system + authoring can reference them. Defining them at the bottom of an authoring file looks tidy but it hides them from the rest of the codebase and creates inconsistency — `grep -r Foo Assets/_Scripts/Components` should find the truth of a component in one predictable place. When scaffolding, reference the component types (`AddComponent(entity, new Foo { ... })`) and leave the struct definitions to the components folder (or call them out as missing in your summary).

## Common mistakes — check before finishing

- [ ] Correct `TransformUsageFlags` (Dynamic for moving, None for data-only).
- [ ] Every enableable component got both `AddComponent<T>` AND `SetComponentEnabled<T>` — not just the first.
- [ ] Default enable state matches the component's role (request/command = disabled; state tag = correct initial state).
- [ ] `DependsOn(so)` called for every ScriptableObject the baker reads from.
- [ ] No attempt to `AddComponent` on any entity other than the one from `GetEntity()`. If you need that, switch to the cross-entity pattern.
- [ ] Buffers initialised with `AddBuffer<T>(entity)` — even if populated at runtime.
- [ ] No `var`, no single-letter semantic names.
- [ ] File is under `Assets/_Scripts/Authoring/<Category>/` matching an existing category folder.
- [ ] No runtime logic in the MonoBehaviour — only inspector fields + the nested `Baker` class.
- [ ] No `IComponentData` / `IBufferElementData` struct definitions in the authoring file — those live in `_Scripts/Components/`.

## Deeper references

- `references/cross-entity-bake.md` — full example of the `PostBakingSystemGroup` pattern with `NativeList` collection, for the rare case when one authoring must seed components onto child entities.
