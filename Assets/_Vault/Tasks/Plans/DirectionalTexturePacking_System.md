# Directional Texture Packing + Recolor — Design Spec

> **Status:** ✅ spec ready · edit the inline **← DECISION** markers, then hand back to start the build.
> **Raw source:** intended paste in `Tasks/Materials/info.md` was **empty at authoring time** — this spec is built from the codebase + Spencer's description (4 directions packed into texture channels, color reintroduced afterward). Re-paste that transcript and fold it in before building if it adds detail.
> **Supersedes / implements:** [`Direction_System.md`](Direction_System.md) §2 "Option B (per-direction slices)" — this doc chooses the **channel-packing** flavor of B and adds the color-recovery half that Option B never specified.

---

**Skills Needed** (project skills in `.claude/skills/` — see [Skills index](../../Memories/Code/Skills.md)):
- `shader-edit` — the packed-channel + ramp recolor part shader (§5, wires existing `SelectChannel` + `ColorRamp4` Painterly nodes)
- `dots-blob-library` — `PaletteColorSO` → `PaletteColorLibraryBlob` (tag → ramp colors) (§4)
- `dots-system-scaffold` — rebuilt `UnitFaceDirectionSystem` + recolor apply in `DesignApplyUtil` path (§5)
- `dots-test` — extend `DirectionUtilsTests` for the facing→channel+flip mapping (§10)

---

## 1. Purpose & v1 scope

Fold a part's **4 facings into the 4 channels (RGBA) of one texture** instead of 4 separate texture-array slices, and reintroduce color at shade time via a **per-part color ramp driven by `CharacterPalette`**. Facing becomes an in-shader channel-select (`_Facing` 0–3) plus an X-flip for the mirrored 4, giving 8-way facing from 4 packed channels.

**Why color must be "added back":** an RGBA channel is a single grayscale scalar, so packing N/S/E/W into R/G/B/A discards color by construction. The packed channel becomes a **luminance/shading mask**; color is supplied per-part at shade time. This is viable *because the rig is already one quad per semantic region* (skin, hair, eye, torso…), so each packed texture only needs one region's color — exactly what `CharacterPalette` already tracks per group.

**v1 handles:**
- Offline editor packer: 4 per-direction grayscale PNGs → one packed RGBA texture (extends the `PainterlyMaskPacker` pattern).
- Part shader variant: sample packed slice → `SelectChannel(_Facing)` → grayscale mask → `ColorRamp` (per-instance ramp colors) → lit color.
- `CharacterPalette` tag → ramp colors mapping (`PaletteColorSO` → blob), applied to new per-instance MaterialProperty components.
- Rebuilt `UnitFaceDirectionSystem` writing `_Facing` + X-flip from `DirectionUtils.Get8Direction`.
- **Hybrid opt-out:** a `recolorMode` flag per `PartDefinitionSO` — inherently multi-hue parts (eyes, detailed faces) stay on the existing pre-colored-slice path unchanged.

**Out of v1:** converting *every* part's art to grayscale (art task, done incrementally behind the flag); 6/8 uniquely-authored facings (mirror-flip covers 8 from 4); normal/height from the mask (Painterly `HeightToNormal` exists — a later polish hook).

## 2. Architecture — the recolor decision (Spencer asked for guidance)

**← DECISION (recommended, resolve before shader work): grayscale mask → 3-stop `ColorRamp` recolor, per part, palette-driven, hybrid opt-out.**

Why this over the alternatives, given *this* codebase:

| Approach | Verdict |
|---|---|
| **Flat tint** (`mask × _BaseColor`, already half-wired) | Cheapest, but multiply-muddies painterly art — no hue shift in shadow, highlights wash to white. Use only as a fallback. |
| **3-stop ColorRamp** (mask value → shadow/mid/highlight colors) **← recommended** | Reuses the existing `ColorRamp4` + `SelectChannel` Painterly nodes; gives painterly shadow-hue-shift; palette supplies the 3 colors per group; one grayscale texture → unlimited palette variants (collapses today's per-color-variant slices → real array-memory win). |
| **Region-ID palette LUT** | Only needed when one texture must show several unrelated hues recolored independently — mostly moot because parts are already split into separate quads. Extra LUT asset + authoring; defer. |

The hybrid flag matters: parts that genuinely carry multiple fixed hues in one quad (eye = sclera + iris + pupil) **stay pre-colored** — the system supports both paths simultaneously, so conversion is incremental and never forced.

**Consequence to flag:** moving color off the slice axis means the packed art is **grayscale/luminance** (shading only). Artists paint grayscale per direction (or the packer desaturates a colored source). The texture array's color-variant slices collapse to one grayscale set per shape. ← DECISION: packer desaturates a colored source vs. requires grayscale input PNGs.

## 3. Entry points

No new request component — facing and recolor are **derived state**, same as today's design apply:
- **Facing:** rebuilt `UnitFaceDirectionSystem` (AnimationAssignmentSystemGroup) reads movement/aim → `DirectionUtils.Get8Direction` → writes `FacingIndex` (0–3 channel) + sign for X-flip.
- **Recolor:** the existing `DesignRandomizeSystem` / `DesignApplySystem` / `DesignChangeSystem` path resolves palette tag → ramp colors (via the new blob) and writes the ramp MaterialProperty components — the same seam that today writes `ImageIndex`.

## 4. Data model

- **`PartDefinitionSO.recolorMode : enum { PreColoredSlice, GrayscaleRamp } = PreColoredSlice`** — baked into `PartLibraryBlob` per part (one enum/byte). Degenerate default keeps every existing part on the current path until its art is converted.
- **`PartDefinitionSO.directionCount : int = 1`** — the seam `Direction_System.md` §4 already prescribed; here it means "channels used" (1 = non-directional, 4 = packed). Baked into `PartLibraryBlob`.
- **`PaletteColorSO`** (new, `dots-blob-library`) — maps `(group, tag)` → `{ float4 shadow; float4 mid; float4 highlight; }`. E.g. Skin/"Tan" → warm ramp, Skin/"Zombie" → green ramp. → `PaletteColorLibrarySO` (`_PaletteColorLibrary.asset`) → `PaletteColorLibraryBlob` → `PaletteColorLibrary` singleton, baked in `PostBakingSystemGroup` (mirror `SoundLibraryBakingSystem`).
- **New MaterialProperty components** on rendering parts (mirror `ImageIndexOverride`): `RampShadowColor`/`RampMidColor`/`RampHighlightColor` (`[MaterialProperty("_RampShadow"|"_RampMid"|"_RampHi")]`, each `float4`), and `FacingChannel` (`[MaterialProperty("_Facing")]`, `float`). ← DECISION: 3 separate color props vs. pack shadow+highlight into one and derive mid — 3 is clearest, costs 2 extra float4 per part instance.

## 5. Systems + shader

- **Shader (`shader-edit`):** either extend `2DTextureArrayShader.shadergraph` with a branch on `_Facing`/recolor, or fork a `2DDirectionalRecolorShader` variant (← DECISION: branch-in-place vs. variant graph — variant avoids perturbing the 100+ existing non-directional parts). Chain: `Sample Texture 2D Array(_Texture2D_Array, _ImageIndex)` → `SelectChannel(_Facing)` → grayscale mask → `ColorRamp4`(stops = `_RampShadow/_RampMid/_RampHi`) → Base Color into **Cel Shaded Lighting**. Mirror-flip is geometry (`scale.x`), not shader.
- **`UnitFaceDirectionSystem`** (rebuild the stub): quantize facing via `DirectionUtils.Get8Direction`; map the 8 sectors → `{ FacingChannel 0–3, flipX bool }`; write `FacingChannel` MaterialProperty and set the part/root `LocalTransform.scale.x` sign for the mirrored half. Characterization comments required — the sector→channel+flip table is intentionally non-obvious.
- **`DesignApplyUtil.ApplyDesign` (+ its callers):** for `GrayscaleRamp` parts, look up `(group, tag)` in `PaletteColorLibraryBlob` and write the three ramp MaterialProperty components (alongside — not instead of — the existing `ImageIndex` write, which still drives animation frame + shape slice). `PreColoredSlice` parts are untouched.
- **New tests:** extend `DirectionUtilsTests` to pin the facing→(channel, flip) mapping.

## 6. MonoBehaviour bridge
None — fully ECS + shader. The packer is an editor tool (§8), not runtime.

## 7. Integration points

- **Texture array build:** the packed grayscale RGBA feeds whatever assembles the part `Texture2DArray` (`_Texture2D_Array`); `_ImageIndex` still selects the slice (animation frame + shape), `_Facing` now selects the channel within it. The two axes are orthogonal — direction never consumed slices, so animation flipbooks are unaffected.
- **Design pipeline:** `CharacterPalette` gains a second consumer — today tag→slice (`DesignApplyUtil.SliceAtOffset`), now additionally tag→ramp for recolor parts. Save is unaffected (`CharacterPalette` already `IPersist`; ramp colors are derived, not stored).
- **Painterly nodes:** reuses `SelectChannel`, `ColorRamp4` (and later `HeightToNormal`) from `Shaders/Nodes/Painterly/`.
- **Direction_System.md:** this implements its Option B; close its §2 fork toward "B via channel-packing + mirror-flip" when this lands.

## 8. Proposed file manifest

**New:** `Editor/DirectionTexturePacker.cs` (4 grayscale PNGs → packed RGBA, `PainterlyMaskPacker` pattern), `Data/SOs/PaletteColorSO.cs`, `Data/SOs/PaletteColorLibrarySO.cs`, `Data/Structs/PaletteColorBlobs.cs`, `Components/Units/RecolorComponents.cs` (the 4 MaterialProperty structs + `FacingChannel`), `Authoring/EntityLibraries/PaletteColorLibraryAuthoring.cs`, `Systems/PostBakingSystemGroup/PaletteColorLibraryBakingSystem.cs`, `Shaders/Graphs/2DDirectionalRecolorShader.shadergraph` (or in-place edit).
**Edited:** `Data/SOs/PartDefinitionSO.cs` (+`recolorMode`, +`directionCount`), `PostBakingSystemGroup/PartLibraryBakingSystem.cs` (bake the two fields), `Utils/DesignApplyUtil.cs` (ramp write for recolor parts), `AnimationSystemGroup/AnimationAssignmentSystemGroup/UnitFaceDirectionSystem.cs` (rebuild), `Data/Enums/PartEnums.cs` (or new) (+`RecolorMode`).
**Assets:** `_PaletteColorLibrary.asset` + a `PaletteColorSO` per group/tag; one converted test part (e.g. a skin part) with 4-direction grayscale packed texture + `recolorMode = GrayscaleRamp`.

## 9. Build phases

1. **Packer + shader (art path proven).** `DirectionTexturePacker` editor tool; the recolor shader graph wired from existing nodes; hand-assign `_Facing`/ramp colors on one material and eyeball channel-select + ramp in the scene. *Proves: packed grayscale + ramp = colored, direction-selectable part.*
2. **Data + blob.** `recolorMode`/`directionCount` on `PartDefinitionSO` + `PartLibraryBlob`; `PaletteColorSO` → `PaletteColorLibraryBlob` pipeline; the MaterialProperty components.
3. **Recolor apply.** `DesignApplyUtil` writes ramp props for `GrayscaleRamp` parts from palette tag; verify a tag swap (Tan→Zombie) recolors the converted part with no slice change.
4. **Facing runtime.** Rebuild `UnitFaceDirectionSystem` (channel + X-flip); `DirectionUtilsTests`; walk a unit around and confirm 8-way facing from the 4 packed channels.

## 10. Verification

- **Ph1 (Spencer, Editor):** packed texture shows 4 distinct directions across RGBA in the texture preview; on the test material, sweeping `_Facing` 0→3 swaps the shown facing and the ramp tints grayscale → colored.
- **Ph3 (play DOTSTestScene):** a converted skin part on a citizen recolors when its palette tag changes (Tan↔Zombie) with the same `_ImageIndex`; non-converted parts visually unchanged.
- **Ph4 (play DOTSTestScene):** walk/rotate a unit → facing updates through all 8 sectors, mirrored sectors are X-flips of their pair; EditMode `DirectionUtilsTests` green.

## Open decisions (collected)
- [ ] §2 — confirm 3-stop ColorRamp recolor (recommended) vs flat tint vs region-ID LUT.
- [ ] §2 — packer desaturates a colored source vs requires grayscale input PNGs.
- [ ] §4 — 3 separate ramp color props vs packed 2-prop form.
- [ ] §5 — branch existing `2DTextureArrayShader` in-place vs fork a `2DDirectionalRecolorShader` variant (recommended).
- [ ] §4/§2 (from Direction_System) — facing count if ever moved past mirror-flip 8: 4 / 6 / 8 unique.
