---
tags: [memory, code, editor, tooling]
related: "[[RULES]], [[Shaders]], [[Data]], [[Systems_Animation]]"
---

# _Scripts/Editor — Editor Tooling

Everything in `Assets/_Scripts/Editor/` compiles into **`StitchPunk.Editor.asmdef`**
(references `Unity.Entities`, `Unity.Entities.Hybrid`, `Unity.Mathematics`,
`Unity.Collections`, `StitchPunk.Data`, `StitchPunk.Components`, `UnityEditor.UI`).

The asmdef does **not** restrict platforms, so **every file here must be wrapped in
`#if UNITY_EDITOR … #endif`**. Forgetting the guard breaks player builds with
"UnityEditor does not exist in the current context".

The project's hard rules ([[RULES]]) apply to Editor code too: **no `var`**, no
single-character names, explicit types everywhere.

---

## Inventory

| Path | What it is |
|---|---|
| `TexturePacker/` | **Texture Channel Packer** — node-graph window that packs greyscale images into RGBA channels (see below). |
| `DialogueEditor/` | `DialogueSequenceEditorWindow` (GraphView node editor for `DialogueSequenceSO`) + `DialogueSequenceSOEditor`. |
| `AnimationEditor/` | Hybrid preview-scene animation tooling: `AnimationClipEditorWindow`, `AnimationPreviewController(+Editor)`, `EditorAnimationSystem`, `EditorApplyAnimatedPoseSystem`, `AnimationClipUtilities`. |
| `NarrativeEditor/` | `NarrativeEventSOEditor` custom inspector. |
| `TextureArrayBuilder.cs` + `TextureArrayConfig.cs` | Builds `Texture2DArray` assets from a folder of slices (body-part texture arrays). |
| `PainterlyMaskGenerator.cs` | Procedurally generates the painterly stroke mask. |
| `PainterlyGradientLUTGenerator.cs` | Bakes the 64×64 gradient-map palette atlas (see [[Shaders]]). |
| `ItemSOEditor.cs`, `BehaviorSOEditor.cs` | Custom inspectors for the SO data assets. |
| `SearchableEnumDrawer.cs` | `PropertyDrawer` giving long enums a searchable popup. |
| `ShowWhenDrawer.cs` | `PropertyDrawer` for `[ShowWhen("siblingBool", shownWhen)]` (`Data/Attributes/ShowWhenAttribute.cs`) — hides the field entirely unless a SIBLING bool matches; works in nested classes/list elements. Used by `PaletteSlot` (min/max only when `useFullRange` off) and `ColorVariation` (`alternative` only when `hasAlternative` on). |

---

## Pattern: GraphView node windows

Two windows use `UnityEditor.Experimental.GraphView` — `DialogueSequenceEditorWindow`
and `TexturePackerWindow`. The shared skeleton:

- `EditorWindow.CreateGUI()` builds a `Toolbar` (from `UnityEditor.UIElements`) plus a
  `GraphView` with `flexGrow = 1`.
- The `GraphView` subclass wires `SetupZoom` + `ContentDragger` + `SelectionDragger` +
  `RectangleSelector`, inserts a `GridBackground` at index 0, and sets
  `graphViewChanged = OnGraphViewChanged`.
- Ports come from `node.InstantiatePort(Orientation.Horizontal, direction, capacity, type)`.
  **Always fully qualify `UnityEditor.Experimental.GraphView.Direction`** — it collides
  with other `Direction` types in scope.
- `GetCompatiblePorts` must reject same-node and same-direction ports, or GraphView
  offers self-connections.

### Gotchas learned here

- **`Port.Capacity.Single` does not enforce itself.** Connecting a second edge to a
  single-capacity input silently leaves both attached. Tear the old edge down inside
  `graphViewChanged` (`edge.output.Disconnect(edge)`, `edge.input.Disconnect(edge)`,
  `RemoveElement(edge)`) before the new one lands.
- **`RemoveElement(edge)` does not disconnect its ports.** Any node that outlives the
  removal keeps reporting `port.connected == true`. Disconnect both ends explicitly.
- **`port.connected` is stale inside `graphViewChanged`** — the removals have not been
  applied yet. Re-read it one frame later via `schedule.Execute(…).ExecuteLater(0)`.
- Make a node undeletable with `capabilities &= ~(Capabilities.Deletable | Capabilities.Copiable)`.

---

## Pattern: reading source texture pixels

`TexturePackerBaker.DecodeSource` is the reference implementation. Two tiers:

1. **Byte-exact (preferred):** `File.ReadAllBytes(path)` → `Texture2D.LoadImage(bytes)`.
   Bypasses the importer entirely, so sources need **no** Read/Write-enabled,
   uncompressed, or linear import settings — and the bytes are exactly what was painted.
   Only decodes **PNG and JPG**.
2. **Blit fallback:** `Graphics.Blit` the imported texture into a temporary
   `RenderTexture` (`RenderTextureReadWrite.Linear`) → `ReadPixels` → `GetPixels32`.
   Reads any displayable texture (TGA/PSD/EXR), but the values have passed through the
   importer, so an sRGB-flagged source comes back colour-converted. Logs a warning.

## Pattern: writing a texture asset in place

`File.WriteAllBytes(absolutePath, texture.EncodeToPNG())` then
`AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate)`.
Overwriting the file **preserves the texture's GUID**, so every material reference and
import setting survives a re-bake. Only stamp `TextureImporter` settings when the file
did not exist beforehand — a repack must never silently undo the user's import config.

---

## Texture Channel Packer — `TexturePacker/`

Open with **Window ▸ Stitch Punk ▸ Texture Channel Packer**, or double-click a
`TexturePackRecipeSO` (`[OnOpenAsset]`).

Drag `Texture2D` assets from the Project window onto the canvas; each becomes a
`SourceImageNodeView` with a thumbnail and four **output** ports (R/G/B/A, `Capacity.Multi`).
The single `PackOutputNodeView` has four **input** ports (`Capacity.Single`); each row
shows an *invert* toggle while wired and a flat-value *slider* while unwired. Sources of
a different size than the output resolution are bilinearly resampled at bake time.

| File | Responsibility |
|---|---|
| `TexturePackerWindow.cs` | `EditorWindow` + toolbar (Bake, Bake As…, recipe `ObjectField`, Save Recipe, Clear); converts the graph into a `PackJobDescription`. |
| `TexturePackerGraphView.cs` | Canvas: drag-drop, port compatibility, single-capacity edge replacement, `ClearSources`. |
| `SourceImageNodeView.cs` | One source texture; also hosts `TexturePackerNodeUI` (shared `MakePort` / `SetHeaderColor`). |
| `PackOutputNodeView.cs` | Channel rows, resolution field, preview `Image` + channel-isolate `EnumField`, Bake button. Undeletable. |
| `TexturePackerBaker.cs` | Static, GraphView-free: `Bake()` writes the PNG, `BakePreview()` returns a 128 px thumbnail. Owns the decode cache. |
| `TexturePackRecipeSO.cs` | Editor-only SO snapshot of a graph: channels (sources by **GUID**), resolution, output path, node layout. Also `PackChannel`, `PackChannelIndex`. |

Notes:
- **`PackChannelIndex`** (`Red/Green/Blue/Alpha = 0..3`, `Count = 4`) is the single
  source of truth for channel order, names, and port colours. Never pass a bare integer.
- Recipes store sources by **asset GUID**, so moving or renaming a source PNG does not
  break them. An unresolvable GUID becomes a red "Missing source" node rather than a
  hard failure, and its wires are dropped from the bake.
- Alpha is written only when it is wired or its default ≠ 1; otherwise the output is
  `RGB24` and the importer gets `alphaSource = None`.
- The baker caches decoded sources keyed by path + file write time, so the debounced
  (250 ms) live preview does not re-decode a 4K PNG on every slider drag. The cache is
  cleared after each bake and when the window closes.
- `PainterlyMaskPacker.cs` (fixed R/G/B menu item) was **deleted 2026-07-09** — this tool
  supersedes it. Reproduce it by wiring `Mask_R/G/B.png` → R/G/B and baking over
  `T_PainterlyMask.png`. See [[Shaders]].
