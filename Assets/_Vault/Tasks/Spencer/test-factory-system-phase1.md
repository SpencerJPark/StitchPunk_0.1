---
title: Test Factory System Phase 1 End-to-End
status: active
created: 2026-04-13
area: code
---

## Goal

Validate the ECS production loop after Phase 1 was built. The system needs real scene testing to confirm the baking pipeline, production ticks, input/output buffers, and worker slot checks all work correctly.

## Steps

### Asset setup
- [ ] Create a `_FactoryLibrary` SO asset (`Factory/FactoryLibrary`)
- [ ] Create a `PrepTable` `ProductionRecipeSO` asset — inputs: `[CorpseBody]`, outputs: `[CorpseBody]`, duration: `3s`, workersRequired: `1`
- [ ] Create an `AssemblyBench` `ProductionRecipeSO` asset — inputs: `[CorpseBody, MechScrap]`, outputs: `[FleshAutomaton]`, duration: `5s`, workersRequired: `1`
- [ ] Create a `GalvanicCharger` `ProductionRecipeSO` asset — inputs: `[FleshAutomaton]`, outputs: `[FleshAutomaton]`, duration: `4s`, workersRequired: `0` (runs automatically)
- [ ] Add all three recipes to the `_FactoryLibrary` SO

### Scene setup
- [ ] In a test scene, add a GO with `FactoryLibraryAuthoring` pointing to `_FactoryLibrary`
- [ ] Add a GO with `FactoryGridAuthoring` (cellSize: `1`, width: `10`, height: `10`)
- [ ] Add a station GO with `FactoryStationAuthoring` — type: `GalvanicCharger`, gridX: `0`, gridZ: `0`, workerSlots: `0`
- [ ] Add a station GO with `FactoryStationAuthoring` — type: `AssemblyBench`, gridX: `1`, gridZ: `0`, workerSlots: `1`

### Library bake check
- [ ] Enter Play mode — open the ECS inspector (Entities window) and confirm a `FactoryLibrary` singleton entity exists
- [ ] Confirm the blob is created (blob.IsCreated = true) and has 3 recipes

### Automatic station test (GalvanicCharger — workersRequired: 0)
- [ ] In the ECS inspector at runtime, manually add a `StationOutputSlot` entry with `itemType = FleshAutomaton` to the GalvanicCharger entity's output buffer... wait — add a `StationInputSlot` entry with `itemType = FleshAutomaton`
- [ ] Confirm `ProductionProgress` enables on the next frame
- [ ] Wait 4 seconds — confirm `ProductionProgress` disables and `StationOutputSlot` now contains `FleshAutomaton`

### Worker-gated station test (AssemblyBench — workersRequired: 1)
- [ ] Manually add `CorpseBody` and `MechScrap` to the `StationInputSlot` buffer
- [ ] Confirm production does NOT start (worker slot is empty — `Entity.Null`)
- [ ] Set the `StationWorkerSlot[0].workerEntity` to any non-null entity (e.g. the player entity)
- [ ] Confirm `ProductionProgress` enables and the inputs are consumed
- [ ] Wait 5 seconds — confirm `StationOutputSlot` contains `FleshAutomaton`

### Grid singleton check
- [ ] Confirm the `FactoryGridConfig` entity exists with correct width/height/cellSize
- [ ] Confirm the `FactoryGridCell` buffer has `width * height` entries (100 for a 10×10 grid), all `Entity.Null`

### Regression check
- [ ] No errors from existing systems — `BuildingsSystemGroup` runs without conflicts
- [ ] No errors when the scene has no factory stations (system no-ops cleanly)
- [ ] Entering play mode with no `FactoryLibraryAuthoring` in the scene — `ProductionSystem` no-ops via `RequireForUpdate<FactoryLibrary>` (no errors)

## Notes

Key files if something breaks:
- `ProductionSystem.cs` — `StartProductionJob` checks `workersRequired` then inputs; `TickProductionJob` ticks elapsed and writes outputs
- `FactoryLibraryBakingSystem.cs` — runs at bake time (PostBakingSystemGroup); if blob is empty, check that `_FactoryLibrary` SO has recipes and that `FactoryLibraryAuthoring` is in the scene
- `FactoryStationAuthoring.cs` — `ProductionProgress` is baked disabled; if it appears enabled at start, check the baker's `SetComponentEnabled` call
- `SystemGroups.cs` — `BuildingsSystemGroup` declared between `MovementSystemGroup` and `CombatSystemGroup`

Input/output buffers are `DynamicBuffer`s — you can edit them live in the Entities inspector during Play mode to simulate item delivery.
