---
name: dots-authoring-baker
description: Stitch Punk conventions for a MonoBehaviour + nested Baker under Assets/_Scripts/Authoring/ — the nested class is literally named Baker, enableable components default to enabled, component structs stay in _Scripts/Components/, plus the cross-entity PostBakingSystemGroup pattern.
---

# Authoring + Baker

Standard `TransformUsageFlags` / `AddComponent` / `DependsOn` usage is baseline DOTS — copy the shape of a
neighbouring file. Category folders under `_Scripts/Authoring/`: `AI/ Animation/ EntityLibraries/ Hazards/
Items/ Particles/ Player/ Registary/ Save/ Spawners/ Structures/ Tags/ Units/`.

## Project rules

1. **The nested baker class is named exactly `Baker`** — `public class Baker : Baker<FooAuthoring>`,
   not `FooAuthoringBaker`. All 71 existing bakers do this.
2. **Enableable components bake in as *enabled*.** Any request/command component must be explicitly
   `SetComponentEnabled<FooRequest>(entity, false)` in the baker, or it fires on frame one.
   State tags get set to their real initial state.
3. **Never declare an `IComponentData` struct in the authoring file.** Components live in
   `_Scripts/Components/`; the authoring file only references them.
4. The MonoBehaviour holds serialized fields and nothing else — no game logic beyond `Bake`.

## Touching other entities

A baker may only add components to *its own* entity. To reach children/other entities, write a baking
system in `PostBakingSystemGroup` with `[WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]`.

**Collect into a `NativeList` during the query, then apply after the loop** — a structural change
(`AddComponent`/`AddComponentData`) inside a live `SystemAPI.Query` invalidates the iterator and throws.
