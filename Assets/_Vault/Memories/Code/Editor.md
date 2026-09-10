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
| _(Texture Channel Packer)_ | **Moved into the toolkit 2026-09-10 (Amendment A81)** — now the DOTS Animator's **Texture Packer** tab, `Packages/com.dotsanimationtoolkit/Editor/TexturePacker/`; see the package's `Documentation~/texture-packer.md`. Nothing game-side remains. |
| `DirectionSetContext/UnitDirectionSetContextProvider.cs` | The game's half of the toolkit's direction-set context seam (`DirectionSetsPanel_System.md` §4, 2026-08-29). `[InitializeOnLoad]` registers it with `DirectionSetsPanel.SetContextProvider`; it flattens every `UnitSO`'s idle/moving/stance/action mappings into `"<Unit> · <state>"` entries carrying the set, plus the rig **and clip set** resolved from the prefab's `ActorAuthoring` (first set, warning when there are several), and the unit's `animationDirections`. **The direction-set authoring tool itself is no longer game-side** — it is a tab of the toolkit's Clip Editor. |
| `DialogueEditor/` | `DialogueSequenceEditorWindow` (GraphView node editor for `DialogueSequenceSO`) + `DialogueSequenceSOEditor`. |
| `AnimationEditor/` | Hybrid preview-scene animation tooling: `AnimationClipEditorWindow`, `AnimationPreviewController(+Editor)`, `EditorAnimationSystem`, `EditorApplyAnimatedPoseSystem`, `AnimationClipUtilities`. |
| `NarrativeEditor/` | `NarrativeEventSOEditor` custom inspector. |
| `TextureArrayBuilder.cs` + `TextureArrayConfig.cs` | Builds `Texture2DArray` assets from a folder of slices (body-part texture arrays). |
| `PainterlyMaskGenerator.cs` | Procedurally generates the painterly stroke mask. |
| `PainterlyGradientLUTGenerator.cs` | Bakes the 64×64 gradient-map palette atlas (see [[Shaders]]). |
| `ColorRampGenerator.cs` | `ColorRampSO` inspector (live gradient preview + **Bake Ramp Texture**) and **Stitch Punk ▸ Bake All Color Ramps** → `Assets/Textures/ColorRamps/T_Ramp_<name>.png`. These 1D ramps are what `PainterlyShader` remaps `_MainTex` luminance through (inverted: light → ramp start). Bakes overwrite in place so texture GUIDs survive; **renaming the SO mints a new texture**. See [[Shaders]]. Replaced `RampShaderGUI.cs`, deleted 2026-07-28 — it baked a non-asset `Texture2D` that could never serialize. |
| `ItemSOEditor.cs`, `BehaviorSOEditor.cs` | Custom inspectors for the SO data assets. |
| `SearchableEnumDrawer.cs` | `PropertyDrawer` giving long enums a searchable popup. |
| `ShowWhenDrawer.cs` | `PropertyDrawer` for `[ShowWhen("siblingBool", shownWhen)]` (`Data/Attributes/ShowWhenAttribute.cs`) — hides the field entirely unless a SIBLING bool matches; works in nested classes/list elements. Used by `PaletteSlot` (min/max only when `useFullRange` off) and `ColorVariation` (`alternative` only when `hasAlternative` on). |

---

## Pattern: feeding host data into a toolkit tool without coupling the package

`PackagingConformanceTests` (d) forbids any `com.dotsanimationtoolkit` file from naming `StitchPunk`
or a host `Assets/` folder, so a toolkit panel can never reference a game type. Two shapes have been
used for the seam, and the second replaced the first:

- **Event-shaped (retired 2026-08-29).** The package exposed `public static event System.Action
  OnDirectionSetsButtonClicked` and a `ToolbarButton` that only showed when it had a subscriber; the
  host opened its own window from the click. Gone with `DirectionSetEditorWindow`.
- **Data-shaped (current).** The package declares an interface —
  `IDirectionSetContextProvider` + `DirectionSetContextEntry { label, set, previewRig,
  actorDirections }` — and the host registers an implementation via
  `DirectionSetsPanel.SetContextProvider`. The tool stays *in* the toolkit and works standalone
  (no provider = the dropdown hides); the host supplies only pre-labelled strings and toolkit
  assets. Prefer this shape: it keeps the sellable tool sellable instead of making the package a
  launcher for a game-side one.

The panel it feeds is a **tab** of the Clip Editor, not a toggled cover pane — since 2026-08-29 the
toolkit's top bar is four exclusive tabs and `SetActiveTab` is their only writer. If a host ever
needs to open one, the entry points are `ClipEditorWindow.FocusClipEditing` /
`FocusVatBakeSettings` / `FocusDirectionSetsTab(asset)`; do not reach for a `Show…Tab` method.

**Whichever shape, the host's registration must sit in a `static` constructor decorated with
`[InitializeOnLoad]`.** A bare `static` ctor only runs the first time something touches the type,
and nothing ever touches a registration class — so without the attribute the hook silently never
happens on a fresh domain load (found 2026-08-29: the old toolbar button was missing entirely until
the window was opened by hand once). Do not drop the attribute.

## Pattern: GraphView node windows

Two node editors use `UnityEditor.Experimental.GraphView` — `DialogueSequenceEditorWindow`
here, and the toolkit's `TexturePackerGraphView` (`Packages/com.dotsanimationtoolkit/Editor/TexturePacker/`,
hosted in a panel rather than a window). The shared skeleton:

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

The toolkit's `TexturePackBaker.DecodeSource` (`Packages/com.dotsanimationtoolkit/Editor/TexturePacker/`)
is the reference implementation. Two tiers:

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

## Texture Channel Packer — moved to the toolkit (2026-09-10, Amendment A81)

The packer is now a tab of the DOTS Animator: `Packages/com.dotsanimationtoolkit/Editor/TexturePacker/`,
documented in the package's `Documentation~/texture-packer.md`. The game-side folder, its
`Window ▸ Stitch Punk ▸ Texture Channel Packer` entry and `TexturePackRecipeSO` are gone (no recipe
asset ever existed in `Assets/`, so nothing needed migrating).
