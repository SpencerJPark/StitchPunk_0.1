# Texture Channel Packer Tool — Design Spec

> **Status:** 🔨 built (2026-07-09) · code landed, Editor compile + verify pending — see [`verify-texturechannelpacker.md`](verify-texturechannelpacker.md).
> **Raw source:** conversation request 2026-07-09 — "in-editor texture packing tool: node window, drag images on, wire channels into slots, render out a texture asset".

---

**Skills Needed** (project skills in `.claude/skills/` — see [Skills index](../../Memories/Code/Skills.md)):
- *(none of the `dots-*` scaffolding skills apply — this is pure Editor tooling, no ECS surface)*. The build reuses two existing files as pattern references instead:
  - [`DialogueSequenceEditorWindow.cs`](../../../_Scripts/Editor/DialogueEditor/DialogueSequenceEditorWindow.cs) — the project's proven `UnityEditor.Experimental.GraphView` window: palette drag-and-drop, `MakePort` factory, `GetCompatiblePorts` filtering, `graphViewChanged` sync.
  - [`PainterlyMaskPacker.cs`](../../../_Scripts/Editor/PainterlyMaskPacker.cs) — the proven bake path: byte-exact PNG decode via `File.ReadAllBytes` + `Texture2D.LoadImage` (bypasses importer, so sources need **no** readable/uncompressed settings), `Color32[]` channel merge, `EncodeToPNG`, overwrite-in-place (preserves GUID + material references), importer flags set after import.

Related plan: [DirectionalTexturePacking_System.md](DirectionalTexturePacking_System.md) packs 4 facing textures into RGBA at *bake/import* time for a fixed pipeline; this tool is the *general-purpose, interactive* packer for hand-authored masks (and could later generate that system's inputs).

---

## 1. Purpose & v1 scope

An `EditorWindow` (menu **Stitch Punk ▸ Texture Channel Packer** ← DECISION: menu path/title) containing a node graph. Spencer drags greyscale source images from the Project window onto the canvas — each becomes a **Source Image node** with a thumbnail and four output ports (R/G/B/A). A single **Pack Output node** has four input ports (R/G/B/A). Wire any source channel into any output channel (e.g. Image 1's alpha → output R, Image 2's red → output G), press **Bake**, and a packed PNG is written into `Assets/` as a normal texture asset to be imported/configured like any other.

**v1 handles:**
- Drag-and-drop of `Texture2D` assets onto the graph → Source Image nodes with thumbnails.
- Channel-level wiring: each source node exposes R/G/B/A output ports; output node has R/G/B/A single-capacity input ports.
- **Per-channel invert toggle** on the output node (roughness ↔ smoothness without repainting).
- **Per-channel default slider** (0–1) used when a channel is unwired (defaults: R/G/B = 0, A = 1 ← DECISION: default values).
- **Resolution field** on the output node (auto-set to largest source on first wire); mismatched sources are **CPU-bilinear resampled** to the output resolution at bake time.
- **Live composite preview** thumbnail on the output node (small debounced mini-bake on wiring change).
- Bake → PNG via `SaveFilePanelInProject` on first bake; subsequent bakes overwrite the remembered path in place (GUID preserved). Importer defaults applied **only when the file is newly created** (`sRGBTexture = false`, mipmaps on ← DECISION: linear default); after that the user owns the import settings.
- **Optional recipe save**: window is transient by default, but a toolbar **Save Recipe / Load Recipe** snapshots the whole graph (sources, wiring, inverts, defaults, resolution, output path, node positions) to a `TexturePackRecipeSO` for one-click repacks when a source painting changes.

**Out of v1:** per-wire levels/remap curves, multiple output nodes per graph, non-PNG/JPG source formats (see §4), channel-isolate preview modes (← DECISION: composite-only preview vs RGBA isolate dropdown), automation/batch repack of all recipes.

## 2. Architecture

Pure Editor code, all under `Assets/_Scripts/Editor/TexturePacker/` inside the existing `StitchPunk.Editor.asmdef`. No runtime types, no ECS entry pattern — the tool's only output is a `.png` asset on disk.

Three layers, deliberately separated so the bake logic is testable/reusable without the window:

```
TexturePackerWindow (EditorWindow, toolbar: Bake · Save Recipe · Load Recipe)
   └── TexturePackerGraphView (GraphView: drag-drop, edges, one enforced output node)
         ├── SourceImageNodeView   (thumbnail + R/G/B/A output ports)   × N
         └── PackOutputNodeView    (R/G/B/A input rows: port + invert toggle + default slider,
                                    resolution field, preview Image, Bake button)
   └── TexturePackerBaker (static; pure data-in → PNG-out, no GraphView types)
```

The GraphView layer converts the visual graph into a plain `PackJobDescription` struct (per channel: source asset path or `null`, source channel, invert flag, default value; plus resolution + output path) and hands it to `TexturePackerBaker`. The recipe SO serializes exactly that description plus node positions — so **recipe = bake description + layout**, one source of truth.

Follows the project's global conventions: no `var`, explicit types, semantic names (see `RULES.md` — they apply to Editor code too).

## 3. Entry points

*(Editor tool — the "request model" section doesn't apply.)* Entered via:
- **Menu item** `Stitch Punk ▸ Texture Channel Packer` — opens an empty transient graph (output node pre-placed).
- **Recipe asset** — `Load Recipe` toolbar button (or double-click the SO ← DECISION: wire `OnOpenAsset` for double-click?) restores a saved graph for repacking.

## 4. Data model

- **`PackChannelSource`** *(serializable struct)* — `string sourceTextureGuid`, `int sourceChannel` (0–3 = R/G/B/A, −1 = unwired), `bool invert`, `float defaultValue`.
- **`TexturePackRecipeSO : ScriptableObject`** *(lives in the Editor assembly — editor-only asset, never baked/shipped)* — `PackChannelSource[4] channels`, `int2 resolution`, `string outputAssetPath`, plus layout: `List<SourceNodeLayout> { string textureGuid; Vector2 position; }`, `Vector2 outputNodePosition`. Sources referenced by **GUID** (survives moves/renames), resolved via `AssetDatabase.GUIDToAssetPath` on load; missing GUIDs produce a placeholder node with a warning badge, not a hard failure.
- **Source pixel access** — `TexturePackerBaker` loads each source **from file bytes** (`File.ReadAllBytes` + `LoadImage`, the `PainterlyMaskPacker` pattern): byte-exact, works regardless of import settings, but limits sources to **PNG/JPG files** (fine for the Affinity-export workflow). Non-PNG sources (TGA/PSD/EXR) error with a clear message in v1. ← DECISION: add a `RenderTexture`-blit fallback later for arbitrary formats (costs byte-exactness through colour-space conversion).
- **Resample** — when a source's size ≠ output resolution, a CPU bilinear sample of the needed channel fills the output-sized buffer (single greyscale channel → cheap; a 4K texture is 16M samples, still well under a second).

## 5. Editor classes *(no runtime systems)*

| File (all new, `Assets/_Scripts/Editor/TexturePacker/`) | Responsibility |
|---|---|
| `TexturePackerWindow.cs` | `EditorWindow` + `[MenuItem]`; toolbar (Bake, Save Recipe, Load Recipe); owns the GraphView; graph ⇄ `PackJobDescription`/recipe conversion. |
| `TexturePackerGraphView.cs` | `GraphView` subclass: grid, zoom, `GetCompatiblePorts` (output→input, no self, single output node), `DragUpdated`/`DragPerform` for `DragAndDrop.objectReferences` containing `Texture2D` → spawn `SourceImageNodeView` at mouse position; deletes clean up edges. |
| `SourceImageNodeView.cs` | `Node`: title = texture name, ~72 px thumbnail (`Image` element bound to the imported asset preview — display only), four output ports R/G/B/A (`Port.Capacity.Multi` — one source channel may feed several output channels). |
| `PackOutputNodeView.cs` | `Node`, exactly one per graph (enforced like the dialogue editor's one-Refresher rule): four rows of [input port (`Capacity.Single`) · invert `Toggle` · default `Slider(0–1)` (visible only while unwired)], `Vector2IntField` resolution, preview `Image`, Bake button. Wiring changes schedule a debounced (~300 ms) preview re-bake at 128×128. |
| `TexturePackerBaker.cs` | Static. `Bake(PackJobDescription description)`: decode sources → per-channel fill (sample/resample, invert, or default) → `Color32[]` → `TextureFormat.RGBA32` (RGB24 when A is unwired **and** its default is 1) → `EncodeToPNG` → `File.WriteAllBytes` → `AssetDatabase.ImportAsset` → first-creation importer defaults. Also `BakePreview(description, 128)` returning a transient `Texture2D`. |
| `TexturePackRecipeSO.cs` | The SO from §4 (+ `CreateAssetMenu` under `Stitch Punk/Texture Pack Recipe`). |

## 6. MonoBehaviour bridge

Not applicable — no runtime side.

## 7. Integration points

- **Consumes nothing at runtime; produces ordinary `Texture2D` assets** used anywhere (shader-graph mask slots, `TextureArrayBuilder` inputs, the painterly mask, future `DirectionalTexturePacking` sources).
- `PainterlyMaskPacker.cs` becomes redundant once this ships — its fixed R/G/B workflow is a strict subset. Keep it until a `T_PainterlyMask` recipe asset reproduces it, then delete (← DECISION: retire PainterlyMaskPacker?).
- No vault context file exists for `_Scripts/Editor/` — the build should add `_Vault/Memories/Code/Editor.md` (folder inventory + the GraphView and byte-exact-decode patterns) and link it from the `Assets/CLAUDE.md` folder map.

## 8. Proposed file manifest

**New:**
- `Assets/_Scripts/Editor/TexturePacker/TexturePackerWindow.cs`
- `Assets/_Scripts/Editor/TexturePacker/TexturePackerGraphView.cs`
- `Assets/_Scripts/Editor/TexturePacker/SourceImageNodeView.cs`
- `Assets/_Scripts/Editor/TexturePacker/PackOutputNodeView.cs`
- `Assets/_Scripts/Editor/TexturePacker/TexturePackerBaker.cs`
- `Assets/_Scripts/Editor/TexturePacker/TexturePackRecipeSO.cs`
- `Assets/_Vault/Memories/Code/Editor.md` (new context file, see §7)

**Edited:**
- `Assets/CLAUDE.md` — folder-map row for `_Scripts/Editor/` → `Editor.md`.
- `Assets/_Vault/Tasks/Plans/README.md` — status row (already added with this spec).

**Assets:** none required; recipe SOs are created ad hoc by the user (suggested home: `Assets/Settings/TexturePackerRecipes/` ← DECISION: recipe folder).

## 9. Build phases

1. **Graph shell** — window + menu item, GraphView with grid/zoom, drag-drop `Texture2D` → source nodes with thumbnails and R/G/B/A ports, pre-placed output node with ports, invert toggles, default sliders, resolution field. No bake yet. *(Signal: wiring and node manipulation feel right; incompatible connections are refused.)*
2. **Bake end-to-end** — `TexturePackerBaker` + Bake button: decode, resample, invert/default fill, save-panel → PNG, overwrite-in-place repacks, first-creation importer defaults. *(Signal: a packed texture whose channels match the wiring, inspected in the importer preview.)*
3. **Preview** — debounced 128×128 composite mini-bake into the output node's `Image`. *(Signal: preview updates within ~half a second of any wiring/invert/default change.)*
4. **Recipes** — `TexturePackRecipeSO` + Save/Load toolbar; GUID-based source resolution with missing-asset badges; repack from a loaded recipe without re-wiring. *(Signal: close window, reopen recipe, bake → byte-identical output.)*
5. **Docs** — write `_Vault/Memories/Code/Editor.md`, update `Assets/CLAUDE.md` folder map, move this plan per the execute-plan flow.

## 10. Verification

All verification is Editor-driven by Spencer (no play mode, no rebake needed — the tool never touches subscenes):

1. Open **Stitch Punk ▸ Texture Channel Packer**; drag two greyscale PNGs from `Assets/Textures/` onto the canvas.
2. Wire image 1's **A** port → output **R**; image 2's **R** port → output **G**; leave B unwired (default 0) and A unwired (default 1); toggle invert on G.
3. Bake to `Assets/Textures/Test_Packed.png`; in the importer preview cycle R/G/B/A channels and confirm each matches its source (G inverted, B black, A white).
4. Repaint one source PNG externally, re-Bake to the same path — confirm the texture GUID and any material references survive (same check `PainterlyMaskPacker` relies on).
5. Save a recipe, close the window/Editor, reload the recipe, Bake — confirm identical output and restored node layout.
6. Mixed sizes: wire a 512² and a 1024² source, output 1024 — confirm the small source is resampled without offset/tiling artifacts.

## Decisions (resolved 2026-07-09, at build time)

- [x] §1 — Menu path is **`Window ▸ Stitch Punk ▸ Texture Channel Packer`**, window title "Texture Packer". Placed under `Window/` to match the existing `DialogueSequenceEditorWindow`; the bare `Stitch Punk/` root is used by one-shot menu *actions*, not windows.
- [x] §1 — Unwired defaults: **R/G/B = 0, A = 1**. The invert toggle applies to sampled values only, never to a flat default (a stale toggle must not silently flip a slider), so the toggle is hidden while a channel is unwired and the slider is hidden while it is wired.
- [x] §1 — First-creation importer defaults: **`sRGBTexture = false`, mipmaps on, wrap Repeat**, plus `alphaSource = None` when the output is RGB24. Applied only when the PNG did not already exist.
- [x] §1 — Preview is **composite + an R/G/B/A isolate dropdown** (`PackPreviewChannel` enum on the output node). Composite is forced opaque so a packed alpha cannot hide the colour channels.
- [x] §3 — **Yes**, double-clicking a `TexturePackRecipeSO` opens the window (`[OnOpenAsset]`), alongside the toolbar `ObjectField`.
- [x] §4 — **Byte-exact first, importer blit as fallback.** PNG/JPG decode straight from disk bytes; anything else (TGA/PSD/EXR) goes through a linear `RenderTexture` blit + `ReadPixels` and logs a warning that values reflect import settings rather than the file. The tool never hard-fails on a readable texture.
- [x] §7 — **`PainterlyMaskPacker.cs` deleted in this commit.** Its R/G/B workflow is a strict subset; `Shaders.md` now points at the packer. Reproduce it by wiring `Mask_R/G/B.png` → output R/G/B and baking over `T_PainterlyMask.png`. *(Verification step 7 confirms parity before you rely on it.)*
- [x] §8 — Recipes default **beside the texture they produce** (the Save panel opens in the output PNG's folder, pre-named `<TextureName>_Recipe`), not in a central folder.

## Build notes (what actually shipped)

- `PackChannelIndex` (Red/Green/Blue/Alpha = 0..3, `Count`, `Names`, `PortColors`) is the single source of truth for channel order — no bare integers at call sites.
- `TexturePackerNodeUI` (shared `MakePort` / `SetHeaderColor`) lives in `SourceImageNodeView.cs`, mirroring how `DialogueNodeUIHelpers` sits beside its node views.
- The baker caches decoded sources keyed by asset path **+ file write time**, so the 250 ms debounced live preview does not re-decode a 4K PNG on every slider drag. Cache is cleared after each bake and on window close.
- Two GraphView traps hit during the build and now documented in [`Editor.md`](../../Memories/Code/Editor.md): `Port.Capacity.Single` does not enforce itself (the old edge must be torn down manually), and `RemoveElement(edge)` leaves surviving nodes' ports reporting `connected == true` unless both ends are explicitly `Disconnect`ed.
- `EditorUtility.InstanceIDToObject` is an **error** (not a warning) in Unity 6.5 — `EditorUtility.EntityIdToObject` replaces it.
