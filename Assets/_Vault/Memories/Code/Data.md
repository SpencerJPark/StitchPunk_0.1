---
tags: [memory, code, data, scriptableobjects, blobAssets]
related: "[[Authoring]], [[Components]], [[Systems]]"
---

# Data — Context

All runtime data is either an **enum**, a **ScriptableObject**, or a **BlobAsset**. SOs are editor-time only; BlobAssets are the Burst-safe runtime form. See [[RULES]] for the mandate against managed types in jobs, and [[Authoring]] for the baker pattern that bridges SO → BlobAsset.

---

## ScriptableObject Pattern

Every SO is a typed list with a `Get(EnumType)` lookup:

```csharp
[CreateAssetMenu(fileName = "FooLibrary", menuName = "StitchPunk/Foo Library")]
public class FooLibrarySO : ScriptableObject {
    public List<FooSO> elements = new List<FooSO>();

    public FooSO Get(FooType type) {
        foreach (FooSO fooSO in elements) {
            if (fooSO.type == type) return fooSO;
        }
        return null;
    }
}
```

- The enum value on each element SO is the lookup key.
- Linear search is fine — this only runs at bake time, never at runtime.

---

## BlobAsset Baking Pattern

Runtime systems never access SOs. Instead, `PostBakingSystemGroup` bakes each SO into a BlobAsset stored on a singleton entity. See [[Systems]] for where PostBakingSystemGroup sits in the execution order.

1. **SO** (`Data/SOs/`) — editor-facing data
2. **Blob struct** (`Data/Structs/`) — Burst-safe mirror of the SO data
3. **Baking system** (`Systems/PostBakingSystemGroup/`) — reads SO, writes blob, stores on singleton
4. **Runtime system** — calls `SystemAPI.GetSingleton<LibraryComponent>()` to get the blob ref

The blob holder [[Components]] (e.g. `AnimationLibrary`, `UnitDataLibrary`) are singletons documented in `EntityLibraries.cs`.

---

## Key SOs

| SO | Enum Key | Purpose |
|---|---|---|
| `UnitLibrarySO` + `UnitSO` | `UnitType` | Unit stats, prefab refs, base motivation weights |
| `AnimationLibrarySO` + `AnimationClipSO` | `AnimationType` | All animation clip data — used by [[Systems_Animation]] |
| `AttackLibrarySO` + `AttackSO` | `AttackType` | Attack stats, ranges, damage |
| `AIScoringLibrary` + `AIScoringCurveSO` | `MotivationType` | Scoring curve shapes per motivation — used by [[Systems_AI]] |
| `BuildingTypeListSO` + `BuildingTypeSO` | `BuildingType` | Building stats and prefab refs |
| `ResourceTypeListSO` + `ResourceTypeSO` | `ResourceType` | Resource display names, icons, caps |
| `ItemLibrarySO` + `ItemSO` | `ItemType` | Item `ItemCategory` + effect data (heal amount, satisfied motivation, restoration, pickup range) — used by `ItemAwarenessSystem` / `PickupItemActionSystem`. Baked by `ItemLibraryBakingSystem` into `ItemLibraryBlob` (holder: `ItemLibrary`) |

---

## Key Enums (`Data/Enums/`)

| Enum | Values | Used For |
|---|---|---|
| `AnimationType` | 45+ | Identifies every animation clip |
| `AnimationTarget` | 36+ | Names every animatable body part quad |
| `AnimationLayerType` | 7 | Base / Direction / Action / Face / Eyes / Mouth / Override |
| `ActionType` | 22 | AI action identifiers |
| `MotivationType` | 9 | Hunger / Energy / Comfort / Bladder / Fun / Social / Safety / Movement / SelfPreservation |
| `UnitType` | 4 | MaleCitizen / FemaleCitizen / MaleZombie / FemaleZombie |
| `BuildingType` | 7 | Building categories |
| `Direction` | 8 | N / NE / E / SE / S / SW / W / NW |
| `ItemType` | 10 | Held + consumable item types (weapons, Bandage/MedKit/Bread/Water) |
| `ItemCategory` | 5 | None / Weapon / Healing / Food / Drink — drives `PickupItemActionSystem` effect branch |

---

## UnitSO vs UnitLibrarySO

`UnitSO` is a per-unit-type data asset (stats, prefab refs, default motivation values). `UnitLibrarySO` is the registry that holds all `UnitSO`s and bakes them together into `UnitLibraryBlob`. When adding a new unit type, you need both — see [[Authoring]] for the full setup steps.

`UnitSO.becomesUnitType` / `UnitDataBlob.becomesUnitType` (`UnitType`, default `None`) declares the unit's revived/converted form (e.g. `MaleCitizen → PlayerZombie`). `None` = does not convert. Read by `ReviveRequestSystem` to drive `SwapBrainSystem` (see [[Systems]]).

---

## Global Constants (`Data/GlobalGameData.cs`)

`GlobalGameData` is a **plain `static class`** — no MonoBehaviour, no scene instance. All values are `const` and are inlined by the compiler, making them safe in Burst jobs.

```csharp
// Physics layers
GlobalGameData.GROUND_LAYER             // 3
GlobalGameData.UNITS_LAYER              // 6
GlobalGameData.WALLS_LAYER              // 8
// ... (see file for full list)

// Pathfinding costs
GlobalGameData.WALL_COST                // byte.MaxValue
GlobalGameData.DEFAULT_COST             // 1

// AI
GlobalGameData.SCORING_CURVE_RESOLUTION // 32
```

Designer-tweakable values (e.g. `animationFrameRate`) are **not** here — they live in `GameSettings` on the GameData entity (see [[Components]]) and are saved/loaded per slot.

---

## BlobLibraryUtils (`Data/Structs/BlobLibraryUtils.cs`)

Static helper class used by `PostBakingSystemGroup` baking systems. Provides:
- `BuildEnumLookup<TSO>()` — builds `Dictionary<int, TSO>` from a SO list (handles null entries)
- `EnumCount<TEnum>()` — returns declared value count for an enum
- `FillWithPreFill<TSO, TItem>()` — pre-fills all slots with defaults, then overwrites from SO list (Attack pattern)
- `FillWithLookup<TSO, TItem>()` — fills slots via Dictionary, calling mapper or defaultFactory per slot (Item pattern)

`SystemAPI.Query<T>()` is source-generator–bound to the ISystem struct, so the query loops (SO extraction, blob assign, dispose) must stay inline in each system.

---

## Save File DTOs (`Data/Structs/SaveFile.cs`)

Plain C# classes with `[Serializable]` for `JsonUtility`. **These hold only JsonUtility-safe primitives** (strings, ints, arrays) — `JsonUtility` still can't serialize `float3`/`quaternion`/`Entity`, so component state is captured generically as **Base64 raw bytes** inside `ComponentRecord.data` (the ECS types ride inside the blob, never as raw fields). The hand-written `PlayerSaveData`/`SettingsSaveData` are gone — see the generic serializer in [[Systems]] (SaveSystemGroup).

```
SaveFile
    int             version
    SaveHeader      header
    EntityRecord[]  entities

SaveHeader
    long    timestampUnix
    double  totalPlaySeconds
    string  sceneLabel

EntityRecord
    string            role           — "Player" | "GameData" (SaveRoles constants)
    ComponentRecord[] components

ComponentRecord
    string  type       — assembly-qualified type name (short-name fallback on load)
    bool    enabled    — for IEnableableComponent; ignored otherwise
    string  data       — Base64 of the component struct's raw bytes
```

The encoder (`SaveSerialization.cs`) is a swappable seam — a future named-field JSON encoder (migration-resilient) can replace the raw-bytes one without touching the DTO or systems. Save files are written to `Application.persistentDataPath/save_slot_N.json`. Use `SavePaths.GetSlotPath(slot)` to resolve the path.
