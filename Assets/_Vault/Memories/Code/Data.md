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
| `UnitLibrarySO` + `UnitSO` | `UnitType` | Unit stats, prefab refs, base motivation weights, `animationDirections` (turn granularity, default `Six`); clip fields are `DirectionSetSO`, not `ClipAsset` |
| `DirectionSetSO` (no library — referenced directly by `UnitSO`/mapping fields) | — | Five east-side `ClipAsset` slots (`southEast/northEast/south/north/east`); effective `AnimationDirections` **derived** from fill pattern via `TryGetEffectiveDirections` (never declared). Bakes to `DirectionSetBlob` via `DirectionSetBakeUtil.Bake` (warns + rounds down on an invalid pattern). See `Systems_Animation.md` (facing) |
| `AnimationLibrarySO` + `AnimationClipSO` | `AnimationType` | All animation clip data — used by [[Systems_Animation]] |
| `AttackLibrarySO` + `AttackSO` | `DamageSource` (was `AttackType`) | Attack stats, ranges, damage + ragdoll response (`ragdollForce`/`launchForceX/Y`/flail/spin/restitution). Optional `ragdollProfile` (`RagdollProfileSO`) — flattened over the inline fields at bake by `AttackLibraryBakingSystem` |
| `RagdollProfileSO` | — (flattened into `AttackBlob`) | Shared ragdoll response for a family of attacks (launch, flail, spin, restitution). Referenced by `AttackSO.ragdollProfile`; zero runtime indirection |
| `RagdollConfigSO` | — (flat singleton) | Global ragdoll sim tuning (gravity, drag, restitution, bounce/sleep thresholds, corpse-stack cells). Baked to the `RagdollSimConfig` singleton by `RagdollSimConfigAuthoring` — NOT a blob; systems have identical built-in fallbacks |
| `BehaviorLibrary` + `BehaviorSO` / `UtilityActionSO` | `ActionType` | Behaviour command sequences + consideration curves, read by `ConsiderationScoringSystem` and `BehaviorExecutionSystem` — see [[Systems_AI]] |
| `BuildingTypeListSO` + `BuildingTypeSO` | `BuildingType` | Building stats and prefab refs |
| `ResourceTypeListSO` + `ResourceTypeSO` | `ResourceType` | Resource display names, icons, caps |
| `ItemLibrarySO` + `ItemSO` | `ItemType` | Item `ItemCategory` + effect data (heal amount, satisfied motivation, restoration, pickup range) — used by `ItemAwarenessSystem`; consumed downstream by `ItemConsumeSystem` (consumables) / `ItemEquipSystem` (weapons) via `PickupBehaviour` + `RequestPickup`. Baked by `ItemLibraryBakingSystem` into `ItemLibraryBlob` (holder: `ItemLibrary`) |
| `PartLibrarySO` + `UnitPartSO` | `UnitPartId` | **CharacterRig** per-part DESIGN config (design only — ragdoll lives on `RagdollJointSO`): free-text shape-tag `group` + optional `textureArray` ref + a list of `PartDesign`s, each bundling a tagged texture span (`useFullTextureRange` (default on) resolves to `[0, textureArray.depth-1]` at bake — warns to a single slice if the array is unassigned; untick for hand-authored `minTextureIndex`/`maxTextureIndex`/`step`) with 3 `PaletteSlot`s (`{palette, useFullRange, minColorIndex, maxColorIndex, useAlternateColor}` → `_BaseColor`/`_SecondaryColor`/`_TertiaryColor`; `useFullRange` (default on) = whole palette, no index bookkeeping — bakes a `[0, short.MaxValue]` window that the resolve clamp trims to the palette length), so shape + colour switch together. Purely descriptive — what a spawn may roll is decided by `CharacterRigAuthoring.randomTags`. Baked by `PartLibraryBakingSystem` into `PartLibraryBlob` (`PartDef` entries with nested `BlobArray<PartDesignDef>`; holder: `PartLibrary`). Read by design (`DesignRandomize`/`Apply`/`Change`) + `CharacterRigBakingSystem` |
| `ColorPaletteLibrarySO` + `ColorPaletteSO` | `ColorPaletteType` | **Colour palettes** — the single source of truth for every colour. Each `ColorPaletteSO` = one palette of `ColorVariation { color, hasAlternative, alternative }` entries — `alternative` is that entry's zombie/converted variant, shown in alternate-colour mode; `hasAlternative` unchecked bakes the main colour into both slots (alt mode leaves the entry unchanged) (colour alpha = layer blend strength on secondary/tertiary part slots). Baked by `ColorPaletteLibraryBakingSystem` into `ColorPaletteLibraryBlob` (enum-indexed `ColorPaletteDef` slots of `ColorBlob` pairs, converted **sRGB → linear** at bake because the DOTS MaterialProperty upload is raw; unauthored slots get a 1-entry white fallback; holder: `ColorPaletteLibrary` in `EntityLibraries.cs`). Read by `DesignRandomizeSystem` (colour roll) + `DesignApplyUtil.ResolveColor`; the palette type doubles as the colour sharing group per character |
| `RagdollJointSO` | — (per joint kind, referenced by `RagdollJointAuthoring`) | **Ragdoll joint physics** — settle speed, flail `segmentLength`/`weight`, landing `zones`. One asset per joint KIND (elbow, knee, neck), shared across rigs; baked per-joint into `RagdollJointBakeData` + the `RagdollLandingZone` buffer by `RagdollJointAuthoring.Baker`. Fully separate from the design pipeline (`UnitPartSO`/`PartLibraryBlob` carry no ragdoll data) |

---

## Key Enums (`Data/Enums/`)

| Enum | Values | Used For |
|---|---|---|
| `AnimationType` | 45+ | Identifies every animation clip |
| `AnimationTarget` | 36+ | Names every animatable body part quad |
| `AnimationLayerType` | 7 | Base / Direction / Action / Face / Eyes / Mouth / Override |
| `ActionType` | 22 | AI action identifiers |
| `NeedType` | 13 | None / Hunger / Energy / Fun / Social / Comfort / Bladder / Safety / Movement / SelfPreservation / SelfDefence / BloodLust / Work (`Data/Enums/AiEnums.cs`; renamed from `MotivationType`) |
| `UnitType` | 4 | MaleCitizen / FemaleCitizen / MaleZombie / FemaleZombie |
| `BuildingType` | 7 | Building categories |
| `Direction` / `AnimationDirections` | 8 / 5 | **Toolkit-owned** (`DotsAnimationToolkit.Direction`/`.AnimationDirections`, `DirectionEnums.cs`) — the game's own copies + `DirectionUtils` were deleted 2026-08-29 (superseded by `FacingResolver`); `using DotsAnimationToolkit;` wherever these appear now |
| `ItemType` | 10 | Held + consumable item types (weapons, Bandage/MedKit/Bread/Water) |
| `ItemCategory` | 5 | None / Weapon / Healing / Food / Drink — drives the `ItemConsumeSystem` consume-vs-equip branch |
| `ColorPaletteType` | 6 | `byte`-backed (`ColorEnums.cs`): None / World / Skin / Blood / Hair / Shirts — indexes `ColorPaletteLibraryBlob`; doubles as the per-character colour sharing group (one rolled index per type). No zombie palettes — conversion looks are each entry's `alternative` colour. Append-only |

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

## BlobLibraryUtils (`Utils/BlobLibraryUtils.cs`)

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
