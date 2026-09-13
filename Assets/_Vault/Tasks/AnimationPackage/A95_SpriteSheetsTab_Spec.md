# Amendment A95 — Sprite Sheets tab: Texture2DArray flipbook builder

> **Status:** 📝 specced 2026-09-10, **rewritten 2026-09-12** (atlas layout dropped — see §2 D0), not
> built. Takes `0.42.0`.
> **Roadmap:** [`AnimationPackage_Roadmap.md`](AnimationPackage_Roadmap.md) Phase 2.
> **Predecessors:** A81 (the Images catalog and `TexturePackBaker`'s readable-copy pattern), A82
> (column, split view). The Texture Packer packs **channels**; this tab stacks **frames** — they are
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

Cutout characters animate sprites through `SpriteTrack` keys whose `sliceIndex` selects a layer of
a `Texture2DArray` (`SpriteFrameMode.Slice`, shader `_ImageIndex`, sampled by
`ToolkitSpriteUnlitArray.shadergraph`). Nothing in the toolkit builds that array; the game builds
them with a hand-rolled inspector button (`Assets/_Scripts/Editor/TextureArrayBuilder.cs`) that
takes pre-made mip textures per layer, and authors then type layer numbers into keys. A
`Texture2DArray` cannot be opened or previewed in the Project window, so nobody can *see* which
layer is which.

After this amendment a Sprite Sheets tab takes a set of source frames from the Images catalog,
stacks them as the layers of one `Texture2DArray`, writes a `SpriteSheetAsset` naming every layer,
shows the array as a contact sheet, and the Clip Editor's sprite key inspector picks a frame **by
name** from a sheet instead of typing an index.

```
┌ Sprite Sheets ──────────────────────────────────────────────────────────────────────────────────┐
│ ┌ Sheets ─────────┐ ┌ CitizenHead ── 64x64 · 12 layers · [Point ▾] [Clamp ▾] [mips ☐] [Bake] ● ─┐ │
│ │ 🔍  [+][⟳]      │ │ ┌ Frames (12) ───────────────┐ ┌ Contact sheet ──────────────────────┐ │ │
│ │ ● CitizenHead   │ │ │ [thumb] head_idle_0   #0    │ │  ▦ ▦ ▦ ▦ ▦ ▦                        │ │ │
│ │ ● CitizenHands  │ │ │ [thumb] head_idle_1   #1    │ │  ▦ ▦ ▦ ▦ ▦ ▦   hover: head_idle_7  │ │ │
│ │                 │ │ │  … (drag from Images,       │ │                                     │ │ │
│ │                 │ │ │     drag rows to reorder)   │ │                                     │ │ │
│ └─────────────────┘ │ └────────────────────────────┘ └─────────────────────────────────────┘ │ │
│                     │ out: Assets/…/T_CitizenHead_Array.asset                                   │ │
│                     └───────────────────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────────────────────────────────────┘
```

---

## 2. Decisions (recorded — do not re-ask). ⚠ = interpretation not yet confirmed.

- **A95-D0 — Arrays, not atlases (owner call, 2026-09-12).** The original draft offered a
  uniform-grid "flipbook" PNG and a shelf-packed atlas PNG. Both are wrong for this runtime:
  1. A **grid PNG keyed by `sliceIndex` cannot render.** `Slice` mode is a `Texture2DArray` layer
     (`ToolkitFlipbook.hlsl` `SliceUV`, `SpriteFrameMode.Slice` doc); neither shipped shader graph
     exposes columns/rows, and `AtlasRectFromGrid` has no node wrapper, so a grid cell is
     unreachable from a slice key.
  2. A **tight atlas gains nothing inside a part.** `atlasRect` reaches the shader only as a UV
     remap (`AtlasFrameUV`); nothing in `ClipSampler` or `SpriteMaterialSystem` scales or offsets
     the quad from the rect, so every frame of a part must already share one canvas size,
     padding included — exactly what an array layer is. Mixed sizes would stretch onto the fixed
     quad and drift the pivot. Cross-part packing savings are already had by one array per part.
  So the tab builds **one `Texture2DArray` per sheet**, one source frame per layer, and the
  `AtlasRect` runtime path is left untouched but gets no builder. Trimming, rotation and multi-
  size packing stay out (§6).
- **A95-D1 — `ClipEditorTab.SpriteSheets`, after Health.** Toggle `tab-sprite-sheets`, text
  "Sprite Sheets", pane `sprite-sheets-pane`. The name stays "sheet" (a sheet of frames) even though
  the artefact is an array; the roadmap, enum and file name do not churn.
- **A95-D2 — `SpriteSheetAsset : ScriptableObject` in `Authoring/Assets/`**, authoring-only
  (never baked — the clip's `SpriteKey` still stores the resolved `sliceIndex`, so the runtime does
  not change). Fields: `Texture2DArray texture` (the baked output), `Vector2Int layerSize`
  (read-only in the UI, taken from the first frame), `FilterMode filterMode = Point`,
  `TextureWrapMode wrapMode = Clamp`, `bool generateMips = false`, `bool linear = false`,
  `List<SpriteSheetFrame> frames { string name; int index; Texture2D source; }`, `string outputPath`.
- **A95-D3 — One layout: the frame list order is the layer order.** Every source must have
  `layerSize` dimensions; a source of another size is refused **by name** at Bake (the error names
  every offender, not the first). Frames are reorderable by drag; reordering renumbers `index` and
  marks unsaved. There is no pack math — a validator, `SpriteSheetValidation.FindSizeMismatches
  (IReadOnlyList<(string name, Vector2Int size)> frames, Vector2Int expected) → List<string>`,
  is the only pure function.
- **A95-D4 — Sources come from the Images catalog**, reused from A81 (`ImageCatalogColumn` or its
  A82 re-homing): drag rows into the Frames list, or multi-select and press Add. Frames keep the
  source texture's name; duplicates get ` 1`, ` 2`.
- **A95-D5 — Bake writes the array asset and fills the frames**; the sheet asset is named on
  creation and saved only by Save (the A81 D23 rule copied verbatim); the ● unsaved marker and
  discard prompt likewise. The array is created as `TextureFormat.RGBA32`, `mipChain =
  generateMips`, `linear`; layers filled from readable copies of the sources (the
  `TexturePackBaker` decode cache pattern — do **not** require Read/Write on the source import);
  `filterMode`/`wrapMode` from the asset; `Apply(updateMipmaps: generateMips)`; written with
  `AssetDatabase.CreateAsset` to `outputPath` (default `T_<sheet>_Array.asset` beside the sheet,
  matching the game's existing `_Array.asset` convention) or overwritten in place if it exists
  (vault `Editor.md` "Pattern: writing a texture asset in place"). ⚠ Uncompressed RGBA32 is the
  default; a compressed array needs per-layer `EditorUtility.CompressTexture` + `Graphics.CopyTexture`
  into a compressed-format array — the checkpoint asks whether the owner wants that as a follow-up.
- **A95-D6 — The Clip Editor's sprite key inspector gains a Sheet field and a Frame dropdown.**
  Picking a frame writes `sliceIndex = frame.index` for `Absolute` keys. For `RelativeToBase` keys
  the dropdown writes `frame.index - track.baseIndex`, and the track's `baseIndex` field gets the
  same dropdown, so a relative track is "base = frame X, keys = frames by name". The raw
  `sliceIndex` field stays visible below, read-only when a sheet is bound. Tracks whose `mode` is
  `AtlasRect` show the Sheet field disabled with the hint "sheets bind Slice tracks" — no atlas
  picker. The sheet reference is remembered per **track** in a new `SpriteTrack.sheet` field —
  authoring-only, not baked; a missing field deserializes to null, which is today's behaviour.
- **A95-D7 — The part's material must sample an array.** The tab does not touch materials. The
  docs page says a sheet-bound track's part must use `ToolkitSpriteUnlitArray.shadergraph` (or any
  material with a `_MainTexArray` slot) and that the array asset is dragged into that slot by hand;
  A96's Materials tab will flag a sheet-bound track whose material has no `_MainTexArray`.
- **A95-D8 — Not joined to the shared selection.**
- **A95-D9 — The contact sheet is the point.** `SpriteSheetPreviewElement` draws every layer as a
  thumbnail in reading order at a zoom the header controls, hover names the layer and shows its
  index, click selects the row in the Frames list. This is the only way to *see* a
  `Texture2DArray` in the editor, and it is why the tab exists.
- **A95-D10 — Game-side follow-up, not this amendment.** Once the tab lands, the game's
  `TextureArrayBuilder.cs` / `TextureArrayConfig.cs` are retired and `UnitPartSO.textureArray`
  points at the tab's output. That is a G-task; the package never references game types.

---

## 3. Read first

- `Authoring/Assets/ClipAsset.cs` lines 265–315 (`SpriteTrack`, `SpriteKey`);
  `Runtime/Components/AnimationToolkitEnums.cs` lines 76–116 (`SpriteSliceSpace`, `SpriteFrameMode`,
  `SpriteIndexMode`).
- `Shaders/Includes/ToolkitFlipbook.hlsl` `SliceUV` (rounded float layer index — D6 writes ints
  that arrive as floats).
- `Editor/TexturePacker/TexturePackBaker.cs` in full — the decode cache and readable-copy pattern
  to copy; `TexturePackRecipeAsset.cs` + `TexturePackRecipeAssetUtility.cs` — the named-on-creation /
  Save-only rule.
- `Samples~/CompositeActor/Editor/CompositeActorBuilder.cs` `CreateFlipbookTexture` lines 116–160 —
  the package's one existing `Texture2DArray` construction (format, filter, wrap, `SetPixels(…,
  layer)`).
- `Editor/TexturePacker/ImageCatalogColumn.cs` (or its A82 host) — the drag source.
- The sprite key inspector: grep `SpriteKey` / `sliceIndex` in `Editor/ClipEditor/Panes/
  ClipInspectorPane.cs`.
- Vault `Editor.md` "Pattern: reading source texture pixels", "Pattern: writing a texture asset in
  place".

---

## 4. Design

### 4.1 Types (T1, orchestrator) — `Authoring/Assets/SpriteSheetAsset.cs` per D2, plus
`SpriteTrack.sheet` (D6).

### 4.2 `Editor/SpriteSheets/SpriteSheetValidation.cs` (T2) — static; `FindSizeMismatches` per D3
and `DedupeFrameName(string name, IReadOnlyCollection<string> taken) → string` per D4.

### 4.3 `Editor/SpriteSheets/SpriteSheetBaker.cs` (T3) — instance class; `Bake(SpriteSheetAsset
sheet, out string error) → bool`: validate sizes (D3), readable copies, new `Texture2DArray`,
`SetPixels32(…, layer)` per frame in list order, `Apply`, write or overwrite at `outputPath`,
assign `sheet.texture`, renumber `frames[i].index = i`.

### 4.4 `Editor/ClipUtilities/SpriteSheetAssetUtility.cs` (T4) — create / rename / trash / save.

### 4.5 Columns (T5, T6, T7) — `Editor/SpriteSheets/SpriteSheetCatalogColumn.cs` (A82 column
host), `SpriteSheetFramesColumn.cs` (the list + drag target + Add + drag-reorder),
`SpriteSheetPreviewElement.cs` (the contact sheet, D9).

### 4.6 `Editor/SpriteSheets/SpriteSheetsPanel.cs` (T8) — header (size, count, filter, wrap, mips,
Bake, unsaved marker, output path), three columns, `Dispose`.

### 4.7 Sprite key inspector (T9) — D6 in the pane.

---

## 5. Tasks

- [ ] **T0 — Baseline (orchestrator).** Gate; totals. One `execute_code` probe: create a 2-layer
  `Texture2DArray(4, 4, 2, RGBA32, mipChain: true)`, `SetPixels32` both layers, `Apply(true)`,
  read back `GetPixels32(layer: 1, mip: 1)` — confirms Unity generates array mips from level 0 so
  D5's `generateMips` needs no per-layer mip sources. Record the result in the build log. Grep
  the sprite key inspector range.
- [ ] **T1 — Types (orchestrator).** §4.1. Gate. Commit `A95-T1`.
- [ ] **T2 — Validation + fixture [parallel-safe]** — Files: new `SpriteSheetValidation.cs`, new
  `Tests/EditMode/SpriteSheetValidationTests.cs` (`SizeMismatches_NameEveryOffender`: four frames,
  two wrong → both names, in list order; `DedupeFrameName_AppendsCounter`: "head", "head",
  "head" → "head", "head 1", "head 2"). Revert-to-fail: return on first mismatch; drop the counter.
- [ ] **T3 — Baker + fixture [parallel-safe]** — Files: new `SpriteSheetBaker.cs`, new
  `Tests/EditMode/SpriteSheetBakerTests.cs` (`Bake_LayerOrderIsListOrder`: three in-memory 2×2
  solid-colour `Texture2D`s in a temp sheet under `Assets/A95TestScratch/`, bake, `GetPixels32(layer)`
  of the written array equals the source colour per layer, `frames[i].index == i`; `TearDown`
  deletes the folder). Revert-to-fail: write layers in reverse. Read `TexturePackBaker.cs`.
- [ ] **T4 — Asset utility [parallel-safe]** — Files: new `SpriteSheetAssetUtility.cs`. Read
  `TexturePackRecipeAssetUtility.cs`.
- [ ] **T5 — Catalog column [parallel-safe]** — Files: new `SpriteSheetCatalogColumn.cs`.
- [ ] **T6 — Frames column [parallel-safe]** — Files: new `SpriteSheetFramesColumn.cs`. Read the
  Images catalog's drag-start code (grep `DragAndDrop` in the A81 column). Drag-reorder within the
  list renumbers indices (D3).
- [ ] **T7 — Contact sheet element [parallel-safe]** — Files: new `SpriteSheetPreviewElement.cs`.
  Thumbnails come from the frame's `source` (already a `Texture2D`) so the sheet previews before
  the first Bake; after Bake they still read the sources, never the array (no readback needed).
- [ ] **T8 — Panel [parallel-safe]** — Files: new `SpriteSheetsPanel.cs`. Read `TexturePackerPanel.cs`.
- [ ] **T9 — Sprite key inspector: sheet + frame picker [parallel-safe]** — Files:
  `ClipInspectorPane.cs` (the sprite key range only). D6, including the `baseIndex` dropdown and
  the disabled state for `AtlasRect` tracks.
- [ ] **T10 — Docs [parallel-safe]** — Files: new `Documentation~/sprite-sheets.md` (workflow, D0
  in two sentences so a reader knows why there is no atlas builder, D7's material rule),
  `Documentation~/cutout-characters.md` (a paragraph pointing at the tab).
- **Gate the wave.** `SpriteSheetValidationTests`, `SpriteSheetBakerTests`, `ClipEditorAddEventTests`
  (inspector pane regression). Commit `A95-T2..T10`.
- [ ] **T11 — Window wiring (orchestrator).** Tab enum/UXML/`BindTab`/`Show…Tab`/layout test;
  `index.md`; `CHANGELOG.md` `## [0.42.0]`; `package.json`; `Conformance_G` (`SpriteSheetValidation`
  is static with the `Validation` suffix on the allowlist if absent; `SpriteSheetBaker` is an
  instance). Gate.
- [ ] **T12 — Drive.** Full suites. Build a 4-frame sheet from four same-size project PNGs into
  `Assets/A95Scratch/`; reload the asset; the array's `depth == 4` and layer 2's pixels equal
  source 2; open a clip with a sprite track, bind the sheet, pick frame 2 → `sliceIndex == 2` on
  disk after save; set a track to `RelativeToBase`, base = frame 1, key = frame 3 → `sliceIndex ==
  2`. Drag the array into a `ToolkitSpriteUnlitArray` material's `_MainTexArray` and enter Play
  with the composite sample actor: the part steps through the four frames. Capture the contact
  sheet. Delete scratch.
- [ ] **T13 — Vault + HANDOFF.** Vault note "Sprite Sheets tab (A95)": D0's two facts and the T0
  mip probe result.
- [ ] **T14 — Close.** Roadmap checkbox.
- [ ] **T15 — ⏸ owner checkpoint.** Message: "Sprite Sheets tab: New, drag frames in from Images,
  reorder, Bake, Save, drag the array into the part material. Then in the Clip Editor pick a frame
  by name on a sprite key. ⚠ Arrays bake as uncompressed RGBA32 — want a compressed-format
  follow-up? And the game-side G-task retiring `TextureArrayBuilder.cs` (D10) — schedule it?"

---

## 6. Deliberately out of scope

- Atlas / grid PNG output of any kind (D0). The `AtlasRect` runtime path stays as-is, builder-less.
- Trimming transparent borders, mixed frame sizes in one sheet, rotation packing.
- Writing the array into materials (A96 cross-checks; nobody auto-writes).
- Importing existing Unity `Sprite` / `SpriteAtlas` assets or existing `Texture2DArray` assets.
- Compressed array formats (checkpoint question).

## 7. Build log

- **2026-09-12 — spec rewritten before build.** Owner asked what the sheet/atlas split meant for a
  project already on arrays. Findings: grid-PNG-by-`sliceIndex` cannot render (no columns/rows on
  the shader), and the atlas rect never resizes the fixed quad, so per-part frames must share a
  canvas anyway. Owner confirmed; atlas dropped, tab redefined as the array builder (D0).
