# Amendment A95 — Sprite Sheets tab: atlas and flipbook builder

> **Status:** 📝 specced 2026-09-10, not built. Takes `0.42.0`.
> **Roadmap:** [`AnimationPackage_Roadmap.md`](AnimationPackage_Roadmap.md) Phase 2.
> **Predecessors:** A81 (the Images catalog and `TexturePackMath`'s file-writing pattern), A82
> (column, split view). The Texture Packer packs **channels**; this tab packs **frames** — they are
> different tools and stay separate tabs.
> **Executor:** one orchestrator; `worker` subagents in **one wave of nine**, each ≤ 2 files.

---

## 0. Session prompt (paste into a fresh session)

You are running **Amendment A95 — Sprite Sheets tab** on the DOTS Animation Toolkit package (head
`0.41.0` or later; **A82 built**). Spec:
`Assets/_Vault/Tasks/AnimationPackage/A95_SpriteSheetsTab_Spec.md`. Read it, the roadmap §3
protocol, then only what §3 here names. T0 and T1 yours; one wave (T2–T10); one gate; T11–T14
yours. Stop at T15.

---

## 1. Goal

Cutout characters animate sprites through `SpriteTrack` keys that are either a `sliceIndex` into a
flipbook (`SpriteFrameMode.Slice`, shader `_ImageIndex`) or an `atlasRect` (`SpriteFrameMode.AtlasRect`,
shader `_AtlasFrame` scale.xy/offset.zw). Nothing in the toolkit builds the flipbook or the atlas;
authors do it in an external tool and type numbers. After this amendment a Sprite Sheets tab takes
a set of source frames, packs them into one texture as a uniform grid (flipbook) or a tight atlas,
writes a `SpriteSheetAsset` naming every frame with its rect and index, and the Clip Editor's sprite
key inspector picks a frame **by name** from a sheet instead of typing an index or four floats.

```
┌ Sprite Sheets ──────────────────────────────────────────────────────────────────────────────────┐
│ ┌ Sheets ─────────┐ ┌ CitizenHead ── [Grid ▾] cell 64x64  cols 8  pad 1  [Bake] ● unsaved ─────┐ │
│ │ 🔍  [+][⟳]      │ │ ┌ Frames (12) ───────────────┐ ┌ Preview ────────────────────────────┐ │ │
│ │ ● CitizenHead   │ │ │ [thumb] head_idle_0   #0    │ │  ▦▦▦▦▦▦▦▦                           │ │ │
│ │ ● CitizenHands  │ │ │ [thumb] head_idle_1   #1    │ │  ▦▦▦▦░░░░   512x128                 │ │ │
│ │                 │ │ │  … (drag from Images)       │ │                                     │ │ │
│ └─────────────────┘ │ └────────────────────────────┘ └─────────────────────────────────────┘ │ │
│                     │ out: Assets/…/T_CitizenHead_Sheet.png                                      │ │
│                     └───────────────────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────────────────────────────────────┘
```

---

## 2. Decisions (recorded — do not re-ask). ⚠ = interpretation not yet confirmed.

- **A95-D1 — `ClipEditorTab.SpriteSheets`, after Health.** Toggle `tab-sprite-sheets`, text
  "Sprite Sheets", pane `sprite-sheets-pane`.
- **A95-D2 — `SpriteSheetAsset : ScriptableObject` in `Authoring/Assets/`**, authoring-only
  (never baked — the clip's `SpriteKey` still stores the resolved `sliceIndex` / `atlasRect`, so
  the runtime does not change). Fields: `Texture2D texture`, `SpriteSheetLayout layout` (`Grid` /
  `Atlas`), `int columns`, `int rows`, `Vector2Int cellSize`, `int padding`, `List<SpriteSheetFrame>
  frames { string name; int index; Rect uvRect; Texture2D source; }`, `string outputPath`.
- **A95-D3 — Two layouts, one packer:** `Grid` places frames in reading order into `cellSize`
  cells (all sources must fit the cell; larger ones are refused by name); `Atlas` uses a shelf
  packer (rows of descending height — simple, deterministic, good enough for character parts) into
  a power-of-two texture chosen as the smallest that fits, capped at 4096. Both are pure functions
  in `SpriteSheetPackMath` over `(width, height)` pairs → rects; pixel copying is the baker's.
- **A95-D4 — Sources come from the Images catalog**, reused from A81 (`ImageCatalogColumn` or its
  A82 re-homing): drag rows into the Frames list, or multi-select and press Add. Frames keep the
  source texture's name; duplicates get ` 1`, ` 2`.
- **A95-D5 — Bake writes the PNG and the asset**; the asset is named on creation and saved only by
  Save (the A81 D23 rule copied verbatim); the ● unsaved marker and discard prompt likewise. Bake
  re-imports the PNG with `alphaIsTransparency = true`, `filterMode = Point`, `mipmapEnabled =
  false`, `textureCompression = None` ⚠ (the owner may prefer compressed; the checkpoint asks).
- **A95-D6 — The Clip Editor's sprite key inspector gains a Sheet field and a Frame dropdown.**
  Picking a frame writes `sliceIndex = frame.index` in Slice mode or `atlasRect = (uv.width,
  uv.height, uv.x, uv.y)` in AtlasRect mode, exactly what `SpriteMaterialSystem` expects (shader
  contract §1). The raw fields stay visible below, read-only when a sheet is bound. The sheet
  reference is remembered per **track** in a new `SpriteTrack.sheet` field — authoring-only, not
  baked; a missing field deserializes to null, which is today's behaviour.
- **A95-D7 — Grid sheets must agree with the material's flipbook layout.** The tab's inspector
  shows "columns × rows" and the docs page says the material's flipbook parameters must match;
  A96's Materials tab will cross-check when both exist. No auto-write into materials here.
- **A95-D8 — Not joined to the shared selection.**

---

## 3. Read first

- `Authoring/Assets/ClipAsset.cs` lines 265–315 (`SpriteTrack`, `SpriteKey`);
  `Runtime/Components/AnimationToolkitEnums.cs` lines 76–116 (`SpriteSliceSpace`, `SpriteFrameMode`,
  `SpriteIndexMode`).
- `Documentation~/shader-contract.md` §1 rows `_ImageIndex`, `_AtlasFrame` and §2.2
  (`ToolkitFlipbook.hlsl` frame addressing) — the numbers D6 writes must match.
- `Editor/TexturePacker/TexturePackBaker.cs` in full — the decode cache, readable-copy and PNG
  write pattern to copy; `TexturePackRecipeAsset.cs` + `TexturePackRecipeAssetUtility.cs` — the
  named-on-creation / Save-only rule.
- `Editor/TexturePacker/ImageCatalogColumn.cs` (or its A82 host) — the drag source.
- The sprite key inspector: grep `SpriteKey` / `sliceIndex` in `Editor/ClipEditor/Panes/
  ClipInspectorPane.cs`.
- Vault `Editor.md` "Pattern: reading source texture pixels", "Pattern: writing a texture asset in
  place".

---

## 4. Design

### 4.1 Types (T1, orchestrator) — `Authoring/Assets/SpriteSheetAsset.cs` per D2, plus
`SpriteTrack.sheet` (D6).

### 4.2 `Editor/SpriteSheets/SpriteSheetPackMath.cs` (T2) — `PackGrid(IReadOnlyList<Vector2Int>
sizes, Vector2Int cell, int columns, int padding, out Vector2Int textureSize) → Rect[]` (pixel
rects) and `PackShelves(sizes, padding, maxSize, out textureSize) → Rect[]`; `ToUv(Rect pixels,
Vector2Int textureSize) → Rect`.

### 4.3 `Editor/SpriteSheets/SpriteSheetBaker.cs` (T3) — instance class; `Bake(SpriteSheetAsset
sheet, IReadOnlyList<Texture2D> sources, out string error)`: readable copies, blit into a new
`Texture2D`, `EncodeToPNG`, write, import with D5 settings, fill `frames`.

### 4.4 `Editor/ClipUtilities/SpriteSheetAssetUtility.cs` (T4) — create / rename / trash / save.

### 4.5 Columns (T5, T6, T7) — `Editor/SpriteSheets/SpriteSheetCatalogColumn.cs` (A82 column
host), `SpriteSheetFramesColumn.cs` (the list + drag target + Add), `SpriteSheetPreviewElement.cs`
(draws the packed texture with frame outlines, hover names a frame).

### 4.6 `Editor/SpriteSheets/SpriteSheetsPanel.cs` (T8) — header (layout dropdown, cell, cols,
pad, Bake, unsaved marker, output path), three columns, `Dispose`.

### 4.7 Sprite key inspector (T9) — D6 in the pane.

---

## 5. Tasks

- [ ] **T0 — Baseline (orchestrator).** Gate; totals. Confirm how `ToolkitFlipbook.hlsl` maps
  `_ImageIndex` to a cell (columns from a material property? or from texture size?) — D7's docs
  sentence depends on it. Grep the sprite key inspector range.
- [ ] **T1 — Types (orchestrator).** §4.1. Gate. Commit `A95-T1`.
- [ ] **T2 — Pack math + fixture [parallel-safe]** — Files: new `SpriteSheetPackMath.cs`, new
  `Tests/EditMode/SpriteSheetPackMathTests.cs` (`Grid_PlacesInReadingOrderWithPadding`: 3 frames,
  cell 4×4, 2 columns, pad 1 → rects at (1,1), (6,1), (1,6), texture 11×11 → rounded to 16×16;
  `Shelves_NeverOverlap`: five random-ish sizes → pairwise `Overlaps` false). Revert-to-fail:
  drop the padding term; drop the shelf advance.
- [ ] **T3 — Baker [parallel-safe]** — Files: new `SpriteSheetBaker.cs`. Read `TexturePackBaker.cs`.
- [ ] **T4 — Asset utility [parallel-safe]** — Files: new `SpriteSheetAssetUtility.cs`. Read
  `TexturePackRecipeAssetUtility.cs`.
- [ ] **T5 — Catalog column [parallel-safe]** — Files: new `SpriteSheetCatalogColumn.cs`.
- [ ] **T6 — Frames column [parallel-safe]** — Files: new `SpriteSheetFramesColumn.cs`. Read the
  Images catalog's drag-start code (grep `DragAndDrop` in the A81 column).
- [ ] **T7 — Preview element [parallel-safe]** — Files: new `SpriteSheetPreviewElement.cs`.
- [ ] **T8 — Panel [parallel-safe]** — Files: new `SpriteSheetsPanel.cs`. Read `TexturePackerPanel.cs`.
- [ ] **T9 — Sprite key inspector: sheet + frame picker [parallel-safe]** — Files:
  `ClipInspectorPane.cs` (the sprite key range only). D6.
- [ ] **T10 — Docs [parallel-safe]** — Files: new `Documentation~/sprite-sheets.md`,
  `Documentation~/cutout-characters.md` (a paragraph pointing at the tab and the D7 rule).
- **Gate the wave.** `SpriteSheetPackMathTests`, `ClipEditorAddEventTests` (inspector pane
  regression). Commit `A95-T2..T10`.
- [ ] **T11 — Window wiring (orchestrator).** Tab enum/UXML/`BindTab`/`Show…Tab`/layout test;
  `index.md`; `CHANGELOG.md` `## [0.42.0]`; `package.json`; `Conformance_G` (`SpriteSheetPackMath`
  is `Math`; `SpriteSheetBaker` is an instance). Gate.
- [ ] **T12 — Drive.** Full suites. Build a 4-frame grid sheet from four project PNGs into
  `Assets/A95Scratch/`; reload the asset; frame 2's `uvRect` equals `ToUv` of the math; open a clip
  with a sprite track, bind the sheet, pick frame 2 → `sliceIndex == 2` on disk after save. Atlas
  layout likewise for `atlasRect`. Capture. Delete scratch.
- [ ] **T13 — Vault + HANDOFF.** Vault note "Sprite Sheets tab (A95)": the D7 flipbook fact from T0.
- [ ] **T14 — Close.** Roadmap checkbox.
- [ ] **T15 — ⏸ owner checkpoint.** Message: "Sprite Sheets tab: New, drag frames in from Images,
  Bake, Save. Then in the Clip Editor pick a frame by name on a sprite key. ⚠ Baked PNGs import
  uncompressed and Point-filtered — keep that default?"

---

## 6. Deliberately out of scope

- Trimming transparent borders, rotation packing, multi-page atlases.
- Writing flipbook parameters into materials (A96 cross-checks; nobody auto-writes).
- Importing existing Unity `Sprite` atlases.

## 7. Build log

_(empty)_
