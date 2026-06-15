---
title: Verify Unit Design System (randomize / apply / persist / runtime re-skin)
status: active
created: 2026-06-14
area: code
---

## Goal

Confirm the Unit Design System works end-to-end in `Assets/Scenes/TestArea/DOTSTestScene.unity`. Code is committed (components, `DesignAuthoring`, `DesignRandomizeSystem`, `DesignApplySystem`, `DesignSystemGroup` + `DesignChangeSystem`, `DesignApplyUtil`), but the systems **no-op until a prefab carries `DesignAuthoring`** — so all runtime behavior depends on the Editor wiring below.

Spec: [`../Claude/Plans/UnitDesign_System.md`](../Claude/Plans/UnitDesign_System.md).

## Steps

### Compile + import (do this first)
- [ ] Re-enter the Unity Editor so scripts compile. Confirm **no compile errors** in the Console.
- [ ] Confirm **no duplicate-GUID warnings** — the `.cs.meta` GUIDs were hand-generated outside Unity. If Unity reports a collision, delete the offending `.meta` and let Unity regenerate it, then re-commit.
- [ ] Open the **Systems** window → confirm `DesignSystemGroup` exists between `HealthSystemGroup` and `AnimationSystemGroup`, and `DesignChangeSystem` is inside it. Confirm `DesignRandomizeSystem` + `DesignApplySystem` sit in `SpawnInitSystemGroup`.

### Editor wiring (one-time setup)
- [ ] Add `DesignAuthoring` to the citizen **body root** GameObject (sibling of the other unit authoring).
- [ ] Author a couple of parts in the `parts` list (e.g. `Head`, `Hat`) with valid `[min,max]` **human** ranges.
  - [ ] Confirm which texture-array indices are human vs zombie per part, so ranges only ever pick human slices.
  - [ ] Optionally add a second range to one part to confirm multi-range union picking.

### Spawn randomize + apply
- [ ] Enter Play. Select a spawned unit in the Entities Hierarchy.
  - [ ] `RandomizeDesign` is **disabled** (consumed on the first frame).
  - [ ] `PersistedDesign.slots` has one `DesignSlot` per declared part, each `imageIndex` inside that part's `[min,max]`.
  - [ ] The corresponding child quads visibly render the chosen slice (never an out-of-range/zombie slice).
- [ ] Spawn several units → they look visibly different.
- [ ] Kill + respawn (reclaim a pooled unit) → its look is **unchanged** (`RandomizeDesign` stayed off).

### Persistence round-trip
- [ ] Mark a unit a `Minion`. Trigger a save (autosave or manual `SaveRequest` via `SaveLoadBridge`).
  - [ ] The save-slot JSON carries a `PersistedDesign` `ComponentRecord` for that minion.
- [ ] Relaunch, `LoadRequest` → the minion respawns with the **same** indices (compare `PersistedDesign.slots` pre/post).
- [ ] A pre-design save (no `PersistedDesign` record) → the unit falls back to a fresh random roll (acceptable).

### Runtime re-skin (`ChangeDesignRequest`)
- [ ] On a spawned unit, fill `ChangeDesignRequest.changes` with a couple of entries (e.g. `Head`→X, `Body`→Y) and **enable** the component in the Entities inspector.
  - [ ] Next frame: the quads change to those **exact** indices, and `ChangeDesignRequest` flips back to **disabled**.
  - [ ] `PersistedDesign.slots` shows the new `imageIndex` for those targets **upserted** (overwritten, not duplicated); other parts unchanged.
- [ ] Save/reload that minion → it returns with the **changed** look (the re-skin persisted).
- [ ] Add a `changes` entry for a part the unit doesn't have (absent from `AnimatorTarget`) → it's skipped cleanly, request still consumes.

## Notes

Files (all committed this round):
- `Assets/_Scripts/Components/Units/DesignComponents.cs` — `DesignPart`, `DesignRange`, `DesignSlot`, `PersistedDesign` (`IPersist`), `ChangeDesignRequest`.
- `Assets/_Scripts/Utils/DesignApplyUtil.cs` — `ApplySlot` + `UpsertSlot` (shared by apply + change systems).
- `Assets/_Scripts/Authoring/Units/DesignAuthoring.cs` — MonoBehaviour + Baker.
- `Assets/_Scripts/Systems/LateSimulationSystemGroup/SpawnInitSystemGroup/DesignRandomizeSystem.cs`, `DesignApplySystem.cs`.
- `Assets/_Scripts/Systems/DesignSystemGroup/DesignChangeSystem.cs` + `DesignSystemGroup` in `Assets/_Scripts/Systems/SystemGroups.cs`.

Gotchas to watch:
- Design indices are meaningful for **non-animated / cosmetic** parts. A part driven by a flipbook animation track has its `ImageIndex` overwritten each frame by keyframes, so the design index won't stick on it (spec §7) — pick cosmetic parts (head shape, hat, skin) for the test.
- `DesignApplySystem` writes `AnimationTargetRestPose.baseImageIndex` (the no-animation source) **and** `ImageIndex{index, onUpdate=true}`. If a part renders the right slice for one frame then reverts, it's animation-driven — expected.
- No save-code changes were needed — `PersistedDesign` rides the generic `IPersist` path. If it does **not** appear in the save JSON, check `PersistRegistry` didn't filter it (it shouldn't: no Entity/Blob fields).

When everything passes: move this file to `Assets/_Vault/Tasks/Done/` and flip the spec status to ✔️ done.
