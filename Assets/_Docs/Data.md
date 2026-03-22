# Data — Context

All runtime data is either an **enum**, a **ScriptableObject**, or a **BlobAsset**. SOs are editor-time only; BlobAssets are the Burst-safe runtime form.

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

Runtime systems never access SOs. Instead, `PostBakingSystemGroup` bakes each SO into a BlobAsset stored on a singleton entity:

1. **SO** (`Data/SOs/`) — editor-facing data
2. **Blob struct** (`Data/Structs/`) — Burst-safe mirror of the SO data
3. **Baking system** (`Systems/PostBakingSystemGroup/`) — reads SO, writes blob, stores on singleton
4. **Runtime system** — calls `SystemAPI.GetSingleton<LibraryComponent>()` to get the blob ref

---

## Key SOs

| SO | Enum Key | Purpose |
|---|---|---|
| `UnitLibrarySO` + `UnitSO` | `UnitType` | Unit stats, prefab refs, base motivation weights |
| `AnimationLibrarySO` + `AnimationClipSO` | `AnimationType` | All animation clip data |
| `AttackLibrarySO` + `AttackSO` | `AttackType` | Attack stats, ranges, damage |
| `AIScoringLibrary` + `AIScoringCurveSO` | `MotivationType` | Scoring curve shapes per motivation |
| `BuildingTypeListSO` + `BuildingTypeSO` | `BuildingType` | Building stats and prefab refs |
| `ResourceTypeListSO` + `ResourceTypeSO` | `ResourceType` | Resource display names, icons, caps |

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
| `ItemType` | — | Held item types |

---

## UnitSO vs UnitLibrarySO

`UnitSO` is a per-unit-type data asset (stats, prefab refs, default motivation values). `UnitLibrarySO` is the registry that holds all `UnitSO`s and bakes them together into `UnitLibraryBlob`. When adding a new unit type, you need both.
