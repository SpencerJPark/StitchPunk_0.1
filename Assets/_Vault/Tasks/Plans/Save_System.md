# Save System — Design Spec

> **Status:** ◐ partially built — **Phases 1–3 + the bridge landed** (`PersistRegistry`, `SaveSerialization`, `PersistentSaveSystem`, `PersistentLoadSystem`, `MinionRestoreQueue`, `SaveLoadBridge`/`DebugSaveMenu`). **Remaining: Phase 4** (`EntityRemapSystem` — no `Entity`-field remap exists yet, so `UnitEquip`/`PlayerHordeSlot` refs do not survive a reload), **Phase 5** (`TravelAutoSaveSystem`; time autosave works), **Phase 6** (version tolerance, delete-slot, empty-slot handling). `PersistedDesignSlot` was never built — design round-trips some other way; confirm before reviving that ← DECISION.
> **Raw source:** [`../futureneedsplan.md`](../futureneedsplan.md) → "SaveSystem" (save data from data components, unique-minion permanence, time + travel autosave, manual save in menu UI)

---

**Skills Needed** (project skills in `.claude/skills/` — see [Skills index](../../../Memories/Code/Skills.md)):
- `dots-system-scaffold` — the new generic save/load/remap systems, the travel-distance autosave system, and the minion-respawn-on-load system (§5). Note: the serializer systems are **deliberately non-Burst** (managed `JsonUtility` / reflection / `System.IO`), mirroring the existing `SaveSystem`/`LoadSystem`.
- `dots-authoring-baker` — the `PersistId` baker addition on saved prefabs/authoring, and any new authoring for the travel tracker (§4). Likely a small edit to `GameDataAuthoring` rather than a new file.
- No `dots-blob-library` — saves are JSON DTO on disk, not baked blobs. No `dots-unit-ai` — no new AI behaviour.

---

## 1. Purpose & v1 scope

Replace the current **hand-written, player-only DTO** save path with a **generic, marker-driven serializer**: any `IComponentData`/`IBufferElementData` a designer tags with the `IPersist` marker interface is saved and restored automatically — no growing `PlayerSaveData`/`MinionSaveData` classes, and the pattern is transferable to other DOTS projects. On top of that, add **unique-minion permanence** (a minion's *design* = the per-body-part texture-array index values), a **travel-based autosave** trigger alongside the existing time-based one, and a **MonoBehaviour→ECS bridge** so the menu UI can trigger manual save/load by slot.

The system keeps the existing **request model** entirely: `SaveRequest`/`LoadRequest` are already `IEnableableComponent`s on the `GameDataTag` singleton (`_Scripts/Components/Save/GameDataComponents.cs`), enabled by triggers, consumed and disabled by the save/load systems. Nothing about *how the system is entered* changes — only *what gets serialized* (generic instead of hardcoded) and *what triggers it* (adds travel + UI).

**v1 handles:**
- Generic serialization of any component marked `IPersist` (value components **and** buffers), via reflection → named-field JSON.
- Stable cross-session identity via `PersistId` + an `Entity`-field remap pass on load.
- Player save/load re-expressed through the generic path (drops the bespoke `PlayerSaveData` field-copying in `SaveSystem`/`LoadSystem`).
- **Minion roster (shallow + design):** each owned minion persists its `UnitType`, transform, alive/health, and **design buffer** (per-part texture-array indices). On load, minions are respawned from the pool by `UnitType`, re-assigned their `PersistId`, then patched with their saved components + design.
- **Travel-based autosave:** a distance accumulator on the player; crossing a threshold enables `SaveRequest { slot = 0 }` (mirrors `AutoSaveTimerSystem`).
- **Manual save/load bridge:** a `PersistentSingleton<SaveLoadBridge>` MonoBehaviour that enables `SaveRequest`/`LoadRequest` with a chosen slot, plus a minimal slot panel.
- **Lightweight save header:** timestamp, total play time, scene/region label, slot label — enough to populate a load menu.

**Out of v1 (reserved hooks):**
- **Deep minion AI/motivation state** (`StateMachine`, `UtilityActions`, `MotivationState`, `ThreatEntry`). The generic path *can* carry these the moment they're marked `IPersist`; v1 deliberately leaves them unmarked so minions restore to a clean Idle. ← DECISION: confirm minions should restore idle rather than mid-behaviour.
- **The "random unit designs" generator itself** — this spec persists *whatever design indices a minion has*; it does not generate them. The `PersistedDesignSlot` buffer is the contract the future design system writes into.
- **Screenshot thumbnails** in the save header (header is text-only for now).
- **Roslyn source-generator** flavor of the serializer (flavor C in the planning discussion) — a later optimization; the marker interface is forward-compatible with it.

## 2. Architecture

**Generic marker-driven serializer (reflection → named-field JSON), main-thread / managed.** The existing save systems already omit `[BurstCompile]` because `JsonUtility` + `System.IO` are managed; the generic serializer extends that same managed island. Save is infrequent (autosave interval / travel threshold / manual press), so reflection cost is irrelevant and off the hot path.

```
                         ┌─ AutoSaveTimerSystem ─────┐ (time)
   triggers ────────────►├─ TravelAutoSaveSystem ────┤ enable SaveRequest{slot} on GameDataTag
                         └─ SaveLoadBridge (UI/MB) ──┘
                                   │
   GameDataTag singleton  SaveRequest (enableable) ──► PersistentSaveSystem
                                                          • query all entities w/ PersistId
                                                          • for each: intersect GetComponentTypes ∩ saveable set
                                                          • Marshal.PtrToStructure → JsonUtility.ToJson (+ enabled bit)
                                                          • buffers: element array → JSON
                                                          • write SaveFile{ header, entities[] } to slot path
                                   │
                          LoadRequest (enableable) ──► PersistentLoadSystem
                                                          • read SaveFile, group entity records by archetype role
                                                          • player: patch the existing singleton entity
                                                          • minions: respawn from pool by UnitType, then patch
                                                          • PASS 2: EntityRemapSystem rewrites Entity-typed fields by PersistId
```

The saveable type set is built **once** at `OnCreate`/first update by scanning `TypeManager`'s known types for managed `Type`s implementing `IPersist`, caching their `TypeIndex` + `ComponentType` + whether they're a buffer / enableable. Per-entity work then intersects that cached set with `EntityManager.GetComponentTypes(entity)`.

**Two hard DOTS constraints drive the design** (see §4):
1. **`Entity` fields are not portable across sessions** → every saved entity carries a stable `PersistId`; a load **pass 2** remaps any saved `Entity`-typed field by `PersistId` lookup.
2. **`BlobAssetReference` fields can't be blitted/restored generically** → they are excluded from serialization and re-resolved from the baked library by key (e.g. `UnitType` → `UnitDataLibrary`) on load.

**← DECISION:** raw component read uses an unsafe pointer path (`EntityManager.GetComponentDataRawRO`/`...RawRW` + `Marshal.PtrToStructure`/`StructureToPtr`). Some of these raw APIs are internal in the Entities package version in use — confirm at build time whether they're accessible, or whether a tiny `IJobChunk`-free reflection shim (`UnsafeUtility.AddressOf` over `GetComponentObject`-style access) is needed. This is the single riskiest build detail; spike it in Phase 1.

## 3. Entry points

Unchanged from the current implementation — the **request model** stays:

- **Save (one-shot)** — `SaveRequest : IComponentData, IEnableableComponent { int slot }` on the `GameDataTag` singleton. `slot 0` = autosave, `1–3` = manual. Enabled by a trigger, read + disabled same-frame by `PersistentSaveSystem`.
- **Load (one-shot)** — `LoadRequest : IComponentData, IEnableableComponent { int slot }` on the same singleton. Read + disabled by `PersistentLoadSystem`. (Existing call-order note holds: enable `LoadRequest` only after the game scene has spawned the player entity.)
- **Identity (persistent)** — **new** `PersistId : IComponentData { Unity.Entities.Hash128 value }` on every entity that should survive a reload (player, owned minions, and anything else later marked persistable). Baked onto authored entities; assigned at respawn for pooled minions.
- **Saveable marker (persistent, zero-size)** — **new** `interface IPersist : IComponentData { }` (and `IPersist` may also be implemented by `IBufferElementData` structs). Adding `: IPersist` to a struct is the entire opt-in.

## 4. Data model

**No SO→Blob library.** Saves are JSON files on disk (`SavePaths.GetSlotPath(slot)` → `Application.persistentDataPath/save_slot_<n>.json`, already exists). The DTO is **generic**, not per-domain:

```
SaveFile
├── int            version
├── SaveHeader     header        // timestamp (unix), totalPlaySeconds, sceneLabel, slotLabel
└── EntityRecord[] entities      // one per PersistId entity
        ├── string   persistId           // Hash128 as string
        ├── string   archetypeRole        // "Player" | "Minion" | ... (drives respawn strategy on load)
        ├── int      unitType             // for Minion: pool key to respawn from
        └── Component[] components
                ├── string componentType  // assembly-qualified type name (resilient lookup w/ fallback)
                ├── bool   enabled         // for IEnableableComponent; ignored otherwise
                ├── string json            // JsonUtility.ToJson of the boxed struct (value comp)
                └── string[] elements       // JsonUtility per element (buffer comp)
```

- **Config vs runtime:** there is no baked config here — everything written is *runtime* per-entity state captured at save time.
- **Excluded field types (skipped by the serializer, documented in `IPersist`'s XML doc):** `BlobAssetReference<>` (re-resolved by key on load), and any field the serializer can't round-trip. `Entity` fields are **kept** but flagged for the remap pass.
- **`Entity`-field remap:** during save, `Entity` field values are written as the **target entity's `PersistId`** (string), not the raw index/version. Load pass 2 (`EntityRemapSystem`) rewrites them to live `Entity` handles via a `PersistId → Entity` map built after all entities exist. Entities referenced but not themselves persisted serialize as `Entity.Null`.
- **Minion design buffer (new):** `PersistedDesignSlot : IBufferElementData, IPersist { int target; int textureIndex; }` — `target` is the `AnimationTarget` (body part), `textureIndex` is its slice into the part's `Texture2DArray`. This is the contract the future unit-design generator writes; the save system just round-trips it. On load it is re-applied to the respawned minion's body-part child entities (the `ImageIndex` / `AnimationTargetPose.imageIndex` of each `AnimatorTarget`, see `AnimatorTargetInitSystem`). ← DECISION: confirm whether design is stored as this dedicated buffer, or read directly off the existing per-part `ImageIndex` components at save time (no new buffer, but requires walking `AnimatorTarget` children during save).
- **New enums:** none required. `archetypeRole` ← DECISION: a small `SaveRole` enum vs a string. Recommend an enum in `_Scripts/Data/Enums/` for refactor safety.
- **Managed registry:** none — there are no `AudioClip`/`Sprite`/prefab references in saved components (those live in baked libraries, excluded above).

## 5. Systems

All live in `SaveSystemGroup` (`_Scripts/Systems/SaveSystemGroup/`), which already runs `OrderLast` in `LateSimulationSystemGroup` (`SystemGroups.cs:135`) — after all spawns/despawns/logic settle. Existing order: `PlayTimeTrackerSystem` (OrderFirst) → `AutoSaveTimerSystem` → `SaveSystem` → `LoadSystem`.

- **`PersistentSaveSystem`** *(replaces `SaveSystem`)* — `[UpdateAfter(AutoSaveTimerSystem)]`. On enabled `SaveRequest`: build/refresh saveable-type cache, query all `PersistId` entities, serialize each into an `EntityRecord` (value comps via `Marshal.PtrToStructure`+`JsonUtility.ToJson`; buffers element-by-element; `Entity` fields → target `PersistId`; capture enabled bits), assemble `SaveFile` + `SaveHeader`, write JSON to `SavePaths.GetSlotPath(slot)`. Disable `SaveRequest` first (same consume-immediately pattern as today). Non-Burst.
- **`PersistentLoadSystem`** *(replaces `LoadSystem`)* — `[UpdateAfter(PersistentSaveSystem)]`. On enabled `LoadRequest`: read + parse `SaveFile`. For `Player` records, patch the existing `Player` singleton entity in place (position/rotation/health/equipment slots/settings/play time — as today, but generically). For `Minion` records, enqueue a respawn (see next). Restore `PlayTimeTracker` + `GameSettings` onto `GameDataTag`. Hand off to the remap pass. Non-Burst.
- **`MinionRestoreSystem`** *(new)* — consumes the load's minion records: for each, reuse the `UnitSpawnerSystem` mechanism (ECB `Instantiate` of the `UnitPrefabEntry.bodyPrefab` for that `UnitType`, or pool reclaim), set `LocalTransform`, assign the saved `PersistId`, enable `NewlySpawned` so `AnimatorTargetInitSystem` rebuilds the body-part buffer, then patch saved components + apply `PersistedDesignSlot` indices to the part children. Runs after `PersistentLoadSystem`, before the remap pass. ← DECISION: spawn via a transient `UnitSpawner` entity (cheap reuse of existing system) vs a dedicated ECB path in this system.
- **`EntityRemapSystem`** *(new, load pass 2)* — runs last in the load sequence (a frame later if respawns settle on the next frame). Builds `NativeHashMap<Hash128, Entity>` from all live `PersistId`s, then rewrites every saved `Entity`-typed field on restored entities to the live handle. ← DECISION: single-frame vs deferred-one-frame (pooled respawns may not be addressable until the next `SpawnInitSystemGroup` pass).
- **`TravelAutoSaveSystem`** *(new)* — `[UpdateAfter(PlayTimeTrackerSystem)]`. Accumulates `distance += length(playerPos − lastPos)` each frame into a `TravelTracker` component on `GameDataTag`; when `distance ≥ threshold` and no `SaveRequest` is pending, enable `SaveRequest { slot = 0 }` and reset. Mirror of `AutoSaveTimerSystem`. ← DECISION: threshold distance (suggest 50 world units) — and whether travel + time both feed slot 0 or travel uses a separate "checkpoint" slot.
- **(unchanged)** `PlayTimeTrackerSystem`, `AutoSaveTimerSystem`.

## 6. MonoBehaviour bridge

**`SaveLoadBridge : PersistentSingleton<SaveLoadBridge>`** (`_Scripts/MonoBehaviours/` — `PersistentSingleton<T>` base exists at `_Scripts/Core/BaseClasses/PersistentSingleton.cs`). It does **not** serialize anything itself — it only flips the request components on the ECS singleton:

- `RequestSave(int slot)` / `RequestLoad(int slot)` — get the default `World`, find the `GameDataTag` singleton entity, `SetComponentData` the slot + `SetComponentEnabled<SaveRequest>(…, true)` (resp. `LoadRequest`). This is the seam the menu UI and the minimal slot panel call.
- `ReadHeaders()` — reads each slot file's `SaveHeader` block (no full deserialize) to populate the load menu (timestamp, play time, scene label).
- **Minimal slot UI:** a small panel listing slots 1–3 with their header info + Save/Load/Delete buttons, wired to the bridge. ← DECISION: full menu layout/visual design is deferred to the **Menu UI** task; v1 ships a functional-but-unstyled panel (or a debug overlay) sufficient to verify the round-trip.

## 7. Integration points

- **Existing save layer (replaced/extended):** `SaveSystem.cs` + `LoadSystem.cs` are superseded by the generic systems (keep `SavePaths.cs`, `AutoSaveTimerSystem.cs`, `PlayTimeTrackerSystem.cs`, `GameDataComponents.cs`). `SaveFile.cs` is rewritten to the generic DTO (drop `PlayerSaveData`/`SettingsSaveData` field lists). `GameDataAuthoring.cs` gains `TravelTracker` + (optionally) a `PersistId` for the singleton.
- **Player:** `Player`, `LocalTransform`, `Health`, `PlayerEquipmentSlots`, `UnitEquip` — these become `IPersist`-marked instead of hand-copied. `UnitEquip.equipItemEntity` (an `Entity`) exercises the remap pass; restoring the live equipped *item entity* still needs spawn/equip integration (the existing `LoadSystem` TODO) — track as a follow-up, the equipped *type* round-trips via `PlayerEquipmentSlots` regardless.
- **Units/minions:** `UnitData` (`UnitType`), `Health`, `Alive`/`Dead`, `Minion` tag, the `AnimatorTarget` body-part graph + `ImageIndex`/`AnimationTargetPose`, pool spawn path (`UnitSpawnerSystem`, `UnitPrefabEntry`, `PoolOwner`, `NewlySpawned`, `AnimatorTargetInitSystem`). Owned-minion selection ← DECISION: persist entities with `Minion` enabled, or only those the player owns (`PlayerUnitBrain`-eligible / in `PlayerHordeSlot`)?
- **Horde groups:** `PlayerHordeSlot` buffer holds `Entity` refs — these round-trip correctly *only* through the remap pass (Phase 4). If minion-group membership isn't needed in v1, leave `PlayerHordeSlot` unmarked.
- **Settings:** `GameSettings.animationFrameRate` persists via the generic path on the `GameDataTag` entity.
- **Dialogue:** `PlayedDialogue` buffer already lives on `GameDataTag` (baked in `GameDataAuthoring`). Marking it `IPersist` makes dialogue-seen state persist for free — ← DECISION: include in v1?
- **No new `SystemGroup`** — `SaveSystemGroup` already exists and is correctly placed.

## 8. Proposed file manifest

**New:**
- `_Scripts/Components/Save/PersistComponents.cs` — `IPersist` marker interface, `PersistId`, `TravelTracker`, `SaveRole` enum (if chosen).
- `_Scripts/Components/Units/UnitDesignComponents.cs` *(or extend existing)* — `PersistedDesignSlot` buffer.
- `_Scripts/Systems/SaveSystemGroup/PersistentSaveSystem.cs`
- `_Scripts/Systems/SaveSystemGroup/PersistentLoadSystem.cs`
- `_Scripts/Systems/SaveSystemGroup/MinionRestoreSystem.cs`
- `_Scripts/Systems/SaveSystemGroup/EntityRemapSystem.cs`
- `_Scripts/Systems/SaveSystemGroup/TravelAutoSaveSystem.cs`
- `_Scripts/Systems/SaveSystemGroup/SaveSerialization.cs` — the reflection/Marshal helpers (build saveable-type cache, component↔JSON, raw read/write, enabled-bit handling). Shared by save + load.
- `_Scripts/MonoBehaviours/SaveLoadBridge.cs`
- `_Scripts/UI/SaveLoadPanel.cs` *(minimal; or fold into an existing menu)*

**Edited:**
- `_Scripts/Data/Structs/SaveFile.cs` — rewrite to generic `SaveFile`/`SaveHeader`/`EntityRecord`/`ComponentRecord` DTO.
- `_Scripts/Authoring/Save/GameDataAuthoring.cs` — add `TravelTracker` (+ `PersistId` on the singleton); travel threshold field.
- Component structs to be persisted gain `: IPersist` — `Player`, `Health`, `PlayerEquipmentSlots`, `UnitEquip`, `UnitData`, `GameSettings`, `PlayedDialogue`, `PersistedDesignSlot`, (later) horde/AI components.
- `_Scripts/Authoring/...` for the player + minion body prefabs — add `PersistId` bake (stable Guid per authored entity).
- **Delete:** `SaveSystem.cs`, `LoadSystem.cs` (superseded). Preserve the player-restore semantics note in `_Vault/Memories/Code/Systems.md`.

**Assets:** none baked. (Save files are generated at runtime under `persistentDataPath`.)

## 9. Build phases

1. **Serializer core (spike the risk first).** `IPersist` + `PersistId` + `SaveSerialization` helpers. Prove the raw-read/write API path (the §2 ← DECISION) on **one** value component round-tripping through JSON in isolation. Build the saveable-type cache from `TypeManager`.
2. **Player parity, generically.** `PersistentSaveSystem`/`PersistentLoadSystem` saving + restoring the player singleton via the generic path — match what `SaveSystem`/`LoadSystem` do today, then delete the old two. Header written. Buffers supported (test with `PlayedDialogue`).
3. **Minion roster + design.** `MinionRestoreSystem` respawns minions from the pool by `UnitType`, re-assigns `PersistId`, applies `PersistedDesignSlot` to body parts. Verify a minion's design survives save→quit→load.
4. **Entity remap pass.** `EntityRemapSystem`; mark a component with an `Entity` field (e.g. `UnitEquip` or `PlayerHordeSlot`) and confirm references re-link after reload.
5. **Triggers + bridge + UI.** `TravelAutoSaveSystem`; `SaveLoadBridge` + minimal slot panel reading headers. Manual save/load by slot from UI.
6. **Polish.** Version/migration tolerance check (add a field to a saved struct, confirm old saves still load with the new field defaulted); delete-slot; error/empty-slot handling.

## 10. Verification

Test in `DOTSTestScene` (where `GameDataAuthoring` + a player + spawnable units exist):

- **Phase 1:** unit-test-style — a debug key serializes one component to a string and deserializes it back; log equality. Success = byte/field-identical round-trip.
- **Phase 2:** move the player, press debug-save (slot 1), move again, press debug-load (slot 1) → player snaps back to saved transform/health; `save_slot_1.json` on disk shows named fields + header. Success = same behaviour as the old `SaveSystem`, now with zero player-specific copy code.
- **Phase 3:** spawn a minion with non-default design indices, save, stop play, start play, load → minion reappears at saved position with the **same design** (visually identical parts). Inspect the JSON: design buffer present as element array.
- **Phase 4:** equip an item / assign a horde group, save, load → the `Entity` reference resolves to a live entity (not `Null`, not stale) in the Entities Hierarchy window.
- **Phase 5:** walk past the travel threshold → autosave fires (log + slot-0 file mtime updates); time threshold still fires independently; UI panel lists slots with correct header info and Save/Load buttons work.
- **Spencer-only (Editor):** confirm design visual fidelity after load, that the minimal UI is acceptable as a stand-in for the Menu UI task, and tune the travel threshold by feel.

## Open decisions (collected)

- [ ] §1 — Minions restore to clean **Idle** (deep AI state left unmarked) vs persist mid-behaviour.
- [ ] §2 — Raw component read/write API path: confirm `GetComponentDataRaw*` accessibility vs a reflection/`unsafe` shim (spike in Phase 1, highest risk).
- [ ] §4 — Design stored as a dedicated `PersistedDesignSlot` buffer vs read live off per-part `ImageIndex` at save time.
- [ ] §4 — `archetypeRole` as a `SaveRole` enum (recommended) vs a string.
- [ ] §5 — `MinionRestoreSystem` respawn path: transient `UnitSpawner` entity (reuse `UnitSpawnerSystem`) vs dedicated ECB.
- [ ] §5 — `EntityRemapSystem`: same-frame vs deferred one frame (pooled respawn addressability).
- [ ] §5 — Travel autosave threshold distance (suggest ~50 units); shares slot 0 with time autosave vs a separate checkpoint slot.
- [ ] §6 — Minimal functional slot panel now vs debug-key-only, with full layout deferred to the Menu UI task.
- [ ] §7 — Owned-minion selection: persist all `Minion`-enabled entities vs only player-owned (`PlayerHordeSlot` / `PlayerUnitBrain`).
- [ ] §7 — Include `PlayedDialogue` (dialogue-seen state) and `PlayerHordeSlot` (group membership) in v1?
