---
status: SPEC v2 (2026-11-11), not built. Executed by an Opus orchestrator driving Sonnet `worker` and `verifier` subagents. Progress lives in the checkboxes and the log in section 14; read the log first after any context reset.
reference: Tasks/Claude/ExampleZoomOutFasterTravel.png
names verified against: main at d5c60204 (2026-11-11). If a name has drifted, grep, follow the code, and note the drift in the log.
---

# World Features: forests, farmland, villages and rail as shapes you draw

## 0. Context: why the world does not look like the picture yet

The reference picture reads as a place because it is built from **shapes with crisp edges**: forest masses, a patchwork of field parcels, lines of trees along roads and field borders, a few clustered buildings, a rail line with a crossing, a river with a bridge. The camera, tilt-shift, sun and far-view ground are already built and are not the gap.

What the terrain tools make today is **noise**: the generator writes fbm heights and a noise biome mask, and scatter follows that mask. Roads, rivers, lakes and crossings are the only shapes. `TerrainWorldRecipeBuilder` (merged 2026-11-11) already lays out mountains, roads, one river and locations from a seed, but it has no forests, fields, villages or rail to place.

The plan in one line: **everything that gives the land its meaning becomes a Feature (a spline or outline in the Scene view), every generator only ever creates Features, and one baker turns Features into per-tile layers.** Roads and rivers already work this way.

```
Base terrain (heights + biome noise)       exists: TerrainGenerationProfile, TerrainRegion
   + Features (vector, persistent scene)   roads, rivers, lakes, crossings exist; areas, lines, rail, stations NEW
   + Stamps (recipes that create Features)  NEW
   + Hand overrides (sculpt, paint, locked instances, anything the owner moved)
        |  bake per tile (debounced, open tiles, stale list): same pattern as TerrainRoadBaker
        v
Tile layers: height deltas, masks, draped meshes, props, card batches, spawn markers, nav
```

## 1. Owner answers (2026-11-11, do not re-ask)

- **WF-D1 Spawning:** both stamps and a whole-world sketch, **stamps first**. Whatever is spawned stays editable shapes.
- **WF-D2 Trees are billboards**, and the camera feature that does the billboarding must be respected (WF-D8). **Trees can be cut down and respawn after a period**; scattered objects must be able to be interactable.
- **WF-D3 The player can ride trains.** The riding gameplay gets its own spec (section 12); this spec builds track, stations and the rail graph.
- **WF-D4 Villages are buildings plus people markers**, and the owner goes in afterwards to add shops, move markers and make the place livelier. Everything a village stamp makes is an ordinary movable scene object, and a hand edit survives a regenerate.

## 2. Orchestrator decisions (final unless the owner overrides)

- **WF-D5** Features live in the persistent world scene (as `TerrainRoad` does), never in a tile scene. Their baked products live in tile scenes and tile data assets so they stream.
- **WF-D6 Generated until touched.** Generated objects carry `TerrainGeneratedMarker` (stamp children) or `TerrainScatterMarker` (props; it already has `locked`). Moving or editing one in the Editor sets `locked`; Regenerate keeps locked objects and plans around them. An object with no marker is hand-owned and never touched. Detach removes the markers from a stamp's children.
- **WF-D7** A stamp holds `recipe + seed + outline`; its children are plain Features and objects. Stamps never write pixels or heights. The world sketch only places stamps, areas and lines.
- **WF-D8 Tree cards are static upright quads.** `CameraWorldLean` leans the whole rendered world by camera pitch; a card that also pitches toward the camera leans twice (`MonoBehaviours.md`). Camera yaw is fixed, so a card is a quad facing the fixed camera yaw with no per-frame work. Cards cast shadows two-sided (the fixed sun travels south, toward the camera). WF-S1 proves this before any tree content is made.
- **WF-D9 Two vegetation tiers per scatter rule** (`TerrainScatterTier`). `Interactable` (default, today's behaviour): one prefab instance, baked as an entity; trees use `Prefabs/ResourceNodes/Tree.prefab` and `MinionGatherSystem`. `Dressing`: baked into one card-batch mesh per tile per atlas, no entities.
- **WF-D10** Interactable count per tile is capped by `TerrainPlatformProfile.maxInteractablePropsPerTile`. Over the cap the filler demotes trees farthest from the area edge to cards, and reports the count. No on-demand promotion now; `HarvestableNodeKey` leaves room for it.
- **WF-D11 Cut and respawn is a ledger keyed by position hash,** copying `TerrainSpawnLedgerEntry` and `TerrainSpawnSystem`. Entry pattern: singleton buffer created in a non-Burst `OnCreate`. Group: `TerrainSystemGroup` (folder `Systems/TerrainSystemGroup/`). The ledger is saved with the world clock. The far view ignores cuts.
- **WF-D12 Fields are draped meshes, not splat colours** (four ground layers cannot hold many crop tints and a row direction). WF-S2 confirms the far view shows them; fallback is a per-tile tint texture.
- **WF-D13** Forest beyond the loaded ring is a baked, thinned canopy card batch per 1 km strategic super-chunk.
- **WF-D14 Rail reuses the road pipeline:** a rail converts to a `TerrainRoadStamp` for mask and carve, uses a ribbon style for the visual and `TerrainRoadGraphBuilder` for the graph. New `TerrainSurface.Rail`: walkable on foot, blocks the caravan except inside a `LevelCrossing`.
- **WF-D15 Editor UI follows the hard rule in `CLAUDE.md`:** Scene view first, `EditorStyleGuide.md` R01 to R22, before/after captures with `TerrainEditorCapture.CaptureTabs`, judged by the orchestrator reading the PNGs. Synthetic events do not reach `duringSceneGui`, so mouse feel is always an owner checkpoint.
- **WF-D16 No location registry exists yet** (grep `LocationRegistry` finds nothing). Names are stored on a `TerrainLocationMarker` MonoBehaviour; a later registry reads those.
- **WF-D17 The scatter planner is not edited.** Area fill calls `TerrainScatterPlanner.Plan` through a wrapper sampler that reports "blocked" outside the area; tile-wide scatter treats area-owned texels as blocked so nothing is planted twice.
- **WF-D18 The world sketch extends the existing recipe.** New pure `TerrainWorldLandUsePlanner` fills a `landUse` field on `TerrainWorldRecipe`; `TerrainWorldRecipePreview` and `TerrainRandomWorldBuilder` gain a few lines each. No second generator.

## Skills Needed

`dots-authoring-baker` (harvestable node, rail graph authoring), `dots-system-scaffold` (ledger systems), `dots-test` (every pure class), `shader-edit` (`Painted/Card`, `Painted/Field`), `unity:ui-uitk` (Features panel).

## 3. Execution protocol

**Roles.** The orchestrator (Opus) owns: spikes, shaders, anything needing the Editor or Unity MCP, shared-file edits marked **[orchestrator]**, capture review, commits and merges. Every task marked **[parallel-safe]** goes to a Sonnet `worker`; a `verifier` checks any worker report the orchestrator doubts. Never `general-purpose`. Subagents never touch `mcp__UnityMCP__*`.

**Where.** Check `ListAgents` first. Another session works on the terrain editor on `main` and has uncommitted edits in `TerrainGeneratePanel.cs`, `TerrainTileAssets.cs` and `TerrainEditor.uss`: never stage or overwrite those. Build in a worktree on branch `spec/world-features` (memory `feedback_worktree_when_peer_busy`: the Library junction lets the gate run there). Merge to `main` at the end of each phase after telling the peer; do Editor passes on `main` after the merge.

**Waves.** A phase is one or more waves. Spawn every task of a wave in one message. Tasks code against the pinned surfaces in section 6, so a task never waits for another task in its wave. After the wave: read each diff, run `python Tools/devloop/devloop.py gate StitchPunk.Editor` (and the Data, Components, Systems, MonoBehaviours or Authoring assembly if touched) once, run `python Tools/devloop/devloop.py lint`, fix or respawn, then the Editor pass (refresh, console, only the fixtures the wave names), then tick the boxes, append one log line, commit with explicit paths.

**Worker brief template** (fill every slot; a brief that cannot is scoped wrong):
```
Task <id> from Assets/_Vault/Tasks/Plans/WorldFeatures_System.md. Read ONLY: section 6.<n> of that spec, and <file>:<line range> (grep the member, read 40 lines either side).
Create/edit exactly: <file 1>, <file 2 (the test)>. Touch nothing else. Write the .cs only; Unity makes the .meta.
Pinned surface (do not change signatures): <paste the code block>.
Behaviour: <paste the task's Behaviour cell>. Fixture must fail if: <paste the Must-fail cell>.
Rules: no `var`, no single-letter names, explicit types, verbose names, comments only for a why. Tests: namespace StitchPunk.Tests, [TestFixture] public sealed class, folder Assets/_Scripts/Tests/Terrain/.
Never `cd`; use absolute paths or `git -C`. No Unity MCP. Before your report run `python Tools/devloop/devloop.py gate <assembly>` and paste its last line. At turn 30 stop editing and write the report. Report: 30 lines or fewer: files, public members added, gate result, anything that did not match the spec.
```

**Rules that bite here.** A capped agent is never resumed; read its diff and spawn a fresh worker with the remainder. A rate-limited wave usually left its edits on disk: diff before respawning. Every new fixture must fail when the fix is reverted, or it is deleted. A recompile closes tile subscenes and clears Undo. Tests that create GameObjects use `EditorSceneManager.NewPreviewScene()`; check `git diff --stat Assets/Scenes` after a run. `⏸ owner checkpoint` ends the session with exact instructions for what to open, press and look at.

## 4. Read first (orchestrator, in order)

Root `CLAUDE.md`; `Assets/_Vault/Memories/Code/RULES.md`; `Assets/_Vault/Memories/Code/Terrain.md` in full (traps section especially); `Gotchas.md` entry on `CameraWorldLean` culling; `MonoBehaviours.md` line on `CameraWorldLean`; `Docs/AnimationToolkit/EditorStyleGuide.md` before WF-4; this spec. Then only the files a task names.

## 5. Existing surface this spec builds on (verified)

| Thing | Where | Members used |
|---|---|---|
| Feature pattern to copy | `MonoBehaviours/Terrain/TerrainRoad.cs` | `TerrainRoadEvents.RoadChanged`, `Raise`, `WorldBounds()`, `profile`, `priority` |
| Road bake | `Editor/Terrain/Roads/TerrainRoadBaker.cs` | `StaleTiles`, `BakeFinished`, `RebakeAllRoads(bool)`, `StampFromRoad(TerrainRoad)`, `BakeTile(TerrainTile, IReadOnlyList<TerrainRoadStamp>)` |
| Road rasteriser | `Editor/Terrain/Roads/TerrainRoadRasterizer.cs` | `TerrainRoadStamp`, `RasteriseRoadMask`, `ApplyHeightCarve`, `SmoothPointHeights`, `BoundsOf`; its texel-to-world mapping is THE mapping, copy it |
| Ribbon mesh | `Editor/Terrain/Roads/TerrainRoadRibbonMeshBuilder.cs`, `TerrainRoadRibbonBaker.cs` | `TerrainRoadRibbonInput`, `Build`, `BakeTileRibbons`; sub-asset rule (loose mesh assets do not bake into a subscene) |
| Compositor | `Editor/Terrain/TerrainCompositor.cs` | `CompositeTexels(baseMask, paintOverlay, surfaceOverlay, roadMask, resolution, texelRect, compositeInOut)`, `Composite(TerrainTile)`; order today: base, paint, water, road |
| Scatter | `Editor/Terrain/TerrainScatterPlanner.cs`, `TerrainScatterGenerator.cs`, `TerrainTileSampler.cs` | `ITerrainScatterSampler`, `TerrainScatterRule`, `TerrainScatterPlacement`, `TerrainScatterObstacle`, `Plan(rules, sampler, worldRectXZ, seed, obstacles)`, `RulesFrom(set)`, `GenerateWithSampler`, `ClearUnlocked`, `TerrainTileSampler(TerrainTile)` |
| Scatter data | `Data/Terrain/TerrainScatterSet.cs`, `MonoBehaviours/Terrain/TerrainScatterMarker.cs` | `TerrainScatterRuleData`, `TerrainScatterPrefab`, `PickPrefab`; marker `locked`, `ruleIndex`, `footprintRadiusMetres` |
| Tile data | `Data/Terrain/TerrainTileData.cs`, `MonoBehaviours/Terrain/TerrainTile.cs` | `TileSizeMetres` 100, `HeightSamplesPerSide` 101, the four height arrays, `HeightAt`, `SampleBilinear`, `HeightExcludingRoadAt`, `maskResolution`, `BumpRevision`; tile centre = coordinate x 100; ownership must be floor-based, never `CoordinateContaining` |
| Nav rules | `Editor/Terrain/Nav/TerrainNavCellRules.cs`, `Components/Terrain/TileNavCost.cs` | `TerrainNavCellInput`, `Classify`, `ApplyCrossing`, `TerrainSurface { Normal, Road, Shallow, Deep }` |
| Crossings | `MonoBehaviours/Terrain/TerrainCrossing.cs` | `TerrainCrossingKind { Bridge, Ford }`, `footprintMetres`, `ContainsXZ` |
| Spawn ledger to copy | `Components/Terrain/TerrainSpawnMarkerData.cs`, `Systems/TerrainSystemGroup/TerrainSpawnSystem.cs`, `Authoring/Terrain/TerrainSpawnMarkerAuthoring.cs` | `markerKey` position hash, `TerrainSpawnLedgerEntry`, `respawnAfterGameDays` |
| Harvest | `Components/Structures/ResourceNodeComponents.cs`, `Authoring/Buildings/ResourceNodeAuthoring.cs`, `Systems/MinionActionSelectionSystemGroup/MinionGatherSystem.cs` (around lines 236 to 262: `remaining -= taken`, `nodeEmptied`) | `ResourceNode { resourceType, remaining, harvestSeconds, loadPerTrip }` |
| Road graph | `Authoring/Terrain/TerrainRoadGraphAuthoring.cs`, `Components/Terrain/RoadGraphBlob.cs`, Utils `TerrainRoadGraphBuilder` | blob parameter is `ref RoadGraphBlob` (EA0009 on `in`) |
| Platform budgets | `Data/Terrain/TerrainPlatformProfile.cs` | `maxDecalsPlusPropsPerTile`, `maxRibbonVerticesPerTile`, `CreatePreset(tier)` |
| Editor window | `Editor/Terrain/Window/TerrainEditorWindow.cs` | `PanelCount` 9, `TabLabels`, `TabIcons`, `PanelIndexByTabPosition`, `GetOrCreatePanel` switch; panels derive `TerrainToolPanel` (`DisplayName`, `StatusText`, `KeyHints`, `OnShown`, `OnHidden`); kit `TerrainUi`; styles only in `TerrainEditor.uss` |
| Scene tool pattern | `Editor/Terrain/Roads/TerrainRoadTool.cs`, `TerrainRoadEditing.cs` | static tool with `SelectionChanged`; pure editing helpers with tests |
| World recipe | `Editor/Terrain/TerrainWorldRecipeBuilder.cs`, `TerrainWorldRecipePreview.cs`, `TerrainRandomWorldBuilder.cs` | `TerrainWorldRecipe { seed, tilesPerSide, worldHalfExtentMetres, mountains, campFlatArea, locations, roads, river }`, `Build(seed, tilesPerSide, profile)`, `TraceRiverDownhill`, `RouteRoad`, `Render`, `PopulateRandomWorld` |
| Strategic bake | `Editor/Terrain/Strategic/TerrainStrategicBaker.cs` | `BakeAll(profile, maxTexturePixels)`, menu `Stitch Punk/Terrain/Bake Strategic Layer` |

## 6. Pinned surfaces (new code; workers do not change these signatures)

New editor code goes in `Assets/_Scripts/Editor/Terrain/Features/`. All gameplay code is in the global namespace.

### 6.1 Area feature and markers
```csharp
// Data/Terrain/TerrainAreaRecipe.cs
public abstract class TerrainAreaRecipe : ScriptableObject
{
    public bool paintsGround = true;
    public TerrainSplatLayer groundLayer = TerrainSplatLayer.Forest;
}

// MonoBehaviours/Terrain/TerrainArea.cs   [ExecuteAlways, RequireComponent(typeof(SplineContainer))]
public static class TerrainFeatureEvents
{
    public static event Action<Component> FeatureChanged;
    public static void Raise(Component feature);
}
public class TerrainArea : MonoBehaviour
{
    public string featureId;              // GUID string, assigned when empty in Reset/OnValidate
    public TerrainAreaRecipe recipe;
    public int seed;
    public int priority;
    public float edgeNoiseMetres = 3f;
    public int cropIndex = -1;            // fields only; -1 = chosen from seed
    public float rowDirectionDegrees = -1f; // fields only; negative = along the longest edge
    public Bounds WorldBounds();
    public List<Vector2> OutlineWorldXZ(float maxSegmentMetres);  // closed spline sampled; SplineContainer.EvaluatePosition is already WORLD space
}

// MonoBehaviours/Terrain/TerrainGeneratedMarker.cs
public class TerrainGeneratedMarker : MonoBehaviour
{
    public string sourceFeatureId;
    public bool locked;
}
// TerrainScatterMarker gains: public string sourceFeatureId;
```

### 6.2 Area rasteriser and tile data
```csharp
// Editor/Terrain/Features/TerrainAreaRasterizer.cs  (pure)
public struct TerrainAreaStamp
{
    public Vector2[] outlineXZ;
    public float edgeNoiseMetres;
    public int seed;
    public int priority;
    public byte areaIndex;        // 1..255, index into the tile's areaEntries + 1; 0 means no area
    public bool paintsGround;
    public TerrainSplatLayer groundLayer;
}
public static class TerrainAreaRasterizer
{
    public const byte NoArea = 0;
    public const float EdgeDistanceRangeMetres = 32f;
    // ownerAreaIndexOut and edgeDistanceOut are resolution * resolution bytes. Higher priority wins a texel; a tie goes to the later stamp.
    // edgeDistanceOut: 0..255 maps 0..EdgeDistanceRangeMetres inward from the owning area's edge.
    public static void RasteriseAreaMask(IReadOnlyList<TerrainAreaStamp> areas, Vector2Int tileCoordinate, int resolution, byte[] ownerAreaIndexOut, byte[] edgeDistanceOut);
    public static float SignedDistanceToEdgeMetres(in TerrainAreaStamp area, float worldX, float worldZ);  // positive inside, edge noise applied
    public static Bounds BoundsOf(in TerrainAreaStamp area);
}

// Data/Terrain/TerrainTileData.cs  (additions)
[Serializable] public struct TerrainTileAreaEntry { public string featureId; public TerrainAreaRecipe recipe; public int seed; public int cropIndex; public float rowDirectionDegrees; }
public List<TerrainTileAreaEntry> areaEntries;   // areaIndex - 1 indexes this list
public Texture2D areaMask;                        // Tile_x_z_AreaMask.png: R = areaIndex, G = edge distance byte, A = 255 where the area paints ground
public float[] areaHeightDelta;                   // building pads; part of the HeightAt sum (WF-7)
```
Compositor overload: `CompositeTexels(baseMask, areaMask, areaLayerColourByIndex, paintOverlay, surfaceOverlay, roadMask, resolution, texelRect, compositeInOut)`; order base, **area**, paint, water, road. `areaLayerColourByIndex` is a 256-entry `Color32[]` built with the same layer-to-colour helper the road baker uses when it writes `RoadMask.png`. The old overloads forward with `null`.

### 6.3 Feature baker and area fill
```csharp
// Editor/Terrain/Features/TerrainFeatureBaker.cs   [InitializeOnLoad], debounce 0.25 s like TerrainRoadBaker
public static class TerrainFeatureBaker
{
    public static IReadOnlyCollection<Vector2Int> StaleTiles { get; }
    public static event Action BakeFinished;
    public static void RebakeAllFeatures(bool openMissingTiles);
    public static TerrainAreaStamp StampFromArea(TerrainArea area, byte areaIndex);
    public static void BakeTile(TerrainTile tile);   // areas -> entries + AreaMask.png -> Composite -> TerrainAreaFiller.FillTile -> field/line bakers -> save tile scene
}

// Editor/Terrain/Features/TerrainAreaRestrictedSampler.cs
public sealed class TerrainAreaRestrictedSampler : ITerrainScatterSampler
{
    public TerrainAreaRestrictedSampler(ITerrainScatterSampler inner, TerrainAreaStamp area, float clearingNoiseScaleMetres, float clearingThreshold01);
    public float EdgeDistanceMetresAt(float worldX, float worldZ);
}

// Editor/Terrain/Features/TerrainAreaFiller.cs
public struct TerrainAreaFillResult { public int interactableCount; public int cardCount; public int demotedCount; }
public static class TerrainAreaFiller
{
    public const string CardsRootName = "Cards";
    public const string CardMeshPrefix = "Cards_";
    public static TerrainAreaFillResult FillTile(TerrainTile tile, IReadOnlyList<TerrainAreaStamp> areas);
    // Pure half, tested: which placements become cards. Sorted by edge distance; beyond interiorDemotionMetres are cards; then farthest-first until the cap holds.
    public static void SplitTiers(IReadOnlyList<TerrainScatterPlacement> placements, IReadOnlyList<float> edgeDistanceMetres, IReadOnlyList<TerrainScatterTier> ruleTiers, float interiorDemotionMetres, int interactableCapRemaining, List<int> interactableIndicesOut, List<int> cardIndicesOut, out int demotedCount);
}
```

### 6.4 Forest, cards, platform
```csharp
// Data/Terrain/TerrainScatterSet.cs (additions)
public enum TerrainScatterTier { Interactable, Dressing }
// TerrainScatterRuleData gains: public TerrainScatterTier tier; public int cardAtlasEntry; public Vector2 cardSizeMetres;

// Data/Terrain/TerrainForestRecipe.cs
[CreateAssetMenu(menuName = "Stitch Punk/Terrain/Forest Recipe")]
public class TerrainForestRecipe : TerrainAreaRecipe
{
    public TerrainScatterSet scatterSet;
    public TerrainCardAtlas cardAtlas;
    public float interiorDemotionMetres = 12f;
    public float clearingNoiseScaleMetres = 60f;
    public float clearingThreshold01 = 0f;      // 0 = no clearings
}

// Data/Terrain/TerrainCardAtlas.cs
[CreateAssetMenu(menuName = "Stitch Punk/Terrain/Card Atlas")]
public class TerrainCardAtlas : ScriptableObject { public Material material; public List<Rect> spriteUvRects; }

// Editor/Terrain/Features/TerrainCardBatchMeshBuilder.cs (pure)
public struct TerrainCard { public Vector3 basePosition; public Vector2 sizeMetres; public Rect uvRect; public float tint01; }
public static class TerrainCardBatchMeshBuilder
{
    public const int MaxCardsPerMesh = 16000;
    // Quads stand on basePosition, upright (world up), facing cameraYawDegrees. Vertex colour r = tint01. uv2.y = 0 at the base, 1 at the top (for wind and ground blend).
    public static Mesh Build(IReadOnlyList<TerrainCard> cards, Vector3 meshOriginWorld, float cameraYawDegrees, out int vertexCount);
}

// Data/Terrain/TerrainPlatformProfile.cs gains: public int maxInteractablePropsPerTile = 600;  presets PC 600, Steam Deck 300, Switch 150 (WF-Q1 may change them after WF-S1)
```

### 6.5 Cut and respawn
```csharp
// Components/Terrain/HarvestedNodeComponents.cs
public struct HarvestableNodeKey : IComponentData { public uint nodeKey; public int fullAmount; public float respawnAfterGameDays; }
public struct HarvestedNodeStump : IComponentData, IEnableableComponent { }      // baked disabled
public struct HarvestedNodeLedger : IComponentData { }                            // tag on the singleton entity
public struct HarvestedNodeLedgerEntry : IBufferElementData { public uint nodeKey; public double harvestedAtWorldSeconds; }

// Utils/Terrain/HarvestedNodeMath.cs (pure, Burst-safe)
public static class HarvestedNodeMath
{
    public static bool IsRespawnDue(double harvestedAtWorldSeconds, double nowWorldSeconds, float respawnAfterGameDays, double worldSecondsPerGameDay);
    public static int IndexOfKey(in DynamicBuffer<HarvestedNodeLedgerEntry> ledger, uint nodeKey);  // -1 when absent
}
```
Systems (both `[UpdateInGroup(typeof(TerrainSystemGroup))]`, jobs scheduled, never `.Run()`): `HarvestedNodeRecordSystem` (a keyed node with `remaining <= 0` and stump disabled: enable stump, append entry) and `HarvestedNodeRestoreSystem` (1 Hz: a non-stump node whose key is in the ledger and not due becomes a stump with `remaining` 0, which covers a tile reload; a stump whose entry is due gets `remaining = fullAmount`, stump disabled, entry removed). Authoring `Authoring/Terrain/HarvestableNodeAuthoring.cs` computes `nodeKey` exactly as `TerrainSpawnMarkerAuthoring` computes `markerKey`.

### 6.6 Scene-view authoring
```csharp
// Editor/Terrain/Features/TerrainFeatureEditing.cs (pure)
public static class TerrainFeatureEditing
{
    public static List<Vector2> BoxOutline(Vector2 cornerA, Vector2 cornerB);
    public static bool IsSelfIntersecting(IReadOnlyList<Vector2> closedOutline);
    public static int NearestSegmentIndex(IReadOnlyList<Vector2> closedOutline, Vector2 point, out Vector2 closestPoint);
    public static float AreaSquareMetres(IReadOnlyList<Vector2> closedOutline);
    public static bool ContainsPoint(IReadOnlyList<Vector2> closedOutline, Vector2 point);
}

// Editor/Terrain/Features/TerrainFeatureTool.cs   (static, modelled on TerrainRoadTool; same input and drawing calls it uses)
public static class TerrainFeatureTool
{
    public static event Action SelectionChanged;
    public static Component SelectedFeature { get; }
    public static bool IsDrawing { get; }
    public static void BeginDrawOutline(TerrainAreaRecipe recipe);   // click to add points, Enter or click the first point closes, Esc cancels
    public static void BeginDragBox(ScriptableObject recipe);        // TerrainAreaRecipe or TerrainStampRecipe
    public static void CancelDrawing();
    public static void FrameSelected();
    public static void DeleteSelectedKnot();
}
```
```csharp
// Editor/Terrain/Features/TerrainFeaturePreview.cs (static; pinned 2026-11-12 so WF-4-2 and WF-4-3 can be built in one wave)
public static class TerrainFeaturePreview
{
    public const int MaxDots = 2000;
    public static void SetOutline(IReadOnlyList<Vector2> outlineXZ, TerrainAreaRecipe recipe, int seed);   // throttled: re-plans at most every 100 ms
    public static void Clear();
    public static void Draw();   // call from duringSceneGui on Repaint; draws fill + dots for the last outline
}
```
`TerrainFeaturePreview` draws the outline fill and the planned placement dots from an un-baked `Plan` call while drawing and while a knot is dragged, so the owner sees the result before the bake. `TerrainFeaturesPanel : TerrainToolPanel` (`DisplayName` "Features"): recipe grid with thumbnails (click arms draw-outline, the box icon arms drag-box), list of features in the scene, selected-feature card (recipe, seed with reroll, Regenerate, Detach, locked count, stale-tile line).

### 6.7 Lines and fields
```csharp
// Data/Terrain/TerrainLineRecipe.cs
[CreateAssetMenu(menuName = "Stitch Punk/Terrain/Line Recipe")]
public class TerrainLineRecipe : ScriptableObject
{
    public List<TerrainScatterPrefab> prefabs;
    public TerrainScatterTier tier = TerrainScatterTier.Dressing;
    public TerrainCardAtlas cardAtlas; public int cardAtlasEntry; public Vector2 cardSizeMetres = new Vector2(3f, 5f);
    public float spacingMetres = 4f; public float jitterMetres = 0.8f; public Vector2 scaleRange = new Vector2(0.85f, 1.15f);
}
// MonoBehaviours/Terrain/TerrainLine.cs: featureId, TerrainLineRecipe recipe, int seed, WorldBounds(), List<Vector2> PolylineWorldXZ(float maxSegmentMetres); raises TerrainFeatureEvents

// Editor/Terrain/Features/TerrainLinePlanner.cs (pure)
public static class TerrainLinePlanner
{
    // Placement positions carry y = 0; the caller samples height. Ownership is the half-open tile rect, floor-based. Same seed gives the same points regardless of which tile asks.
    public static List<TerrainScatterPlacement> Plan(IReadOnlyList<Vector2> polylineXZ, bool closedLoop, float spacingMetres, float jitterMetres, Vector2 scaleRange, int seed, Rect tileRectXZ);
}

// Data/Terrain/TerrainFieldRecipe.cs
[Serializable] public struct TerrainCropStyle { public string label; public Color tint; public float rowSpacingMetres; public float rowContrast01; }
[CreateAssetMenu(menuName = "Stitch Punk/Terrain/Field Recipe")]
public class TerrainFieldRecipe : TerrainAreaRecipe   // paintsGround false by default
{
    public List<TerrainCropStyle> crops; public Material fieldMaterial; public TerrainLineRecipe borderLine; public float fallowChance01 = 0.1f;
}

// Editor/Terrain/Features/TerrainFieldMeshBuilder.cs (pure)
public struct TerrainFieldMeshInput
{
    public IReadOnlyList<Vector2> outlineXZ; public Rect tileRectXZ; public Func<float, float, float> groundHeightAt;
    public float gridStepMetres; public float liftMetres; public Color tint; public float rowDirectionDegrees; public float rowSpacingMetres;
}
public static class TerrainFieldMeshBuilder
{
    public const int MaxVerticesPerField = 6000;
    // Grid cells whose centre is inside the outline and inside the half-open tile rect. Vertex colour = tint. uv0 = (metres along the rows, metres across the rows / rowSpacing). uv2.x = metres to the outline edge (the shader fades the last metre).
    public static Mesh Build(in TerrainFieldMeshInput input, Vector3 meshOriginWorld, out int vertexCount);
}
```
`TerrainFieldBaker.BakeTileFields(TerrainTile)` mirrors `TerrainRoadRibbonBaker`: meshes are sub-assets `Field_<n>` of the tile data under a `Fields` child.

### 6.8 Stamps
```csharp
// Data/Terrain/TerrainStampRecipe.cs
public abstract class TerrainStampRecipe : ScriptableObject { }

// Data/Terrain/TerrainFarmlandRecipe.cs
[CreateAssetMenu(menuName = "Stitch Punk/Terrain/Farmland Recipe")]
public class TerrainFarmlandRecipe : TerrainStampRecipe
{
    public Vector2 parcelAreaRangeSquareMetres = new Vector2(1500f, 6000f);
    public TerrainFieldRecipe fieldRecipe; public float laneGapMetres = 2f;
    public List<GameObject> farmhousePrefabs; public TerrainRoadProfile trackProfile;
}

// MonoBehaviours/Terrain/TerrainStamp.cs: featureId, TerrainStampRecipe recipe, int seed, WorldBounds(), OutlineWorldXZ(float)

// Editor/Terrain/Features/TerrainParcelSubdivider.cs (pure)
public struct TerrainCutLine { public Vector2[] pointsXZ; public float halfWidthMetres; }
public static class TerrainParcelSubdivider
{
    // Recursive split across the longest axis of the oriented bounding box with seeded jitter, until every parcel is inside parcelAreaRange. Cut lines (roads, rivers, rail) split first and leave a gap of their half width. Each parcel is inset by laneGapMetres / 2.
    public static List<List<Vector2>> Subdivide(IReadOnlyList<Vector2> outlineXZ, IReadOnlyList<TerrainCutLine> cutLines, Vector2 parcelAreaRangeSquareMetres, float laneGapMetres, int seed);
    public static bool TrySplitPolygon(IReadOnlyList<Vector2> polygon, Vector2 linePoint, Vector2 lineDirection, List<Vector2> leftOut, List<Vector2> rightOut);
}

// Editor/Terrain/Features/TerrainStampExecutor.cs
public static class TerrainStampExecutor
{
    public static void Regenerate(TerrainStamp stamp);   // destroys unlocked children whose marker names this stamp, then dispatches on recipe type
    public static void Detach(TerrainStamp stamp);       // removes the markers, destroys the stamp component
    public static GameObject CreateChild(TerrainStamp stamp, string childName);   // adds TerrainGeneratedMarker
}
// Editor/Terrain/Features/TerrainFarmlandStampGenerator.cs: public static void Generate(TerrainStamp stamp, TerrainFarmlandRecipe recipe)
// Editor/Terrain/Features/TerrainVillageStampGenerator.cs:  public static void Generate(TerrainStamp stamp, TerrainVillageRecipe recipe)
// Editor/Terrain/Features/TerrainGeneratedLockWatcher.cs [InitializeOnLoad]: Undo.postprocessModifications sets locked on TerrainGeneratedMarker / TerrainScatterMarker;
//   public static IDisposable SuppressLocking();  generators wrap their writes in it
```

### 6.9 Village
```csharp
// Data/Terrain/TerrainVillageRecipe.cs
[Serializable] public struct TerrainVillageBuilding { public GameObject prefab; public Vector2 lotSizeMetres; public float weight; public bool onlyOnce; }
[Serializable] public struct TerrainVillageSpawn { public UnitType unitType; public float perBuilding; public float radiusMetres; }
[CreateAssetMenu(menuName = "Stitch Punk/Terrain/Village Recipe")]
public class TerrainVillageRecipe : TerrainStampRecipe
{
    public List<TerrainVillageBuilding> buildings; public TerrainRoadProfile streetProfile;
    public float sideStreetSpacingMetres = 40f; public float lotSetbackMetres = 3f; public float padBlendMetres = 4f;
    public List<TerrainVillageSpawn> spawns; public List<string> names;
}

// Editor/Terrain/Features/TerrainVillageLayout.cs (pure)
public struct TerrainVillageLot { public Vector2 centreXZ; public float yawDegrees; public Vector2 sizeMetres; public int buildingIndex; }
public sealed class TerrainVillagePlan { public List<List<Vector2>> streets = new List<List<Vector2>>(); public List<TerrainVillageLot> lots = new List<TerrainVillageLot>(); }
public static class TerrainVillageLayout
{
    // Main street from entryPointXZ through the outline centroid to the far edge; side streets perpendicular every sideStreetSpacingMetres, clipped to the outline; lots on both sides of every street, set back, never overlapping each other or a street, facing the street.
    public static TerrainVillagePlan Plan(IReadOnlyList<Vector2> outlineXZ, Vector2 entryPointXZ, IReadOnlyList<Vector2> lotSizesMetres, IReadOnlyList<float> lotWeights, float sideStreetSpacingMetres, float lotSetbackMetres, int seed);
}

// Editor/Terrain/Features/TerrainPadRasterizer.cs (pure)
public struct TerrainPadStamp { public Vector2 centreXZ; public float yawDegrees; public Vector2 sizeMetres; public float blendMetres; public float padHeight; }
public static class TerrainPadRasterizer
{
    // Delta that moves the ground to padHeight inside the rectangle and blends out over blendMetres. Computed from WORLD coordinates only, so the shared edge samples of two tiles get the same value.
    public static void ApplyPads(IReadOnlyList<TerrainPadStamp> pads, Vector2Int tileCoordinate, float[] heightsWithoutAreaDelta, float[] areaDeltaOut);
}

// MonoBehaviours/Terrain/TerrainLocationMarker.cs: public string displayName; public float radiusMetres;
```

### 6.10 Rail
```csharp
// Data/Terrain/TerrainRailProfile.cs
[CreateAssetMenu(menuName = "Stitch Punk/Terrain/Rail Profile")]
public class TerrainRailProfile : ScriptableObject
{
    public float bedWidthMetres = 5f; public float shoulderMetres = 2f; public float carveShoulderMetres = 10f;
    public float heightSmoothingMetres = 80f; public AnimationCurve carveBlendCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
    public float maxGradePercent = 2.5f; public float minCurveRadiusMetres = 80f;
    public TerrainSplatLayer bedLayer = TerrainSplatLayer.Stone; public TerrainRoadRibbonStyle ribbonStyle;
    public float viaductWhenDropExceedsMetres = 4f; public float viaductPierSpacingMetres = 12f; public GameObject viaductPierPrefab;
}
// MonoBehaviours/Terrain/TerrainRail.cs: featureId, TerrainRailProfile profile, int priority, WorldBounds(); raises TerrainFeatureEvents
// MonoBehaviours/Terrain/TerrainStation.cs: string displayName; TerrainRail rail; float platformLengthMetres = 30f; GameObject platformPrefab;

// Editor/Terrain/Features/TerrainRailConstraints.cs (pure)
public enum TerrainRailViolationKind { TooSteep, TooTight }
public struct TerrainRailViolation { public int segmentIndex; public TerrainRailViolationKind kind; public float measuredValue; }
public static class TerrainRailConstraints
{
    public static List<TerrainRailViolation> Check(IReadOnlyList<Vector3> points, float maxGradePercent, float minCurveRadiusMetres);
    public static float CurveRadiusMetres(Vector3 previous, Vector3 current, Vector3 next);   // circumradius in XZ; infinity when straight
}

// Editor/Terrain/Features/TerrainRailViaductPlanner.cs (pure)
public struct TerrainViaductSpan { public float startArcMetres; public float endArcMetres; public List<Vector3> pierBasePositions; }
public static class TerrainRailViaductPlanner
{
    public static List<TerrainViaductSpan> Plan(IReadOnlyList<Vector3> smoothedDeckPoints, Func<float, float, float> groundHeightAt, float dropThresholdMetres, float pierSpacingMetres);
}

// Additions to existing types (append only, so serialized values keep their meaning):
// TerrainSurface.Rail = 4;  TerrainCrossingKind.LevelCrossing (after Ford);  TerrainNavCellInput.onRail (bool)
// TerrainNavCellRules.Classify: onRail and not water -> surface Rail, cost = groundCost (people walk across). ApplyCrossing for LevelCrossing -> surface Road.
// TerrainTileData.railMask (Texture2D, Tile_x_z_RailMask.png, A = coverage), written by the rail bake, read by TerrainNavBaker.
// Authoring/Terrain/TerrainRailGraphAuthoring.cs -> RailGraphSingleton { BlobAssetReference<RoadGraphBlob> graph }, a copy of TerrainRoadGraphAuthoring over TerrainRail.
```

### 6.11 Far forest and world sketch
```csharp
// Data/Terrain/TerrainTileCanopyData.cs : ScriptableObject, asset Tile_x_z_Canopy.asset beside the tile data, NOT referenced by the tile scene (editor-only input to the strategic bake)
public class TerrainTileCanopyData : ScriptableObject { public Vector3[] treeBasePositions; public float[] treeHeightsMetres; public int cardAtlasEntry; }

// Editor/Terrain/Strategic/TerrainStrategicCanopyBuilder.cs (pure)
public static class TerrainStrategicCanopyBuilder
{
    // One clump card per clumpCellMetres cell that holds at least one tree; card size grows with the count up to maxClumpSizeMetres.
    public static List<TerrainCard> Thin(IReadOnlyList<Vector3> treeBasePositions, IReadOnlyList<float> treeHeightsMetres, float clumpCellMetres, float maxClumpSizeMetres, Rect uvRect);
}

// Editor/Terrain/TerrainWorldLandUsePlanner.cs (pure)
public sealed class TerrainWorldLandUse
{
    public List<Vector2> villageCentres = new List<Vector2>(); public List<float> villageRadiiMetres = new List<float>();
    public List<List<Vector2>> farmlandOutlines = new List<List<Vector2>>();
    public List<List<Vector2>> forestOutlines = new List<List<Vector2>>();
    public List<List<Vector3>> railLines = new List<List<Vector3>>();
    public List<Vector2> stationPoints = new List<Vector2>();
}
public static class TerrainWorldLandUsePlanner
{
    public static TerrainWorldLandUse Plan(TerrainWorldRecipe recipe, TerrainGenerationProfile profile);
}
// TerrainWorldRecipe gains: public TerrainWorldLandUse landUse;
```
Land-use rules: villages sit on `recipe.locations` whose ground slope is under 6 degrees (radius 60 to 120 m by seed); farmland is a ragged ring around each village on ground under 8 degrees, clipped away from water, mountains and other villages; forest outlines are the contours of a low-frequency noise above a threshold on ground not already used, each at least 1 hectare, simplified to under 40 points; one rail line links the two villages farthest apart using `RouteRoad`'s router with the slope cost raised until `TerrainRailConstraints.Check` passes or three tries are spent (then it is dropped and the log says so); a station at each end.

## 7. Spikes (orchestrator, before the phase that needs them; numbers appended to `Tasks/Plans/Terrain_Spikes.md`)

- [ ] **WF-S1 Card look and cost** (before WF-2). In `Assets/Scenes/TerrainWorld.unity`, one tile, 1,000 upright alpha-clipped quads as (a) one mesh and (b) 1,000 subscene entities. Capture at `StrategicZoomState.Mix01` 0 and 1 with `CameraWorldLean` on (reproduction recipe: Terrain.md, "Zoom dark filter" entry). Pass: trees stand straight at both zooms, shadows present and not culled, frame cost recorded for both. Output: the camera yaw constant for `TerrainCardBatchMeshBuilder`, and WF-Q1's numbers.
- [ ] **WF-S2 Field in the far view** (before WF-5). One draped parcel mesh on a test tile; check whether the strategic stand-in shows it and whether road ribbons show today. Output: draped mesh stays (the strategic bake must then include field and ribbon meshes) or the tint-texture fallback.
- [ ] **WF-S3 Far forest cost** (before WF-9). One fully forested 1 km super-chunk as thinned cards; triangles, draw calls, frame time against `maxStrategicLayerMegabytes`. Output: `clumpCellMetres` per platform tier.
- [ ] **WF-S4 Parcel look** (inside WF-6, after the subdivider exists). Dump a PNG sheet of 20 seeds on three irregular outlines with a road through them to `Tasks/Claude/ParcelSheet.png`. ⏸ owner picks (WF-Q5).

## 8. Phase WF-1: feature core (a drawn outline paints forest ground)

Wave A, all **[parallel-safe]**:

| ✓ | Task | Files (edit only these) | Read | Behaviour | Fixture must fail if |
|---|---|---|---|---|---|
| [ ] | WF-1-1 | `Data/Terrain/TerrainAreaRecipe.cs`, `MonoBehaviours/Terrain/TerrainArea.cs` | 6.1; `TerrainRoad.cs` whole file (171 lines) | Copy `TerrainRoad`'s change detection (spline edit, move, validate, disable) raising `TerrainFeatureEvents`. `OutlineWorldXZ` samples the closed spline so no segment exceeds the argument. | no fixture (MonoBehaviour shell); gate only |
| [ ] | WF-1-2 | `Features/TerrainAreaRasterizer.cs`, `Tests/Terrain/TerrainAreaRasterizerTests.cs` | 6.2; `TerrainRoadRasterizer.cs` lines 20 to 160 for the texel-to-world mapping and the edge-noise function | Point-in-polygon per texel with the outline displaced by seeded edge noise; edge distance byte; priority rule. | the priority comparison is flipped; the edge noise ignores the seed; a square area straddling two tiles leaves a gap or overlap at the shared border |
| [ ] | WF-1-3 | `MonoBehaviours/Terrain/TerrainGeneratedMarker.cs`, `Features/TerrainGeneratedLockWatcher.cs` | 6.1, 6.8; `TerrainScatterMarker.cs` | Lock on any Undo-recorded transform or property change to a marked object; `SuppressLocking` nests. Also add `sourceFeatureId` to `TerrainScatterMarker` (third file, one line: allowed). | no fixture (Undo callback); orchestrator checks by hand in the Editor pass |
| [ ] | WF-1-4 | `Editor/Terrain/TerrainCompositor.cs`, `Tests/Terrain/TerrainCompositorTests.cs` | 6.2; `TerrainCompositor.cs` lines 8 to 60 | Add the nine-argument overload; old overloads forward with `null`. | area is applied after paint instead of before; an area texel with A = 0 still paints |
| [ ] | WF-1-5 | `Data/Terrain/TerrainTileData.cs` | 6.2; the file's first 60 lines | Add `areaEntries`, `areaMask`, `areaHeightDelta` (allocated in `EnsureHeightArrays`, included in `HeightAt`, `SampleBilinear` and both `Excluding` variants). | existing `TerrainHeightBrushTests` and `TerrainHeightSamplerTests` still pass; add one test in `TerrainHeightSamplerTests` that a non-zero `areaHeightDelta` shows in `SampleBilinear` |

Wave B (after A is gated):

| ✓ | Task | Files | Read | Behaviour | Fixture |
|---|---|---|---|---|---|
| [ ] | WF-1-6 **[parallel-safe]** | `Features/TerrainFeatureBaker.cs` | 6.3; `TerrainRoadBaker.cs` lines 11 to 130 and `BakeTile` | Subscribe to `TerrainFeatureEvents`, debounce 0.25 s, collect areas overlapping each OPEN tile (floor-based tile rect), assign `areaIndex`, write `areaEntries` and `Tile_x_z_AreaMask.png` (uncompressed RGBA32, readable, point filter; path helper beside the road mask's in `TerrainTileAssets`, which has uncommitted peer edits: add the helper inside `TerrainFeatureBaker` instead), then `TerrainCompositor.Composite(tile)`, rebuild the mesh if it returns true, save the tile scene. Unopened tiles go to `StaleTiles`. | covered by WF-1-8 |
| [ ] | WF-1-7 **[orchestrator]** | `TerrainCompositor.Composite`, `TerrainTileSampler` | | `Composite` loads `areaMask` and builds `areaLayerColourByIndex`; `TerrainTileSampler.TryGetBiome` returns false on an area-owned texel unless constructed with `includeAreaTexels: true` (WF-D17). | extend `TerrainScattererTests`: tile-wide scatter plants nothing inside an area |
| [ ] | WF-1-8 **[orchestrator]** Editor pass | | | Refresh, console clean, run `TerrainAreaRasterizerTests`, `TerrainCompositorTests`, `TerrainHeightSamplerTests`, `TerrainScattererTests`. In `TerrainWorld.unity` create a `TerrainArea` by `execute_code` with a forest recipe asset, confirm the composite SDF changed inside the outline and not outside, move a knot, confirm the re-bake. Verify seams (`Stitch Punk/Terrain/Verify Seams`). | |

## 9. Phase WF-2: forest (the area fills with trees)

Run WF-S1 first. Wave A, all **[parallel-safe]**:

| ✓ | Task | Files | Read | Behaviour | Fixture must fail if |
|---|---|---|---|---|---|
| [ ] | WF-2-1 | `Data/Terrain/TerrainForestRecipe.cs`, `Data/Terrain/TerrainCardAtlas.cs` | 6.4 | Data only. | gate only |
| [ ] | WF-2-2 | `Data/Terrain/TerrainScatterSet.cs`, `Editor/Terrain/TerrainScatterGenerator.cs` | 6.4; both files whole | Add the tier fields to `TerrainScatterRuleData`; `RulesFrom` is unchanged; `GenerateWithSampler` skips `Dressing` rules (they never become prefabs). | `TerrainScattererTests`: a Dressing rule instantiates a prefab |
| [ ] | WF-2-3 | `Features/TerrainCardBatchMeshBuilder.cs`, `Tests/Terrain/TerrainCardBatchMeshBuilderTests.cs` | 6.4 | Four vertices per card, upright, facing the yaw; splits are the caller's job (throw over `MaxCardsPerMesh`). | a card's top edge is not directly above its base (x and z equal); yaw 90 does not rotate the quad's width axis; uv rect is not applied |
| [ ] | WF-2-4 | `Features/TerrainAreaRestrictedSampler.cs`, `Tests/Terrain/TerrainAreaRestrictedSamplerTests.cs` | 6.3; `TerrainScatterPlanner.cs` lines 39 to 46 | `TryGetBiome` false outside the area (signed distance < 0) and inside a clearing (noise below threshold); otherwise forwards. | a point outside the outline is accepted; threshold 0 still makes clearings |
| [ ] | WF-2-5 | `Data/Terrain/TerrainPlatformProfile.cs`, `Editor/Terrain/TerrainBudgetCheck.cs` | 6.4; `CreatePreset`; how `TerrainBudgetCheck` reports decals plus props | Field, three preset values, one budget line "Interactable props n / cap". | gate only |

Wave B:

| ✓ | Task | Files | Behaviour | Fixture must fail if |
|---|---|---|---|---|
| [ ] | WF-2-6 **[parallel-safe]** | `Features/TerrainAreaFiller.cs`, `Tests/Terrain/TerrainAreaFillerTests.cs` | Per forest entry: `Plan` through the restricted sampler with the tile rect, obstacles = locked markers; `SplitTiers`; instantiate Interactable prefabs under the tile's `Scatter` child with `TerrainScatterMarker.sourceFeatureId` (clear this feature's unlocked ones first, inside `SuppressLocking`); build card meshes as sub-assets `Cards_<n>` under `Cards` (copy the sub-asset handling in `TerrainRoadRibbonBaker.BakeTileRibbons`, including re-naming after `CopySerialized`). Tests cover `SplitTiers` only. | interior placements stay interactable; the cap is exceeded; the nearest-to-edge trees are the ones demoted |
| [ ] | WF-2-7 **[orchestrator]** | `Painted/Card` shader (`shader-edit` skill): alpha clip, two-sided, two-sided ShadowCaster, `PaintedLighting_float`, vertex-colour tint. Placeholder tree atlas (3 sprites) and `TerrainCardAtlas`, `TerrainScatterSet` and `TerrainForestRecipe` assets in `Assets/ScriptableObjects/Terrain/Features/`. Wire `FillTile` into `TerrainFeatureBaker.BakeTile`. | |
| [ ] | WF-2-8 **[orchestrator]** Editor pass | Fixtures of this phase; bake the WF-1-8 forest; Game-view captures at mix 0 and mix 1; `Report Data Size` before and after; record trees per tile, card vertices, baked size in the log. | |

## 10. Phases WF-3 to WF-10

Each row is one worker task unless marked; every wave ends with the gate, lint, the named fixtures and a commit. Surfaces are in section 6.

**WF-3 Cut and respawn** (can run beside WF-2; no shared files)

| ✓ | Task | Files | Notes and fixture |
|---|---|---|---|
| [ ] | WF-3-1 **[parallel-safe]** | `Components/Terrain/HarvestedNodeComponents.cs`, `Authoring/Terrain/HarvestableNodeAuthoring.cs` | 6.5; `dots-authoring-baker` rules: nested class named `Baker`; `HarvestedNodeStump` baked disabled; key hash copied from `TerrainSpawnMarkerAuthoring`. |
| [ ] | WF-3-2 **[parallel-safe]** | `Utils/Terrain/HarvestedNodeMath.cs`, `Tests/Terrain/HarvestedNodeMathTests.cs` | Fails if: due one second early; `respawnAfterGameDays` 0 ever respawns. Take seconds-per-game-day from where `TerrainSpawnSystem` converts `respawnAfterGameDays`. |
| [ ] | WF-3-3 **[parallel-safe]** | `Systems/TerrainSystemGroup/HarvestedNodeRecordSystem.cs` | Singleton created in a non-Burst `OnCreate` (the BC1028 lesson from `WorldTimeScaleSystem`). |
| [ ] | WF-3-4 **[parallel-safe]** | `Systems/TerrainSystemGroup/HarvestedNodeRestoreSystem.cs`, `Tests/PlayMode/Terrain/HarvestedNodeTests.cs` | PlayMode, manual `World`: empty a node, update, it is a stump with one ledger entry; destroy and recreate the node entity (a tile reload), update, still a stump; advance `WorldClock.totalWorldSeconds` past due, update, `remaining == fullAmount` and the entry is gone. Fails if either system is disabled. |
| [ ] | WF-3-5 **[orchestrator]** | `MinionGatherSystem.cs` around line 246, `Tree.prefab` | If the emptied branch enables `Despawn` on the node, skip it for nodes with `HarvestableNodeKey`. Grep `remaining -=` for any other harvester. Add `HarvestableNodeAuthoring` to `Tree.prefab`. Stump look: `ImageIndexOverride` to a stump frame if the tree renders through the image-index path, else `PostTransformMatrix` scale Y 0.15; record which in the log. Run `MinionGatherSystemTests`. |
| [ ] | WF-3-6 **[orchestrator]** | save hook | Save and load the ledger buffer where `WorldClockSnapshot` is written and read. |

**WF-4 Scene-view authoring** (after WF-2; the first thing the owner touches)

| ✓ | Task | Files | Notes and fixture |
|---|---|---|---|
| [ ] | WF-4-1 **[parallel-safe]** | `Features/TerrainFeatureEditing.cs`, `Tests/Terrain/TerrainFeatureEditingTests.cs` | Fails if: a figure-eight is not reported self-intersecting; `ContainsPoint` is wrong for a concave L. |
| [ ] | WF-4-2 **[parallel-safe]** | `Features/TerrainFeatureTool.cs` | Read `TerrainRoadTool.cs` whole (use exactly its event handling, control-id handling and drawing calls). Ground point from the tile height (`TerrainHeightSampler`), not a physics ray. `[` `]` not used; Enter closes, Esc cancels, Backspace removes the last point, Delete removes the selected knot. Refuses a self-intersecting outline with a HUD line. |
| [ ] | WF-4-3 **[parallel-safe]** | `Features/TerrainFeaturePreview.cs` | Fill plus placement dots from an un-baked `Plan` over the outline's bounds, capped at 2,000 dots, recomputed at most every 100 ms while dragging. |
| [ ] | WF-4-4 **[parallel-safe]** | `Window/TerrainFeaturesPanel.cs` | Brief names `EditorStyleGuide.md` and rules R01 to R22; uses `TerrainUi` kit rows and existing USS classes only; new classes are listed in the report for the orchestrator to add to `TerrainEditor.uss` (peer has uncommitted edits there). |
| [ ] | WF-4-5 **[orchestrator]** | `TerrainEditorWindow.cs` (PanelCount 10, label "Features" after "Paint", icon, index map, switch case), `TerrainEditor.uss` | Coordinate with the peer session first. Before/after `TerrainEditorCapture.CaptureTabs`; read the PNGs; fix until the tab matches the guide. |
| [ ] | ⏸ **WF-4 owner checkpoint** | | Owner: open `TerrainWorld.unity`, `Stitch Punk > Terrain > Terrain Editor`, Features tab, click the forest recipe, click five points on the ground, press Enter; drag a knot; enter Play and send a minion to cut an edge tree. Report feel and look. |

**WF-5 Lines and fields** (run WF-S2 first)

| ✓ | Task | Files | Fixture must fail if |
|---|---|---|---|
| [ ] | WF-5-1 **[parallel-safe]** | `Data/Terrain/TerrainLineRecipe.cs`, `MonoBehaviours/Terrain/TerrainLine.cs` | gate only |
| [ ] | WF-5-2 **[parallel-safe]** | `Features/TerrainLinePlanner.cs`, `Tests/Terrain/TerrainLinePlannerTests.cs` | two adjacent tile rects both own, or neither owns, a point on the border; the same seed gives different points for different rects |
| [ ] | WF-5-3 **[parallel-safe]** | `Data/Terrain/TerrainFieldRecipe.cs` | gate only |
| [ ] | WF-5-4 **[parallel-safe]** | `Features/TerrainFieldMeshBuilder.cs`, `Tests/Terrain/TerrainFieldMeshBuilderTests.cs` | a vertex lies outside the tile rect; vertex y is not `groundHeightAt + liftMetres`; rotating `rowDirectionDegrees` by 90 does not swap the uv0 axes |
| [ ] | WF-5-5 **[parallel-safe]** (wave B) | `Features/TerrainFieldBaker.cs`, `Features/TerrainLineBaker.cs` | sub-asset pattern from `TerrainRoadRibbonBaker`; lines produce cards or prefabs by tier through `TerrainCardBatchMeshBuilder` |
| [ ] | WF-5-6 **[orchestrator]** | `Painted/Field` shader (row stripes from uv0.y, edge fade from uv2.x, `PaintedLighting_float`, depth offset like `Painted/Ribbon`), assets, baker wiring, `BeginDrawOutline` for lines (open polyline mode), far-view handling per WF-S2, captures at both zooms | |

**WF-6 Farmland stamp**

| ✓ | Task | Files | Fixture must fail if |
|---|---|---|---|
| [ ] | WF-6-1 **[parallel-safe]** | `Data/Terrain/TerrainStampRecipe.cs` + `TerrainFarmlandRecipe.cs`, `MonoBehaviours/Terrain/TerrainStamp.cs` (three small files, allowed) | gate only |
| [ ] | WF-6-2 **[parallel-safe]** | `Features/TerrainParcelSubdivider.cs`, `Tests/Terrain/TerrainParcelSubdividerTests.cs` | a parcel's area is outside the range (except slivers under 20 percent of the minimum, which are dropped); the summed parcel area exceeds the outline's; a parcel crosses a cut line; the same seed gives a different result |
| [ ] | WF-6-3 **[parallel-safe]** | `Features/TerrainStampExecutor.cs`, `Tests/Terrain/TerrainStampExecutorTests.cs` | (preview scene) Regenerate destroys a locked child; Regenerate keeps an unlocked child; Detach leaves a marker behind |
| [ ] | WF-6-4 **[parallel-safe]** (wave B) | `Features/TerrainFarmlandStampGenerator.cs` | Cut lines from every `TerrainRoad`, `TerrainRiver`, `TerrainRail` overlapping the bounds; one child `TerrainArea` (field recipe, crop by seed, fallow chance) and one closed `TerrainLine` (border) per parcel; farmhouse on the parcel corner nearest a road with a `TerrainRoad` track (track profile) to that road. |
| [ ] | WF-6-5 **[orchestrator]** | WF-S4 sheet; panel hook (drag box with a stamp recipe); captures | |
| [ ] | ⏸ **WF-6 owner checkpoint** | | Owner picks the parcel look from `Tasks/Claude/ParcelSheet.png`, drags a farmland box, moves a hedge, presses Regenerate. |

**WF-7 Village stamp**

| ✓ | Task | Files | Fixture must fail if |
|---|---|---|---|
| [ ] | WF-7-1 **[parallel-safe]** | `Data/Terrain/TerrainVillageRecipe.cs`, `MonoBehaviours/Terrain/TerrainLocationMarker.cs` | gate only |
| [ ] | WF-7-2 **[parallel-safe]** | `Features/TerrainVillageLayout.cs`, `Tests/Terrain/TerrainVillageLayoutTests.cs` | two lots overlap; a lot overlaps a street; a lot centre is outside the outline; the main street does not start at the entry point |
| [ ] | WF-7-3 **[parallel-safe]** | `Features/TerrainPadRasterizer.cs`, `Tests/Terrain/TerrainPadRasterizerTests.cs` | a sample inside the pad is not at `padHeight`; the shared edge samples of two adjacent tiles differ; a sample beyond the blend distance has a non-zero delta |
| [ ] | WF-7-4 **[parallel-safe]** (wave B) | `Features/TerrainVillageStampGenerator.cs` | Entry point = nearest point on any `TerrainRoad` to the outline; streets become child `TerrainRoad`s; one prefab per lot under the owning tile's `Buildings` child with `TerrainGeneratedMarker` and `NavFootprintAuthoring` (add if the prefab lacks one); pads into `areaHeightDelta` for every overlapped open tile then mesh rebuild; `TerrainSpawnMarkerAuthoring` per `spawns` row; one `TerrainLocationMarker` named from `names` by seed. |
| [ ] | WF-7-5 **[orchestrator]** | Placeholder building prefabs (boxes with roofs are fine), recipe asset, nav re-bake, seam verify, play check that units walk the streets and buildings block; captures. | |
| [ ] | ⏸ **WF-7 owner checkpoint** | | Owner drags a village box beside a road, drops a prefab in as a shop, moves a people marker, regenerates, confirms both survive. |

**WF-8 Rail track** (parallel with WF-5 to WF-7 in its own worktree; touches the road pipeline, not areas)

| ✓ | Task | Files | Fixture must fail if |
|---|---|---|---|
| [ ] | WF-8-1 **[parallel-safe]** | `Data/Terrain/TerrainRailProfile.cs`, `MonoBehaviours/Terrain/TerrainRail.cs` (+ `TerrainStation.cs`) | gate only |
| [ ] | WF-8-2 **[parallel-safe]** | `Features/TerrainRailConstraints.cs`, `Tests/Terrain/TerrainRailConstraintsTests.cs` | a 5 percent grade passes a 2.5 limit; a 40 m radius passes an 80 m limit; a straight line reports TooTight |
| [ ] | WF-8-3 **[parallel-safe]** | `Editor/Terrain/Nav/TerrainNavCellRules.cs`, `Tests/Terrain/TerrainNavCellRulesTests.cs` (+ the enum lines in `TileNavCost.cs` and `TerrainCrossing.cs`) | rail on dry ground is not `Rail`; rail under deep water is not `Deep`; a level crossing does not give `Road` |
| [ ] | WF-8-4 **[parallel-safe]** | `Features/TerrainRailViaductPlanner.cs`, `Tests/Terrain/TerrainRailViaductPlannerTests.cs` | a 6 m drop with a 4 m threshold gives no span; piers are spaced wider than the spacing |
| [ ] | WF-8-5 **[parallel-safe]** | `Authoring/Terrain/TerrainRailGraphAuthoring.cs`, `Tests/Terrain/` one route test | copy `TerrainRoadGraphAuthoring`; fails if two stations on one line have no route |
| [ ] | WF-8-6 **[orchestrator]** | `TerrainRoadBaker` (rails join the stamp list through a `StampFromRail`; rail texels also go to `RailMask.png`), `TerrainNavBaker` (reads `railMask`, sets `onRail`, handles `LevelCrossing`), `CaravanDriveSystem` (treat `TerrainSurface.Rail` the way it treats `Deep`; read how it does that first), rail ribbon style and material, auto-offer of a `LevelCrossing` where a rail stamp meets a road stamp, viaduct deck and piers, red constraint segments in `TerrainFeatureTool`. PlayMode: caravan stops at track, crosses at the crossing. | |
| [ ] | ⏸ **WF-8 owner checkpoint** | | Owner draws a rail line, sees red on a steep segment, drives the caravan to the track and over the crossing. |

**WF-9 Far forest** (run WF-S3 first)

| ✓ | Task | Files | Fixture must fail if |
|---|---|---|---|
| [ ] | WF-9-1 **[parallel-safe]** | `Data/Terrain/TerrainTileCanopyData.cs`, edit `Features/TerrainAreaFiller.cs` to write it (both tiers' trees) | gate only |
| [ ] | WF-9-2 **[parallel-safe]** | `Strategic/TerrainStrategicCanopyBuilder.cs`, `Tests/Terrain/TerrainStrategicCanopyBuilderTests.cs` | two trees in one cell give two cards; a card is larger than the maximum |
| [ ] | WF-9-3 **[orchestrator]** | `TerrainStrategicBaker.BakeAll` gathers canopy data per super-chunk, builds card meshes as sub-assets of the strategic chunk data, fades them with `_StrategicLayerFade` like the ground stand-in. Pixel-diff check from Terrain.md ("Far view" entry) with forest present; budget report. | |

**WF-10 World sketch**

| ✓ | Task | Files | Fixture must fail if |
|---|---|---|---|
| [ ] | WF-10-1 **[parallel-safe]** | `Editor/Terrain/TerrainWorldLandUsePlanner.cs`, `Tests/Terrain/TerrainWorldLandUsePlannerTests.cs` | the same recipe gives a different plan; a village sits on ground steeper than 6 degrees; a forest outline overlaps a village circle; a returned rail line fails `TerrainRailConstraints.Check` |
| [ ] | WF-10-2 **[orchestrator]** | `TerrainWorldRecipeBuilder.Build` (one call, one field), `TerrainWorldRecipePreview.Render` (draw forests, farmland, villages, rail), `TerrainRandomWorldBuilder.PopulateRandomWorld` (create `TerrainArea`, `TerrainStamp`, `TerrainRail`, `TerrainStation` objects, then `RebakeAllFeatures(true)`). These are the peer session's files: agree first. Existing `TerrainWorldRecipeBuilderTests` must stay green. | |
| [ ] | ⏸ **WF-10 owner checkpoint** | | Owner: Generate tab, pick a seed, create an 11 x 11 world, zoom out with M, compare with the reference picture; grab any forest, field or village and move it. |

## 11. Shared files and the other terrain session

Shared edits, all marked **[orchestrator]** above: `TerrainCompositor`, `TerrainTileSampler`, `TerrainScatterSet`, `TerrainScatterGenerator`, `TerrainTileData`, `TerrainPlatformProfile`, `TerrainBudgetCheck`, `TerrainNavCellRules`, `TerrainNavBaker`, `TerrainRoadBaker`, `CaravanDriveSystem`, `MinionGatherSystem`, `TerrainStrategicBaker`, `TerrainEditorWindow`, `TerrainEditor.uss`, and the three world-recipe files. Everything else is a new file. Tell the peer which of these a phase will touch before starting it.

## 12. Riding trains (own spec `TrainTravel_System.md`, own Q&A round; do not build here)

This spec guarantees: `RailGraphSingleton`, `TerrainStation`s with names, level crossings, track that streams. The train spec decides: timetable or on-demand departure; the train as one abstract traveller along the rail graph as a function of `WorldClock.totalWorldSeconds` (how caravans already move) that becomes real near the player; boarding moves `TerrainNavFocus` to the train so streaming and the nav window follow it, with the strategic view and time scale as in caravan mode; what happens to the caravan and minions on boarding; cost; events that stop a train; entry pattern and group placement.

## 13. Verification (becomes `Tasks/Verification/verify-world-features.md` when the spec retires)

Headless: every fixture named above; seam check and data-size report on a tile holding each feature; strategic pixel-diff with fields and forest present; `StitchPunk.Tests` and `StitchPunk.Tests.PlayMode` in full once per phase merge, discovered total not lower than before. Owner only: the four ⏸ checkpoints. On retiring: `git mv` to `Tasks/Verification/`, write the verify file, fix the `Plans/README.md` link, add a "World Features" section to `Memories/Code/Terrain.md` with every trap met.

## Open decisions (defaults apply until the owner answers; none blocks WF-1 to WF-4)

- [ ] **WF-Q1** Interactable tree cap per tile ← DECISION after WF-S1 (default PC 600, Deck 300, Switch 150).
- [ ] **WF-Q2** Tree respawn time ← DECISION (default 3 game days, stump shown meanwhile, set on the prefab's `HarvestableNodeAuthoring`).
- [ ] **WF-Q3** Dense forest interior on foot ← DECISION (default walkable with heavy nav cost; trunks are not walls).
- [ ] **WF-Q4** Do fields do anything ← DECISION (default scenery; the area keeps its crop index for a later system).
- [ ] **WF-Q5** Parcel look from the WF-S4 sheet ← DECISION at the WF-6 checkpoint.
- [ ] **WF-Q6** Does track ever block units on foot ← DECISION (default no).
- [ ] **WF-Q7** The train spec's questions (section 12).

## 14. Log (append one line per wave, newest last)

- 2026-11-11 spec v1 written from the owner Q&A; v2 the same day: pinned surfaces, task tables, worker protocol, folded into the merged `TerrainWorldRecipe`. Nothing built.
