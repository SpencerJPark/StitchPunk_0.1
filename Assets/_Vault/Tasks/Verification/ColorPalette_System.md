# ColorPalette System — Design Spec

> **Status:** 🔨 built (2026-07-11, **v2 rework**) — code landed, awaiting Editor compile + asset wiring + play verify ([verify-colorpalette.md](verify-colorpalette.md)).
>
> **⚠ v2 REWORK (same day, Spencer-directed) — the spec body below describes v1 and is superseded where it conflicts. Final architecture:**
> - `UnitPartSO` = `group` + a list of **`PartDesign`** entries; each design bundles a tagged texture span (`tag`/`minTextureIndex`/`maxTextureIndex`/`step`) WITH its 3 `PaletteSlot`s — shape and colour switch together; the matched design colours the part.
> - `PaletteSlot` = `{palette, minColorIndex, maxColorIndex, useAlternateColor}` — a **window** into the palette. The character still rolls ONE index per `ColorPaletteType` (full palette length); slots clamp it into their window (fixed colour = `[n,n]` window; unrolled fallback = `minColorIndex`).
> - `ColorPaletteSO` entries are **`ColorVariation { color, alternative }`** pairs — the `alternative` is that colour's zombie/converted variant. Zombify = `ChangeDesignRequest.alternateColorMode = Enable` → `CharacterPalette.useAlternateColors` → every slot resolves to `.alternative`, rolled identity kept. **No `ZombieSkin` palette, no `PaletteRemap`/`colorOverrides`** (v1 concepts, removed).
> - **Randomness is authoring-decided:** `CharacterRigAuthoring.randomTags` (group → rollable tags) bakes a `RandomTagOption` buffer; `DesignRandomizeSystem` rolls tags ONLY from it (unlisted tags like "Zombie" never spawn-roll). Per-range/per-slot `randomizable` flags removed from the SO.
> - **Ragdoll fully separated from design:** new `RagdollJointSO` (per joint kind: settle/segment/weight/zones) + `RagdollJointAuthoring` on the dedicated joint empties → resolved `RagdollJointBakeData` + `RagdollLandingZone` buffer. `UnitPartSO`/`PartDef`/`BodyPartAuthoring` carry zero ragdoll data; `Ragdoll2DInitSystem` no longer touches the `PartLibrary` blob.
> **Raw source:** conversation braindump (2026-07-11) — unified colour pallet SO, enum-keyed groups, 3 pallet refs per part, randomize on init, shared groups (skin across arms/face, hair across eyebrows/head). Started files: `ColorPalletSO.cs`, `PartDesign` stub in `UnitPartSO.cs`.
>
> **Build divergences from the manifest** (all convention-driven):
> - Library holder components appended to `Components/EntityLibraries/EntityLibraries.cs` (per `dots-blob-library`) — no `ColorPaletteLibraryComponents.cs` file; the holder field is `blob` (new-library convention), not `library`.
> - `ColorPaletteLibraryAuthoring.cs` lives in `Authoring/EntityLibraries/` (where every library authoring lives), not `Authoring/Units/`.
> - Tests live flat as `Tests/ColorPaletteResolveTests.cs` (the project has no `Tests/EditMode/` subfolder).
> - No zombify call-site edit: nothing fires `ChangeDesignRequest` yet (ZombieConversion plan is spec-only) — the request now carries the colour fields for when it lands.
> - `ApplyDesign` behaviour tweak: a part whose shape pool resolves empty (`slice < 0`) previously skipped entirely; it now still gets its palette colours (shape write skipped, colour write proceeds).

---

**Skills Needed** (project skills in `.claude/skills/` — see [Skills index](../../Memories/Code/Skills.md)):
- `dots-blob-library` — the ColorPaletteLibrary pipeline: `ColorPaletteSO` → `ColorPaletteLibrarySO` → `ColorPaletteLibraryBlob` + holder/reference components + authoring + PostBaking system (§4, Phase 1).
- `dots-test` — EditMode fixture for the pure resolve math in `DesignApplyUtil` (remap → palette → index → clamp) (Phase 6).

No `dots-system-scaffold` / `dots-authoring-baker` needed — every runtime change extends an existing system or baker (`DesignRandomizeSystem`, `DesignApplySystem`, `DesignChangeSystem`, `BodyPartAuthoring`, `PartLibraryBakingSystem`).

---

## 1. Purpose & v1 scope

One authoritative place where every colour in the game is defined: a `ColorPaletteSO` per palette (enum-keyed list of colours), baked into an enum-indexed blob. Unit parts stop carrying colour in their textures — the packed-mask recolor shader (`2DPackedArrayShader` + `PackedChannelRecolor` node) tints up to 3 mask layers per part from `_BaseColor` / `_SecondaryColor` / `_TertiaryColor`, and this system feeds those three properties from palettes. Colour is a **separate axis from shape**: the existing tag/range system on `UnitPartSO` keeps selecting texture-array slices; palettes select colours.

**Decisions locked (2026-07-11 Q&A):** separate colour/shape axes · palette type = the sharing group (one roll per `ColorPaletteType` per character) · extend the existing design pipeline (no new systems) · standalone library blob · per-slot `{palette, defaultIndex, randomizable}` · conversion via per-character palette remaps · spelling standardized on **Palette**.

**v1 handles:**
- `ColorPaletteType`-indexed library blob readable from Burst (units now; world/UI consumers read the same singleton later).
- 3 palette slots on `UnitPartSO` (primary/secondary/tertiary), each `{palette, defaultIndex, randomizable}`.
- Random colour roll on spawn (under the existing `RandomizeDesign` one-shot), one index per palette type per character → skin uniform across arms/face, hair across eyebrows/head, automatically.
- Fixed colours via `defaultIndex` when a slot is non-randomizable.
- Save/load for free (colour state rides `CharacterPalette : IPersist`).
- Conversion recolour (zombify) via palette remaps on `ChangeDesignRequest`.

**Out of v1:** world/environment tinting consumers (the `World` palette exists in the enum, nothing reads it yet); UI colour theming; per-placement colour overrides on `BodyPartAuthoring` (the authored `tintColor` stays as a placeholder for non-design parts only); migrating every rig quad to the packed shader (art task — see §7 risk note).

**Related work (explicitly NOT this plan):** stripping ragdoll config (`defaultSettleSpeed` / `zones` / `ragdollSegmentLength` / `ragdollWeight`) out of `UnitPartSO`/`PartDef` onto dedicated joint-empty config — its own future plan; it changes `Ragdoll2DInitSystem`, `CharacterRigBakingSystem`, and every rig prefab. This plan leaves those fields untouched.

## 2. Architecture

No new systems, no new groups. The colour axis slots into the proven design pipeline at three points:

```
bake:   ColorPaletteLibrarySO ──ColorPaletteLibraryBakingSystem──▶ ColorPaletteLibraryBlob (singleton)
        UnitPartSO palette slots ──PartLibraryBakingSystem──▶ PartDef.primary/secondary/tertiary

spawn:  DesignRandomizeSystem (SpawnInitSystemGroup)
          rolls CharacterPalette.colors — one index per ColorPaletteType used by any
          randomizable slot on the character's parts
        DesignApplySystem (SpawnInitSystemGroup, after MinionRestoreApplySystem)
          DesignApplyUtil.ApplyDesign — per DesignSlot part: slice (existing) + resolve
          3 colours → write BodyPartTint / BodyPartSecondaryTint / BodyPartTertiaryTint

change: DesignChangeSystem (DesignSystemGroup)
          ChangeDesignRequest now also carries paletteRemaps + colorOverrides →
          upsert into CharacterPalette → same ApplyDesign fan-out

render: Entities Graphics uploads the three MaterialProperty components →
        PackedChannelRecolor composites mask R/G/B layers per quad
```

Colour resolution for one slot: `slot.palette → CharacterPalette.remaps (if any) → palette def in blob → index (rolled if slot.randomizable, else slot.defaultIndex) → clamp → float4 (linear)`.

## 3. Entry points

Both existing entries, extended — no new components enter the system:

- **Spawn roll (one-shot):** `RandomizeDesign` (IEnableableComponent, consumed by `DesignRandomizeSystem`) now also rolls colour indices. Restored minions keep their saved `CharacterPalette.colors` because `MinionRestoreApplySystem` overwrites the wasted roll before `DesignApplySystem` fans out (existing ordering, unchanged).
- **Runtime re-skin (request model):** `ChangeDesignRequest` (enableable, consumed one-shot by `DesignChangeSystem`) gains `paletteRemaps` and `colorOverrides`. Zombification = `paletteChanges { "Skin" → "Zombie" }` (shape, existing) **+** `paletteRemaps { Skin → ZombieSkin }` (colour, new) in one request.

## 4. Data model

### Enum — `Assets/_Scripts/Data/Enums/ColorEnums.cs` (new file; move out of the SO file)

```csharp
public enum ColorPaletteType : byte   // byte-backed: rides 2-byte FixedList entries
{
    None,        // slot unused — apply skips the property entirely
    World,
    Skin,
    ZombieSkin,
    Blood,
    Hair,
    Shirts,
}
```
**← DECISION:** initial palette entries beyond these (Pants? Eyes? Metal?). Append-only like every library enum.

### SO side — `Assets/_Scripts/Data/SOs/`

- **`ColorPaletteSO.cs`** (rename of the started `ColorPalletSO.cs` — rename file + `.meta` together so the GUID survives; drop the enum from this file). Fields: `[SearchableEnum] ColorPaletteType paletteType; Color[] colors;`. Colour alpha is meaningful: for secondary/tertiary layers the shader uses alpha as layer blend strength (0 hides the layer); the base layer ignores it.
- **`ColorPaletteLibrarySO.cs`** (new) — `List<ColorPaletteSO> palettes` + `Get(ColorPaletteType)`, the standard registry shape (Data.md pattern). Asset: `_ColorPaletteLibrary`.
- **`UnitPartSO.cs`** — delete the `PartDesign` stub class; add under the existing `[Header("Design")]`:

```csharp
[Serializable]
public class PaletteSlot
{
    [SearchableEnum] public ColorPaletteType palette = ColorPaletteType.None; // None = slot unused
    public int  defaultIndex = 0;      // colour used when not randomized (and roll fallback)
    public bool randomizable = false;  // join the character's RandomizeDesign colour roll
}

public PaletteSlot primaryColor   = new();  // → _BaseColor      (mask R, fill)
public PaletteSlot secondaryColor = new();  // → _SecondaryColor (mask G layer)
public PaletteSlot tertiaryColor  = new();  // → _TertiaryColor  (mask B layer)
```

### Blob side — `Assets/_Scripts/Data/Structs/`

- **`ColorPaletteLibraryBlob.cs`** (new):

```csharp
public struct ColorPaletteDef
{
    public ColorPaletteType id;
    public BlobArray<float4> colors;   // LINEAR space — converted at bake (see gotcha below)
}
public struct ColorPaletteLibraryBlob
{
    public BlobArray<ColorPaletteDef> palettes;  // enum-indexed, one slot per ColorPaletteType
}
```

- **`PartLibraryBlob.cs`** — `PartDef` gains three slots (blittable mirror of `PaletteSlot`):

```csharp
public struct PartPaletteSlot
{
    public ColorPaletteType palette;   // None = unused
    public short            defaultIndex;
    public bool             randomizable;
}
// on PartDef:
public PartPaletteSlot primaryColor;
public PartPaletteSlot secondaryColor;
public PartPaletteSlot tertiaryColor;
```

**⚠ sRGB→linear gotcha (bake-time, both libraries):** the DOTS `MaterialProperty` upload is raw — unlike the material inspector it does NOT auto-convert colour properties, and the project renders in Linear. `ColorPaletteLibraryBakingSystem` must store `color.linear` (`float4`), exactly as `BodyPartAuthoring.Baker` already does for `tintColor` (`BodyPartAuthoring.cs:113`).

### Components

- **`Assets/_Scripts/Components/Units/ColorPaletteLibraryComponents.cs`** (new, mirrors `PartLibraryComponents.cs`): `ColorPaletteLibrary { BlobAssetReference<ColorPaletteLibraryBlob> library; }` singleton + managed `ColorPaletteLibraryReference { UnityObjectRef<ColorPaletteLibrarySO> library; }`.
- **`CharacterPalette`** (`BodyPartComponents.cs`, stays `IComponentData, IPersist`, stays blittable — save format change breaks old saves, precedent accepted per `DesignComponents.cs` header):

```csharp
public struct ColorChoice  { public ColorPaletteType palette; public byte index; }   // 2 bytes
public struct PaletteRemap { public ColorPaletteType from;    public ColorPaletteType to; } // 2 bytes
// on CharacterPalette (alongside the existing groups list):
public FixedList64Bytes<ColorChoice>  colors;   // rolled index per palette type (~31 cap)
public FixedList32Bytes<PaletteRemap> remaps;   // conversion overrides (~15 cap)
```

- **Material property components** (`AnimationComponents.cs`, next to `BodyPartTint`):

```csharp
[MaterialProperty("_SecondaryColor")] public struct BodyPartSecondaryTint : IComponentData { public float4 Value; }
[MaterialProperty("_TertiaryColor")]  public struct BodyPartTertiaryTint  : IComponentData { public float4 Value; }
```
`BodyPartTint` (`_BaseColor`) already exists and stays the primary channel.

- **`ChangeDesignRequest`** (`DesignComponents.cs`) gains:

```csharp
public FixedList32Bytes<PaletteRemap> paletteRemaps;   // e.g. Skin → ZombieSkin
public FixedList64Bytes<ColorChoice>  colorOverrides;  // explicit index set (upsert into colors)
```

## 5. Systems (all extensions of existing files)

| System | Group (unchanged) | Change |
|---|---|---|
| `ColorPaletteLibraryBakingSystem` (**new**, `Systems/PostBakingSystemGroup/`) | `PostBakingSystemGroup` (`WorldSystemFilterFlags.BakingSystem`) | Mirrors `PartLibraryBakingSystem`: seed every enum slot with a safe default (1-entry white palette so a missing SO can never index out of range), overwrite authored slots with linear-converted colours, duplicate-id warning, `IsCreated` dispose guard in assign + `OnDestroy`. |
| `PartLibraryBakingSystem` | `PostBakingSystemGroup` | Bake the three `PaletteSlot`s into `PartDef` (default: `None`/0/false for the seeded slots). |
| `DesignRandomizeSystem` | `SpawnInitSystemGroup` (after `BodyPartInitSystem`, before `MinionRestoreApplySystem`) | `RequireForUpdate<ColorPaletteLibrary>`; pass the colour blob into `DesignRandomizeJob`. New roll step: collect the distinct `ColorPaletteType`s appearing on any **randomizable** slot of the character's `DesignSlot` parts (util helper, like `CollectGroups`), roll `index = random.NextInt(0, palette.colors.Length)` per type, write `palette.colors`. Clear `colors` + `remaps` alongside the existing `groups.Clear()`. |
| `DesignApplySystem` | `SpawnInitSystemGroup` (after `MinionRestoreApplySystem`) | `RequireForUpdate<ColorPaletteLibrary>`; add three `ComponentLookup`s (`BodyPartTint`, `BodyPartSecondaryTint`, `BodyPartTertiaryTint`); pass colour blob + lookups into `DesignApplyUtil.ApplyDesign`. |
| `DesignChangeSystem` | `DesignSystemGroup` | Upsert `paletteRemaps` into `CharacterPalette.remaps` and `colorOverrides` into `CharacterPalette.colors` (new util upserts, same shape as `SetTag`/`UpsertShape`), then the shared `ApplyDesign` fan-out re-tints every part. Same lookups/blob as above. |

### `DesignApplyUtil` additions (pure static, Burst-safe — the testable core)

- `GetColorIndex(in FixedList64Bytes<ColorChoice>, ColorPaletteType) → int` (-1 if unrolled).
- `SetColorIndex(ref …)` / `SetRemap(ref …)` upserts with the same capacity-guard-and-warn pattern as `SetTag` (`DesignApplyUtil.cs:37`).
- `ResolvePalette(in FixedList32Bytes<PaletteRemap>, ColorPaletteType) → ColorPaletteType` (identity if no remap).
- `ResolveColor(ref ColorPaletteLibraryBlob, in CharacterPalette, in PartPaletteSlot, out float4) → bool` — returns false for `None` (caller skips the property write, leaving the baked value); otherwise remap → rolled-or-default index → clamp to palette length → colour.
- `ApplyDesign(…)` grows: colour blob param + the three tint lookups; per `DesignSlot` part, after the existing slice write, resolve + write each of the three colours where the child has the component. Signature change ripples only to `DesignApplySystem` + `DesignChangeSystem`.

### `BodyPartAuthoring.Baker`

In the existing `hasRenderer` block (next to the `BodyPartTint` add): also `AddComponent(entity, new BodyPartSecondaryTint { Value = new float4(1f, 1f, 1f, 1f) })` and the tertiary twin, so every rendering part has the full property set and stays in one batchable archetype. The authored `tintColor` inspector field remains the pre-palette placeholder; `ApplyDesign` overwrites it on design-slot parts. **← DECISION:** default alpha for secondary/tertiary at bake — `1` (mask-authored layers show untinted) or `0` (layers hidden until a palette writes them). Recommend `1`: parts without palettes keep whatever the mask shows.

## 7. Integration points

- **Rendering:** `2DPackedArrayShader` already declares `_BaseColor` / `_SecondaryColor` / `_TertiaryColor` (Hybrid Per Instance) feeding `PackedChannelRecolor`. Quads still on the non-packed `2DArrayShader` only have `_BaseColor` — the extra components are simply not uploaded for those materials (verify no Entities Graphics warning spam in Phase 4; if it warns, gate the two extra `AddComponent`s behind an authoring bool).
- **Save:** free — `CharacterPalette` is `IPersist` and stays blittable; the new FixedLists ride the generic raw-bytes path. Old saves break on the struct-layout change (accepted precedent).
- **Conversion/zombify:** whoever fires the zombify `ChangeDesignRequest` today adds `paletteRemaps { Skin → ZombieSkin }` to the same request — one call site edit.
- **PartLibrary pipeline:** `PartLibraryBakingSystem` + `PartDef` grow slots; `_Vault` Data.md row for `PartLibrarySO` updated (it also still says `PartDefinitionSO`/grid-mode — stale, fix while there).
- **World/UI consumers (future):** read the same `ColorPaletteLibrary` singleton; `World` palette reserved.
- **Structural conformance tests:** `ColorPaletteLibraryBakingSystem` is a baking-world system — confirm `SystemGroupOrderTests` / placement tests don't need a registration (mirror whatever `PartLibraryBakingSystem` has).

## 8. Proposed file manifest

**New:**
- `Assets/_Scripts/Data/Enums/ColorEnums.cs` — `ColorPaletteType : byte` (+ `None`)
- `Assets/_Scripts/Data/SOs/ColorPaletteLibrarySO.cs`
- `Assets/_Scripts/Data/Structs/ColorPaletteLibraryBlob.cs`
- `Assets/_Scripts/Components/Units/ColorPaletteLibraryComponents.cs`
- `Assets/_Scripts/Authoring/Units/ColorPaletteLibraryAuthoring.cs`
- `Assets/_Scripts/Systems/PostBakingSystemGroup/ColorPaletteLibraryBakingSystem.cs`
- `Assets/_Scripts/Tests/EditMode/ColorPaletteResolveTests.cs` (name per existing fixture convention)

**Edited:**
- `Assets/_Scripts/Data/SOs/ColorPalletSO.cs` → **rename** `ColorPaletteSO.cs` (file + `.meta` together, GUID preserved); enum moves out; class renamed
- `Assets/_Scripts/Data/SOs/UnitPartSO.cs` — delete `PartDesign` stub; add `PaletteSlot` + 3 fields
- `Assets/_Scripts/Data/Structs/PartLibraryBlob.cs` — `PartPaletteSlot` + 3 fields on `PartDef`
- `Assets/_Scripts/Systems/PostBakingSystemGroup/PartLibraryBakingSystem.cs` — bake the slots
- `Assets/_Scripts/Components/Units/BodyPartComponents.cs` — `ColorChoice`, `PaletteRemap`, `CharacterPalette.colors/.remaps`
- `Assets/_Scripts/Components/Units/DesignComponents.cs` — `ChangeDesignRequest.paletteRemaps/.colorOverrides`
- `Assets/_Scripts/Components/Animation/AnimationComponents.cs` — `BodyPartSecondaryTint`, `BodyPartTertiaryTint`
- `Assets/_Scripts/Authoring/Units/BodyPartAuthoring.cs` — bake the two new tints
- `Assets/_Scripts/Utils/DesignApplyUtil.cs` — resolve/upsert helpers + `ApplyDesign` colour fan-out
- `Assets/_Scripts/Systems/SpawnInitSystemGroup/DesignRandomizeSystem.cs` — colour roll
- `Assets/_Scripts/Systems/SpawnInitSystemGroup/DesignApplySystem.cs` — lookups + blob wiring
- `Assets/_Scripts/Systems/DesignSystemGroup/DesignChangeSystem.cs` — remap/override upserts + wiring
- Vault: `Data.md`, `Components.md`, `Contracts.md` (ChangeDesignRequest row) — post-build doc pass

**Assets (Editor, by Spencer):**
- One `ColorPaletteSO` per enum entry in use (`Skin`, `ZombieSkin`, `Hair`, `Blood`, `Shirts`, …) under `Assets/Data/…` **← DECISION:** asset folder
- `_ColorPaletteLibrary.asset` + `ColorPaletteLibraryAuthoring` on the same subscene GO that carries `PartLibraryAuthoring` (Game.unity + DOTSTestScene)
- `UnitPartSO` assets: fill the 3 palette slots (e.g. head/arms primary=Skin randomizable; hair/eyebrows primary=Hair randomizable; mouth secondary=Blood fixed idx 0)

## 9. Build phases

1. **Library pipeline** (`dots-blob-library`): rename to Palette spelling, enum file, `ColorPaletteLibrarySO`, blob, components, authoring, baking system with linear conversion + safe white defaults. ✅ blob visible on the singleton in the Entities window after rebake.
2. **Part config:** `PaletteSlot` on `UnitPartSO`, `PartPaletteSlot` on `PartDef`, baking. ✅ inspect a `PartDef` slot in the blob.
3. **Roll:** `CharacterPalette.colors/.remaps`, `DesignRandomizeSystem` colour roll + clears. ✅ spawned unit shows rolled `ColorChoice` entries in the inspector; same palette type ⇒ same index across parts by construction.
4. **Apply:** tint components + `BodyPartAuthoring` bake, `ResolveColor`, `ApplyDesign` fan-out, both apply-path systems wired. ✅ **the visual milestone** — randomized crowd with per-character-uniform skin/hair, varied across characters; save → load keeps colours.
5. **Conversion:** `ChangeDesignRequest` fields + `DesignChangeSystem` upserts; zombify call site adds `Skin → ZombieSkin`. ✅ convert a citizen in play mode → all skin-palette parts go green same frame, shapes preserved.
6. **Tests + docs** (`dots-test`): EditMode coverage of `ResolveColor`/`ResolvePalette`/`GetColorIndex`/upsert-capacity paths; vault doc updates.

## 10. Verification

Standard loop per phase: save → Spencer focuses Unity (compile) → grep Editor.log for `error CS`/`BC` → rebake subscene → play `DOTSTestScene`. Only Spencer can verify the visual phases (4, 5) — screenshot or on-screen confirm: crowd colour variety, per-character uniformity, zombify recolour. Phase 4 also needs a check that non-packed-shader quads don't spam material-property warnings. EditMode tests via Test Runner.

## Open decisions (resolved in the 2026-07-11 pre-build Q&A)

- [x] §4 — enum ships the planned 7 (`None/World/Skin/ZombieSkin/Blood/Hair/Shirts`); append later as art needs them.
- [x] §5 — baked default alpha = **1** (white, fully blended — matches the shader property defaults).
- [x] §8 — palette assets live in `Assets/ScriptableObjects/Colors/`.
- [ ] Art migration order: which rigs/arrays move to `2DPackedArrayShader` packed masks first (colour only shows on packed-mask parts) — Editor/art task, tracked in verify-colorpalette.md notes.
