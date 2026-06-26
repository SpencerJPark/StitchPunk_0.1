# Player Resource System — Design Spec

> **Status:** ✅ spec ready · edit the inline **← DECISION** markers, then hand back to start the build.
> **Raw source:** [`futureneedsplan.md`](futureneedsplan.md) → "Player resource system"

## Context

The braindump calls for "a system/entity that tracks the player's inventory and resources" — wood, scrap metal, corpses, currency — which the player accrues, spends on building/summoning, and grows storage for when setting up a base. It is a **foundational dependency** of the unbuilt Building, Trade, Caravan, and wave-summoning systems (all need to read/spend a player pool), so it should land first and self-contained.

Grounding showed **no live resource system exists** — the old `ResourceManager` (event-driven `Dictionary`), `ResourceTypeSO` (`Iron/Gold/Oil`), `ResourceAmount`, and `ResourceManagerUI` are all **commented out**. We start clean but reuse strong live patterns: the `Player` singleton tag, the `MotivationChangeRequest` delta-buffer mutation pattern, the `IPersist` auto-save opt-in, and the `DialogueUIManager` ECS→MonoBehaviour bridge.

This plan is the v1 data + mutation + save + minimal-HUD foundation. Factory/Building/Trade integration is explicitly deferred to those systems.

---

**Skills Needed** (project skills in `.claude/skills/` — see [Skills index](../../Memories/Code/Skills.md)):
- `dots-system-scaffold` — the `ResourceChangeSystem` consumer + the save-snapshot sync systems (§5).
- `dots-authoring-baker` — add the resource buffer/components to the player entity at bake (§4); a debug-source authoring for verification (§9).

> No `dots-blob-library` — there is no authored per-resource config worth blobbing in v1 (no prices/icons yet). Add it later if resource metadata (display name, icon, cap defaults) grows into a `ResourceType`-indexed library.

---

## 1. Purpose & v1 scope

A per-player resource ledger on the `Player` entity, mutated by a delta-buffer request and persisted via the save system. Other systems queue `ResourceChangeRequest` deltas (gain from harvest/factory, spend on build/summon); one consumer applies them clamped to `[0, cap]`. A thin HUD reads the pool each frame so it is observable on screen.

**v1 handles:**
- A `ResourceType` enum (`Wood, ScrapMetal, Corpse, Currency`, + `None`) — **← DECISION:** confirm the starter set / names.
- `ResourceStack` buffer on the `Player` entity = the live, extensible ledger (one entry per resource present).
- `ResourceChangeRequest` delta buffer (Add/Set, mirrors `MotivationChangeRequest`) as the only mutation entry point.
- A minimal per-resource capacity cap (`StorageCapacity`) that buildings/caravan can raise later — default effectively uncapped.
- A static affordability helper (`ResourceLedger.CanAfford` / `Get`) callers query before queuing a spend.
- `IPersist` save via a blittable snapshot component (see §4 — buffers aren't saveable in v1).
- A thin MonoBehaviour HUD readout (mirrors `DialogueUIManager`).
- A debug source (key/authoring) that queues deltas, to verify the loop end-to-end.

**Out of v1 (reserved hooks):** factory `OutputBay` → pool wiring (lands with Factory/Building; `FactoryItemType → ResourceType` mapping); building/summon spend callers; trade currency events; resource icons/prices library; atomic spend-rejection (v1 clamps).

## 2. Architecture

Pure ECS data + mutation, plus a read-only MonoBehaviour HUD bridge (ECS-decides / MonoBehaviour-displays — nothing crosses back into ECS from the HUD).

A new **`PlayerResourceSystemGroup`** is declared in `SystemGroups.cs`, `[UpdateAfter(BuildingsSystemGroup)]` + `[UpdateBefore(CombatSystemGroup)]` — so producer systems (factory/building, later) have run and emitted deltas before resources settle this frame, and downstream combat/spend logic sees the result.

```
producers (debug source now; factory/building later)
        └─ append ResourceChangeRequest entries (buffer on Player)
PlayerResourceSystemGroup
        └─ ResourceChangeSystem: sum deltas → apply to ResourceStack (clamp [0, cap]) → clear buffer
SaveSystemGroup (Late)
        └─ ResourceSaveSyncSystem: mirror ResourceStack → PlayerResourcesSnapshot (IPersist)  [save only]
        └─ ResourceLoadSyncSystem: mirror snapshot → ResourceStack on load
MonoBehaviour (LateUpdate): ResourceHudManager reads ResourceStack → updates HUD
```

**← DECISION:** group name `PlayerResourceSystemGroup` (vs `ResourceSystemGroup`).

## 3. Entry points

- **Mutation (persistent, on the Player entity)** — `ResourceChangeRequest : IBufferElementData { ResourceType resourceType; ResourceChangeType changeType; int value; }`. Any system appends entries; `ResourceChangeSystem` reads, applies, and `.Clear()`s the buffer each frame. Mirrors the live `MotivationChangeRequest` / `MotivationChangeRequestSystem` pair exactly.
- **Read (query helper)** — `ResourceLedger` static: `Get(in DynamicBuffer<ResourceStack>, ResourceType)`, `CanAfford(in DynamicBuffer<ResourceStack>, ResourceType, int)`. Burst-friendly, no allocation. Callers (build/summon) check before queuing a negative delta.

> No one-frame signal entity here — the player pool is a single well-known singleton (the `Player` entity), so a buffer on it is cheaper than spawning signal entities (the LoggingSystem pattern earns its keep only for many-target, transient effects).

## 4. Data model

Live runtime (extensible, enum-indexed by presence):
- `ResourceType` enum → `Assets/_Scripts/Data/Enums/ResourceType.cs` (new; the old commented `ResourceTypeSO.ResourceType` is replaced, not revived).
- `ResourceChangeType` enum `{ Add, Set }` → reuse the shape of the existing `MotivationChangeType` (do **not** re-declare if a generic Add/Set enum already exists — **← DECISION:** reuse `MotivationChangeType` vs a dedicated `ResourceChangeType`).
- `ResourceStack : IBufferElementData { ResourceType resourceType; int amount; }` — on `Player`.
- `StorageCapacity : IBufferElementData { ResourceType resourceType; int cap; }` — on `Player`; missing entry ⇒ treat as uncapped (`int.MaxValue`). Building/caravan systems raise `cap` later.

Save target (v1 buffers are **not** IPersist-able — confirmed in `PersistComponents.cs`: "Buffers are not handled in v1", and no `Entity`/`BlobAssetReference` fields allowed):
- `PlayerResourcesSnapshot : IComponentData, IPersist` — a **fixed blittable** mirror of the ledger. **← DECISION:** representation — (a) named int fields per starter resource (`wood/scrap/corpse/currency`) — simplest, blittable, but adding a resource edits the struct; or (b) a fixed-capacity inline array (`FixedList…` of `int` indexed by `ResourceType`) — extensible but ties save layout to enum order. Recommend (a) for v1 (small fixed set), revisit if the enum grows.
- `ResourceSaveSyncSystem` writes `ResourceStack → snapshot` just before `PersistentSaveSystem`; `ResourceLoadSyncSystem` writes `snapshot → ResourceStack` just after `PersistentLoadSystem`. Both in `SaveSystemGroup`.

No managed registry needed in v1 (no icons/clips). No blob library in v1.

## 5. Systems

| System | Group | Reads | Writes |
|---|---|---|---|
| `ResourceChangeSystem` | `PlayerResourceSystemGroup` | `ResourceChangeRequest` buffer, `StorageCapacity` | mutates `ResourceStack` (clamp `[0, cap]`), clears request buffer |
| `ResourceSaveSyncSystem` | `SaveSystemGroup` (`UpdateBefore` `PersistentSaveSystem`) | `ResourceStack` | `PlayerResourcesSnapshot` |
| `ResourceLoadSyncSystem` | `SaveSystemGroup` (`UpdateAfter` `PersistentLoadSystem`) | `PlayerResourcesSnapshot` | `ResourceStack` |
| `DebugResourceSourceSystem` *(verify-only, `#if UNITY_EDITOR` or debug tag)* | `PlayerResourceSystemGroup` (`OrderFirst`) | a debug key / `DebugResourceSource` authoring | appends `ResourceChangeRequest` |

All `ISystem` + `[BurstCompile]`, `state.RequireForUpdate<Player>()` (and `GameSceneTag` per project convention). Single-target work on the `Player` singleton; if iterating requests use `.Schedule()` (never `.Run()`). `[ReadOnly]` from `Unity.Collections`. No `var`, explicit types.

**← DECISION:** does `ResourceChangeSystem` clamp at write time only, or also prune zero-amount `ResourceStack` entries (keep the buffer compact)? Recommend keep entries (stable indices, fewer structural ops).

## 6. MonoBehaviour bridge

`ResourceHudManager : PersistentSingleton<ResourceHudManager>` (or plain MonoBehaviour — **← DECISION:** match how `DialogueUIManager` is instantiated). In `Start()` cache `EntityManager` + the `Player` singleton entity (`CreateEntityQuery(ComponentType.ReadOnly<Player>()).GetSingletonEntity()`). In `Update()`/`LateUpdate()` read `GetBuffer<ResourceStack>(playerEntity)` and push counts to the UI widgets. Read-only — never writes ECS.

**← DECISION:** HUD tech — a minimal uGUI/TextMeshPro readout (fastest, matches the "minimal" intent) vs Rive vs UI Toolkit. Recommend minimal TMP labels for v1; restyle in the Game-UI pass.

## 7. Integration points

- **Player entity** — `PlayerControllerAuthoring.Baker` (`Assets/_Scripts/Authoring/Player/PlayerControllerAuthoring.cs`) adds `ResourceStack`, `StorageCapacity`, `ResourceChangeRequest` buffers + `PlayerResourcesSnapshot`. Sits alongside existing `Player`, `PlayerEquipmentSlots`, `UnitEquip`.
- **Save** — opt-in via `IPersist` on `PlayerResourcesSnapshot`; `PersistRegistry` auto-includes it. No save-format edits beyond the new component. Verified against `Save_System.md` limits (blittable, no Entity/Blob, no buffer).
- **Future producers** — factory `ProductionSystem` (`Assets/_Scripts/Systems/BuildingsSystemGroup/`, currently commented out) and building harvesters append `ResourceChangeRequest` when revived; a `FactoryItemType → ResourceType` map lives with that work, not here.
- **Future spenders** — build placement, wave summoning, trade buyers call `ResourceLedger.CanAfford` then queue a negative `Add` delta.

## 8. Proposed file manifest

**New:**
- `Assets/_Scripts/Data/Enums/ResourceType.cs`
- `Assets/_Scripts/Components/Player/ResourceComponents.cs` — `ResourceStack`, `StorageCapacity`, `ResourceChangeRequest`, `PlayerResourcesSnapshot` (+ `ResourceChangeType` if not reusing `MotivationChangeType`)
- `Assets/_Scripts/Utils/ResourceLedger.cs` — static `Get` / `CanAfford`
- `Assets/_Scripts/Systems/PlayerResourceSystemGroup/ResourceChangeSystem.cs`
- `Assets/_Scripts/Systems/SaveSystemGroup/ResourceSaveSyncSystem.cs` + `ResourceLoadSyncSystem.cs`
- `Assets/_Scripts/Systems/PlayerResourceSystemGroup/DebugResourceSourceSystem.cs` *(verify-only)*
- `Assets/_Scripts/MonoBehaviours/ResourceHudManager.cs`
- `Assets/_Scripts/Authoring/Debug/DebugResourceSourceAuthoring.cs` *(verify-only, optional)*

**Edited:**
- `Assets/_Scripts/Systems/SystemGroups.cs` — declare `PlayerResourceSystemGroup`
- `Assets/_Scripts/Authoring/Player/PlayerControllerAuthoring.cs` — bake the new buffers/component onto the player
- Vault: `Assets/_Vault/Memories/Code/Components.md`, `Systems.md`, and `Assets/CLAUDE.md` status block; register row in `Plans/README.md`

**Assets:** a HUD prefab/Canvas with TMP labels; a debug-source GameObject in `DOTSTestScene`. (No SOs in v1.)

## 9. Build phases

1. **Data layer** — `ResourceType` enum, `ResourceComponents.cs`, `ResourceLedger`. Bake the buffers onto the player in `PlayerControllerAuthoring`. Confirm they appear in the Entities inspector on `Player`.
2. **Mutation path end-to-end** — `PlayerResourceSystemGroup` + `ResourceChangeSystem` + `DebugResourceSourceSystem`. Press the debug key → watch `ResourceStack.amount` change (clamped) in the inspector.
3. **Save round-trip** — `PlayerResourcesSnapshot` (`IPersist`) + the two sync systems. Save, change values, load → values restored.
4. **HUD** — `ResourceHudManager` + Canvas; counts render and update live on screen.

## 10. Verification

Play `DOTSTestScene`:
- **Ph1:** select the `Player` entity in the Entities window → the three new buffers + snapshot are present.
- **Ph2:** trigger `DebugResourceSourceSystem` (debug key / authoring) → `ResourceStack` amounts move and **clamp at 0 and at `cap`**; over-spend floors at 0; the `ResourceChangeRequest` buffer empties each frame.
- **Ph3:** via `SaveLoadBridge.RequestSave`, save → mutate → load → amounts return to saved values; confirm `PlayerResourcesSnapshot` matches `ResourceStack` post-save.
- **Ph4:** HUD labels match the inspector counts and update in real time.
- **Compile gate:** clean `Unity_GetConsoleLogs` (no `CS####` / Burst `BC####`) after each phase.
- **Spencer-only (Editor):** building the HUD Canvas/prefab and visual styling; confirming the debug source wiring.

## Open decisions (collected)
- [ ] §1/§4 — confirm starter `ResourceType` set & names (`Wood, ScrapMetal, Corpse, Currency`).
- [ ] §2 — group name `PlayerResourceSystemGroup` vs `ResourceSystemGroup`.
- [ ] §4 — reuse `MotivationChangeType` for Add/Set vs dedicated `ResourceChangeType`.
- [ ] §4 — snapshot representation: named int fields **(recommended)** vs fixed inline array.
- [ ] §5 — prune zero-amount `ResourceStack` entries or keep them (recommend keep).
- [ ] §6 — `ResourceHudManager` as `PersistentSingleton<T>` vs plain MonoBehaviour; HUD tech (recommend TMP).
