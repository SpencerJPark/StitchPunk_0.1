---
name: dots-feature-group
description: Scaffold a complete new top-level feature system group for Stitch Punk — the GameSceneSystemGroup-derived group declaration in SystemGroups.cs (with explicit UpdateBefore/UpdateAfter pipeline edges), the matching Systems/<Name>SystemGroup/ folder, optional feature plug tag + FeatureConfigAuthoring checkbox, a contract row in _Vault/Memories/Code/Contracts.md, and the test registrations in SystemGroupOrderTests. Use this whenever the user says "add a new feature group", "create a WeatherSystemGroup", "new system group for X", "make X a pluggable feature", or a new domain needs its own top-level slot in the frame pipeline. Do NOT use for: adding a single system to an existing group (dots-system-scaffold), nested child groups inside an existing feature (edit SystemGroups.cs directly), or non-ECS features.
---

# dots-feature-group

## What this skill does

Creates a **feature** — not just a group. In Stitch Punk a feature is: a `ComponentSystemGroup` declared in the `SystemGroups.cs` manifest, a matching folder that holds all of its systems, explicit ordering edges into the frame pipeline, a scene-gating story, and a documented contract surface. Skipping any of these steps is how the codebase drifted before the 2026-07 structural pass; the conformance tests will fail the Test Runner if you do.

## Read first

1. `Assets/_Scripts/Systems/SystemGroups.cs` — the manifest and its header rules.
2. `Assets/_Vault/Memories/Code/Systems.md` — the pipeline diagram + "Structural conformance" section.
3. `Assets/_Vault/Memories/Code/Contracts.md` — how features talk to each other.
4. `Assets/_Vault/Memories/Code/RULES.md` — "System placement & structure" section.

## Checklist — every step is mandatory

### 1. Declare the group in SystemGroups.cs (never inline elsewhere)

```csharp
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(PreviousFeatureSystemGroup))]
[UpdateBefore(typeof(NextFeatureSystemGroup))]
public partial class WeatherSystemGroup : GameSceneSystemGroup { }
```

- Derive from **`GameSceneSystemGroup`** (top-level gameplay feature — inherits the `GameSceneTag` gate) or plain `ComponentSystemGroup` (world service / nested child).
- Place it in the file **at its pipeline position** — the file reads top-down in execution order.
- Add **both** explicit edges (`UpdateAfter` the predecessor, `UpdateBefore` the successor) so the pipeline cannot silently reorder. Update the pipeline comment block at the top of the file.
- Edges must never target a group with a different parent — Unity silently ignores cross-parent constraints (`SystemGroupOrderTests` fails on them).

### 2. Create the folder

`Assets/_Scripts/Systems/WeatherSystemGroup/` — name must equal the group type name exactly. Every system of the feature lives here (or in a child-group subfolder named after the child group). `SystemPlacementConformanceTests` enforces this.

### 3. Register in the order tests

`Assets/_Scripts/Tests/SystemGroupOrderTests.cs`:
- Insert the group at its position in `SimulationPipeline` (or `LateSimulationPipeline`).
- If it has nested child groups, add them to `ChildToParent`.

### 4. Decide the plug story

- Always-on feature: nothing extra — the `GameSceneSystemGroup` gate is enough.
- Scene-toggleable feature: add a `WeatherFeature : IComponentData` tag to `Components/Tags/FeatureTags.cs`, a checkbox to `FeatureConfigAuthoring`, and (once every playable subscene carries a `FeatureConfigAuthoring`) `RequireForUpdate<WeatherFeature>()` in a group `OnCreate` override.

### 5. Document the contract

- Add the feature's request/event components to `_Vault/Memories/Code/Contracts.md` (producers + consumer). Interaction with other features goes through these — never by writing another feature's internal state.
- Add the feature to the pipeline diagram in `_Vault/Memories/Code/Systems.md` and the folder map in `Assets/CLAUDE.md`.

### 6. Fill it

Use `dots-system-scaffold` for each system, `dots-blob-library` if the feature has SO-authored data, `dots-authoring-baker` for scene wiring. Systems declare only DATA requirements (`RequireForUpdate<WeatherLibrary>`) — never `GameSceneTag` (the group covers it).

## Verify

Compile clean (`mcp__UnityMCP__refresh_unity` then `mcp__UnityMCP__read_console`), then run the EditMode Test Runner — `SystemPlacementConformanceTests` + `SystemGroupOrderTests` green means the feature is structurally sound before it has any behavior.
