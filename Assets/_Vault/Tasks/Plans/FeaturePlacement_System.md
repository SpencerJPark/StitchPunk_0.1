---
status: SPEC READY (2026-11-13), nothing built. Owner Q&A done (FP-D1 to FP-D4). Start at FP-S0, then phase FP-0.
extends: Tasks/Plans/WorldFeatures_System.md (WF). This spec replaces WF-5's line tasks (WF-5-1, WF-5-2) and becomes the way every later WF stamp places objects.
reference: Tasks/Claude/coveredUp.png (Features hint strip hidden under the Scene view tool overlay and the Clip Editor overlay)
names verified against: main at d3ba2a1e (2026-11-13). If a name has drifted, grep, follow the code, and note the drift in the log.
---

# Feature Placement: features place objects, objects own their behaviour

## 0. Context: what the owner hit

World Features works, but three things get in the way:

1. **There is no obvious way to add a feature.** The Features tab shows a grid of recipe tiles and nothing else. There is no `+`, no choice of shape (straight line, curve, box, circle, polygon) and no open line at all, so fences, hedges and tree rows cannot be drawn.
2. **The instructions are covered.** `TerrainSceneHud` draws its key strip with raw IMGUI 16 px above the bottom of the Scene view, and `TerrainFeatureTool.DrawHud` draws a second line at (12, 12). Unity's overlays (the Scene tool strip, `ClipEditorStageOverlay`) draw on top of both, as `coveredUp.png` shows.
3. **The tools stay live when the tab is hidden.** The owner docks the Terrain Editor next to the Inspector. Clicking the Inspector tab hides the window without disabling it, so `OnDisable` (the only place that calls `TerrainSceneHud.Clear()` and `OnHidden()`) never runs. `TerrainFeatureTool.OnSceneGui` also turns itself on whenever a `TerrainArea` is selected, whatever tab is showing.

The owner also wants new kinds of content: **tall grass that bends when units walk through it, wheat fields that vehicles and big units mow flat (the quads lie down in the direction of travel and read as decals on the ground), and fences of different kinds drawn as straight lines, curves or vector shapes.**

The plan in one line: **a feature is only a shape plus a way of placing things along or inside it. Whatever is placed is an ordinary prefab, and that prefab carries its own behaviour** (bend, crush, block, break, harvest). Adding a new kind of feature means making a prefab and a recipe asset, with no new code.

```
Shape (how you draw)      Curve | Straight | Box | Circle | Polygon    open or closed
   + Placer (how the shape becomes instances)   Fill | Along path | Rows
   + Placeables (prefabs, weighted)            each prefab owns its behaviour through its own authoring + material
        | existing bake pipeline (TerrainFeatureBaker, per tile, debounced)
        v
Tile products: area mask + ground paint (closed shapes only), entity instances (Interactable), card batches (Dressing)
Runtime: shared Trample field (bend + crush) that placed objects READ; they never know who walked on them
```

## 1. Owner answers (2026-11-13, do not re-ask)

- **FP-D1 Instructions live in a Scene-view Overlay**, which the owner can drag or dock and which nothing draws over. It exists only while the Terrain Editor tab is showing. When the Inspector (or any tab) covers the Terrain Editor, the overlay and **all** terrain editing in the Scene view go away.
- **FP-D2 Trample:** tall grass bends and springs back within seconds. Wheat flattened by a vehicle or big unit **stays flat**, reads like a decal track on the ground, regrows over a few game days, and its tracks are **saved**.
- **FP-D3 Fences block** units on foot and nav, can have gate gaps, and the **caravan or big units smash through** and leave broken pieces.
- **FP-D4 Logic lives in the objects; features are only ways to place them.** The owner must be able to build custom features easily.

## 2. Orchestrator decisions (final unless the owner overrides)

- **FP-D5 Three parts.** A `TerrainFeatureRecipe` asset = allowed shapes + one placer (`[SerializeReference]`, picked from a dropdown in its inspector) + a weighted placeable list + optional ground paint. Placers are pure planning code and know nothing about what they place. A placeable is any prefab. This is the extension point FP-D4 asks for: **a custom feature is a new prefab plus a new recipe asset**.
- **FP-D6 Two feature components, one recipe type.** `TerrainArea` (closed outline; writes the area mask, as today) and new `TerrainPath` (open line or closed loop; writes **no** area mask, only placements). Both hold a `TerrainFeatureRecipe`, raise `TerrainFeatureEvents`, and bake through `TerrainFeatureBaker`. A fence loop around a field is a closed `TerrainPath`, because a fence must not repaint or empty the ground inside it.
- **FP-D7 Shapes only matter while drawing.** After creation every shape is spline knots: Curve uses AutoSmooth knots, Straight and Polygon use Linear (sharp) knots, Box is 4 sharp knots, and Circle is 8 AutoSmooth knots on a circle. No component remembers the shape kind. Editing is the existing knot drag, insert and delete.
- **FP-D8 Migration keeps the forest working.** `TerrainFeatureRecipe : TerrainAreaRecipe`, so the rasteriser, the baker and every `TerrainArea` keep compiling. `TerrainForestRecipe` stays for existing assets. The menu `Stitch Punk/Terrain/Convert Forest Recipes` writes a matching `TerrainFeatureRecipe` (Fill placer, the forest's scatter rules as placeables) beside each one, and the filler accepts both until the old type is deleted in FP-1's last wave.
- **FP-D9 The two tiers carry over (WF-D9).** Interactable placeables become entities (trees, fences: they need nav, collision and state). Dressing placeables are merged into card batch meshes per tile (grass, wheat: thousands of quads, never entities). A Dressing prefab declares its cards with a new `TerrainCardSource` authoring component (a list of atlas entry, size and local offset), and `TerrainCardBatchMeshBuilder` expands it.
- **FP-D10 Trample is a shared field that placeables read.** Movers get a `TrampleSource` (radius, kind). `Brush` (every unit by default) bends grass. `Crush` (the caravan via `Vehicle`, and any unit authored as heavy) also lays wheat flat and breaks fences. "Heavy" means exactly `TrampleKind.Crush`; there is no second flag. A placeable's reaction lives in its material (`Painted/TrampleCard`: stiffness, max bend, crush response None or LayFlat, lay-flat lift). Dressing batches are grouped by material already, so the per-object behaviour batches for free.
- **FP-D11 Bend is transient and GPU-only.** One `_TrampleBendField` render texture (64 m x 64 m at 0.25 m, so 256 x 256 RG16F; direction and amount) follows the camera focus. Each frame it decays toward zero (recovery about 2 s), and the sources inside the window splat into it. A managed system in `TerrainSystemGroup` uploads the source list and runs the splat, in the same way `TerrainCardFacingSystem` sets globals. Nothing is saved.
- **FP-D12 Crush is saved as strokes, not pixels.** `TrampleCrushStroke { float2 startXZ; float2 endXZ; half widthMetres; float gameDay; }`: a Crush source that has moved 2 m appends a segment. Per-tile crush grids (0.5 m texels, RG8: direction and amount) are a cache, rasterised from strokes for resident tiles and packed into `_TrampleCrushAtlas` with the same min-tile and count layout as `_TerrainTileResidencyGrid`. Regrowth is computed from `WorldClock.totalWorldSeconds - gameDay` when a tile is re-rasterised (once per game hour, or when the tile loads). The saved ledger is the newest strokes in a `FixedList4096Bytes` snapshot on the Player (the `HarvestedNodeLedgerSnapshot` pattern; about 160 strokes). Older tracks regrow early, which nobody will notice.
- **FP-D13 Wheat lies flat in the vertex shader.** Each wheat card samples the crush atlas at its root. As the amount rises to 1 it rotates about its base to lie along the stored direction, lifted 2 cm with the `Painted/Ribbon` depth offset, so it reads as a decal stripe. The yaw billboard (`_TerrainCardFacingRight`) is blended out as the card lies down, or a flat card would spin with the camera.
- **FP-D14 Fences are post + segment placeables.** The Along path placer in Segments mode drops a post every `spacingMetres` and stretches the segment prefab between consecutive posts (a straight chord with each end on the ground height). Curves are approximated by chords. **Gaps:** a knot flagged as a gap (Alt+click in the Scene view, stored in a `TerrainPath.gapKnotIndices` list) skips the segment that starts there. Each segment bakes its own `NavFootprintAuthoring` box along its chord, so nav blocking stays in the object.
- **FP-D15 Breaking is the object's job.** `BreakableAuthoring` (on the fence segment prefab) bakes `Breakable { intactPart, brokenPart, respawnAfterGameDays }`. A `TrampleCrushContactSystem` (in `CombatExecutionSystemGroup` after `CaravanCrashSystem`; it copies that system's `OverlapCapsule` query) enables `BrokenPiece` on touched breakables. The visual swap copies the `HarvestedNodeVisualPart` and stump swap pattern (static entities: write `LocalToWorld`). Broken state uses a ledger keyed by position hash, which copies `HarvestedNodeRecord/Restore` and is saved the same way. How a broken piece stops blocking nav is spike FP-S2.
- **FP-D16 The overlay replaces both HUDs.** `TerrainToolOverlay` (`[Overlay(typeof(SceneView), "stitchpunk-terrain-tool", "Terrain", true)]`, UI Toolkit content, built to `EditorStyleGuide.md`) shows the active tab name, the shape segmented control while drawing (R11), key hints as keycap rows, the cursor info line, and refusal toasts. `TerrainSceneHud.DrawInSceneView` and `TerrainFeatureTool.DrawHud` are deleted; `TerrainSceneHud`'s `SetActiveTool`, `SetCursorInfo` and `Clear` API stays and now feeds the overlay.
- **FP-D17 One visibility gate for every terrain Scene tool.** New static `TerrainEditorVisibility { static bool ToolsLive; static event Action Changed; }`, set by `TerrainEditorWindow` from `OnBecameVisible`, `OnBecameInvisible`, `OnEnable` and `OnDisable`. Every `duringSceneGui` handler under `Editor/Terrain/` early-outs when `!ToolsLive`: `TerrainFeatureTool`, `TerrainRoadTool`, `TerrainBrushOverlay`, the water tool and any other found by `grep -rn "duringSceneGui +=" Assets/_Scripts/Editor/Terrain`. On going hidden: cancel any drawing, end drags, hide the overlay (`displayed = false`), and keep the selection. On going visible: show the overlay again and restore the shown panel's hints. Selecting a feature with the Terrain Editor hidden does nothing special; it is just a GameObject in the Inspector.
- **FP-D18 One primary action: `+ New feature`** at the top right of the Features tab (R12). It opens a picker of recipes grouped by `category` (Areas, Lines and fences, Ground cover), each tile with its thumbnail and allowed shapes. The last row, **Create recipe…**, asks for a placer kind and a folder, writes a `TerrainFeatureRecipe` asset with that placer, selects it in the Project and pings it, so a custom feature starts from a working asset. Picking a recipe arms drawing with the recipe's `defaultShape`. The overlay's segmented control (or keys 1 to 5) switches the shape while drawing, and only the recipe's allowed shapes are enabled. The old recipe grid becomes this picker; the feature list and the selected-feature card stay.
- **FP-D19 Rows placer for crops.** Rows has a direction in degrees (default: the outline's longest edge, with a drag handle in the Scene view), a row spacing, and a spacing along each row with jitter. Wheat = a Rows recipe with the wheat patch placeable, Dressing tier, painting a crop ground layer. WF-5-4's `TerrainFieldMeshBuilder` stays the far-view and under-crop ground; the wheat cards stand on it.
- **FP-D20 Spike before shaders (FP-S1).** Measure 20,000 grass cards plus 30,000 wheat cards inside a tile on the PC profile and on the Switch profile, before and after the bend and crush sampling. Per-tile Dressing caps for trample cards are set from those numbers in `TerrainPlatformProfile` (`maxTrampleCardsPerTile`).

## Skills Needed

`dots-authoring-baker` (`TrampleSourceAuthoring`, `TerrainCardSource`, `BreakableAuthoring`), `dots-system-scaffold` (bend, crush, contact, breakable ledger systems), `dots-test` (placers, stroke rasteriser, visibility gate), `shader-edit` (`Painted/TrampleCard`), `unity:ui-uitk` (overlay, picker).

## 3. Execution protocol

**The orchestrator observes; lower tiers do the work (owner rule, memory `feedback_low_tier_subagents`).** Tokens spent in the orchestrator are the expensive ones.

| Who | Model | Does | Never does |
|---|---|---|---|
| Orchestrator | Opus | briefs, reads diffs, runs the gate and lint, Unity MCP (refresh, console, tests, `execute_code` probes and spikes, asset creation), reads captures, ticks boxes, writes the log line, commits and merges | writes code, except a one-line fix found while checking a diff |
| `worker` | `model: sonnet` | every code edit: C#, shaders, USS, including the `[shared]` files | Unity MCP, more than two files, whole-file reads over 300 lines |
| `verifier` | `model: sonnet` | behaviour reviews of a worker diff the orchestrator doubts, Burst and aliasing reviews, R01 to R22 audits of the orchestrator's capture PNGs | edits |
| `verifier` | `model: haiku` | pure greps and inventories: "list every `duringSceneGui +=` under Editor/Terrain", "find the save hook for `HarvestedNodeLedgerSnapshot`", name-drift checks before a wave | judgement calls, reviews |

Every spawn prompt states its tier explicitly (`model: sonnet` or `model: haiku`). Prefer a scripted check (gate, lint, `git diff --stat`, a grep the orchestrator runs itself) over spawning any agent. Never `general-purpose`; a capped agent is never resumed (respawn with the remainder); after a 429, diff before respawning.

**Task tags.** `[parallel-safe]`: a Sonnet worker in the wave's single spawn message. `[shared]`: a Sonnet worker too, but it edits a file in section 9, so it runs alone in its wave (or with tasks touching other files) and the orchestrator tells any peer session first. `[orchestrator]`: Editor or MCP work only (probes, assets, bakes, Play checks, captures); if such a task turns out to need code, the code is briefed to a worker.

**Waves.** Spawn every task of a wave in one message; tasks code against the pinned surfaces in section 6, so none waits for another. Use the WF section 3 brief template (two files max, named line ranges, pasted pinned surface, Must-fail cell, `gate` before the report, stop at turn 30, report of 30 lines or fewer). Check `ListAgents` first and build in a worktree (`spec/feature-placement`) if a peer is on `main`. After each wave: `python Tools/devloop/devloop.py gate game`, `python Tools/devloop/devloop.py lint`, the Editor pass (refresh, console, the named fixtures only), revert-to-fail on every new fixture, tick the boxes, append one log line, commit with explicit paths.

**UI hard rule (root `CLAUDE.md`).** FP-0 and FP-1 change Scene-view UI and the Features tab: every brief names `Docs/AnimationToolkit/EditorStyleGuide.md` and R01 to R22. Before and after captures of the Features tab (`TerrainEditorCapture.CaptureTabs`) and of the Scene view with the overlay are read by the orchestrator. Overlay placement and mouse feel are owner checkpoints, because synthetic events do not reach `duringSceneGui` (WF-D15).

## 4. Read first (orchestrator, in order)

Root `CLAUDE.md`; `Memories/Code/RULES.md`; `Memories/Code/Terrain.md` (World Features section and every trap about static tile entities, Burst staleness, preview scenes); WF spec sections 2, 6.6, 6.7 and 14; `EditorStyleGuide.md`; this spec.

## 5. Existing surface this spec builds on (verified 2026-11-13)

| Thing | Where | Members used |
|---|---|---|
| Window tab switching | `Editor/Terrain/Window/TerrainEditorWindow.cs` | `SelectTool` (calls `OnHidden`/`OnShown`, `TerrainSceneHud.SetActiveTool`), `OnDisable` (the only `TerrainSceneHud.Clear()`), `shownPanel` |
| Panel base | `Window/TerrainToolPanel.cs` | `TerrainKeyHint { keys, action }`, `DisplayName`, `KeyHints`, `OnShown`, `OnHidden` |
| Scene HUD to replace | `Window/TerrainSceneHud.cs` | `SetActiveTool`, `SetCursorInfo`, `Clear`, `DrawInSceneView` (deleted) |
| Feature tool | `Features/TerrainFeatureTool.cs` | `BeginDrawOutline`, `BeginDragBox`, `CancelDrawing`, `OnSceneGui` (live when `trackedArea != null`), `DrawHud` (deleted), `CreateArea` |
| Overlay precedent | `Packages/com.dotsanimationtoolkit/Editor/ClipEditor/ClipEditorStageOverlay.cs` | `[Overlay(typeof(SceneView), id, name, true)]`, `CreatePanelContent` |
| Features panel | `Window/TerrainFeaturesPanel.cs` | recipe grid (`BuildRecipeTile`, `FindAllRecipes`, `ArmOutline`, `ArmBox`), feature list, selected card |
| Area feature and bake | `MonoBehaviours/Terrain/TerrainArea.cs`, `Features/TerrainFeatureBaker.cs`, `TerrainAreaFiller.cs`, `TerrainAreaRestrictedSampler.cs` | `TerrainFeatureEvents.Raise`, `densityMultiplier`, filler tier split and cap |
| Card batching | `Features/TerrainCardBatchMeshBuilder.cs`, `Painted/TreeCard` (`Assets/PaintedGround/Shaders/PaintedTreeCard.shader`) | `Build(IReadOnlyList<TerrainCard>, Vector3 meshOriginWorld, float cameraYawDegrees, out int vertexCount)`; sizes in uv3; yaw from `_TerrainCardFacingRight` |
| Scatter data | `Data/Terrain/TerrainScatterSet.cs` | `TerrainScatterPrefab { prefab, weight }`, `TerrainScatterTier { Interactable, Dressing }` |
| Nav blocker | `Authoring/Terrain/NavFootprintAuthoring.cs` | `halfExtentsMetres`, `rotationDegrees`, `cost` (255 blocks) |
| Vehicles | `Components/Structures/CaravanComponents.cs` | `Vehicle`, `CaravanDriving` |
| Contact query to copy | `Systems/CombatSystemGroup/CombatExecutionSystemGroup/CaravanCrashSystem.cs` | `OverlapCapsule` against `CollisionWorld` (line ~84) |
| Ledger + save to copy | `Components/Terrain/HarvestedNodeComponents.cs`, `HarvestedNodeRecordSystem`, `HarvestedNodeRestoreSystem`, `HarvestedNodeStumpPresentationSystem` | `HarvestedNodeLedgerSnapshot : IPersist` (`FixedList4096Bytes`), `HarvestedNodeVisualPart` swap via `LocalToWorld` |
| Residency layout to copy | `TerrainTileResidencyShaderSystem` | `_TerrainTileResidencyGrid` (min tile xy, count zw) |
| Shader globals pattern | `TerrainCardFacingSystem` (AnimationSystemGroup) | managed system writes a global per frame |

## 6. Pinned surfaces (new code; workers do not change these signatures)

Editor code in `Assets/_Scripts/Editor/Terrain/Features/` unless stated. Gameplay code is in the global namespace.

### 6.1 Visibility gate and overlay (FP-0)
```csharp
// Editor/Terrain/Window/TerrainEditorVisibility.cs
public static class TerrainEditorVisibility
{
    public static bool ToolsLive { get; }
    public static event Action Changed;
    public static void SetToolsLive(bool live);   // raises Changed only on a real change; on false also calls TerrainFeatureTool.CancelDrawing and TerrainSceneHud.Clear
}

// Editor/Terrain/Window/TerrainToolOverlay.cs
[Overlay(typeof(SceneView), "stitchpunk-terrain-tool", "Terrain", true)]
public sealed class TerrainToolOverlay : Overlay
{
    public override VisualElement CreatePanelContent();   // title row, shape segmented control (hidden unless drawing), keycap rows, cursor line, toast line
}
// TerrainSceneHud keeps SetActiveTool / SetCursorInfo / Clear and gains:
public static event Action Changed; public static string ActiveToolName { get; } public static IReadOnlyList<TerrainKeyHint> ActiveHints { get; }
public static string CursorInfo { get; } public static void ShowToast(string message, double seconds = 3.0);
```

### 6.2 Recipe, placers, shapes (FP-1)
```csharp
// Data/Terrain/TerrainFeatureRecipe.cs
public enum TerrainFeatureShapeKind { Curve, Straight, Box, Circle, Polygon }
[Flags] public enum TerrainFeatureShapeSupport { OpenPath = 1, ClosedLoop = 2, ClosedArea = 4 }
public enum TerrainFeatureCategory { Areas, LinesAndFences, GroundCover }
[Serializable] public struct TerrainPlaceable { public GameObject prefab; public float weight; public TerrainScatterTier tier; }

[CreateAssetMenu(menuName = "Stitch Punk/Terrain/Feature Recipe")]
public class TerrainFeatureRecipe : TerrainAreaRecipe
{
    public string displayName; public Texture2D thumbnail; public TerrainFeatureCategory category;
    public TerrainFeatureShapeSupport supportedShapes = TerrainFeatureShapeSupport.ClosedArea;
    public TerrainFeatureShapeKind defaultShape = TerrainFeatureShapeKind.Curve;
    [SerializeReference] public TerrainPlacer placer;
    public List<TerrainPlaceable> placeables;
}

// Data/Terrain/TerrainPlacers.cs  (settings only; planning lives in the Editor)
[Serializable] public abstract class TerrainPlacer { }
[Serializable] public sealed class TerrainFillPlacer : TerrainPlacer { public float densityPer100SquareMetres = 2f; public float minSpacingMetres = 2f; public Vector2 scaleRange = new Vector2(0.85f, 1.15f); }
public enum TerrainAlongPathMode { Points, Segments }
[Serializable] public sealed class TerrainAlongPathPlacer : TerrainPlacer { public TerrainAlongPathMode mode; public float spacingMetres = 3f; public float jitterMetres; public float sideOffsetMetres; public bool alignToPath = true; public GameObject segmentPrefab; }   // in Segments mode placeables are the posts
[Serializable] public sealed class TerrainRowsPlacer : TerrainPlacer { public float rowSpacingMetres = 0.6f; public float alongRowSpacingMetres = 0.5f; public float jitterMetres = 0.1f; public float rowDirectionDegrees = float.NaN; }   // NaN = longest outline edge

// MonoBehaviours/Terrain/TerrainPath.cs   [ExecuteAlways, RequireComponent(typeof(SplineContainer))]
// featureId, TerrainFeatureRecipe recipe, int seed, bool closedLoop, List<int> gapKnotIndices, WorldBounds(), List<Vector2> PolylineWorldXZ(float maxSegmentMetres); raises TerrainFeatureEvents

// Editor/Terrain/Features/TerrainFeaturePlacement.cs (pure)
public struct TerrainPlacementResult { public Vector3 positionXZ; public float yawDegrees; public float scale; public int placeableIndex; public bool isSegment; public Vector3 segmentEndXZ; }
public static class TerrainFeaturePlacement
{
    // Ownership: half-open tile rect, floor-based (WF rule). Same seed gives the same points whichever tile asks. y = 0; the caller samples height.
    public static List<TerrainPlacementResult> PlanAlongPath(IReadOnlyList<Vector2> polylineXZ, bool closedLoop, IReadOnlyList<int> gapSegmentIndices, TerrainAlongPathPlacer placer, int placeableCount, int seed, Rect tileRectXZ);
    public static List<TerrainPlacementResult> PlanRows(IReadOnlyList<Vector2> closedOutlineXZ, TerrainRowsPlacer placer, int placeableCount, int seed, Rect tileRectXZ);
    // Fill goes through the existing TerrainScatterPlanner + TerrainAreaRestrictedSampler (WF-D17); no new fill code.
}

// Editor/Terrain/Features/TerrainFeatureShapes.cs (pure)
public static class TerrainFeatureShapes
{
    public static List<Vector2> CircleKnots(Vector2 centreXZ, float radiusMetres);   // 8 knots
    public static List<Vector2> BoxKnots(Vector2 cornerA, Vector2 cornerB);         // = TerrainFeatureEditing.BoxOutline
    public static bool KnotsAreSharp(TerrainFeatureShapeKind kind);                  // Straight, Polygon, Box true
    public static bool IsAllowed(TerrainFeatureShapeKind kind, TerrainFeatureShapeSupport support, out bool closed);
}
```
`TerrainFeatureTool` gains `BeginDraw(TerrainFeatureRecipe recipe, TerrainFeatureShapeKind shape)` (replaces `BeginDrawOutline`/`BeginDragBox` callers; keep both as thin wrappers until FP-1's last wave), `SetDrawShape(TerrainFeatureShapeKind)`, and `static TerrainFeatureShapeKind DrawShape`. Circle: click centre, drag radius. Box: drag. Open paths: Enter finishes, double-click finishes. Closed: Enter or click the first knot. Alt+click a knot on a selected `TerrainPath` toggles a gap.

### 6.3 Trample runtime (FP-3, FP-4)
```csharp
// Components/Terrain/TrampleComponents.cs
public enum TrampleKind : byte { Brush, Crush }
public struct TrampleSource : IComponentData { public float radiusMetres; public TrampleKind kind; }
public struct TrampleCrushStroke : IBufferElementData { public float2 startXZ; public float2 endXZ; public half widthMetres; public float gameDay; }
public struct TrampleCrushLedger : IComponentData { }   // singleton tag; strokes in its DynamicBuffer<TrampleCrushStroke>; created in a non-Burst OnCreate (WF-D11 entry pattern)
public struct TrampleCrushLastPoint : IComponentData { public float2 lastXZ; public byte hasPoint; }
public struct TrampleCrushLedgerSnapshot : IComponentData, IPersist { public byte hasData; public FixedList4096Bytes<TrampleCrushStroke> strokes; }

// Authoring/Terrain/TrampleSourceAuthoring.cs  (radius, kind; added to unit prefabs as Brush 0.4 m and to the caravan as Crush with its body half width)

// Utils/Terrain/TrampleCrushRasterizer.cs (pure, Burst-friendly)
public static class TrampleCrushRasterizer
{
    public const float TexelMetres = 0.5f; public const int TexelsPerTile = 200;
    // Writes direction (0..255 = 0..360 degrees) and amount (255 = flat) into an RG8 tile grid. Amount fades to 0 linearly over regrowGameDays.
    public static void RasteriseTile(int2 tileCoordinate, NativeArray<TrampleCrushStroke> strokes, float nowGameDay, float regrowGameDays, NativeArray<byte2> gridInOut);
}
```
Systems (all `[UpdateInGroup(typeof(TerrainSystemGroup))]` unless noted, files in `Systems/TerrainSystemGroup/`):
- `TrampleCrushRecordSystem` (Burst): a Crush source more than 2 m from its `lastXZ` appends a stroke, merging into the last stroke when it is collinear within 10 degrees.
- `TrampleCrushTextureSystem` (managed): re-rasterises resident tiles that a new stroke touches, plus every resident tile once per game hour, packs them into `_TrampleCrushAtlas` and sets `_TrampleCrushAtlasGrid`.
- `TrampleBendFieldSystem` (managed, after `TerrainCardFacingSystem` ordering is checked): gathers Brush and Crush sources inside the 64 m window and runs the decay plus splat on `_TrampleBendField` (sets `_TrampleBendFieldOriginSize`).
- `TrampleCrushSaveSystem` / `TrampleCrushLoadSyncSystem`: copy the `HarvestedNodeSave/LoadSync` pair.
- `TrampleCrushContactSystem` (`CombatExecutionSystemGroup`, after `CaravanCrashSystem`): Crush sources break overlapping `Breakable`s (FP-D15).

Every new group placement goes in `Systems/SystemGroups.cs` only if a new group is needed; none is planned.

### 6.4 Placeable authoring (FP-2, FP-3)
```csharp
// Authoring/Terrain/TerrainCardSourceAuthoring.cs: [Serializable] struct TerrainCardSourceEntry { int atlasEntry; Vector2 sizeMetres; Vector3 localOffset; } List<...> cards; editor-only (Dressing prefabs never bake as entities)
// Authoring/Terrain/BreakableAuthoring.cs: GameObject intactPart, brokenPart; float respawnAfterGameDays = 7; nested Baker adds Breakable + BrokenPiece (baked disabled) + the key hash copied from HarvestableNodeAuthoring
// Components/Terrain/BreakableComponents.cs: Breakable, BrokenPiece (IEnableableComponent), BrokenPieceLedgerEntry, BrokenPieceLedgerSnapshot : IPersist
```

## 7. Spikes (orchestrator; numbers appended to `Tasks/Plans/Terrain_Spikes.md`)

- **FP-S0 Visibility events (before FP-0).** One `execute_code` probe: dock a throwaway `EditorWindow` beside the Inspector, switch tabs, and log `OnBecameVisible` and `OnBecameInvisible`, plus `hasFocus` and `docked`. Expected: invisible on switching to the Inspector, visible on switching back, and nothing on focus changes. If the events do not fire for docked tabs, fall back to polling `EditorWindow.HasOpenInstances` + a `rootVisualElement` `GeometryChangedEvent`/`panel` visibility check every 250 ms, and record which one works.
- **FP-S1 Trample card cost (before FP-3).** FP-D20 numbers. Also check that `_TrampleBendField` at 256 x 256 RG16F is supported on the Switch profile; fall back to RGBA8 with packed direction if it is not.
- **FP-S2 Runtime nav change (before FP-2 wave B).** Find how a runtime-placed blocker changes nav today (the deployed caravan workstation, or any `cost map version` bump that `DStarLiteSystem` reads through `lastSeenCostMapVersion`). A broken fence piece uses that path to drop its footprint cost to the tile's base cost. If no runtime path exists, the fallback is: broken pieces stay blocking in nav but are physically passable for the heavy unit that broke them, and the gap is logged as an open item.

## 8. Phases

Every wave ends with the gate, lint, the named fixtures and a commit.

**FP-0 Tool gating + overlay** (fixes the owner's two complaints; nothing else depends on content)

| ✓ | Task | Files | Notes and fixture |
|---|---|---|---|
| [ ] | FP-S0 **[orchestrator]** | probe only | section 7 |
| [ ] | FP-0-1 **[parallel-safe]** | `Window/TerrainEditorVisibility.cs`, `Tests/Terrain/TerrainEditorVisibilityTests.cs` | Fixture fails if: `Changed` fires twice for the same value; going hidden while drawing leaves `TerrainFeatureTool.IsDrawing` true. |
| [ ] | FP-0-2 **[parallel-safe]** | `Window/TerrainToolOverlay.cs`, `Window/TerrainSceneHud.cs` | 6.1; delete `DrawInSceneView` and its styles; overlay content rebuilds on `TerrainSceneHud.Changed`; keycaps as UI Toolkit labels with the `TerrainUi` kit and existing USS classes; new classes listed in the report for the orchestrator. Guide R01 to R22. |
| [ ] | FP-0-3a **[shared]** | `TerrainEditorWindow.cs`, `Features/TerrainFeatureTool.cs` | FP-D17: visibility events set `TerrainEditorVisibility`; tool early-outs; delete `DrawHud` and route its lines to the overlay toast and hints. |
| [ ] | FP-0-3b **[parallel-safe]** (one worker per two files) | the other `duringSceneGui` handlers | A Haiku verifier lists them first (`grep -rn "duringSceneGui +=" Assets/_Scripts/Editor/Terrain`); one early-out line each. |
| [ ] | FP-0-4 **[orchestrator]** | captures (Sonnet verifier audits the PNGs against R01 to R22) | Scene view before (the covered strip) and after (the overlay docked top-left, then with the Terrain Editor hidden behind the Inspector: no overlay, clicking the ground selects normally). |
| [ ] | ⏸ **FP-0 owner checkpoint** | | Owner: dock the Terrain Editor beside the Inspector, open Features, start drawing, check the overlay is readable and drag it where you like; click the Inspector tab, check the overlay is gone and clicks in the Scene view no longer add points; click back. |

**FP-1 Placement model + `+ New feature`**

| ✓ | Task | Files | Fixture must fail if |
|---|---|---|---|
| [ ] | FP-1-1 **[parallel-safe]** | `Data/Terrain/TerrainFeatureRecipe.cs`, `Data/Terrain/TerrainPlacers.cs` | gate only |
| [ ] | FP-1-2 **[parallel-safe]** | `MonoBehaviours/Terrain/TerrainPath.cs` | gate only; copy `TerrainArea`'s event raising and bounds |
| [ ] | FP-1-3 **[parallel-safe]** | `Features/TerrainFeaturePlacement.cs`, `Tests/Terrain/TerrainFeaturePlacementTests.cs` | a point on a shared tile border is owned by both or neither tile; a gap segment still gets a fence segment; Segments mode leaves a segment whose ends are not consecutive posts; Rows ignores `rowDirectionDegrees` (rotating it 90 degrees does not swap the row and column counts of a square); the same seed gives different points for different tile rects |
| [ ] | FP-1-4 **[parallel-safe]** | `Features/TerrainFeatureShapes.cs`, `Tests/Terrain/TerrainFeatureShapesTests.cs` | Circle knots are not all at `radiusMetres`; `IsAllowed(Straight, ClosedArea)` returns false or `closed` false (a straight-edged area is allowed and closed); `IsAllowed(Circle, OpenPath)` returns true |
| [ ] | FP-1-5 **[parallel-safe]** (wave B) | `Features/TerrainPathBaker.cs` | Mirrors the filler: per open tile, `PlanAlongPath`; Interactable placeables instantiated under the tile's `Features` child with `TerrainScatterMarker.sourceFeatureId` (lock rules WF-D6 apply); Segments mode stretches `segmentPrefab` along the chord (scale Z = chord length / prefab length, pitch from the two end heights); Dressing placeables become cards through `TerrainCardSource`. Wiring into `TerrainFeatureBaker` beside the area pass is FP-1-5b. |
| [ ] | FP-1-5b **[shared]** (wave C) | `Features/TerrainFeatureBaker.cs` | call `TerrainPathBaker` beside the area pass; `TerrainPath` changes mark tiles stale like areas |
| [ ] | FP-1-6 **[shared]** | `TerrainAreaFiller.cs` | accept `TerrainFeatureRecipe` with a Fill placer or a Rows placer; keep `TerrainForestRecipe` until FP-1-9 |
| [ ] | FP-1-7 **[shared]** (wave B, after FP-0-3a merged) | `TerrainFeatureTool.cs` | `BeginDraw`, shapes, open paths, gaps (Alt+click), keys 1 to 5 |
| [ ] | FP-1-8 **[parallel-safe]** | `Window/TerrainFeaturesPanel.cs` | `+ New feature` (R12) opens the categorised picker; Create recipe… (placer kind + folder, writes, selects and pings); the feature list shows `TerrainArea` and `TerrainPath` together; the selected card shows recipe, seed, density (Fill), spacing (Along path), row direction (Rows). Guide R01 to R22. |
| [ ] | FP-1-9 **[orchestrator]** (convert-menu code and the final `TerrainForestRecipe` deletion go to a Sonnet worker) | convert menu, assets, delete `TerrainForestRecipe` once nothing references it | Assets: the Forest recipe converted; placeholder recipes **Tree Row** (Along path, Points, Tree.prefab), **Wooden Fence** and **Stone Wall** (Along path, Segments, placeholder post + rail boxes). Captures of the Features tab and the picker. Bake one of each on tile (0,0), look in Play, delete, rebake clean. |
| [ ] | ⏸ **FP-1 owner checkpoint** | | Owner: Features, `+ New feature`, Wooden Fence, draw a straight fence, then a curved one, Alt+click a knot for a gate; draw a Tree Row; then Create recipe… from your own prefab and draw it. |

**FP-2 Fences block and break**

| ✓ | Task | Files | Fixture must fail if |
|---|---|---|---|
| [ ] | FP-2-1 **[parallel-safe]** | `Authoring/Terrain/BreakableAuthoring.cs`, `Components/Terrain/BreakableComponents.cs` | gate only; `dots-authoring-baker` rules |
| [ ] | FP-2-2 **[parallel-safe]** | `Systems/TerrainSystemGroup/BrokenPieceRecordSystem.cs`, `BrokenPieceRestoreSystem.cs`, `Tests/PlayMode/Terrain/BrokenPieceTests.cs` | copy the HarvestedNode pair; fails if a reloaded tile shows the piece intact before it is due, or it never comes back after `respawnAfterGameDays` |
| [ ] | FP-S2 **[orchestrator]** | probe | section 7 |
| [ ] | FP-2-3 **[parallel-safe]** (wave B, split: one worker per pair) | `TrampleCrushContactSystem.cs` + the nav change from FP-S2; `BrokenPiecePresentationSystem.cs` (copy `HarvestedNodeStumpPresentationSystem`) + the save hook **[shared]**. Orchestrator runs the PlayMode check. | PlayMode: a caravan driven through a fence breaks the touched segments only; a unit on foot is stopped by an intact segment. |
| [ ] | ⏸ **FP-2 owner checkpoint** | | Owner: drive the caravan through a fence, walk a minion to the gate gap, save, reload, check the broken pieces are still broken. |

**FP-3 Tall grass bends** (run FP-S1 first)

| ✓ | Task | Files | Fixture must fail if |
|---|---|---|---|
| [ ] | FP-3-1 **[parallel-safe]** | `Components/Terrain/TrampleComponents.cs`, `Authoring/Terrain/TrampleSourceAuthoring.cs` | gate only |
| [ ] | FP-3-2 **[parallel-safe]** | `Authoring/Terrain/TerrainCardSourceAuthoring.cs`, `Features/TerrainCardBatchMeshBuilder.cs` (expand card sources), its existing test file | a card source's local offset is not rotated by the placement yaw |
| [ ] | FP-3-3 **[parallel-safe]** workers (shader; `TrampleBendFieldSystem`) then **[orchestrator]** (prefab, recipe, adding `TrampleSourceAuthoring` to unit prefabs and the caravan via MCP, Play check) | `Painted/TrampleCard` shader (copy `Painted/TreeCard`; bend from `_TrampleBendField`, crush hook left at 0), `TrampleBendFieldSystem`, grass placeholder prefab + recipe (Ground cover, Fill, Dressing), `TrampleSourceAuthoring` on unit prefabs and the caravan | Play: a minion walking through grass leaves a bent wake that recovers in about 2 s; frame cost against FP-S1. Capture. |

**FP-4 Wheat is mowed flat**

| ✓ | Task | Files | Fixture must fail if |
|---|---|---|---|
| [ ] | FP-4-1 **[parallel-safe]** | `Utils/Terrain/TrampleCrushRasterizer.cs`, `Tests/Terrain/TrampleCrushRasterizerTests.cs` | a stroke crossing a tile border writes only one tile; direction is reversed for a stroke drawn end to start (it must follow travel direction); amount is not 0 at exactly `regrowGameDays` |
| [ ] | FP-4-2 **[parallel-safe]** | `Systems/TerrainSystemGroup/TrampleCrushRecordSystem.cs`, `Tests/PlayMode/Terrain/TrampleCrushRecordTests.cs` | a Brush source records a stroke; a Crush source moving 10 m in a straight line records more than one stroke |
| [ ] | FP-4-3 **[parallel-safe]** | `TrampleCrushSaveSystem.cs`, `TrampleCrushLoadSyncSystem.cs` | copy the HarvestedNode save pair; newest strokes win when over capacity |
| [ ] | FP-4-4 **[parallel-safe]** workers (`TrampleCrushTextureSystem`; shader crush branch) then **[orchestrator]** (assets, Play check) | `TrampleCrushTextureSystem`, crush branch of `Painted/TrampleCard` (FP-D13), wheat placeholder prefab + recipe (Areas, Rows, Dressing, crop ground layer) | Play: drive the caravan across a wheat field; the cards lie flat along the wheel path and read as a stripe; a minion walking through only bends them; advance the clock past regrowth and the field stands again; save and reload mid-regrowth. Captures at near and mid zoom. |
| [ ] | ⏸ **FP-4 owner checkpoint** | | Owner: draw a wheat field and a tall grass patch, walk minions through both, drive the caravan across both, judge the look of the flattened tracks. |

## 9. Shared files and other sessions

Shared files (tasks touching them are tagged **[shared]** and written by a Sonnet worker running alone on that file): `TerrainEditorWindow`, `TerrainFeatureTool`, `TerrainFeatureBaker`, `TerrainAreaFiller`, `TerrainCardBatchMeshBuilder`, `TerrainEditor.uss`, unit prefabs, the caravan prefab, the save hook. Tell any peer on the terrain editor before starting a phase that touches them.

## 10. Effect on the WF spec

- WF-5-1 and WF-5-2 (`TerrainLine`, `TerrainLineRecipe`, `TerrainLinePlanner`) are replaced by `TerrainPath` + the Along path placer. Do not build them.
- WF-5-3 to WF-5-6 (field ground mesh) still stand: they are the ground under the wheat and the far view.
- WF-6 farmland: borders become closed `TerrainPath`s with a hedge or fence recipe, and parcels become `TerrainArea`s with a wheat (Rows) or other crop recipe.

## 11. Verification (becomes `Tasks/Verification/verify-feature-placement.md` when the spec retires)

Headless: every fixture above; the gate and lint per wave; `StitchPunk.Tests` and `StitchPunk.Tests.PlayMode` in full once per phase merge. Owner only: the ⏸ checkpoints. On retiring: `git mv` to `Tasks/Verification/`, write the verify file, fix the `Plans/README.md` row, and add every trap met to `Memories/Code/Terrain.md`.

## Open decisions (defaults apply until the owner answers; none blocks FP-0)

- [ ] **FP-Q1** Wheat regrow time ← DECISION (default 4 game days).
- [ ] **FP-Q2** Broken fence pieces respawn ← DECISION (default rebuilt after 7 game days, like trees; alternative: never, until a minion repairs them in a later spec).
- [ ] **FP-Q3** Which units count as heavy (Crush) ← DECISION (default: the caravan only, plus any unit prefab the owner ticks; minions and citizens are Brush).
- [ ] **FP-Q4** Does tall grass do anything in gameplay (hide units, slow them) ← DECISION (default scenery only).
- [ ] **FP-Q5** Trample card caps per platform ← DECISION after FP-S1.
- [ ] **FP-Q6** Overlay default dock position ← DECISION at the FP-0 checkpoint (default top-left floating; Unity remembers where the owner moves it).

## 12. Log (append one line per wave, newest last)

- 2026-11-13 spec v1 from the owner's Features feedback and Q&A (FP-D1 to FP-D4). Root causes confirmed in code: HUD drawn by raw IMGUI under the overlays (`TerrainSceneHud.DrawInSceneView`, `TerrainFeatureTool.DrawHud`); tools cleared only in `TerrainEditorWindow.OnDisable`, which a docked tab switch never calls; `TerrainFeatureTool.OnSceneGui` is live whenever a `TerrainArea` is selected. Nothing built.
