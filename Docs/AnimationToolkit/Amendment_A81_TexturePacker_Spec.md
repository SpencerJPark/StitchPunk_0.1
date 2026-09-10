# Amendment A81 — Texture Packer tab: the channel packer moves into the toolkit, with an image sidebar

> **Status:** 📝 specced 2026-09-10, not built. Takes `0.28.0`.
> **Prompt:** [`Amendment_A81_TexturePacker_Prompt.md`](Amendment_A81_TexturePacker_Prompt.md).
> **Predecessors:** [`Amendment_A76_RigsTab_Spec.md`](Amendment_A76_RigsTab_Spec.md) (the catalog column
> this sidebar mirrors) and [`Amendment_A80_SharedAssetSelection_Spec.md`](Amendment_A80_SharedAssetSelection_Spec.md)
> (the one-wave, many-workers cut this task list copies). Where this document and A76's shipped
> `RigCatalogColumn.cs` disagree on how a catalog looks, the shipped code wins.
> **Executor:** one Editor-connected orchestrator running the gate; `worker` subagents edit files
> in **one wave of fourteen** and never touch MCP. Every task is at most two files, with named line
> ranges to read.

---

## 1. What the owner asked for (2026-09-10, verbatim where it matters)

> "I have a dots texture packer window in my projects, I want to move it out and make it part of the
> dots animation package, I want it to be the first tab in the row of window tabs for the Dots
> Animator, I also want it to have a side bar of a similar design to the left bar for clip sets and
> rigs, except I want it to be searching the images in my project and showing a small visual preview
> of them, if I want to pack an image or images all I have to do is drag it out of the side bar into
> the node space and then that will add it to it. still maintain the ability to drag the image files
> from the project folder, but I feel like this system will be easier to work with for the texture
> packer."

Asked four follow-ups, the owner chose: **boxed list rows** for the sidebar (the Clip Sets / Rigs
shape, a 48px thumbnail per row); a **segmented sidebar** — `Images | Recipes` — so saved recipes get
a catalog too and the toolbar's lone recipe object field goes; three of the four offered extras —
**double-click adds to canvas** (with a ✓ on rows already on the canvas and a hide filter),
**drop onto a channel row auto-wires**, and **resolution presets + a per-source channel view**;
and **no** rig-scoped filtering, standalone window, or auto-repack this round. This is the
acceptance layout:

```
┌ [Images][Recipes]        [👁][⟳] ┬ Recipe: T_PainterlyMask_Recipe   [Bake][Bake As…][Save Recipe][Clear] ┐
│ 🔍 search                        │                                                                        │
│ ┌────┬─────────────────────────┐ │   ┌ Mask_R ───────┐                    ┌ Pack Output ─────────┐        │
│ │▒▒▒▒│ Mask_R                  │ │   │ [thumb]       │ R ●────────────● R │ [inv]                │        │
│ │▒▒▒▒│ 1024 x 1024 · Assets/…  │ │   │ R G B A chips │ G ●            ● G │ [slider]             │        │
│ └────┴─────────────────────────┘ │   └───────────────┘                    │ Size 1024x1024 [▾]   │        │
│ ┌────┬─────────────────────────┐ │                                        │ View RGB  [preview]  │        │
│ │░░░░│ Mask_G               ✓  │ │                                        │ out: Assets/…/T.png  │        │
│ │░░░░│ 1024 x 1024 · Assets/…  │ │                                        │ [Bake]               │        │
│ └────┴─────────────────────────┘ │                                        └──────────────────────┘        │
└──────────────────────────────────┴────────────────────────────────────────────────────────────────────────┘
```

---

## 2. Decisions (recorded — do not re-ask). ⚠ marks an interpretation the owner has not confirmed.

- **A81-D1 — The packer becomes `ClipEditorTab.TexturePacker = 0`; every other value shifts up by
  one.** `tabToggles` is sized 7. The UXML gains `tab-texture-packer` (text "Texture Packer") as the
  first toggle in `tab-strip` and a `texture-packer-pane` cover pane beside the other five. The
  session-state `tab` int is not migrated — it persists within one session only (the enum's own
  summary says so), so a shifted number costs at most one wrong tab after the first reload.
- **A81-D2 — The whole tool moves; nothing stays game-side.** `Assets/_Scripts/Editor/TexturePacker/`
  (six files) is `git rm`'d by the orchestrator once the wave lands. No `TexturePackRecipeSO` asset
  exists anywhere in `Assets/` (verified by script GUID on 2026-09-10), so there is no asset to
  migrate and no `[MovedFrom]` shim. The `Window ▸ Stitch Punk ▸ Texture Channel Packer` menu item
  goes with it; the tab and the recipe double-click are the two entry points.
- **A81-D3 — New folder `Editor/TexturePacker/`, namespace `DotsAnimationToolkit.Editor`,** a sibling
  of `Editor/VatBaking/`. The recipe type lives there too, in the Editor assembly: it has no runtime
  meaning, its `ToPackRequest` needs `AssetDatabase`, and `Conformance_C` forbids `UnityEditor` in
  `Authoring/`.
- **A81-D4 — Renames, all forced by the package's gates, done in the move rather than as a follow-up:**

  | Game-side | Package | Why |
  |---|---|---|
  | `TexturePackRecipeSO` | `TexturePackRecipeAsset` | every toolkit SO ends in `Asset` |
  | `TexturePackerBaker` (static) | `TexturePackBaker` (**instance**, `sealed class`) | `Baker` is not a `Conformance_G` suffix; the decode cache is state and belongs on an instance — a static cache shared by two hosts would clobber |
  | `TexturePackerBaker.ComposePixels` & friends | `TexturePackMath` (static) | pure numeric channel work; `Math` is the allowed suffix, and it makes the packing testable without a file on disk |
  | `TexturePackerNodeUI` (static) | `TexturePackPortBuilder` (static) | `UI` is not a suffix; `Builder` is |
  | `PackJobDescription` / `PackChannelJob` | `PackRequest` / `PackChannelBinding` | "Job" in a DOTS package reads as `IJob` |
  | `PackChannelIndex` | unchanged, **added to `PlainNounStaticClasses`** | a plain noun; `Index` is neither allowed nor banned, so it needs the allowlist |
  | `TexturePackerWindow` | `TexturePackerPanel : VisualElement, IDisposable` | it is a tab, not a window |
  | `SourceImageNodeView`, `PackOutputNodeView`, `TexturePackerGraphView` | unchanged | — |

  The `[OnOpenAsset]` hook becomes `TexturePackRecipeAssetOpener` (internal static, allowlisted like
  `CutsceneAssetOpener`) calling `ClipEditorWindow.FocusTexturePackerTab(recipe)`.
- **A81-D5 — Two columns over one `TwoPaneSplitView(0, 280f, Horizontal)`:** the sidebar (fixed
  pane, `minWidth = 200f`) and the graph column (`minWidth = 480f`). Same 280 the other catalogs
  start at; the divider position is not persisted (the standing A76 §6 item, unchanged here).
- **A81-D6 — The sidebar's mode switch is two `ToolbarToggle`s in the `clip-editor__tab` style,**
  sitting where a pane title would, in one `toolkit-pane-header`; the header's `toolkit-pane-actions`
  slot shows the *active column's* actions (Images: 👁 hide-on-canvas toggle + ⟳ Refresh; Recipes:
  + New + ⟳ Refresh). The owner said the top tabs "should stay the same" — the same look one level
  down is the consistent choice, and a title reading "Images" under a lit "Images" toggle is noise.
  ⚠ interpretation.
- **A81-D7 — Image rows mirror `RigCatalogColumn` exactly** (`fixedItemHeight = 64f`, the zeroed
  search-field margins, the `Color.clear` item slot, `toolkit-box` rows with `marginTop/Bottom = 4f`),
  plus a 48×48 `Image` on the left of the header row. Title = texture name; info =
  `"<W> x <H> · <folder>"`; a ✓ `Label` at the right of the title row while the texture is on the
  canvas. `SelectionType.Multiple`, so a shift-click run can be dragged as one.
- **A81-D8 — Thumbnails are the imported textures themselves** (`Image.image = texture`,
  `ScaleMode.ScaleToFit`), loaded lazily in `bindItem` and cached on the entry — never
  `AssetPreview.GetAssetPreview`, which is asynchronous, returns null on first ask and would need a
  polling repaint. `SourceImageNodeView` has shown thumbnails this way since July. The scan itself
  loads nothing: `FindAssets("t:Texture2D")` → paths → entries; only rows that scroll into view load.
- **A81-D9 — The scan lists `Assets/`-rooted textures only.** Anything under `Packages/` (Unity's
  own icons, TMP sprites, this package's sample textures) is excluded by path prefix, always. No
  toggle this round.
- **A81-D10 — A sidebar drag is an editor drag.** `PointerMoveEvent` with `pressedButtons == 1` on a
  row → `DragAndDrop.PrepareStartDrag()`, `objectReferences = <the dragged textures>`,
  `StartDrag(...)`, `StopPropagation()` — the idiom `ClipEditorWindow.RegisterReparentDrag` already
  uses (`:3762-3800`). The payload is the selection when the pressed row is in it, else that row
  alone. The graph's existing `DragUpdated`/`DragPerform` handlers read `DragAndDrop.objectReferences`
  and therefore accept a sidebar drag and a Project-window drag by the same code — "still maintain
  the ability to drag from the project folder" costs nothing.
- **A81-D11 — Drop on a channel row of the Pack Output node adds the source and wires its R
  channel into that slot.** R because a greyscale mask carries the same value in R, G and B, and the
  wire can be re-dragged in one motion if not. Only the first dragged texture is wired; any others
  are added unwired beside it. A drop anywhere else on the canvas adds nodes, unwired, as today.
- **A81-D12 — Double-click on an image row adds it at the visible centre of the graph.** Rows
  already on the canvas show ✓; the 👁 toggle hides them from the list. Neither re-adds — the graph's
  `AddSourceNode` already de-duplicates by GUID.
- **A81-D13 — The recipe catalog is the Clip Sets shape, including its New:** `+ New` creates
  `NewTexturePackRecipe.asset` immediately in the remembered folder (EditorPrefs
  `DotsAnimationToolkit.TexturePacker.RecipeFolder`, validated with `IsValidFolder`, fallback
  `"Assets"`), selects it, pings it, and clears the graph. Right-click Rename (inline, via
  `InlineRenameEditing.Begin`) and Delete (confirm dialog, `MoveAssetToTrash`). Clicking a recipe
  loads it. `Save Recipe` writes into the selected recipe, or — with none selected — prompts for a
  path as today and then selects the result. The remembered folder is updated whenever a recipe is
  created, saved, or picked. ⚠ interpretation: mirrors A77's "New makes the empty asset active".
- **A81-D14 — Deleting a recipe is allowed.** Unlike a rig, nothing references a recipe; the packed
  PNG it produced is untouched. The dialog says so.
- **A81-D15 — The graph column's chrome is a `toolkit-pane-header`, not a `Toolbar`.** Left: a
  `Label` `texture-packer-recipe-label` reading `"Recipe: <name>"` or `"Unsaved graph"`. Right,
  pushed by `toolkit-pane-actions`: Bake, Bake As…, Save Recipe, Clear as
  `ToolkitIcons.MakeIconTextButton`s (icon + word, the catalogs' New/Refresh style). The recipe
  `ObjectField` is gone (D13 replaces it).
- **A81-D16 — Resolution presets are a `GenericDropdownMenu` off a "Presets ▾" button** beside the
  size field (`RigsPanel.OpenRowKindPicker` at `:429-437` is the idiom): `Match Largest Source`,
  `256`, `512`, `1024`, `2048`, `4096`. A number sets the size field to N×N; the first item raises
  `MatchLargestSourceRequested`, which the panel answers with the largest source on the canvas. The
  size field stays the single source of truth.
- **A81-D17 — Source channel view: four `ToolbarToggle` chips `R G B A` under each source
  thumbnail.** None lit = the texture as imported. Lighting one raises `ChannelViewChanged(node,
  channelIndex)`; the panel builds a preview through `TexturePackBaker.BakePreview` with R, G and B
  all bound to that source channel (so the result is greyscale) at the source's own size capped at
  96px, and hands it to `node.SetChannelPreview(texture)`, which owns and destroys it. No new baker
  method — the existing preview path already does this.
- **A81-D18 — The graph view owns recipe translation.** `LoadFromRecipe`, `WriteToRecipe`,
  `BuildPackRequest` move from the window onto `TexturePackerGraphView`: they are nodes-and-ports
  code, and moving them keeps the panel small enough for one worker.
- **A81-D19 — The GraphView is hosted in its own `graphHost` element.** `GraphView` calls
  `StretchToParentSize()` on itself (absolute, all insets 0) — added as a sibling of the header it
  would draw over it. The panel adds the graph to a plain `VisualElement` with `flexGrow = 1` under
  the header, never beside it.
- **A81-D20 — Two fixtures, both on `TexturePackMath`.** The sidebar, the nodes, the graph and the
  panel are UI wiring (HANDOFF §2: zero tests); the recipe asset's `ToPackRequest` hits
  `AssetDatabase`; the baker writes files. The channel routing, invert, default fill and resampling
  are the logic the feature exists for, were never tested game-side, and are pure once extracted.
- **A81-D21 — Tab tooltip and log prefix.** Tooltip: "Pack greyscale images into the channels of one
  texture: drag images from the sidebar or the Project window onto the canvas, wire their channels
  into the Pack Output node, and bake over the output in place." Log prefix:
  `"[DOTS Animation Toolkit] Texture Packer: "`.

---

## 3. Read first (the executor and every subagent — only what your task names)

1. Repo root `CLAUDE.md`; `Docs/AnimationToolkit/HANDOFF.md` §2, §3, §5 (skip §4 history);
   `Assets/_Vault/Memories/Code/RULES.md`; `Assets/_Vault/Tasks/NewPlans/Cutscene_Roadmap.md` §4.
2. `Assets/_Vault/Memories/Code/Editor.md` — sections **"Pattern: GraphView node windows"** (the
   four gotchas: single-capacity ports do not self-enforce, `RemoveElement` does not disconnect,
   `port.connected` is stale inside `graphViewChanged`, fully qualify `Direction`), **"Pattern:
   reading source texture pixels"**, **"Pattern: writing a texture asset in place"**, and
   **"Texture Channel Packer"**.
3. `Assets/_Vault/Memories/Code/AnimationToolkit.md` sections **"Shared editor chrome"**, **"Never
   rebuild a pane from a value-changed callback"**, **"Rigs tab (A76)"**, **"Both catalog tabs
   create, rename and delete in place (A77)"**.
4. The game-side source being moved, `Assets/_Scripts/Editor/TexturePacker/` — by named range only:
   - `TexturePackerWindow.cs` (504 lines): `:29-77` open/`[OnOpenAsset]`; `:78-162` `CreateGUI`,
     `OnDisable`, `BuildToolbar`; `:164-211` `BuildJobDescription`/`BuildChannelJob`; `:213-282`
     `Bake`, `BakeAs`, `DirectoryOfOrAssets`, `BakeTo`; `:284-342` `OnGraphChanged`,
     `AutoAssignResolution`, `SchedulePreviewRefresh`, `RefreshPreview`; `:344-437` `ClearGraph`,
     `LoadRecipe`; `:439-503` `SaveRecipe`, `WriteGraphInto`.
   - `TexturePackerBaker.cs` (463 lines): `:1-76` types + cache; `:77-148` `Bake`; `:150-204`
     `BakePreview`, `IsolateChannel`; `:206-327` `ComposePixels`, `SampleChannelBilinear`,
     `ReadChannel`, `WriteChannel`, `ToByte`; `:329-431` `DecodeSource`, `DecodeByteExact`,
     `DecodeViaBlit`; `:433-462` `ValidateDescription`.
   - `TexturePackerGraphView.cs` (235), `SourceImageNodeView.cs` (151), `PackOutputNodeView.cs`
     (202), `TexturePackRecipeSO.cs` (151) — each in full.
5. `Packages/com.dotsanimationtoolkit/Editor/ClipEditor/Authoring/RigCatalogColumn.cs` in full (315
   lines) — the sidebar columns mirror it, comments and all.
6. `Editor/ClipEditor/Authoring/ClipSetsPanel.cs:52-64` (the split view) and `:621-635`
   (`CreateAndSelectNewClipSet` — the New idiom D13 copies); `Editor/ClipEditor/Authoring/ClipSetSaveLocation.cs`
   in full (66 lines — its static helpers are reused, its class is not copied).
7. `Editor/ClipUtilities/ActorProfileAssetUtility.cs` in full (110 lines) — Create / Rename / Trash,
   the shape `TexturePackRecipeAssetUtility` copies.
8. `Editor/ClipEditor/Shared/ToolkitIcons.cs:148-192` (`MakeIconButton`, `MakeIconTextButton`,
   `SetButtonIconAndText`), `:235-257` (`SetToggleIcon`); `Editor/ClipEditor/Authoring/InlineRenameEditing.cs:13`
   (`Begin(Label, string, Action<string>)`); `ClipEditorWindow.uss:239-420` (`toolkit-pane-*`,
   `toolkit-box*`), `:581-612` (`clip-editor__cover-pane`, `clip-editor__hint`).
9. `Editor/ClipEditor/ClipEditorWindow.cs`: `:36` (`StyleSheetPath`), `:70-71` (the two USS class
   constants), `:239-275` (pane fields, `tabToggles`), `:494-560` (`ShowWindow`, the `Focus…Tab`
   entry points, `FocusTab`), `:764-812` (`OnDisable` disposal), `:1002-1003` (pane lookup),
   `:1567-1614` (`BindTabs`/`BindTab`), `:1616-1683` (`SetActiveTab`, `ResolveActiveTransportTarget`,
   `ApplyActiveTab`), `:1760-1783` (`ShowClipSetsTab` — the template), `:3757-3800`
   (`RegisterReparentDrag` — the drag-start idiom).
10. `Editor/ClipEditor/Authoring/RigsPanel.cs:429-437` (`GenericDropdownMenu` idiom);
    `Editor/ClipEditor/Cutscene/CutsceneAssetOpener.cs` in full (28 lines).
11. `Tests/EditMode/ClipEditorLayoutTests.cs:30-92, 158-190` (the element-name arrays);
    `Tests/EditMode/PackagingConformanceTests.cs:293-338` (D), `:339-371` (E), `:381-395` (the
    plain-noun allowlist), `:408-466` (F), `:467-536` (G).

---

## 4. Design

### 4.1 Data types — `Editor/TexturePacker/PackRequest.cs` (T1, orchestrator, before the wave)

The moved value types, renamed per D4, so every worker codes against names that already exist on
disk. Straight transliterations of `TexturePackerBaker.cs:20-47` and `TexturePackRecipeSO.cs:60-80`.

```csharp
namespace DotsAnimationToolkit.Editor
{
    /// <summary>Channel slot indices, names and port colours for the packer; every channel-indexed API takes one of these, never a bare integer.</summary>
    public static class PackChannelIndex
    {
        public const int Red = 0; public const int Green = 1; public const int Blue = 2; public const int Alpha = 3; public const int Count = 4;
        public static readonly string[] Names = { "R", "G", "B", "A" };
        public static readonly Color[] PortColors = { … };   // the four colours from TexturePackRecipeSO.cs:73-78
    }

    public struct PackChannelBinding          // was PackChannelJob
    {
        public string sourceAssetPath; public int sourceChannel; public bool invert; public float defaultValue;
        public bool IsWired => sourceChannel >= 0 && !string.IsNullOrEmpty(sourceAssetPath);
    }

    public struct PackRequest                 // was PackJobDescription
    {
        public PackChannelBinding[] channels; public Vector2Int resolution; public string outputAssetPath;
    }

    public enum PackPreviewChannel { RGB = 0, R = 1, G = 2, B = 3, A = 4 }

    /// One decoded source: pixels bottom-up as Texture2D.GetPixels32 returns them.
    public sealed class PackSourcePixels      // was TexturePackerBaker.DecodedSource, now public so TexturePackMath can take it
    {
        public Color32[] pixels; public int width; public int height; public long fileWriteTicks;
    }
}
```

### 4.2 Pure packing — new `Editor/TexturePacker/TexturePackMath.cs` (T2)

`public static class TexturePackMath`. The body of `ComposePixels` (`:206-266`),
`SampleChannelBilinear` (`:268-296`), `ReadChannel`/`WriteChannel`/`ToByte` (`:298-326`) and
`IsolateChannel` (`:186-204`), with the decode call replaced by a lookup the caller supplies:

```csharp
/// Composes width x height pixels: unwired channels take their flat default, wired ones sample (and optionally invert) their source. False when a wired channel's source is missing from sourcesByPath.
public static bool ComposePixels(PackRequest request, IReadOnlyDictionary<string, PackSourcePixels> sourcesByPath, int width, int height, out Color32[] packedPixels)

/// Rewrites every pixel to the greyscale of one channel, alpha 255.
public static void IsolateChannel(Color32[] pixels, int channelIndex)

public static byte SampleChannelBilinear(PackSourcePixels source, int sourceChannel, int destinationX, int destinationY, int destinationWidth, int destinationHeight)
public static byte ReadChannel(Color32 pixel, int channelIndex)
public static void WriteChannel(ref Color32 pixel, int channelIndex, byte value)
public static byte ToByte(float normalizedValue)
```

Keep both existing `//` comments in `ComposePixels` (the invert-applies-to-samples-only rule, the
exact-size fast path); they are the reasoning. Nothing in this file touches `UnityEditor`.

### 4.3 The baker — new `Editor/TexturePacker/TexturePackBaker.cs` (T3)

`public sealed class TexturePackBaker`. Instance state: `Dictionary<string, PackSourcePixels>
sourceCache`. Methods, each the game-side body with `ComposePixels` calls redirected to
`TexturePackMath` after resolving the request's wired paths through `DecodeSource` into a
dictionary:

```csharp
public void ClearSourceCache()
/// Writes the packed PNG over request.outputAssetPath in place (the GUID survives), imports it, stamps import defaults only when the file did not exist. Logs and returns false on a bad request or an undecodable source.
public bool Bake(PackRequest request)
/// A small in-memory bake, caller owns the texture. previewChannel picks the composite or one channel as greyscale. Null when a source cannot be decoded.
public Texture2D BakePreview(PackRequest request, int maxDimension, PackPreviewChannel previewChannel)
```

Private: `DecodeSource` (cache keyed by path, invalidated by file write time — as today),
`DecodeByteExact`, `DecodeViaBlit`, `ValidateRequest`. `Bake` clears the cache after writing, as
today. `Debug` lines use the D21 prefix. The `"Assets/"` literal in `ValidateRequest` is legal under
`Conformance_D` (the segment after the slash is empty).

### 4.4 The recipe — new `Editor/TexturePacker/TexturePackRecipeAsset.cs` (T4a)

`TexturePackRecipeSO.cs` moved: `PackChannel`, `SourceNodeLayout` and the asset, minus
`PackChannelIndex` (now in `PackRequest.cs`).

```csharp
[CreateAssetMenu(fileName = "NewTexturePackRecipe", menuName = "DOTS Animation Toolkit/Texture Pack Recipe", order = 40)]
public sealed class TexturePackRecipeAsset : ScriptableObject
{
    public PackChannel[] channels = CreateDefaultChannels();
    public Vector2Int resolution = new Vector2Int(1024, 1024);
    public string outputAssetPath = string.Empty;
    public List<SourceNodeLayout> sourceLayouts = new List<SourceNodeLayout>();
    public Vector2 outputNodePosition = new Vector2(600f, 200f);

    /// True once anything has been placed on the canvas — a freshly created recipe is false, so the first source dropped in still sizes the output.
    public bool HasAnySource { get; }
    public int WiredChannelCount { get; }
    public static PackChannel[] CreateDefaultChannels()
    public PackRequest ToPackRequest()       // was ToJobDescription
}
```

`OnValidate` as today. One `<summary>` on the asset only (Conformance_F); the field `[Tooltip]`s
stay — they are inspector text, not doc comments.

### 4.5 Recipe surgery — new `Editor/ClipUtilities/TexturePackRecipeAssetUtility.cs` (T4b)

`public static class TexturePackRecipeAssetUtility`, the `ActorProfileAssetUtility` shape:

```csharp
public const string RecipeFolderPrefsKey = "DotsAnimationToolkit.TexturePacker.RecipeFolder";
public const string DefaultAssetName = "NewTexturePackRecipe";

/// The last folder a recipe was created, saved or picked in, when it still exists; otherwise "Assets".
public static string RecallRecipeFolder()
public static void RememberRecipeFolder(string projectRelativeFolder)
/// Creates an empty recipe at a uniquified path in folder. Null when the folder is invalid.
public static TexturePackRecipeAsset CreateRecipe(string folder)
public static bool RenameRecipe(TexturePackRecipeAsset recipe, string newName)
public static bool TrashRecipe(TexturePackRecipeAsset recipe)
```

`CreateRecipe` = `ClipSetSaveLocation.ResolveTargetAssetPath(folder, DefaultAssetName)` →
`CreateInstance` → `AssetDatabase.CreateAsset` → `SaveAssets` → `RememberRecipeFolder(folder)`.
`RenameRecipe` sanitizes with `ClipSetSaveLocation.SanitizeAssetName` and refuses an unchanged or
empty name. `TrashRecipe` = `MoveAssetToTrash`, false with a warning when it fails.

### 4.6 Nodes — `Editor/TexturePacker/TexturePackPortBuilder.cs` + `SourceImageNodeView.cs` (T5), `PackOutputNodeView.cs` (T6)

**`TexturePackPortBuilder`** (static): `MakePort(Node, UnityEditor.Experimental.GraphView.Direction,
Port.Capacity, string portName, Color)` and `SetHeaderColor(Node, Color)` — the two members of the
old `TexturePackerNodeUI`, unchanged.

**`SourceImageNodeView : Node`** — the moved node plus D17:

```csharp
public string TextureGuid { get; }
public Texture2D SourceTexture { get; }
public bool IsMissing { get; }
/// -1 while no chip is lit.
public int ViewedChannel { get; }
public event Action<SourceImageNodeView, int> ChannelViewChanged;
public Port GetChannelPort(int channelIndex)
public int FindChannelIndex(Port port)
/// Takes ownership of an isolated-channel thumbnail (null restores the imported texture) and destroys the one it replaces.
public void SetChannelPreview(Texture2D channelPreview)
public void DisposeChannelPreview()
public static SourceImageNodeView CreateFromGuid(string textureGuid)
```

The chips: a row of four `ToolbarToggle`s named `source-channel-chip-r/g/b/a`, text `R G B A`,
class `clip-editor__tab`, under the thumbnail, radio behaviour via `SetValueWithoutNotify` (the
`ApplyActiveTab` pattern — assign the other three without notify or the callback re-enters). Not
built on a missing node. Lighting the lit chip again turns it off (`ViewedChannel = -1`) and
restores `thumbnail.image = SourceTexture` — this one differs from the top tabs deliberately, because
"no channel" is a real state here.

**`PackOutputNodeView : Node`** — the moved node plus D11 and D16:

```csharp
public event Action SettingsChanged;
public event Action BakeRequested;
public event Action MatchLargestSourceRequested;
/// A texture drop landed on one channel row.
public event Action<int, IReadOnlyList<Texture2D>> SourceDroppedOnChannel;
public Port GetChannelPort(int channelIndex)
public bool GetInvert(int) / void SetInvert(int, bool) / float GetDefaultValue(int) / void SetDefaultValue(int, float)
public Vector2Int Resolution { get; set; }
public PackPreviewChannel PreviewChannel { get; }
public void SetOutputPathLabel(string outputAssetPath)
public void SetPreviewTexture(Texture2D) / void DisposePreviewTexture()
public void RefreshChannelRows()
```

Each channel row (`BuildChannelRow`) registers `DragUpdatedEvent` (visual mode Copy when
`DragAndDrop.objectReferences` holds a `Texture2D`, else nothing) and `DragPerformEvent`
(`AcceptDrag`, raise `SourceDroppedOnChannel(channelIndex, textures)`, `StopPropagation()` — or the
GraphView's own handler also adds the node at the drop point). The presets button: `Button`
`output-presets-button` labelled `"Presets ▾"` beside the size field in one row; click opens a
`GenericDropdownMenu` (D16); a numeric pick sets `Resolution = new Vector2Int(n, n)` and raises
`SettingsChanged`.

### 4.7 The graph — `Editor/TexturePacker/TexturePackerGraphView.cs` (T7)

The moved view plus the recipe translation (D18) and three helpers the sidebar needs:

```csharp
public PackOutputNodeView OutputNode { get; }
/// Any node or edge added or removed, including through the helpers below.
public event Action GraphChanged;
public SourceImageNodeView AddSourceNode(string textureGuid, Vector2 graphPosition)
public SourceImageNodeView FindSourceNode(string textureGuid)
public IEnumerable<SourceImageNodeView> EnumerateSourceNodes()
public void ClearSources()
public void ConnectPorts(Port outputPort, Port inputPort)
public Vector2 GetVisibleCenter()

/// Adds each texture at the visible centre, cascading 30px, skipping GUIDs already on the canvas. Raises GraphChanged once.
public void AddSourcesAtVisibleCenter(IReadOnlyList<Texture2D> textures)
/// Adds the first texture 320px left of the output node, wires its R port into outputChannelIndex (replacing any edge already there), adds the rest unwired below it. Raises GraphChanged once.
public void AddSourcesWiredIntoChannel(int outputChannelIndex, IReadOnlyList<Texture2D> textures)
/// GUIDs of every source node on the canvas — what the sidebar marks ✓.
public List<string> CollectSourceGuids()

/// Rebuilds the canvas from a recipe: sources by GUID at their saved positions, wires, inverts, defaults, size, output node position.
public void LoadFromRecipe(TexturePackRecipeAsset recipe)
/// Writes channels, size, layouts and the output node position; the caller writes outputAssetPath.
public void WriteToRecipe(TexturePackRecipeAsset recipe)
public PackRequest BuildPackRequest(string outputAssetPath)
```

`LoadFromRecipe`/`WriteToRecipe`/`BuildPackRequest` are `TexturePackerWindow.LoadRecipe` (`:363-437`,
the part after the null-graph guard), `WriteGraphInto` (`:474-503`) and
`BuildJobDescription`/`BuildChannelJob` (`:164-211`) moved verbatim onto the view. The output node's
`SourceDroppedOnChannel` is subscribed in the constructor and answered by
`AddSourcesWiredIntoChannel`. **`AddSourcesWiredIntoChannel` must call `DisconnectExistingEdges`
before `ConnectPorts`** — `ConnectPorts` bypasses `graphViewChanged`, so the single-capacity
replacement the vault documents does not run on its own. `ClearSources` and the
`elementsToRemove` branch of `OnGraphViewChanged` call `DisposeChannelPreview()` on every source
node they drop, or the D17 textures leak.

### 4.8 The sidebar — `ImageCatalogColumn.cs` (T8), `RecipeCatalogColumn.cs` (T9), `TexturePackerSidebar.cs` (T10)

**`ImageCatalogColumn : VisualElement`** — `RigCatalogColumn` with the header removed (the host owns
it, D6) and a thumbnail added (D7, D8):

```csharp
public sealed class ImageCatalogEntry { public string Guid; public string AssetPath; public string Name; public string Folder; public Texture2D LoadedTexture; public bool IsOnCanvas; public Texture2D Load(); }

/// Double-click, or Enter on the selection.
public event Action<IReadOnlyList<Texture2D>> ImagesActivated;
/// The 👁 toggle and ⟳ Refresh, for the host's header.
public VisualElement HeaderActions { get; }
public void RescanProject();
public void SetOnCanvasGuids(IReadOnlyCollection<string> guids);
```

Element names: `images-search`, `images-list`, `image-row-box`, `image-row-thumbnail`,
`image-row-title`, `image-row-on-canvas-mark`, `image-row-info`, `images-refresh-button`,
`images-hide-on-canvas-toggle`. The 👁 toggle is a `ToolbarToggle` with an `Image` child set through
`ToolkitIcons.SetToggleIcon(toggle, icon, "d_scenevis_hidden_hover", "On canvas")`, tooltip "Hide
images already on the canvas". Scan: `AssetDatabase.FindAssets("t:Texture2D")` → `GUIDToAssetPath`
→ skip paths not starting `"Assets/"` (D9) → entries sorted `OrdinalIgnoreCase` by name. Filter =
search text on `Name` AND (`!hideOnCanvas || !IsOnCanvas`). `bindItem` calls `entry.Load()`
(`LoadAssetAtPath<Texture2D>`, cached) and writes `thumbnail.image`, `"<W> x <H> · <Folder>"`, the ✓
mark's `display`. Drag per D10 on `image-row-box`, threshold: only once the pointer has moved
(`PointerMoveEvent` fires only on movement, which is the threshold). Empty texts: `"No textures under
Assets/ yet."` / `"No images match your search."` — mind `Conformance_D`: `Assets/` with nothing
after the slash is legal, `Assets/Textures` is not.

**`RecipeCatalogColumn : VisualElement`** — `RigCatalogColumn` with the header removed and the A77
context menu kept:

```csharp
public event Action<TexturePackRecipeAsset> RecipeSelected;
public event Action NewRequested;
public event Action<TexturePackRecipeAsset, string> RecipeRenameRequested;
public event Action<TexturePackRecipeAsset> RecipeDeleteRequested;
public VisualElement HeaderActions { get; }          // + New, ⟳ Refresh
public TexturePackRecipeAsset SelectedRecipe { get; }
public void RescanProject();
public void SetSelectedRecipe(TexturePackRecipeAsset recipe);   // no event
public void RefreshRows();                                        // after a save or bake changed a row's info
```

Element names: `recipes-search`, `recipes-list`, `recipe-row-box`, `recipe-row-title`,
`recipe-row-info`, `recipes-new-button`, `recipes-refresh-button`. Row info:
`"<n> wired · <output file name>"` or `"<n> wired · no output yet"`; tooltip the output path. Empty
texts: `"No recipes in this project yet. Press New."` / `"No recipes match your search."`.

**`TexturePackerSidebar : VisualElement`** — the host:

```csharp
public enum SidebarMode { Images, Recipes }
public SidebarMode Mode { get; }
public ImageCatalogColumn Images { get; }
public RecipeCatalogColumn Recipes { get; }
public void SetMode(SidebarMode mode);
public void RescanProject();     // both
```

Layout: `name = "texture-packer-sidebar"`, `flexGrow = 1`, `minWidth = 200f`, padding `8/10/10` like
the catalogs. One `toolkit-pane-header` holding, on the left, `sidebar-images-toggle` and
`sidebar-recipes-toggle` (`ToolbarToggle`s, class `clip-editor__tab`, texts "Images" / "Recipes",
`clip-editor__tab--active` on the lit one, `SetValueWithoutNotify` radio — clicking the lit one is a
no-op that snaps back to true) and, on the right, a `toolkit-pane-actions` slot the host clears and
refills with `activeColumn.HeaderActions` on every `SetMode`. Below it both columns, the inactive
one `display = None`. Opens on Images.

### 4.9 The panel — `Editor/TexturePacker/TexturePackerPanel.cs` (T11)

```csharp
public sealed class TexturePackerPanel : VisualElement, IDisposable
{
    public TexturePackRecipeAsset LoadedRecipe { get; }
    public TexturePackerSidebar Sidebar { get; }
    public TexturePackerGraphView Graph { get; }
    public void RescanProject();
    /// Loads the graph, selects the recipe in the catalog, switches the sidebar to Recipes.
    public void LoadRecipe(TexturePackRecipeAsset recipe);
    public void Dispose();
}
```

Constructor: `style.flexGrow = 1`; the D5 split view with `Sidebar` and a graph column
(`texture-packer-graph-column`, `flexGrow = 1`, `minWidth = 480f`) holding the D15 header
(`texture-packer-header`, buttons `texture-packer-bake-button` `"d_SaveAs"`/"Bake",
`texture-packer-bake-as-button` `"d_SaveAs"`/"Bake As…", `texture-packer-save-recipe-button`
`"d_ScriptableObject Icon"`/"Save Recipe", `texture-packer-clear-button`
`"d_TreeEditor.Trash"`/"Clear" — every icon name carries its word as fallback, so a name that does
not resolve still reads) and the D19 `graphHost` with the `Graph` inside. One `TexturePackBaker`
field.

Wiring, all in the constructor:

| From | Handler |
|---|---|
| `Graph.GraphChanged` | `AutoAssignResolution()`, `Sidebar.Images.SetOnCanvasGuids(Graph.CollectSourceGuids())`, `SchedulePreviewRefresh()` |
| `Graph.OutputNode.SettingsChanged` | `SchedulePreviewRefresh()` |
| `Graph.OutputNode.BakeRequested` | `Bake()` |
| `Graph.OutputNode.MatchLargestSourceRequested` | `resolutionAssigned = false; AutoAssignResolution();` |
| every source node's `ChannelViewChanged` (subscribed by the graph and re-raised as `Graph.SourceChannelViewChanged` — add that pass-through event to §4.7's surface) | build a `PackRequest` with R, G, B bound to `(node path, channel)`, A default 1, resolution = the source's size; `node.SetChannelPreview(baker.BakePreview(request, 96, PackPreviewChannel.RGB))`; `-1` → `SetChannelPreview(null)` |
| `Sidebar.Images.ImagesActivated` | `Graph.AddSourcesAtVisibleCenter(textures)` |
| `Sidebar.Recipes.RecipeSelected` | `LoadRecipe(recipe)` |
| `Sidebar.Recipes.NewRequested` | `CreateRecipe(RecallRecipeFolder())` → rescan → `LoadRecipe(newRecipe)` → `PingObject` |
| `Sidebar.Recipes.RecipeRenameRequested` | `RenameRecipe` → rescan → reselect |
| `Sidebar.Recipes.RecipeDeleteRequested` | `DisplayDialog("Delete Recipe", "Delete '<name>'? The packed texture it produced is not touched.", "Delete", "Cancel")` → `TrashRecipe` → if it was `LoadedRecipe`, `ClearGraph()` → rescan |

`Bake`, `BakeAs`, `BakeTo`, `DirectoryOfOrAssets`, `AutoAssignResolution`, `SchedulePreviewRefresh`,
`RefreshPreview`, `ClearGraph`, `SaveRecipe` are the window's (`:213-342`, `:344-361`, `:439-472`)
with `graphView.X` → `Graph.X`, `TexturePackerBaker.` → `baker.`, `BuildJobDescription()` →
`Graph.BuildPackRequest(outputAssetPath)`, `LoadRecipe` → `Graph.LoadFromRecipe(recipe)` then
`outputAssetPath = recipe.outputAssetPath; resolutionAssigned = recipe.HasAnySource;` then the
label, `Sidebar.Recipes.SetSelectedRecipe(recipe)`, `Sidebar.SetMode(Recipes)`,
`RememberRecipeFolder(folder of recipe)`, `SchedulePreviewRefresh()`. `SaveRecipe` with no
`LoadedRecipe` prompts as today, then rescans and selects. After a save or a successful bake:
`Sidebar.Recipes.RefreshRows()`. `Dispose`: `Graph.OutputNode.DisposePreviewTexture()`, every source
node's `DisposeChannelPreview()`, `baker.ClearSourceCache()`, `scheduledPreview?.Pause()`.

### 4.10 Window integration — `Editor/ClipEditor/ClipEditorWindow.cs` + new `Editor/TexturePacker/TexturePackRecipeAssetOpener.cs` (T12)

Mirroring `ShowClipSetsTab` at every step:

- Fields beside `:248-249`: `private VisualElement texturePackerPane; private TexturePackerPanel texturePackerPanel;`.
  `tabToggles = new ToolbarToggle[7]` at `:268`.
- `:1002-1003`: `texturePackerPane = rootVisualElement.Q<VisualElement>("texture-packer-pane");`.
- `BindTabs` (`:1567`): first call `BindTab(ClipEditorTab.TexturePacker, "tab-texture-packer", <D21 tooltip>)`.
- `ApplyActiveTab` (`:1659`): `ShowTexturePackerTab(activeTab == ClipEditorTab.TexturePacker);` first in the list.
- New `ShowTexturePackerTab(bool isShown)` beside `ShowClipSetsTab`: lazy `new TexturePackerPanel()`,
  `texturePackerPane.Add(panel)`, `RescanProject()` on show, `EnableInClassList(HiddenUssClassName, !isShown)` last.
- `OnDisable` (`:801-804` neighbourhood): `texturePackerPanel.Dispose(); texturePackerPanel = null;`.
- Beside `FocusCutsceneTab` (`:518`): `public static void FocusTexturePackerTab(TexturePackRecipeAsset recipe)` —
  `FocusTab(ClipEditorTab.TexturePacker)` then `window.texturePackerPanel.LoadRecipe(recipe)` when both are non-null.
- `ResolveActiveTransportTarget` needs no case — the packer has no transport; `default: return null` covers it.

`TexturePackRecipeAssetOpener` is `CutsceneAssetOpener` with the type and method swapped.

### 4.11 Orchestrator edits (T15) — five files, none a worker task

- `ClipEditorTab.cs`: `TexturePacker = 0` with a one-line summary; the other six shift.
- `ClipEditorWindow.uxml:5-12`: `<uie:ToolbarToggle name="tab-texture-packer" text="Texture Packer" class="clip-editor__tab"/>` first; `:136-140`: `<ui:VisualElement name="texture-packer-pane" class="clip-editor__cover-pane clip-editor--hidden"/>` first.
- `ClipEditorLayoutTests.cs:39-40` and `:165-166`: add `"tab-texture-packer"`; `:79`: add `"texture-packer-pane"` with a one-line comment in the file's own voice.
- `PackagingConformanceTests.cs:381-395`: `"PackChannelIndex"`, `"TexturePackRecipeAssetOpener"` appended to `PlainNounStaticClasses`.
- `package.json`: `0.27.0` → `0.28.0`.

---

## 5. Tasks

One wave, **T2–T14 all `[parallel-safe]`**, gated once. T0, T1, T15, T16 are the orchestrator's,
T17 the drive, T18 the ⏸ checkpoint. Every worker brief pastes: the spec path, the task text, its
"Read" line, the §4 block it builds, the hard rules (no `var`, no single-letter names, explicit
types), Conformance_F's comment rule (one `<summary>` per file on the primary type, ≤3 lines, no
`<remarks>`, no spec citations, no `§`), and ends: "at turn 30 stop editing and write your report;
report ≤ 30 lines; never call any `mcp__UnityMCP__*` tool." Spawn from the repo root — A76's build
log records the read-guard breaking when cwd had drifted.

### T0 — Baseline and grounding (orchestrator)
1. `git status` — record anything uncommitted as the owner's; stage only your own paths throughout.
2. Gate per HANDOFF §3; record EditMode / PlayMode discovered totals in §7.
3. **Drive the game-side window once before anything moves.** Its verification checklist
   (`Assets/_Vault/Tasks/Verification/verify-texturechannelpacker.md`) has every box unticked since
   2026-07-09 and no recipe asset exists in the project — it may never have run. Open `Window ▸
   Stitch Punk ▸ Texture Channel Packer`; over `execute_code` (CodeDom C# 6: no `using`, fully
   qualified names, `resolvedStyle` in a second call) reflect the window's `graphView`, call
   `AddSourceNode` twice with two `Assets/`-rooted greyscale PNG GUIDs, `ConnectPorts` one wire,
   set the output path to `Assets/A81Scratch/T_Probe.png`, call `Bake`, and confirm the PNG
   exists and imports. Record in §7 what worked, and specifically whether the toolbar is visible
   above the graph or drawn over by it (D19 is written for either answer). If the Editor is closed,
   say so and record "not driven".

### T1 — `PackRequest.cs` (orchestrator, before spawning)
Write §4.1 by hand from `TexturePackerBaker.cs:20-47` and `TexturePackRecipeSO.cs:60-80`. Save. Do
not gate yet — the wave compiles as one.

### T2 — `TexturePackMath` + fixture [parallel-safe]
Files: **new** `Editor/TexturePacker/TexturePackMath.cs`, **new** `Tests/EditMode/TexturePackMathTests.cs`.
Read `TexturePackerBaker.cs:150-327` and §4.1, §4.2. Tests build `PackSourcePixels` by hand — no
asset, no file:
- `ComposePixels_RoutesInvertsAndFillsDefaults`: a 2×2 source whose R is `{0,64,128,255}`; request
  R ← (source, R) inverted, G unwired default 0.5, B ← (source, R), A unwired default 1 → pixel 1
  reads `(191, 128, 64, 255)`. (Revert-to-fail: drop the invert branch.)
- `ComposePixels_ResamplesAMismatchedSource`: a 1×1 source of value 200 into a 2×2 request → all
  four pixels' R are 200 and the exact-size path was not taken (a 1×1 read straight across would
  index out of range — the test passing at all proves resampling ran). Plus the missing-source
  path: a wired path absent from the dictionary → returns false, `packedPixels == null`.

### T3 — `TexturePackBaker` [parallel-safe]
Files: **new** `Editor/TexturePacker/TexturePackBaker.cs`. Read `TexturePackerBaker.cs:1-148`,
`:329-462`, §4.1, §4.3 and the `TexturePackMath` surface in §4.2. No fixture — it writes files.

### T4a — `TexturePackRecipeAsset` [parallel-safe]
Files: **new** `Editor/TexturePacker/TexturePackRecipeAsset.cs`. Read `TexturePackRecipeSO.cs` in
full, §4.1, §4.4. No fixture.

### T4b — `TexturePackRecipeAssetUtility` [parallel-safe]
Files: **new** `Editor/ClipUtilities/TexturePackRecipeAssetUtility.cs`. Read
`ActorProfileAssetUtility.cs` in full, `ClipSetSaveLocation.cs` in full, §4.4's surface, §4.5. No
fixture — three-line wrappers over `AssetDatabase`. The file **must** sit in `Editor/ClipUtilities/`
or `Conformance_G` rejects the `Utility` suffix.

### T5 — `TexturePackPortBuilder` + `SourceImageNodeView` [parallel-safe]
Files: **new** `Editor/TexturePacker/TexturePackPortBuilder.cs`, **new**
`Editor/TexturePacker/SourceImageNodeView.cs`. Read `SourceImageNodeView.cs` (game-side) in full,
`ClipEditorWindow.cs:1641-1657` (the radio idiom), `Editor.md` "Pattern: GraphView node windows",
§4.1, §4.6. No fixture.

### T6 — `PackOutputNodeView` [parallel-safe]
Files: **new** `Editor/TexturePacker/PackOutputNodeView.cs`. Read `PackOutputNodeView.cs` (game-side)
in full, `TexturePackerGraphView.cs:187-235` (the drag handlers being mirrored per row),
`RigsPanel.cs:429-437`, §4.1, §4.6, and the `TexturePackPortBuilder` surface. No fixture.

### T7 — `TexturePackerGraphView` [parallel-safe]
Files: **new** `Editor/TexturePacker/TexturePackerGraphView.cs`. Read `TexturePackerGraphView.cs`
(game-side) in full, `TexturePackerWindow.cs:164-211` and `:363-437` and `:474-503` (the three
members moving here), `Editor.md` "Pattern: GraphView node windows", §4.6's two node surfaces, §4.7.
No fixture.

### T8 — `ImageCatalogColumn` [parallel-safe]
Files: **new** `Editor/TexturePacker/ImageCatalogColumn.cs`. Read `RigCatalogColumn.cs` in full,
`ClipEditorWindow.cs:3757-3800`, `ToolkitIcons.cs:148-192, 235-257`, §4.8 (Images). No fixture.

### T9 — `RecipeCatalogColumn` [parallel-safe]
Files: **new** `Editor/TexturePacker/RecipeCatalogColumn.cs`. Read `RigCatalogColumn.cs` in full,
`InlineRenameEditing.cs:13`, §4.4's surface, §4.8 (Recipes). No fixture.

### T10 — `TexturePackerSidebar` [parallel-safe]
Files: **new** `Editor/TexturePacker/TexturePackerSidebar.cs`. Read `ClipEditorWindow.cs:1641-1657`
(radio idiom), `:70-71`, `RigCatalogColumn.cs:36-56` (header shape), §4.8 in full. No fixture.

### T11 — `TexturePackerPanel` [parallel-safe]
Files: **new** `Editor/TexturePacker/TexturePackerPanel.cs`. Read `TexturePackerWindow.cs:78-162`,
`:213-361`, `:439-472`; `ClipSetsPanel.cs:52-64`; §4.3, §4.5, §4.7, §4.8 surfaces; §4.9 in full.
No fixture. This is the largest task: if the report at turn 30 says the recipe handlers are
unwritten, the orchestrator spawns a second `worker` for exactly those, not a resume.

### T12 — Window wiring + opener [parallel-safe]
Files: `Editor/ClipEditor/ClipEditorWindow.cs`, **new** `Editor/TexturePacker/TexturePackRecipeAssetOpener.cs`.
Read `ClipEditorWindow.cs:239-275, 494-560, 764-812, 1002-1003, 1567-1614, 1616-1683, 1760-1783`,
`CutsceneAssetOpener.cs` in full, §4.9's surface, §4.10. No fixture. `ClipEditorTab.TexturePacker`
will not exist until T15 — write against the name; the wave compiles together.

### T13 — Documentation [parallel-safe]
Files: **new** `Documentation~/texture-packer.md`, `Documentation~/index.md` (one link line under
the page list, wherever `clip-editor.md` is linked). Read `Documentation~/clip-editor.md:1-60` (voice
and heading style), §1's layout sketch, §2 D6–D17. Write: what the tab is for, the sidebar (both
modes, drag, double-click, ✓/👁), the two drop behaviours, chips and presets, recipes (New / Save /
Rename / Delete / double-click), bake-in-place and the GUID guarantee, the PNG-vs-blit decode note.
**No `Assets/<Folder>` example paths** — `Conformance_D` scans `*.md`; write `Assets/…` or
`<YourFolder>/`. No spec citations, no `§`.

### T14 — Changelog [parallel-safe]
Files: `CHANGELOG.md` (a new `## [0.28.0] — A81 — texture packer tab` section above `## [0.27.0]`).
Read `CHANGELOG.md:1-40` for the voice, §2. Added: the tab, the sidebar, the recipe catalog, chips,
presets, channel-row drop, `TexturePackMath`. Changed: nothing user-facing in other tabs beyond the
tab strip gaining a first entry. Removed: nothing (the game-side tool was never in the package).

### T15 — Orchestrator edits
§4.11, by hand, after the wave's files are on disk. Then **T16** before the gate.

### T16 — Retire the game-side tool (orchestrator)
`git rm -r Assets/_Scripts/Editor/TexturePacker` (six `.cs`, six `.meta`, the folder `.meta`). Then
the vault: `Assets/_Vault/Memories/Code/Editor.md` — the inventory row and the whole "Texture Channel
Packer" section become two lines pointing at the package (`Packages/com.dotsanimationtoolkit/Editor/TexturePacker/`,
"a tab of the DOTS Animator, see `Documentation~/texture-packer.md`"), the GraphView and
pixel-reading patterns stay (they are still true, and `DialogueSequenceEditorWindow` still uses the
first); `Shaders.md:168` and any other `Window ▸ Stitch Punk ▸ Texture Channel Packer` mention →
"the DOTS Animator's Texture Packer tab"; `verify-texturechannelpacker.md` gets a status line
`superseded by Amendment A81's drive (2026-09-10)` and moves to `Tasks/Completed/`.

### T17 — Gate, drive, captures, docs (orchestrator)
1. Compile gate; then `TexturePackMathTests`, `ClipEditorLayoutTests`, `PackagingConformanceTests`
   by `test_names`. Full suites once at the end (HANDOFF §3); totals must not drop below T0's and
   EditMode gains 2.
2. Drive over `execute_code`, against `Assets/A81Scratch/` copies of two greyscale PNGs, never the
   owner's originals:
   - Open the DOTS Animator, `SetActiveTab(ClipEditorTab.TexturePacker)`. Second call: `images-list`
     `itemsSource.Count` equals the number of `Assets/`-rooted `Texture2D` assets; the sidebar and
     graph column both have non-zero `layout.width` near `280 : rest`; the header's four buttons
     resolve; `Graph.OutputNode` is on the canvas.
   - Reflect `Sidebar.Images` → raise `ImagesActivated` with one scratch texture → one source node,
     the row's ✓ visible; toggle 👁 → the row is filtered out.
   - `Graph.AddSourcesWiredIntoChannel(PackChannelIndex.Green, [second texture])` → the output's G
     port `connected == true` one frame later; a second call into G → still exactly one edge.
   - Set the output path to `Assets/A81Scratch/T_Packed.png`, `Bake` → the file exists, imports
     uncompressed, sRGB off; reload the `Texture2D` from disk and read one pixel through a linear
     `RenderTexture` blit to confirm G carries the second source and R the first's alpha or R (say
     which). Bake again → same GUID.
   - Recipes: `Sidebar.Recipes` New → an asset exists at `RecallRecipeFolder()`, is selected, the
     graph is empty; wire one source, `Save Recipe`; **reload the recipe from disk** and assert
     `WiredChannelCount == 1` and `outputAssetPath` set. Rename via `RenameRecipe` → the row
     reads the new name after `RescanProject`. `TrashRecipe` → gone from the catalog.
   - Double-click a recipe asset in the Project (`AssetDatabase.OpenAsset`) → the window lands on
     the Texture Packer tab with that recipe loaded.
   - A source node's chip: `SetChannelPreview` through the graph's pass-through event → the
     thumbnail's `image` is a 96px-capped greyscale texture; chip off → the imported texture again.
3. Capture `Library/A81TexturePackerCaptures/texture-packer-tab.png` (scale by `pixelsPerPoint`,
   check `EditorApplication.isFocused` first, positive window position — A76's build log has the
   recipe) and **look at it**: sidebar rows with thumbnails, ✓ on one, the two nodes wired, the
   header's four icon-word buttons on one line (the button-child text-measure trap makes a wrapped
   button obvious).
4. Delete `Assets/A81Scratch` and its `.meta`; `git status` shows only your paths plus T0's list.
5. HANDOFF §4 paragraph; the vault note `AnimationToolkit.md` gains one section "Texture Packer tab
   (A81)" — traps only: `StretchToParentSize` needs a host element, `ConnectPorts` bypasses
   single-capacity replacement, `SetValueWithoutNotify` in every radio, `Conformance_D` scans `*.md`.
   Commit per wave (`A81-T2..T14`, `A81-T15/16`, `A81-T17`), staging paths explicitly; push when green.

### T18 — ⏸ owner checkpoint
End the session with this message, verbatim in spirit:

> Open the DOTS Animator; **Texture Packer** is now the first tab. The left column lists every
> texture under `Assets/` with a thumbnail — search it, drag one or several onto the canvas (or
> double-click one), or drag one straight onto the R/G/B/A row of the Pack Output node to have it
> wired in. Rows already on the canvas show ✓; the eye button hides them. Dragging from the Project
> window still works. Under each source thumbnail the R G B A chips show that channel alone; beside
> the size field, **Presets ▾** offers the common squares and "Match Largest Source". Switch the
> sidebar to **Recipes**: New creates one in the last folder you used, click loads, right-click
> renames or deletes, Save Recipe writes the graph into the selected one, and double-clicking a
> recipe asset in the Project opens this tab on it. The old `Window ▸ Stitch Punk` entry is gone.
> Judge: the sidebar mode switch borrows the top-tab look (A81-D6) — right, or should it be a plain
> title with a dropdown? New creating the recipe instantly with a default name (A81-D13) — right, or
> should it prompt for a name first? And the 280px sidebar start width.

---

## 6. Deliberately out of scope (follow-ups, not omissions)

- **A standalone Texture Packer window** and **auto-repack when a source changes on disk** — offered
  2026-09-10, not chosen. Both are one small task each on top of this build (`VatBakeWindow` is the
  89-line template for the first).
- **A rig-scoped image filter** (textures referenced by the active rig's prefab materials) — offered,
  not chosen; the packer ignores `ActiveAssetSelection` entirely.
- **Including `Packages/` textures** in the sidebar (D9).
- **Persisting the sidebar divider** — the standing A76 §6 item, now on four tabs.
- **Extracting a shared catalog column** — the handoff's #1 item grows to five near-copies with
  this amendment (`ClipSetsPanel`, `RigCatalogColumn`, `ActorProfileCatalogColumn`,
  `ImageCatalogColumn`, `RecipeCatalogColumn`). Deliberate: the owner is about to judge all of them,
  and the extraction is its own session.
- **Undo for graph edits** — GraphView has none of its own and the game-side tool never had it.
- **Packing into a `Texture2DArray`** or reading `.psd` layers — different tools.

## 7. Build log

_(empty until the build)_
