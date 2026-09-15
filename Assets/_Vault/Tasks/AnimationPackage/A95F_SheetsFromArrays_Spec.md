# Amendment A95F — Sprite Sheets over existing arrays: every Texture2DArray listed, frames named by number

> **Status:** ✅ built 2026-09-14 as `0.45.0` in the A93F–A95F parallel worktree batch (merged `f30bbd62`, integrated `f67b47e3`); **T10 accepted 2026-09-14** (owner: "these look good for now"). Specced the same day from the owner's A95 T15 answer and widened with
> importer-made baked sheets (S-D8, S-D9).
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

- [x] **T0 — Grounding (lead).** Verify every §3 name and line range; make S-D1's catalog call; confirm the frames
  column's rename cell (or add the inline text field in T4); confirm `BuildSheetField`'s callers in
  `ClipInspectorPane.cs` (two sites, ~670 and ~1422). Log drift in §7.
- [x] **T1 — Shared types (lead).** `IsImportedArray`; stubs for `SpriteSheetLayerThumbnailCache`,
  `SpriteSheetAssetUtility.GetOrCreateSheetForArray`. Gate `SpriteSheetValidationTests`,
  `PackagingConformanceTests`. Commit `A95F-T1`.
- [x] **T2 — Thumbnails [parallel-safe]** — Files: new `Editor/SpriteSheets/SpriteSheetLayerThumbnailCache.cs`,
  `Editor/SpriteSheets/SpriteSheetPreviewElement.cs`. S-D4.
- [x] **T3 — Catalog [parallel-safe]** — Files: `Editor/SpriteSheets/SpriteSheetCatalogColumn.cs`. S-D1.
- [x] **T4 — Panel + frames column, imported mode [parallel-safe]** — Files: `Editor/SpriteSheets/SpriteSheetsPanel.cs`,
  `Editor/SpriteSheets/SpriteSheetFramesColumn.cs`. S-D2, S-D3 (Save gating), S-D5, S-D7. Add NO `<summary>` blocks.
- [x] **T5 — Names asset utility + fixture [parallel-safe]** — Files: `Editor/ClipUtilities/SpriteSheetAssetUtility.cs`,
  new `Tests/EditMode/SpriteSheetArrayNamesTests.cs`:
  - `GetOrCreateSheetForArray_NamesByIndex_AndReusesTheExistingSheet`: create a 3-layer `Texture2DArray` asset in a
    GUID-named scratch folder (Conformance_D: no `Assets/<Folder>` literal; resolve the path from the folder GUID, as
    `SpriteSheetBakerTests` does); the first call writes a sheet with names `0`, `1`, `2` and `texture` set; the second
    call returns the same asset. TearDown deletes the folder.
  - Revert-to-fail: skip the "already wrapped" lookup (the second call creates a second asset).
- [x] **T6 — Sheet field takes arrays [parallel-safe]** — Files: `Editor/ClipEditor/Panes/SpriteSheetFramePickerBuilder.cs`
  (`BuildSheetField` objectType and the array branch), `Editor/ClipEditor/Panes/ClipInspectorPane.cs` only if the
  callers need a signature change (T0 decides; otherwise one file).
- [x] **T7 — Docs [parallel-safe]** — Files: `Documentation~/sprite-sheets.md`. Existing arrays come first: they
  appear on their own, frames are numbered, rename and Save to keep names. Stacking separate images comes second, as a
  grid PNG imported with the same settings as a chosen array, with no uncompressed option.
- [x] **T7a — Baker: grid PNG + copied import settings [parallel-safe]** — Files: `Editor/SpriteSheets/SpriteSheetBaker.cs`,
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
- [x] **T8 — Drive (stage).** Full suites. `AssetDatabase.CopyAsset` `EyeArray.png` (+ its import settings) into a
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
- [x] **T9 — Vault + HANDOFF + close (stage).** CHANGELOG, `package.json` and conformance pin `0.45.0`.
- [x] **T10 — ⏸ owner checkpoint.** "Sprite Sheets: your eight Units arrays (and two legacy hair arrays) are in the
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
- **2026-09-14 — Phase 0 (stage, parallel batch A93F-A95F).** Head `eb60b150`, package `0.42.0`, CHANGELOG top section `## [0.42.0]`. Baseline: compile clean; EditMode 850 (Conformance_A the one standing failure), PlayMode 285. Registry sha256: AnimEventKeyRegistry `3bdb420d…d14701`, TargetTagRegistry `dbec3d5f…eb4f`. Preflight: broker alive, hooks installed, stage blockers only the owner's five uncommitted files. Lead opus, worker sonnet.
- **2026-09-14 — T0 grounding (lead, worktree `spec/a95f`).** Every §3 name and range verified: `SpriteSheetsPanel` 37–210 / 383–479, `SpriteSheetFramesColumn` 28–80 / 147–215 / 265–300, `SpriteSheetPreviewElement` 37–160, `BuildSheetField` 20–37, baker write path 84–145, `ClipInspectorPane` callers at exactly 670 and 1422. No `SpriteSheetAsset` asset exists under `Assets` (script GUID grep), so S-D8 needs no migration. Drift and calls:
  - **Drift 1: the S-D1 call.** The column stays the shared `ToolkitCatalogColumn`, now `<UnityEngine.Object>`. Its context menu gated Rename/Delete per column, so `CatalogColumnOptions` gains an additive `rowAllowsRenameAndDelete` (`Func<TAsset,bool>`; null keeps every other catalog unchanged). `SpriteSheetCatalogColumn` raises `SheetSelected` or a new `ArraySelected(Texture2DArray)`, and adds `SelectedArray` and `SetSelectedArray`. Arrays are matched to sheets by asset path.
  - **Drift 2: task split.** The lead built T3 (catalog) and `SpriteSheetLayerThumbnailCache` in full in T1, not as stubs, because every other file compiles against them. The wave was T2+T6 (one worker), T4a panel, T4b frames column, T5, T7a and T7.
  - **Drift 3: T6 is one file.** A UI Toolkit `ObjectField` takes one `objectType`, so the Sheet field uses `UnityEngine.Object` and the callback keeps sheets, turns arrays into sheets and reverts anything else. `ClipInspectorPane` is untouched. The object picker now lists every asset type (trap below).
  - **Drift 4: frame rename.** The frames column had no rename cell. Double-clicking a frame's name runs `InlineRenameEditing.Begin`, deduped through `SpriteSheetValidation.DedupeFrameName`, in every mode.
  - **Drift 5: Save gating and depth changes.** S-D3's gating applies to bare arrays only; sheet rows keep the always-enabled Save. A depth reconcile that changes the frame count marks the sheet unsaved, so Save persists it.
  - **Drift 6: two extra utility helpers,** `CreateWorkingCopyForArray` and `ReconcileFramesWithArrayDepth`, so the panel and the fixture share one numeric-frame rule.
  - **Drift 7: an unusable reference fails the bake.** Bake with an `importSettingsSource` that no `TextureImporter` made (a `.asset` array) stops with a message; it does not fall back to the defaults.
  - **Drift 8: how settings are copied.** Default-platform settings go through the importer's own `maxTextureSize` / `textureCompression` / `crunchedCompression` / `compressionQuality`. Per-platform overrides are copied over a fixed platform-name list, because no API enumerates overrides.
  - **Drift 9: `generateMips` defaults to true** (S-D8's defaults).
- **2026-09-14 — T1** committed `7fa86f08`. Gate SpriteSheetValidationTests + PackagingConformanceTests: compile clean, 13 passed, 1 failed (Conformance_A, the standing failure).
- **2026-09-14 — T2..T7 wave** committed `705146d4` (six workers, all reported before the gate).
  - **Gate:** SpriteSheetArrayNamesTests, SpriteSheetValidationTests, SpriteSheetBakerTests, ClipEditorAddEventTests and PackagingConformanceTests. Compile clean; 18 passed, 1 failed (Conformance_A only), 19 tests named. One "Unity is compiling" refusal was retried.
  - **Revert-to-fail:** mutation `6e524bc2` skipped the existing-sheet lookup and the grid y flip.
    - `GetOrCreateSheetForArray_NamesByIndex_AndReusesTheExistingSheet` failed, returning a second sheet ("NamesTestArray_Sheet 1").
    - `Bake_LayerOrderIsListOrder` failed: layer 0 read 0, where 255 was expected.
  - **Reset:** `git reset --hard HEAD~1` restored both files; their sha256s match the pre-mutation hashes.
  - **Unverified:** the panel, frames column and Sheet field have no fixture. Their UI behaviour is left to the stage drive, T8.

### For integration

**CHANGELOG `## [0.45.0]`**
```
## [0.45.0]

### Changed
- Sprite Sheets lists every Texture2DArray in the project beside the sprite sheets. A bare array's row reads "N frames · W×H · imported, unnamed", and a sheet that wraps an imported array reads "· imported". Rename and Delete appear on sheet rows only, through the new `CatalogColumnOptions.rowAllowsRenameAndDelete`.
- Opening an array builds a names-only working copy: frames are named 0…n-1, the thumbnails are GPU layer copies (`SpriteSheetLayerThumbnailCache`, `Graphics.CopyTexture`), and Bake, the output path, adding images, reorder and removal are disabled. Filter, wrap, mips and linear show the array's own importer values, read-only.
- Double-click a frame's name to rename it; names stay unique. For a bare array, Save is enabled once any name differs from its number. The first Save writes `<ArrayName>_Sheet.asset` beside the array (`SpriteSheetAssetUtility.GetOrCreateSheetForArray`).
- Opening a names sheet whose array changed depth appends numeric frames, or drops the trailing ones with a warning.
- The Clip Editor's sprite-track Sheet field accepts a Texture2DArray too. Dropping one reuses or creates its names sheet.
- Bake writes a grid PNG, `T_<Sheet>_Array.png`, row-major from the top-left with transparent padding cells, and imports it as a Texture2DArray. New "Match import settings of" (`SpriteSheetAsset.importSettingsSource`) copies a reference array's importer settings and per-platform overrides, keeping the grid's rows and columns. With no reference, Bake uses the project arrays' shared defaults (Compressed, mips on, Point, Clamp, sRGB, max 2048, no crunch). Re-bakes keep the GUID. The uncompressed `.asset` array output is gone, and `SpriteSheetAsset.generateMips` now defaults to true.

### Added
- `SpriteSheetAsset.IsImportedArray`, `SpriteSheetAssetUtility.CreateWorkingCopyForArray` and `ReconcileFramesWithArrayDepth`, and `SpriteSheetArrayNamesTests`.
```

**Conformance_G allowlist:** none needed. The only new type, `SpriteSheetLayerThumbnailCache`, is a sealed non-static class; the existing `SpriteSheetAssetUtility` gained members only.

**Wiring:** none. `ClipEditorTab`, the UXML and the `ClipEditorWindow*` files are unchanged. The panel is still `new SpriteSheetsPanel()` plus `Dispose()`, which now also disposes the preview and frames column caches. New public surface for drives:
- `SpriteSheetsPanel.LoadArray(Texture2DArray)` and `LoadedArray`.
- `SpriteSheetCatalogColumn.ArraySelected`, `SelectedArray` and `SetSelectedArray`.
- Element names `sprite-sheet-imported-hint`, `sprite-sheet-depth-warning` and `sprite-sheet-import-settings-source`.

**Vault-note traps (AnimationToolkit.md)**
- The Sheet `ObjectField` has `objectType = UnityEngine.Object` (a UI Toolkit ObjectField takes one type), so its picker lists every asset. Anything that is not a sheet or an array reverts in the callback. Do not "fix" it back to `SpriteSheetAsset`; arrays would stop dropping.
- Arrays are matched to their names sheet by asset path, not reference. `FindAssets("t:Texture2DArray")` returns importer-made PNG arrays, and a sheet's `texture` loads the same main object.
- `IsImportedArray` is derived (texture set, no frame source). A sheet that loses all its sources to missing files flips into names-only mode.
- Bake imports twice on first write: `ImportAsset` creates a default importer, then `SaveAndReimport` applies the array settings. A drive must bake in one `execute_code` call and read back in the next (S-D9).
- Per-platform overrides are copied over a fixed platform-name list in `SpriteSheetBaker`; a new build target needs adding there.

**HANDOFF draft**
> A95F (0.45.0) makes Sprite Sheets a names layer over the project's existing Texture2DArrays. Every array appears in the catalog. Opening one shows GPU-copied layer thumbnails with frames numbered 0…n-1. Renaming and pressing Save writes `<Array>_Sheet.asset` beside it, and the Clip Editor's Sheet field takes an array directly. Baking separate images now composes a grid PNG imported as a Texture2DArray with a chosen reference array's importer settings (or the shared project defaults), replacing A95's uncompressed `.asset`. The catalog column gained a per-row rename/delete predicate. Stage drive T8 and owner checkpoint T10 remain.

### Close (stage, 2026-09-14)

- **Merge:** rebased onto trunk (head `f30bbd62`), pushed; worktree and branch removed cleanly. No window wiring.
- **Gates:** as A93F's close (fixtures 32/33, EditMode 850/851, PlayMode 285/285, Conformance_A the only failure).
- **T8 drive (scratch only, `Assets/A95FScratch/`):**
  - `EyeArray.png` copied with `AssetDatabase.CopyAsset` in its own call: 64×64, depth 64, `RGBA_DXT5_SRGB`, 7 mips.
    `FindAssets("t:Texture2DArray")` returned 11 (the ten project arrays plus the copy), and the catalog held 11
    array rows including the copy.
  - `LoadArray`: 64 frames named `0`…`63`, `IsImportedArray` true, Bake and Save disabled, the hint "The importer owns
    the layer order: rename frames, then Save to keep the names." shown, and 64 of 64 GPU thumbnails.
  - Frame 5 renamed to `blink_half` through the frames column's rename commit: unsaved, Save enabled. Save wrote
    `EyeArray_Sheet.asset` beside the copy: 64 frames, `blink_half` at 5, 63 numeric names, `texture` = the copy.
  - Reopened: the catalog showed 10 bare arrays plus the sheet row `EyeArray_Sheet`, and `LoadSheet` kept `blink_half`.
  - `BuildSheetField` hosted in a temporary utility window (closed after): `objectType` Object; setting the copy raised
    one pick that reused `EyeArray_Sheet` (still one names sheet) and set `track.sheet`. `BuildFramePopup` at layer 5
    showed `blink_half` (65 choices with "(no frame)").
  - Bake: `Bonewalker`, `ChromeWraith`, `CloudNomad`, `CoastalBreeze` (4×16 each) with `importSettingsSource` =
    `EyeArray`, in one call (352 ms), read back in the next. `T_A95FBakeSheet_Array.png`: a 2×2 grid, depth 4, 4×16,
    5 mips. `TextureImporterSettings` identical to `EyeArray.png`'s except rows and columns; default platform
    Compressed, max 2048, no crunch, AutomaticCompressed; Point, mips, sRGB, Clamp, aniso 1, alpha-is-transparency off.
  - **Drift:** the format is `RGBA_DXT1_SRGB`, not `EyeArray`'s DXT5. The swatches have no alpha
    (`DoesSourceTextureHaveAlpha` false; `EyeArray`'s source true), so AutomaticCompressed picks DXT1. The settings
    match; the format follows the source image.
  - Re-bake with frames 0 and 1 swapped: GUID `1f237c069d8cd494f9571acaca08f78d` unchanged; the decoded PNG's top-left
    cell matched `ChromeWraith` 64/64 and its top-right `Bonewalker` 64/64.
  - Scratch deleted; the project is back to 10 arrays and 0 sprite sheets. Nothing under `Assets/Textures/` changed;
    registry sha256s unchanged.
- **Not seen by eye:** the contact sheet, the inline rename and the header's "Match import settings of" field.

### Owner checkpoint answer (2026-09-14)

- **Accepted as built.** The owner, on T10: "these look good for now". Nothing in this section's drift is overturned,
  so the checkpoint closes with no follow-up.
