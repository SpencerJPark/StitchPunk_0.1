# Amendment A95F — Sprite Sheets over existing arrays: every Texture2DArray listed, frames named by number

> **Status:** 📝 specced 2026-09-14 from the owner's A95 T15 answer; widened the same evening so baked sheets import
> exactly like the project's arrays (S-D8, S-D9). Takes `0.45.0`.
> **Roadmap:** [`AnimationPackage_Roadmap.md`](AnimationPackage_Roadmap.md), Phase 2 follow-up to A95.
> **Predecessors:** A95 (`0.42.0`), A82 (catalog column).
> **Executor:** one lead; `worker` subagents in **one wave of six**, each ≤ 2 files; the stage does the drive and
> the close (no window wiring changes).

---

## 0. Session prompt

You are running **A95F — Sprite Sheets over existing arrays** on the DOTS Animation Toolkit (head `0.42.0` or later).
Spec: `Assets/_Vault/Tasks/AnimationPackage/A95F_SheetsFromArrays_Spec.md`. Read it, the roadmap §3 protocol, then
only what §3 names. §2 is settled by the owner (2026-09-14). T0 and T1 are the lead's; one wave (T2–T7); one gate;
T8–T10 belong to the stage orchestrator.

---

## 1. Goal

The owner's A95 answer: keep frame naming, but the project's existing arrays must show up in the Sprite Sheets tab,
with frame names defaulting to numbers.

The project's part arrays are grid PNGs that Unity's importer slices into `Texture2DArray`s (`textureShape: 4`,
8×8 flipbook): ten of them under `Assets/Textures/Units/` (2026-09-14). The tab lists only `SpriteSheetAsset`s today,
so none of them appear. After this amendment every `Texture2DArray` in the project is in the catalog. Selecting one
shows its layers as a contact sheet with frames named `0`…`63`. Renaming a frame and pressing Save writes a small names
asset beside the array, which the sprite key picker uses.

---

## 2. Decisions (owner answer + orchestrator calls, 2026-09-14 — do not re-ask)

- **S-D1 — The catalog lists sheets and bare arrays.** Rows are every `SpriteSheetAsset` plus every
  `Texture2DArray` (`FindAssets("t:Texture2DArray")`) that no sheet's `texture` references. A bare-array row's second
  line reads `64 frames · 64×64 · imported, unnamed`; a sheet row wrapping an imported array reads
  `64 frames · 64×64 · imported`. Rename and Delete are offered on sheet rows only. T0 decides whether
  `ToolkitCatalogColumn<TAsset>` can take `UnityEngine.Object` rows with per-row rename/delete, or whether the column
  wraps both in a small row type; log the call.
- **S-D2 — An imported sheet is names only.** Opening a bare array builds a `HideAndDontSave` working copy:
  `texture` = the array, `layerSize` = its size, `frames[i] = { name = i.ToString(), index = i, source = null }` for
  every layer. In this mode Bake, the output path, Add images, drag-reorder and frame removal are **disabled**: the
  importer owns the layer order. Filter, wrap and mips show the array's own values, read-only. Frame names are
  editable inline in the Frames rows.
- **S-D3 — A names asset is written only when there is something to keep.** Save stays disabled until a name differs
  from its index. The first Save of a bare array calls new
  `SpriteSheetAssetUtility.GetOrCreateSheetForArray(Texture2DArray array)`, which creates `<ArrayName>_Sheet.asset`
  beside the array (or returns the sheet that already wraps it) and copies the working names in. No manual asset
  wiring, per the standing call.
- **S-D4 — Layer thumbnails come from the GPU**, not pixels. A frame with no `source` and a sheet with a `texture`
  draws its thumbnail from a per-layer `Texture2D` built as
  `new Texture2D(width, height, array.graphicsFormat, TextureCreationFlags.None)` and filled with
  `Graphics.CopyTexture(array, layer, 0, layerTexture, 0, 0)`. New `SpriteSheetLayerThumbnailCache` owns them and
  destroys them on `SetSheet` and `Dispose`. **Probe 2026-09-14:** `EyeArray.png` is DXT5 sRGB, 64×64, depth 64, 7 mips,
  not readable; CopyTexture of one layer took ~1 ms, all 64 under 1 ms, and a Blit readback of layer 5 showed real
  content (354 of 4096 pixels opaque). Frames with a `source` keep drawing the source.
- **S-D5 — A depth change is reconciled on open.** If a names asset's array now has more layers, numeric frames are
  appended; if fewer, trailing frames are dropped and the tab shows a one-line warning naming how many.
- **S-D6 — The Clip Editor's Sheet field also takes an array.** Its `ObjectField` accepts `SpriteSheetAsset` or
  `Texture2DArray`. Dropping an array calls `GetOrCreateSheetForArray` and binds the result. The Frame and Base Frame
  dropdowns then list `0`…`n-1` (or the saved names). Keys still store the layer number; names are labels (A95 D6
  unchanged).
- **S-D7 — Names stay unique** through the existing `SpriteSheetValidation` dedupe ("mouth", "mouth" → "mouth",
  "mouth 1").
- **S-D8 — A baked sheet is a grid PNG imported like the project's arrays** (owner, 2026-09-14: "copy what the arrays
  are now … other methods make them look bad"). A95's baker wrote an uncompressed RGBA32 `Texture2DArray` `.asset`;
  that path is replaced.
  - **The image:** `SpriteSheetBaker.Bake` composes the frames into one PNG, `T_<Sheet>_Array.png` beside the sheet,
    row-major from the top-left. The grid is `columns = ceil(sqrt(n))`, `rows = ceil(n / columns)`, one cell =
    `layerSize`.
  - **Padding:** unused trailing cells are transparent, so the array's depth is `rows × columns`. The extra layers are
    padding, never listed as frames.
  - **Y is flipped:** `Texture2D` pixel rows count from the bottom, so row `r` is written at
    `y = (rows - 1 - r) × height`.
  - **The import:** the importer is set to `textureShape = Texture2DArray` with `flipbookRows`/`flipbookColumns`, and
    **every other import setting is copied from a reference array's `TextureImporter`**: `TextureImporterSettings`
    via `ReadTextureSettings`/`SetTextureSettings` (then rows and columns restored), the default-platform settings
    (compression, max size, crunch) and any per-platform overrides.
  - **The reference:** new field `SpriteSheetAsset.importSettingsSource` (`Texture2DArray`), shown in the panel header
    as "Match import settings of".
  - **Defaults with no reference:** the settings every project array shares today. That's Compressed (Normal quality),
    mipmaps on, Point, Clamp, sRGB on, aniso 1, alpha-is-transparency off, max 2048, no crunch (all ten arrays read
    2026-09-14; `HeadArray` alone is linear). The sheet's `filterMode`, `wrapMode`, `generateMips` and `linear` fields
    are now these defaults, written into the importer.
  - **Re-bake** rewrites the PNG and reimports, so the GUID never changes; the `CopySerialized` overwrite goes.
  - **No migration:** no baked sheet exists in the project (A95's drive scratch was deleted); T0 confirms.
- **S-D9 — Probe, 2026-09-14 (stage).** A 4×4 PNG with a red, green, blue and white 2×2 grid, imported as
  `Texture2DArray` with rows and columns 2: depth 4, layer 0 = top-left (red), 1 = top-right, 2 = bottom-left,
  3 = bottom-right. The same PNG re-imported Compressed with mipmaps succeeded, and the GUID stayed stable. A reimport
  of a new PNG inside one `execute_code` call can outlast the MCP response timeout, so drives split the import and the
  readback into separate calls.

---

## 3. Read first

- `Editor/SpriteSheets/SpriteSheetCatalogColumn.cs` (109 lines, whole) and `ToolkitCatalogColumn`'s
  `CatalogColumnOptions<TAsset>` (grep under `Editor/ClipEditor/Shared/`).
- `Editor/SpriteSheets/SpriteSheetsPanel.cs` lines 37–210 (construction, `LoadSheet`, selection) and 383–479 (Bake,
  Save, `SetControlsEnabled`).
- `Editor/SpriteSheets/SpriteSheetFramesColumn.cs` lines 28–80, 147–215, 265–300.
- `Editor/SpriteSheets/SpriteSheetPreviewElement.cs` lines 37–160.
- `Editor/ClipUtilities/SpriteSheetAssetUtility.cs` (whole, small).
- `Editor/ClipEditor/Panes/SpriteSheetFramePickerBuilder.cs` 20–40 (`BuildSheetField`).
- `Authoring/Assets/SpriteSheetAsset.cs` (whole).
- `Editor/SpriteSheets/SpriteSheetBaker.cs` lines 26–160 (the output path, the size check and the array write that
  S-D8 replaces) and `Tests/EditMode/SpriteSheetBakerTests.cs` (whole).
- One project array's import settings, for S-D8's defaults: `Assets/Textures/Units/Arrays/EyeArray.png.meta`.

---

## 4. Design notes

- `SpriteSheetAsset` gains `public bool IsImportedArray => texture != null && frames.TrueForAll(frame => frame.source == null)`.
  It's a property, so the serialized data is unchanged.
- `SpriteSheetLayerThumbnailCache`: `Texture2D GetLayerThumbnail(Texture2DArray array, int layerIndex)`, `Clear()`.
- Element names: `sprite-sheet-imported-hint` (the "layer order belongs to the importer" line), `sprite-sheet-depth-warning`.

---

## 5. Tasks

- [ ] **T0 — Grounding (lead).** Verify every §3 name and line range; make S-D1's catalog call; confirm the frames
  column's rename cell (or add the inline text field in T4); confirm `BuildSheetField`'s callers in
  `ClipInspectorPane.cs` (two sites, ~670 and ~1422). Log drift in §7.
- [ ] **T1 — Shared types (lead).** `IsImportedArray`; stubs for `SpriteSheetLayerThumbnailCache`,
  `SpriteSheetAssetUtility.GetOrCreateSheetForArray`. Gate `SpriteSheetValidationTests`,
  `PackagingConformanceTests`. Commit `A95F-T1`.
- [ ] **T2 — Thumbnails [parallel-safe]** — Files: new `Editor/SpriteSheets/SpriteSheetLayerThumbnailCache.cs`,
  `Editor/SpriteSheets/SpriteSheetPreviewElement.cs`. S-D4.
- [ ] **T3 — Catalog [parallel-safe]** — Files: `Editor/SpriteSheets/SpriteSheetCatalogColumn.cs`. S-D1.
- [ ] **T4 — Panel + frames column, imported mode [parallel-safe]** — Files: `Editor/SpriteSheets/SpriteSheetsPanel.cs`,
  `Editor/SpriteSheets/SpriteSheetFramesColumn.cs`. S-D2, S-D3 (Save gating), S-D5, S-D7. Add NO `<summary>` blocks.
- [ ] **T5 — Names asset utility + fixture [parallel-safe]** — Files: `Editor/ClipUtilities/SpriteSheetAssetUtility.cs`,
  new `Tests/EditMode/SpriteSheetArrayNamesTests.cs`:
  - `GetOrCreateSheetForArray_NamesByIndex_AndReusesTheExistingSheet`: create a 3-layer `Texture2DArray` asset in a
    GUID-named scratch folder (Conformance_D: no `Assets/<Folder>` literal; resolve the path from the folder GUID, as
    `SpriteSheetBakerTests` does); the first call writes a sheet with names `0`, `1`, `2` and `texture` set; the second
    call returns the same asset. TearDown deletes the folder.
  - Revert-to-fail: skip the "already wrapped" lookup (the second call creates a second asset).
- [ ] **T6 — Sheet field takes arrays [parallel-safe]** — Files: `Editor/ClipEditor/Panes/SpriteSheetFramePickerBuilder.cs`
  (`BuildSheetField` objectType and the array branch), `Editor/ClipEditor/Panes/ClipInspectorPane.cs` only if the
  callers need a signature change (T0 decides; otherwise one file).
- [ ] **T7 — Docs [parallel-safe]** — Files: `Documentation~/sprite-sheets.md`. Existing arrays come first: they
  appear on their own, frames are numbered, rename and Save to keep names. Stacking separate images comes second, as a
  grid PNG imported with the same settings as a chosen array, with no uncompressed option.
- [ ] **T7a — Baker: grid PNG + copied import settings [parallel-safe]** — Files: `Editor/SpriteSheets/SpriteSheetBaker.cs`,
  `Tests/EditMode/SpriteSheetBakerTests.cs`. S-D8. The fixture keeps `Bake_LayerOrderIsListOrder`:
  - It bakes three 2×2 solid-colour frames in a GUID-named scratch folder, then sets the resulting PNG's importer to
    Uncompressed and readable (a test-only override after the bake) and reimports.
  - It asserts each layer's colour equals its frame's colour in list order, and that the importer's `filterMode`,
    `mipmapEnabled` and `textureCompression` came from the reference array it passed.
  - Revert-to-fail: compose the grid bottom-up (skip the y flip).
- (T4 also adds the header's "Match import settings of" `ObjectField`, bound to `importSettingsSource`.)
- **Gate the wave.** `SpriteSheetArrayNamesTests`, `SpriteSheetValidationTests`, `SpriteSheetBakerTests`,
  `ClipEditorAddEventTests`, `PackagingConformanceTests` (namespace-qualified). Revert-to-fail. Commit `A95F-T2..T7`.
  For-integration block (CHANGELOG `## [0.45.0]`, traps, HANDOFF draft). `worktree.py status a95f ready`.
- [ ] **T8 — Drive (stage).** Full suites. `AssetDatabase.CopyAsset` `EyeArray.png` (+ its import settings) into a
  scratch folder. The detached panel's catalog lists it (and the ten project arrays); open it: 64 frames named
  `0`…`63`, 64 thumbnails from the GPU path, Bake disabled. Rename frame 5 to `blink_half`, Save: `EyeArray_Sheet.asset`
  appears beside the scratch copy with that name and 63 numeric names; reopening shows it as a sheet row. Bind the
  scratch sheet on a scratch clip's sprite track through `BuildSheetField` hosted in a temporary utility window (a
  detached field dispatches no events); the Frame dropdown lists `blink_half` at 5.

  Then a baked sheet: four `CaravanCustomColors` PNGs with `importSettingsSource = EyeArray`.
  - Bake in one call and read back in the next (S-D9 timeout note).
  - The output `T_<Sheet>_Array.png` imports as an array of depth 4 whose importer settings match `EyeArray.png`'s
    except rows and columns, and whose format matches `EyeArray`'s (DXT5 sRGB on this machine).
  - Re-bake with two frames swapped: same GUID.

  Delete scratch. Never write beside the real arrays.
- [ ] **T9 — Vault + HANDOFF + close (stage).** CHANGELOG, `package.json` and conformance pin `0.45.0`.
- [ ] **T10 — ⏸ owner checkpoint.** "Sprite Sheets: your eight Units arrays (and two legacy hair arrays) are in the
  list. Open EyeArray: every frame shows, numbered 0–63. Rename a few and press Save; a small EyeArray_Sheet asset
  appears beside the PNG. In a clip, drop EyeArray onto a sprite track's Sheet field and pick frames by those names.
  Does this match how you want to name frames?"

---

## 6. Out of scope

- Re-slicing or re-importing arrays (the importer's job).
- Storing names in the PNG's import settings or `userData`.
- Keys that store a frame name instead of a layer number.

## 7. Build log

- **2026-09-14 — T0 probe run by the stage** (S-D4): recorded above.
