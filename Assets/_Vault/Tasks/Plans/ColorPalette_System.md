# Color Palette System — Design Spec

> **Status:** ✅ spec ready · edit the inline **← DECISION** markers, then hand back to start the build.
> **Origin:** grew out of the per-instance sprite-tint work (`BodyPartTint` / `_BaseColor` Hybrid Per Instance). Right now every part is baked white or given a one-off `tintColor` in `BodyPartAuthoring`; this system centralises those colours into a referenced global registry.

---

**Skills Needed** (project skills in `.claude/skills/` — see [Skills index](../../Memories/Code/Skills.md)):
- `dots-blob-library` — the `ColorPaletteSO → ColorPaletteLibrarySO → ColorPaletteBlob` enum-indexed registry (§4).
- `dots-system-scaffold` — `TintApplySystem` (`ISystem` + `IJobEntity`) that resolves the ref and writes `BodyPartTint` (§5).
- `dots-authoring-baker` — extend `BodyPartAuthoring` to bake `PaletteColorRef` + `ApplyTintRequest` (§4, §8). *(Edit, not a new baker.)*

---

## 1. Purpose & v1 scope

A single **global colour registry** parts reference by an enum id, instead of hard-coding an RGB per part in the authoring inspector. A part stores a `PaletteColorRef` (a `PaletteColorId`); a system resolves that id through the baked `ColorPaletteBlob` to a **linear** `float4` and writes it into `BodyPartTint` (which drives `_BaseColor`, Hybrid Per Instance). Change the registry once → every part that references that colour updates on the next bake/apply. Entered via a **one-shot enableable request** (`ApplyTintRequest`), mirroring the existing `ChangeDesignRequest` / `DesignChangeSystem` re-skin pattern.

**v1 handles:**
- A `ColorPaletteSO` list of named colours baked into an enum-indexed blob, readable from Burst.
- A per-part `PaletteColorRef { PaletteColorId id }` set in `BodyPartAuthoring` (replaces the raw `tintColor` field).
- `TintApplySystem` resolving id → linear `float4` → `BodyPartTint`, one-shot on `ApplyTintRequest`.
- Baking-time application so pre-placed units in `DOTSTestScene` show their palette colour on load.

**Out of v1 (reserved hooks):**
- **Unifying with `CharacterPalette` group→tag** so a `Skin → Zombie` design change *also* drives the tint. ← DECISION below; v1 keeps the two orthogonal. The clean hook: have `DesignChangeSystem` additionally enable `ApplyTintRequest` once a group→colour mapping exists.
- Runtime colour picking / a design-UI to recolour a unit live (v1 is authored + one-shot).
- Per-part alpha / HDR emissive tints (v1 is an opaque RGB multiply; alpha stays 1).

## 2. Architecture

Pure ECS, Burst, `ScheduleParallel`. Config (the colour table) is immutable SO→Blob; per-part state is `PaletteColorRef` + the `ApplyTintRequest` enableable flag. No MonoBehaviour bridge — no managed objects involved (a `Color` is only touched at bake time, converted to `float4.linear` there).

```
ColorPaletteSO (global asset)        PaletteColorId enum
  SkinTan    = #C8A07A                  None, SkinTan, SkinZombie,
  SkinZombie = #6E8B5A                  HairBlack, ClothRed, ...
  ClothRed   = #A03028
        │ ColorPaletteLibraryBakingSystem (PostBakingSystemGroup)
        ▼
  ColorPaletteBlob  (blob[(int)id] → float4, stored LINEAR)
        │  read via singleton
        ▼
Part entity: PaletteColorRef{ id } + ApplyTintRequest(enabled)
        │  TintApplySystem  (resolve id → blob → float4)
        ▼
  BodyPartTint.Value = blob[(int)id]   → _BaseColor (Hybrid Per Instance)
```

**Colour-space rule (carried from the tint bug):** the blob stores **linear** `float4`. The `Color → float4` conversion (`.linear`) happens **once, at bake**, inside `ColorPaletteLibraryBakingSystem` — so the runtime path is a raw blob read with no per-frame conversion, and no caller can forget `.linear`. See `_Vault/Memories/Code/Gotchas.md` → "DOTS `[MaterialProperty]` colours skip sRGB→linear".

**← DECISION:** system-group placement. Recommended: a small `TintApplySystem` in **`DesignSystemGroup`** (after `DesignChangeSystem`, before `AnimationSystemGroup`) so a design-driven recolour and a tint land in the same group before the image-index push. Alternative: `SpawnInitSystemGroup` if tint should only ever apply at spawn.

## 3. Entry points

- **One-shot request** — `struct ApplyTintRequest : IComponentData, IEnableableComponent` on a rendering part (or on the root, fanned to parts — ← DECISION §5). A caller sets/changes `PaletteColorRef` and enables it; `TintApplySystem` reads it, writes `BodyPartTint`, disables it. Idle until the colour changes again. NOT `IPersist` — the request is never saved.
- **Persistent state** — `struct PaletteColorRef : IComponentData { PaletteColorId id; }` on each rendering part. ← DECISION: mark `IPersist` so a runtime recolour survives save/load (like `PersistedDesign`), or leave it bake-only if colours are always authored. Recommended: `IPersist`.

## 4. Data model

SO→Blob library via `dots-blob-library`:
- `PaletteColorId` enum in `_Scripts/Data/Enums/` — `None = 0` first (neutral white fallback), then named entries (`SkinTan`, `SkinZombie`, `HairBlack`, `ClothRed`, …). ← DECISION: the actual colour vocabulary.
- `ColorPaletteSO` — one authored colour entry: `PaletteColorId id`, `Color color` (`[ColorUsage(false, false)]`, sRGB in the inspector).
- `ColorPaletteLibrarySO` — the global list of `ColorPaletteSO` (the single `_ColorPalette` asset).
- `ColorPaletteBlob` — `BlobArray<float4> colors` indexed by `(int)PaletteColorId`, stored **linear**. `ColorPaletteLibrary` + `ColorPaletteLibraryReference` singleton components.
- `ColorPaletteLibraryBakingSystem` (`PostBakingSystemGroup`) — builds the blob, gap-fills unmapped ids with white `(1,1,1,1)`, converts each authored `Color` with `.linear`. Standard `IsCreated` dispose guard.

Config (blob) = immutable colour table. Runtime = `PaletteColorRef.id` per part. No managed references at runtime (Color lives only in the SO / bake).

## 5. Systems

- **`TintApplySystem`** — `[UpdateInGroup(typeof(DesignSystemGroup))]`, `[UpdateAfter(typeof(DesignChangeSystem))]`. `RequireForUpdate<ColorPaletteLibrary>()` (+ `GameSceneTag`). `IJobEntity` over `(RefRO<PaletteColorRef>, EnabledRefRW<ApplyTintRequest> applyTintRequestEnabled, RefRW<BodyPartTint>)` with `WithAll<ApplyTintRequest>()`: reads the blob singleton, writes `bodyPartTint.Value = blob.colors[(int)ref.id]`, sets `applyTintRequestEnabled.ValueRW = false`. `ScheduleParallel`, `state.Dependency`. (EnabledRef param name follows the `fooEnabled` rule — see `feedback_enabledref_naming`.)
- ← DECISION: **ref granularity.** Recommended: `PaletteColorRef` lives **per rendering part** (a head part and a coat part hold different ids), set individually in `BodyPartAuthoring`. Alternative: a root-level "skin colour" that fans to all parts of a group via the `BodyPart` buffer (closer to how `CharacterPalette` shares one tag across a group) — more machinery, defer unless needed.

## 6. MonoBehaviour bridge

None — no managed objects at runtime.

## 7. Integration points

- **`BodyPartTint` / `_BaseColor`** (`AnimationComponents.cs`, `2DTextureArrayShader` + `2DShader`) — the write target. Hybrid Per Instance already wired; this system is the intended long-term writer that the authoring comment ("a future global palette/skin system will overwrite this at runtime") points at.
- **`BodyPartAuthoring`** — replace the interim `public Color tintColor` field with `public PaletteColorId tintColorId`; bake `PaletteColorRef{ id }` + add `ApplyTintRequest` (enabled) on rendering parts. Keeps the `hasRenderer` gate. The `.linear` conversion moves out of the baker into the library bake.
- **`DesignChangeSystem` / `CharacterPalette`** — orthogonal in v1. Future unify hook: a group→PaletteColorId map so a `ChangeDesignRequest` (e.g. `Skin → Zombie`) also enables `ApplyTintRequest` with the mapped colour. ← DECISION, deferred.
- **Save** — if `PaletteColorRef` is `IPersist`, it auto-round-trips via `PersistRegistry` (value-type, no Entity/Blob) like `PersistedDesign`. No per-field code.

## 8. Proposed file manifest

**New:**
- `_Scripts/Data/Enums/PaletteColorId.cs`
- `_Scripts/Data/SOs/ColorPaletteSO.cs`, `ColorPaletteLibrarySO.cs`
- `_Scripts/Data/Structs/ColorPaletteBlob.cs` (or alongside the library components)
- `_Scripts/Components/Units/PaletteColorComponents.cs` — `PaletteColorRef`, `ApplyTintRequest`, `ColorPaletteLibrary`, `ColorPaletteLibraryReference`
- `_Scripts/Authoring/Data/ColorPaletteLibraryAuthoring.cs` + `ColorPaletteLibraryBakingSystem.cs`
- `_Scripts/Systems/DesignSystemGroup/TintApplySystem.cs`

**Edited:**
- `_Scripts/Authoring/Units/BodyPartAuthoring.cs` — swap `tintColor` → `tintColorId`; bake `PaletteColorRef` + `ApplyTintRequest`; drop the local `.linear` conversion.
- `_Vault/Memories/Code/Shaders.md` + `Data.md` — note the palette registry drives `_BaseColor`.
- `SystemGroupOrderTests` — register `TintApplySystem` placement (structural conformance).

**Assets:**
- One `_ColorPalette` `ColorPaletteLibrarySO` + a `ColorPaletteSO` per colour, under `Assets/Data/` (or wherever the other library SOs live — ← DECISION path).

## 9. Build phases

1. **Data layer** — `PaletteColorId` enum + the SO→Blob library (`dots-blob-library`) + `ColorPaletteLibraryBakingSystem` with the `.linear` conversion. Author a tiny `_ColorPalette` (White, one skin, one cloth). Verify the singleton exists with correct linear values in the Entities window.
2. **One path end-to-end** — `PaletteColorRef` + `ApplyTintRequest` components; `TintApplySystem`; wire `BodyPartAuthoring` to bake a single part's ref. Rebake `DOTSTestScene`, confirm that one part shows the registry colour (and matches the old inspector look).
3. **Breadth** — set ids across a full character rig; confirm a crowd of differently-referenced parts still batches (one draw call) and each shows its colour.
4. **Polish / hooks** — `IPersist` on `PaletteColorRef` (save round-trip); stub the `CharacterPalette` unify hook if wanted.

## 10. Verification

- **Compile gate:** save `.cs` → focus Unity → clean Console (no `error CS` / `BC`), or grep `Editor.log`.
- **Phase 1:** Entities window → the `ColorPaletteLibrary` singleton blob holds the expected **linear** `float4`s (sRGB #C8A07A ≈ linear ~0.57,0.35,0.20, not 0.78,0.63,0.48).
- **Phase 2:** rebake the subscene → the wired part renders the registry colour; toggling the SO colour + rebake changes it. Confirm it reads as a true multiply (no white wash — the bug that started this).
- **Phase 3:** two parts with different ids under one material → Frame Debugger shows a single draw call (per-instance override intact).
- **Editor-only (Spencer):** the on-screen colour match and draw-call count — Claude cannot capture the Editor; ask for a screenshot / Frame Debugger readout.

## Open decisions (collected)
- [ ] §2 — `TintApplySystem` group: `DesignSystemGroup` (recommended) vs `SpawnInitSystemGroup`.
- [ ] §3 — `PaletteColorRef` `IPersist` (recommended) vs bake-only.
- [ ] §4 — the colour vocabulary (which `PaletteColorId` entries ship in v1) + the `_ColorPalette` asset path.
- [ ] §5 — ref granularity: per-part id (recommended) vs root group-colour fanned via the `BodyPart` buffer.
- [ ] §7 — whether to build the `CharacterPalette` group→colour unify hook now or leave it reserved.
